using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Mirror;

public class ClientPrefabRuntime : MonoBehaviour
{
    public static ClientPrefabRuntime Instance { get; private set; }

    [Header("Performance Settings")]
    [SerializeField] private int maxSpawnPerFrame = 3;
    [SerializeField] private int maxDespawnPerFrame = 5;
    [SerializeField] private float maxSpawnTimePerFrameMs = 1.5f;

    [Header("Pool Settings")]
    [SerializeField] private int poolSizePerPrefab = 20;
    [SerializeField] private int commonPrefabPoolSize = 50;
    [SerializeField] private int maxPoolExpansion = 10;
    [SerializeField] private string[] commonPrefabKeys = { "tree", "rock", "bush" };

    [Header("Pool Management")]
    [SerializeField] private bool enablePoolExpansion = true;
    [SerializeField] private bool enablePoolShrinking = false;
    [SerializeField] private float poolShrinkCheckInterval = 30f;

    private PrefabRegistry _registry;

    // Enhanced pooling system
    private readonly Dictionary<ushort, ObjectPool> _pools = new();
    private readonly Dictionary<ushort, Transform> _poolParents = new();
    private readonly Dictionary<ChunkCoord, List<PooledObject>> _spawnedByChunk = new();
    private readonly HashSet<ChunkCoord> processedChunks = new();
    private readonly Dictionary<GameObject, PooledObject> _pooledObjectCache = new();

    // Pool statistics tracking
    private readonly Dictionary<ushort, PoolStatistics> _poolStats = new();

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
        public PooledObject pooledObject;
        public ushort prefabId;
    }

    private readonly Queue<DespawnJob> _despawnQueue = new();
    private bool _despawning;
    private bool _poolsInitialized = false;
    private Coroutine _initCoroutine;
    private Coroutine _poolMaintenanceCoroutine;

    // Enhanced pool class
    private class ObjectPool
    {
        private readonly Stack<PooledObject> _available = new();
        private readonly HashSet<PooledObject> _inUse = new();
        private readonly GameObject _prefab;
        public readonly Transform _parent;
        private readonly ushort _prefabId;
        private readonly int _initialSize;
        private readonly int _maxExpansion;
        private readonly bool _canExpand;

        public int TotalCreated => _available.Count + _inUse.Count;
        public int Available => _available.Count;
        public int InUse => _inUse.Count;

        public ObjectPool(GameObject prefab, Transform parent, ushort prefabId, int initialSize, int maxExpansion, bool canExpand)
        {
            _prefab = prefab;
            _parent = parent;
            _prefabId = prefabId;
            _initialSize = initialSize;
            _maxExpansion = maxExpansion;
            _canExpand = canExpand;

            // Pre-populate pool
            for (int i = 0; i < initialSize; i++)
            {
                CreateNewObject();
            }
        }

        private PooledObject CreateNewObject()
        {
            GameObject go = Instantiate(_prefab, _parent);
            go.SetActive(false);

            PooledObject pooledObj = go.GetComponent<PooledObject>();
            if (pooledObj == null)
            {
                pooledObj = go.AddComponent<PooledObject>();
            }
            pooledObj.Initialize(_prefabId, this);

            // Setup NetworkIdentity
            NetworkIdentity ni = go.GetComponent<NetworkIdentity>();
            if (ni == null)
            {
                ni = go.AddComponent<NetworkIdentity>();
            }

            // Setup ResourceNodeBase if present
            ResourceNodeBase node = go.GetComponent<ResourceNodeBase>();
            if (node != null && node.definition != null)
            {
                ValidateResourceNodeComponents(go, node);
            }

            _available.Push(pooledObj);
            return pooledObj;
        }

        private void ValidateResourceNodeComponents(GameObject go, ResourceNodeBase node)
        {
            SpriteRenderer sr = go.GetComponent<SpriteRenderer>();
            if (sr == null)
            {
                Debug.LogWarning($"Prefab {_prefab.name} (ID: {_prefabId}) lacks SpriteRenderer.");
            }

            ParticleSystem ps = go.GetComponent<ParticleSystem>();
            if (ps == null && node.definition.GetMaxStageTransitions() > 0)
            {
                Debug.LogWarning($"Prefab {_prefab.name} (ID: {_prefabId}) lacks ParticleSystem for stage transitions.");
            }
        }

        public PooledObject GetObject()
        {
            PooledObject obj;

            if (_available.Count > 0)
            {
                obj = _available.Pop();
            }
            else if (_canExpand && TotalCreated < _initialSize + _maxExpansion)
            {
                obj = CreateNewObject();
                _available.Pop(); // Remove from available since we're about to use it
            }
            else
            {
                Debug.LogWarning($"Pool for {_prefab.name} is exhausted and cannot expand further!");
                return null;
            }

            _inUse.Add(obj);
            obj.SetInUse(true);
            return obj;
        }

        public void ReturnObject(PooledObject obj)
        {
            if (_inUse.Remove(obj))
            {
                obj.SetInUse(false);
                obj.Reset();
                _available.Push(obj);
            }
        }

        public void ShrinkPool(int targetSize)
        {
            if (!_canExpand) return;

            int toRemove = Mathf.Max(0, _available.Count - targetSize);
            for (int i = 0; i < toRemove; i++)
            {
                if (_available.Count > 0)
                {
                    PooledObject obj = _available.Pop();
                    if (obj != null && obj.gameObject != null)
                    {
                        DestroyImmediate(obj.gameObject);
                    }
                }
            }
        }

        public PoolStatistics GetStatistics()
        {
            return new PoolStatistics
            {
                prefabId = _prefabId,
                totalCreated = TotalCreated,
                available = Available,
                inUse = InUse,
                initialSize = _initialSize,
                maxSize = _initialSize + _maxExpansion
            };
        }
    }

    // Enhanced pooled object class
    private class PooledObject : MonoBehaviour
    {
        public ushort PrefabId { get; private set; }
        public bool IsInUse { get; private set; }

        private ObjectPool _parentPool;
        private ResourceNodeBase _resourceNode;
        private NetworkIdentity _networkIdentity;
        private SpriteRenderer _spriteRenderer;

        public void Initialize(ushort prefabId, ObjectPool parentPool)
        {
            PrefabId = prefabId;
            _parentPool = parentPool;

            // Cache components
            _resourceNode = GetComponent<ResourceNodeBase>();
            _networkIdentity = GetComponent<NetworkIdentity>();
            _spriteRenderer = GetComponent<SpriteRenderer>();
        }

        public void SetInUse(bool inUse)
        {
            IsInUse = inUse;
            gameObject.SetActive(inUse);
        }

        public void Reset()
        {
            // Reset ResourceNode state
            if (_resourceNode != null && _resourceNode.definition != null)
            {
                _resourceNode.enabled = false;
                _resourceNode.ClearExistingParticleSystems();

                if (NetworkServer.active)
                {
                    _resourceNode.remaining = _resourceNode.definition.maxHealth;
                    _resourceNode.stageIndex = 0;
                    _resourceNode.occupied = false;
                    _resourceNode.occupierNetId = 0;
                }
            }

            // Reset position and parent
            transform.position = Vector3.zero;
            transform.SetParent(_parentPool._parent);
        }

        public void Activate(Vector3 position)
        {
            transform.position = position;
            gameObject.SetActive(true);

            // Setup ResourceNode
            if (_resourceNode != null && _resourceNode.definition != null)
            {
                _resourceNode.enabled = true;

                if (NetworkServer.active)
                {
                    _resourceNode.remaining = _resourceNode.definition.maxHealth;
                    _resourceNode.stageIndex = 0;
                    _resourceNode.occupied = false;
                    _resourceNode.occupierNetId = 0;
                }

                // Set initial sprite
                if (_spriteRenderer != null && _resourceNode.definition.depletionSprites != null &&
                    _resourceNode.definition.depletionSprites.Length > 0)
                {
                    _spriteRenderer.sprite = _resourceNode.definition.depletionSprites[0];
                }

                _resourceNode.InitializeParticleSystem();
            }

            // Handle networking
            if (_networkIdentity != null && NetworkServer.active)
            {
                NetworkServer.Spawn(gameObject);
            }
        }

        public void ReturnToPool()
        {
            if (_parentPool != null && IsInUse)
            {
                _parentPool.ReturnObject(this);
            }
        }
    }

    public struct PoolStatistics
    {
        public ushort prefabId;
        public int totalCreated;
        public int available;
        public int inUse;
        public int initialSize;
        public int maxSize;
    }

    private void Awake()
    {
        Instance = this;
        _initCoroutine = StartCoroutine(InitializeRegistry());
    }

    private IEnumerator InitializeRegistry()
    {
        while (NetworkWorldManager.Instance == null || NetworkWorldManager.Instance.Meta == null)
            yield return null;

        string path = NetworkWorldManager.Instance.Meta.prefabRegistryResource;
        _registry = Resources.Load<PrefabRegistry>(path);

        if (_registry == null)
        {
            Debug.LogError("Failed to load PrefabRegistry!");
            yield break;
        }

        _registry.BuildCaches();
        InitializePools();
        _poolsInitialized = true;

        // Start pool maintenance coroutine
        if (enablePoolShrinking)
        {
            _poolMaintenanceCoroutine = StartCoroutine(PoolMaintenanceRoutine());
        }
    }

    private void InitializePools()
    {
        foreach (var entry in _registry._idToPrefab)
        {
            ushort id = entry.Key;
            GameObject prefab = entry.Value;
            if (prefab == null) continue;

            CreatePoolForPrefab(id, prefab);
        }
    }

    private void CreatePoolForPrefab(ushort prefabId, GameObject prefab)
    {
        // Determine pool size
        bool isCommon = commonPrefabKeys.Any(k => prefab.name.ToLower().Contains(k));
        int poolSize = isCommon ? commonPrefabPoolSize : poolSizePerPrefab;

        // Create pool parent
        Transform parent = new GameObject($"Pool_{prefab.name}_{prefabId}").transform;
        parent.SetParent(transform);
        _poolParents[prefabId] = parent;

        // Create object pool
        ObjectPool pool = new ObjectPool(
            prefab,
            parent,
            prefabId,
            poolSize,
            maxPoolExpansion,
            enablePoolExpansion
        );

        _pools[prefabId] = pool;
        _poolStats[prefabId] = pool.GetStatistics();
    }

    public void ApplySpawns(PrefabSpawn[] spawns)
    {
        if (!_poolsInitialized) return;

        foreach (var spawn in spawns)
        {
            _spawnQueue.Enqueue(new SpawnJob
            {
                chunk = CellToChunk(spawn.cell),
                prefabId = spawn.prefabId,
                worldPos = NetworkWorldManager.Instance.grid.CellToWorld(spawn.cell),
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
            stopwatch.Restart();

            while (_spawnQueue.Count > 0 &&
                   spawnedThisFrame < maxSpawnPerFrame &&
                   stopwatch.ElapsedMilliseconds < maxSpawnTimePerFrameMs)
            {
                var job = _spawnQueue.Dequeue();
                PooledObject pooledObj = GetFromPool(job.prefabId);

                if (pooledObj == null) continue;

                pooledObj.Activate(job.worldPos);

                // Track spawned objects by chunk
                if (!_spawnedByChunk.TryGetValue(job.chunk, out var list))
                {
                    list = new List<PooledObject>();
                    _spawnedByChunk[job.chunk] = list;
                }
                list.Add(pooledObj);

                // Cache for quick lookup
                _pooledObjectCache[pooledObj.gameObject] = pooledObj;

                spawnedThisFrame++;
            }

            yield return null;
        }

        _spawning = false;
    }

    private PooledObject GetFromPool(ushort prefabId)
    {
        if (!_pools.TryGetValue(prefabId, out ObjectPool pool))
        {
            // Create pool on demand if it doesn't exist
            GameObject prefab = _registry.GetPrefab(prefabId);
            if (prefab == null)
            {
                Debug.LogError($"No prefab found for ID: {prefabId}");
                return null;
            }

            CreatePoolForPrefab(prefabId, prefab);
            pool = _pools[prefabId];
        }

        return pool.GetObject();
    }

    public void DespawnChunk(ChunkCoord chunk)
    {
        if (!_spawnedByChunk.TryGetValue(chunk, out var objects))
        {
            return;
        }

        foreach (var pooledObj in objects)
        {
            if (pooledObj == null || !pooledObj.IsInUse) continue;

            _despawnQueue.Enqueue(new DespawnJob
            {
                pooledObject = pooledObj,
                prefabId = pooledObj.PrefabId
            });

            // Remove from cache
            if (_pooledObjectCache.ContainsKey(pooledObj.gameObject))
            {
                _pooledObjectCache.Remove(pooledObj.gameObject);
            }
        }

        objects.Clear();
        _spawnedByChunk.Remove(chunk);

        if (!_despawning)
        {
            StartCoroutine(ProcessDespawnQueue());
        }
    }

    private IEnumerator ProcessDespawnQueue()
    {
        _despawning = true;
        var stopwatch = new System.Diagnostics.Stopwatch();

        while (_despawnQueue.Count > 0)
        {
            int despawnedThisFrame = 0;
            stopwatch.Restart();

            while (_despawnQueue.Count > 0 &&
                   despawnedThisFrame < maxDespawnPerFrame &&
                   stopwatch.ElapsedMilliseconds < maxSpawnTimePerFrameMs)
            {
                var job = _despawnQueue.Dequeue();

                if (job.pooledObject != null && job.pooledObject.IsInUse)
                {
                    job.pooledObject.ReturnToPool();
                }

                despawnedThisFrame++;
            }

            yield return null;
        }

        _despawning = false;
    }

    private IEnumerator PoolMaintenanceRoutine()
    {
        var wait = new WaitForSeconds(poolShrinkCheckInterval);

        while (true)
        {
            yield return wait;

            if (!_poolsInitialized) continue;

            // Update statistics and potentially shrink pools
            foreach (var kvp in _pools)
            {
                ushort prefabId = kvp.Key;
                ObjectPool pool = kvp.Value;

                _poolStats[prefabId] = pool.GetStatistics();

                // Shrink pool if it has too many unused objects
                if (enablePoolShrinking && pool.Available > pool.InUse * 2)
                {
                    int targetSize = Mathf.Max(poolSizePerPrefab, pool.InUse + 5);
                    pool.ShrinkPool(targetSize);
                }
            }
        }
    }

    public void FlushAll()
    {
        StopAllCoroutines();
        _spawning = _despawning = false;
        _poolsInitialized = false;

        // Return all spawned objects to pools
        foreach (var kvp in _spawnedByChunk)
        {
            var objects = kvp.Value;
            if (objects == null) continue;

            foreach (var pooledObj in objects)
            {
                if (pooledObj != null && pooledObj.IsInUse)
                {
                    pooledObj.ReturnToPool();
                }
            }
            objects.Clear();
        }

        _spawnedByChunk.Clear();
        processedChunks.Clear();
        _spawnQueue.Clear();
        _despawnQueue.Clear();
        _pooledObjectCache.Clear();

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
        foreach (var kvp in _poolStats)
        {
            ushort prefabId = kvp.Key;
            var stats = kvp.Value;
            var prefab = _registry.GetPrefab(prefabId);
            string name = prefab ? prefab.name : $"Unknown_{prefabId}";

            Debug.Log($"Pool {name} (ID:{prefabId}): " +
                     $"Total: {stats.totalCreated}, " +
                     $"Available: {stats.available}, " +
                     $"In Use: {stats.inUse}, " +
                     $"Utilization: {(stats.inUse / (float)stats.totalCreated * 100):F1}%");
        }
        Debug.Log("=====================");
    }

    public Dictionary<ushort, PoolStatistics> GetPoolStatistics()
    {
        return new Dictionary<ushort, PoolStatistics>(_poolStats);
    }

    // Utility methods for external access
    public bool IsPoolInitialized => _poolsInitialized;
    public int GetSpawnQueueCount => _spawnQueue.Count;
    public int GetDespawnQueueCount => _despawnQueue.Count;
    public bool IsSpawning => _spawning;
    public bool IsDespawning => _despawning;
}