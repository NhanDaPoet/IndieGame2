using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "WorldGen/Biome Set")]
public class BiomeSet : ScriptableObject
{
    public List<BiomeDefinition> biomes = new();

    private Dictionary<BiomeType, BiomeDefinition> _map;

    public void BuildCache()
    {
        _map = new Dictionary<BiomeType, BiomeDefinition>();
        foreach (var b in biomes)
        {
            if (b == null) continue;
            _map[b.biomeType] = b;
        }
    }

    public BiomeDefinition Get(BiomeType t)
    {
        if (_map == null) BuildCache();
        return _map != null && _map.TryGetValue(t, out var def) ? def : null;
    }
}
