using Mirror;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
[RequireComponent(typeof(Collider2D))]
public abstract class ResourceNodeBase : NetworkBehaviour
{
    [Header("Definition")]
    public ResourceDefinition definition;

    [Header("Visuals")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private ParticleSystem stageTransitionParticleSystem;

    [Header("State (Synced)")]
    [SyncVar(hook = nameof(OnRemainingChanged))] private int remaining;
    [SyncVar(hook = nameof(OnStageIndexChanged))] private int stageIndex;
    [SyncVar(hook = nameof(OnOccupiedChanged))] private bool occupied;
    [SyncVar] private uint occupierNetId;

    private Dictionary<int, ParticleSystem> stageParticleSystems = new Dictionary<int, ParticleSystem>();
    private readonly HashSet<uint> contributors = new HashSet<uint>();
    private float sharedAccumulatedTime;
    private float lastSoloHitTime;
    protected ItemPoolManager poolManager;
    protected IBiomeProvider biomeProvider;
    public override void OnStartServer()
    {
        base.OnStartServer();
        remaining = Mathf.Max(1, definition != null ? definition.maxHealth : 1);
        stageIndex = 0;
        occupied = false;
        occupierNetId = 0;
        poolManager = FindFirstObjectByType<ItemPoolManager>();
        biomeProvider = BiomeManager.Get();
    }
    public override void OnStartClient()
    {
        base.OnStartClient();
        UpdateVisualStage();
        InitializeParticleSystem();
    }
    private void InitializeParticleSystem()
    {
        ClearExistingParticleSystems();
        if (definition == null) return;

        int maxStages = definition.GetMaxStageTransitions();
        for (int i = 0; i < maxStages; i++)
        {
            GameObject effectPrefab = definition.GetStageTransitionEffect(i);

            if (effectPrefab != null)
            {
                GameObject particleGO = Instantiate(effectPrefab, transform);
                ParticleSystem ps = particleGO.GetComponent<ParticleSystem>();
                if (ps != null)
                {
                    ConfigureParticleSystem(ps);
                    stageParticleSystems[i] = ps;
                    if (stageTransitionParticleSystem == null) stageTransitionParticleSystem = ps;
                }
                else
                {
                    Destroy(particleGO);
                }
            }
        }

        if (stageParticleSystems.Count == 0 && stageTransitionParticleSystem != null) ConfigureParticleSystem(stageTransitionParticleSystem);
    }

    // ===================== API server được Player gọi qua Command =====================
    [Server]
    public void ServerStartGather(PlayerMovement player)
    {
        if (!NetworkServer.active || definition == null) return;
        if (remaining <= 0 || player == null) return;
        if (!ServerValidateTool(player)) return;
        if (!ServerValidateDistance(player.transform.position)) return;
        uint playerNetId = player.netIdentity != null ? player.netIdentity.netId : 0;
        if (playerNetId == 0) return;
        if (definition.gatherMode == GatherMode.Solo)
        {
            if (!occupied)
            {
                occupied = true;
                occupierNetId = playerNetId;
            }
            else if (occupierNetId != playerNetId)
            {
                return;
            }
        }
        else
        {
            occupied = true;
            contributors.Add(playerNetId);
        }
    }
    [Server]
    public void ServerStopGather(PlayerMovement player)
    {
        if (!NetworkServer.active || definition == null || player == null) return;
        uint playerNetId = player.netIdentity != null ? player.netIdentity.netId : 0;
        if (playerNetId == 0) return;
        if (definition.gatherMode == GatherMode.Solo)
        {
            if (occupied && occupierNetId == playerNetId)
            {
                occupierNetId = 0;
                occupied = false;
            }
        }
        else
        {
            if (contributors.Contains(playerNetId)) contributors.Remove(playerNetId);
            if (contributors.Count == 0) occupied = false;
        }
    }
    [Server]
    public void ServerHit(PlayerMovement player)
    {
        if (!NetworkServer.active || definition == null) return;
        if (remaining <= 0 || player == null) return;
        if (!ServerValidateDistance(player.transform.position)) return;
        if (!ServerValidateTool(player)) return;

        uint playerNetId = player.netIdentity != null ? player.netIdentity.netId : 0;
        if (playerNetId == 0) return;

        if (definition.gatherMode == GatherMode.Solo)
        {
            if (!occupied || occupierNetId != playerNetId) return;
            if (Time.time < lastSoloHitTime + definition.soloHitInterval) return;
            lastSoloHitTime = Time.time;
            ServerApplyProgress(1, player);
        }
        else
        {
            //contributors.Add(playerNetId);
            //occupied = contributors.Count > 0;
            return;
        }
    }
    // ===================== SERVER LOOP =====================
    [ServerCallback]
    private void Update()
    {
        if (!NetworkServer.active || definition == null) return;
        if (remaining <= 0) return;
        if (definition.gatherMode == GatherMode.Shared && occupied && contributors.Count > 0)
        {
            sharedAccumulatedTime += Time.deltaTime;
            if (sharedAccumulatedTime >= definition.sharedTickInterval)
            {
                int workers = contributors.Count;
                int progress = Mathf.Max(1, workers);
                sharedAccumulatedTime = 0f;
                var anyPlayer = GetAnyContributorPlayer();
                ServerApplyProgress(progress, anyPlayer);
            }
        }
    }
    // ===================== SERVER INTERNALS =====================
    [Server]
    private void ServerApplyProgress(int amount, PlayerMovement sourcePlayer)
    {
        if (remaining <= 0) return;
        int previousStageIndex = stageIndex;
        remaining = Mathf.Max(0, remaining - amount);
        UpdateStageByRemaining();
        if (stageIndex != previousStageIndex && stageIndex > 0) RpcPlayStageTransitionEffect(stageIndex - 1);
        if (remaining == 0)
        {
            occupied = false;
            occupierNetId = 0;
            contributors.Clear();
            ServerOnDepleted(sourcePlayer);
        }
    }
    [Server]
    protected virtual void ServerOnDepleted(PlayerMovement sourcePlayer)
    {
        if (definition == null || definition.lootTable == null) return;
        if (poolManager == null) poolManager = FindFirstObjectByType<ItemPoolManager>();
        string biome = biomeProvider != null ? biomeProvider.GetBiomeAt(transform.position) : "default";
        ItemData usingTool = GetPlayerTool(sourcePlayer);
        var rng = new System.Random();
        var drops = definition.lootTable.Roll(rng, biome, usingTool);
        foreach (var drop in drops)
        {
            WorldItem.SpawnWorldItem(drop, transform.position, Vector3.zero);
        }
        RpcSetDepletedVisual();
    }
    // ===================== VALIDATIONS =====================
    [Server]
    private bool ServerValidateTool(PlayerMovement player)
    {
        if (definition == null) return false;
        var inv = player.GetComponent<PlayerInventory>();
        if (inv == null) return false;
        var held = inv.GetSelectedHotbarItem();
        if (held.IsEmpty || held.itemData == null) return false;
        if (held.itemData.itemType != ItemType.Tool) return false;
        if (!string.IsNullOrEmpty(definition.requiredToolMaterial))
        {
            if (!string.Equals(held.itemData.material, definition.requiredToolMaterial, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        if (held.itemData.rarity < definition.minToolRarity)
            return false;
        return true;
    }
    [Server]
    private bool ServerValidateDistance(Vector3 playerPos)
    {
        const float maxDist = 2.5f;
        return (playerPos - transform.position).sqrMagnitude <= (maxDist * maxDist);
    }

    // ===================== GETTERS / UTILS =====================
    [Server]
    private PlayerMovement GetAnyContributorPlayer()
    {
        foreach (var id in contributors)
        {
            if (NetworkServer.spawned.TryGetValue(id, out var identity))
                return identity.GetComponent<PlayerMovement>();
            break;
        }
        return null;
    }
    [Server]
    private ItemData GetPlayerTool(PlayerMovement player)
    {
        var inv = player != null ? player.GetComponent<PlayerInventory>() : null;
        var held = inv != null ? inv.GetSelectedHotbarItem() : new ItemStack();
        return held.itemData;
    }

    // ===================== VISUALS =====================
    private void OnRemainingChanged(int oldValue, int newValue)
    {
        if (newValue == 0) RpcSetDepletedVisual();
    }
    private void OnStageIndexChanged(int oldV, int newV)
    {
        UpdateVisualStage();
    }
    private void OnOccupiedChanged(bool oldV, bool newV)
    {
       
    }
    private void UpdateStageByRemaining()
    {
        if (definition == null || definition.depletionThresholds == null || definition.depletionThresholds.Length == 0)
        {
            stageIndex = 0;
            return;
        }
        float percent = remaining / (float)Mathf.Max(1, definition.maxHealth);
        int idx = 0;
        for (int i = 0; i < definition.depletionThresholds.Length; i++)
        {
            if (percent <= definition.depletionThresholds[i])
                idx = i + 1;
        }
        stageIndex = Mathf.Clamp(
        idx,
        0,
        (definition.depletionSprites != null ? Mathf.Max(0, definition.depletionSprites.Length - 1) : int.MaxValue)
        );
    }
    private void UpdateVisualStage()
    {
        if (spriteRenderer == null || definition == null) return;
        if (definition.depletionSprites != null && definition.depletionSprites.Length > 0)
        {
            int idx = Mathf.Clamp(stageIndex, 0, definition.depletionSprites.Length - 1);
            spriteRenderer.sprite = definition.depletionSprites[idx];
        }
    }

    private void ClearExistingParticleSystems()
    {
        foreach (var kvp in stageParticleSystems)
        {
            if (kvp.Value != null && kvp.Value.gameObject != null)
            {
                if (Application.isPlaying)
                    Destroy(kvp.Value.gameObject);
                else
                    DestroyImmediate(kvp.Value.gameObject);
            }
        }
        stageParticleSystems.Clear();
    }

    private void ConfigureParticleSystem(ParticleSystem ps)
    {
        if (ps == null) return;
        ps.Stop();
        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        var emission = ps.emission;
        emission.enabled = true;
        ps.gameObject.SetActive(false);
    }

    [ClientRpc]
    private void RpcSetDepletedVisual()
    {
        if (spriteRenderer != null && definition != null && definition.depletionSprites != null && definition.depletionSprites.Length > 0) spriteRenderer.sprite = definition.depletionSprites[definition.depletionSprites.Length - 1];
    }

    [ClientRpc]
    private void RpcPlayStageTransitionEffect(int transitionStageIndex)
    {
        StartCoroutine(PlayParticleEffectCoroutine(transitionStageIndex));
    }

    private IEnumerator PlayParticleEffectCoroutine(int transitionStageIndex)
    {
        yield return null;
        ParticleSystem targetPS = GetParticleSystemForStage(transitionStageIndex);
        if (targetPS == null)
        {
            Debug.LogWarning($"[{Time.time:F2}] {gameObject.name}: No particle system found for stage transition {transitionStageIndex}");
            yield break;
        }
        if (!targetPS.gameObject.activeInHierarchy)
        {
            targetPS.gameObject.SetActive(true);
        }
        targetPS.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        yield return null;
        targetPS.Play();
        yield return new WaitForSeconds(0.1f);
        if (targetPS.main.duration > 0)
        {
            yield return new WaitForSeconds(targetPS.main.duration + 1f);
            targetPS.gameObject.SetActive(false);
        }
    }

    private ParticleSystem GetParticleSystemForStage(int stageIndex)
    {
        if (stageParticleSystems.ContainsKey(stageIndex))
        {
            return stageParticleSystems[stageIndex];
        }
        return stageTransitionParticleSystem;
    }

    protected virtual void OnDestroy()
    {
        ClearExistingParticleSystems();
    }

    // ===================== GIZMOS =====================
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, 2.5f);
    }
}