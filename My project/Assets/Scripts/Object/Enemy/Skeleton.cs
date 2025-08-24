using Mirror;
using System.Collections;
using Unity.VisualScripting.Antlr3.Runtime.Misc;
using UnityEngine;

public class Skeleton : BaseEnemy
{
    [Header("Ranged")]
    [SerializeField] private GameObject missilePrefab;  
    [SerializeField] private Transform shootPoint;       
    [SerializeField] private float rangedAttackRange = 10f; 
    [SerializeField] private float minRangedRange = 2f;    
    [SerializeField] private float windup = 0.35f;        
    [SerializeField] private float flightTime = 0.9f;   
    [SerializeField] private float arcHeight = 2.5f;      
    [SerializeField] private float explosionRadius = 1.75f;

    protected override void HandleChasingState()
    {
        if (!SimulateAuthority) return;
        if (currentTarget == null || !IsValidTarget(currentTarget))
        {
            if (stateTimer >= minChaseDurationBeforeIdle) LoseTarget();
            return;
        }
        float d = Vector2.Distance(transform.position, currentTarget.transform.position);
        if (d <= stats.attackRange || (d <= rangedAttackRange && d >= minRangedRange))
        {
            Stop();
            ChangeState(EnemyState.Attacking);
            return;
        }
        MoveTo(currentTarget.transform.position);
        lastKnownTargetPosition = currentTarget.transform.position;
    }

    protected override void HandleAttackingState()
    {
        if (!SimulateAuthority) { FaceTargetClientSide(); return; }
        if (currentTarget == null || !IsValidTarget(currentTarget)) { LoseTarget(); return; }
        float d = Vector2.Distance(transform.position, currentTarget.transform.position);
        float maxRange = Mathf.Max(rangedAttackRange, stats.attackRange);
        if (d > maxRange * 1.15f) { ChangeState(EnemyState.Chasing); return; }
        FaceTarget(currentTarget.transform.position);
        if (d <= stats.attackRange)
        {
            if (Time.time >= lastAttackTime + stats.attackCooldown)
                Attack(currentTarget);
            return;
        }
        if (Time.time >= lastAttackTime + stats.attackCooldown)
        {
            lastAttackTime = Time.time;
            Vector2 start = shootPoint ? (Vector2)shootPoint.position : (Vector2)transform.position;
            Vector2 targetPos = currentTarget.transform.position;
            StartCoroutine(ServerFireMissileAfterDelay(start, targetPos));
            if (networkAnimator) networkAnimator.SetTrigger("RangedAttack");
            else if (animator) animator.SetTrigger("RangedAttack");
        }
    }

    [Server]
    private IEnumerator ServerFireMissileAfterDelay(Vector2 start, Vector2 targetPos)
    {
        if (windup > 0f) yield return new WaitForSeconds(windup);
        if (!missilePrefab) yield break;
        GameObject go = Instantiate(missilePrefab, start, Quaternion.identity);
        var missile = go.GetComponent<MissileProjectile>();
        if (missile != null)
        {
            missile.Init(start, targetPos, flightTime, arcHeight, stats.attackDamage, explosionRadius, gameObject);
        }

        NetworkServer.Spawn(go);
    }
}
