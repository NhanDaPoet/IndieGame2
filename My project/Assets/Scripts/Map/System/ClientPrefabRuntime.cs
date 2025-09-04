using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class ClientPrefabRuntime : MonoBehaviour
{
    public static ClientPrefabRuntime Instance { get; private set; }

    [Header("Performance Settings")]
    [Tooltip("Số lượng Activate tối đa mỗi frame")]
    [SerializeField] private int maxSpawnPerFrame = 3; // Giảm từ 5 xuống 3

    [Tooltip("Số lượng Deactivate tối đa mỗi frame khi unload")]
    [SerializeField] private int maxDespawnPerFrame = 5; // Giảm từ 10 xuống 5

    [Tooltip("Thời gian tối đa cho spawn mỗi frame (ms)")]
    [SerializeField] private float maxSpawnTimePerFrameMs = 1.5f; // Giảm từ 2f xuống 1.5f

    [Header("Pool Settings")]
    [Tooltip("Pool size cho mỗi prefab type")]
    [SerializeField] private int poolSizePerPrefab = 20; // Giảm từ 30 xuống 20

    [Tooltip("Pool size cho prefabs phổ biến (trees, rocks...)")]
    [SerializeField] private int commonPrefabPoolSize = 50; // Giảm từ 100 xuống 50

    [Header("Common Prefab Keys")]
    [Tooltip("Keys của các prefabs phổ biến cần pool size lớn")]
    [SerializeField] private string[] commonPrefabKeys = { "tree", "rock", "bush" };

    private PrefabRegistry _registry;
    private readonly Dictionary<ushort, Stack<GameObject>> _pool = new();
    private readonly Dictionary<ushort, Transform> _poolParents = new();
    private readonly Dictionary<ChunkCoord, List<GameObject>> _spawnedByChunk = new();
    private readonly HashSet<ChunkCoord> processedChunks = new();

    // Cache để tránh GetComponent calls
    private readonly Dictionary<GameObject, PrefabIdHolder> _idHolderCache = new();

    private struct SpawnJob
    {
        public ChunkCoord chunk;
        public ushort prefabId;
        public Vector3 worldPos;
        public byte variant;
    }

    private readonly Queue<SpawnJob> _spawnQueue = new();
    private bool _spawning;

    private struct DespawnJob
    {
        public GameObject go;
        public ushort prefabId;
    }

    private readonly Queue<DespawnJob> _despawnQueue = new();
    private bool _despawning;

    // Pool initialization state
    private bool _poolsInitialized = false;
    private Coroutine _initCoroutine;

    private void Awake()
    {
        Instance = this;
        _initCoroutine = StartCoroutine(InitializeRegistry());
    }

    private IEnumerator InitializeRegistry()
    {
        while (NetworkWorldManager.Instance == null || NetworkWorldManager.Instance.Meta == null)
        {
            yield return null;
        }

        string path = NetworkWorldManager.Instance.Meta.prefabRegistryResource;
        _registry = Resources.Load<PrefabRegistry>(path);
        if (_registry == null)
        {
            Debug.LogError($"Failed to load PrefabRegistry from path: {path}");
            yield break;
        }

        _registry.BuildCaches();
        yield return StartCoroutine(InitializeAllPoolsAsync());
        _poolsInitialized = true;
        Debug.Log("ClientPrefabRuntime initialization complete");
    }

    private IEnumerator InitializeAllPoolsAsync()
    {
        int poolsCreated = 0;
        const int maxPoolsPerFrame = 2; // Giảm từ 3 xuống 2
        const float maxTimePerFrameMs = 2f; // Giảm từ 3f xuống 2f
        var stopwatch = new System.Diagnostics.Stopwatch();

        foreach (var kv in _registry._idToPrefab)
        {
            stopwatch.Reset();
            stopwatch.Start();

            ushort id = kv.Key;
            var prefab = kv.Value;
            if (prefab == null) continue;

            int poolSize = commonPrefabKeys.Any(k => prefab.name.Contains(k)) ? commonPrefabPoolSize : poolSizePerPrefab;
            _pool[id] = new Stack<GameObject>(poolSize);
            var parent = new GameObject($"Pool_{prefab.name}").transform;
            parent.SetParent(transform, false);
            _poolParents[id] = parent;

            for (int i = 0; i < poolSize; i++)
            {
                var go = Instantiate(prefab, parent);
                go.SetActive(false);
                var idHolder = go.AddComponent<PrefabIdHolder>();
                idHolder.PrefabId = id;
                _pool[id].Push(go);
                _idHolderCache[go] = idHolder;

                if (++poolsCreated % maxPoolsPerFrame == 0 && stopwatch.ElapsedMilliseconds >= maxTimePerFrameMs)
                {
                    yield return null;
                    stopwatch.Reset();
                    stopwatch.Start();
                }
            }
        }
    }

    public void ApplySpawns(PrefabSpawn[] spawns)
    {
        foreach (var spawn in spawns)
        {
            _spawnQueue.Enqueue(new SpawnJob
            {
                chunk = NetworkWorldManager.Instance.WorldToChunk(spawn.cell),
                prefabId = spawn.prefabId,
                worldPos = spawn.cell,
                variant = spawn.variant
            });
        }

        if (!_spawning)
        {
            StartCoroutine(ProcessSpawnQueue());
        }
    }

    private IEnumerator ProcessSpawnQueue()
    {
        _spawning = true;
        var stopwatch = new System.Diagnostics.Stopwatch();

        while (_spawnQueue.Count > 0)
        {
            int spawnedThisFrame = 0;
            stopwatch.Reset();
            stopwatch.Start();

            while (_spawnQueue.Count > 0 &&
                   spawnedThisFrame < maxSpawnPerFrame &&
                   stopwatch.ElapsedMilliseconds < maxSpawnTimePerFrameMs)
            {
                var job = _spawnQueue.Dequeue();
                if (!_registry._idToPrefab.ContainsKey(job.prefabId)) continue;

                GameObject go = GetFromPool(job.prefabId);
                if (go == null) continue;

                go.transform.position = job.worldPos;
                go.SetActive(true);

                if (!_spawnedByChunk.TryGetValue(job.chunk, out var list))
                {
                    list = new List<GameObject>(50); // Pre-allocate với dung lượng ban đầu
                    _spawnedByChunk[job.chunk] = list;
                }
                list.Add(go);

                spawnedThisFrame++;
            }

            yield return null;
        }

        _spawning = false;
    }

    private GameObject GetFromPool(ushort prefabId)
    {
        if (_pool.TryGetValue(prefabId, out var stack) && stack.Count > 0)
        {
            return stack.Pop();
        }

        if (_registry._idToPrefab.TryGetValue(prefabId, out var prefab) && _poolParents.TryGetValue(prefabId, out var parent))
        {
            var go = Instantiate(prefab, parent);
            var idHolder = go.AddComponent<PrefabIdHolder>();
            idHolder.PrefabId = prefabId;
            _idHolderCache[go] = idHolder;
            return go;
        }

        return null;
    }

    private void ReturnToPool(ushort prefabId, GameObject go)
    {
        if (!_pool.ContainsKey(prefabId))
        {
            Destroy(go);
            return;
        }

        go.SetActive(false);
        go.transform.SetParent(_poolParents[prefabId], false);
        _pool[prefabId].Push(go);
    }

    public void DespawnChunk(ChunkCoord chunk)
    {
        if (!_spawnedByChunk.TryGetValue(chunk, out var objects)) return;

        foreach (var go in objects)
        {
            if (!go) continue;
            ushort pid = GetCachedPrefabId(go);
            if (pid > 0)
            {
                _despawnQueue.Enqueue(new DespawnJob { go = go, prefabId = pid });
            }
        }

        if (!_despawning)
        {
            StartCoroutine(ProcessDespawnQueue());
        }
    }

    private ushort GetCachedPrefabId(GameObject go)
    {
        if (_idHolderCache.TryGetValue(go, out var holder))
        {
            return holder.PrefabId;
        }
        return 0;
    }

    private IEnumerator ProcessDespawnQueue()
    {
        _despawning = true;
        const float maxDespawnTimePerFrameMs = 1.5f; // Giảm từ 2f xuống 1.5f
        var stopwatch = new System.Diagnostics.Stopwatch();

        while (_despawnQueue.Count > 0)
        {
            int despawnedThisFrame = 0;
            stopwatch.Reset();
            stopwatch.Start();

            while (_despawnQueue.Count > 0 &&
                   despawnedThisFrame < maxDespawnPerFrame &&
                   stopwatch.ElapsedMilliseconds < maxDespawnTimePerFrameMs)
            {
                var job = _despawnQueue.Dequeue();
                if (job.go && job.prefabId > 0)
                {
                    ReturnToPool(job.prefabId, job.go);
                }
                despawnedThisFrame++;
            }

            yield return null;
        }

        _despawning = false;
    }

    public void FlushAll()
    {
        StopAllCoroutines();
        _spawning = _despawning = false;
        _poolsInitialized = false;

        foreach (var kvp in _spawnedByChunk)
        {
            var objects = kvp.Value;
            if (objects == null) continue;

            foreach (var go in objects)
            {
                if (!go) continue;
                ushort pid = GetCachedPrefabId(go);
                if (pid > 0)
                {
                    ReturnToPool(pid, go);
                }
            }
            objects.Clear();
        }

        _spawnedByChunk.Clear();
        processedChunks.Clear();
        _spawnQueue.Clear();
        _despawnQueue.Clear();
        _idHolderCache.Clear();

        Debug.Log("ClientPrefabRuntime flushed completely.");

        if (this && gameObject.activeInHierarchy)
        {
            _initCoroutine = StartCoroutine(InitializeRegistry());
        }
    }

    private ChunkCoord CellToChunk(Vector3Int cell)
    {
        int cs = NetworkWorldManager.Instance.Meta.chunkSize;
        int cx = Mathf.FloorToInt((float)cell.x / cs);
        int cy = Mathf.FloorToInt((float)cell.y / cs);
        return new ChunkCoord(cx, cy);
    }

    public void LogPoolStatistics()
    {
        Debug.Log("=== Pool Statistics ===");
        foreach (var kvp in _pool)
        {
            ushort id = kvp.Key;
            int available = kvp.Value.Count;
            int inUse = 0;

            foreach (var chunkObjects in _spawnedByChunk.Values)
            {
                foreach (var go in chunkObjects)
                {
                    if (go && go.activeInHierarchy)
                    {
                        ushort goId = GetCachedPrefabId(go);
                        if (goId == id)
                        {
                            inUse++;
                        }
                    }
                }
            }

            var prefab = _registry.GetPrefab(id);
            string name = prefab ? prefab.name : $"Unknown_{id}";
            Debug.Log($"Prefab {name} (ID:{id}) - Available: {available}, In Use: {inUse}");
        }
    }

    [DisallowMultipleComponent]
    private class PrefabIdHolder : MonoBehaviour
    {
        public ushort PrefabId;
    }
}