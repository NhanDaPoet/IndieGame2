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

    // === Helper: chọn tile nền (deterministic) ===
    public TileBase PickGroundTileDeterministic(int worldX, int worldY, int seed)
    {
        // Nếu không có variants -> dùng groundTile
        if (groundVariants == null || groundVariants.Count == 0)
            return groundTile;

        int total = 0;
        for (int i = 0; i < groundVariants.Count; i++)
        {
            if (groundVariants[i].tile != null && groundVariants[i].weight > 0)
                total += groundVariants[i].weight;
        }
        if (total <= 0) return groundTile;

        // deterministic pseudo-random từ seed + tọa độ + biome
        unchecked
        {
            int h = seed;
            h = (h * 397) ^ worldX;
            h = (h * 397) ^ worldY;
            h = (h * 397) ^ (int)biomeType;
            if (h < 0) h = -h;
            int pick = (h % total) + 1;

            int acc = 0;
            for (int i = 0; i < groundVariants.Count; i++)
            {
                var wt = groundVariants[i];
                if (wt.tile == null || wt.weight <= 0) continue;
                acc += wt.weight;
                if (pick <= acc) return wt.tile;
            }
        }

        return groundTile;
    }
}
