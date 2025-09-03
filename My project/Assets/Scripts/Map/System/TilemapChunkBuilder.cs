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
    private struct ChunkBuildJob { public ChunkCoord coord; public byte[] data; }
    private readonly Queue<ChunkBuildJob> _queue = new();
    [SerializeField] private int maxChunksPerFrame = 2;
    [SerializeField] private float buildBudgetMs = 3f;
    private bool _processing;

    private void Awake() { Instance = this; }

    private void EnsureCache()
    {
        if (_biomeDefs != null) return;
        _biomeDefs = new Dictionary<BiomeType, BiomeDefinition>();
        var set = NetworkWorldManager.Instance.BiomeSet;
        set.BuildCache();
        foreach (var b in set.biomes)
        {
            if (b == null) continue;
            if (!_biomeDefs.ContainsKey(b.biomeType))
                _biomeDefs[b.biomeType] = b;
        }
    }

    public void EnqueueBuild(ChunkCoord coord, byte[] biomeData)
    {
        EnsureCache();
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
            sw.Reset(); sw.Start();
            while (_queue.Count > 0 && built < maxChunksPerFrame && sw.ElapsedMilliseconds < buildBudgetMs)
            {
                var job = _queue.Dequeue();
                BuildGroundFromBytes(job.coord, job.data);
                built++;
            }
            yield return null; 
        }
        _processing = false;
    }

    private void BuildGroundFromBytes(ChunkCoord coord, byte[] data)
    {
        EnsureCache();
        var meta = NetworkWorldManager.Instance.Meta;
        int cs = meta.chunkSize;
        int x0 = coord.x * cs;
        int y0 = coord.y * cs;
        if (data == null || data.Length != cs * cs)
        {
            BuildGroundForChunk(coord);
            return;
        }

        var positions = new Vector3Int[cs * cs];
        var tiles = new TileBase[cs * cs];

        int idx = 0;
        for (int ly = 0; ly < cs; ly++)
        {
            for (int lx = 0; lx < cs; lx++)
            {
                int wx = x0 + lx;
                int wy = y0 + ly;

                var bt = (BiomeType)data[idx];
                if (_biomeDefs.TryGetValue(bt, out var def) && def != null)
                {
                    tiles[idx] = def.PickGroundTileDeterministic(wx, wy, meta.seed);
                }
                positions[idx] = new Vector3Int(wx, wy, 0);
                idx++;
            }
        }
        groundTilemap.SetTiles(positions, tiles);
    }

    public void BuildGroundForChunk(ChunkCoord coord)
    {
        EnsureCache();
        var meta = NetworkWorldManager.Instance.Meta;
        int cs = meta.chunkSize;
        int x0 = coord.x * cs;
        int y0 = coord.y * cs;
        var positions = new List<Vector3Int>(cs * cs);
        var tiles = new List<TileBase>(cs * cs);
        for (int lx = 0; lx < cs; lx++)
            for (int ly = 0; ly < cs; ly++)
            {
                int wx = x0 + lx;
                int wy = y0 + ly;
                BiomeType bt = BiomeService.SampleBiome(wx, wy,
                    NetworkWorldManager.Instance.Meta.seed,
                    NetworkWorldManager.Instance.Noise,
                    NetworkWorldManager.Instance.BiomeRegion,
                    NetworkWorldManager.Instance.BiomeSet);

                if (_biomeDefs.TryGetValue(bt, out var def) && def != null)
                {
                    var tile = def.PickGroundTileDeterministic(wx, wy, meta.seed);
                    if (tile != null)
                    {
                        positions.Add(new Vector3Int(wx, wy, 0));
                        tiles.Add(tile);
                    }
                }
            }

        groundTilemap.SetTiles(positions.ToArray(), tiles.ToArray());
    }

    public void ClearChunk(ChunkCoord coord)
    {
        var meta = NetworkWorldManager.Instance.Meta;
        int cs = meta.chunkSize;
        int x0 = coord.x * cs;
        int y0 = coord.y * cs;

        var positions = new Vector3Int[cs * cs];
        var tiles = new TileBase[cs * cs]; 

        int idx = 0;
        for (int ly = 0; ly < cs; ly++)
            for (int lx = 0; lx < cs; lx++)
                positions[idx++] = new Vector3Int(x0 + lx, y0 + ly, 0);
        groundTilemap.SetTiles(positions, tiles);
    }
}
