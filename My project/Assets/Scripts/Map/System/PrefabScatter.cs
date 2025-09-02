using System.Collections.Generic;
using UnityEngine;

public static class PrefabScatter
{
    public static PrefabSpawn[] BuildSpawnsForChunk(ChunkData cd, BiomeSet biomeSet, PrefabRegistry registry, int seed)
    {
        var list = new List<PrefabSpawn>();
        int cs = cd.biome.GetLength(0);
        var rand = new System.Random(Hash(seed, cd.coord));
        var occupied = new Dictionary<ushort, HashSet<Vector2Int>>();
        for (int lx = 0; lx < cs; lx++)
        {
            for (int ly = 0; ly < cs; ly++)
            {
                var b = cd.biome[lx, ly];
                var def = biomeSet.Get(b);
                if (def == null) continue;
                foreach (var rule in def.prefabRules)
                {
                    if (!registry.TryKeyToId(rule.prefabKey, out ushort pid)) continue;
                    float p = Mathf.Clamp01((float)rule.targetCountPerChunk / (cs * cs));
                    if (rand.NextDouble() > p) continue;
                    var cell = new Vector2Int(lx, ly);
                    if (!CheckSpacing(occupied, pid, cell, rule.minSpacing)) continue;
                    if (rule.cluster && rule.clusterRadius > 0)
                    {
                        int n = RandRange(rand, rule.clusterCountRange.x, rule.clusterCountRange.y);
                        for (int i = 0; i < n; i++)
                        {
                            var off = RandomInRadius(rand, rule.clusterRadius);
                            var cc = new Vector2Int(
                                Mathf.Clamp(lx + off.x, 0, cs - 1),
                                Mathf.Clamp(ly + off.y, 0, cs - 1)
                            );
                            if (!CheckSpacing(occupied, pid, cc, rule.minSpacing)) continue;

                            AddSpawn(list, occupied, pid, cd.coord, cc);
                        }
                    }
                    else
                    {
                        AddSpawn(list, occupied, pid, cd.coord, cell);
                    }
                }
            }
        }

        return list.ToArray();
    }

    private static void AddSpawn(List<PrefabSpawn> list, Dictionary<ushort, HashSet<Vector2Int>> occ, ushort pid, ChunkCoord coord, Vector2Int local)
    {
        if (!occ.TryGetValue(pid, out var set))
        {
            set = new HashSet<Vector2Int>();
            occ[pid] = set;
        }
        set.Add(local);
        int cs = NetworkWorldManager.Instance.Meta.chunkSize;
        var worldCell = new Vector3Int(coord.x * cs + local.x, coord.y * cs + local.y, 0);
        list.Add(new PrefabSpawn
        {
            prefabId = pid,
            cell = worldCell,
            variant = 0
        });
    }

    private static bool CheckSpacing(Dictionary<ushort, HashSet<Vector2Int>> occ, ushort pid, Vector2Int cell, int minSpacing)
    {
        if (minSpacing <= 0) return true;
        if (!occ.TryGetValue(pid, out var set)) return true;
        foreach (var ex in set)
        {
            int d = Mathf.Abs(ex.x - cell.x) + Mathf.Abs(ex.y - cell.y);
            if (d < minSpacing) return false;
        }
        return true;
    }

    private static Vector2Int RandomInRadius(System.Random r, int radius)
    {
        int x = r.Next(-radius, radius + 1);
        int y = r.Next(-radius, radius + 1);
        return new Vector2Int(x, y);
    }

    private static int RandRange(System.Random r, int a, int b) => a >= b ? a : (a + r.Next(b - a + 1));

    private static int Hash(int seed, ChunkCoord c) => seed ^ (c.x * 73856093) ^ (c.y * 19349663);
}
