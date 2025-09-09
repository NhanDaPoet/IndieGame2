using UnityEngine;
using UnityEngine.InputSystem;
using Mirror;
using System.Collections;

public class PlayerMovement : NetworkBehaviour
{
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float animationSmoothTime = 0.1f;
    [SerializeField] private float remoteMoveThreshold = 0.02f;
    [SerializeField] private float attackCooldown = 0.15f;
    [SerializeField] private bool canMoveWhileAttacking = false;
    [SerializeField] private float attackFallbackDuration = 0.4f;
    private Rigidbody2D rb;
    private Animator animator;
    private PlayerInputActions inputActions;
    private Vector2 moveInput;
    private Vector2 lastMoveDirection;
    private Vector2 smoothMoveInput;
    private Vector2 currentVelocity;
    private Vector3 lastRepPosition;
    [SyncVar(hook = nameof(OnIsAttackingChanged))] private bool isAttacking = false;
    private float lastAttackTime = 0f;
    private bool isHoldingAttack = false;
    private ResourceInteractor resourceInteractor;
    private PlayerInventory playerInventory;
    private ResourceNodeBase currentGatherNode;
    public Vector2 FacingDirection => lastMoveDirection.magnitude > 0.1f ? lastMoveDirection : Vector2.down;
    private static readonly int MoveX = Animator.StringToHash("MoveX");
    private static readonly int MoveY = Animator.StringToHash("MoveY");
    private static readonly int IsMoving = Animator.StringToHash("IsMoving");
    private static readonly int AttackHash = Animator.StringToHash("Attack");
    private static readonly int AttackX = Animator.StringToHash("AttackX");
    private static readonly int AttackY = Animator.StringToHash("AttackY");
    private static readonly int IsAttackingHash = Animator.StringToHash("IsAttacking");
    private static readonly int UseConsumableHash = Animator.StringToHash("UseItemConsumable");
    private static readonly int HeldTypeHash = Animator.StringToHash("HeldType");

    private enum HeldUseMode { None = 0, Attack = 1, Consume = 2 }
    private HeldUseMode currentUseMode = HeldUseMode.None;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        resourceInteractor = GetComponent<ResourceInteractor>();
        playerInventory = GetComponent<PlayerInventory>();
        lastMoveDirection = Vector2.down;
        lastRepPosition = transform.position;
    }

    public override void OnStartLocalPlayer()
    {
        if (inputActions != null)
        {
            inputActions.Player.Disable();
            inputActions.Dispose();
        }
        inputActions = new PlayerInputActions();
        inputActions.Player.Enable();
        inputActions.Player.Move.performed += OnMovePerformed;
        inputActions.Player.Move.canceled += OnMoveCanceled;
        inputActions.Player.Attack.performed += OnAttackPerformed;
        inputActions.Player.Attack.canceled += OnAttackCancelled;
        PlayerInventory.OnSelectedHotbarSlotChanged += OnHotbarSelectionChanged_LocalOnly;
        SetupCameraFollow();
    }

    public override void OnStopLocalPlayer()
    {
        if (inputActions != null)
        {
            inputActions.Player.Move.performed -= OnMovePerformed;
            inputActions.Player.Move.canceled -= OnMoveCanceled;
            inputActions.Player.Attack.performed -= OnAttackPerformed;
            inputActions.Player.Attack.canceled -= OnAttackCancelled;
            inputActions.Player.Disable();
            inputActions.Dispose();
            inputActions = null;
        }
    }

    void OnDestroy()
    {
        if (isLocalPlayer)
            PlayerInventory.OnSelectedHotbarSlotChanged -= OnHotbarSelectionChanged_LocalOnly;
        if (inputActions != null)
            inputActions.Dispose();
    }

    private void OnMovePerformed(InputAction.CallbackContext ctx) => moveInput = ctx.ReadValue<Vector2>();
    private void OnMoveCanceled(InputAction.CallbackContext ctx) => moveInput = Vector2.zero;

    private void OnAttackPerformed(InputAction.CallbackContext ctx)
    {
        if (!isLocalPlayer) return;
        if (!CanAttackLocal()) return;
        if (currentUseMode == HeldUseMode.Consume)
        {
            CmdUseConsumable();
            return;
        }
        isHoldingAttack = true;
        TryPerformAttack();
    }

    private void OnAttackCancelled(InputAction.CallbackContext ctx)
    {
        if (!isLocalPlayer) return;
        isHoldingAttack = false;
        if (currentGatherNode != null && resourceInteractor != null)
        {
            resourceInteractor.ClientStopGather(currentGatherNode);
            currentGatherNode = null;
        }
        CmdEndAttack();
    }

    private void TryPerformAttack()
    {
        if (!CanAttackLocal() || currentUseMode == HeldUseMode.Consume) return;
        Vector2 mouseWorld = GetMouseWorldPosition();
        Vector2 dir = (mouseWorld - (Vector2)transform.position).normalized;
        CmdPerformAttack(dir);
        TryStartOrHitNode();
    }

    private void TryStartOrHitNode()
    {
        if (resourceInteractor == null) return;
        var inv = GetComponent<PlayerInventory>();
        if (inv == null) return;
        var held = inv.GetSelectedHotbarItem();
        if (held.IsEmpty || held.itemData == null || held.itemData.itemType != ItemType.Tool) return;
        var node = resourceInteractor.FindNodeInFront();
        if (node == null) return;
        lastMoveDirection = FacingDirection;
        if (node.definition != null)
        {
            if (node.definition.gatherMode == GatherMode.Shared)
            {
                resourceInteractor.ClientStartGather(node);
                currentGatherNode = node;
            }
            else
            {
                resourceInteractor.ClientHit(node);
            }
        }
    }

    private void OnHotbarSelectionChanged_LocalOnly(int slot)
    {
        if (!isLocalPlayer) return;
        UpdateHeldUseModeFromInventory();
    }

    private void UpdateHeldUseModeFromInventory()
    {
        if (playerInventory == null)
            playerInventory = GetComponent<PlayerInventory>();
        var stack = playerInventory != null ? playerInventory.GetSelectedHotbarItem() : new ItemStack();
        ItemType type = stack.IsEmpty || stack.itemData == null ? ItemType.Material : stack.itemData.itemType;
        if (type == ItemType.Consumable)
            currentUseMode = HeldUseMode.Consume;
        else if (type == ItemType.Tool || type == ItemType.Weapon)
            currentUseMode = HeldUseMode.Attack;
        else
            currentUseMode = HeldUseMode.None;
        if (animator != null)
        {
            int heldParam = 0;
            if (type == ItemType.Weapon) heldParam = 1;
            else if (type == ItemType.Tool) heldParam = 2;
            else if (type == ItemType.Consumable) heldParam = 3;
            animator.SetInteger(HeldTypeHash, heldParam);
        }
    }

    private Vector2 GetMouseWorldPosition()
    {
        Vector3 mouse = Mouse.current.position.ReadValue();
        Vector3 world = Camera.main != null ? Camera.main.ScreenToWorldPoint(mouse) : new Vector3(transform.position.x, transform.position.y, 0f);
        world.z = 0f;
        return world;
    }

    private bool CanAttackLocal() => Time.time >= lastAttackTime + attackCooldown;

    [Command]
    private void CmdPerformAttack(Vector2 attackDirection)
    {
        if (Time.time < lastAttackTime + attackCooldown) return;
        isAttacking = true;
        lastAttackTime = Time.time;
        Vector2 dir = attackDirection.sqrMagnitude > 0.0001f ? attackDirection.normalized : Vector2.down;
        lastMoveDirection = dir;
        RpcStartAttack(dir);
    }

    [ClientRpc]
    private void RpcStartAttack(Vector2 attackDirection)
    {
        lastMoveDirection = attackDirection.sqrMagnitude > 0.0001f ? attackDirection.normalized : lastMoveDirection;
        if (animator != null)
        {
            animator.SetFloat(AttackX, lastMoveDirection.x);
            animator.SetFloat(AttackY, lastMoveDirection.y);
            animator.SetBool(IsAttackingHash, true);
            animator.ResetTrigger(AttackHash);
            animator.SetTrigger(AttackHash);
        }
    }

    [Command]
    private void CmdEndAttack()
    {
        if (!isAttacking) return;
        isAttacking = false;
        RpcEndAttack();
    }

    [ClientRpc]
    private void RpcEndAttack()
    {
        if (animator != null)
        {
            animator.SetBool(IsAttackingHash, false);
        }
    }

    private void OnIsAttackingChanged(bool oldVal, bool newVal)
    {
        if (animator != null)
        {
            animator.SetBool(IsAttackingHash, newVal);
        }
    }

    [Command]
    private void CmdUseConsumable()
    {
        var inv = GetComponent<PlayerInventory>();
        if (inv != null)
        {
            inv.ServerUseSelectedConsumable();
        }
        RpcPlayUseConsumableAnimation();
    }

    [ClientRpc]
    private void RpcPlayUseConsumableAnimation()
    {
        if (animator != null)
        {
            animator.ResetTrigger(UseConsumableHash);
            animator.SetTrigger(UseConsumableHash);
        }
    }

    private void SetupCameraFollow()
    {
        if (!isLocalPlayer) return;
        if (Camera.main != null)
        {
            var cameraFollow = Camera.main.GetComponent<CameraFollow>();
            if (cameraFollow != null)
            {
                cameraFollow.SetTarget(transform);
            }
        }
    }

    void Update()
    {
        if (isLocalPlayer)
        {
            HandleInput();
            HandleAnimation();
        }
        else
        {
            HandleAnimation();
        }
    }

    private void HandleInput()
    {
        if (!isLocalPlayer) return;
        var attackInput = inputActions.Player.Attack;
        if (attackInput.IsPressed() && isHoldingAttack && !isAttacking && CanAttackLocal())
        {
            TryPerformAttack();
        }
    }

    void FixedUpdate()
    {
        if (!isLocalPlayer || rb == null) return;
        if (isAttacking && !canMoveWhileAttacking)
        {
            rb.linearVelocity = Vector2.zero;
            return;
        }
        Vector2 moveDirection = moveInput.normalized;
        rb.linearVelocity = moveDirection * moveSpeed;
    }

    private void HandleAnimation()
    {
        if (animator == null) return;
        bool isMovingAnim;
        Vector2 animDir;
        if (isLocalPlayer)
        {
            isMovingAnim = moveInput.magnitude > 0.1f && (!isAttacking || canMoveWhileAttacking);
            animDir = isMovingAnim ? moveInput.normalized : lastMoveDirection;
            if (isMovingAnim && !isAttacking)
                lastMoveDirection = animDir;
        }
        else
        {
            Vector3 delta = transform.position - lastRepPosition;
            isMovingAnim = delta.sqrMagnitude > (remoteMoveThreshold * remoteMoveThreshold);
            animDir = isMovingAnim ? ((Vector2)delta).normalized : lastMoveDirection;
            if (isMovingAnim && !isAttacking)
                lastMoveDirection = animDir;
        }
        smoothMoveInput = Vector2.SmoothDamp(smoothMoveInput, animDir, ref currentVelocity, animationSmoothTime);
        animator.SetFloat(MoveX, smoothMoveInput.x);
        animator.SetFloat(MoveY, smoothMoveInput.y);
        animator.SetBool(IsMoving, isMovingAnim);
    }

    public void OnAttackAnimationComplete()
    {
        if (isLocalPlayer)
        {
            CmdEndAttack();
            if (isHoldingAttack && CanAttackLocal())
            {
                TryPerformAttack();
            }
        }
    }

    public void OnAttackHit()
    {
        if (isLocalPlayer && currentUseMode == HeldUseMode.Attack)
        {
            var inv = GetComponent<PlayerInventory>();
            if (inv == null) return;
            var held = inv.GetSelectedHotbarItem();
            if (held.IsEmpty || held.itemData == null || held.itemData.itemType != ItemType.Tool) return;
            var node = resourceInteractor.FindNodeInFront();
            if (node == null) return;
            if (node.definition != null && node.definition.gatherMode == GatherMode.Solo)
            {
                resourceInteractor.ClientHit(node);
            }
        }
    }
}