using Mirror;
using UnityEngine;

[RequireComponent(typeof(PlayerMovement))]
public class ResourceInteractor : NetworkBehaviour
{
    [SerializeField] private float interactRange = 2.5f;
    [SerializeField] private LayerMask resourceLayer;

    private PlayerMovement movement;

    void Awake()
    {
        movement = GetComponent<PlayerMovement>();
    }

    // ---------- CLIENT API ----------
    [Client]
    public void ClientStartGather(ResourceNodeBase node)
    {
        if (!isLocalPlayer || node == null) return;
        var ni = node.netIdentity;
        if (ni == null) return;
        CmdStartGatherOnServer(ni.netId);
    }

    [Client]
    public void ClientStopGather(ResourceNodeBase node)
    {
        if (!isLocalPlayer || node == null) return;
        var ni = node.netIdentity;
        if (ni == null) return;
        CmdStopGatherOnServer(ni.netId);
    }

    [Client]
    public void ClientHit(ResourceNodeBase node)
    {
        if (!isLocalPlayer || node == null) return;
        var ni = node.netIdentity;
        if (ni == null) return;
        CmdHitOnServer(ni.netId);
    }

    [Client]
    public ResourceNodeBase FindNodeInFront()
    {
        if (!isLocalPlayer) return null;
        Vector2 origin = transform.position;
        Vector2 dir = movement != null ? movement.FacingDirection : Vector2.down;

        RaycastHit2D hit = Physics2D.Raycast(origin, dir, interactRange, resourceLayer);
        if (hit.collider != null)
        {
            return hit.collider.GetComponent<ResourceNodeBase>();
        }
        return null;
    }

    // ---------- SERVER COMMANDS (Player has authority) ----------
    [Command]
    private void CmdStartGatherOnServer(uint nodeNetId)
    {
        var node = FindNode(nodeNetId);
        if (node == null) return;
        node.ServerStartGather(movement);
    }

    [Command]
    private void CmdStopGatherOnServer(uint nodeNetId)
    {
        var node = FindNode(nodeNetId);
        if (node == null) return;
        node.ServerStopGather(movement);
    }

    [Command]
    private void CmdHitOnServer(uint nodeNetId)
    {
        var node = FindNode(nodeNetId);
        if (node == null)
        {
            return;
        }
        node.ServerHit(movement);
    }

    // ---------- SERVER UTILS ----------
    [Server]
    private ResourceNodeBase FindNode(uint netId)
    {
        if (netId == 0) return null;
        if (NetworkServer.spawned.TryGetValue(netId, out var identity))
            return identity.GetComponent<ResourceNodeBase>();
        return null;
    }

    private void OnDrawGizmosSelected()
    {
        if (movement == null) movement = GetComponent<PlayerMovement>();
        Vector3 dir = movement != null ? (Vector3)movement.FacingDirection : Vector3.down;
        Gizmos.color = Color.magenta;
        Gizmos.DrawRay(transform.position, dir * interactRange);
    }
}
