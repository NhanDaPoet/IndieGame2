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

    private BiomeSet biomeSet;
    private PrefabRegistry prefabRegistry;
    private NoiseSettings noiseSettings;
    private Dictionary<ChunkCoord, ChunkData> chunks = new();
    private HashSet<NetworkConnectionToClient> readyConnections = new();
    private Dictionary<int, ChunkCoord> playerLastChunk = new();
    private BiomeRegionSettings biomeRegion;

    public override void OnStartServer()
    {
        base.OnStartServer();
        if (meta.seed == 0) meta.seed = Random.Range(int.MinValue, int.MaxValue);
        LoadSharedAssets(); 
        prefabRegistry.BuildCaches();
        NetworkServer.RegisterHandler<ChunkPrefabsMessage>((conn, msg) => { /* no-op on server */ });
        StartCoroutine(GenerateSpawnRing(meta.minPlayableRadiusChunks));
    }
    public BiomeRegionSettings BiomeRegion => biomeRegion;
    public override void OnStartClient()
    {
        base.OnStartClient();
        LoadSharedAssets();
        prefabRegistry.BuildCaches();
        NetworkClient.RegisterHandler<ChunkPrefabsMessage>(OnChunkPrefabsMessage);
        NetworkClient.RegisterHandler<ChunkUnloadMessage>(OnChunkUnloadMessage);
    }

    private void LoadSharedAssets()
    {
        biomeSet = Resources.Load<BiomeSet>(meta.biomeSetResource);
        prefabRegistry = Resources.Load<PrefabRegistry>(meta.prefabRegistryResource);
        noiseSettings = Resources.Load<NoiseSettings>(meta.noiseSettingsResource);
        biomeRegion = Resources.Load<BiomeRegionSettings>(meta.biomeRegionSettingsResource);
        if (!biomeSet || !prefabRegistry || !noiseSettings)
        {
            Debug.LogError("Missing shared assets (BiomeSet/PrefabRegistry/NoiseSettings) in Resources.");
        }
    }

    [Server]
    private IEnumerator GenerateSpawnRing(int r)
    {
        int cs = meta.chunkSize;
        for (int cx = -r; cx <= r; cx++)
        {
            for (int cy = -r; cy <= r; cy++)
            {
                if (Mathf.Abs(cx) + Mathf.Abs(cy) > r) continue;
                var coord = new ChunkCoord(cx, cy);
                if (!chunks.ContainsKey(coord))
                {
                    yield return StartCoroutine(GenerateChunk(coord));
                }
                yield return null; 
            }
        }
    }

    [Server]
    private IEnumerator GenerateChunk(ChunkCoord coord)
    {
        int cs = meta.chunkSize;
        var cd = new ChunkData(coord, cs);
        int x0 = coord.x * cs;
        int y0 = coord.y * cs;
        for (int lx = 0; lx < cs; lx++)
        {
            int wx = x0 + lx;
            for (int ly = 0; ly < cs; ly++)
            {
                int wy = y0 + ly;
                cd.biome[lx, ly] = BiomeService.SampleBiome(wx, wy, meta.seed, noiseSettings, biomeRegion, biomeSet);
            }
        }
        cd.ready = true;
        chunks[coord] = cd;
        var spawns = PrefabScatter.BuildSpawnsForChunk(cd, biomeSet, prefabRegistry, meta.seed);
        var msg = new ChunkPrefabsMessage { coord = coord, version = cd.version, spawns = spawns };
        BroadcastChunkToInterestedClients(coord, msg);
        yield break;
    }

    [Server]
    private void BroadcastChunkToInterestedClients(ChunkCoord coord, ChunkPrefabsMessage msg)
    {
        foreach (var kv in playerLastChunk)
        {
            var conn = NetworkServer.connections[kv.Key];
            if (conn == null) continue;
            var last = kv.Value;
            int vr = GetPlayerViewRadiusChunks(conn);
            if (Mathf.Abs(coord.x - last.x) <= vr && Mathf.Abs(coord.y - last.y) <= vr)
            {
                conn.Send(msg);
            }
        }
    }

    // ==== Player tracking & streaming ====

    [ServerCallback]
    private void Update()
    {
        foreach (var connKvp in NetworkServer.connections)
        {
            var conn = connKvp.Value;
            if (conn == null || conn.identity == null) continue;
            Transform pt = conn.identity.transform; 
            var current = WorldToChunk(pt.position);
            if (!playerLastChunk.TryGetValue(conn.connectionId, out var prev) || !prev.Equals(current))
            {
                playerLastChunk[conn.connectionId] = current;
                StreamChunksForPlayer(conn, current);
            }
        }
    }

    [Server]
    private void StreamChunksForPlayer(NetworkConnectionToClient conn, ChunkCoord center)
    {
        int vr = GetPlayerViewRadiusChunks(conn);
        for (int dx = -vr; dx <= vr; dx++)
        {
            for (int dy = -vr; dy <= vr; dy++)
            {
                var c = new ChunkCoord(center.x + dx, center.y + dy);
                if (!chunks.TryGetValue(c, out var cd))
                {
                    StartCoroutine(GenerateChunk(c));
                }
                else
                {
                    var spawns = PrefabScatter.BuildSpawnsForChunk(cd, biomeSet, prefabRegistry, meta.seed);
                    conn.Send(new ChunkPrefabsMessage { coord = c, version = cd.version, spawns = spawns });
                }
            }
        }
    }

    private int GetPlayerViewRadiusChunks(NetworkConnectionToClient conn)
    {
        // TODO: every Player has difference setting - default setting 
        return 2;
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
        TilemapChunkBuilder.Instance.BuildGroundForChunk(msg.coord);
        ClientPrefabRuntime.Instance.ApplySpawns(msg.spawns);
    }

    [Client]
    private void OnChunkUnloadMessage(ChunkUnloadMessage msg)
    {
        TilemapChunkBuilder.Instance.ClearChunk(msg.coord);
        ClientPrefabRuntime.Instance.DespawnChunk(msg.coord);
    }

    public static NetworkWorldManager Instance { get; private set; }
    private void Awake() => Instance = this;

    public WorldMeta Meta => meta;
    public BiomeSet BiomeSet => biomeSet;
    public NoiseSettings Noise => noiseSettings;
}
