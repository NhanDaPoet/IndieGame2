using System.Collections.Generic;
using UnityEngine;

public class ChunkData
{
    public ChunkCoord coord;
    public BiomeType[,] biome;
    public bool ready;
    public int version;
    public byte[] biomeBytes;
    public PrefabSpawn[] spawns;
    public Dictionary<ushort, int> currentPrefabCounts; 
    public Dictionary<ushort, int> targetPrefabCounts; 

    public ChunkData(ChunkCoord c, int size)
    {
        coord = c;
        version = 1;
        ready = false;
        biome = new BiomeType[size, size];
        currentPrefabCounts = new Dictionary<ushort, int>();
        targetPrefabCounts = new Dictionary<ushort, int>();
    }
}