using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "LootTable", menuName = "Resource/LootTable")]
public class LootTable : ScriptableObject
{
    [Tooltip("Các dòng loot, có thể lọc theo biome (chuỗi) nếu cần.")]
    public List<LootEntry> entries = new List<LootEntry>();

    [Tooltip("Mỗi cấp độ rarity của Tool (Common=0..Legendary=4) sẽ nhân xác suất thêm theo công thức: chance *= (1 + rarityIndex * bonusPerRarity).")]
    [Range(0f, 0.5f)]
    public float toolRarityBonusPerStep = 0.05f;

    public List<ItemStack> Roll(System.Random rng, string biome, ItemData usingTool)
    {
        var results = new List<ItemStack>();
        int rarityIndex = usingTool != null ? (int)usingTool.rarity : 0;
        float rarityMul = 1f + rarityIndex * toolRarityBonusPerStep;

        foreach (var e in entries)
        {
            if (!string.IsNullOrEmpty(e.biome) && !string.Equals(e.biome, biome, StringComparison.OrdinalIgnoreCase))
                continue;

            float chance = Mathf.Clamp01(e.baseDropChance * rarityMul);
            if (rng.NextDouble() <= chance)
            {
                int qty = UnityEngine.Random.Range(e.minQuantity, e.maxQuantity + 1);
                if (qty > 0 && e.itemData != null)
                {
                    results.Add(new ItemStack(e.itemData.id, qty, e.itemData));
                }
            }
        }
        return results;
    }
}

[Serializable]
public class LootEntry
{
    public ItemData itemData;
    [Min(0)] public int minQuantity = 1;
    [Min(1)] public int maxQuantity = 1;

    [Tooltip("Xác suất rơi cơ bản (0..1). Sẽ được nhân thêm theo độ hiếm của Tool.")]
    [Range(0f, 1f)]
    public float baseDropChance = 1f;

    [Tooltip("Để trống nếu không muốn lọc theo biome.")]
    public string biome = "";
}
