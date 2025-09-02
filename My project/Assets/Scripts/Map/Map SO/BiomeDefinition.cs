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

[CreateAssetMenu(menuName = "WorldGen/Biome Definition")]
public class BiomeDefinition : ScriptableObject
{
    public BiomeType biomeType;

    [Header("Ground Tile")]
    public TileBase groundTile;

    [Header("Prefab Rules")]
    public List<PrefabSpawnRule> prefabRules = new List<PrefabSpawnRule>();

    [Header("Moisture/Elevation windows (0..1) cho mapping đơn giản")]
    [Range(0, 1)] public float minElevation = 0f;
    [Range(0, 1)] public float maxElevation = 1f;
    [Range(0, 1)] public float minMoisture = 0f;
    [Range(0, 1)] public float maxMoisture = 1f;
}
