using Mirror;
using System.Collections;
using UnityEngine;

public class PlayerHealth : NetworkBehaviour, IDamageable
{
    [Header("Player Stats")]
    [SerializeField] private float maxHealth = 100f;
    [SyncVar(hook = nameof(OnHealthChanged))]
    private float currentHealth;

    [Header("Components")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Rigidbody2D rb;
    [SerializeField] private BoxCollider2D playerCollider;

    public bool IsDead => currentHealth <= 0;
    public float CurrentHealth => currentHealth;
    public float MaxHealth => maxHealth;

    private void Start()
    {
        Initialize();
    }

    private void Initialize()
    {
        currentHealth = maxHealth;
    }

    public void TakeDamage(float damage, GameObject attacker)
    {
        if (!isServer || IsDead) return; 
        currentHealth -= damage;
        if (currentHealth <= 0)
        {
            currentHealth = 0;
            Die();
        }
        RpcPlayDamageEffect();
    }

    [ClientRpc]
    private void RpcPlayDamageEffect()
    {
        StartCoroutine(DamageFlash());
    }

    private IEnumerator DamageFlash()
    {
        if (spriteRenderer != null)
        {
            Color original = spriteRenderer.color;
            spriteRenderer.color = Color.red;
            yield return new WaitForSeconds(0.1f);
            spriteRenderer.color = original;
        }
    }

    private void Die()
    {
        if (isServer)
        {
            RpcPlayDeathEffect();
        }
    }

    [ClientRpc]
    private void RpcPlayDeathEffect()
    {

    }

    private void OnHealthChanged(float oldHealth, float newHealth)
    {
        Debug.Log($"Player health changed: {newHealth}/{maxHealth}");
    }
}
