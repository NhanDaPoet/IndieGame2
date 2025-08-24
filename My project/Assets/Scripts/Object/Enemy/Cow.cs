using Mirror;
using UnityEngine;

public class Cow : BaseEnemy
{
    [Header("Cow Specific")]
    [SerializeField] private float milkCooldown = 300f;
    [SerializeField] private string milkItemId = "milk_bucket";

    private float lastMilkedTime;

    public bool CanBeMilked()
    {
        return Time.time >= lastMilkedTime + milkCooldown;
    }

    public void Milk(GameObject player)
    {
        if (!CanBeMilked()) return;
        lastMilkedTime = Time.time;
        var playerInventory = player.GetComponent<PlayerInventory>();
        if (playerInventory != null)
        {
            // playerInventory.AddItem(milkItemId, 1);
        }
        if (animator != null)
        {
            animator.SetTrigger("GetMilked");
        }

        RpcPlayMilkEffect();
    }

    [ClientRpc]
    private void RpcPlayMilkEffect()
    {
        // Play moo sound, hearts particles
    }

    protected override void HandleIdleState()
    {
        base.HandleIdleState();
        if (Random.value < 0.01f)
        {
            TryEatGrass();
        }
    }

    private void TryEatGrass()
    {
        //Eat
    }
}
