using Mirror;
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
    private (int offsetX, int offsetY) lastMatchedRecipeOffset; // Lưu vị trí khớp của công thức

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

    public void UpdateCraftingResult()
    {
        ItemStack[,] currentGrid = new ItemStack[3, 3];
        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                currentGrid[i, j] = craftingSlots[i, j].GetItemStack();
            }
        }

        ItemStack result = new ItemStack();
        lastMatchedRecipeOffset = (0, 0);

        foreach (var recipe in recipes)
        {
            if (CheckRecipeMatch(currentGrid, recipe, out var offset))
            {
                // FIXED: Calculate maximum possible crafts based on available materials
                int maxCrafts = CalculateMaxPossibleCrafts(currentGrid, recipe);
                if (maxCrafts > 0)
                {
                    result = new ItemStack(
                        recipe.result.itemId,
                        recipe.result.quantity * maxCrafts,
                        recipe.result.itemData
                    );
                }
                lastMatchedRecipeOffset = offset;
                break;
            }
        }

        resultSlot.UpdateSlot(result);
    }

    // NEW METHOD: Calculate maximum number of times a recipe can be crafted
    private int CalculateMaxPossibleCrafts(ItemStack[,] craftingGrid, CraftingRecipe recipe)
    {
        int minPossibleCrafts = int.MaxValue;
        bool hasRequiredItems = false;

        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                RecipeSlot recipeSlot = recipe.GetSlot(i, j);
                if (recipeSlot.itemId != 0) // If this slot requires an item
                {
                    hasRequiredItems = true;
                    ItemStack gridStack = craftingGrid[i, j];

                    if (gridStack.IsEmpty || gridStack.itemId != recipeSlot.itemId)
                    {
                        return 0; // Recipe can't be made
                    }

                    // Calculate how many times this slot can satisfy the recipe
                    int possibleCraftsFromThisSlot = gridStack.quantity / recipeSlot.quantity;
                    minPossibleCrafts = Mathf.Min(minPossibleCrafts, possibleCraftsFromThisSlot);
                }
            }
        }

        // If no required items found, recipe can't be made
        if (!hasRequiredItems)
        {
            return 0;
        }

        return minPossibleCrafts == int.MaxValue ? 0 : minPossibleCrafts;
    }

    private bool CheckRecipeMatch(ItemStack[,] craftingGrid, CraftingRecipe recipe, out (int offsetX, int offsetY) offset)
    {
        offset = (0, 0);

        // Kiểm tra toàn bộ lưới 3x3, yêu cầu khớp chính xác
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

        offset = (0, 0); // Không cần offset vì yêu cầu khớp chính xác
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

    // NEW METHOD: Get result for a single recipe craft
    public ItemStack GetSingleRecipeResult()
    {
        ItemStack[,] currentGrid = new ItemStack[3, 3];
        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                currentGrid[i, j] = craftingSlots[i, j].GetItemStack();
            }
        }

        foreach (var recipe in recipes)
        {
            if (CheckRecipeMatch(currentGrid, recipe, out var offset))
            {
                return recipe.result;
            }
        }

        return new ItemStack();
    }

    // NEW METHOD: Get maximum possible crafts (public version)
    public int GetMaxPossibleCrafts()
    {
        ItemStack[,] currentGrid = new ItemStack[3, 3];
        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                currentGrid[i, j] = craftingSlots[i, j].GetItemStack();
            }
        }

        foreach (var recipe in recipes)
        {
            if (CheckRecipeMatch(currentGrid, recipe, out var offset))
            {
                return CalculateMaxPossibleCrafts(currentGrid, recipe);
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
        ItemStack[,] currentGrid = new ItemStack[3, 3];
        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                currentGrid[i, j] = craftingSlots[i, j].GetItemStack();
            }
        }

        foreach (var recipe in recipes)
        {
            if (CheckRecipeMatch(currentGrid, recipe, out var offset))
            {
                for (int i = 0; i < 3; i++)
                {
                    for (int j = 0; j < 3; j++)
                    {
                        RecipeSlot recipeSlot = recipe.GetSlot(i, j);
                        if (recipeSlot.itemId != 0)
                        {
                            ItemStack gridStack = currentGrid[i, j];
                            if (!gridStack.IsEmpty)
                            {
                                int totalConsume = recipeSlot.quantity * recipeCount;
                                int newQuantity = gridStack.quantity - totalConsume;

                                if (newQuantity <= 0)
                                    craftingSlots[i, j].UpdateSlot(new ItemStack());
                                else
                                    craftingSlots[i, j].UpdateSlot(new ItemStack(gridStack.itemId, newQuantity, gridStack.itemData));
                            }
                        }
                    }
                }
                break;
            }
        }
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