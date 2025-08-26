using Mirror;
using System.Collections.Generic;
using UnityEngine;
using static CraftingRecipe;

public class CraftingUI : NetworkBehaviour
{
    [SerializeField] private Transform craftingGridContainer;
    [SerializeField] private InventorySlotUI craftingSlotPrefab;
    [SerializeField] private InventorySlotUI resultSlot;
    [SerializeField] private CraftingRecipe[] recipes;
    [SerializeField] private ItemDatabase itemDatabase;

    private CraftingSlotUI[,] craftingSlots = new CraftingSlotUI[3, 3];
    private InventoryUI inventoryUI;
    private PlayerInventory playerInventory;
    private (int offsetX, int offsetY) lastMatchedRecipeOffset; 

    private void Start()
    {
        inventoryUI = GetComponent<InventoryUI>();
        if (inventoryUI == null)
        {
            Debug.LogError("InventoryUI component not found on CraftingUI.");
            inventoryUI = FindFirstObjectByType<InventoryUI>();
        }
        playerInventory = GetComponentInParent<PlayerInventory>();
        InitializeCraftingGrid();
    }

    private void InitializeCraftingGrid()
    {
        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                var slot = Instantiate(craftingSlotPrefab, craftingGridContainer);
                slot.name = $"CraftingSlot_{i}_{j}";
                CraftingSlotUI craftingSlotUI = slot.GetComponent<CraftingSlotUI>();
                if (craftingSlotUI == null)
                {
                    Debug.LogError($"CraftingSlotUI component missing on {slot.name}. Adding one.");
                    craftingSlotUI = slot.gameObject.AddComponent<CraftingSlotUI>();
                }
                craftingSlotUI.Initialize(i, j, this, inventoryUI);
                craftingSlots[i, j] = craftingSlotUI;
            }
        }

        ResultSlotUI resultSlotUI = resultSlot.GetComponent<ResultSlotUI>();
        if (resultSlotUI == null)
        {
            Debug.LogError("ResultSlotUI component missing on resultSlot. Adding one.");
            resultSlotUI = resultSlot.gameObject.AddComponent<ResultSlotUI>();
        }
        resultSlotUI.Initialize(-1, false, inventoryUI, this);
        UpdateCraftingResult();
    }

    private struct MatchData
    {
        public CraftingRecipe recipe;
        public int rMin, rMax, cMin, cMax;   
        public int gMin, gMax, hMin, hMax;     
        public int height, width;           
    }

    public void UpdateCraftingResult()
    {
        ItemStack[,] grid = new ItemStack[3, 3];
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                grid[i, j] = craftingSlots[i, j].GetItemStack();

        ItemStack result = new ItemStack();

        foreach (var recipe in recipes)
        {
            if (recipe.isShapeless)
            {
                int maxCrafts = CalculateMaxPossibleCraftsShapeless(grid, recipe);
                if (maxCrafts > 0)
                {
                    result = new ItemStack(recipe.result.itemId, recipe.result.quantity * maxCrafts, recipe.result.itemData);
                    break;
                }
            }
            else
            {
                if (TryMatchShaped(grid, recipe, out var match))
                {
                    int maxCrafts = CalculateMaxPossibleCrafts(grid, match);
                    if (maxCrafts > 0)
                    {
                        result = new ItemStack(recipe.result.itemId, recipe.result.quantity * maxCrafts, recipe.result.itemData);
                    }
                    break;
                }
            }
        }
        resultSlot.UpdateSlot(result);
    }

    private int CalculateMaxPossibleCrafts(ItemStack[,] grid, MatchData match)
    {
        int minCrafts = int.MaxValue;
        bool hasAny = false;

        for (int dr = 0; dr < match.height; dr++)
        {
            for (int dc = 0; dc < match.width; dc++)
            {
                var rSlot = match.recipe.GetSlot(match.rMin + dr, match.cMin + dc);
                if (rSlot.itemId == 0) continue; 
                hasAny = true;
                var gStack = grid[match.gMin + dr, match.hMin + dc];
                if (gStack.IsEmpty || gStack.itemId != rSlot.itemId) return 0;
                int possible = gStack.quantity / rSlot.quantity;
                if (possible < minCrafts) minCrafts = possible;
            }
        }
        if (!hasAny) return 0;
        return (minCrafts == int.MaxValue) ? 0 : minCrafts;
    }

    private bool CheckRecipeMatch(ItemStack[,] craftingGrid, CraftingRecipe recipe, out (int offsetX, int offsetY) offset)
    {
        offset = (0, 0);
        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                RecipeSlot recipeSlot = recipe.GetSlot(i, j);
                ItemStack gridStack = craftingGrid[i, j];

                if (recipeSlot.itemId == 0 && !gridStack.IsEmpty)
                {
                    return false;
                }
                if (recipeSlot.itemId != 0)
                {
                    if (gridStack.IsEmpty || gridStack.itemId != recipeSlot.itemId || gridStack.quantity < recipeSlot.quantity)
                    {
                        return false;
                    }
                }
            }
        }
        offset = (0, 0);
        return true;
    }

    public void OnResultSlotClicked()
    {
        if (!playerInventory.isLocalPlayer || resultSlot.GetItemStack().IsEmpty) return;
        ItemStack singleRecipeResult = GetSingleRecipeResult();
        if (singleRecipeResult.IsEmpty) return;
        int maxCrafts = GetMaxPossibleCrafts();
        if (maxCrafts <= 0) return;
        playerInventory.CmdTakeCraftingResultClick(singleRecipeResult.itemId, singleRecipeResult.quantity, maxCrafts);
    }

    public ItemStack GetSingleRecipeResult()
    {
        ItemStack[,] grid = new ItemStack[3, 3];
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                grid[i, j] = craftingSlots[i, j].GetItemStack();
        foreach (var recipe in recipes)
        {
            if (recipe.isShapeless)
            {
                if (CalculateMaxPossibleCraftsShapeless(grid, recipe) > 0)
                    return recipe.result;
            }
            else
            {
                if (TryMatchShaped(grid, recipe, out _))
                    return recipe.result;
            }
        }
        return new ItemStack();
    }

    private bool GetRecipeBounds(CraftingRecipe recipe, out int rMin, out int rMax, out int cMin, out int cMax)
    {
        rMin = 3; rMax = -1; cMin = 3; cMax = -1;
        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                var slot = recipe.GetSlot(i, j);
                if (slot.itemId != 0)
                {
                    if (i < rMin) rMin = i;
                    if (i > rMax) rMax = i;
                    if (j < cMin) cMin = j;
                    if (j > cMax) cMax = j;
                }
            }
        }
        return rMax >= rMin && cMax >= cMin; 
    }

    private bool GetGridBounds(ItemStack[,] grid, out int gMin, out int gMax, out int hMin, out int hMax)
    {
        gMin = 3; gMax = -1; hMin = 3; hMax = -1;
        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                if (!grid[i, j].IsEmpty)
                {
                    if (i < gMin) gMin = i;
                    if (i > gMax) gMax = i;
                    if (j < hMin) hMin = j;
                    if (j > hMax) hMax = j;
                }
            }
        }
        return gMax >= gMin && hMax >= hMin; 
    }

    private void ConsumeShapelessMaterials(ItemStack[,] grid, int recipeCount, CraftingRecipe recipe)
    {
        var req = recipe.GetShapelessDict();
        if (req.Count == 0 || recipeCount <= 0) return;
        var toConsume = new Dictionary<int, int>();
        foreach (var kv in req) toConsume[kv.Key] = kv.Value * recipeCount;
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
            {
                var s = grid[i, j];
                if (s.IsEmpty || !toConsume.ContainsKey(s.itemId)) continue;
                int need = toConsume[s.itemId];
                if (need <= 0) continue;
                int take = Mathf.Min(need, s.quantity);
                int remain = s.quantity - take;
                toConsume[s.itemId] -= take;
                if (remain <= 0)
                    craftingSlots[i, j].UpdateSlot(new ItemStack());
                else
                    craftingSlots[i, j].UpdateSlot(new ItemStack(s.itemId, remain, s.itemData));
            }
    }

    private int CalculateMaxPossibleCraftsShapeless(ItemStack[,] grid, CraftingRecipe recipe)
    {
        var req = recipe.GetShapelessDict();
        if (req.Count == 0) return 0;
        var have = BuildGridCounts(grid);
        if (have.Count == 0) return 0;
        foreach (var kv in have)
            if (!req.ContainsKey(kv.Key))
                return 0;
        int minTimes = int.MaxValue;
        foreach (var kv in req)
        {
            int id = kv.Key;
            int need = kv.Value;
            if (!have.TryGetValue(id, out int haveQty)) return 0;
            int can = haveQty / need;
            if (can < minTimes) minTimes = can;
        }
        return (minTimes == int.MaxValue) ? 0 : minTimes;
    }

    private bool TryMatchShaped(ItemStack[,] grid, CraftingRecipe recipe, out MatchData match)
    {
        match = default;
        if (!GetRecipeBounds(recipe, out int rMin, out int rMax, out int cMin, out int cMax))
            return false; 
        if (!GetGridBounds(grid, out int gMin, out int gMax, out int hMin, out int hMax))
            return false; 
        int rHeight = rMax - rMin + 1;
        int rWidth = cMax - cMin + 1;
        int gHeight = gMax - gMin + 1;
        int gWidth = hMax - hMin + 1;
        if (rHeight != gHeight || rWidth != gWidth)
            return false;
        for (int dr = 0; dr < rHeight; dr++)
        {
            for (int dc = 0; dc < rWidth; dc++)
            {
                var rSlot = recipe.GetSlot(rMin + dr, cMin + dc);
                var gStack = grid[gMin + dr, hMin + dc];
                if (rSlot.itemId == 0)
                {
                    if (!gStack.IsEmpty) return false;
                }
                else
                {
                    if (gStack.IsEmpty) return false;
                    if (gStack.itemId != rSlot.itemId) return false;
                    if (gStack.quantity < rSlot.quantity) return false;
                }
            }
        }
        match = new MatchData
        {
            recipe = recipe,
            rMin = rMin,
            rMax = rMax,
            cMin = cMin,
            cMax = cMax,
            gMin = gMin,
            gMax = gMax,
            hMin = hMin,
            hMax = hMax,
            height = rHeight,
            width = rWidth
        };
        return true;
    }

    public int GetMaxPossibleCrafts()
    {
        ItemStack[,] grid = new ItemStack[3, 3];
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                grid[i, j] = craftingSlots[i, j].GetItemStack();
        foreach (var recipe in recipes)
        {
            if (recipe.isShapeless)
            {
                int max = CalculateMaxPossibleCraftsShapeless(grid, recipe);
                if (max > 0) return max;
            }
            else
            {
                if (TryMatchShaped(grid, recipe, out var match))
                    return CalculateMaxPossibleCrafts(grid, match);
            }
        }
        return 0;
    }

    public void TakeResultItems(int quantity, int targetSlotIndex, bool targetIsHotbar)
    {
        if (!playerInventory.isLocalPlayer || quantity <= 0) return;

        ItemStack resultStack = resultSlot.GetItemStack();
        if (resultStack.IsEmpty) return;

        ItemStack singleRecipeResult = GetSingleRecipeResult();
        if (singleRecipeResult.IsEmpty) return;

        int recipesNeeded = Mathf.CeilToInt((float)quantity / singleRecipeResult.quantity);
        int maxPossibleCrafts = GetMaxPossibleCrafts();
        int actualRecipes = Mathf.Min(recipesNeeded, maxPossibleCrafts);

        if (actualRecipes <= 0) return;
        int actualQuantity = actualRecipes * singleRecipeResult.quantity;
        playerInventory.CmdTakeCraftingResultDrag(singleRecipeResult.itemId, actualQuantity, targetSlotIndex, targetIsHotbar, actualRecipes);
    }

    public void TakeResultItemsAndDrop(int quantity)
    {
        if (!playerInventory.isLocalPlayer || quantity <= 0) return;
        ItemStack resultStack = resultSlot.GetItemStack();
        if (resultStack.IsEmpty) return;
        ItemStack singleRecipeResult = GetSingleRecipeResult();
        if (singleRecipeResult.IsEmpty) return;
        int recipesNeeded = Mathf.CeilToInt((float)quantity / singleRecipeResult.quantity);
        int maxPossibleCrafts = GetMaxPossibleCrafts();
        int actualRecipes = Mathf.Min(recipesNeeded, maxPossibleCrafts);
        if (actualRecipes <= 0) return;
        int actualQuantity = actualRecipes * singleRecipeResult.quantity;
        playerInventory.CmdTakeCraftingResultDrop(singleRecipeResult.itemId, actualQuantity, actualRecipes);
    }

    public void ConsumeRecipeMaterials(int recipeCount)
    {
        ItemStack[,] grid = new ItemStack[3, 3];
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                grid[i, j] = craftingSlots[i, j].GetItemStack();
        foreach (var recipe in recipes)
        {
            if (recipe.isShapeless)
            {
                int max = CalculateMaxPossibleCraftsShapeless(grid, recipe);
                if (max <= 0) continue;
                recipeCount = Mathf.Min(recipeCount, max);
                ConsumeShapelessMaterials(grid, recipeCount, recipe);
                break;
            }
            else
            {
                if (TryMatchShaped(grid, recipe, out var match))
                {
                    int max = CalculateMaxPossibleCrafts(grid, match);
                    if (max <= 0) continue;
                    recipeCount = Mathf.Min(recipeCount, max);
                    for (int dr = 0; dr < match.height; dr++)
                        for (int dc = 0; dc < match.width; dc++)
                        {
                            var rSlot = recipe.GetSlot(match.rMin + dr, match.cMin + dc);
                            if (rSlot.itemId == 0) continue;

                            var gStack = grid[match.gMin + dr, match.hMin + dc];
                            int consume = rSlot.quantity * recipeCount;
                            int newQty = gStack.quantity - consume;

                            if (newQty <= 0)
                                craftingSlots[match.gMin + dr, match.hMin + dc].UpdateSlot(new ItemStack());
                            else
                                craftingSlots[match.gMin + dr, match.hMin + dc]
                                    .UpdateSlot(new ItemStack(gStack.itemId, newQty, gStack.itemData));
                        }
                    break;
                }
            }
        }
    }

    private Dictionary<int, int> BuildGridCounts(ItemStack[,] grid)
    {
        var counts = new Dictionary<int, int>();
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
            {
                var s = grid[i, j];
                if (s.IsEmpty || s.itemId <= 0) continue;
                if (!counts.ContainsKey(s.itemId)) counts[s.itemId] = 0;
                counts[s.itemId] += s.quantity;
            }
        return counts;
    }

    // MODIFIED METHOD: Consume materials for a single recipe craft
    private void ConsumeSingleRecipeMaterials()
    {
        ConsumeRecipeMaterials(1);
    }

    // NEW METHOD: Consume all materials for maximum crafts
    private void ConsumeAllCraftingMaterials()
    {
        int maxCrafts = GetMaxPossibleCrafts();
        if (maxCrafts > 0)
        {
            ConsumeRecipeMaterials(maxCrafts);
        }
    }

    // KEEP ORIGINAL METHOD: For compatibility with existing network calls
    public void ConsumeCraftingMaterials()
    {
        ConsumeSingleRecipeMaterials();
        UpdateCraftingResult();
    }
}