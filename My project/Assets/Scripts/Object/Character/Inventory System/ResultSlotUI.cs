using UnityEngine;
using UnityEngine.EventSystems;

public class ResultSlotUI : InventorySlotUI
{
    private CraftingUI craftingUI;

    public void Initialize(int index, bool hotbar, InventoryUI ui, CraftingUI craftingUI)
    {
        this.craftingUI = craftingUI;
        base.Initialize(index, hotbar, ui);
    }

    public override void OnPointerDown(PointerEventData eventData)
    {
        // Override to handle ResultSlot specific logic
        pointerDownPosition = eventData.position;
        dragStarted = false;

        if (eventData.button == PointerEventData.InputButton.Left)
        {
            // For ResultSlot, left click should be handled differently
            // Don't wait for drag - handle immediately unless we detect drag movement
        }
        else if (eventData.button == PointerEventData.InputButton.Right)
        {
            HandleRightClick();
        }
        else if (eventData.button == PointerEventData.InputButton.Middle)
        {
            HandleMiddleClick();
        }
    }

    public override void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left && !dragStarted)
        {
            // This was a click, not a drag - handle left click for ResultSlot
            if (!GetItemStack().IsEmpty)
            {
                HandleLeftClick();
            }
        }
    }

    public override void OnBeginDrag(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left || GetItemStack().IsEmpty || inventoryUI == null)
            return;

        // Check if we've moved enough to start dragging
        float dragDistance = Vector3.Distance(eventData.position, pointerDownPosition);
        if (dragDistance < DRAG_THRESHOLD)
        {
            return; // Not enough movement to start drag
        }

        dragStarted = true;
        isDragging = true;
        originalPosition = transform.position;

        bool isSplitDrag = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        bool isHalfSplit = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        ItemStack currentStack = GetItemStack();
        ItemStack singleRecipeResult = craftingUI.GetSingleRecipeResult();

        if (singleRecipeResult.IsEmpty)
        {
            isDragging = false;
            dragStarted = false;
            return;
        }

        int dragQuantity = currentStack.quantity;

        if (isSplitDrag)
        {
            dragQuantity = 1;
        }
        else if (isHalfSplit && currentStack.quantity > 1)
        {
            dragQuantity = Mathf.CeilToInt(currentStack.quantity / 2f);
        }

        dragQuantity = Mathf.Min(dragQuantity, currentStack.quantity);
        int maxPossible = CalculateMaxTakeableQuantity(dragQuantity);

        if (maxPossible <= 0)
        {
            isDragging = false;
            dragStarted = false;
            return;
        }

        inventoryUI.StartDrag(this, maxPossible);
        SetDragVisuals(true);
    }

    public override void OnEndDrag(PointerEventData eventData)
    {
        if (!isDragging || inventoryUI == null)
        {
            isDragging = false;
            dragStarted = false;
            return;
        }

        isDragging = false;
        dragStarted = false;
        InventorySlotUI targetSlot = GetSlotUnderMouse();

        if (targetSlot != null && !(targetSlot is CraftingSlotUI) && inventoryUI.CanDropOnSlot(targetSlot))
        {
            HandleResultSlotDrop(targetSlot);
        }
        else if (targetSlot == null)
        {
            HandleResultSlotDropToWorld();
        }

        SetDragVisuals(false);
        ResetPosition();
        inventoryUI.EndDrag(targetSlot);

        if (craftingUI != null)
            craftingUI.UpdateCraftingResult();
    }

    private void HandleResultSlotDrop(InventorySlotUI targetSlot)
    {
        int draggedQuantity = inventoryUI.isDragActive ? inventoryUI.draggedQuantity : GetItemStack().quantity;

        if (draggedQuantity > 0)
        {
            TakeResultItems(draggedQuantity, targetSlot.SlotIndex, targetSlot.IsHotbar);
        }
    }

    private void HandleResultSlotDropToWorld()
    {
        int draggedQuantity = inventoryUI.isDragActive ? inventoryUI.draggedQuantity : GetItemStack().quantity;

        if (draggedQuantity > 0)
        {
            TakeResultItemsAndDrop(draggedQuantity);
        }
    }

    private int CalculateMaxTakeableQuantity(int requestedQuantity)
    {
        ItemStack resultStack = GetItemStack();
        if (resultStack.IsEmpty) return 0;

        ItemStack singleRecipeResult = craftingUI.GetSingleRecipeResult();
        if (singleRecipeResult.IsEmpty) return 0;

        int availableCrafts = craftingUI.GetMaxPossibleCrafts();

        // For small quantities (1 item or less than recipe output)
        if (requestedQuantity == 1 || requestedQuantity < singleRecipeResult.quantity)
        {
            if (availableCrafts > 0 && resultStack.quantity >= requestedQuantity)
            {
                return requestedQuantity;
            }
            return 0;
        }

        // For larger quantities, calculate by recipe increments
        int recipesNeeded = Mathf.CeilToInt((float)requestedQuantity / singleRecipeResult.quantity);
        int actualRecipes = Mathf.Min(recipesNeeded, availableCrafts);

        // Also limit by what's currently available in the result slot
        int maxFromResult = Mathf.FloorToInt(resultStack.quantity / singleRecipeResult.quantity);
        actualRecipes = Mathf.Min(actualRecipes, maxFromResult);

        int maxByRecipes = actualRecipes * singleRecipeResult.quantity;

        // For exact quantity requests that don't align with recipes, be more flexible
        if (requestedQuantity < maxByRecipes && requestedQuantity <= resultStack.quantity)
        {
            int recipesForRequest = Mathf.CeilToInt((float)requestedQuantity / singleRecipeResult.quantity);
            if (recipesForRequest <= availableCrafts)
            {
                return requestedQuantity;
            }
        }

        return maxByRecipes;
    }

    private void TakeResultItems(int quantity, int targetSlotIndex, bool targetIsHotbar)
    {
        if (quantity <= 0) return;
        craftingUI.TakeResultItems(quantity, targetSlotIndex, targetIsHotbar);
    }

    private void TakeResultItemsAndDrop(int quantity)
    {
        if (quantity <= 0) return;
        craftingUI.TakeResultItemsAndDrop(quantity);
    }

    private void HandleLeftClick()
    {
        ItemStack resultStack = GetItemStack();
        if (resultStack.IsEmpty) return;

        int maxTakeable = CalculateMaxTakeableQuantity(resultStack.quantity);
        if (maxTakeable > 0)
        {
            TakeResultItems(maxTakeable, -1, false);
        }
    }

    private void HandleRightClick()
    {
        ItemStack resultStack = GetItemStack();
        if (resultStack.IsEmpty) return;

        ItemStack singleRecipeResult = craftingUI.GetSingleRecipeResult();
        if (singleRecipeResult.IsEmpty) return;

        int takeAmount;
        if (resultStack.quantity > 1)
        {
            takeAmount = Mathf.CeilToInt(resultStack.quantity / 2f);
        }
        else
        {
            takeAmount = 1;
        }

        int maxTakeable = CalculateMaxTakeableQuantity(takeAmount);
        if (maxTakeable > 0)
        {
            TakeResultItems(maxTakeable, -1, false);
        }
    }

    private void HandleMiddleClick()
    {
        ItemStack resultStack = GetItemStack();
        if (resultStack.IsEmpty) return;

        ItemStack singleRecipeResult = craftingUI.GetSingleRecipeResult();
        if (singleRecipeResult.IsEmpty) return;

        int maxTakeable = CalculateMaxTakeableQuantity(1);
        if (maxTakeable > 0)
        {
            TakeResultItemsAndDrop(maxTakeable);
        }
    }

    public override void OnPointerEnter(PointerEventData eventData)
    {
        if (!isDragging)
        {
            SetSlotHighlight(highlightColor);
        }
        else
        {
            bool canDrop = inventoryUI.CanDropOnSlot(this);
            SetSlotHighlight(canDrop ? validDropColor : invalidDropColor);
        }

        ItemStack currentStack = GetItemStack();
        if (!currentStack.IsEmpty && currentStack.itemData != null && !isDragging)
        {
            ShowTooltip();
        }
    }

    public bool CanPerformCraftingAction()
    {
        return craftingUI != null &&
               craftingUI.GetMaxPossibleCrafts() > 0 &&
               !craftingUI.GetSingleRecipeResult().IsEmpty;
    }
}