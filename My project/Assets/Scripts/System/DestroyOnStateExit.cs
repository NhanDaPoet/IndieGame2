using UnityEngine;

public class DestroyOnStateExit : StateMachineBehaviour
{
    public float extraDelay = 0f;

    public override void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        Object.Destroy(animator.gameObject, extraDelay);
    }
}
