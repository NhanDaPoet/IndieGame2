using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "New Item", menuName = "Inventory/Item")]
public class ItemData : ScriptableObject
{
    public int id;
    public string itemName;
    public Sprite icon;
    public string material; 
    public int maxStackSize = 64;
    public ItemType itemType;
    public ItemRarity rarity; 
    public string description;

    [Header("Enchant/Catalyst")]
    [Tooltip("Nếu là catalyst dùng để enchant (thay XP).")]
    public bool isCatalyst = false;

    [Tooltip("Bậc catalyst (càng cao cho phép roll level enchant cao hơn).")]
    public int catalystTier = 0;

    [Tooltip("Số socket tối đa cho item này (mặc định theo rarity). -1 để dùng mặc định theo rarity.")]
    public int overrideMaxSockets = -1;

    [Header("Fuel (Smelting)")]
    [Tooltip("Nếu >0, item là fuel với thời lượng cháy này.")]
    public float fuelBurnTime = 0f;
}
public enum ItemType
{
    Weapon,
    Tool,
    Consumable,
    Material
}
public enum ItemRarity
{
    Common,
    Uncommon,
    Rare,
    Epic,
    Legendary
}

public enum ResourceKind 
{ 
    Ore, 
    Tree, 
    Crop, 
    FoodPlant 
}
public enum GatherMode 
{ 
    Solo, 
    Shared 
}

[System.Serializable]
public struct ItemStack
{
    public int itemId;
    public int quantity;
    public ItemData itemData;

    public List<EnchantmentInstance> enchantments;

    public ItemStack(int id, int qty, ItemData data = null)
    {
        itemId = id;
        quantity = qty;
        itemData = data;
        enchantments = null;
    }
    //TODO : Enchant, upgrade -> crafting table, enchant table, npc trade
    public bool IsEmpty => itemId == 0 || quantity <= 0;
    public bool CanStackWith(ItemStack other)
    {
        if (itemId != other.itemId) return false;
        bool selfHasEnchant = enchantments != null && enchantments.Count > 0;
        bool otherHasEnchant = other.enchantments != null && other.enchantments.Count > 0;
        if (selfHasEnchant || otherHasEnchant) return false;
        return true;
    }

    public int GetMaxStackSize() => itemData?.maxStackSize ?? 64;

    public ItemStack Split(int amount)
    {
        int splitAmount = Mathf.Min(amount, quantity);
        quantity -= splitAmount;
        var split = new ItemStack(itemId, splitAmount, itemData);
        return split;
    }

    public void Clear()
    {
        itemId = 0;
        quantity = 0;
        itemData = null;
        enchantments = null;
    }
}

public static class ToolTags
{
    public const string Axe = "Axe";
    public const string Pickaxe = "Pickaxe";
    public const string Sickle = "Sickle";
}