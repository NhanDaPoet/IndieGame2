using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "New Biome", menuName = "Biome/Definition")]
public class BiomeData : ScriptableObject
{
    [Header("Biome Information")]
    public string biomeName;
    public BiomeType biomeType; 

    [Header("Biome Visuals")]
    public Color biomeColor; 

    [Header("Biome Resources")]
    public List<ItemData> resources; 

    [Header("Biome Attributes")]
    public float temperature; 
    public float humidity; 

    public enum BiomeType
    {
        Forest,
        Desert,
        Mountain,
        Ocean,
        Snow
    }
}
