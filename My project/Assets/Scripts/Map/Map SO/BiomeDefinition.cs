using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;
using static System.TimeZoneInfo;

[Serializable]
public struct PrefabSpawnRule
{
    [Tooltip("Khóa trùng với PrefabRegistry (ví dụ: 'tree_oak', 'rock_small')")]
    public string prefabKey;

    [Tooltip("Số lượng mục tiêu mỗi CHUNK (trung bình).")]
    public int targetCountPerChunk;

    [Tooltip("Khoảng cách tối thiểu giữa 2 spawn cùng loại (đơn vị tile, Manhattan).")]
    public int minSpacing;

    [Tooltip("Spawn dạng cụm?")]
    public bool cluster;
    [Tooltip("Số lượng trong 1 cụm (nếu cluster)")]
    public Vector2Int clusterCountRange;
    [Tooltip("Bán kính cụm (tile)")]
    public int clusterRadius;

    [Header("Border Spawning")]
    [Tooltip("Prefab này chỉ spawn ở border?")]
    public bool borderOnly;
    [Tooltip("Prefab này không spawn ở border?")]
    public bool avoidBorder;
}

[Serializable]
public struct WeightedTile
{
    [Tooltip("Tile hoặc RuleTile cho ground hoặc border. RuleTile tự động blend dựa trên neighbors.")]
    public TileBase tile;
    [Range(1, 100)] public int weight;
}

[Serializable]
public struct BiomeBorderTiles
{
    [Tooltip("Biome kề bên để apply border tiles")]
    public BiomeType neighborBiome;
    [Tooltip("Border tiles hoặc RuleTile khi kề với biome này")]
    public List<WeightedTile> borderTiles;
}

[System.Serializable]
public struct BiomeTransition
{
    public BiomeType neighborBiome;  
    public List<TransitionTile> transitionTiles;
    public List<CornerTransitionTile> cornerTransitionTiles;
}

[System.Serializable]
public struct TransitionTile
{
    public Vector2Int direction; 
    public List<WeightedTile> tiles;  
}

[System.Serializable]
public struct CornerTransitionTile
{
    public Vector2Int cornerDirection;  
    public List<WeightedTile> cornerTiles;  
}

[CreateAssetMenu(menuName = "WorldGen/Biome Definition")]
public class BiomeDefinition : ScriptableObject
{
    public BiomeType biomeType;

    [Header("Ground Tile (legacy - dùng nếu không có variants)")]
    public TileBase groundTile;

    [Header("Ground Variants (ưu tiên dùng nếu có phần tử)")]
    public List<WeightedTile> groundVariants = new List<WeightedTile>();

    [Header("Border Tiles - Ranh giới với biome khác")]
    public List<WeightedTile> generalBorderTiles = new List<WeightedTile>();

    [Header("Transition Tiles")]
    public List<BiomeTransition> biomeTransitions = new List<BiomeTransition>();

    public List<BiomeBorderTiles> specificBorderTiles = new List<BiomeBorderTiles>();

    public List<PrefabSpawnRule> prefabRules = new List<PrefabSpawnRule>();

    [Header("Border Settings")]
    [Range(0f, 1f)]
    public float borderTileThreshold = 0.3f;

    [Header("Moisture/Elevation windows (0..1) cho mapping đơn giản")]
    [Range(0, 1)] public float minElevation = 0f;
    [Range(0, 1)] public float maxElevation = 1f;
    [Range(0, 1)] public float minMoisture = 0f;
    [Range(0, 1)] public float maxMoisture = 1f;

    [System.NonSerialized] private bool _cacheBuilt = false;
    [System.NonSerialized] private int _totalWeight = 0;
    [System.NonSerialized] private WeightedTile[] _validTiles;
    [System.NonSerialized] private int[] _cumulativeWeights;

    [System.NonSerialized] private int _borderTotalWeight = 0;
    [System.NonSerialized] private WeightedTile[] _validBorderTiles;
    [System.NonSerialized] private int[] _borderCumulativeWeights;
    [System.NonSerialized] private Dictionary<BiomeType, (WeightedTile[], int[], int)> _specificBorderCache;

    [System.NonSerialized] private readonly Dictionary<int, TileBase> _tileCache = new();

    private void BuildTileCache()
    {
        if (_cacheBuilt) return;

        // Build normal tiles cache
        BuildNormalTileCache();

        // Build border tiles cache
        BuildBorderTileCache();

        // Build specific border tiles cache
        BuildSpecificBorderCache();

        _cacheBuilt = true;
    }

    private void BuildNormalTileCache()
    {
        var validTilesList = new List<WeightedTile>();
        var cumulativeWeightsList = new List<int>();
        int currentWeight = 0;
        foreach (var wt in groundVariants)
        {
            if (wt.tile != null && wt.weight > 0)
            {
                validTilesList.Add(wt);
                currentWeight += wt.weight;
                cumulativeWeightsList.Add(currentWeight);
            }
        }
        _validTiles = validTilesList.ToArray();
        _cumulativeWeights = cumulativeWeightsList.ToArray();
        _totalWeight = currentWeight;
    }

    private void BuildBorderTileCache()
    {
        var validBorderTilesList = new List<WeightedTile>();
        var borderCumulativeWeightsList = new List<int>();
        int currentBorderWeight = 0;

        foreach (var wt in generalBorderTiles)
        {
            if (wt.tile != null && wt.weight > 0)
            {
                validBorderTilesList.Add(wt);
                currentBorderWeight += wt.weight;
                borderCumulativeWeightsList.Add(currentBorderWeight);
            }
        }

        _validBorderTiles = validBorderTilesList.ToArray();
        _borderCumulativeWeights = borderCumulativeWeightsList.ToArray();
        _borderTotalWeight = currentBorderWeight;
    }

    private void BuildSpecificBorderCache()
    {
        _specificBorderCache = new Dictionary<BiomeType, (WeightedTile[], int[], int)>();

        foreach (var borderDef in specificBorderTiles)
        {
            var validTiles = new List<WeightedTile>();
            var cumulativeWeights = new List<int>();
            int currentWeight = 0;

            foreach (var wt in borderDef.borderTiles)
            {
                if (wt.tile != null && wt.weight > 0)
                {
                    validTiles.Add(wt);
                    currentWeight += wt.weight;
                    cumulativeWeights.Add(currentWeight);
                }
            }

            if (currentWeight > 0)
            {
                _specificBorderCache[borderDef.neighborBiome] = (
                    validTiles.ToArray(),
                    cumulativeWeights.ToArray(),
                    currentWeight
                );
            }
        }
    }

    public TileBase PickGroundTileWithBlend(int worldX, int worldY, int seed, BiomeService.BiomeBlendData blendData)
    {
        if (!_cacheBuilt)
        {
            BuildTileCache();
        }

        int cacheKey = HashPosition(worldX, worldY, seed, blendData.isBorder ? 1 : 0, (int)blendData.primaryBiome);

        if (_tileCache.TryGetValue(cacheKey, out var cachedTile))
        {
            return cachedTile;
        }

        TileBase selectedTile = null;

        // Kiểm tra biên giới và chọn transition tile cho các biome liền kề
        if (blendData.isBorder && blendData.blendFactor >= borderTileThreshold)
        {
            // Lặp qua các biome liền kề để tìm TransitionTile và CornerTransitionTile phù hợp
            foreach (var neighborBiome in blendData.neighborBiomes)
            {
                var transition = biomeTransitions.Find(b => b.neighborBiome == neighborBiome);
                    // Chọn transition tile từ BiomeTransition
                    selectedTile = SelectTransitionTile(transition.transitionTiles, blendData.direction);

                    // Nếu không tìm thấy transition tile, kiểm tra CornerTransitionTile
                    if (selectedTile == null)
                    {
                        selectedTile = SelectCornerTransitionTile(transition.cornerTransitionTiles, blendData.cornerDirection);
                    }

                    if (selectedTile != null)
                    {
                        break; // Nếu đã tìm thấy tile, dừng vòng lặp
                    }
            }
        }
        if (selectedTile == null)
        {
            selectedTile = groundTile;
        }

        _tileCache[cacheKey] = selectedTile;
        return selectedTile;
    }

    private TileBase SelectTransitionTile(List<TransitionTile> transitionTiles, Vector2Int direction)
    {
        foreach (var transition in transitionTiles)
        {
            if (transition.direction == direction)
            {
                return SelectTileByWeight(direction.GetHashCode(), transition.tiles.ToArray(),
                                          transition.tiles.Select(t => t.weight).ToArray(),
                                          transition.tiles.Sum(t => t.weight));
            }
        }
        return null; 
    }


    private TileBase SelectCornerTransitionTile(List<CornerTransitionTile> cornerTransitionTiles, Vector2Int cornerDirection)
    {
        foreach (var corner in cornerTransitionTiles)
        {
            if (corner.cornerDirection == cornerDirection)
            {
                return SelectTileByWeight(cornerDirection.GetHashCode(), corner.cornerTiles.ToArray(),
                                          corner.cornerTiles.Select(t => t.weight).ToArray(),
                                          corner.cornerTiles.Sum(t => t.weight));
            }
        }
        return null; 
    }

    public TileBase PickGroundTileDeterministic(int worldX, int worldY, int seed)
    {
        var dummyBlend = new BiomeService.BiomeBlendData
        {
            primaryBiome = biomeType,
            blendFactor = 0f,
            isBorder = false
        };

        return PickGroundTileWithBlend(worldX, worldY, seed, dummyBlend);
    }

    private int HashPosition(int worldX, int worldY, int seed, int borderFlag, int secondaryBiome)
    {
        unchecked
        {
            int h = seed;
            h = (h * 397) ^ worldX;
            h = (h * 397) ^ worldY;
            h = (h * 397) ^ (int)biomeType;
            h = (h * 397) ^ borderFlag;
            h = (h * 397) ^ secondaryBiome;
            return h;
        }
    }
    //TODO : Tile Layer

    private TileBase SelectTileByWeight(int hash, WeightedTile[] tiles, int[] cumulativeWeights, int totalWeight)
    {
        if (hash < 0) hash = -hash;
        int pick = (hash % totalWeight) + 1;

        int left = 0;
        int right = cumulativeWeights.Length - 1;
        while (left <= right)
        {
            int mid = (left + right) / 2;
            if (pick <= cumulativeWeights[mid])
            {
                if (mid == 0 || pick > cumulativeWeights[mid - 1])
                {
                    return tiles[mid].tile;
                }
                right = mid - 1;
            }
            else
            {
                left = mid + 1;
            }
        }
        return tiles[0].tile;
    }

    public bool ValidateDefinition(out string errorMessage)
    {
        errorMessage = "";
        if (groundTile == null && (groundVariants == null || groundVariants.Count == 0))
        {
            errorMessage = "No ground tiles specified";
            return false;
        }

        if (groundVariants != null && groundVariants.Count > 0)
        {
            int validTiles = 0;
            foreach (var wt in groundVariants)
            {
                if (wt.tile != null && wt.weight > 0)
                {
                    validTiles++;
                }
            }
            if (validTiles == 0)
            {
                errorMessage = "No valid weighted tiles (null tiles or zero weights)";
                return false;
            }
        }

        // Validate border tiles
        if (generalBorderTiles != null && generalBorderTiles.Count > 0)
        {
            foreach (var wt in generalBorderTiles)
            {
                if (wt.tile == null)
                {
                    errorMessage = "Border tiles contain null references";
                    return false;
                }
            }
        }

        if (prefabRules != null)
        {
            for (int i = 0; i < prefabRules.Count; i++)
            {
                var rule = prefabRules[i];
                if (string.IsNullOrEmpty(rule.prefabKey))
                {
                    errorMessage = $"Prefab rule {i} has empty prefabKey";
                    return false;
                }
                if (rule.targetCountPerChunk < 0)
                {
                    errorMessage = $"Prefab rule {i} has negative targetCountPerChunk";
                    return false;
                }
                if (rule.cluster && rule.clusterRadius <= 0)
                {
                    errorMessage = $"Prefab rule {i} is cluster but has invalid radius";
                    return false;
                }
            }
        }
        return true;
    }

    public void InvalidateCache()
    {
        _cacheBuilt = false;
        _tileCache.Clear();
    }

    public void LogCacheStats()
    {
        Debug.Log($"Biome {biomeType} cache stats: " +
                 $"Built: {_cacheBuilt}, " +
                 $"Valid tiles: {_validTiles?.Length ?? 0}, " +
                 $"Border tiles: {_validBorderTiles?.Length ?? 0}, " +
                 $"Specific borders: {_specificBorderCache?.Count ?? 0}, " +
                 $"Total weight: {_totalWeight}, " +
                 $"Border weight: {_borderTotalWeight}, " +
                 $"Cached results: {_tileCache.Count}");
    }

    private void OnValidate()
    {
        InvalidateCache();
        if (ValidateDefinition(out string error))
        {
            Debug.Log($"Biome {biomeType} validation passed");
        }
        else
        {
            Debug.LogError($"Biome {biomeType} validation failed: {error}", this);
        }
    }

    private void OnDestroy()
    {
        _tileCache?.Clear();
    }
}