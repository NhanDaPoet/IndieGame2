using Mirror;
using UnityEngine;

public class ExplosionAoE : NetworkBehaviour
{
    float damage;
    float radius;
    uint ownerNetId;
    LayerMask damageMask;

    [Header("Lifetime / FX")]
    [SerializeField] float life = 0.7f;         
    [SerializeField] string animState = "Explosion"; 

    [Server]
    public void Setup(float dmg, float r, uint owner, LayerMask mask)
    {
        damage = dmg;
        radius = r;
        ownerNetId = owner;
        damageMask = mask;
    }

    public override void OnStartServer()
    {
        DoDamageOnce();
        Invoke(nameof(Despawn), life);
    }

    void Awake()
    {
        var anim = GetComponent<Animator>();
        if (anim)
        {
            anim.updateMode = AnimatorUpdateMode.Normal;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            anim.Play(animState, 0, 0f);
        }
    }

    [Server]
    void DoDamageOnce()
    {
        var hits = Physics2D.OverlapCircleAll(transform.position, radius, damageMask);
        foreach (var h in hits)
        {
            var ni = h.GetComponentInParent<NetworkIdentity>();
            if (ni && ni.netId == ownerNetId) continue;

            var dmgable = h.GetComponent<IDamageable>();
            if (dmgable != null)
            {
                GameObject ownerGo = null;
                if (NetworkServer.spawned.TryGetValue(ownerNetId, out var ownerNI))
                    ownerGo = ownerNI.gameObject;

                dmgable.TakeDamage(damage, ownerGo);
            }
        }
    }

    [Server]
    void Despawn()
    {
        NetworkServer.Destroy(gameObject);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}
