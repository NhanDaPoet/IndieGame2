using UnityEngine;

public static class RegionService
{
    private static int Hash(int seed, int x, int y)
    {
        unchecked
        {
            int h = seed;
            h = (h * 73856093) ^ x;
            h = (h * 19349663) ^ y;
            h ^= (h << 13); h ^= (h >> 17); h ^= (h << 5);
            return h;
        }
    }

    private static float Rand01(int h)
    {
        unchecked { return (uint)h / (float)uint.MaxValue; }
    }

    private static Vector2 JitteredCenter(int seed, int cellX, int cellY, int regionSize, float jitter)
    {
        float j = Mathf.Clamp01(jitter);
        float r = regionSize * 0.5f;
        int hx = Hash(seed ^ unchecked((int)0x55555555), cellX, cellY);
        int hy = Hash(seed ^ unchecked((int)0xAAAAAAAA), cellX, cellY);
        float ox = (Rand01(hx) * 2f - 1f) * r * j;
        float oy = (Rand01(hy) * 2f - 1f) * r * j;
        return new Vector2(cellX * regionSize + r + ox, cellY * regionSize + r + oy);
    }

    public static Vector2Int NearestRegionCell(int seed, int x, int y, int regionSize, float jitter)
    {
        int cx = Mathf.FloorToInt((float)x / regionSize);
        int cy = Mathf.FloorToInt((float)y / regionSize);

        float best = float.MaxValue;
        Vector2Int bestCell = new(cx, cy);

        for (int dx = 0; dx <= 1; dx++)
            for (int dy = 0; dy <= 1; dy++)
            {
                int nx = cx + dx, ny = cy + dy;
                Vector2 c = JitteredCenter(seed, nx, ny, regionSize, jitter);
                float d = (new Vector2(x, y) - c).sqrMagnitude;
                if (d < best) { best = d; bestCell = new Vector2Int(nx, ny); }
            }
        return bestCell;
    }

    public static int RegionId(int seed, Vector2Int cell) => Hash(seed, cell.x, cell.y);
}
