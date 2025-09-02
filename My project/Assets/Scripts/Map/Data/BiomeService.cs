using UnityEngine;

public static class BiomeService
{
    //Elevation + Moisture + SeaLevel
    public static BiomeType SampleBiome(int worldX, int worldY, int seed, NoiseSettings noise)
    {
        float e = NoiseService.FractalPerlin(worldX + noise.elevationOffset.x, worldY + noise.elevationOffset.y,
                                             seed, noise.elevationScale, noise.elevationOctaves, noise.elevationPersistence, noise.elevationLacunarity);
        float m = NoiseService.FractalPerlin(worldX + noise.moistureOffset.x, worldY + noise.moistureOffset.y,
                                             seed, noise.moistureScale, noise.moistureOctaves, noise.moisturePersistence, noise.moistureLacunarity);
        if (e < noise.seaLevel) return BiomeType.Ocean;
        if (e < noise.seaLevel + 0.05f && m > 0.6f) return BiomeType.Swamp;   
        if (e > 0.82f) return BiomeType.Mountains;
        if (m < 0.25f) return BiomeType.Desert;
        if (m > 0.6f) return BiomeType.Forest;
        return BiomeType.Plains;
    }
}
