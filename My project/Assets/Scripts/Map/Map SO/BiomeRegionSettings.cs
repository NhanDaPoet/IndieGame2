using UnityEngine;

[CreateAssetMenu(menuName = "WorldGen/Biome Region Settings")]
public class BiomeRegionSettings : ScriptableObject
{
    [Header("Kích thước một vùng biome (đơn vị tile)")]
    [Min(8)] public int regionSize = 512;

    [Header("Jitter vị trí tâm vùng (0..1)")]
    [Range(0f, 1f)] public float centerJitter = 0.35f;

    [Header("Độ dày biên chuyển tiếp (tile) - dành cho nâng cấp sau")]
    [Min(0)] public int edgeBlend = 4;

    [Header("Bảng phân bố biome (nếu muốn ép tỷ lệ)")]
    public BiomeType[] biomePalette = {
        BiomeType.Forest, BiomeType.Plains, BiomeType.Desert,
        BiomeType.Mountains, BiomeType.Swamp
    };
}
