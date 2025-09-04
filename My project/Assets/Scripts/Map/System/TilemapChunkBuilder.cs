using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using System.Collections;

public class TilemapChunkBuilder : MonoBehaviour
{
    public static TilemapChunkBuilder Instance { get; private set; }

    [SerializeField] private Grid grid;
    [SerializeField] private Tilemap groundTilemap;

    private Dictionary<BiomeType, BiomeDefinition> _biomeDefs;

    private struct ChunkBuildJob
    {
        public ChunkCoord coord;
        public byte[] data;
    }

    private readonly Queue<ChunkBuildJob> _queue = new();

    [SerializeField] private int maxChunksPerFrame = 1; // Giảm từ 2 xuống 1
    [SerializeField] private float buildBudgetMs = 2f; // Giảm từ 3ms xuống 2ms
    [SerializeField] private int tilesPerBatch = 50; // Giảm từ 100 xuống 50

    private bool _processing;

    // Cache cho tile picking để tránh recalculate
    private readonly Dictionary<string, TileBase> _tileCache = new();

    // Reusable arrays để tránh allocation
    private Vector3Int[] _reusablePositions;
    private TileBase[] _reusableTiles;

    private void Awake()
    {
        Instance = this;
        InitializeReusableArrays();
    }

    private void InitializeReusableArrays()
    {
        // Pre-allocate arrays với kích thước chunk tối đa
        int maxChunkSize = 64; // Giả sử chunk size tối đa là 64x64
        int maxTiles = maxChunkSize * maxChunkSize;
        _reusablePositions = new Vector3Int[maxTiles];
        _reusableTiles = new TileBase[maxTiles];
    }

    private void EnsureCache()
    {
        if (_biomeDefs != null) return;

        _biomeDefs = new Dictionary<BiomeType, BiomeDefinition>();
        var set = NetworkWorldManager.Instance?.BiomeSet;

        if (set == null)
        {
            Debug.LogWarning("BiomeSet is null in TilemapChunkBuilder");
            return;
        }

        set.BuildCache();
        foreach (var b in set.biomes)
        {
            if (b == null) continue;
            if (!_biomeDefs.ContainsKey(b.biomeType))
                _biomeDefs[b.biomeType] = b;
        }

        Debug.Log($"Cached {_biomeDefs.Count} biome definitions");
    }

    public void EnqueueBuild(ChunkCoord coord, byte[] biomeData)
    {
        EnsureCache();

        // Check for duplicates in queue
        foreach (var existingJob in _queue)
        {
            if (existingJob.coord.Equals(coord))
            {
                Debug.LogWarning($"Chunk {coord} already queued for building");
                return;
            }
        }

        _queue.Enqueue(new ChunkBuildJob { coord = coord, data = biomeData });

        if (!_processing)
        {
            StartCoroutine(ProcessQueue());
        }
    }

    private IEnumerator ProcessQueue()
    {
        _processing = true;
        var sw = new System.Diagnostics.Stopwatch();

        while (_queue.Count > 0)
        {
            int built = 0;
            sw.Reset();
            sw.Start();

            while (_queue.Count > 0 &&
                   built < maxChunksPerFrame &&
                   sw.ElapsedMilliseconds < buildBudgetMs)
            {
                var job = _queue.Dequeue();
                yield return StartCoroutine(BuildGroundFromBytesCoroutine(job.coord, job.data));
                built++;

                // Early exit nếu quá thời gian
                if (sw.ElapsedMilliseconds >= buildBudgetMs)
                {
                    break;
                }
            }

            yield return null;
        }

        _processing = false;
    }

    private IEnumerator BuildGroundFromBytesCoroutine(ChunkCoord coord, byte[] data)
    {
        EnsureCache();

        var meta = NetworkWorldManager.Instance?.Meta;
        if (meta == null)
        {
            Debug.LogError("NetworkWorldManager.Meta is null");
            yield break;
        }

        int cs = meta.chunkSize;
        int x0 = coord.x * cs;
        int y0 = coord.y * cs;

        if (data == null || data.Length != cs * cs)
        {
            Debug.LogWarning($"Invalid biome data for chunk {coord}, falling back to generation");
            yield return StartCoroutine(BuildGroundForChunkCoroutine(coord));
            yield break;
        }

        var stopwatch = new System.Diagnostics.Stopwatch();
        stopwatch.Start();

        // Prepare tiles using reusable arrays
        int tileCount = 0;

        for (int ly = 0; ly < cs; ly++)
        {
            for (int lx = 0; lx < cs; lx++)
            {
                int dataIndex = ly * cs + lx;
                int wx = x0 + lx;
                int wy = y0 + ly;

                var bt = (BiomeType)data[dataIndex];

                if (_biomeDefs.TryGetValue(bt, out var def) && def != null)
                {
                    var tile = GetCachedTile(def, wx, wy, meta.seed);
                    if (tile != null)
                    {
                        _reusablePositions[tileCount] = new Vector3Int(wx, wy, 0);
                        _reusableTiles[tileCount] = tile;
                        tileCount++;
                    }
                }
            }
        }

        // Set tiles in batches to spread across frames
        for (int i = 0; i < tileCount; i += tilesPerBatch)
        {
            int batchSize = Mathf.Min(tilesPerBatch, tileCount - i);

            // Create batch arrays
            var batchPositions = new Vector3Int[batchSize];
            var batchTiles = new TileBase[batchSize];

            System.Array.Copy(_reusablePositions, i, batchPositions, 0, batchSize);
            System.Array.Copy(_reusableTiles, i, batchTiles, 0, batchSize);

            groundTilemap.SetTiles(batchPositions, batchTiles);

            // Yield if we've spent too much time
            if (stopwatch.ElapsedMilliseconds >= 1f) // 1ms per batch max
            {
                stopwatch.Reset();
                stopwatch.Start();
                yield return null;
            }
        }

        Debug.Log($"Built chunk {coord} with {tileCount} tiles in {stopwatch.ElapsedMilliseconds}ms");
    }

    private TileBase GetCachedTile(BiomeDefinition def, int worldX, int worldY, int seed)
    {
        // Create cache key
        string cacheKey = $"{def.biomeType}_{worldX}_{worldY}_{seed}";

        if (_tileCache.TryGetValue(cacheKey, out var cachedTile))
        {
            return cachedTile;
        }

        // Generate tile và cache
        var tile = def.PickGroundTileDeterministic(worldX, worldY, seed);

        // Limit cache size để tránh memory leak
        if (_tileCache.Count > 10000) // Cache tối đa 10k tiles
        {
            _tileCache.Clear();
        }

        _tileCache[cacheKey] = tile;
        return tile;
    }

    public void BuildGroundForChunk(ChunkCoord coord)
    {
        StartCoroutine(BuildGroundForChunkCoroutine(coord));
    }

    private IEnumerator BuildGroundForChunkCoroutine(ChunkCoord coord)
    {
        EnsureCache();

        var meta = NetworkWorldManager.Instance?.Meta;
        var worldManager = NetworkWorldManager.Instance;

        if (meta == null || worldManager == null)
        {
            Debug.LogError("Required components are null");
            yield break;
        }

        int cs = meta.chunkSize;
        int x0 = coord.x * cs;
        int y0 = coord.y * cs;

        var positions = new List<Vector3Int>(cs * cs);
        var tiles = new List<TileBase>(cs * cs);

        var stopwatch = new System.Diagnostics.Stopwatch();
        stopwatch.Start();
        int processedTiles = 0;

        for (int lx = 0; lx < cs; lx++)
        {
            for (int ly = 0; ly < cs; ly++)
            {
                int wx = x0 + lx;
                int wy = y0 + ly;

                BiomeType bt = BiomeService.SampleBiome(wx, wy,
                    worldManager.Meta.seed,
                    worldManager.Noise,
                    worldManager.BiomeRegion,
                    worldManager.BiomeSet);

                if (_biomeDefs.TryGetValue(bt, out var def) && def != null)
                {
                    var tile = GetCachedTile(def, wx, wy, meta.seed);
                    if (tile != null)
                    {
                        positions.Add(new Vector3Int(wx, wy, 0));
                        tiles.Add(tile);
                    }
                }

                processedTiles++;

                // Yield every 100 tiles processed để tránh lag
                if (processedTiles % 100 == 0 && stopwatch.ElapsedMilliseconds >= 2f)
                {
                    stopwatch.Reset();
                    stopwatch.Start();
                    yield return null;
                }
            }
        }

        // Set all tiles at once sau khi tạo xong
        if (positions.Count > 0)
        {
            // Set tiles in batches
            for (int i = 0; i < positions.Count; i += tilesPerBatch)
            {
                int batchSize = Mathf.Min(tilesPerBatch, positions.Count - i);
                var batchPos = positions.GetRange(i, batchSize).ToArray();
                var batchTiles = tiles.GetRange(i, batchSize).ToArray();

                groundTilemap.SetTiles(batchPos, batchTiles);

                if (i > 0 && i % (tilesPerBatch * 2) == 0) // Yield mỗi 2 batches
                {
                    yield return null;
                }
            }
        }
    }

    public void ClearChunk(ChunkCoord coord)
    {
        var meta = NetworkWorldManager.Instance?.Meta;
        if (meta == null) return;

        int cs = meta.chunkSize;
        int x0 = coord.x * cs;
        int y0 = coord.y * cs;

        // Use reusable arrays for clearing
        int tileCount = cs * cs;
        for (int i = 0; i < tileCount; i++)
        {
            int lx = i % cs;
            int ly = i / cs;
            _reusablePositions[i] = new Vector3Int(x0 + lx, y0 + ly, 0);
            _reusableTiles[i] = null;
        }

        // Create properly sized arrays for SetTiles
        var clearPositions = new Vector3Int[tileCount];
        var clearTiles = new TileBase[tileCount];

        System.Array.Copy(_reusablePositions, clearPositions, tileCount);
        // clearTiles is already filled with nulls

        groundTilemap.SetTiles(clearPositions, clearTiles);

        // Clear related cache entries
        string cachePrefix = $"_{coord.x * cs}_";
        var keysToRemove = new List<string>();

        foreach (var key in _tileCache.Keys)
        {
            if (key.Contains(cachePrefix))
            {
                keysToRemove.Add(key);
            }
        }

        foreach (var key in keysToRemove)
        {
            _tileCache.Remove(key);
        }
    }

    // Cleanup method to call when changing scenes
    public void FlushAll()
    {
        StopAllCoroutines();
        _processing = false;
        _queue.Clear();
        _tileCache.Clear();
        Debug.Log("TilemapChunkBuilder flushed completely.");
    }

    private void OnDestroy()
    {
        FlushAll();
    }
}