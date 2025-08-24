using UnityEngine;

public interface IBiomeProvider
{
    string GetBiomeAt(Vector3 worldPos);
}

public class BiomeManager : MonoBehaviour, IBiomeProvider
{
    [Tooltip("Tên biome mặc định nếu không phân loại.")]
    public string defaultBiome = "default";

    public string GetBiomeAt(Vector3 worldPos)
    {
        return defaultBiome;
    }

    public static IBiomeProvider Get()
    {
        var found = FindFirstObjectByType<BiomeManager>();
        return found != null ? found : null;
    }
}
