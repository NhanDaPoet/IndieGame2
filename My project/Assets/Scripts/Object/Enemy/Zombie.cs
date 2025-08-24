using UnityEngine;

public class Zombie : BaseEnemy
{
    [Header("Zombie Specific")]
    [SerializeField] private float sunlightDamage = 5f;
    [SerializeField] private bool burnInSunlight = true;

    protected override void UpdateBehavior()
    {
        base.UpdateBehavior();
        if (burnInSunlight && !IsNightTime() && !IsInShade())
        {
            if (Time.time % 1f < Time.deltaTime)
            {
                TakeDamage(sunlightDamage, null);
            }
        }
    }

    private bool IsInShade()
    {
        RaycastHit2D hit = Physics2D.Raycast(transform.position, Vector2.up, 1f, LayerMask.GetMask("Blocks"));
        return hit.collider != null;
    }

    public override void Attack(GameObject target)
    {
        base.Attack(target);
        if (Random.value < 0.3f)
        {
            var playerHealth = target.GetComponent<PlayerHealth>();
            if (playerHealth != null)
            {
                // Apply hunger effect
                // playerStats.ApplyEffect("Hunger", 30f);
            }
        }
    }
}
