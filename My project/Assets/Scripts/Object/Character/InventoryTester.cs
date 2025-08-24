using UnityEngine;

public class InventoryTester : MonoBehaviour
{
    [SerializeField] private ItemDatabase itemDatabase;
    [SerializeField] private KeyCode testKey = KeyCode.T;

    private void Update()
    {
        if (Input.GetKeyDown(testKey))
        {
            var items = itemDatabase.GetAllItems();
            if (items.Length > 0)
            {
                var randomItem = items[Random.Range(0, items.Length)];
                var stack = new ItemStack(randomItem.id, 1, randomItem);

                var player = FindFirstObjectByType<PlayerInventory>();
                if (player != null)
                {
                    player.TryAddItemStack(stack);
                }
            }
        }
    }
}
