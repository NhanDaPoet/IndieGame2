using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Client-side: Optimized prefab runtime với full object pooling
/// Không instantiate trong runtime, chỉ activate/deactivate objects từ pool
/// </summary>
public class ClientPrefabRuntime : MonoBehaviour
{
    public static ClientPrefabRuntime Instance { get; private set; }

    [Header("Performance Settings")]
    [Tooltip("Số lượng Activate tối đa mỗi frame")]
    [SerializeField] private int maxSpawnPerFrame = 5; // Giảm từ 10 xuống 5

    [Tooltip("Số lượng Deactivate tối đa mỗi frame khi unload")]
    [SerializeField] private int maxDespawnPerFrame = 10; // Giảm từ 20 xuống 10

    [Tooltip("Thời gian tối đa cho spawn mỗi frame (ms)")]
    [SerializeField] private float maxSpawnTimePerFrameMs = 2f;

    [Header("Pool Settings")]
    [Tooltip("Pool size cho mỗi prefab type")]
    [SerializeField] private int poolSizePerPrefab = 30; // Giảm từ 50 xuống 30

    [Tooltip("Pool size cho prefabs phổ biến (trees, rocks...)")]
    [SerializeField] private int commonPrefabPoolSize = 100; // Giảm từ 200 xuống 100

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
        const int maxPoolsPerFrame = 3; // Giảm từ 5 xuống 3
        const float maxTimePerFrameMs = 3f; // Giới hạn thời gian mỗi frame
        var stopwatch = new System.Diagnostics.Stopwatch();

        foreach (var kv in _registry._idToPrefab)
        {
            stopwatch.Reset();
            stopwatch.Start();

            ushort pid = kv.Key;
            GameObject prefab = kv.Value;
            if (prefab == null) continue;

            int poolSize = GetPoolSizeForPrefab(prefab.name);
            yield return StartCoroutine(CreatePoolAsync(pid, prefab, poolSize));

            poolsCreated++;

            // Kiểm tra thời gian và số lượng pools mỗi frame
            if (poolsCreated >= maxPoolsPerFrame || stopwatch.ElapsedMilliseconds >= maxTimePerFrameMs)
            {
                poolsCreated = 0;
                yield return null; // Cho phép frame khác chạy
            }
        }
    }

    private IEnumerator CreatePoolAsync(ushort prefabId, GameObject prefab, int poolSize)
    {
        var parent = GetOrCreatePoolParent(prefabId);
        var stack = new Stack<GameObject>();

        const int objectsPerFrame = 5; // Tạo tối đa 5 objects mỗi frame
        int created = 0;

        for (int i = 0; i < poolSize; i++)
        {
            var go = Instantiate(prefab, parent);
            go.SetActive(false);

            var idHolder = go.GetComponent<PrefabIdHolder>();
            if (!idHolder)
            {
                idHolder = go.AddComponent<PrefabIdHolder>();
            }
            idHolder.PrefabId = prefabId;

            // Cache để tránh GetComponent sau này
            _idHolderCache[go] = idHolder;
            stack.Push(go);

            created++;
            if (created >= objectsPerFrame)
            {
                created = 0;
                yield return null; // Spread across frames
            }
        }

        _pool[prefabId] = stack;
        Debug.Log($"Created pool for {prefab.name} (ID: {prefabId}) with {poolSize} objects");
    }

    private int GetPoolSizeForPrefab(string prefabName)
    {
        string lowerName = prefabName.ToLower();
        foreach (string key in commonPrefabKeys)
        {
            if (lowerName.Contains(key.ToLower()))
            {
                return commonPrefabPoolSize;
            }
        }
        return poolSizePerPrefab;
    }

    private Transform GetOrCreatePoolParent(ushort prefabId)
    {
        if (_poolParents.TryGetValue(prefabId, out var t)) return t;

        var holder = new GameObject($"Pool_{prefabId}").transform;
        holder.SetParent(transform, false);
        _poolParents[prefabId] = holder;
        return holder;
    }

    private GameObject RentFromPool(ushort prefabId)
    {
        if (!_pool.TryGetValue(prefabId, out var stack))
        {
            Debug.LogWarning($"Pool for prefabId {prefabId} not found");
            return null;
        }

        if (stack.Count > 0)
        {
            var go = stack.Pop();
            go.transform.SetParent(transform, false);
            return go;
        }

        // Nếu pool hết, tạo thêm object mới (emergency fallback)
        var prefab = _registry.GetPrefab(prefabId);
        if (prefab != null)
        {
            var go = Instantiate(prefab);
            var idHolder = go.GetComponent<PrefabIdHolder>();
            if (!idHolder)
            {
                idHolder = go.AddComponent<PrefabIdHolder>();
            }
            idHolder.PrefabId = prefabId;
            _idHolderCache[go] = idHolder;
            Debug.LogWarning($"Pool for {prefab.name} exhausted, creating emergency instance");
            return go;
        }

        return null;
    }

    private void ReturnToPool(ushort prefabId, GameObject go)
    {
        if (!go) return;

        go.SetActive(false);
        go.transform.SetParent(GetOrCreatePoolParent(prefabId), false);

        if (_pool.TryGetValue(prefabId, out var stack))
        {
            stack.Push(go);
        }
    }

    /// <summary>
    /// Apply spawns from server - với duplicate protection và early exit
    /// </summary>
    public void ApplySpawns(PrefabSpawn[] spawns)
    {
        if (!_poolsInitialized)
        {
            Debug.LogWarning("Pools not yet initialized, queuing spawns for later");
            StartCoroutine(WaitForPoolsAndApplySpawns(spawns));
            return;
        }

        if (spawns == null || spawns.Length == 0)
        {
            return;
        }

        var spawnsByChunk = new Dictionary<ChunkCoord, List<PrefabSpawn>>();

        foreach (var spawn in spawns)
        {
            var chunk = CellToChunk(spawn.cell);

            // Skip đã processed chunks
            if (processedChunks.Contains(chunk))
            {
                continue;
            }

            if (!spawnsByChunk.TryGetValue(chunk, out var list))
            {
                list = new List<PrefabSpawn>();
                spawnsByChunk[chunk] = list;
            }
            list.Add(spawn);
        }

        foreach (var kvp in spawnsByChunk)
        {
            var chunk = kvp.Key;
            var chunkSpawns = kvp.Value;

            processedChunks.Add(chunk);

            foreach (var spawn in chunkSpawns)
            {
                var worldPos = NetworkWorldManager.Instance.grid.CellToWorld(spawn.cell);
                _spawnQueue.Enqueue(new SpawnJob
                {
                    chunk = chunk,
                    prefabId = spawn.prefabId,
                    worldPos = worldPos,
                    variant = spawn.variant
                });
            }
        }

        if (_spawnQueue.Count > 0 && !_spawning)
        {
            StartCoroutine(ProcessSpawnQueue());
        }
    }

    private IEnumerator WaitForPoolsAndApplySpawns(PrefabSpawn[] spawns)
    {
        while (!_poolsInitialized)
        {
            yield return null;
        }
        ApplySpawns(spawns);
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
                var go = RentFromPool(job.prefabId);

                if (go == null)
                {
                    continue;
                }

                go.transform.position = job.worldPos;
                go.SetActive(true);

                if (!_spawnedByChunk.TryGetValue(job.chunk, out var list))
                {
                    list = new List<GameObject>();
                    _spawnedByChunk[job.chunk] = list;
                }
                list.Add(go);

                spawnedThisFrame++;
            }

            yield return null;
        }

        _spawning = false;
    }

    /// <summary>
    /// Despawn entire chunk với time budget
    /// </summary>
    public void DespawnChunk(ChunkCoord coord)
    {
        if (!_spawnedByChunk.TryGetValue(coord, out var objects) || objects.Count == 0)
        {
            return;
        }

        Debug.Log($"Despawning {objects.Count} objects from chunk {coord}");

        // Queue all objects for despawn
        foreach (var go in objects)
        {
            if (!go) continue;

            ushort pid = GetCachedPrefabId(go);
            _despawnQueue.Enqueue(new DespawnJob { go = go, prefabId = pid });
        }

        objects.Clear();
        _spawnedByChunk.Remove(coord);
        processedChunks.Remove(coord);

        if (!_despawning && _despawnQueue.Count > 0)
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

        // Fallback to GetComponent if not cached
        var idHolder = go.GetComponent<PrefabIdHolder>();
        if (idHolder != null)
        {
            _idHolderCache[go] = idHolder; // Cache for future use
            return idHolder.PrefabId;
        }

        return 0;
    }

    private IEnumerator ProcessDespawnQueue()
    {
        _despawning = true;
        const float maxDespawnTimePerFrameMs = 2f;
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

    /// <summary>
    /// Clean everything - for scene changes
    /// </summary>
    public void FlushAll()
    {
        StopAllCoroutines();
        _spawning = _despawning = false;
        _poolsInitialized = false;

        // Return all active objects to pools
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

        // Restart initialization if needed
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

    // ===== Pool Statistics (for debugging) =====
    public void LogPoolStatistics()
    {
        Debug.Log("=== Pool Statistics ===");
        foreach (var kvp in _pool)
        {
            ushort id = kvp.Key;
            int available = kvp.Value.Count;
            int inUse = 0;

            // Count in-use objects
            if (_spawnedByChunk.Count > 0)
            {
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
            }

            var prefab = _registry.GetPrefab(id);
            string name = prefab ? prefab.name : $"Unknown_{id}";
            Debug.Log($"Prefab {name} (ID:{id}) - Available: {available}, In Use: {inUse}");
        }
    }

    // ===== PrefabIdHolder Component =====
    [DisallowMultipleComponent]
    private class PrefabIdHolder : MonoBehaviour
    {
        public ushort PrefabId;
    }
}