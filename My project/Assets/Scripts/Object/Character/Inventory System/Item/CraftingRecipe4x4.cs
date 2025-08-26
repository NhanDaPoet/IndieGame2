using System;
using UnityEngine;

[CreateAssetMenu(fileName = "Recipe4x4", menuName = "Crafting/Recipe 4x4")]
public class CraftingRecipe4x4 : ScriptableObject
{
    public bool isShapeless = false;

    [Tooltip("Lưới 4x4: index = row*4 + col")]
    public GridCell[] gridData = new GridCell[16];

    [Serializable]
    public struct GridCell
    {
        public int itemId;
        public int quantity;
    }

    [Header("Result")]
    public int resultItemId;
    public int resultQuantity = 1;

    [Header("Station")]
    public bool requiresCraftingTable4x4 = true;

    // === Match helpers (shaped) ===
    public bool MatchesShaped(int[,] ids, int[,] qtys)
    {
        // ids/qtys 4x4 từ UI, cho phép auto-trim bounding và đặt lệch góc
        // Chuẩn hoá: trim recipe & trim input rồi so sánh pattern (có thể mở rộng rotate/mirror)
        Normalize4x4(gridData, out int[,] rIds, out int[,] rQtys, out int rH, out int rW);
        Normalize4x4(ids, qtys, out int[,] inIds, out int[,] inQtys, out int inH, out int inW);

        if (rH != inH || rW != inW) return false;

        for (int i = 0; i < rH; i++)
            for (int j = 0; j < rW; j++)
            {
                if (rIds[i, j] == 0 && inIds[i, j] == 0) continue;
                if (rIds[i, j] == 0 || inIds[i, j] == 0) return false;
                if (rIds[i, j] != inIds[i, j]) return false;
                if (rQtys[i, j] > 0 && inQtys[i, j] < rQtys[i, j]) return false;
            }
        return true;
    }

    public bool MatchesShapeless(int[] flatInputIds, int[] flatInputQty)
    {
        // Gom recipe -> dict id->qty; gom input -> dict; so sánh >=
        System.Collections.Generic.Dictionary<int, int> need = new();
        for (int i = 0; i < 16; i++)
        {
            int id = gridData[i].itemId;
            int q = gridData[i].quantity;
            if (id <= 0 || q <= 0) continue;
            if (!need.ContainsKey(id)) need[id] = 0;
            need[id] += q;
        }

        System.Collections.Generic.Dictionary<int, int> have = new();
        for (int i = 0; i < 16; i++)
        {
            int id = flatInputIds[i];
            int q = flatInputQty[i];
            if (id <= 0 || q <= 0) continue;
            if (!have.ContainsKey(id)) have[id] = 0;
            have[id] += q;
        }

        foreach (var kv in need)
        {
            if (!have.TryGetValue(kv.Key, out int got) || got < kv.Value) return false;
        }
        return true;
    }

    private static void Normalize4x4(GridCell[] src, out int[,] ids, out int[,] qtys, out int h, out int w)
    {
        int[,] sIds = new int[4, 4];
        int[,] sQty = new int[4, 4];
        for (int i = 0; i < 4; i++)
            for (int j = 0; j < 4; j++)
            {
                int idx = i * 4 + j;
                sIds[i, j] = src[idx].itemId;
                sQty[i, j] = src[idx].quantity;
            }
        Normalize4x4(sIds, sQty, out ids, out qtys, out h, out w);
    }

    private static void Normalize4x4(int[,] sIds, int[,] sQty, out int[,] nIds, out int[,] nQty, out int h, out int w)
    {
        int rMin = 4, rMax = -1, cMin = 4, cMax = -1;
        for (int i = 0; i < 4; i++)
            for (int j = 0; j < 4; j++)
                if (sIds[i, j] != 0)
                { rMin = Mathf.Min(rMin, i); rMax = Mathf.Max(rMax, i); cMin = Mathf.Min(cMin, j); cMax = Mathf.Max(cMax, j); }
        if (rMax < rMin) { nIds = new int[1, 1]; nQty = new int[1, 1]; h = 1; w = 1; return; }
        h = rMax - rMin + 1; w = cMax - cMin + 1;
        nIds = new int[h, w];
        nQty = new int[h, w];
        for (int i = 0; i < h; i++)
            for (int j = 0; j < w; j++)
            { nIds[i, j] = sIds[rMin + i, cMin + j]; nQty[i, j] = sQty[rMin + i, cMin + j]; }
    }
}
