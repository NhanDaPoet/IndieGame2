using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

[CreateAssetMenu(fileName = "CustomBiomeRuleTile", menuName = "2D/Tiles/Custom Biome Rule Tile")]
public class CustomBiomeRuleTile : RuleTile<CustomBiomeRuleTile.Neighbor>
{
    [Serializable]
    public struct BiomeTileMapping
    {
        [Tooltip("Loại Biome (phải khớp với enum BiomeType trong BiomeDefinition)")]
        public BiomeType biomeType;

        [Tooltip("RuleTile tương ứng với biome này")]
        public RuleTile biomeTile;
    }

    [Tooltip("Danh sách ánh xạ giữa BiomeType và RuleTile")]
    public List<BiomeTileMapping> biomeMappings = new List<BiomeTileMapping>();

    public class Neighbor : RuleTile.TilingRuleOutput.Neighbor
    {
        public const int @this = 1;      // Tile giống transition (mặc định)
        public const int notthis = 2;   // Không phải transition (mặc định)
        public const int AnyBiome = 3;  // Bất kỳ biome nào trong biomeMappings
        // Biome-specific neighbors sẽ được map động (4, 5, 6, ...)
    }

    private Dictionary<BiomeType, RuleTile> _biomeTileCache;

    private void BuildBiomeTileCache()
    {
        if (_biomeTileCache != null) return;
        _biomeTileCache = new Dictionary<BiomeType, RuleTile>();
        foreach (var mapping in biomeMappings)
        {
            if (mapping.biomeTile != null && !_biomeTileCache.ContainsKey(mapping.biomeType))
            {
                _biomeTileCache[mapping.biomeType] = mapping.biomeTile;
            }
        }
    }

    public override bool RuleMatch(int neighbor, TileBase other)
    {
        BuildBiomeTileCache();

        switch (neighbor)
        {
            case Neighbor.@this:
                return other == this || other is CustomBiomeRuleTile;  // Cùng loại transition
            case Neighbor.notthis:
                return other != this && !(other is CustomBiomeRuleTile);  // Không phải transition
            case Neighbor.AnyBiome:
                return _biomeTileCache.ContainsValue(other as RuleTile);  // Là bất kỳ biome trong mappings
            default:
                // Neighbor >= 4 tương ứng với biomeType cụ thể
                foreach (var kvp in _biomeTileCache)
                {
                    if (neighbor == (int)kvp.Key + 4)  // Offset 4 để tránh trùng This/NotThis
                    {
                        return other == kvp.Value || (other is RuleTile rt && rt.name.Contains(kvp.Key.ToString()));
                    }
                }
                return base.RuleMatch(neighbor, other);  // Fallback cho Any/Null
        }
    }

    private void OnValidate()
    {
        _biomeTileCache = null;  // Invalidate cache khi mappings thay đổi
        foreach (var mapping in biomeMappings)
        {
            if (mapping.biomeTile == null)
            {
                Debug.LogWarning($"Biome mapping for {mapping.biomeType} has null RuleTile", this);
            }
        }
    }
}