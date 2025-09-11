using System;
using System.Collections.Generic;
using UnityEngine;

public static class BiomeService
{
    private static readonly Dictionary<string, BiomeType> _biomeCache = new();
    private const int BIOME_CACHE_GRID_SIZE = 4;

    /// <summary>
    /// Sample biome at world position
    /// </summary>
    public static BiomeType SampleBiome(
        int worldX, int worldY,
        int seed,
        NoiseSettings noise,
        BiomeRegionSettings regionCfg,
        BiomeSet biomeSet)
    {
        int cacheX = worldX / BIOME_CACHE_GRID_SIZE;
        int cacheY = worldY / BIOME_CACHE_GRID_SIZE;
        string cacheKey = $"{seed}_{cacheX}_{cacheY}";

        if (_biomeCache.TryGetValue(cacheKey, out BiomeType cachedBiome))
        {
            return cachedBiome;
        }

        var primaryRegion = RegionService.NearestRegionCell(seed, worldX, worldY, regionCfg.regionSize, regionCfg.centerJitter);
        var baseBiome = GetRegionBiome(seed, primaryRegion, worldX, worldY, noise, regionCfg);

        _biomeCache[cacheKey] = baseBiome;
        if (_biomeCache.Count > 10000)
        {
            _biomeCache.Clear();
        }

        return baseBiome;
    }

    /// <summary>
    /// Generate simple biome map without edge processing
    /// </summary>
    public static BiomeType[,] GenerateBiomeMap(
        int startX, int startY, int width, int height,
        int seed, NoiseSettings noise, BiomeRegionSettings regionCfg, BiomeSet biomeSet)
    {
        var biomeMap = new BiomeType[width, height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int worldX = startX + x;
                int worldY = startY + y;
                biomeMap[x, y] = SampleBiome(worldX, worldY, seed, noise, regionCfg, biomeSet);
            }
        }

        return biomeMap;
    }

    private static BiomeType GetRegionBiome(int seed, Vector2Int regionCell, int worldX, int worldY,
        NoiseSettings noise, BiomeRegionSettings regionCfg)
    {
        int regionId = RegionService.RegionId(seed, regionCell);
        BiomeType baseBiome = PickBiomeFromPalette(regionCfg, regionId);

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

    private static BiomeType PickBiomeFromPalette(BiomeRegionSettings cfg, int regionId)
    {
        if (cfg.biomePalette == null || cfg.biomePalette.Length == 0)
            return BiomeType.Forest;
        int idx = Mathf.Abs(regionId) % cfg.biomePalette.Length;
        return cfg.biomePalette[idx];
    }

    public static void FlushCaches()
    {
        _biomeCache.Clear();
    }
}