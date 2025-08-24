using Mirror;
using UnityEngine;

[RequireComponent(typeof(PlayerInventory))]
[RequireComponent(typeof(PlayerMovement))]
public class PlayerCombat : NetworkBehaviour
{
    [Header("Melee Settings")]
    [SerializeField] private LayerMask enemyMask;  
    [SerializeField] private string enemyTag = "Enemy";
    [SerializeField] private float baseDamage = 20f;
    [SerializeField] private float attackRange = 1.6f; 
    [SerializeField] private float attackArc = 100f;  
    [SerializeField] private float attackCooldown = 0.45f;

    [Header("FX/Anim (optional)")]
    [SerializeField] private Transform fxSpawn;
    [SerializeField] private GameObject slashFxPrefab;

    private PlayerInventory inv;
    private PlayerMovement move;
    private Animator animator;
    private double lastAttackTime; 

    void Awake()
    {
        inv = GetComponent<PlayerInventory>();
        move = GetComponent<PlayerMovement>();
        animator = GetComponentInChildren<Animator>();
    }

    void Update()
    {
        if (!isLocalPlayer) return;
        if (Input.GetMouseButtonDown(0))
        {
            var held = inv != null ? inv.GetSelectedHotbarItem() : default;
            if (!held.IsEmpty && held.itemData != null && held.itemData.itemType == ItemType.Weapon)
            {
                Vector2 face = move != null ? move.FacingDirection : Vector2.down; // đã dùng logic tương tự khi gather node
                if (face.sqrMagnitude < 0.0001f) face = Vector2.down;
                CmdMeleeAttack(face);
            }
        }
    }

    [Command]
    private void CmdMeleeAttack(Vector2 faceDir)
    {
        if (NetworkTime.time < lastAttackTime + attackCooldown) return;
        var held = inv != null ? inv.GetSelectedHotbarItem() : default;
        if (held.IsEmpty || held.itemData == null || held.itemData.itemType != ItemType.Weapon) return;

        lastAttackTime = NetworkTime.time;

        Vector2 origin = transform.position;
        var cols = Physics2D.OverlapCircleAll(origin, attackRange, enemyMask);
        foreach (var c in cols)
        {
            if (!string.IsNullOrEmpty(enemyTag) && !c.CompareTag(enemyTag)) continue;

            Vector2 to = (Vector2)c.transform.position - origin;
            if (to.sqrMagnitude < 0.0001f) continue;

            float ang = Vector2.Angle(faceDir.normalized, to.normalized);
            if (ang <= attackArc * 0.5f)
            {
                var dmgable = c.GetComponentInParent<IDamageable>();
                if (dmgable != null && !dmgable.IsDead)
                {
                    dmgable.TakeDamage(baseDamage, gameObject);
                }
            }
        }
        RpcPlayMeleeFx(faceDir);
    }

    [ClientRpc]
    private void RpcPlayMeleeFx(Vector2 faceDir)
    {
        if (animator) animator.SetTrigger("Attack"); 
        if (slashFxPrefab != null)
        {
            Vector3 pos = fxSpawn ? fxSpawn.position : (Vector3)transform.position + (Vector3)(faceDir.normalized * 0.8f);
            float zRot = Mathf.Atan2(faceDir.y, faceDir.x) * Mathf.Rad2Deg;
            GameObject fx = Object.Instantiate(slashFxPrefab, pos, Quaternion.Euler(0, 0, zRot));
            Object.Destroy(fx, 1.5f);
        }
    }
}
