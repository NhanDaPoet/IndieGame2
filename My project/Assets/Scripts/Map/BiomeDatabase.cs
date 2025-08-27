using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "BiomeDatabase", menuName = "Biome/Database")]
public class BiomeDatabase : ScriptableObject
{
    [Header("Biomes")]
    public List<BiomeData> biomes;  

    public BiomeData GetBiome(string biomeName)
    {
        foreach (BiomeData biome in biomes)
        {
            if (biome.biomeName.Equals(biomeName, System.StringComparison.OrdinalIgnoreCase))
            {
                return biome;
            }
        }
        return null; 
    }

    public BiomeData GetBiome(BiomeData.BiomeType biomeType)
    {
        foreach (BiomeData biome in biomes)
        {
            if (biome.biomeType == biomeType)
            {
                return biome;
            }
        }
        return null; 
    }
}
