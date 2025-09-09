using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.Tilemaps;

public class NetworkWorldManager : NetworkBehaviour
{
    [Header("Scene refs (Client build cũng có)")]
    public Grid grid;
    public Tilemap groundTilemap;
    public string worldName = "DefaultWorld";

    [Header("Meta (server set)")]
    [SerializeField] private WorldMeta meta = new WorldMeta();
    [SerializeField] private bool generateOnServerStart = false;

    [Header("World Generation Settings")]
    [Tooltip("Pre-generate world trong radius này khi start server")]
    [SerializeField] private int preGenRadius = 3;

    [Tooltip("Có tự động generate chunks khi player di chuyển không")]
    [SerializeField] private bool dynamicGeneration = true;

    [Header("Performance Settings")]
    [Tooltip("Max chunks to generate per frame during pre-gen")]
    [SerializeField] private int maxPreGenChunksPerFrame = 1;

    [Tooltip("Time budget per frame for chunk generation (ms)")]
    [SerializeField] private float chunkGenTimeBudgetMs = 3f;

    [Tooltip("Player update check interval (seconds)")]
    [SerializeField] private float playerUpdateInterval = 0.5f;

    private BiomeSet biomeSet;
    private PrefabRegistry prefabRegistry;
    private NoiseSettings noiseSettings;
    private BiomeRegionSettings biomeRegion;

    private Dictionary<ChunkCoord, ChunkData> chunks = new();
    private HashSet<NetworkConnectionToClient> readyConnections = new();
    private Dictionary<int, ChunkCoord> playerLastChunk = new();
    private Dictionary<int, int> playerViewRadiusCache = new();

    private bool worldReady = false;
    private Vector2Int mapSizeChunks = new Vector2Int(0, 0);
    private ChunkCoord mapMinChunk;
    private ChunkCoord mapMaxChunk;
    private int lastSettingsSeed = 0;

    public Vector2Int MapSizeChunks => mapSizeChunks;
    public bool WorldReady => worldReady;

    private HashSet<ChunkCoord> generatedChunks = new();
    private float lastPlayerUpdateTime = 0f;
    private bool isPreGenerating = false;
    private Queue<ChunkCoord> dynamicGenQueue = new();
    private bool processingDynamicGen = false;

    // Spawn position cache
    private Vector3? _cachedSpawnPosition = null;

    public override void OnStartServer()
    {
        base.OnStartServer();
        if (!LoadSharedAssets())
        {
            Debug.LogError("Failed to load shared assets!");
            return;
        }
        prefabRegistry.BuildCaches();
        NetworkServer.RegisterHandler<WorldSettingsRequest>(OnWorldSettingsRequest);
        NetworkServer.RegisterHandler<CharacterSettingsMessage>(OnCharacterSettingsMessage);

        if (generateOnServerStart)
        {
            if (meta.seed == 0) meta.seed = Random.Range(int.MinValue, int.MaxValue);
            var dims = GetMapDimensions(MapSize.Medium);
            BeginBoundedGeneration(dims, meta.seed);
        }
    }

    public BiomeRegionSettings BiomeRegion => biomeRegion;

    public override void OnStartClient()
    {
        base.OnStartClient();
        if (!LoadSharedAssets())
        {
            Debug.LogError("Failed to load shared assets on client!");
            return;
        }

        prefabRegistry.BuildCaches();
        NetworkClient.RegisterHandler<ChunkPrefabsMessage>(OnChunkPrefabsMessage);
        NetworkClient.RegisterHandler<ChunkUnloadMessage>(OnChunkUnloadMessage);
        NetworkClient.RegisterHandler<WorldReadyMessage>(OnWorldReadyClient);
    }

    private bool LoadSharedAssets()
    {
        try
        {
            biomeSet = Resources.Load<BiomeSet>(meta.biomeSetResource);
            prefabRegistry = Resources.Load<PrefabRegistry>(meta.prefabRegistryResource);
            noiseSettings = Resources.Load<NoiseSettings>(meta.noiseSettingsResource);
            biomeRegion = Resources.Load<BiomeRegionSettings>(meta.biomeRegionSettingsResource);

            if (!biomeSet || !prefabRegistry || !noiseSettings || !biomeRegion)
            {
                Debug.LogError($"Missing assets: BiomeSet={biomeSet != null}, PrefabRegistry={prefabRegistry != null}, NoiseSettings={noiseSettings != null}, BiomeRegion={biomeRegion != null}");
                return false;
            }
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Exception loading assets: {e.Message}");
            return false;
        }
    }

    [Server]
    private void OnWorldSettingsRequest(NetworkConnectionToClient conn, WorldSettingsRequest req)
    {
        int seed = req.seed != 0 ? req.seed : Random.Range(int.MinValue, int.MaxValue);
        var dims = GetMapDimensions(req.mapSize);
        BeginBoundedGeneration(dims, seed);
    }

    [Server]
    private void BeginBoundedGeneration(Vector2Int dimsChunks, int seed)
    {
        if (isPreGenerating || processingDynamicGen)
        {
            StopAllCoroutines();
            isPreGenerating = false;
            processingDynamicGen = false;
        }
        worldReady = false;
        lastSettingsSeed = seed;
        FlushAll();
        meta.seed = seed;
        mapSizeChunks = dimsChunks;
        int halfWidth = dimsChunks.x / 2;
        int halfHeight = dimsChunks.y / 2;
        mapMinChunk = new ChunkCoord(-halfWidth, -halfHeight);
        mapMaxChunk = new ChunkCoord(halfWidth - 1, halfHeight - 1);
        _cachedSpawnPosition = new Vector3(0, 0, 0);
        dynamicGeneration = false; 
        StartCoroutine(PreGenerateWorldRectangle(mapMinChunk, mapMaxChunk));
    }

    [Server]
    private IEnumerator PreGenerateWorldRectangle(ChunkCoord minC, ChunkCoord maxC)
    {
        isPreGenerating = true;
        int width = maxC.x - minC.x + 1;
        int height = maxC.y - minC.y + 1;
        int total = width * height;
        int done = 0;
        var stopwatch = new System.Diagnostics.Stopwatch();

        Debug.Log($"Pre-generating {total} chunks from {minC} to {maxC}...");

        for (int cx = minC.x; cx <= maxC.x; cx++)
        {
            for (int cy = minC.y; cy <= maxC.y; cy++)
            {
                stopwatch.Restart();

                var coord = new ChunkCoord(cx, cy);
                if (!generatedChunks.Contains(coord))
                {
                    yield return StartCoroutine(GenerateChunkOptimized(coord));
                }

                done++;
                float p = (float)done / total;

                // Send progress to all clients
                NetworkServer.SendToAll(new WorldGeneratingMessage
                {
                    progress01 = p,
                    stage = $"Generating chunks... {done}/{total}"
                });

                // Log progress every 10%
                if (done % Mathf.Max(1, total / 10) == 0)
                {
                    Debug.Log($"Generation progress: {done}/{total} ({p * 100:F1}%)");
                }

                if (stopwatch.ElapsedMilliseconds >= chunkGenTimeBudgetMs)
                {
                    yield return null;
                }
            }
        }

        isPreGenerating = false;
        worldReady = true;

        Debug.Log($"World generation complete! Generated {done} chunks.");

        // Send world ready message
        NetworkServer.SendToAll(new WorldReadyMessage
        {
            widthChunks = mapSizeChunks.x,
            heightChunks = mapSizeChunks.y,
            seed = meta.seed
        });
    }

    private bool IsWithinBounds(ChunkCoord c)
    {
        if (mapSizeChunks.x <= 0 || mapSizeChunks.y <= 0) return true;
        return c.x >= mapMinChunk.x && c.x <= mapMaxChunk.x &&
               c.y >= mapMinChunk.y && c.y <= mapMaxChunk.y;
    }

    [Server]
    private void QueueChunksForPlayer(NetworkConnectionToClient conn, ChunkCoord center)
    {
        int vr = GetPlayerViewRadiusChunks(conn);
        int sentCount = 0;

        for (int dx = -vr; dx <= vr; dx++)
        {
            for (int dy = -vr; dy <= vr; dy++)
            {
                var c = new ChunkCoord(center.x + dx, center.y + dy);

                if (!IsWithinBounds(c)) continue;

                if (chunks.TryGetValue(c, out var cd) && cd.ready)
                {
                    conn.Send(new ChunkPrefabsMessage
                    {
                        coord = c,
                        version = cd.version,
                        spawns = cd.spawns,
                        biomeData = cd.biomeBytes
                    });
                    sentCount++;
                }
                else if (dynamicGeneration && !generatedChunks.Contains(c) && !dynamicGenQueue.Contains(c))
                {
                    dynamicGenQueue.Enqueue(c);
                }
            }
        }

        Debug.Log($"Queued {sentCount} chunks for player at {center}");
    }

    [Server]
    private void OnCharacterSettingsMessage(NetworkConnectionToClient conn, CharacterSettingsMessage msg)
    {
        if (!worldReady)
        {
            Debug.LogWarning("World not ready, ignoring character settings");
            return;
        }

        GameObject player;
        Vector3 spawnPos = _cachedSpawnPosition ?? Vector3.zero;

        if (conn.identity == null)
        {
            var playerPrefab = NetworkManager.singleton.playerPrefab;
            player = Instantiate(playerPrefab, spawnPos, Quaternion.identity);
            NetworkServer.AddPlayerForConnection(conn, player);
            Debug.Log($"[Server] Spawned new player for connection {conn.connectionId} at {spawnPos}");
        }
        else
        {
            player = conn.identity.gameObject;
            player.transform.position = spawnPos;
            Debug.Log($"[Server] Repositioned existing player for connection {conn.connectionId} to {spawnPos}");
        }

        var displayName = string.IsNullOrWhiteSpace(msg.playerName) ? "Player" : msg.playerName;
        var color = msg.color;
        var skinIndex = msg.skinIndex;

        // Apply character customization (commented out as components don't exist)
        //var nameplate = player.GetComponent<PlayerNameplate>();
        //if (nameplate != null)
        //{
        //    nameplate.SetName(displayName);
        //}

        //var appearance = player.GetComponent<PlayerAppearance>();
        //if (appearance != null)
        //{
        //    appearance.Apply(color, skinIndex);
        //}

        // Send chunks around spawn position
        var spawnChunk = WorldToChunk(spawnPos);
        Debug.Log($"Player spawned at chunk {spawnChunk}");
        QueueChunksForPlayer(conn, spawnChunk);

        // Track player position
        playerLastChunk[conn.connectionId] = spawnChunk;
    }

    [Server]
    private IEnumerator GenerateChunkOptimized(ChunkCoord coord)
    {
        if (generatedChunks.Contains(coord))
        {
            yield break;
        }

        var stopwatch = new System.Diagnostics.Stopwatch();
        stopwatch.Start();

        int cs = meta.chunkSize;
        var cd = new ChunkData(coord, cs);
        int x0 = coord.x * cs;
        int y0 = coord.y * cs;

        // Generate biome data
        int tilesProcessed = 0;
        for (int lx = 0; lx < cs; lx++)
        {
            int wx = x0 + lx;
            for (int ly = 0; ly < cs; ly++)
            {
                int wy = y0 + ly;
                cd.biome[lx, ly] = BiomeService.SampleBiome(wx, wy, meta.seed, noiseSettings, biomeRegion, biomeSet);
                tilesProcessed++;

                if (tilesProcessed % 100 == 0 && stopwatch.ElapsedMilliseconds >= 2f)
                {
                    yield return null;
                    stopwatch.Restart();
                }
            }
        }

        // Convert to bytes
        var bytes = new byte[cs * cs];
        int idx = 0;
        for (int ly = 0; ly < cs; ly++)
        {
            for (int lx = 0; lx < cs; lx++)
            {
                bytes[idx++] = (byte)cd.biome[lx, ly];
            }
        }
        cd.biomeBytes = bytes;
        stopwatch.Restart();
        cd.spawns = PrefabScatter.BuildSpawnsForChunk(cd, biomeSet, prefabRegistry, meta.seed);
        if (stopwatch.ElapsedMilliseconds >= 5f)
        {
            yield return null;
        }
        cd.ready = true;
        chunks[coord] = cd;
        generatedChunks.Add(coord);
    }

    [ServerCallback]
    private void Update()
    {
        if (!worldReady || !dynamicGeneration) return;

        if (Time.time - lastPlayerUpdateTime < playerUpdateInterval)
        {
            return;
        }
        lastPlayerUpdateTime = Time.time;
        UpdatePlayerChunks();
        if (!processingDynamicGen && dynamicGenQueue.Count > 0 && !isPreGenerating)
        {
            StartCoroutine(ProcessDynamicGenerationQueue());
        }
    }

    [Server]
    private void UpdatePlayerChunks()
    {
        foreach (var connKvp in NetworkServer.connections)
        {
            var conn = connKvp.Value;
            if (conn?.identity?.transform == null) continue;
            Transform pt = conn.identity.transform;
            var current = WorldToChunk(pt.position);
            if (!playerLastChunk.TryGetValue(conn.connectionId, out var prev) || !prev.Equals(current))
            {
                playerLastChunk[conn.connectionId] = current;
                QueueChunksForPlayer(conn, current);
            }
        }
    }

    [Server]
    private IEnumerator ProcessDynamicGenerationQueue()
    {
        processingDynamicGen = true;
        const int maxChunksPerBatch = 2;
        int processed = 0;
        var stopwatch = new System.Diagnostics.Stopwatch();
        while (dynamicGenQueue.Count > 0 && processed < maxChunksPerBatch)
        {
            stopwatch.Restart();
            var coord = dynamicGenQueue.Dequeue();
            if (!generatedChunks.Contains(coord))
            {
                yield return StartCoroutine(GenerateChunkOptimized(coord));
                processed++;
                NotifyPlayersOfNewChunk(coord);
            }
            if (stopwatch.ElapsedMilliseconds >= chunkGenTimeBudgetMs)
            {
                yield return null;
            }
        }
        processingDynamicGen = false;
    }

    [Server]
    private void NotifyPlayersOfNewChunk(ChunkCoord coord)
    {
        if (!chunks.TryGetValue(coord, out var cd) || !cd.ready)
        {
            return;
        }
        var msg = new ChunkPrefabsMessage
        {
            coord = coord,
            version = cd.version,
            spawns = cd.spawns,
            biomeData = cd.biomeBytes
        };
        BroadcastChunkToInterestedClients(coord, msg);
    }

    [Server]
    private void BroadcastChunkToInterestedClients(ChunkCoord coord, ChunkPrefabsMessage msg)
    {
        int sentCount = 0;
        foreach (var kv in playerLastChunk)
        {
            var conn = NetworkServer.connections.TryGetValue(kv.Key, out var connection) ? connection : null;
            if (conn == null) continue;
            var last = kv.Value;
            int vr = GetPlayerViewRadiusChunks(conn);
            if (Mathf.Abs(coord.x - last.x) <= vr && Mathf.Abs(coord.y - last.y) <= vr)
            {
                conn.Send(msg);
                sentCount++;
            }
        }
        if (sentCount > 0)
        {
            Debug.Log($"Sent chunk {coord} to {sentCount} players");
        }
    }

    public Vector2Int GetMapDimensions(MapSize size)
    {
        switch (size)
        {
            case MapSize.Small: return new Vector2Int(50, 50);
            case MapSize.Medium: return new Vector2Int(100, 100);
            case MapSize.Large: return new Vector2Int(200, 200);
            default: return new Vector2Int(100, 100);
        }
    }

    private int GetPlayerViewRadiusChunks(NetworkConnectionToClient conn)
    {
        if (playerViewRadiusCache.TryGetValue(conn.connectionId, out int cached))
        {
            return cached;
        }
        int radius = 3;
        playerViewRadiusCache[conn.connectionId] = radius;
        return radius;
    }

    public ChunkCoord WorldToChunk(Vector3 worldPos)
    {
        Vector3Int cell = grid.WorldToCell(worldPos);
        int cs = meta.chunkSize;
        int cx = Mathf.FloorToInt((float)cell.x / cs);
        int cy = Mathf.FloorToInt((float)cell.y / cs);
        return new ChunkCoord(cx, cy);
    }

    [Client]
    private void OnWorldReadyClient(WorldReadyMessage msg)
    {
        worldReady = true;
        mapSizeChunks = new Vector2Int(msg.widthChunks, msg.heightChunks);
    }

    [Client]
    private void OnChunkPrefabsMessage(ChunkPrefabsMessage msg)
    {
        if (TilemapChunkBuilder.Instance != null)
        {
            TilemapChunkBuilder.Instance.EnqueueBuild(msg.coord, msg.biomeData);
        }
        else
        {
            Debug.LogWarning("TilemapChunkBuilder.Instance is null!");
        }

        if (msg.spawns != null && msg.spawns.Length > 0 && ClientPrefabRuntime.Instance != null)
        {
            ClientPrefabRuntime.Instance.ApplySpawns(msg.spawns);
        }
        else if (ClientPrefabRuntime.Instance == null)
        {
            Debug.LogWarning("ClientPrefabRuntime.Instance is null!");
        }
    }

    [Client]
    private void OnChunkUnloadMessage(ChunkUnloadMessage msg)
    {
        TilemapChunkBuilder.Instance?.ClearChunk(msg.coord);
        ClientPrefabRuntime.Instance?.DespawnChunk(msg.coord);
    }

    [Server]
    public override void OnStopServer()
    {
        base.OnStopServer();
        FlushAll();
    }

    [Client]
    public override void OnStopClient()
    {
        base.OnStopClient();
        if (!NetworkClient.isConnected)
        {
            Debug.Log("[Client] OnStopClient: client disconnected, flushing state.");
            FlushAll();
        }
    }

    private void FlushAll()
    {
        StopAllCoroutines();
        chunks.Clear();
        generatedChunks.Clear();
        playerLastChunk.Clear();
        playerViewRadiusCache.Clear();
        dynamicGenQueue.Clear();
        processingDynamicGen = false;
        isPreGenerating = false;
        worldReady = false;
        _cachedSpawnPosition = null;
        Debug.Log("NetworkWorldManager flushed completely.");
    }

    // Public Properties & Methods
    public static NetworkWorldManager Instance { get; private set; }
    private void Awake() => Instance = this;

    public WorldMeta Meta => meta;
    public BiomeSet BiomeSet => biomeSet;
    public NoiseSettings Noise => noiseSettings;

    public bool IsChunkGenerated(ChunkCoord coord) => generatedChunks.Contains(coord);
    public bool HasChunkData(ChunkCoord coord) => chunks.ContainsKey(coord);

    public int GeneratedChunkCount => generatedChunks.Count;
    public int QueuedDynamicChunks => dynamicGenQueue.Count;
    public bool IsPreGenerating => isPreGenerating;
    public bool IsProcessingDynamicGen => processingDynamicGen;

    [Server]
    public void OnPrefabDestroyed(ChunkCoord coord, ushort prefabId)
    {
        if (chunks.TryGetValue(coord, out var cd))
        {
            if (cd.currentPrefabCounts.ContainsKey(prefabId))
            {
                cd.currentPrefabCounts[prefabId] = Mathf.Max(0, cd.currentPrefabCounts[prefabId] - 1);
                Debug.Log($"Chunk {coord}: Prefab {prefabId} destroyed, current count: {cd.currentPrefabCounts[prefabId]}");
            }
        }
    }
}