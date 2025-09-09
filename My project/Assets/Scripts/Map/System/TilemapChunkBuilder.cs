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
    private HashSet<ChunkCoord> _processedChunks = new();

    private struct ChunkBuildJob
    {
        public ChunkCoord coord;
        public byte[] data;
    }

    private readonly Queue<ChunkBuildJob> _queue = new();

    [SerializeField] private int maxChunksPerFrame = 1;
    [SerializeField] private float buildBudgetMs = 3f;
    [SerializeField] private int tilesPerBatch = 100;

    private bool _processing;

    // Cache cho tile picking
    private readonly Dictionary<string, TileBase> _tileCache = new();

    private void Awake()
    {
        Instance = this;
        Debug.Log("TilemapChunkBuilder initialized");
    }

    private void Start()
    {
        // Đảm bảo references được set
        if (grid == null)
            grid = FindFirstObjectByType<Grid>();
        if (groundTilemap == null)
            groundTilemap = FindFirstObjectByType<Tilemap>();

        if (grid == null || groundTilemap == null)
        {
            Debug.LogError($"Missing references! Grid: {grid}, Tilemap: {groundTilemap}");
        }
        else
        {
            Debug.Log($"TilemapChunkBuilder ready with Grid: {grid.name}, Tilemap: {groundTilemap.name}");
        }
    }

    private void EnsureCache()
    {
        if (_biomeDefs != null) return;

        _biomeDefs = new Dictionary<BiomeType, BiomeDefinition>();
        var worldManager = NetworkWorldManager.Instance;

        if (worldManager == null)
        {
            Debug.LogWarning("NetworkWorldManager.Instance is null");
            return;
        }

        var set = worldManager.BiomeSet;
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
        // Check if already processed
        if (_processedChunks.Contains(coord))
        {
            Debug.Log($"Chunk {coord} already processed, skipping");
            return;
        }

        // Check for duplicates in queue
        foreach (var existingJob in _queue)
        {
            if (existingJob.coord.Equals(coord))
            {
                Debug.Log($"Chunk {coord} already queued for building");
                return;
            }
        }

        _queue.Enqueue(new ChunkBuildJob { coord = coord, data = biomeData });
        Debug.Log($"Enqueued chunk {coord} for building. Queue size: {_queue.Count}");

        if (!_processing)
        {
            StartCoroutine(ProcessQueue());
        }
    }

    private IEnumerator ProcessQueue()
    {
        _processing = true;
        Debug.Log($"Starting to process {_queue.Count} chunks in build queue");

        var sw = new System.Diagnostics.Stopwatch();

        while (_queue.Count > 0)
        {
            int built = 0;
            sw.Restart();

            while (_queue.Count > 0 &&
                   built < maxChunksPerFrame &&
                   sw.ElapsedMilliseconds < buildBudgetMs)
            {
                var job = _queue.Dequeue();
                Debug.Log($"Building chunk {job.coord}...");

                yield return StartCoroutine(BuildGroundFromBytesCoroutine(job.coord, job.data));
                built++;

                // Mark as processed
                _processedChunks.Add(job.coord);

                if (sw.ElapsedMilliseconds >= buildBudgetMs)
                {
                    break;
                }
            }

            yield return null;
        }

        _processing = false;
        Debug.Log("Finished processing build queue");
    }

    private IEnumerator BuildGroundFromBytesCoroutine(ChunkCoord coord, byte[] data)
    {
        EnsureCache();
        var meta = NetworkWorldManager.Instance?.Meta;
        if (meta == null)
        {
            yield break;
        }
        if (grid == null || groundTilemap == null)
        {
            yield break;
        }
        int cs = meta.chunkSize;
        int x0 = coord.x * cs;
        int y0 = coord.y * cs;
        if (data == null || data.Length != cs * cs)
        {
            yield return StartCoroutine(BuildGroundForChunkCoroutine(coord));
            yield break;
        }
        var stopwatch = new System.Diagnostics.Stopwatch();
        stopwatch.Start();
        var positions = new List<Vector3Int>();
        var tiles = new List<TileBase>();
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
                    var tile = GetCachedTileWithBlend(def, wx, wy, meta.seed);
                    if (tile != null)
                    {
                        positions.Add(new Vector3Int(wx, wy, 0));
                        tiles.Add(tile);
                    }
                }
            }
        }
        for (int i = 0; i < positions.Count; i += tilesPerBatch)
        {
            int batchSize = Mathf.Min(tilesPerBatch, positions.Count - i);
            var batchPositions = new Vector3Int[batchSize];
            var batchTiles = new TileBase[batchSize];
            for (int j = 0; j < batchSize; j++)
            {
                batchPositions[j] = positions[i + j];
                batchTiles[j] = tiles[i + j];
            }
            groundTilemap.SetTiles(batchPositions, batchTiles);

            if (stopwatch.ElapsedMilliseconds >= 2f)
            {
                stopwatch.Restart();
                yield return null;
            }
        }
        groundTilemap.RefreshAllTiles();
    }

    private TileBase GetCachedTileWithBlend(BiomeDefinition def, int worldX, int worldY, int seed)
    {
        var worldManager = NetworkWorldManager.Instance;
        if (worldManager == null) return def.PickGroundTileDeterministic(worldX, worldY, seed);

        var blendData = BiomeService.GetBlendDataForTile(worldX, worldY, seed,
            worldManager.Noise, worldManager.BiomeRegion, worldManager.BiomeSet);

        string cacheKey = $"{def.biomeType}_{worldX}_{worldY}_{seed}_{blendData.isBorder}";

        if (_tileCache.TryGetValue(cacheKey, out var cachedTile))
        {
            return cachedTile;
        }

        var tile = def.PickGroundTileWithBlend(worldX, worldY, seed, blendData);
        if (_tileCache.Count > 10000)
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

        if (grid == null || groundTilemap == null)
        {
            Debug.LogError("Grid or Tilemap is null!");
            yield break;
        }

        int cs = meta.chunkSize;
        int x0 = coord.x * cs;
        int y0 = coord.y * cs;

        var positions = new List<Vector3Int>();
        var tiles = new List<TileBase>();

        var stopwatch = new System.Diagnostics.Stopwatch();
        stopwatch.Start();
        int processedTiles = 0;

        Debug.Log($"Generating chunk {coord} from scratch at world pos ({x0}, {y0})");

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
                    var tile = GetCachedTileWithBlend(def, wx, wy, meta.seed);
                    if (tile != null)
                    {
                        positions.Add(new Vector3Int(wx, wy, 0));
                        tiles.Add(tile);
                    }
                }

                processedTiles++;

                if (processedTiles % 100 == 0 && stopwatch.ElapsedMilliseconds >= 2f)
                {
                    stopwatch.Restart();
                    yield return null;
                }
            }
        }

        Debug.Log($"Chunk {coord}: Generated {positions.Count} tiles from scratch");

        // Set tiles in batches
        for (int i = 0; i < positions.Count; i += tilesPerBatch)
        {
            int batchSize = Mathf.Min(tilesPerBatch, positions.Count - i);
            var batchPos = new Vector3Int[batchSize];
            var batchTiles = new TileBase[batchSize];

            for (int j = 0; j < batchSize; j++)
            {
                batchPos[j] = positions[i + j];
                batchTiles[j] = tiles[i + j];
            }

            groundTilemap.SetTiles(batchPos, batchTiles);

            if (i > 0 && i % (tilesPerBatch * 2) == 0)
            {
                yield return null;
            }
        }

        // Refresh tiles để RuleTile tự động chọn sprite dựa trên neighbors
        groundTilemap.RefreshAllTiles();
        Debug.Log($"Built chunk {coord} with {positions.Count} tiles");
        _processedChunks.Add(coord);
    }

    public void ClearChunk(ChunkCoord coord)
    {
        var meta = NetworkWorldManager.Instance?.Meta;
        if (meta == null || groundTilemap == null) return;

        int cs = meta.chunkSize;
        int x0 = coord.x * cs;
        int y0 = coord.y * cs;

        var clearPositions = new Vector3Int[cs * cs];
        var clearTiles = new TileBase[cs * cs]; // All nulls

        int idx = 0;
        for (int ly = 0; ly < cs; ly++)
        {
            for (int lx = 0; lx < cs; lx++)
            {
                clearPositions[idx++] = new Vector3Int(x0 + lx, y0 + ly, 0);
            }
        }

        groundTilemap.SetTiles(clearPositions, clearTiles);

        // Clear cache entries for this chunk
        var keysToRemove = new List<string>();
        foreach (var key in _tileCache.Keys)
        {
            if (key.Contains($"_{coord.x * cs}_") || key.Contains($"_{coord.y * cs}_"))
            {
                keysToRemove.Add(key);
            }
        }

        foreach (var key in keysToRemove)
        {
            _tileCache.Remove(key);
        }

        _processedChunks.Remove(coord);
        Debug.Log($"Cleared chunk {coord}");
    }

    public void FlushAll()
    {
        StopAllCoroutines();
        _processing = false;
        _queue.Clear();
        _tileCache.Clear();
        _processedChunks.Clear();
        _biomeDefs = null;
        Debug.Log("TilemapChunkBuilder flushed completely.");
    }

    private void OnDestroy()
    {
        FlushAll();
    }

    // Debug methods
    public void LogStatus()
    {
        Debug.Log($"TilemapChunkBuilder Status: " +
                 $"Queue: {_queue.Count}, " +
                 $"Processing: {_processing}, " +
                 $"Processed chunks: {_processedChunks.Count}, " +
                 $"Biome defs: {_biomeDefs?.Count ?? 0}, " +
                 $"Tile cache: {_tileCache.Count}");
    }
}