using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Client-side: nhận PrefabSpawn[] từ server và spawn theo Queue + Pool.
/// Quản lý instance theo Chunk để dọn khi Unload.
/// </summary>
public class ClientPrefabRuntime : MonoBehaviour
{
    public static ClientPrefabRuntime Instance { get; private set; }

    [Header("Limits")]
    [Tooltip("Số lượng Instantiate/Activate tối đa mỗi frame")]
    [SerializeField] private int maxSpawnPerFrame = 15;

    [Tooltip("Số lượng Destroy/Deactivate tối đa mỗi frame khi unload")]
    [SerializeField] private int maxDespawnPerFrame = 25;

    [Header("Pooling")]
    [Tooltip("Prewarm mặc định cho mỗi prefab phổ biến (0 = tắt)")]
    [SerializeField] private int defaultPrewarmCount = 0;

    private PrefabRegistry _registry;
    private readonly Dictionary<ushort, Stack<GameObject>> _pool = new(); 
    private readonly Dictionary<ushort, Transform> _poolParents = new(); 

    private readonly Dictionary<ChunkCoord, List<GameObject>> _spawnedByChunk = new();

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

    private void Awake()
    {
        Instance = this;
        if (_registry == null)
        {
            _registry = Resources.Load<PrefabRegistry>(NetworkWorldManager.Instance.Meta.prefabRegistryResource);
        }
        if (_registry == null)
        {
            Debug.LogError($"PrefabRegistry asset not found at: {NetworkWorldManager.Instance.Meta.prefabRegistryResource}");
            return;
        }
        _registry.BuildCaches();
    }

    private Transform GetOrCreatePoolParent(ushort prefabId)
    {
        if (_poolParents.TryGetValue(prefabId, out var t)) return t;
        var holder = new GameObject($"Pool_{prefabId}").transform;
        holder.SetParent(transform, false);
        _poolParents[prefabId] = holder;
        return holder;
    }

    private GameObject Rent(ushort prefabId)
    {
        if (!_pool.TryGetValue(prefabId, out var stack))
        {
            stack = new Stack<GameObject>();
            _pool[prefabId] = stack;
        }

        if (stack.Count > 0)
        {
            var go = stack.Pop();
            go.SetActive(true);
            return go;
        }
        var prefab = _registry.GetPrefab(prefabId);
        if (prefab == null)
        {
            Debug.LogError($"Prefab not found for prefabId: {prefabId}");
            return null;
        }

        var inst = Instantiate(prefab);
        return inst;
    }

    private void Return(ushort prefabId, GameObject go)
    {
        if (!go) return;
        go.SetActive(false);
        go.transform.SetParent(GetOrCreatePoolParent(prefabId), false);
        if (!_pool.TryGetValue(prefabId, out var stack))
        {
            stack = new Stack<GameObject>();
            _pool[prefabId] = stack;
        }
        stack.Push(go);
    }

    /// <summary>
    /// Tạo sẵn một số lượng object trong pool (khuyên dùng cho loại đông).
    /// </summary>
    public void Prewarm(ushort prefabId, int count)
    {
        if (count <= 0) return;
        var parent = GetOrCreatePoolParent(prefabId);
        if (!_pool.TryGetValue(prefabId, out var stack))
        {
            stack = new Stack<GameObject>();
            _pool[prefabId] = stack;
        }

        var prefab = _registry.GetPrefab(prefabId);
        if (!prefab) return;

        for (int i = 0; i < count; i++)
        {
            var go = Instantiate(prefab, parent);
            go.SetActive(false);
            stack.Push(go);
        }
    }

    /// <summary>
    /// API chính: nhận danh sách spawn cho 1 hoặc nhiều chunk, đẩy vào queue.
    /// </summary>
    public void ApplySpawns(PrefabSpawn[] spawns)
    {
        if (spawns == null || spawns.Length == 0) return;
        Debug.Log($"Spawning {spawns.Length} prefabs...");
        foreach (var s in spawns)
        {
            var chunk = CellToChunk(s.cell);
            var worldPos = NetworkWorldManager.Instance.grid.CellToWorld(s.cell);
            _spawnQueue.Enqueue(new SpawnJob
            {
                chunk = chunk,
                prefabId = s.prefabId,
                worldPos = worldPos,
                variant = s.variant
            });
        }
        if (!_spawning) StartCoroutine(ProcessSpawnQueue());
    }

    private IEnumerator ProcessSpawnQueue()
    {
        _spawning = true;
        if (defaultPrewarmCount > 0)
        {
            var prewarmChecked = new HashSet<ushort>();
            foreach (var job in _spawnQueue)
            {
                if (prewarmChecked.Contains(job.prefabId)) continue;
                Prewarm(job.prefabId, defaultPrewarmCount);
                prewarmChecked.Add(job.prefabId);
                if (prewarmChecked.Count > 64) break;
            }
        }
        while (_spawnQueue.Count > 0)
        {
            int spawnedThisFrame = 0;
            while (_spawnQueue.Count > 0 && spawnedThisFrame < maxSpawnPerFrame)
            {
                var job = _spawnQueue.Dequeue();
                var go = Rent(job.prefabId);
                if (!go) { continue; }
                go.transform.SetParent(transform, false);
                go.transform.position = job.worldPos;
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
    /// Dọn toàn bộ object của 1 chunk (đưa về pool), làm dần để tránh spike.
    /// </summary>
    public void DespawnChunk(ChunkCoord coord)
    {
        if (!_spawnedByChunk.TryGetValue(coord, out var gos) || gos == null || gos.Count == 0)
            return;

        foreach (var go in gos)
        {
            if (!go) continue;
            var idHolder = go.GetComponent<PrefabIdHolder>();
            ushort pid = (idHolder != null) ? idHolder.PrefabId : GuessPrefabIdFromName(go.name);
            _despawnQueue.Enqueue(new DespawnJob { go = go, prefabId = pid });
        }
        gos.Clear();
        _spawnedByChunk.Remove(coord);
        if (!_despawning) StartCoroutine(ProcessDespawnQueue());
    }

    private IEnumerator ProcessDespawnQueue()
    {
        _despawning = true;

        while (_despawnQueue.Count > 0)
        {
            int cnt = 0;
            while (_despawnQueue.Count > 0 && cnt < maxDespawnPerFrame)
            {
                var job = _despawnQueue.Dequeue();
                if (job.go)
                {
                    // Clear parent để dễ quản lý
                    Return(job.prefabId, job.go);
                }
                cnt++;
            }
            yield return null;
        }

        _despawning = false;
    }

    /// <summary>
    /// Dọn sạch mọi thứ (hiếm khi cần, ví dụ đổi scene).
    /// </summary>
    public void FlushAll()
    {
        StopAllCoroutines();
        _spawning = _despawning = false;

        foreach (var kv in _spawnedByChunk)
        {
            var list = kv.Value;
            if (list == null) continue;
            foreach (var go in list)
            {
                if (!go) continue;
                var idHolder = go.GetComponent<PrefabIdHolder>();
                ushort pid = (idHolder != null) ? idHolder.PrefabId : GuessPrefabIdFromName(go.name);
                Return(pid, go);
            }
            list.Clear();
        }
        _spawnedByChunk.Clear();
    }

    private ChunkCoord CellToChunk(Vector3Int cell)
    {
        int cs = NetworkWorldManager.Instance.Meta.chunkSize;
        int cx = Mathf.FloorToInt((float)cell.x / cs);
        int cy = Mathf.FloorToInt((float)cell.y / cs);
        return new ChunkCoord(cx, cy);
    }

    // ===== Hỗ trợ gắn PrefabId vào instance để Despawn nhanh & chính xác =====
    // Bạn có thể thêm script này vào prefab gốc, hoặc add-on khi Rent (nếu thiếu).
    [DisallowMultipleComponent]
    private class PrefabIdHolder : MonoBehaviour
    {
        public ushort PrefabId;
    }

    private ushort GuessPrefabIdFromName(string name)
    {
        // fallback an toàn nếu thiếu PrefabIdHolder (nên hạn chế)
        // có thể map theo prefix tên hoặc giữ 0
        return 0;
    }

    // Hook khi Rent: đảm bảo PrefabIdHolder tồn tại & đúng pid
    private GameObject RentWithId(ushort prefabId)
    {
        var go = Rent(prefabId);
        if (!go) return null;
        var idh = go.GetComponent<PrefabIdHolder>();
        if (!idh) idh = go.AddComponent<PrefabIdHolder>();
        idh.PrefabId = prefabId;
        return go;
    }
}
