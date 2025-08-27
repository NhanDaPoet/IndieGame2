using UnityEngine;

public interface IBiomeProvider
{
    string GetBiomeAt(Vector3 worldPos);
}

public class BiomeManager : MonoBehaviour, IBiomeProvider
{
    [Tooltip("Tên biome mặc định nếu không phân loại.")]
    public string defaultBiome = "default";

    public BiomeDatabase biomeDatabase;
    public BiomeData[] biomes;

    void Start()
    {
        if (biomeDatabase == null)
        {
            return;
        }
    }

    public string GetBiomeAt(Vector3 worldPos)
    {
        float xCoord = worldPos.x / 100f;
        float yCoord = worldPos.y / 100f;
        float noiseValue = Mathf.PerlinNoise(xCoord, yCoord);
        if (noiseValue < 0.3f)
            return biomeDatabase.GetBiome(BiomeData.BiomeType.Forest).biomeName;
        else if (noiseValue < 0.6f)
            return biomeDatabase.GetBiome(BiomeData.BiomeType.Desert).biomeName;
        else
            return biomeDatabase.GetBiome(BiomeData.BiomeType.Mountain).biomeName;
    }

    public BiomeData GetBiomeData(Vector3 worldPos)
    {
        string biomeName = GetBiomeAt(worldPos);
        return biomeDatabase.GetBiome(biomeName);
    }

    public static IBiomeProvider Get()
    {
        var found = FindFirstObjectByType<BiomeManager>();
        return found != null ? found : null;
    }
}
