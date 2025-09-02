using System.Collections.Generic;
using UnityEngine;

public class ClientPrefabRuntime : MonoBehaviour
{
    public static ClientPrefabRuntime Instance { get; private set; }

    private PrefabRegistry _registry;
    private readonly Dictionary<ChunkCoord, List<GameObject>> _spawnedByChunk = new();

    private void Awake()
    {
        Instance = this;
        _registry = NetworkWorldManager.Instance.GetComponent<NetworkWorldManager>()?.BiomeSet
            ? Resources.Load<PrefabRegistry>(NetworkWorldManager.Instance.Meta.prefabRegistryResource)
            : NetworkWorldManager.Instance.BiomeSet ? null : null;
        _registry = Resources.Load<PrefabRegistry>(NetworkWorldManager.Instance.Meta.prefabRegistryResource);
        _registry.BuildCaches();
    }

    public void ApplySpawns(PrefabSpawn[] spawns)
    {
        var byChunk = new Dictionary<ChunkCoord, List<PrefabSpawn>>();
        foreach (var s in spawns)
        {
            var c = CellToChunk(s.cell);
            if (!byChunk.TryGetValue(c, out var list)) { list = new List<PrefabSpawn>(); byChunk[c] = list; }
            list.Add(s);
        }
        foreach (var kv in byChunk)
        {
            SpawnChunk(kv.Key, kv.Value);
        }
    }

    private void SpawnChunk(ChunkCoord coord, List<PrefabSpawn> list)
    {
        if (!_spawnedByChunk.TryGetValue(coord, out var gos))
        {
            gos = new List<GameObject>();
            _spawnedByChunk[coord] = gos;
        }
        foreach (var s in list)
        {
            var prefab = _registry.GetPrefab(s.prefabId);
            if (!prefab) continue;
            Vector3 worldPos = NetworkWorldManager.Instance.grid.CellToWorld(s.cell);
            var go = Instantiate(prefab, worldPos, Quaternion.identity, transform);
            gos.Add(go);
        }
    }

    public void DespawnChunk(ChunkCoord coord)
    {
        if (_spawnedByChunk.TryGetValue(coord, out var gos))
        {
            foreach (var go in gos)
            {
                if (go) Destroy(go);
            }
            gos.Clear();
        }
    }

    private ChunkCoord CellToChunk(Vector3Int cell)
    {
        int cs = NetworkWorldManager.Instance.Meta.chunkSize;
        int cx = Mathf.FloorToInt((float)cell.x / cs);
        int cy = Mathf.FloorToInt((float)cell.y / cs);
        return new ChunkCoord(cx, cy);
    }
}
