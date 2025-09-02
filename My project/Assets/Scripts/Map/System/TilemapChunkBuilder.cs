using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class TilemapChunkBuilder : MonoBehaviour
{
    public static TilemapChunkBuilder Instance { get; private set; }

    [SerializeField] private Grid grid;
    [SerializeField] private Tilemap groundTilemap;

    private Dictionary<BiomeType, TileBase> _tiles;

    private void Awake()
    {
        Instance = this;
    }

    private void EnsureCache()
    {
        if (_tiles != null) return;
        _tiles = new Dictionary<BiomeType, TileBase>();
        foreach (var b in NetworkWorldManager.Instance.BiomeSet.biomes)
        {
            if (!_tiles.ContainsKey(b.biomeType))
                _tiles[b.biomeType] = b.groundTile;
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
                BiomeType bt = BiomeService.SampleBiome(wx, wy, meta.seed, noise);
                var tile = _tiles.TryGetValue(bt, out var t) ? t : null;

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
        for (int lx = 0; lx < cs; lx++)
        {
            for (int ly = 0; ly < cs; ly++)
            {
                groundTilemap.SetTile(new Vector3Int(x0 + lx, y0 + ly, 0), null);
            }
        }
    }
}
