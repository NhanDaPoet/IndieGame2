using System.Collections.Generic;

public static class RLECompression
{
    public static byte[] Compress(byte[] data)
    {
        var compressed = new List<byte>();
        if (data.Length == 0) return compressed.ToArray();

        byte current = data[0];
        byte count = 1;

        for (int i = 1; i < data.Length; i++)
        {
            if (data[i] == current && count < 255)
            {
                count++;
            }
            else
            {
                compressed.Add(current);
                compressed.Add(count);
                current = data[i];
                count = 1;
            }
        }
        compressed.Add(current);
        compressed.Add(count);

        return compressed.ToArray();
    }

    public static byte[] Decompress(byte[] compressed)
    {
        var decompressed = new List<byte>();
        for (int i = 0; i < compressed.Length; i += 2)
        {
            byte biome = compressed[i];
            byte count = compressed[i + 1];
            for (int j = 0; j < count; j++)
            {
                decompressed.Add(biome);
            }
        }
        return decompressed.ToArray();
    }
}