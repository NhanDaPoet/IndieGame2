using UnityEngine;

public static class NoiseService 
{
    public static float FractalPerlin(float x, float y, int seed, float scale, int octaves, float persistence, float lacunarity)
    {
        if (scale <= 0f) scale = 0.0001f;
        float amp = 1f;
        float freq = 1f;
        float sum = 0f;
        float norm = 0f;
        float sx = x + seed * 0.12345f;
        float sy = y - seed * 0.54321f;
        for (int o = 0; o < octaves; o++)
        {
            float nx = (sx * freq) * scale;
            float ny = (sy * freq) * scale;
            float per = Mathf.PerlinNoise(nx, ny);
            sum += per * amp;
            norm += amp;
            amp *= persistence;
            freq *= lacunarity;
        }
        return sum / Mathf.Max(0.0001f, norm); 
    }
}
