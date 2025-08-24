using Mirror;
using System.Collections;
using UnityEngine;
using static Unity.Collections.AllocatorManager;

public class Creeper : BaseEnemy
{
    [Header("Creeper Specific")]
    [SerializeField] private float explosionRadius = 3f;
    [SerializeField] private float explosionDamage = 50f;
    [SerializeField] private float fuseTime = 1.5f;
    [SerializeField] private GameObject explosionEffect;

    private bool isExploding = false;
    private bool hasExploded = false;

    protected override void HandleAttackingState()
    {
        if (currentTarget == null || !IsValidTarget(currentTarget))
        {
            LoseTarget();
            return;
        }
        float distanceToTarget = Vector3.Distance(transform.position, currentTarget.transform.position);
        if (distanceToTarget <= stats.attackRange && !isExploding && !hasExploded)
        {
            StartExplosion();
        }
        else if (distanceToTarget > stats.attackRange * 1.5f)
        {
            ChangeState(EnemyState.Chasing);
        }
    }

    private void StartExplosion()
    {
        isExploding = true;
        Stop(); 
        if (animator != null)
        {
            animator.SetTrigger("StartFuse");
        }
        RpcPlayFuseEffect();
        StartCoroutine(ExplodeAfterDelay(fuseTime));
    }

    [ClientRpc]
    private void RpcPlayFuseEffect()
    {
        StartCoroutine(FuseFlash());
    }

    private IEnumerator FuseFlash()
    {
        float timer = 0f;
        Color originalColor = spriteRenderer.color;

        while (timer < fuseTime)
        {
            spriteRenderer.color = Color.white;
            yield return new WaitForSeconds(0.1f);
            spriteRenderer.color = originalColor;
            yield return new WaitForSeconds(0.1f);
            timer += 0.2f;
        }
    }

    private IEnumerator ExplodeAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        Explode();
    }

    private void Explode()
    {
        if (hasExploded) return;
        hasExploded = true;
        Collider2D[] hitColliders = Physics2D.OverlapCircleAll(transform.position, explosionRadius);
        //Explosive Block
        RpcPlayExplosionEffect();
        NetworkServer.Destroy(gameObject);
    }

    [ClientRpc]
    private void RpcPlayExplosionEffect()
    {
        if (explosionEffect != null)
        {
            Instantiate(explosionEffect, transform.position, Quaternion.identity);
        }

        // Play explosion sound, screen shake, etc.
    }

    public void CancelExplosion()
    {
        if (isExploding && !hasExploded)
        {
            isExploding = false;
            StopAllCoroutines();

            if (animator != null)
            {
                animator.SetTrigger("CancelFuse");
            }
        }
    }
}
