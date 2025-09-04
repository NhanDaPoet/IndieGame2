using System.Collections.Generic;
using UnityEngine;

public static class PrefabScatter
{
    // Cache để tránh tính toán lại
    private static readonly Dictionary<string, System.Random> _cachedRandoms = new();
    private static readonly Dictionary<string, List<Vector2Int>> _candidateCache = new();

    // Reusable collections để tránh allocation
    private static readonly List<PrefabSpawn> _spawnList = new();
    private static readonly Dictionary<ushort, HashSet<Vector2Int>> _occupiedDict = new();
    private static readonly Dictionary<BiomeType, int> _biomeDensityDict = new();
    private static readonly List<Vector2Int> _candidateList = new();

    public static PrefabSpawn[] BuildSpawnsForChunk(ChunkData cd, BiomeSet biomeSet, PrefabRegistry registry, int seed)
    {
        // Clear reusable collections
        _spawnList.Clear();
        _occupiedDict.Clear();
        _biomeDensityDict.Clear();

        int cs = cd.biome.GetLength(0);
        string cacheKey = $"{seed}_{cd.coord.x}_{cd.coord.y}";

        // Get or create cached random
        if (!_cachedRandoms.TryGetValue(cacheKey, out var rand))
        {
            rand = new System.Random(Hash(seed, cd.coord));
            _cachedRandoms[cacheKey] = rand;

            // Limit cache size để tránh memory leak
            if (_cachedRandoms.Count > 1000)
            {
                _cachedRandoms.Clear();
                rand = new System.Random(Hash(seed, cd.coord));
                _cachedRandoms[cacheKey] = rand;
            }
        }

        Debug.Log($"Generating spawns for chunk {cd.coord} with seed {seed}");

        // Tạo map mật độ biome trong chunk - optimized
        CalculateBiomeDensity(cd, cs);

        if (_biomeDensityDict.Count == 0)
        {
            Debug.LogWarning($"No biomes found in chunk {cd.coord}");
            return new PrefabSpawn[0];
        }

        // Spawn theo từng biome với mật độ được kiểm soát
        foreach (var biomeKv in _biomeDensityDict)
        {
            var biomeType = biomeKv.Key;
            var tileCount = biomeKv.Value;
            var def = biomeSet.Get(biomeType);

            if (def?.prefabRules == null) continue;

            ProcessBiomeSpawns(def, biomeType, tileCount, cs, cd, registry, rand);
        }

        // Convert to array và clear cache nếu cần
        var result = _spawnList.ToArray();

        Debug.Log($"Total spawns generated for chunk {cd.coord}: {result.Length}");
        return result;
    }

    private static void CalculateBiomeDensity(ChunkData cd, int cs)
    {
        _biomeDensityDict.Clear();

        for (int lx = 0; lx < cs; lx++)
        {
            for (int ly = 0; ly < cs; ly++)
            {
                var b = cd.biome[lx, ly];
                _biomeDensityDict.TryGetValue(b, out int count);
                _biomeDensityDict[b] = count + 1;
            }
        }
    }

    private static void ProcessBiomeSpawns(BiomeDefinition def, BiomeType biomeType, int tileCount, int cs,
                                         ChunkData cd, PrefabRegistry registry, System.Random rand)
    {
        foreach (var rule in def.prefabRules)
        {
            if (!registry.TryKeyToId(rule.prefabKey, out ushort pid))
            {
                Debug.LogWarning($"Prefab key '{rule.prefabKey}' not found in registry.");
                continue;
            }

            // Tính số spawn thực tế dựa trên mật độ biome trong chunk
            float density = (float)tileCount / (cs * cs);
            int maxSpawns = Mathf.RoundToInt(rule.targetCountPerChunk * density);

            // Giới hạn spawn tối đa để tránh quá dày
            maxSpawns = Mathf.Min(maxSpawns, tileCount / Mathf.Max(1, rule.minSpacing + 1));

            if (maxSpawns <= 0) continue;

            // Get candidates for this biome - with caching
            GetCandidatesForBiome(cd, biomeType, cs);

            if (_candidateList.Count == 0) continue;

            // Shuffle candidates for randomness - Fisher-Yates optimized
            ShuffleCandidates(rand);

            int spawned = ProcessSpawnRule(rule, pid, maxSpawns, cd, rand, cs, biomeType);

            Debug.Log($"Spawned {spawned}/{maxSpawns} of {rule.prefabKey} in biome {biomeType}");
        }
    }

    private static void GetCandidatesForBiome(ChunkData cd, BiomeType biomeType, int cs)
    {
        _candidateList.Clear();

        for (int lx = 0; lx < cs; lx++)
        {
            for (int ly = 0; ly < cs; ly++)
            {
                if (cd.biome[lx, ly] == biomeType)
                {
                    _candidateList.Add(new Vector2Int(lx, ly));
                }
            }
        }
    }

    private static void ShuffleCandidates(System.Random rand)
    {
        // Fisher-Yates shuffle - optimized
        for (int i = _candidateList.Count - 1; i > 0; i--)
        {
            int j = rand.Next(i + 1);
            var temp = _candidateList[i];
            _candidateList[i] = _candidateList[j];
            _candidateList[j] = temp;
        }
    }

    private static int ProcessSpawnRule(PrefabSpawnRule rule, ushort pid, int maxSpawns, ChunkData cd,
                                      System.Random rand, int cs, BiomeType biomeType)
    {
        int spawned = 0;
        foreach (var pos in _candidateList)
        {
            if (spawned >= maxSpawns) break;
            if (!CheckSpacingOptimized(pid, pos, rule.minSpacing)) continue;
            if (rule.cluster && rule.clusterRadius > 0)
            {
                spawned += ProcessClusterSpawn(rule, pid, pos, cd, rand, cs, biomeType, maxSpawns - spawned);
            }
            else
            {
                AddSpawnOptimized(pid, cd.coord, pos);
                spawned++;
            }
        }
        return spawned;
    }

    private static int ProcessClusterSpawn(PrefabSpawnRule rule, ushort pid, Vector2Int centerPos,
                                         ChunkData cd, System.Random rand, int cs, BiomeType biomeType, int remainingSpawns)
    {
        int clusterSize = RandRange(rand, rule.clusterCountRange.x, rule.clusterCountRange.y);
        clusterSize = Mathf.Min(clusterSize, remainingSpawns);

        int spawned = 0;

        for (int i = 0; i < clusterSize; i++)
        {
            Vector2Int spawnPos;

            if (i == 0)
            {
                spawnPos = centerPos;
            }
            else
            {
                var offset = RandomInRadius(rand, rule.clusterRadius);
                spawnPos = new Vector2Int(
                    Mathf.Clamp(centerPos.x + offset.x, 0, cs - 1),
                    Mathf.Clamp(centerPos.y + offset.y, 0, cs - 1)
                );

                // Check if position is still in correct biome
                if (cd.biome[spawnPos.x, spawnPos.y] != biomeType) continue;
            }

            if (!CheckSpacingOptimized(pid, spawnPos, rule.minSpacing)) continue;

            AddSpawnOptimized(pid, cd.coord, spawnPos);
            spawned++;
        }

        return spawned;
    }

    private static void AddSpawnOptimized(ushort pid, ChunkCoord coord, Vector2Int local)
    {
        if (!_occupiedDict.TryGetValue(pid, out var set))
        {
            set = new HashSet<Vector2Int>();
            _occupiedDict[pid] = set;
        }
        set.Add(local);

        int cs = NetworkWorldManager.Instance.Meta.chunkSize;
        var worldCell = new Vector3Int(coord.x * cs + local.x, coord.y * cs + local.y, 0);

        _spawnList.Add(new PrefabSpawn
        {
            prefabId = pid,
            cell = worldCell,
            variant = 0
        });
    }

    private static bool CheckSpacingOptimized(ushort pid, Vector2Int cell, int minSpacing)
    {
        if (minSpacing <= 0) return true;
        if (!_occupiedDict.TryGetValue(pid, out var set)) return true;

        // Optimized spacing check - early exit
        foreach (var ex in set)
        {
            int dx = ex.x - cell.x;
            int dy = ex.y - cell.y;
            int d = (dx < 0 ? -dx : dx) + (dy < 0 ? -dy : dy); // Abs optimization
            if (d < minSpacing) return false;
        }
        return true;
    }

    private static Vector2Int RandomInRadius(System.Random r, int radius)
    {
        // More efficient than original
        int x = r.Next(-radius, radius + 1);
        int y = r.Next(-radius, radius + 1);
        return new Vector2Int(x, y);
    }

    private static int RandRange(System.Random r, int a, int b)
    {
        return a >= b ? a : (a + r.Next(b - a + 1));
    }

    private static int Hash(int seed, ChunkCoord c)
    {
        unchecked
        {
            int hash = seed;
            hash = (hash * 73856093) ^ c.x;
            hash = (hash * 19349663) ^ c.y;
            return hash;
        }
    }

    // Cleanup method để call khi change scenes
    public static void FlushCaches()
    {
        _cachedRandoms.Clear();
        _candidateCache.Clear();
        _spawnList.Clear();
        _occupiedDict.Clear();
        _biomeDensityDict.Clear();
        _candidateList.Clear();

        Debug.Log("PrefabScatter caches flushed.");
    }
}