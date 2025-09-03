using UnityEngine;

public static class BiomeService
{
    //Elevation + Moisture + SeaLevel
    public static BiomeType SampleBiome(
        int worldX, int worldY,
        int seed,
        NoiseSettings noise,
        BiomeRegionSettings regionCfg,
        BiomeSet biomeSet)
    {
        var cell = RegionService.NearestRegionCell(seed, worldX, worldY, regionCfg.regionSize, regionCfg.centerJitter);
        int regionId = RegionService.RegionId(seed, cell);
        BiomeType baseBiome = PickBiomeFromPalette(regionCfg, regionId);
        float e = NoiseService.FractalPerlin(worldX + noise.elevationOffset.x, worldY + noise.elevationOffset.y,
                                             seed, noise.elevationScale, noise.elevationOctaves, noise.elevationPersistence, noise.elevationLacunarity);
        float m = NoiseService.FractalPerlin(worldX + noise.moistureOffset.x, worldY + noise.moistureOffset.y,
                                             seed, noise.moistureScale, noise.moistureOctaves, noise.moisturePersistence, noise.moistureLacunarity);
        switch (baseBiome)
        {
            case BiomeType.Forest:
                if (e < noise.seaLevel + 0.03f && m > 0.65f) return BiomeType.Swamp;
                return BiomeType.Forest;

            case BiomeType.Plains:
                if (m < 0.22f) return BiomeType.Desert;
                return BiomeType.Plains;

            case BiomeType.Desert:
                if (m > 0.6f && e > noise.seaLevel + 0.1f) return BiomeType.Plains;
                return BiomeType.Desert;

            case BiomeType.Mountains:
                if (e < 0.75f) return BiomeType.Plains;
                return BiomeType.Mountains;

            case BiomeType.Swamp:
                if (m < 0.5f) return BiomeType.Plains;
                return BiomeType.Swamp;

            default:
                return baseBiome;
        }
    }

    private static BiomeType PickBiomeFromPalette(BiomeRegionSettings cfg, int regionId)
    {
        if (cfg.biomePalette == null || cfg.biomePalette.Length == 0)
            return BiomeType.Forest;
        int idx = Mathf.Abs(regionId) % cfg.biomePalette.Length;
        return cfg.biomePalette[idx];
    }
}
