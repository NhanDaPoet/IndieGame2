using Mirror;
using UnityEngine;

[RequireComponent(typeof(CircleCollider2D))]
[RequireComponent(typeof(Rigidbody2D))]
public class MissileProjectile : NetworkBehaviour
{
    [Header("Flight")]
    [SyncVar] private Vector2 startPos;
    [SyncVar] private Vector2 targetPos;
    [SyncVar] private float startTime;
    [SyncVar] private float flightTime = 1.2f;
    [SyncVar] private float arcHeight = 2.0f; 

    [Header("Damage/Explode")]
    [SyncVar] private float damage = 20f;
    [SyncVar] private float explosionRadius = 1.8f;
    [SyncVar] private uint ownerNetId;

    [Header("Collision/FX")]
    public LayerMask explodeOnMask = ~0;   
    public float armDelay = 0.05f;
    public LayerMask obstacleMask;
    [SerializeField] private GameObject explosionPrefab;

    [Header("Fake Height")]
    [SerializeField] private Vector2 heightAxis = new Vector2(0f, 1f);
    [SerializeField, Range(0f, 1f)] private float sideArcStrength = 0.8f;
    [SerializeField] private bool invertSide = false;
    [SerializeField] private float arcByDistanceRef = 6f; 
    [SerializeField] private float arcByDistanceMin = 0.6f;
    [SerializeField] private float arcByDistanceMax = 1.4f;

    Transform visual;  
    SpriteRenderer sr;
    Rigidbody2D rb;
    CircleCollider2D cc;
    Vector2 planarPos;
    Vector2 lastPlanarPos;
    bool hasExploded = false;

    // === API: Gọi TRÊN SERVER trước khi Spawn ===
    [Server]
    public void Init(
        Vector2 s, Vector2 t,
        float fly, float height,
        float dmg, float radius,
        GameObject owner)
    {
        startPos = s;
        targetPos = t;
        flightTime = Mathf.Max(0.1f, fly);
        arcHeight = height;
        damage = dmg;
        explosionRadius = radius;
        startTime = (float)NetworkTime.time;
        lastPlanarPos = s;
        var ni = owner ? owner.GetComponent<NetworkIdentity>() : null;
        ownerNetId = ni ? ni.netId : 0u;
        transform.position = s;
    }

    void Awake()
    {
        visual = transform;
        sr = GetComponentInChildren<SpriteRenderer>();

        rb = GetComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic; 
        rb.gravityScale = 0f;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;

        cc = GetComponent<CircleCollider2D>();
        cc.isTrigger = true;
        cc.compositeOperation = Collider2D.CompositeOperation.None;  
    }

    void Update()
    {
        if (flightTime <= 0f) return;
        float now = (float)NetworkTime.time;
        float t01 = Mathf.Clamp01((now - startTime) / flightTime);
        Vector2 prevPlanar = planarPos;
        planarPos = Vector2.Lerp(startPos, targetPos, t01);
        float baseH = 4f * arcHeight * t01 * (1f - t01);
        float distPlanar = (targetPos - startPos).magnitude;
        float arcScale = Mathf.Clamp(
            distPlanar / Mathf.Max(0.001f, arcByDistanceRef),
            arcByDistanceMin, arcByDistanceMax
        );
        float h = baseH * arcScale;
        Vector2 dir = (targetPos - startPos);
        dir = (dir.sqrMagnitude > 1e-6f) ? dir.normalized : Vector2.right;
        Vector2 ortho = new Vector2(-dir.y, dir.x); 
        if (invertSide) ortho = -ortho;
        Vector2 up = (heightAxis.sqrMagnitude > 1e-6f) ? heightAxis.normalized : Vector2.up;
        float verticality = Mathf.Abs(Vector2.Dot(dir, up));  
        float k = sideArcStrength * verticality;           
        Vector2 offset = up * h * (1f - k) + ortho * h * k;
        Vector3 pos3 = new Vector3(planarPos.x + offset.x, planarPos.y + offset.y, 0f);
        visual.position = pos3;
        if (isServer && rb)
            rb.position = new Vector2(pos3.x, pos3.y);
        if (isServer && !hasExploded)
        {
            Vector2 delta = planarPos - lastPlanarPos;
            float segLen = delta.magnitude;
            if (segLen > 0.0001f)
            {
                var hit = Physics2D.CircleCast(lastPlanarPos, 0.1f, delta.normalized, segLen, obstacleMask);
                if (hit.collider)
                {
                    ExplodeAt(hit.point);
                    return;
                }
            }
            lastPlanarPos = planarPos;
        }
        if (isServer && !hasExploded && t01 >= 1f)
            ExplodeAt(planarPos);
    }

    [ServerCallback]
    void OnTriggerEnter2D(Collider2D other)
    {
        if (hasExploded) return;
        var owner = other.GetComponentInParent<NetworkIdentity>();
        if (owner && owner.netId == ownerNetId)
        {
            if ((float)NetworkTime.time - startTime < armDelay) return;
        }
        if ((explodeOnMask.value & (1 << other.gameObject.layer)) == 0) return;
        ExplodeAt(planarPos);
    }

    [Server]
    void ExplodeAt(Vector2 center)
    {
        if (hasExploded) return;
        hasExploded = true;
        if (explosionPrefab != null)
        {
            var go = Instantiate(explosionPrefab, center, Quaternion.identity);
            var aoe = go.GetComponent<ExplosionAoE>();
            if (aoe != null)
            {
                aoe.Setup(damage, explosionRadius, ownerNetId, explodeOnMask);
            }
            NetworkServer.Spawn(go);
        }
        else
        {
            // fallback
            // OverlapCircleAll 
        }
        NetworkServer.Destroy(gameObject);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(planarPos == Vector2.zero ? (Vector3)targetPos : (Vector3)planarPos, explosionRadius);
    }
}
