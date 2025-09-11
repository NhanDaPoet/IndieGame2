using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

[Serializable]
public struct WeightedTile
{
    [Tooltip("Tile cho ground.")]
    public TileBase tile;
    [Range(1, 100)] public int weight;
}

[Serializable]
public struct PrefabSpawnRule
{
    [Tooltip("Khóa trùng với PrefabRegistry")]
    public string prefabKey;
    [Tooltip("Số lượng mục tiêu mỗi CHUNK")]
    public int targetCountPerChunk;
    [Tooltip("Khoảng cách tối thiểu giữa 2 spawn")]
    public int minSpacing;
    [Tooltip("Spawn dạng cụm?")]
    public bool cluster;
    [Tooltip("Số lượng trong 1 cụm")]
    public Vector2Int clusterCountRange;
    [Tooltip("Bán kính cụm")]
    public int clusterRadius;
    [Tooltip("Chỉ spawn ở border?")]
    public bool borderOnly;
    [Tooltip("Không spawn ở border?")]
    public bool avoidBorder;
}

[CreateAssetMenu(menuName = "WorldGen/Biome Definition")]
public class BiomeDefinition : ScriptableObject
{
    [Header("Basic Properties")]
    public BiomeType biomeType;

    [Header("Sorting Weight - Higher values render on top")]
    [Tooltip("Biome weight để xác định thứ tự render. Càng cao càng đè lên trên.")]
    [Range(0, 100)]
    public int sortingWeight = 50;

    [Header("Base Ground Tiles")]
    [Tooltip("Main tile - thường là RuleTile để tự động xử lý biên")]
    public TileBase baseTile;

    [Tooltip("Variants của base tile với weight khác nhau")]
    public List<WeightedTile> baseVariants = new List<WeightedTile>();

    [Header("Prefab Rules")]
    public List<PrefabSpawnRule> prefabRules = new List<PrefabSpawnRule>();

    [Header("Environment Properties")]
    [Range(0, 1)] public float minElevation = 0f;
    [Range(0, 1)] public float maxElevation = 1f;
    [Range(0, 1)] public float minMoisture = 0f;
    [Range(0, 1)] public float maxMoisture = 1f;

    // Cache system
    [System.NonSerialized] private bool _cacheBuilt = false;
    [System.NonSerialized] private int _baseTotalWeight = 0;
    [System.NonSerialized] private WeightedTile[] _validBaseTiles;
    [System.NonSerialized] private int[] _baseCumulativeWeights;
    [System.NonSerialized] private readonly Dictionary<int, TileBase> _baseTileCache = new();

    private void BuildCache()
    {
        if (_cacheBuilt) return;

        BuildBaseTileCache();
        _cacheBuilt = true;
    }

    private void BuildBaseTileCache()
    {
        var validTilesList = new List<WeightedTile>();
        var cumulativeWeightsList = new List<int>();
        int currentWeight = 0;

        foreach (var wt in baseVariants)
        {
            if (wt.tile != null && wt.weight > 0)
            {
                validTilesList.Add(wt);
                currentWeight += wt.weight;
                cumulativeWeightsList.Add(currentWeight);
            }
        }

        _validBaseTiles = validTilesList.ToArray();
        _baseCumulativeWeights = cumulativeWeightsList.ToArray();
        _baseTotalWeight = currentWeight;
    }

    /// <summary>
    /// Pick tile for this biome at given position
    /// </summary>
    public TileBase PickTile(int worldX, int worldY, int seed)
    {
        if (!_cacheBuilt)
        {
            BuildCache();
        }

        int cacheKey = HashPosition(worldX, worldY, seed);

        if (_baseTileCache.TryGetValue(cacheKey, out var cachedTile))
        {
            return cachedTile;
        }

        TileBase selectedTile = null;

        // Use variants if available, otherwise use base tile
        if (_validBaseTiles != null && _validBaseTiles.Length > 0 && _baseTotalWeight > 0)
        {
            selectedTile = SelectTileByWeight(cacheKey, _validBaseTiles, _baseCumulativeWeights, _baseTotalWeight);
        }
        else
        {
            selectedTile = baseTile;
        }

        // Cache management
        if (_baseTileCache.Count > 10000)
        {
            _baseTileCache.Clear();
        }
        _baseTileCache[cacheKey] = selectedTile;

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

    public void InvalidateCache()
    {
        _cacheBuilt = false;
        _baseTileCache.Clear();
    }

    public bool ValidateDefinition(out string errorMessage)
    {
        errorMessage = "";

        if (baseTile == null && (baseVariants == null || baseVariants.Count == 0))
        {
            errorMessage = "No base tiles specified";
            return false;
        }

        return true;
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
        _baseTileCache?.Clear();
    }
}