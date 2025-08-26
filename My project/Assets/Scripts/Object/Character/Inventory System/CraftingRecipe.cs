using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "New Crafting Recipe", menuName = "Inventory/Crafting Recipe")]
public class CraftingRecipe : ScriptableObject
{
    [System.Serializable]
    public struct RecipeSlot
    {
        public int itemId;
        public int quantity;
    }
    [SerializeField]
    private RecipeSlot[] gridData = new RecipeSlot[9];

    public ItemStack result;
    public bool isShapeless = false;
    public RecipeSlot[] shapelessRequirements;

    public RecipeSlot[,] Grid
    {
        get
        {
            RecipeSlot[,] grid = new RecipeSlot[3, 3];
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    grid[i, j] = gridData[i * 3 + j];
            return grid;
        }
        set
        {
            if (gridData == null || gridData.Length != 9) gridData = new RecipeSlot[9];
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    gridData[i * 3 + j] = value[i, j];
        }
    }

    public RecipeSlot GetSlot(int row, int col)
    {
        if (row < 0 || row >= 3 || col < 0 || col >= 3)
            return new RecipeSlot { itemId = 0, quantity = 0 };
        if (gridData == null || gridData.Length != 9) InitializeGrid();
        return gridData[row * 3 + col];
    }

    public void SetSlot(int row, int col, RecipeSlot slot)
    {
        if (row < 0 || row >= 3 || col < 0 || col >= 3) return;
        if (gridData == null || gridData.Length != 9) InitializeGrid();
        gridData[row * 3 + col] = slot;
    }

    private void OnEnable()
    {
        InitializeGrid();
    }

    private void InitializeGrid()
    {
        if (gridData == null || gridData.Length != 9)
        {
            gridData = new RecipeSlot[9];
            for (int i = 0; i < 9; i++)
            {
                gridData[i] = new RecipeSlot { itemId = 0, quantity = 0 };
            }
        }
    }

    public Dictionary<int, int> GetShapelessDict()
    {
        var dict = new Dictionary<int, int>();
        if (shapelessRequirements == null) return dict;

        foreach (var r in shapelessRequirements)
        {
            if (r.itemId <= 0 || r.quantity <= 0) continue;
            if (!dict.ContainsKey(r.itemId)) dict[r.itemId] = 0;
            dict[r.itemId] += r.quantity;
        }
        return dict;
    }
}