using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

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
}

[Serializable]
public struct WeightedTile
{
    public TileBase tile;
    [Range(1, 100)] public int weight;
}

[CreateAssetMenu(menuName = "WorldGen/Biome Definition")]
public class BiomeDefinition : ScriptableObject
{
    public BiomeType biomeType;

    [Header("Ground Tile (legacy - dùng nếu không có variants)")]
    public TileBase groundTile;

    [Header("Ground Variants (ưu tiên dùng nếu có phần tử)")]
    public List<WeightedTile> groundVariants = new List<WeightedTile>();

    [Header("Prefab Rules")]
    public List<PrefabSpawnRule> prefabRules = new List<PrefabSpawnRule>();

    [Header("Moisture/Elevation windows (0..1) cho mapping đơn giản")]
    [Range(0, 1)] public float minElevation = 0f;
    [Range(0, 1)] public float maxElevation = 1f;
    [Range(0, 1)] public float minMoisture = 0f;
    [Range(0, 1)] public float maxMoisture = 1f;

    [System.NonSerialized] private bool _cacheBuilt = false;
    [System.NonSerialized] private int _totalWeight = 0;
    [System.NonSerialized] private WeightedTile[] _validTiles;
    [System.NonSerialized] private int[] _cumulativeWeights;

    [System.NonSerialized] private readonly Dictionary<int, TileBase> _tileCache = new();

    private void BuildTileCache()
    {
        if (_cacheBuilt) return;
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
        _cacheBuilt = true;
    }

    /// <summary>
    /// Optimized deterministic tile picking với caching
    /// </summary>
    public TileBase PickGroundTileDeterministic(int worldX, int worldY, int seed)
    {
        if (!_cacheBuilt)
        {
            BuildTileCache();
        }
        if (_validTiles == null || _validTiles.Length == 0 || _totalWeight <= 0)
        {
            return groundTile;
        }
        int cacheKey = HashPosition(worldX, worldY, seed);
        if (_tileCache.TryGetValue(cacheKey, out var cachedTile))
        {
            return cachedTile;
        }
        var selectedTile = SelectTileByWeight(cacheKey);
        if (_tileCache.Count > 10000)
        {
            _tileCache.Clear();
        }
        _tileCache[cacheKey] = selectedTile;

        return selectedTile;
    }

    private int HashPosition(int worldX, int worldY, int seed)
    {
        unchecked
        {
            int h = seed;
            h = (h * 397) ^ worldX;
            h = (h * 397) ^ worldY;
            h = (h * 397) ^ (int)biomeType;
            return h;
        }
    }

    private TileBase SelectTileByWeight(int hash)
    {
        if (hash < 0) hash = -hash;
        int pick = (hash % _totalWeight) + 1;
        int left = 0;
        int right = _cumulativeWeights.Length - 1;
        while (left <= right)
        {
            int mid = (left + right) / 2;
            if (pick <= _cumulativeWeights[mid])
            {
                if (mid == 0 || pick > _cumulativeWeights[mid - 1])
                {
                    return _validTiles[mid].tile;
                }
                right = mid - 1;
            }
            else
            {
                left = mid + 1;
            }
        }
        return _validTiles[0].tile;
    }

    /// <summary>
    /// Validate biome definition để catch setup errors
    /// </summary>
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

    /// <summary>
    /// Invalidate cache - gọi khi thay đổi settings
    /// </summary>
    public void InvalidateCache()
    {
        _cacheBuilt = false;
        _tileCache.Clear();
    }

    /// <summary>
    /// Get cache stats cho debugging
    /// </summary>
    public void LogCacheStats()
    {
        Debug.Log($"Biome {biomeType} cache stats: " +
                 $"Built: {_cacheBuilt}, " +
                 $"Valid tiles: {_validTiles?.Length ?? 0}, " +
                 $"Total weight: {_totalWeight}, " +
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