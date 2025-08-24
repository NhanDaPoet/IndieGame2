using Mirror;
using UnityEngine;

public class ItemPoolSpawner : NetworkBehaviour
{
    [Header("Spawn Settings")]
    [SerializeField] private ItemDatabase itemDatabase;
    [SerializeField] private ItemPoolManager poolManager;
    [SerializeField] private KeyCode spawnKey = KeyCode.F;
    [SerializeField] private Vector3 spawnPosition = Vector3.zero;
    [SerializeField] private bool useCustomSpawnPosition = true;
    [SerializeField] private float dropForce = 3f;

    public override void OnStartServer()
    {
        if (itemDatabase == null || poolManager == null)
        {
            Debug.LogError("ItemDatabase or ItemPoolManager not assigned!");
        }
    }

    void Update()
    {
        if (!isServer || !Input.GetKeyDown(spawnKey)) return;
        SpawnRandomItem();
    }

    [ContextMenu("Spawn Random Item")]
    private void SpawnRandomItem()
    {
        if (!NetworkServer.active) return;
        var items = itemDatabase.GetAllItems();
        if (items.Length == 0)
        {
            Debug.LogWarning("No items in ItemDatabase!");
            return;
        }
        var randomItem = items[Random.Range(0, items.Length)];
        int quantity = Random.Range(1, randomItem.maxStackSize + 1);
        Vector3 spawnPos = useCustomSpawnPosition
            ? spawnPosition
            : transform.position + (Vector3)(Random.insideUnitCircle * 2f);
        Vector3 velocity = Random.insideUnitCircle.normalized * dropForce;
        poolManager.GetItem(randomItem.id, spawnPos, quantity, velocity);
    }
}
