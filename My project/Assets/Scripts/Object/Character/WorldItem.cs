using Mirror;
using System.Collections;
using UnityEngine;

[RequireComponent(typeof(SpriteRenderer), typeof(Collider2D), typeof(Rigidbody2D))]
public class WorldItem : NetworkBehaviour
{
    [Header("Item Data")]
    [SyncVar(hook = nameof(OnItemIdChanged))]
    private int itemId;
    [SyncVar(hook = nameof(OnQuantityChanged))]
    private int quantity;
    private ItemData itemData;

    [Header("Setting UI")]
    [SerializeField] private float bobHeight = 0.2f;
    [SerializeField] private float bobSpeed = 2f;
    [SerializeField] private float rotationSpeed = 30f;

    [Header("Setting Pickup")]
    [SerializeField] private float pickupRange = 1.5f;
    [SerializeField] private float pickupDelay = 0.5f;
    [SerializeField] private LayerMask playerLayer;

    [Header("Setting Physics")]
    [SerializeField] private float dropForce = 3f;
    [SerializeField] private float despawnTime = 300f;

    private SpriteRenderer spriteRenderer;
    private Collider2D itemCollider;
    private Rigidbody2D rb;
    [SerializeField] private ItemDatabase itemDatabase;
    private ItemPoolManager poolManager;

    private Vector3 startPosition;
    private float bobOffset;
    private bool canBePickedUp = false;
    private PlayerMovement targetPlayer;
    private Coroutine despawnCoroutine;

    [SyncVar] private float spawnTime;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        itemCollider = GetComponent<Collider2D>();
        rb = GetComponent<Rigidbody2D>();
        poolManager = FindFirstObjectByType<ItemPoolManager>();

        itemCollider.isTrigger = true;
        rb.gravityScale = 0;
        rb.linearDamping = 2f;

        bobOffset = Random.Range(0f, Mathf.PI * 2f);
    }

    public override void OnStartServer()
    {
        startPosition = transform.position;
        UpdateItemData();
        spawnTime = Time.time;
        StartCoroutine(EnablePickupAfterDelay());
        despawnCoroutine = StartCoroutine(DespawnAfterTime());
    }

    private void Update()
    {
        if (!isServer) return;
        HandleBobAnimation();
        HandleRotation();
    }

    public void Initialize(int id, int qty)
    {
        itemId = id;
        quantity = qty;
        UpdateItemData();
    }

    public int GetItemId() => itemId;

    public void SetItemDatabase(ItemDatabase database)
    {
        itemDatabase = database;
    }

    [Server]
    public static WorldItem SpawnWorldItem(ItemStack itemStack, Vector3 position, Vector3 velocity = default)
    {
        ItemPoolManager poolManager = FindFirstObjectByType<ItemPoolManager>();
        if (poolManager == null)
        {
            Debug.LogError("ItemPoolManager not found!");
            return null;
        }
        GameObject itemGO = poolManager.GetItem(itemStack.itemId, position, itemStack.quantity, velocity);
        return itemGO != null ? itemGO.GetComponent<WorldItem>() : null;
    }

    private void UpdateItemData()
    {
        if (itemDatabase == null) return;
        itemData = itemDatabase.GetItemData(itemId);
        if (itemData != null)
        {
            spriteRenderer.sprite = itemData.icon;
            spriteRenderer.color = Color.white;
        }
        else
        {
            spriteRenderer.sprite = null;
            spriteRenderer.color = Color.clear;
        }
    }

    private void HandleBobAnimation()
    {
        float newY = startPosition.y + Mathf.Sin((Time.time * bobSpeed) + bobOffset) * bobHeight;
        transform.position = new Vector3(transform.position.x, newY, transform.position.z);
    }

    private void HandleRotation()
    {
        transform.Rotate(0, 0, rotationSpeed * Time.deltaTime);
    }

    [Server]
    public void TryPickup(PlayerMovement player)
    {
        if (!canBePickedUp)
        {
            return;
        }
        if (itemId == 0 || quantity <= 0)
        {
            return;
        }
        PlayerInventory inventory = player.GetComponent<PlayerInventory>();
        if (inventory == null)
        {
            return;
        }
        ItemStack stack = new ItemStack(itemId, quantity, itemData);
        bool success = inventory.TryAddItemStack(stack);
        if (success)
        {
            RpcPlayPickupEffect();
            poolManager.ReturnItem(gameObject);
            //CmdPickupItem()
        }
    }

    [Command(requiresAuthority = false)]
    private void CmdPickupItem()
    {
        if (itemId == 0 || quantity <= 0) return;
        RpcPlayPickupEffect();
        poolManager.ReturnItem(gameObject);
    }

    [ClientRpc]
    private void RpcPlayPickupEffect()
    {
        // Loot effect
    }

    [TargetRpc]
    private void TargetShowPickupPrompt(NetworkConnectionToClient target, string itemName)
    {
        if (PlayerPickup.Instance != null)
        {
            PlayerPickup.Instance.ShowPickupPrompt(itemName);
        }
    }

    [TargetRpc]
    private void TargetHidePickupPrompt(NetworkConnectionToClient target)
    {
        if (PlayerPickup.Instance != null)
        {
            PlayerPickup.Instance.HidePickupPrompt();
        }
    }

    public void ApplyDropForce(Vector3 direction)
    {
        if (rb != null)
        {
            Vector3 force = direction.normalized * dropForce;
            rb.AddForce(force, ForceMode2D.Impulse);
        }
    }

    private void OnItemIdChanged(int oldId, int newId)
    {
        itemId = newId;
        UpdateItemData();
    }

    private void OnQuantityChanged(int oldQty, int newQty)
    {
        quantity = newQty;
    }

    private IEnumerator EnablePickupAfterDelay()
    {
        yield return new WaitForSeconds(pickupDelay);
        canBePickedUp = true;
    }

    private IEnumerator DespawnAfterTime()
    {
        yield return new WaitForSeconds(despawnTime);
        if (gameObject != null)
        {
            poolManager.ReturnItem(gameObject);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, pickupRange);
        Gizmos.color = Color.yellow;
    }

    private void OnMouseEnter()
    {
        if (!isServer) ItemTooltip.ShowTooltip(itemData);
    }

    private void OnMouseExit()
    {
        ItemTooltip.HideTooltip();
    }
}