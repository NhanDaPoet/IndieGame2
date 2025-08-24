using Mirror;
using System.Collections.Generic;
using UnityEngine;

public class ItemPoolManager : NetworkBehaviour
{
    [SerializeField] private ItemDatabase itemDatabase;
    [SerializeField] private GameObject itemDropPrefab; // Reference to ItemDrop Prefab
    [SerializeField] private int poolSizePerMaterial = 30; // Pool size for Material
    [SerializeField] private int poolSizePerOther = 10; // Pool size for Tool/Weapon/Consumable
    private Dictionary<int, Queue<GameObject>> itemPools = new Dictionary<int, Queue<GameObject>>();

    public override void OnStartServer()
    {
        if (itemDatabase == null || itemDropPrefab == null)
        {
            Debug.LogError("ItemDatabase or ItemDropPrefab not assigned in ItemPoolManager!");
            return;
        }
        InitializePool();
    }

    [Server]
    private void InitializePool()
    {
        foreach (var itemData in itemDatabase.GetAllItems())
        {
            Queue<GameObject> pool = new Queue<GameObject>();
            int poolSize = itemData.itemType == ItemType.Material ? poolSizePerMaterial : poolSizePerOther;
            for (int i = 0; i < poolSize; i++)
            {
                GameObject itemObj = Instantiate(itemDropPrefab);
                itemObj.SetActive(false);
                WorldItem worldItem = itemObj.GetComponent<WorldItem>();
                if (worldItem != null)
                {
                    worldItem.SetItemDatabase(itemDatabase);
                    worldItem.Initialize(itemData.id, 1); // Default initialization
                }
                pool.Enqueue(itemObj);
            }
            itemPools[itemData.id] = pool;
        }
    }

    [Server]
    public GameObject GetItem(int itemId, Vector3 position, int quantity, Vector3 velocity = default)
    {
        if (!itemPools.ContainsKey(itemId))
        {
            Debug.LogError($"No pool for item ID: {itemId}");
            return null;
        }

        GameObject itemObj;
        if (itemPools[itemId].Count > 0)
        {
            itemObj = itemPools[itemId].Dequeue();
        }
        else
        {
            itemObj = Instantiate(itemDropPrefab);
            WorldItem worldItem = itemObj.GetComponent<WorldItem>();
            if (worldItem != null)
            {
                worldItem.SetItemDatabase(itemDatabase);
                worldItem.Initialize(itemId, 1);
            }
        }

        itemObj.SetActive(true);
        itemObj.transform.position = position;
        WorldItem worldItemComponent = itemObj.GetComponent<WorldItem>();
        worldItemComponent.Initialize(itemId, quantity);
        if (velocity != Vector3.zero)
        {
            worldItemComponent.ApplyDropForce(velocity);
        }
        NetworkServer.Spawn(itemObj);
        return itemObj;
    }

    [Server]
    public void ReturnItem(GameObject itemObj)
    {
        WorldItem worldItem = itemObj.GetComponent<WorldItem>();
        if (worldItem == null) return;
        int itemId = worldItem.GetItemId();
        if (!itemPools.ContainsKey(itemId))
        {
            Debug.LogError($"No pool for item ID: {itemId}");
            return;
        }
        NetworkServer.UnSpawn(itemObj);
        itemObj.SetActive(false);
        itemPools[itemId].Enqueue(itemObj);
    }
}
