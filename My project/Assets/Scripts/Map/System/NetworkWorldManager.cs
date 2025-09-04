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

    [Header("World Generation Settings")]
    [Tooltip("Pre-generate world trong radius này khi start server")]
    [SerializeField] private int preGenRadius = 3; // Giảm từ 5 xuống 3

    [Tooltip("Có tự động generate chunks khi player di chuyển không")]
    [SerializeField] private bool dynamicGeneration = true;

    [Header("Performance Settings")]
    [Tooltip("Max chunks to generate per frame during pre-gen")]
    [SerializeField] private int maxPreGenChunksPerFrame = 1; // Thay vì 3

    [Tooltip("Time budget per frame for chunk generation (ms)")]
    [SerializeField] private float chunkGenTimeBudgetMs = 3f;

    [Tooltip("Player update check interval (seconds)")]
    [SerializeField] private float playerUpdateInterval = 0.5f; // Update mỗi 0.5s thay vì mỗi frame

    private BiomeSet biomeSet;
    private PrefabRegistry prefabRegistry;
    private NoiseSettings noiseSettings;
    private Dictionary<ChunkCoord, ChunkData> chunks = new();
    private HashSet<NetworkConnectionToClient> readyConnections = new();
    private Dictionary<int, ChunkCoord> playerLastChunk = new();
    private BiomeRegionSettings biomeRegion;

    // Track generated chunks để tránh generate lại
    private HashSet<ChunkCoord> generatedChunks = new();

    // Cache để tránh tính toán lại
    private Dictionary<int, int> playerViewRadiusCache = new();

    // Player tracking optimization
    private float lastPlayerUpdateTime = 0f;
    private bool isPreGenerating = false;

    // Queue cho dynamic generation
    private Queue<ChunkCoord> dynamicGenQueue = new();
    private bool processingDynamicGen = false;

    public override void OnStartServer()
    {
        base.OnStartServer();
        if (meta.seed == 0) meta.seed = Random.Range(int.MinValue, int.MaxValue);

        if (!LoadSharedAssets())
        {
            Debug.LogError("Failed to load shared assets, server cannot start properly");
            return;
        }

        prefabRegistry.BuildCaches();
        NetworkServer.RegisterHandler<ChunkPrefabsMessage>((conn, msg) => { /* no-op on server */ });

        // Pre-generate world nếu được bật
        if (preGenRadius > 0)
        {
            Debug.Log($"Starting pre-generation of world in radius {preGenRadius}...");
            StartCoroutine(PreGenerateWorldOptimized());
        }
    }

    public BiomeRegionSettings BiomeRegion => biomeRegion;

    public override void OnStartClient()
    {
        base.OnStartClient();
        if (!LoadSharedAssets())
        {
            Debug.LogError("Failed to load shared assets on client");
            return;
        }

        prefabRegistry.BuildCaches();
        NetworkClient.RegisterHandler<ChunkPrefabsMessage>(OnChunkPrefabsMessage);
        NetworkClient.RegisterHandler<ChunkUnloadMessage>(OnChunkUnloadMessage);
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
                Debug.LogError($"Missing assets: BiomeSet={biomeSet}, PrefabRegistry={prefabRegistry}, " +
                             $"NoiseSettings={noiseSettings}, BiomeRegion={biomeRegion}");
                return false;
            }

            Debug.Log("Successfully loaded all shared assets.");
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Exception loading shared assets: {e.Message}");
            return false;
        }
    }

    [Server]
    private IEnumerator PreGenerateWorldOptimized()
    {
        isPreGenerating = true;
        int totalChunks = 0;
        int generatedThisFrame = 0;
        var stopwatch = new System.Diagnostics.Stopwatch();

        // Tính tổng số chunks cần generate
        int expectedChunks = 0;
        for (int cx = -preGenRadius; cx <= preGenRadius; cx++)
        {
            for (int cy = -preGenRadius; cy <= preGenRadius; cy++)
            {
                if (Mathf.Abs(cx) + Mathf.Abs(cy) <= preGenRadius)
                {
                    expectedChunks++;
                }
            }
        }

        Debug.Log($"Planning to pre-generate {expectedChunks} chunks...");

        for (int cx = -preGenRadius; cx <= preGenRadius; cx++)
        {
            for (int cy = -preGenRadius; cy <= preGenRadius; cy++)
            {
                if (Mathf.Abs(cx) + Mathf.Abs(cy) > preGenRadius) continue;

                var coord = new ChunkCoord(cx, cy);

                // Chỉ generate nếu chưa có
                if (!generatedChunks.Contains(coord))
                {
                    stopwatch.Reset();
                    stopwatch.Start();

                    yield return StartCoroutine(GenerateChunkOptimized(coord));

                    generatedThisFrame++;
                    totalChunks++;

                    // Log progress
                    if (totalChunks % 10 == 0)
                    {
                        float progress = (float)totalChunks / expectedChunks * 100f;
                        Debug.Log($"Pre-generation progress: {totalChunks}/{expectedChunks} ({progress:F1}%)");
                    }

                    // Yield dựa trên thời gian và số lượng
                    if (generatedThisFrame >= maxPreGenChunksPerFrame ||
                        stopwatch.ElapsedMilliseconds >= chunkGenTimeBudgetMs)
                    {
                        generatedThisFrame = 0;
                        yield return null;
                    }
                }
            }
        }

        isPreGenerating = false;
        Debug.Log($"Pre-generation complete! Generated {totalChunks} chunks successfully.");
    }

    [Server]
    private IEnumerator GenerateChunkOptimized(ChunkCoord coord)
    {
        // Tránh generate lại chunk đã có
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

        // Generate biome data với early yield
        int tilesProcessed = 0;
        for (int lx = 0; lx < cs; lx++)
        {
            int wx = x0 + lx;
            for (int ly = 0; ly < cs; ly++)
            {
                int wy = y0 + ly;
                cd.biome[lx, ly] = BiomeService.SampleBiome(wx, wy, meta.seed, noiseSettings, biomeRegion, biomeSet);
                tilesProcessed++;

                // Yield mỗi 100 tiles để tránh lag
                if (tilesProcessed % 100 == 0 && stopwatch.ElapsedMilliseconds >= 2f)
                {
                    stopwatch.Reset();
                    stopwatch.Start();
                    yield return null;
                }
            }
        }

        // Convert to bytes - optimized
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

        // Generate spawns với time limit
        stopwatch.Reset();
        stopwatch.Start();

        cd.spawns = PrefabScatter.BuildSpawnsForChunk(cd, biomeSet, prefabRegistry, meta.seed);

        if (stopwatch.ElapsedMilliseconds >= 5f) // Nếu spawn generation quá lâu
        {
            yield return null;
        }

        cd.ready = true;

        // Store chunk
        chunks[coord] = cd;
        generatedChunks.Add(coord);

        // Send to interested clients - optimized
        var msg = new ChunkPrefabsMessage
        {
            coord = coord,
            version = cd.version,
            spawns = cd.spawns,
            biomeData = cd.biomeBytes
        };

        BroadcastChunkToInterestedClients(coord, msg);

        Debug.Log($"Generated chunk {coord} with {cd.spawns?.Length ?? 0} spawns in {stopwatch.ElapsedMilliseconds}ms");
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

    // ==== Player tracking & streaming ====

    [ServerCallback]
    private void Update()
    {
        if (!dynamicGeneration) return;

        // Throttle player updates
        if (Time.time - lastPlayerUpdateTime < playerUpdateInterval)
        {
            return;
        }

        lastPlayerUpdateTime = Time.time;
        UpdatePlayerChunks();

        // Process dynamic generation queue
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
    private void QueueChunksForPlayer(NetworkConnectionToClient conn, ChunkCoord center)
    {
        int vr = GetPlayerViewRadiusChunks(conn);

        for (int dx = -vr; dx <= vr; dx++)
        {
            for (int dy = -vr; dy <= vr; dy++)
            {
                var c = new ChunkCoord(center.x + dx, center.y + dy);

                if (!chunks.TryGetValue(c, out var cd))
                {
                    // Queue for generation nếu chưa có và chưa được queue
                    if (dynamicGeneration && !generatedChunks.Contains(c) && !dynamicGenQueue.Contains(c))
                    {
                        dynamicGenQueue.Enqueue(c);
                    }
                }
                else if (cd.ready)
                {
                    // Send existing chunk
                    conn.Send(new ChunkPrefabsMessage
                    {
                        coord = c,
                        version = cd.version,
                        spawns = cd.spawns,
                        biomeData = cd.biomeBytes
                    });
                }
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
            stopwatch.Reset();
            stopwatch.Start();

            var coord = dynamicGenQueue.Dequeue();

            // Double-check if still needed
            if (!generatedChunks.Contains(coord))
            {
                yield return StartCoroutine(GenerateChunkOptimized(coord));
                processed++;

                // Notify interested players
                NotifyPlayersOfNewChunk(coord);
            }

            // Time limit per frame
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

    private int GetPlayerViewRadiusChunks(NetworkConnectionToClient conn)
    {
        if (playerViewRadiusCache.TryGetValue(conn.connectionId, out int cached))
        {
            return cached;
        }

        int radius = 2; // Default radius
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

    // ==== Client handlers ====

    [Client]
    private void OnChunkPrefabsMessage(ChunkPrefabsMessage msg)
    {
        if (TilemapChunkBuilder.Instance != null)
        {
            TilemapChunkBuilder.Instance.EnqueueBuild(msg.coord, msg.biomeData);
        }

        if (msg.spawns != null && msg.spawns.Length > 0 && ClientPrefabRuntime.Instance != null)
        {
            ClientPrefabRuntime.Instance.ApplySpawns(msg.spawns);
        }
    }

    [Client]
    private void OnChunkUnloadMessage(ChunkUnloadMessage msg)
    {
        TilemapChunkBuilder.Instance?.ClearChunk(msg.coord);
        ClientPrefabRuntime.Instance?.DespawnChunk(msg.coord);
    }

    // ==== Cleanup ====

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
        FlushAll();
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

        Debug.Log("NetworkWorldManager flushed completely.");
    }

    // ==== Public Properties & Methods ====

    public static NetworkWorldManager Instance { get; private set; }
    private void Awake() => Instance = this;

    public WorldMeta Meta => meta;
    public BiomeSet BiomeSet => biomeSet;
    public NoiseSettings Noise => noiseSettings;

    public bool IsChunkGenerated(ChunkCoord coord) => generatedChunks.Contains(coord);
    public bool HasChunkData(ChunkCoord coord) => chunks.ContainsKey(coord);

    // Stats cho debugging
    public int GeneratedChunkCount => generatedChunks.Count;
    public int QueuedDynamicChunks => dynamicGenQueue.Count;
    public bool IsPreGenerating => isPreGenerating;
    public bool IsProcessingDynamicGen => processingDynamicGen;
}