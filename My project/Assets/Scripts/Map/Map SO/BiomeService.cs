using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class BiomeService
{
    private static readonly Dictionary<string, BiomeType> _biomeCache = new();
    private static readonly Dictionary<string, BiomeBlendData> _blendCache = new();
    private const int BIOME_CACHE_GRID_SIZE = 4;

    public struct BiomeBlendData
    {
        public BiomeType primaryBiome;
        public List<BiomeType> neighborBiomes; 
        public float blendFactor;
        public bool isBorder;
        public Vector2Int direction;
        public Vector2Int cornerDirection;
    }

    public static BiomeType SampleBiome(
        int worldX, int worldY,
        int seed,
        NoiseSettings noise,
        BiomeRegionSettings regionCfg,
        BiomeSet biomeSet)
    {
        var blendData = SampleBiomeWithBlend(worldX, worldY, seed, noise, regionCfg, biomeSet);
        return blendData.primaryBiome;
    }

    public static BiomeBlendData SampleBiomeWithBlend(
        int worldX, int worldY,
        int seed,
        NoiseSettings noise,
        BiomeRegionSettings regionCfg,
        BiomeSet biomeSet)
    {
        int cacheX = worldX / BIOME_CACHE_GRID_SIZE;
        int cacheY = worldY / BIOME_CACHE_GRID_SIZE;
        string cacheKey = $"{seed}_{cacheX}_{cacheY}";
        if (_blendCache.TryGetValue(cacheKey, out BiomeBlendData cachedBlend))
        {
            return cachedBlend;
        }

        var primaryRegion = RegionService.NearestRegionCell(seed, worldX, worldY, regionCfg.regionSize, regionCfg.centerJitter);
        var primaryBiome = GetRegionBiome(seed, primaryRegion, worldX, worldY, noise, regionCfg);

        var blendData = new BiomeBlendData
        {
            primaryBiome = primaryBiome,
            neighborBiomes = new List<BiomeType>(),
            blendFactor = 0f,
            isBorder = false,
            cornerDirection = Vector2Int.zero
        };

        int edgeBlend = regionCfg.edgeBlend;
        if (edgeBlend > 0)
        {
            float distanceToBoundary = GetDistanceToNearestBoundary(worldX, worldY, regionCfg.regionSize);

            if (distanceToBoundary <= edgeBlend)
            {
                var nearestDifferentBiome = FindNearestDifferentBiome(
                    worldX, worldY, seed, noise, regionCfg, primaryBiome, edgeBlend);
                if (nearestDifferentBiome != primaryBiome)
                {
                    blendData.neighborBiomes.Add(nearestDifferentBiome);
                    blendData.blendFactor = 1f - (distanceToBoundary / edgeBlend);
                    blendData.isBorder = true;

                    // Now safe: GetNeighborDirections uses direct computation, no recursion
                    List<Vector2Int> directions = GetNeighborDirections(worldX, worldY, seed, noise, regionCfg, biomeSet, primaryBiome);
                    if (directions.Count > 1)
                    {
                        blendData.cornerDirection = directions[1];
                    }
                }
            }
        }

        _blendCache[cacheKey] = blendData;
        if (_blendCache.Count > 10000)
        {
            _blendCache.Clear();
        }
        return blendData;
    }

    public static List<Vector2Int> GetNeighborDirections(int worldX, int worldY, int seed, NoiseSettings noise,
        BiomeRegionSettings regionCfg, BiomeSet biomeSet, BiomeType currentBiome)
    {
        List<Vector2Int> directions = new();
        Vector2Int[] checkDirs = {
            new Vector2Int(0, 1), new Vector2Int(1, 0), new Vector2Int(0, -1), new Vector2Int(-1, 0),
            new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, -1), new Vector2Int(-1, 1)
        };

        foreach (var dir in checkDirs)
        {
            int nx = worldX + dir.x;
            int ny = worldY + dir.y;
            // Direct call: Compute biome without full sampling/recursion
            BiomeType neighborBiome = ComputeBiomeDirect(seed, nx, ny, noise, regionCfg);
            if (neighborBiome != currentBiome)
            {
                directions.Add(dir);
            }
        }

        return directions;
    }

    private static BiomeType GetRegionBiome(int seed, Vector2Int regionCell, int worldX, int worldY,
        NoiseSettings noise, BiomeRegionSettings regionCfg)
    {
        int regionId = RegionService.RegionId(seed, regionCell);
        BiomeType baseBiome = PickBiomeFromPalette(regionCfg, regionId);

        // Apply noise-based modifications
        float e = NoiseService.FractalPerlin(worldX + noise.elevationOffset.x, worldY + noise.elevationOffset.y,
                                             seed, noise.elevationScale, noise.elevationOctaves, noise.elevationPersistence, noise.elevationLacunarity);
        float m = NoiseService.FractalPerlin(worldX + noise.moistureOffset.x, worldY + noise.moistureOffset.y,
                                             seed, noise.moistureScale, noise.moistureOctaves, noise.moisturePersistence, noise.moistureLacunarity);

        BiomeType resultBiome = baseBiome;

        switch (baseBiome)
        {
            case BiomeType.Forest:
                if (e < noise.seaLevel + 0.03f && m > 0.65f) resultBiome = BiomeType.Swamp;
                break;
            case BiomeType.Plains:
                if (m < 0.22f) resultBiome = BiomeType.Desert;
                break;
            case BiomeType.Desert:
                if (m > 0.6f && e > noise.seaLevel + 0.1f) resultBiome = BiomeType.Plains;
                break;
            case BiomeType.Mountains:
                if (e < 0.75f) resultBiome = BiomeType.Plains;
                break;
            case BiomeType.Swamp:
                if (m < 0.5f) resultBiome = BiomeType.Plains;
                break;
        }

        return resultBiome;
    }

    private static float GetDistanceToNearestBoundary(int worldX, int worldY, int regionSize)
    {
        // Calculate distance to nearest region boundary
        int localX = worldX % regionSize;
        int localY = worldY % regionSize;

        if (localX < 0) localX += regionSize;
        if (localY < 0) localY += regionSize;

        float distanceToLeft = localX;
        float distanceToRight = regionSize - localX;
        float distanceToBottom = localY;
        float distanceToTop = regionSize - localY;

        return Mathf.Min(Mathf.Min(distanceToLeft, distanceToRight), Mathf.Min(distanceToBottom, distanceToTop));
    }

    private static BiomeType FindNearestDifferentBiome(int worldX, int worldY, int seed, NoiseSettings noise,
        BiomeRegionSettings regionCfg, BiomeType primaryBiome, int searchRadius)
    {
        int regionSize = regionCfg.regionSize;

        // Check adjacent regions
        var directions = new Vector2Int[]
        {
            new Vector2Int(-1, 0), new Vector2Int(1, 0),  // Left, Right
            new Vector2Int(0, -1), new Vector2Int(0, 1),  // Down, Up
            new Vector2Int(-1, -1), new Vector2Int(1, -1), // Diagonals
            new Vector2Int(-1, 1), new Vector2Int(1, 1)
        };

        foreach (var dir in directions)
        {
            int sampleX = worldX + dir.x * (regionSize / 2);
            int sampleY = worldY + dir.y * (regionSize / 2);

            var regionCell = RegionService.NearestRegionCell(seed, sampleX, sampleY, regionSize, regionCfg.centerJitter);
            var biome = GetRegionBiome(seed, regionCell, sampleX, sampleY, noise, regionCfg);

            if (biome != primaryBiome)
            {
                return biome;
            }
        }

        return primaryBiome; // No different biome found
    }

    private static BiomeType PickBiomeFromPalette(BiomeRegionSettings cfg, int regionId)
    {
        if (cfg.biomePalette == null || cfg.biomePalette.Length == 0)
            return BiomeType.Forest;
        int idx = Mathf.Abs(regionId) % cfg.biomePalette.Length;
        return cfg.biomePalette[idx];
    }

    public static BiomeBlendData GetBlendDataForTile(int worldX, int worldY, int seed, NoiseSettings noise,
        BiomeRegionSettings regionCfg, BiomeSet biomeSet)
    {
        var blendData = SampleBiomeWithBlend(worldX, worldY, seed, noise, regionCfg, biomeSet);
        // Now safe: Uses refactored GetNeighborBiomes
        List<BiomeType> neighborBiomes = GetNeighborBiomes(worldX, worldY, seed, noise, regionCfg, biomeSet, blendData.primaryBiome);
        blendData.neighborBiomes = neighborBiomes;
        return blendData;
    }

    public static List<BiomeType> GetNeighborBiomes(int worldX, int worldY, int seed, NoiseSettings noise,
        BiomeRegionSettings regionCfg, BiomeSet biomeSet, BiomeType currentBiome)
    {
        HashSet<BiomeType> uniqueNeighbors = new HashSet<BiomeType>(); // Use HashSet for efficient uniqueness
        Vector2Int[] checkDirs = {
            new Vector2Int(0, 1), new Vector2Int(1, 0), new Vector2Int(0, -1), new Vector2Int(-1, 0),
            new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, -1), new Vector2Int(-1, 1)
        };
        foreach (var dir in checkDirs)
        {
            int nx = worldX + dir.x;
            int ny = worldY + dir.y;
            // Direct call: Compute biome without full sampling/recursion
            BiomeType neighborBiome = ComputeBiomeDirect(seed, nx, ny, noise, regionCfg);
            if (neighborBiome != currentBiome)
            {
                uniqueNeighbors.Add(neighborBiome);
            }
        }
        return uniqueNeighbors.ToList();
    }

    // NEW HELPER: Direct biome computation (mirrors GetRegionBiome logic, no recursion or cache)
    private static BiomeType ComputeBiomeDirect(int seed, int worldX, int worldY, NoiseSettings noise, BiomeRegionSettings regionCfg)
    {
        var regionCell = RegionService.NearestRegionCell(seed, worldX, worldY, regionCfg.regionSize, regionCfg.centerJitter);
        return GetRegionBiome(seed, regionCell, worldX, worldY, noise, regionCfg);
    }


    public static bool IsCorner(Vector2Int direction1, Vector2Int direction2)
    {
        return (direction1.x != 0 && direction2.y != 0) || (direction1.y != 0 && direction2.x != 0);
    }

    public static void FlushCaches()
    {
        _biomeCache.Clear();
        _blendCache.Clear();
    }
}