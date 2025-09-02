using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "WorldGen/Biome Set")]
public class BiomeSet : ScriptableObject
{
    public List<BiomeDefinition> biomes = new();

    public BiomeDefinition Get(BiomeType t)
    {
        return biomes.Find(b => b.biomeType == t);
    }
}
