using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class TilemapChunkBuilder : MonoBehaviour
{
    public static TilemapChunkBuilder Instance { get; private set; }

    [SerializeField] private Grid grid;
    [SerializeField] private Tilemap groundTilemap;
    private Dictionary<BiomeType, BiomeDefinition> _biomeDefs;

    private void Awake()
    {
        Instance = this;
    }

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

    public void BuildGroundForChunk(ChunkCoord coord)
    {
        EnsureCache();
        var meta = NetworkWorldManager.Instance.Meta;
        var noise = NetworkWorldManager.Instance.Noise;
        int cs = meta.chunkSize;
        int x0 = coord.x * cs;
        int y0 = coord.y * cs;
        var positions = new List<Vector3Int>(cs * cs);
        var tiles = new List<TileBase>(cs * cs);
        for (int lx = 0; lx < cs; lx++)
        {
            for (int ly = 0; ly < cs; ly++)
            {
                int wx = x0 + lx;
                int wy = y0 + ly;
                BiomeType bt = BiomeService.SampleBiome(wx, wy,meta.seed,NetworkWorldManager.Instance.Noise,NetworkWorldManager.Instance.BiomeRegion,NetworkWorldManager.Instance.BiomeSet);
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
        }
        groundTilemap.SetTiles(positions.ToArray(), tiles.ToArray());
    }

    public void ClearChunk(ChunkCoord coord)
    {
        var meta = NetworkWorldManager.Instance.Meta;
        int cs = meta.chunkSize;
        int x0 = coord.x * cs;
        int y0 = coord.y * cs;
        for (int lx = 0; lx < cs; lx++)
        {
            for (int ly = 0; ly < cs; ly++)
            {
                groundTilemap.SetTile(new Vector3Int(x0 + lx, y0 + ly, 0), null);
            }
        }
    }
}
