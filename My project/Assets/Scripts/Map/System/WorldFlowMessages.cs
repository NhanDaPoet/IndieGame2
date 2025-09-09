using Mirror;
using UnityEngine;

public enum MapSize
{
    Small = 0,
    Medium = 1,
    Large = 2
}

public struct WorldSettingsRequest : NetworkMessage
{
    public MapSize mapSize;
    public int seed;
    // TODO: biome preset, difficulty, resource multipliers...
}

public struct WorldGeneratingMessage : NetworkMessage
{
    public float progress01;
    public string stage;
}

public struct WorldReadyMessage : NetworkMessage
{
    public int widthChunks;
    public int heightChunks;
    public int seed;
}

public struct CharacterSettingsMessage : NetworkMessage
{
    public string playerName;
    public Color color;
    public int skinIndex; 
}
