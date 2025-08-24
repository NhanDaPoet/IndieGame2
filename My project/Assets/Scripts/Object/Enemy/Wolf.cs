using System.Collections.Generic;
using UnityEngine;

public class Wolf : BaseEnemy
{
    [Header("Wolf Specific")]
    [SerializeField] private float packDistance = 10f;
    [SerializeField] private int maxPackSize = 4;

    private List<Wolf> packMembers = new List<Wolf>();

    protected override void Start()
    {
        base.Start();
        if (isServer)
        {
            FindPackMembers();
        }
    }

    private void FindPackMembers()
    {
        Collider2D[] nearbyColliders = Physics2D.OverlapCircleAll(transform.position, packDistance);

        foreach (var collider in nearbyColliders)
        {
            var wolf = collider.GetComponent<Wolf>();
            if (wolf != null && wolf != this && packMembers.Count < maxPackSize)
            {
                packMembers.Add(wolf);
            }
        }
    }

    public override void TakeDamage(float damage, GameObject attacker)
    {
        base.TakeDamage(damage, attacker);
        foreach (var packMember in packMembers)
        {
            if (packMember != null && !packMember.isDead)
            {
                packMember.AlertToEnemy(attacker);
            }
        }
    }

    public void AlertToEnemy(GameObject enemy)
    {
        if (currentTarget == null && enemyType != EnemyType.Passive)
        {
            currentTarget = enemy;
            ChangeState(EnemyState.Chasing);
        }
    }

    protected override bool ShouldChaseTarget()
    {
        return currentTarget != null && base.ShouldChaseTarget();
    }
}
