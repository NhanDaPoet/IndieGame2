using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "Item Database", menuName = "Inventory/Item Database")]
public class ItemDatabase : ScriptableObject
{
    [SerializeField] private ItemData[] items;
    private Dictionary<int, ItemData> itemLookup;

    private void OnEnable()
    {
        BuildLookup();
    }

    private void BuildLookup()
    {
        itemLookup = new Dictionary<int, ItemData>();
        foreach (var item in items)
        {
            if (!itemLookup.ContainsKey(item.id))
                itemLookup.Add(item.id, item);
        }
    }

    public ItemData GetItemData(int id)
    {
        if (itemLookup == null) BuildLookup();
        return itemLookup.TryGetValue(id, out ItemData item) ? item : null;
    }
    public ItemData[] GetAllItems() => items;
}
