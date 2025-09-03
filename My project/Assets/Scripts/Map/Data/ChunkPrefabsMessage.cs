using Mirror;
using UnityEngine;

public struct ChunkPrefabsMessage : NetworkMessage
{
    public ChunkCoord coord;
    public int version;
    public PrefabSpawn[] spawns;
    public byte[] biomeData;
}
public struct ChunkUnloadMessage : NetworkMessage
{
    public ChunkCoord coord;
}

public struct PrefabSpawn
{
    public ushort prefabId;
    public Vector3Int cell;
    public byte variant;
}