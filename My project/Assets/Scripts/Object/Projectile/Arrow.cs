using Mirror;
using UnityEngine;

public class Arrow : NetworkBehaviour
{
    [SerializeField] private float damage = 10f;
    [SerializeField] private float lifetime = 5f;
    [SerializeField] private LayerMask hitLayers;

    private GameObject owner;
    private bool hasHit = false;

    void Start()
    {
        Destroy(gameObject, lifetime);
    }

    void Update()
    {
        if (hasHit) return;
        if (GetComponent<Rigidbody2D>().linearVelocity != Vector2.zero)
        {
            float angle = Mathf.Atan2(GetComponent<Rigidbody2D>().linearVelocity.y,
                                     GetComponent<Rigidbody2D>().linearVelocity.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.AngleAxis(angle, Vector3.forward);
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (hasHit || other.gameObject == owner) return;
        if (((1 << other.gameObject.layer) & hitLayers) != 0)
        {
            var damageable = other.GetComponent<IDamageable>();
            if (damageable != null)
            {
                damageable.TakeDamage(damage, owner);
            }
            Hit();
        }
    }

    private void Hit()
    {
        hasHit = true;
        GetComponent<Rigidbody2D>().linearVelocity = Vector2.zero;
        GetComponent<Rigidbody2D>().isKinematic = true;
        Destroy(gameObject, 2f);
    }

    public void SetDamage(float newDamage)
    {
        damage = newDamage;
    }

    public void SetOwner(GameObject newOwner)
    {
        owner = newOwner;
    }
}
