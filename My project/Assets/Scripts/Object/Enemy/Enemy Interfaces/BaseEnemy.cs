using Mirror;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IDamageable
{
    void TakeDamage(float damage, GameObject attacker);
    bool IsDead { get; }
    float CurrentHealth { get; }
    float MaxHealth { get; }
}

public interface IMoveable
{
    void MoveTo(Vector3 target);
    void Stop();
    float MoveSpeed { get; set; }
    bool IsMoving { get; }
}

public interface ICombat
{
    void Attack(GameObject target);
    float AttackDamage { get; }
    float AttackRange { get; }
    float AttackCooldown { get; }
}

[System.Serializable]
public class EnemyStats
{
    [Header("Basic Stats")]
    public float maxHealth = 100f;
    public float moveSpeed = 3f;
    public float chaseSpeed = 5f;
    public float attackDamage = 20f;
    public float attackRange = 1.5f;
    public float attackCooldown = 2f;
    public float detectionRange = 10f;
    public float aggroRange = 8f;

    [Header("Behavior")]
    public bool canAttackBlocks = false;
    public bool canJump = false;
    public bool isNocturnal = false;
    public bool avoidsWater = true;

    [Header("Movement")]
    public float wanderDistance = 5f;
    public float wanderInterval = 3f;

    [Header("Drops")]
    public ItemDrop[] drops;
    public int experienceValue = 10;
}

[System.Serializable]
public class ItemDrop
{
    public string itemId;
    public int minAmount = 1;
    public int maxAmount = 3;
    public float dropChance = 1f;
}

public enum EnemyType
{
    Passive,
    Neutral,
    Hostile,
    Boss
}

public enum EnemyState
{
    Idle,     
    Chasing,   
    Attacking,
    Fleeing,  
    Dead 
}

public enum MovementDirection
{
    Idle,
    North,
    NorthEast,
    East,
    SouthEast,
    South,
    SouthWest,
    West,
    NorthWest
}

public abstract class BaseEnemy : NetworkBehaviour, IDamageable, IMoveable, ICombat
{
    [Header("Enemy Configuration")]
    [SerializeField] protected EnemyType enemyType = EnemyType.Hostile;
    [SerializeField] protected EnemyStats stats;

    [Header("Components")]
    [SerializeField] protected Animator animator;
    [SerializeField] protected SpriteRenderer spriteRenderer;
    [SerializeField] protected Rigidbody2D rb;
    [SerializeField] protected BoxCollider2D collisionCollider;
    [SerializeField] protected BoxCollider2D detectionCollider;
    [SerializeField] protected NetworkAnimator networkAnimator;
    [SerializeField] private LayerMask playerLayerMask = -1;

    [Header("Animation Settings")]
    [SerializeField] protected bool useBlendTree = true;
    [SerializeField] protected float animationSmoothTime = 0.1f;
    [SerializeField] protected bool flipSpriteForDirection = false;
    [SerializeField] protected float minIdleDurationBeforeChase = 0.15f;
    [SerializeField] protected float minChaseDurationBeforeIdle = 0.25f;
    [SerializeField] protected float chaseEnterMargin = 0.25f;  
    [SerializeField] protected float chaseExitMargin = 0.75f;
    [SerializeField] protected int detectionMaxHits = 16;
    [SerializeField] protected float chaseStickTime = 0.6f;

    [Header("Hit Reaction")]
    [SerializeField] protected float hitKnockback = 2.4f;
    [SerializeField] protected float hitStaggerDuration = 0.12f;
    [SerializeField] protected float knockbackBackDist = 0.35f;
    [SerializeField] protected float knockbackOutTime = 0.08f;
    [SerializeField] protected float knockbackReturnTime = 0.07f;
    [SerializeField] protected GameObject deathFxPrefab;

    [SyncVar(hook = nameof(OnHealthChanged))]
    protected float currentHealth;

    [SyncVar(hook = nameof(OnStateChanged))]
    protected EnemyState currentState = EnemyState.Idle;

    [SyncVar(hook = nameof(OnDirectionChanged))]
    protected MovementDirection currentDirection = MovementDirection.Idle;

    [SyncVar(hook = nameof(OnFacingDirectionChanged))]
    protected MovementDirection facingDirection = MovementDirection.South;

    [SyncVar(hook = nameof(OnSpeedChanged))] private float netSpeed;
    [SyncVar(hook = nameof(OnMoveXChanged))] private float netMoveX;
    [SyncVar(hook = nameof(OnMoveYChanged))] private float netMoveY;
    [SyncVar(hook = nameof(OnIsMovingChanged))] private bool netIsMoving;

    [SyncVar]
    protected bool isDead = false;

    protected Vector2 currentMovementDirection = Vector2.zero;
    protected float currentMoveX = 0f;
    protected float currentMoveY = 0f;
    protected float targetMoveX = 0f;
    protected float targetMoveY = 0f;

    protected GameObject currentTarget;
    protected Vector3 lastKnownTargetPosition;
    protected float lastAttackTime;
    protected List<GameObject> nearbyPlayers = new List<GameObject>();
    protected Vector3 spawnPosition;

    protected float stateTimer;
    protected float lastDirectionChangeTime = 0f;
    protected float nextWanderTime;
    protected float targetLostGrace = 0.5f; 
    protected float lastSeenTargetTime = -999f;
    protected float acquiredTargetTime = -999f;
    protected float knockbackEndTime;
    Coroutine knockbackRoutine;

    protected Vector3 moveDestination;
    protected bool hasDestination = false;
    protected readonly Collider2D[] detectionHits = new Collider2D[16];

    protected bool SimulateAuthority => !NetworkClient.active || isServer;
    protected bool animatorReady;

    public float CurrentHealth => currentHealth;
    public float MaxHealth => stats.maxHealth;
    public bool IsDead => isDead;
    public float MoveSpeed { get; set; }
    public bool IsMoving { get; protected set; }
    public float AttackDamage => stats.attackDamage;
    public float AttackRange => stats.attackRange;
    public float AttackCooldown => stats.attackCooldown;
    public EnemyState State => currentState;
    public MovementDirection Direction => currentDirection;
    public MovementDirection FacingDirection => facingDirection;
    protected readonly Dictionary<GameObject, int> playerOverlapCount = new Dictionary<GameObject, int>();

    protected int animMoveXHash;
    protected int animMoveYHash;
    protected int animStateHash;
    protected int animAttackHash;
    protected int animDieHash;
    protected int animSpeedHash;

    protected virtual void Start()
    {
        Initialize();
    }

    protected virtual void Awake()
    {
        if (!animator) animator = GetComponentInChildren<Animator>();
        if (!spriteRenderer) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        CacheAnimationHashes();
        animatorReady = animator && animSpeedHash != 0 && animMoveXHash != 0 &&
                        animMoveYHash != 0 && animStateHash != 0;
    }

    bool EnsureAnimatorReady()
    {
        if (!animatorReady)
        {
            if (!animator) animator = GetComponentInChildren<Animator>();
            if (animSpeedHash == 0) CacheAnimationHashes();
            animatorReady = animator && animSpeedHash != 0 && animMoveXHash != 0 &&
                            animMoveYHash != 0 && animStateHash != 0;
        }
        return animatorReady;
    }

    protected virtual void Initialize()
    {
        if (!animator) animator = GetComponentInChildren<Animator>();
        if (!rb) rb = GetComponent<Rigidbody2D>();
        if (!spriteRenderer) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (!networkAnimator) networkAnimator = GetComponent<NetworkAnimator>();
        currentHealth = stats.maxHealth;
        MoveSpeed = stats.moveSpeed;
        spawnPosition = transform.position;
        spawnPosition.z = 0f;
        transform.position = new Vector3(transform.position.x, transform.position.y, 0f);
        facingDirection = MovementDirection.South;
        CacheAnimationHashes();
        if (animator != null)
        {
            animator.updateMode = AnimatorUpdateMode.Normal;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }
        ChangeState(EnemyState.Idle);
        nearbyPlayers.Clear();
        nextWanderTime = Time.time + Random.Range(1f, stats.wanderInterval);
        if (detectionCollider != null)
        {
            detectionCollider.isTrigger = true;
            detectionCollider.size = new Vector2(stats.detectionRange * 2f, stats.detectionRange * 2f);
        }
        if (rb != null)
        {
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            rb.linearDamping = 0f; 
            rb.angularDamping = 0.05f;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        }
    }

    protected virtual void CacheAnimationHashes()
    {
        if (animator != null)
        {
            animMoveXHash = Animator.StringToHash("MoveX");
            animMoveYHash = Animator.StringToHash("MoveY");
            animStateHash = Animator.StringToHash("State");
            animAttackHash = Animator.StringToHash("Attack");
            animDieHash = Animator.StringToHash("Die");
            animSpeedHash = Animator.StringToHash("Speed");
        }
    }

    protected virtual void Update()
    {
        stateTimer += Time.deltaTime;
        if (!isDead)
        {
            UpdateBehavior();    
            UpdateMovement();     
            if (SimulateAuthority)
                UpdateAnimation();
        }
    }

    protected virtual void UpdateBehavior()
    {
        switch (currentState)
        {
            case EnemyState.Idle:
                HandleIdleState();
                break;
            case EnemyState.Chasing:
                HandleChasingState();
                break;
            case EnemyState.Attacking:
                HandleAttackingState();
                break;
            case EnemyState.Fleeing:
                HandleFleeingState();
                break;
        }
    }

    protected virtual void HandleIdleState()
    {
        if (stateTimer >= minIdleDurationBeforeChase && ShouldChaseTarget())
        {
            ChangeState(EnemyState.Chasing);
            return;
        }
        if (Time.time >= nextWanderTime)
        {
            if (!IsMoving || (hasDestination && Vector2.Distance(transform.position, moveDestination) < 0.5f))
            {
                SetNewWanderTarget();
                nextWanderTime = Time.time + Random.Range(stats.wanderInterval, stats.wanderInterval * 2f);
            }
        }
    }

    protected virtual void HandleChasingState()
    {
        if (!SimulateAuthority) return;
        if (currentTarget == null || !IsValidTarget(currentTarget))
        {
            if (stateTimer >= minChaseDurationBeforeIdle) LoseTarget();
            return;
        }
        float distanceToTarget = Vector2.Distance(transform.position, currentTarget.transform.position);
        if (distanceToTarget <= stats.attackRange)
        {
            ChangeState(EnemyState.Attacking);
            return;
        }
        float exitDist = Mathf.Max(0f, stats.aggroRange + chaseExitMargin);
        bool beyondExit = distanceToTarget > exitDist;
        if (beyondExit)
        {
            if (Time.time >= acquiredTargetTime + chaseStickTime &&
                Time.time - lastSeenTargetTime > targetLostGrace &&
                stateTimer >= minChaseDurationBeforeIdle)
            {
                LoseTarget();
                return;
            }
        }
        else
        {
            lastSeenTargetTime = Time.time;
        }
        MoveTo(currentTarget.transform.position);
        lastKnownTargetPosition = currentTarget.transform.position;
    }

    protected virtual void HandleAttackingState()
    {
        if (!SimulateAuthority)
        {
            FaceTargetClientSide();
            return;
        }

        if (currentTarget == null || !IsValidTarget(currentTarget))
        {
            LoseTarget();
            return;
        }

        float distanceToTarget = Vector2.Distance(transform.position, currentTarget.transform.position);

        if (distanceToTarget > stats.attackRange * 1.2f)
        {
            ChangeState(EnemyState.Chasing);
            return;
        }

        // Face target and attack
        FaceTarget(currentTarget.transform.position);

        if (Time.time >= lastAttackTime + stats.attackCooldown)
        {
            Attack(currentTarget);
        }
    }

    protected virtual void HandleFleeingState()
    {
        if (!SimulateAuthority) return;

        if (currentTarget != null)
        {
            Vector3 fleeDirection = (transform.position - currentTarget.transform.position).normalized;
            Vector3 fleeTarget = transform.position + fleeDirection * 5f;
            MoveTo(fleeTarget);
        }

        if (stateTimer > 5f)
        {
            ChangeState(EnemyState.Idle);
        }
    }

    public virtual void MoveTo(Vector3 target)
    {
        if (!SimulateAuthority) return;
        if (Time.time < knockbackEndTime) return;
        moveDestination = new Vector3(target.x, target.y, 0f);
        hasDestination = true;
        Vector2 direction = (moveDestination - transform.position).normalized;
        rb.linearVelocity = direction * MoveSpeed;
        currentMovementDirection = direction;
        IsMoving = true;
        targetMoveX = direction.x;
        targetMoveY = direction.y;
        UpdateMovementDirection(direction);
        if (Time.time > lastDirectionChangeTime + 0.5f)
        {
            UpdateFacingDirection(direction);
            lastDirectionChangeTime = Time.time;
        }
    }

    public virtual void Stop()
    {
        if (!SimulateAuthority) return;

        rb.linearVelocity = Vector2.zero;
        IsMoving = false;
        hasDestination = false;
        currentMovementDirection = Vector2.zero;
        targetMoveX = 0f;
        targetMoveY = 0f;
        UpdateMovementDirection(Vector2.zero);
    }

    protected virtual void UpdateMovement()
    {
        if (!SimulateAuthority) return;

        if (hasDestination && currentState != EnemyState.Chasing)
        {
            float distanceToDestination = Vector2.Distance(transform.position, moveDestination);
            if (distanceToDestination < 0.5f)
            {
                Stop();
            }
        }

        if (rb.linearVelocity.sqrMagnitude < 0.01f && IsMoving && currentState != EnemyState.Chasing)
        {
            Stop();
        }
    }

    protected virtual Vector2 NormalizeTo8Directions(Vector2 direction)
    {
        if (direction.magnitude < 0.1f)
            return Vector2.zero;

        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        if (angle < 0) angle += 360;

        float normalizedAngle = Mathf.Round(angle / 45f) * 45f;
        float radians = normalizedAngle * Mathf.Deg2Rad;

        return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)).normalized;
    }

    protected virtual void UpdateMovementDirection(Vector2 moveDir)
    {
        MovementDirection newDirection = MovementDirection.Idle;

        if (moveDir.magnitude < 0.1f)
        {
            newDirection = MovementDirection.Idle;
        }
        else
        {
            Vector2 normalizedDir = NormalizeTo8Directions(moveDir);
            newDirection = GetDirectionFromVector(normalizedDir);
        }

        if (currentDirection != newDirection && SimulateAuthority)
        {
            currentDirection = newDirection;
        }
    }

    protected virtual void UpdateFacingDirection(Vector2 faceDir)
    {
        if (faceDir.magnitude < 0.1f)
            return;

        Vector2 normalizedDir = NormalizeTo8Directions(faceDir);
        MovementDirection newFacingDirection = GetDirectionFromVector(normalizedDir);

        if (facingDirection != newFacingDirection && SimulateAuthority)
        {
            facingDirection = newFacingDirection;
        }
    }

    protected virtual MovementDirection GetDirectionFromVector(Vector2 direction)
    {
        if (direction.magnitude < 0.1f)
            return MovementDirection.Idle;

        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        if (angle < 0) angle += 360;

        if (angle >= 337.5f || angle < 22.5f)
            return MovementDirection.East;
        else if (angle >= 22.5f && angle < 67.5f)
            return MovementDirection.NorthEast;
        else if (angle >= 67.5f && angle < 112.5f)
            return MovementDirection.North;
        else if (angle >= 112.5f && angle < 157.5f)
            return MovementDirection.NorthWest;
        else if (angle >= 157.5f && angle < 202.5f)
            return MovementDirection.West;
        else if (angle >= 202.5f && angle < 247.5f)
            return MovementDirection.SouthWest;
        else if (angle >= 247.5f && angle < 292.5f)
            return MovementDirection.South;
        else if (angle >= 292.5f && angle < 337.5f)
            return MovementDirection.SouthEast;
        else
            return MovementDirection.Idle;
    }

    protected virtual void FaceTarget(Vector3 targetPosition)
    {
        Vector2 direction = (targetPosition - transform.position).normalized;
        UpdateFacingDirection(direction);

        if (!IsMoving)
        {
            Vector2 faceVector = GetDirectionVector(facingDirection);
            targetMoveX = faceVector.x;
            targetMoveY = faceVector.y;
        }
    }

    // client-side facing fallback (when client not authoritative)
    protected virtual void FaceTargetClientSide()
    {
        if (currentTarget == null) return;
        Vector2 dir = (currentTarget.transform.position - transform.position).normalized;
        if (dir.sqrMagnitude > 0.0001f)
        {
            Vector2 faceVector = NormalizeTo8Directions(dir);
            targetMoveX = faceVector.x;
            targetMoveY = faceVector.y;
        }
    }

    protected virtual void SetNewWanderTarget()
    {
        // Generate random point around spawn position
        Vector2 randomDirection = Random.insideUnitCircle.normalized;
        float randomDistance = Random.Range(1f, stats.wanderDistance);
        Vector3 wanderTarget = spawnPosition + new Vector3(randomDirection.x, randomDirection.y, 0) * randomDistance;

        MoveTo(wanderTarget);
    }

    protected virtual Vector2 GetDirectionVector(MovementDirection direction)
    {
        switch (direction)
        {
            case MovementDirection.North: return Vector2.up;
            case MovementDirection.NorthEast: return new Vector2(1, 1).normalized;
            case MovementDirection.East: return Vector2.right;
            case MovementDirection.SouthEast: return new Vector2(1, -1).normalized;
            case MovementDirection.South: return Vector2.down;
            case MovementDirection.SouthWest: return new Vector2(-1, -1).normalized;
            case MovementDirection.West: return Vector2.left;
            case MovementDirection.NorthWest: return new Vector2(-1, 1).normalized;
            default: return Vector2.zero;
        }
    }

    protected virtual void UpdateAnimation()
    {
        if (!SimulateAuthority) return;
        Vector2 vel = rb ? rb.linearVelocity : Vector2.zero;
        float speed = vel.magnitude;
        bool moving = speed > 0.01f;
        Vector2 animDir = moving ? vel.normalized : GetDirectionVector(facingDirection);
        float lerp = 0.3f * Time.deltaTime * 30f;
        currentMoveX = Mathf.Lerp(currentMoveX, animDir.x, lerp);
        currentMoveY = Mathf.Lerp(currentMoveY, animDir.y, lerp);
        if (Mathf.Abs(netSpeed - speed) > 0.001f) netSpeed = speed;
        if (Mathf.Abs(netMoveX - currentMoveX) > 0.001f) netMoveX = currentMoveX;
        if (Mathf.Abs(netMoveY - currentMoveY) > 0.001f) netMoveY = currentMoveY;
        if (netIsMoving != moving) netIsMoving = moving;
        animator.SetFloat(animSpeedHash, netSpeed);
        animator.SetInteger(animStateHash, (int)currentState);
        animator.SetBool("IsMoving", netIsMoving);
        animator.SetFloat(animMoveXHash, netMoveX);
        animator.SetFloat(animMoveYHash, netMoveY);
        if (flipSpriteForDirection && spriteRenderer != null)
        {
            if (currentMoveX > 0.1f) spriteRenderer.flipX = false;
            else if (currentMoveX < -0.1f) spriteRenderer.flipX = true;
        }
    }

    protected GameObject ScanClosestPlayerInRange(float radius)
    {
        var filter = new ContactFilter2D
        {
            useLayerMask = true,
            layerMask = playerLayerMask,
            useTriggers = true
        };
        int hitCount = Physics2D.OverlapCircle(transform.position, radius, filter, detectionHits);
        GameObject best = null;
        float bestDist = float.MaxValue;
        for (int i = 0; i < hitCount; i++)
        {
            var col = detectionHits[i];
            if (!col) continue;
            if (!col.CompareTag("Player")) continue;
            var root = GetPlayerRoot(col);
            if (!root) continue;
            var dmg = root.GetComponent<IDamageable>();
            if (dmg == null || dmg.IsDead) continue;
            float d = Vector2.Distance(transform.position, root.transform.position);
            if (d < bestDist)
            {
                best = root;
                bestDist = d;
            }
        }
        return best;
    }

    protected GameObject GetPlayerRoot(Collider2D col)
    {
        if (col == null) return null;
        var rb2d = col.attachedRigidbody;
        return rb2d ? rb2d.gameObject : col.gameObject;
    }

    protected virtual void OnTriggerEnter2D(Collider2D other)
    {
        if (!SimulateAuthority) return;
        if (!other.CompareTag("Player") || detectionCollider == null) return;
        var root = GetPlayerRoot(other);
        if (root == null) return;
        var damageable = root.GetComponent<IDamageable>();
        if (damageable == null || damageable.IsDead) return;
        if (!playerOverlapCount.TryGetValue(root, out int c))
        {
            playerOverlapCount[root] = 1;
            if (!nearbyPlayers.Contains(root)) nearbyPlayers.Add(root);
        }
        else
        {
            playerOverlapCount[root] = c + 1;
        }
    }

    protected virtual void OnTriggerExit2D(Collider2D other)
    {
        if (!SimulateAuthority) return;
        if (!other.CompareTag("Player") || detectionCollider == null) return;
        var root = GetPlayerRoot(other);
        if (root == null) return;
        if (playerOverlapCount.TryGetValue(root, out int c))
        {
            c -= 1;
            if (c <= 0)
            {
                playerOverlapCount.Remove(root);
                nearbyPlayers.Remove(root);
            }
            else
            {
                playerOverlapCount[root] = c;
            }
        }
    }

    protected virtual bool ShouldChaseTarget()
    {
        if (!SimulateAuthority) return false;
        if (enemyType == EnemyType.Passive) return false;
        GameObject detected = ScanClosestPlayerInRange(stats.detectionRange);
        GameObject closestPlayer = detected ?? GetClosestPlayer();
        if (closestPlayer == null) return false;
        float distance = Vector2.Distance(transform.position, closestPlayer.transform.position);
        switch (enemyType)
        {
            case EnemyType.Hostile:
                {
                    float enterDist = Mathf.Max(0f, stats.aggroRange - chaseEnterMargin);
                    if (distance <= enterDist)
                    {
                        currentTarget = closestPlayer;
                        lastSeenTargetTime = Time.time;
                        acquiredTargetTime = Time.time; 
                        return true;
                    }
                    return false;
                }
            case EnemyType.Neutral:
                {
                    bool shouldChase = (currentTarget != null && distance <= stats.aggroRange) ||
                                       (stats.isNocturnal && IsNightTime() && distance <= stats.aggroRange);
                    if (shouldChase) currentTarget = closestPlayer;
                    return shouldChase;
                }
            default:
                return false;
        }
    }

    protected virtual GameObject GetClosestPlayer()
    {
        if (!SimulateAuthority) return null;

        GameObject closest = null;
        float closestDistance = float.MaxValue;

        // cleanup nulls
        for (int i = nearbyPlayers.Count - 1; i >= 0; i--)
        {
            if (nearbyPlayers[i] == null) nearbyPlayers.RemoveAt(i);
        }

        foreach (var player in nearbyPlayers)
        {
            if (player == null || !IsValidTarget(player)) continue;

            float distance = Vector2.Distance(transform.position, player.transform.position);
            if (distance < closestDistance)
            {
                closest = player;
                closestDistance = distance;
            }
        }

        return closest;
    }

    protected virtual bool IsValidTarget(GameObject target)
    {
        if (target == null) return false;

        var damageable = target.GetComponent<IDamageable>();
        if (damageable == null || damageable.IsDead) return false;

        return true;
    }

    protected virtual void LoseTarget()
    {
        if (!SimulateAuthority) return;
        if (Time.time < acquiredTargetTime + chaseStickTime) return;
        currentTarget = null;
        lastKnownTargetPosition = Vector3.zero;
        spawnPosition = transform.position;
        Stop();
        ChangeState(EnemyState.Idle);
        nextWanderTime = Time.time + Random.Range(1f, stats.wanderInterval);
    }

    public virtual void Attack(GameObject target)
    {
        if (!SimulateAuthority) return;
        if (Time.time < lastAttackTime + stats.attackCooldown) return;
        lastAttackTime = Time.time;
        FaceTarget(target.transform.position);
        if (networkAnimator != null)
        {
            animator.ResetTrigger(animAttackHash);
            animator.SetTrigger(animAttackHash);
        }
        else if (animator != null)
        {
            animator.SetTrigger(animAttackHash);
        }
        var damageable = target.GetComponent<IDamageable>();
        if (damageable != null)
        {
            damageable.TakeDamage(stats.attackDamage, gameObject);
        }
        RpcPlayAttackEffect();
    }

    [ClientRpc]
    protected virtual void RpcPlayAttackEffect()
    {
        // Play attack sound, particles, etc.
    }

    public virtual void TakeDamage(float damage, GameObject attacker)
    {
        if (isDead) return;
        currentHealth -= damage;
        if (attacker != null)
        {
            FaceTarget(attacker.transform.position);
        }
        if (isServer && rb != null && attacker != null)
            StartKnockbackBounce((Vector2)attacker.transform.position);
        if (currentTarget == null && enemyType != EnemyType.Passive)
        {
            currentTarget = attacker;
            ChangeState(EnemyState.Chasing);
        }
        else if (enemyType == EnemyType.Passive)
        {
            currentTarget = attacker;
            ChangeState(EnemyState.Fleeing);
        }
        if (currentHealth <= 0)
        {
            Die();
        }
        RpcPlayDamageEffect();
    }

    [ClientRpc]
    protected virtual void RpcPlayDamageEffect()
    {
        StartCoroutine(DamageFlash());
    }

    protected virtual IEnumerator DamageFlash()
    {
        if (spriteRenderer != null)
        {
            Color original = spriteRenderer.color;
            spriteRenderer.color = Color.red;
            yield return new WaitForSeconds(0.1f);
            spriteRenderer.color = original;
        }
    }

    protected virtual void Die()
    {
        if (isDead) return;

        isDead = true;
        ChangeState(EnemyState.Dead);
        Stop();
        if (collisionCollider) collisionCollider.enabled = false;
        if (detectionCollider) detectionCollider.enabled = false;
        DropItems();
        RpcPlayDeathEffect();
        StartCoroutine(DestroyAfterDelay(3f));
    }

    protected virtual void DropItems()
    {
        if (stats.drops == null) return;

        foreach (var drop in stats.drops)
        {
            if (Random.value <= drop.dropChance)
            {
                int amount = Random.Range(drop.minAmount, drop.maxAmount + 1);
                // ItemManager.Instance.SpawnItem(drop.itemId, transform.position, amount);
            }
        }
    }

    [ClientRpc]
    protected virtual void RpcPlayDeathEffect()
    {
        if (animator != null)
            animator.SetTrigger(animDieHash);
        if (deathFxPrefab)
        {
            var fx = Instantiate(deathFxPrefab, transform.position, Quaternion.identity);
            Destroy(fx, 2.5f);
        }
        if (spriteRenderer != null)
            StartCoroutine(FadeOutAndHide(0.25f));
    }

    protected virtual IEnumerator DestroyAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (isServer)
            NetworkServer.Destroy(gameObject);
        else
            Destroy(gameObject);
    }

    private IEnumerator FadeOutAndHide(float time)
    {
        if (spriteRenderer == null) yield break;
        Color c0 = spriteRenderer.color;
        float t = 0f;
        while (t < time)
        {
            t += Time.deltaTime;
            float a = Mathf.Lerp(c0.a, 0f, t / time);
            spriteRenderer.color = new Color(c0.r, c0.g, c0.b, a);
            yield return null;
        }
        spriteRenderer.enabled = false;
    }

    protected virtual void ChangeState(EnemyState newState)
    {
        if (currentState == newState) return;

        currentState = newState;
        stateTimer = 0f;

        switch (newState)
        {
            case EnemyState.Idle:
                MoveSpeed = stats.moveSpeed;
                break;

            case EnemyState.Chasing:
                MoveSpeed = stats.chaseSpeed;
                break;

            case EnemyState.Attacking:
                Stop(); // đứng lại khi tấn công cận chiến
                MoveSpeed = stats.chaseSpeed;
                break;

            case EnemyState.Fleeing:
                MoveSpeed = stats.moveSpeed;
                break;

            case EnemyState.Dead:
                Stop();
                break;
        }
    }

    protected virtual void OnHealthChanged(float oldHealth, float newHealth)
    {
        // Update health bar, etc.
    }

    protected virtual void OnStateChanged(EnemyState oldState, EnemyState newState)
    {
        if (animator != null)
        {
            animator.SetInteger(animStateHash, (int)newState);
        }
    }

    protected virtual void OnDirectionChanged(MovementDirection oldDirection, MovementDirection newDirection)
    {
        if (IsMoving)
        {
            Vector2 dirVector = GetDirectionVector(newDirection);
            targetMoveX = dirVector.x;
            targetMoveY = dirVector.y;
        }
    }

    protected virtual void OnFacingDirectionChanged(MovementDirection oldFacing, MovementDirection newFacing)
    {
        if (!IsMoving)
        {
            Vector2 faceVector = GetDirectionVector(newFacing);
            targetMoveX = faceVector.x;
            targetMoveY = faceVector.y;
        }
    }

    protected virtual bool IsNightTime()
    {
        return false;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        if (EnsureAnimatorReady())
        {
            animator.SetFloat(animSpeedHash, netSpeed);
            animator.SetFloat(animMoveXHash, netMoveX);
            animator.SetFloat(animMoveYHash, netMoveY);
            animator.SetBool("IsMoving", netIsMoving);
            animator.SetInteger(animStateHash, (int)currentState);
        }
    }

    [Server]
    protected void StartKnockbackBounce(Vector2 attackerPos)
    {
        if (knockbackRoutine != null) StopCoroutine(knockbackRoutine);
        knockbackRoutine = StartCoroutine(KnockbackBounce(attackerPos));
    }

    [Server]
    private IEnumerator KnockbackBounce(Vector2 attackerPos)
    {
        if (rb == null) yield break;
        Vector2 start = rb.position;
        Vector2 dir = (start - attackerPos).normalized;
        if (dir.sqrMagnitude < 0.0001f) dir = Vector2.up;
        Vector2 back = start + dir * knockbackBackDist;
        knockbackEndTime = Time.time + hitStaggerDuration + knockbackOutTime + knockbackReturnTime;
        rb.linearVelocity = Vector2.zero;
        float t = 0f;
        while (t < knockbackOutTime)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / knockbackOutTime);
            float eased = 1f - (1f - k) * (1f - k); 
            rb.MovePosition(Vector2.Lerp(start, back, eased));
            yield return null;
        }
        t = 0f;
        while (t < knockbackReturnTime)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / knockbackReturnTime);
            float eased = k * k;
            rb.MovePosition(Vector2.Lerp(back, start, eased));
            yield return null;
        }
        rb.MovePosition(start);
        rb.linearVelocity = Vector2.zero;
        knockbackRoutine = null;
    }

    void OnSpeedChanged(float _, float v) { if (EnsureAnimatorReady()) animator.SetFloat(animSpeedHash, v); }
    void OnMoveXChanged(float _, float v) { if (EnsureAnimatorReady()) animator.SetFloat(animMoveXHash, v); }
    void OnMoveYChanged(float _, float v) { if (EnsureAnimatorReady()) animator.SetFloat(animMoveYHash, v); }
    void OnIsMovingChanged(bool _, bool v) { if (EnsureAnimatorReady()) animator.SetBool("IsMoving", v); }
    
    protected virtual void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, stats.detectionRange);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, stats.aggroRange);
        Gizmos.color = Color.blue;
        Gizmos.DrawWireSphere(transform.position, stats.attackRange);

        if (Application.isPlaying)
        {
            if (currentMovementDirection != Vector2.zero)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawRay(transform.position, new Vector3(currentMovementDirection.x, currentMovementDirection.y, 0) * 2f);
            }

            Vector2 faceVector = GetDirectionVector(facingDirection);
            if (faceVector != Vector2.zero)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawRay(transform.position, new Vector3(faceVector.x, faceVector.y, 0) * 1.5f);
            }

            if (hasDestination)
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawWireSphere(moveDestination, 0.5f);
                Gizmos.DrawLine(transform.position, moveDestination);
            }
        }
    }
}
