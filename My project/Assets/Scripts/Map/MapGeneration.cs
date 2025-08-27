using System.Collections.Generic;
using UnityEngine;

public class MapGeneration : MonoBehaviour
{
    public int chunkSize = 16;
    public int worldSize = 100;
    public Transform player;

    private Dictionary<Vector2, Chunk> loadedChunks = new Dictionary<Vector2, Chunk>();
    private IBiomeProvider biomeProvider;

    void Start()
    {
        biomeProvider = BiomeManager.Get();
        GenerateWorld();
    }

    void Update()
    {
        UpdateChunks();
    }

    void GenerateWorld()
    {
        for (int x = 0; x < worldSize; x++)
        {
            for (int y = 0; y < worldSize; y++)
            {
                Vector2 key = new Vector2(x, y);
                var chunk = new Chunk(x, y, chunkSize);
                chunk.GenerateChunk(biomeProvider);
                loadedChunks[key] = chunk;
                InstantiateChunk(chunk);
            }
        }
    }

    void UpdateChunks()
    {
        int pcx = Mathf.FloorToInt(player.position.x / chunkSize);
        int pcy = Mathf.FloorToInt(player.position.y / chunkSize);

        for (int x = pcx - 1; x <= pcx + 1; x++)
        {
            for (int y = pcy - 1; y <= pcy + 1; y++)
            {
                Vector2 key = new Vector2(x, y);
                if (!loadedChunks.ContainsKey(key))
                {
                    var chunk = new Chunk(x, y, chunkSize);
                    chunk.GenerateChunk(biomeProvider);
                    loadedChunks[key] = chunk;
                    InstantiateChunk(chunk);
                }
            }
        }
    }

    void InstantiateChunk(Chunk chunk)
    {
        if (chunk.resources == null) return;
        foreach (var go in chunk.resources) if (go) Instantiate(go);
    }
}

public class Chunk
{
    public int chunkX;
    public int chunkY;
    public int chunkSize;
    public string[,] chunkBiomes;  
    public GameObject[] resources;

    public Chunk(int x, int y, int size)
    {
        chunkX = x;
        chunkY = y;
        chunkSize = size;
        chunkBiomes = new string[size, size];
    }

    public void GenerateChunk(IBiomeProvider provider)
    {
        // duyệt từng cell trong chunk, hỏi provider biome theo world-pos
        for (int ix = 0; ix < chunkSize; ix++)
        {
            for (int iy = 0; iy < chunkSize; iy++)
            {
                Vector3 worldPos = new Vector3(
                    chunkX * chunkSize + ix,
                    chunkY * chunkSize + iy,
                    0f
                );
                chunkBiomes[ix, iy] = provider != null ? provider.GetBiomeAt(worldPos) : "default";
            }
        }

        // tuỳ bạn generate resources theo chunkBiomes ở đây (nếu cần)
        // resources = ...
    }
}
