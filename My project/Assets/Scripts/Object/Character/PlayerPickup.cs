using Mirror;
using TMPro;
using UnityEngine;

public class PlayerPickup : NetworkBehaviour
{
    [Header("PickUp Setting")]
    [SerializeField] private float pickupRange = 1.5f;
    [SerializeField] private LayerMask itemLayer;
    [SerializeField] private KeyCode pickupKey = KeyCode.E;

    [Header("UI")]
    [SerializeField] private TextMeshProUGUI pickupPromptText;
    private bool isPromptVisible = false;

    public static PlayerPickup Instance { get; private set; }

    public override void OnStartLocalPlayer()
    {
        if (isLocalPlayer)
        {
            Instance = this;
        }
    }

    private void Start()
    {
        if (pickupPromptText != null)
        {
            pickupPromptText.gameObject.SetActive(false);
        }
    }

    private void Update()
    {
        if (!isLocalPlayer) return;

        if (Input.GetKeyDown(pickupKey))
        {
            TryPickupItem();
        }
    }

    private void TryPickupItem()
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, pickupRange, itemLayer);
        foreach (var hit in hits)
        {
            WorldItem worldItem = hit.GetComponent<WorldItem>();
            if (worldItem != null)
            {
                CmdTryPickupItem(worldItem.netIdentity);
                break;
            }
        }
    }

    [Command(requiresAuthority = false)]
    private void CmdTryPickupItem(NetworkIdentity itemIdentity)
    {
        if (itemIdentity == null)
        {
            return;
        }
        WorldItem worldItem = itemIdentity.GetComponent<WorldItem>();
        if (worldItem != null)
        {
            worldItem.TryPickup(GetComponent<PlayerMovement>());
        }
        else
        {
            Debug.Log("[Server] WorldItem component not found!");
        }
    }

    public void ShowPickupPrompt(string itemName)
    {
        if (pickupPromptText != null && !isPromptVisible)
        {
            pickupPromptText.text = $"Nhấn {pickupKey} để nhặt {itemName}";
            pickupPromptText.gameObject.SetActive(true);
            isPromptVisible = true;
        }
    }

    public void HidePickupPrompt()
    {
        if (pickupPromptText != null && isPromptVisible)
        {
            pickupPromptText.gameObject.SetActive(false);
            isPromptVisible = false;
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.blue;
        Gizmos.DrawWireSphere(transform.position, pickupRange);
    }
}