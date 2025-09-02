using UnityEngine;

[CreateAssetMenu(menuName = "WorldGen/Noise Settings")]
public class NoiseSettings : ScriptableObject
{
    [Header("Elevation")]
    public float elevationScale = 0.008f;
    public int elevationOctaves = 4;
    public float elevationPersistence = 0.5f;
    public float elevationLacunarity = 2.0f;
    public Vector2 elevationOffset = new Vector2(1000, 2000);

    [Header("Moisture")]
    public float moistureScale = 0.008f;
    public int moistureOctaves = 4;
    public float moisturePersistence = 0.55f;
    public float moistureLacunarity = 2.0f;
    public Vector2 moistureOffset = new Vector2(5000, 6000);

    [Header("Sea Level (0..1)")]
    [Range(0f, 1f)] public float seaLevel = 0.45f;
}
