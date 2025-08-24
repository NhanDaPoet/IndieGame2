using Mirror;
using UnityEngine;

public class Slime : BaseEnemy
{
    [Header("Slime Specific")]
    [SerializeField] private float bounceHeight = 2f;
    [SerializeField] private float bounceFrequency = 1f;
    [SerializeField] private int splitCount = 2;
    [SerializeField] private GameObject smallSlimePrefab;

    private float bounceTimer;
    private bool isLargeSlime = true;

    protected override void UpdateMovement()
    {
        base.UpdateMovement();
        bounceTimer += Time.deltaTime;
        if (bounceTimer >= bounceFrequency && currentState != EnemyState.Dead)
        {
            Bounce();
            bounceTimer = 0f;
        }
    }

    private void Bounce()
    {
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, bounceHeight);
        if (animator != null)
        {
            animator.SetTrigger("Bounce");
        }

        RpcPlayBounceEffect();
    }

    [ClientRpc]
    private void RpcPlayBounceEffect()
    {
        // Play bounce sound, squash/stretch animation
    }

    protected override void Die()
    {
        if (isLargeSlime && smallSlimePrefab != null)
        {
            SplitIntoSmallSlimes();
        }

        base.Die();
    }

    private void SplitIntoSmallSlimes()
    {
        for (int i = 0; i < splitCount; i++)
        {
            Vector3 spawnPos = transform.position + (Vector3)Random.insideUnitCircle.normalized * 1f;
            GameObject smallSlime = Instantiate(smallSlimePrefab, spawnPos, Quaternion.identity);
            NetworkServer.Spawn(smallSlime);
            var slimeScript = smallSlime.GetComponent<Slime>();
            if (slimeScript != null)
            {
                slimeScript.isLargeSlime = false;
                slimeScript.stats.maxHealth *= 0.5f;
                slimeScript.stats.attackDamage *= 0.5f;
                slimeScript.currentHealth = slimeScript.stats.maxHealth;
            }
        }
    }
}