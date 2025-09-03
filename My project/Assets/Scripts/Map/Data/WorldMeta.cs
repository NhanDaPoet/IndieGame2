using UnityEngine;

[System.Serializable]
public class WorldMeta
{
    public int seed;
    public int chunkSize = 32; 
    public int minPlayableRadiusChunks = 2;

    public string biomeSetResource = "Biomes/BiomeSet_Default";
    public string prefabRegistryResource = "Registries/PrefabRegistry_Default";
    public string noiseSettingsResource = "Registries/NoiseSettings_Default";
    public string biomeRegionSettingsResource = "Registries/BiomeRegionSettings_Default";
}
