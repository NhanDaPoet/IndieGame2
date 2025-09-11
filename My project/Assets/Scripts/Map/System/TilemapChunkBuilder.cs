using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;

public class TilemapChunkBuilder : MonoBehaviour
{
    public static TilemapChunkBuilder Instance { get; private set; }

    [SerializeField] private Grid grid;
    [SerializeField] private Tilemap tilemapPrefab;
    [SerializeField] private int invasionRadius = 1; 
    [SerializeField] private bool includeDiagonals = false; 

    private Dictionary<BiomeType, BiomeDefinition> _biomeDefs;
    private Dictionary<BiomeType, Tilemap> _activeTilemaps = new();
    private Dictionary<BiomeType, Queue<Tilemap>> _tilemapPool = new();

    private HashSet<ChunkCoord> _processedChunks = new();
    private readonly Dictionary<string, TileBase> _tileCache = new();

    private struct ChunkBuildJob
    {
        public ChunkCoord coord;
        public byte[] data;
    }
    private readonly Queue<ChunkBuildJob> _queue = new();
    private bool _processing;

    [SerializeField] private int maxChunksPerFrame = 1;
    [SerializeField] private float buildBudgetMs = 3f;
    [SerializeField] private int tilesPerBatch = 100;

    private void Awake()
    {
        Instance = this;
    }

    private void EnsureCache()
    {
        if (_biomeDefs != null) return;

        _biomeDefs = new Dictionary<BiomeType, BiomeDefinition>();
        var worldManager = NetworkWorldManager.Instance;
        if (worldManager == null) return;

        var set = worldManager.BiomeSet;
        if (set == null) return;

        set.BuildCache();
        foreach (var b in set.biomes)
        {
            if (b != null && !_biomeDefs.ContainsKey(b.biomeType))
                _biomeDefs[b.biomeType] = b;
        }
    }

    private Tilemap GetTilemapForBiome(BiomeDefinition def)
    {
        if (_activeTilemaps.TryGetValue(def.biomeType, out var tm))
            return tm;

        if (!_tilemapPool.TryGetValue(def.biomeType, out var queue))
        {
            queue = new Queue<Tilemap>();
            _tilemapPool[def.biomeType] = queue;
        }

        if (queue.Count > 0)
        {
            tm = queue.Dequeue();
            tm.gameObject.SetActive(true);
        }
        else
        {
            var go = Instantiate(tilemapPrefab.gameObject, grid.transform);
            go.name = $"Tilemap_{def.biomeType}";
            tm = go.GetComponent<Tilemap>();
            var renderer = tm.GetComponent<TilemapRenderer>();
            renderer.sortingOrder = def.sortingWeight;
        }

        _activeTilemaps[def.biomeType] = tm;
        return tm;
    }

    private void ReleaseTilemap(BiomeType biomeType)
    {
        if (_activeTilemaps.TryGetValue(biomeType, out var tm))
        {
            tm.ClearAllTiles();
            tm.gameObject.SetActive(false);
            _tilemapPool[biomeType].Enqueue(tm);
            _activeTilemaps.Remove(biomeType);
        }
    }

    // === API tương thích NetworkWorldManager ===
    public void EnqueueBuild(ChunkCoord coord, byte[] biomeData)
    {
        if (_processedChunks.Contains(coord)) return;
        foreach (var job in _queue)
            if (job.coord.Equals(coord)) return;

        _queue.Enqueue(new ChunkBuildJob { coord = coord, data = biomeData });
        if (!_processing) StartCoroutine(ProcessQueue());
    }

    private IEnumerator ProcessQueue()
    {
        _processing = true;
        var sw = new System.Diagnostics.Stopwatch();

        while (_queue.Count > 0)
        {
            int built = 0;
            sw.Restart();

            while (_queue.Count > 0 && built < maxChunksPerFrame && sw.ElapsedMilliseconds < buildBudgetMs)
            {
                var job = _queue.Dequeue();
                yield return StartCoroutine(BuildTerrainFromBytesCoroutine(job.coord, job.data));
                built++;
                _processedChunks.Add(job.coord);
            }

            yield return null;
        }

        _processing = false;
    }

    private IEnumerator BuildTerrainFromBytesCoroutine(ChunkCoord coord, byte[] data)
    {
        EnsureCache();
        var meta = NetworkWorldManager.Instance?.Meta;
        if (meta == null) yield break;

        int cs = meta.chunkSize;
        int x0 = coord.x * cs;
        int y0 = coord.y * cs;

        var biomeMap = new BiomeType[cs, cs];
        for (int ly = 0; ly < cs; ly++)
            for (int lx = 0; lx < cs; lx++)
                biomeMap[lx, ly] = (BiomeType)data[ly * cs + lx];

        yield return StartCoroutine(BuildTilesWithWeightSystemCoroutine(coord, biomeMap, cs, x0, y0, meta.seed));
    }

    private IEnumerator BuildTilesWithWeightSystemCoroutine(ChunkCoord coord,  BiomeType[,] biomeMap,  int size,  int startX, int startY,int seed)
    {
        var biomeTileData = new Dictionary<BiomeType, List<(Vector3Int pos, TileBase tile)>>();

        var stopwatch = new System.Diagnostics.Stopwatch();
        stopwatch.Start();

        for (int ly = 0; ly < size; ly++)
        {
            for (int lx = 0; lx < size; lx++)
            {
                int wx = startX + lx;
                int wy = startY + ly;
                var biomeType = biomeMap[lx, ly];

                if (_biomeDefs.TryGetValue(biomeType, out var def) && def != null)
                {
                    var tile = GetCachedTile(def, wx, wy, seed);
                    if (tile != null)
                    {
                        if (!biomeTileData.ContainsKey(biomeType))
                            biomeTileData[biomeType] = new List<(Vector3Int, TileBase)>();

                        biomeTileData[biomeType].Add((new Vector3Int(wx, wy, 0), tile));
                    }

                    // Lấn sang hàng xóm nếu weight cao hơn
                    for (int dx = -invasionRadius; dx <= invasionRadius; dx++)
                    {
                        for (int dy = -invasionRadius; dy <= invasionRadius; dy++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            if (!includeDiagonals && Mathf.Abs(dx) + Mathf.Abs(dy) != 1) continue;

                            int nx = lx + dx;
                            int ny = ly + dy;
                            if (nx >= 0 && nx < size && ny >= 0 && ny < size)
                            {
                                var neighborBiome = biomeMap[nx, ny];
                                if (_biomeDefs.TryGetValue(neighborBiome, out var neighborDef))
                                {
                                    if (def.sortingWeight > neighborDef.sortingWeight)
                                    {
                                        var neighborPos = new Vector3Int(startX + nx, startY + ny, 0);
                                        var neighborTile = GetCachedTile(def, neighborPos.x, neighborPos.y, seed);
                                        if (neighborTile != null)
                                        {
                                            if (!biomeTileData.ContainsKey(biomeType))
                                                biomeTileData[biomeType] = new List<(Vector3Int, TileBase)>();

                                            biomeTileData[biomeType].Add((neighborPos, neighborTile));
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            if (ly % 10 == 0 && stopwatch.ElapsedMilliseconds >= 2f)
            {
                stopwatch.Restart();
                yield return null;
            }
        }

        // Vẽ theo thứ tự weight
        foreach (var kvp in biomeTileData.OrderBy(k => _biomeDefs[k.Key].sortingWeight))
        {
            var def = _biomeDefs[kvp.Key];
            var tm = GetTilemapForBiome(def);
            var tiles = kvp.Value;

            for (int i = 0; i < tiles.Count; i += tilesPerBatch)
            {
                int batchSize = Mathf.Min(tilesPerBatch, tiles.Count - i);
                var batchPositions = new Vector3Int[batchSize];
                var batchTiles = new TileBase[batchSize];

                for (int j = 0; j < batchSize; j++)
                {
                    batchPositions[j] = tiles[i + j].pos;
                    batchTiles[j] = tiles[i + j].tile;
                }

                tm.SetTiles(batchPositions, batchTiles);

                if (stopwatch.ElapsedMilliseconds >= 2f)
                {
                    stopwatch.Restart();
                    yield return null;
                }
            }

            tm.RefreshAllTiles();
        }
    }

    private TileBase GetCachedTile(BiomeDefinition def, int worldX, int worldY, int seed)
    {
        string cacheKey = $"{def.biomeType}_{worldX}_{worldY}_{seed}";
        if (_tileCache.TryGetValue(cacheKey, out var cachedTile))
            return cachedTile;

        var tile = def.PickTile(worldX, worldY, seed);
        if (_tileCache.Count > 10000) _tileCache.Clear();
        _tileCache[cacheKey] = tile;
        return tile;
    }

    // === ClearChunk overload ===
    public void ClearChunk(ChunkCoord coord)
    {
        var meta = NetworkWorldManager.Instance?.Meta;
        if (meta != null) ClearChunk(coord, meta.chunkSize);
    }

    public void ClearChunk(ChunkCoord coord, int chunkSize)
    {
        foreach (var biomeType in new List<BiomeType>(_activeTilemaps.Keys))
        {
            ReleaseTilemap(biomeType);
        }
        _processedChunks.Remove(coord);
    }
}
