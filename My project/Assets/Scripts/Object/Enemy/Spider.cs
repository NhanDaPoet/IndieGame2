using System.Collections;
using UnityEngine;

public class Spider : BaseEnemy
{
    [Header("Spider Specific")]
    [SerializeField] private float jumpForce = 8f;
    [SerializeField] private float webSlowEffect = 0.5f;
    [SerializeField] private float climbSpeed = 2f;

    private bool isClimbing = false;

    protected override void UpdateMovement()
    {
        base.UpdateMovement();
        CheckForWallClimbing();
    }

    protected override bool ShouldChaseTarget()
    {
        if (currentTarget != null) return true; 

        if (!IsNightTime()) return false;

        return base.ShouldChaseTarget();
    }

    public override void Attack(GameObject target)
    {
        base.Attack(target);
        if (Random.value < 0.4f)
        {
            var playerStats = target.GetComponent<PlayerHealth>();
            if (playerStats != null)
            {
                // Apply poison or slow effect
                // playerStats.ApplyEffect("Poison", 10f);
            }
        }
    }

    protected virtual void CheckForWallClimbing()
    {
        if (!isClimbing && currentState == EnemyState.Chasing)
        {
            RaycastHit2D wallHit = Physics2D.Raycast(transform.position,
                spriteRenderer.flipX ? Vector2.left : Vector2.right, 1f, LayerMask.GetMask("Blocks"));

            if (wallHit.collider != null)
            {
                StartClimbing();
            }
        }
    }

    private void StartClimbing()
    {
        isClimbing = true;
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, climbSpeed);
        StartCoroutine(StopClimbingAfterDelay(2f));
    }

    private IEnumerator StopClimbingAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        isClimbing = false;
    }
}
