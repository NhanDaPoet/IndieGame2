using Mirror;
using UnityEngine;
using System.Collections.Generic;
using System.Collections;

// ===========================================
// ENEMY SPAWN DATA
// ===========================================

[System.Serializable]
public class EnemySpawnData
{
    [Header("Enemy Info")]
    public string enemyId;
    public GameObject enemyPrefab;
    public EnemyType enemyType;

    [Header("Spawn Conditions")]
    public BiomeType[] allowedBiomes;
    public int minY = -64;
    public int maxY = 320;
    public bool requiresSkyAccess = false;
    public bool requiresNight = false;
    public bool requiresDay = false;
    public int minLightLevel = 0;
    public int maxLightLevel = 15;

    [Header("Spawn Rates")]
    public float spawnWeight = 1f;
    public int minGroupSize = 1;
    public int maxGroupSize = 4;
    public float spawnCooldown = 30f;

    [Header("Population Control")]
    public int maxWorldPopulation = 50;
    public int maxLocalPopulation = 8; // Within 32 blocks
    public float despawnDistance = 128f;
}

public enum BiomeType
{
    Forest,
    Plains,
    Desert,
    Mountains,
    Swamp,
    Ocean,
    Underground,
    Nether,
    End
}

// ===========================================
// ENEMY MANAGER
// ===========================================

public class EnemyManager : NetworkBehaviour
{
    public static EnemyManager Instance { get; private set; }

    [Header("Spawn Configuration")]
    [SerializeField] private EnemySpawnData[] enemySpawnData;
    [SerializeField] private float spawnCheckInterval = 2f;
    [SerializeField] private float playerNearbyDistance = 32f;
    [SerializeField] private float maxSpawnDistance = 128f;
    [SerializeField] private int maxEnemiesPerPlayer = 20;

    [Header("Day/Night Settings")]
    [SerializeField] private bool enableDayNightSpawning = true;
    [SerializeField] private float nightSpawnMultiplier = 2f;

    // Runtime data
    private Dictionary<string, List<GameObject>> spawnedEnemies = new Dictionary<string, List<GameObject>>();
    private Dictionary<string, float> lastSpawnTimes = new Dictionary<string, float>();
    private List<GameObject> activePlayers = new List<GameObject>();

    // ===========================================
    // INITIALIZATION
    // ===========================================

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    public override void OnStartServer()
    {
        InitializeSpawnSystem();
        InvokeRepeating(nameof(SpawnCheck), spawnCheckInterval, spawnCheckInterval);
        InvokeRepeating(nameof(CleanupDeadEnemies), 30f, 30f);
        InvokeRepeating(nameof(DespawnDistantEnemies), 60f, 60f);
    }

    private void InitializeSpawnSystem()
    {
        // Initialize tracking dictionaries
        foreach (var spawnData in enemySpawnData)
        {
            spawnedEnemies[spawnData.enemyId] = new List<GameObject>();
            lastSpawnTimes[spawnData.enemyId] = 0f;
        }

        Debug.Log($"Enemy Manager initialized with {enemySpawnData.Length} enemy types");
    }

    // ===========================================
    // PLAYER TRACKING
    // ===========================================

    void Update()
    {
        if (!isServer) return;

        // Update active players list
        UpdateActivePlayersList();
    }

    private void UpdateActivePlayersList()
    {
        activePlayers.Clear();

        foreach (var conn in NetworkServer.connections.Values)
        {
            if (conn.identity != null && conn.identity.CompareTag("Player"))
            {
                activePlayers.Add(conn.identity.gameObject);
            }
        }
    }

    // ===========================================
    // SPAWNING SYSTEM
    // ===========================================

    private void SpawnCheck()
    {
        if (activePlayers.Count == 0) return;

        foreach (var player in activePlayers)
        {
            if (player == null) continue;

            CheckSpawnsAroundPlayer(player);
        }
    }

    private void CheckSpawnsAroundPlayer(GameObject player)
    {
        Vector3 playerPos = player.transform.position;

        // Count nearby enemies
        int nearbyEnemyCount = CountEnemiesNearPosition(playerPos, playerNearbyDistance);
        if (nearbyEnemyCount >= maxEnemiesPerPlayer) return;

        // Try to spawn enemies
        foreach (var spawnData in enemySpawnData)
        {
            if (ShouldAttemptSpawn(spawnData, playerPos))
            {
                AttemptSpawn(spawnData, playerPos);
            }
        }
    }

    private bool ShouldAttemptSpawn(EnemySpawnData spawnData, Vector3 playerPos)
    {
        // Check cooldown
        if (Time.time < lastSpawnTimes[spawnData.enemyId] + spawnData.spawnCooldown)
            return false;

        // Check global population limit
        int globalCount = GetGlobalEnemyCount(spawnData.enemyId);
        if (globalCount >= spawnData.maxWorldPopulation)
            return false;

        // Check local population limit
        int localCount = CountSpecificEnemiesNearPosition(spawnData.enemyId, playerPos, playerNearbyDistance);
        if (localCount >= spawnData.maxLocalPopulation)
            return false;

        // Check day/night requirements
        if (enableDayNightSpawning)
        {
            bool isNight = IsNightTime();
            if (spawnData.requiresNight && !isNight) return false;
            if (spawnData.requiresDay && isNight) return false;
        }

        // Random chance based on weight
        float spawnChance = spawnData.spawnWeight * Time.deltaTime;
        if (enableDayNightSpawning && IsNightTime())
        {
            spawnChance *= nightSpawnMultiplier;
        }

        return Random.value < spawnChance;
    }

    private void AttemptSpawn(EnemySpawnData spawnData, Vector3 playerPos)
    {
        List<Vector3> validPositions = FindValidSpawnPositions(spawnData, playerPos, 10);
        if (validPositions.Count == 0) return;
        int groupSize = Random.Range(spawnData.minGroupSize, spawnData.maxGroupSize + 1);
        groupSize = Mathf.Min(groupSize, validPositions.Count);
        for (int i = 0; i < groupSize; i++)
        {
            Vector3 spawnPos = validPositions[Random.Range(0, validPositions.Count)];
            SpawnEnemy(spawnData, spawnPos);
            validPositions.Remove(spawnPos); 
        }
        lastSpawnTimes[spawnData.enemyId] = Time.time;
    }

    private List<Vector3> FindValidSpawnPositions(EnemySpawnData spawnData, Vector3 playerPos, int maxAttempts)
    {
        List<Vector3> validPositions = new List<Vector3>();
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            Vector2 randomDirection = Random.insideUnitCircle.normalized;
            float distance = Random.Range(playerNearbyDistance, maxSpawnDistance);
            Vector3 candidatePos = playerPos + new Vector3(randomDirection.x * distance, 0, randomDirection.y * distance);
            candidatePos = FindGroundPosition(candidatePos);
            if (IsValidSpawnPosition(spawnData, candidatePos, playerPos))
            {
                validPositions.Add(candidatePos);
            }
        }
        return validPositions;
    }

    private Vector3 FindGroundPosition(Vector3 position)
    {
        RaycastHit2D hit = Physics2D.Raycast(new Vector2(position.x, position.y + 10), Vector2.down, 20f, LayerMask.GetMask("Blocks"));
        if (hit.collider != null)
        {
            return new Vector3(position.x, hit.point.y + 1f, position.z);
        }
        return position;
    }

    private bool IsValidSpawnPosition(EnemySpawnData spawnData, Vector3 position, Vector3 playerPos)
    {
        if (position.y < spawnData.minY || position.y > spawnData.maxY)
            return false;
        Collider2D blockCheck = Physics2D.OverlapPoint(position, LayerMask.GetMask("Blocks"));
        if (blockCheck != null) return false;
        RaycastHit2D groundCheck = Physics2D.Raycast(position, Vector2.down, 2f, LayerMask.GetMask("Blocks"));
        if (groundCheck.collider == null) return false;
        BiomeType currentBiome = GetBiomeAtPosition(position);
        if (spawnData.allowedBiomes.Length > 0)
        {
            bool biomeValid = false;
            foreach (var biome in spawnData.allowedBiomes)
            {
                if (biome == currentBiome)
                {
                    biomeValid = true;
                    break;
                }
            }
            if (!biomeValid) return false;
        }
        int lightLevel = GetLightLevelAtPosition(position);
        if (lightLevel < spawnData.minLightLevel || lightLevel > spawnData.maxLightLevel)
            return false;
        if (spawnData.requiresSkyAccess && !HasSkyAccess(position))
            return false;
        float distanceToPlayer = Vector3.Distance(position, playerPos);
        if (distanceToPlayer < playerNearbyDistance || distanceToPlayer > maxSpawnDistance)
            return false;

        return true;
    }

    private GameObject SpawnEnemy(EnemySpawnData spawnData, Vector3 position)
    {
        GameObject enemyObj = Instantiate(spawnData.enemyPrefab, position, Quaternion.identity);
        NetworkServer.Spawn(enemyObj);
        spawnedEnemies[spawnData.enemyId].Add(enemyObj);

        Debug.Log($"Spawned {spawnData.enemyId} at {position}");

        return enemyObj;
    }

    // ===========================================
    // MANUAL SPAWNING API
    // ===========================================

    public GameObject SpawnEnemyById(string enemyId, Vector3 position, bool forceSpawn = false)
    {
        if (!isServer) return null;
        var spawnData = GetSpawnDataById(enemyId);
        if (spawnData == null)
        {
            Debug.LogError($"Enemy spawn data not found for ID: {enemyId}");
            return null;
        }
        if (!forceSpawn && !IsValidSpawnPosition(spawnData, position, position))
        {
            Debug.LogWarning($"Invalid spawn position for {enemyId} at {position}");
            return null;
        }

        return SpawnEnemy(spawnData, position);
    }

    public void SpawnEnemyGroup(string enemyId, Vector3 centerPosition, int count)
    {
        if (!isServer) return;

        for (int i = 0; i < count; i++)
        {
            Vector3 offset = Random.insideUnitCircle.normalized * Random.Range(1f, 3f);
            Vector3 spawnPos = centerPosition + offset;
            spawnPos = FindGroundPosition(spawnPos);

            SpawnEnemyById(enemyId, spawnPos, false);
        }
    }

    // ===========================================
    // CLEANUP SYSTEM
    // ===========================================

    private void CleanupDeadEnemies()
    {
        foreach (var enemyList in spawnedEnemies.Values)
        {
            enemyList.RemoveAll(enemy => enemy == null);
        }
    }

    private void DespawnDistantEnemies()
    {
        foreach (var kvp in spawnedEnemies)
        {
            string enemyId = kvp.Key;
            List<GameObject> enemies = kvp.Value;
            var spawnData = GetSpawnDataById(enemyId);
            if (spawnData == null) continue;
            for (int i = enemies.Count - 1; i >= 0; i--)
            {
                GameObject enemy = enemies[i];
                if (enemy == null)
                {
                    enemies.RemoveAt(i);
                    continue;
                }
                bool tooFar = true;
                foreach (var player in activePlayers)
                {
                    if (player != null)
                    {
                        float distance = Vector3.Distance(enemy.transform.position, player.transform.position);
                        if (distance <= spawnData.despawnDistance)
                        {
                            tooFar = false;
                            break;
                        }
                    }
                }

                if (tooFar)
                {
                    NetworkServer.Destroy(enemy);
                    enemies.RemoveAt(i);
                    Debug.Log($"Despawned distant {enemyId}");
                }
            }
        }
    }

    // ===========================================
    // UTILITY METHODS
    // ===========================================

    private int CountEnemiesNearPosition(Vector3 position, float radius)
    {
        int count = 0;
        foreach (var enemyList in spawnedEnemies.Values)
        {
            foreach (var enemy in enemyList)
            {
                if (enemy != null && Vector3.Distance(enemy.transform.position, position) <= radius)
                {
                    count++;
                }
            }
        }
        return count;
    }

    private int CountSpecificEnemiesNearPosition(string enemyId, Vector3 position, float radius)
    {
        if (!spawnedEnemies.ContainsKey(enemyId)) return 0;

        int count = 0;
        foreach (var enemy in spawnedEnemies[enemyId])
        {
            if (enemy != null && Vector3.Distance(enemy.transform.position, position) <= radius)
            {
                count++;
            }
        }
        return count;
    }

    private int GetGlobalEnemyCount(string enemyId)
    {
        if (!spawnedEnemies.ContainsKey(enemyId)) return 0;

        spawnedEnemies[enemyId].RemoveAll(enemy => enemy == null);
        return spawnedEnemies[enemyId].Count;
    }

    private EnemySpawnData GetSpawnDataById(string enemyId)
    {
        foreach (var spawnData in enemySpawnData)
        {
            if (spawnData.enemyId == enemyId)
                return spawnData;
        }
        return null;
    }

    private bool IsNightTime()
    {
        // Integrate with your day/night cycle system
        // For now, return a simple time-based calculation
        float timeOfDay = (Time.time % 1200f) / 1200f;
        return timeOfDay > 0.5f;
    }

    private BiomeType GetBiomeAtPosition(Vector3 position)
    {
        // Integrate with your biome system
        // This is a placeholder implementation
        return BiomeType.Forest;
    }

    private int GetLightLevelAtPosition(Vector3 position)
    {
        // Integrate with your lighting system
        // 0 = completely dark, 15 = full sunlight
        // For now, return simple day/night based value
        return IsNightTime() ? 0 : 15;
    }

    private bool HasSkyAccess(Vector3 position)
    {
        RaycastHit2D hit = Physics2D.Raycast(position, Vector2.up, 50f, LayerMask.GetMask("Blocks"));
        return hit.collider == null;
    }

    // ===========================================
    // DEBUG & STATISTICS
    // ===========================================

    public void GetEnemyStatistics(out Dictionary<string, int> enemyCounts)
    {
        enemyCounts = new Dictionary<string, int>();

        foreach (var kvp in spawnedEnemies)
        {
            kvp.Value.RemoveAll(enemy => enemy == null);
            enemyCounts[kvp.Key] = kvp.Value.Count;
        }
    }

    [Server]
    public void ClearAllEnemies()
    {
        foreach (var enemyList in spawnedEnemies.Values)
        {
            foreach (var enemy in enemyList)
            {
                if (enemy != null)
                {
                    NetworkServer.Destroy(enemy);
                }
            }
            enemyList.Clear();
        }
        Debug.Log("All enemies cleared");
    }

    [Server]
    public void ClearEnemiesOfType(string enemyId)
    {
        if (spawnedEnemies.ContainsKey(enemyId))
        {
            foreach (var enemy in spawnedEnemies[enemyId])
            {
                if (enemy != null)
                {
                    NetworkServer.Destroy(enemy);
                }
            }
            spawnedEnemies[enemyId].Clear();
            Debug.Log($"Cleared all {enemyId} enemies");
        }
    }

    void OnDrawGizmosSelected()
    {
        if (activePlayers != null)
        {
            Gizmos.color = Color.yellow;
            foreach (var player in activePlayers)
            {
                if (player != null)
                {
                    Gizmos.DrawWireSphere(player.transform.position, playerNearbyDistance);

                    Gizmos.color = Color.red;
                    Gizmos.DrawWireSphere(player.transform.position, maxSpawnDistance);
                    Gizmos.color = Color.yellow;
                }
            }
        }
    }
}