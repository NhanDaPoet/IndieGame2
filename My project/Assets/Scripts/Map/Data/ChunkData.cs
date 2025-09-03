using UnityEngine;

public class ChunkData 
{
    public ChunkCoord coord;
    public BiomeType[,] biome; 
    public bool ready;
    public int version;
    public byte[] biomeBytes;
    public PrefabSpawn[] spawns;

    public ChunkData(ChunkCoord c, int size)
    {
        coord = c;
        version = 1;
        ready = false;
        biome = new BiomeType[size, size];
    }
}
