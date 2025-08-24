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

    public ItemStack(int id, int qty, ItemData data = null)
    {
        itemId = id;
        quantity = qty;
        itemData = data;
    }

    public bool IsEmpty => itemId == 0 || quantity <= 0;
    public bool CanStackWith(ItemStack other) => itemId == other.itemId && itemData == other.itemData;
    public int GetMaxStackSize() => itemData?.maxStackSize ?? 64;

    public ItemStack Split(int amount)
    {
        int splitAmount = Mathf.Min(amount, quantity);
        quantity -= splitAmount;
        return new ItemStack(itemId, splitAmount, itemData);
    }

    public void Clear()
    {
        itemId = 0;
        quantity = 0;
        itemData = null;
    }
}

public static class ToolTags
{
    public const string Axe = "Axe";
    public const string Pickaxe = "Pickaxe";
    public const string Sickle = "Sickle";
}