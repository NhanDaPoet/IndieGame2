using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class InventoryUI : NetworkBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject inventoryPanel;
    [SerializeField] private Transform mainInventoryContainer;
    [SerializeField] private Transform hotbarContainer;
    [SerializeField] private InventorySlotUI slotUIPrefab;
    [SerializeField] private KeyCode inventoryToggleKey = KeyCode.E;
    [SerializeField] private KeyCode dropKey = KeyCode.Q;

    [Header("Hotbar Selection")]
    [SerializeField] private Image hotbarSelector; 
    [SerializeField] private Color selectorColor = Color.yellow;

    [Header("Crafting UI")]
    [SerializeField] private CraftingUI craftingUI;

    [Header("Drag Visual")]
    [SerializeField] private GameObject dragVisualPrefab;
    [SerializeField] private Canvas dragCanvas;

    private InventorySlotUI[] mainInventorySlots;
    private InventorySlotUI[] hotbarSlots;
    public PlayerInventory playerInventory;

    private bool isInventoryPanelOpen = false;
    private bool hasUserSelectedHotbar = false;

    private InventorySlotUI draggedSlot;
    private GameObject draggedItemVisual;
    private ItemStack originalDraggedStack;

    public int draggedQuantity;
    public bool isDragActive = false;

    private float lastClickTime = 0f;
    [SerializeField] private float doubleClickTime = 0.3f;
    private InventorySlotUI lastClickedSlot;

    // ===== DRAG & DROP SYSTEM =====

    public void StartDrag(InventorySlotUI slot, int quantity)
    {
        if (slot.GetItemStack().IsEmpty || quantity <= 0)
        {
            return;
        }
        originalDraggedStack = new ItemStack(slot.GetItemStack().itemId, slot.GetItemStack().quantity, slot.GetItemStack().itemData);
        draggedSlot = slot;
        draggedQuantity = Mathf.Min(quantity, slot.GetItemStack().quantity);
        isDragActive = true;
        CreateDragVisual(slot.GetItemStack(), draggedQuantity);
        slot.SetDragVisuals(true);
        if (slot is CraftingSlotUI)
        {
            var currentStack = slot.GetItemStack();
            if (draggedQuantity < currentStack.quantity)
            {
                slot.UpdateSlot(new ItemStack(currentStack.itemId, currentStack.quantity - draggedQuantity, currentStack.itemData));
            }
            else
            {
                slot.UpdateSlot(new ItemStack());
            }
        }
    }

    public void UpdateDrag()
    {
        if (!isDragActive || draggedItemVisual == null) return;

        Vector3 mousePos = Input.mousePosition;
        draggedItemVisual.transform.position = mousePos;
    }

    public void EndDrag(InventorySlotUI targetSlot)
    {
        if (!isDragActive || draggedSlot == null)
        {
            CleanupDrag();
            return;
        }
        bool dragHandled = false;
        if (targetSlot == null)
        {
            if (draggedSlot is CraftingSlotUI)
            {
                if (!originalDraggedStack.IsEmpty && draggedQuantity > 0 && draggedQuantity <= originalDraggedStack.quantity)
                {
                    playerInventory.DropItemFromCraftingSlot(originalDraggedStack.itemId, draggedQuantity, originalDraggedStack.itemData);
                    int remainingQuantity = originalDraggedStack.quantity - draggedQuantity;
                    if (remainingQuantity > 0)
                    {
                        draggedSlot.UpdateSlot(new ItemStack(originalDraggedStack.itemId, remainingQuantity, originalDraggedStack.itemData));
                    }
                    else
                    {
                        draggedSlot.UpdateSlot(new ItemStack());
                    }
                    dragHandled = true;
                }
            }
            else
            {
                ItemStack stackToDrop = draggedSlot.GetItemStack();
                if (!stackToDrop.IsEmpty && draggedQuantity > 0)
                {
                    playerInventory.DropItem(draggedSlot.SlotIndex, draggedSlot.IsHotbar, draggedQuantity);
                    dragHandled = true;
                }
            }
        }
        else
        {
            if (CanDropOnSlot(targetSlot))
            {
                HandleSlotDrop(draggedSlot, targetSlot, draggedQuantity);
                dragHandled = true;
            }
            else
            {
                RestoreOriginalDraggedSlot();
            }
        }
        if (draggedSlot != null)
        {
            draggedSlot.SetDragVisuals(false);
            draggedSlot.ResetPosition();
        }
        CleanupDrag();
        if (craftingUI != null)
        {
            craftingUI.UpdateCraftingResult();
        }
    }

    private void RestoreOriginalDraggedSlot()
    {
        if (draggedSlot != null && draggedSlot is CraftingSlotUI)
        {
            if (!originalDraggedStack.IsEmpty)
            {
                draggedSlot.UpdateSlot(originalDraggedStack);
                if (craftingUI != null)
                {
                    craftingUI.UpdateCraftingResult();
                }
            }
        }
    }

    public bool CanDropOnSlot(InventorySlotUI targetSlot)
    {
        if (!isDragActive || draggedSlot == null || targetSlot == null || targetSlot == draggedSlot)
        {
            return false;
        }
        ItemStack draggedStack = CreateDraggedItemStack(); 
        ItemStack targetStack = targetSlot.GetItemStack();
        if (targetSlot is CraftingSlotUI && !(draggedSlot is ResultSlotUI))
        {
            if (targetStack.IsEmpty) return true;

            if (draggedStack.CanStackWith(targetStack))
            {
                int availableSpace = targetStack.GetMaxStackSize() - targetStack.quantity;
                return availableSpace >= draggedQuantity;
            }
            return true;
        }
        if (targetSlot is ResultSlotUI)
        {
            return false;
        }
        if (targetStack.IsEmpty) return true;
        if (draggedStack.CanStackWith(targetStack))
        {
            int availableSpace = targetStack.GetMaxStackSize() - targetStack.quantity;
            return availableSpace >= draggedQuantity;
        }
        return !(targetSlot.IsHotbar && draggedSlot.IsHotbar);
    }

    private ItemStack CreateDraggedItemStack()
    {
        if (draggedSlot is CraftingSlotUI)
        {
            return new ItemStack(originalDraggedStack.itemId, draggedQuantity, originalDraggedStack.itemData);
        }
        else
        {
            var currentStack = draggedSlot.GetItemStack();
            return new ItemStack(currentStack.itemId, draggedQuantity, currentStack.itemData);
        }
    }

    public void HandleSlotDrop(InventorySlotUI fromSlot, InventorySlotUI toSlot, int quantity)
    {
        ItemStack fromStack;
        if (fromSlot is CraftingSlotUI)
        {
            fromStack = originalDraggedStack;
        }
        else
        {
            fromStack = fromSlot.GetItemStack();
        }
        ItemStack toStack = toSlot.GetItemStack();

        if (toSlot is CraftingSlotUI)
        {
            HandleDropToCraftingSlot(fromSlot, toSlot, quantity, fromStack, toStack);
        }
        else if (fromSlot is CraftingSlotUI)
        {
            HandleDropFromCraftingSlot(fromSlot, toSlot, quantity, fromStack, toStack);
        }
        else
        {
            HandleRegularSlotDrop(fromSlot, toSlot, quantity, fromStack, toStack);
        }
    }

    private void HandleDropToCraftingSlot(InventorySlotUI fromSlot, InventorySlotUI toSlot, int quantity, ItemStack fromStack, ItemStack toStack)
    {
        if (toStack.IsEmpty)
        {
            toSlot.UpdateSlot(new ItemStack(fromStack.itemId, quantity, fromStack.itemData));
        }
        else if (toStack.itemId == fromStack.itemId && toStack.CanStackWith(fromStack))
        {
            int newQuantity = Mathf.Min(toStack.quantity + quantity, toStack.GetMaxStackSize());
            int actualAdded = newQuantity - toStack.quantity;
            toSlot.UpdateSlot(new ItemStack(toStack.itemId, newQuantity, toStack.itemData));
            quantity = actualAdded;
        }
        else
        {
            toSlot.UpdateSlot(new ItemStack(fromStack.itemId, quantity, fromStack.itemData));
            if (fromSlot is CraftingSlotUI)
            {
                fromSlot.UpdateSlot(toStack);
                return; 
            }
        }
        if (fromSlot is CraftingSlotUI)
        {
            int remainingQuantity = originalDraggedStack.quantity - quantity;
            if (remainingQuantity > 0)
            {
                fromSlot.UpdateSlot(new ItemStack(originalDraggedStack.itemId, remainingQuantity, originalDraggedStack.itemData));
            }
            else
            {
                fromSlot.UpdateSlot(new ItemStack());
            }
        }
        else if (!(fromSlot is ResultSlotUI))
        {
            playerInventory.RemoveItems(fromSlot.SlotIndex, fromSlot.IsHotbar, quantity);
        }
    }
    private void HandleDropFromCraftingSlot(InventorySlotUI fromSlot, InventorySlotUI toSlot, int quantity, ItemStack fromStack, ItemStack toStack)
    {
        if (toStack.IsEmpty)
        {
            if (toSlot.IsHotbar)
            {
                playerInventory.CmdTryAddToSpecificSlot(fromStack.itemId, quantity, toSlot.SlotIndex, true);
            }
            else
            {
                playerInventory.CmdTryAddToSpecificSlot(fromStack.itemId, quantity, toSlot.SlotIndex, false);
            }
        }
        else if (fromStack.CanStackWith(toStack))
        {
            int availableSpace = toStack.GetMaxStackSize() - toStack.quantity;
            int actualQuantity = Mathf.Min(quantity, availableSpace);
            if (actualQuantity > 0)
            {
                if (toSlot.IsHotbar)
                {
                    playerInventory.CmdAddToSpecificSlot(fromStack.itemId, actualQuantity, toSlot.SlotIndex, true);
                }
                else
                {
                    playerInventory.CmdAddToSpecificSlot(fromStack.itemId, actualQuantity, toSlot.SlotIndex, false);
                }
                quantity = actualQuantity;
            }
            else
            {
                RestoreOriginalDraggedSlot();
                return;
            }
        }
        else
        {
            // Swap items - this is more complex, need to handle carefully
            // First try to add the crafting item to inventory
            if (toSlot.IsHotbar)
            {
                playerInventory.CmdReplaceSlotFromCrafting(fromStack.itemId, quantity, toSlot.SlotIndex, true);
            }
            else
            {
                playerInventory.CmdReplaceSlotFromCrafting(fromStack.itemId, quantity, toSlot.SlotIndex, false);
            }
            fromSlot.UpdateSlot(toStack);
            return;
        }
        int remainingQuantity = originalDraggedStack.quantity - quantity;
        if (remainingQuantity > 0)
        {
            fromSlot.UpdateSlot(new ItemStack(originalDraggedStack.itemId, remainingQuantity, originalDraggedStack.itemData));
        }
        else
        {
            fromSlot.UpdateSlot(new ItemStack());
        }
    }

    private void HandleRegularSlotDrop(InventorySlotUI fromSlot, InventorySlotUI toSlot, int quantity, ItemStack fromStack, ItemStack toStack)
    {
        if (toStack.IsEmpty)
        {
            if (quantity == fromStack.quantity)
            {
                SwapSlots(fromSlot, toSlot);
            }
            else
            {
                playerInventory.SplitStackToSpecificSlot(fromSlot.SlotIndex, toSlot.SlotIndex, fromSlot.IsHotbar, toSlot.IsHotbar, quantity);
            }
        }
        else if (fromStack.CanStackWith(toStack))
        {
            playerInventory.CombineStacksPartial(fromSlot.SlotIndex, toSlot.SlotIndex, fromSlot.IsHotbar, toSlot.IsHotbar, quantity);
        }
        else
        {
            if (quantity == fromStack.quantity)
            {
                SwapSlots(fromSlot, toSlot);
            }
        }
    }
    private void CreateDragVisual(ItemStack itemStack, int quantity)
    {
        if (dragCanvas == null)
        {
            dragCanvas = GetComponentInParent<Canvas>();
            if (dragCanvas == null)
            {
                return;
            }
        }
        draggedItemVisual = new GameObject("DraggedItem");
        draggedItemVisual.transform.SetParent(dragCanvas.transform);
        draggedItemVisual.transform.SetAsLastSibling();
        var image = draggedItemVisual.AddComponent<Image>();
        image.sprite = itemStack.itemData?.icon;
        image.raycastTarget = false;
        var canvasGroup = draggedItemVisual.AddComponent<CanvasGroup>();
        canvasGroup.alpha = 0.8f;
        canvasGroup.blocksRaycasts = false;
        var rectTransform = draggedItemVisual.GetComponent<RectTransform>();
        rectTransform.sizeDelta = new Vector2(64, 64);
        if (quantity > 1)
        {
            CreateQuantityText(draggedItemVisual, quantity);
        }
    }

    private void CreateQuantityText(GameObject parent, int quantity)
    {
        GameObject quantityGO = new GameObject("QuantityText");
        quantityGO.transform.SetParent(parent.transform);
        var text = quantityGO.AddComponent<TextMeshProUGUI>();
        text.text = quantity.ToString();
        text.fontSize = 14;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.BottomRight;
        text.raycastTarget = false;
        var rectTransform = quantityGO.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0.7f, 0f);
        rectTransform.anchorMax = new Vector2(1f, 0.3f);
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
    }

    private void CleanupDrag()
    {
        if (draggedItemVisual != null)
        {
            Destroy(draggedItemVisual);
        }
        draggedSlot = null;
        draggedItemVisual = null;
        draggedQuantity = 0;
        isDragActive = false;
        originalDraggedStack = new ItemStack();
    }

    // ===== SLOT INTERACTION HANDLERS =====

    private void SwapSlots(InventorySlotUI fromSlot, InventorySlotUI toSlot)
    {
        if (playerInventory != null)
        {
            playerInventory.SwapSlots(fromSlot.SlotIndex, toSlot.SlotIndex, fromSlot.IsHotbar, toSlot.IsHotbar);
        }
    }

    public void RequestSplitStack(InventorySlotUI slot, int amount)
    {
        if (playerInventory != null)
        {
            if (slot is CraftingSlotUI)
            {
                InventorySlotUI targetSlot = FindEmptySlot();
                if (targetSlot != null)
                {
                    ItemStack fromStack = slot.GetItemStack();
                    if (fromStack.quantity > amount)
                    {
                        slot.UpdateSlot(new ItemStack(fromStack.itemId, fromStack.quantity - amount, fromStack.itemData));
                        if (targetSlot.IsHotbar)
                        {
                            playerInventory.CmdTryAddToSpecificSlot(fromStack.itemId, amount, targetSlot.SlotIndex, true);
                        }
                        else
                        {
                            playerInventory.CmdTryAddToSpecificSlot(fromStack.itemId, amount, targetSlot.SlotIndex, false);
                        }
                        if (craftingUI != null)
                            craftingUI.UpdateCraftingResult();
                    }
                }
            }
            else
            {
                InventorySlotUI targetSlot = FindEmptySlot();
                if (targetSlot != null)
                {
                    playerInventory.SplitStackToSpecificSlot(slot.SlotIndex, targetSlot.SlotIndex, slot.IsHotbar, targetSlot.IsHotbar, amount);
                }
            }
        }
    }

    private bool IsDraggingFromCraftingSlot()
    {
        return isDragActive && draggedSlot != null && draggedSlot is CraftingSlotUI;
    }

    public void DropSingleItem(InventorySlotUI slot)
    {
        if (slot is CraftingSlotUI)
        {
            ItemStack stack = slot.GetItemStack();
            if (!stack.IsEmpty)
            {
                playerInventory.DropItemFromCraftingSlot(stack.itemId, 1, stack.itemData);

                int remainingQuantity = stack.quantity - 1;
                if (remainingQuantity > 0)
                {
                    slot.UpdateSlot(new ItemStack(stack.itemId, remainingQuantity, stack.itemData));
                }
                else
                {
                    slot.UpdateSlot(new ItemStack());
                }

                if (craftingUI != null)
                {
                    craftingUI.UpdateCraftingResult();
                }
            }
        }
        else
        {
            DropToWorld(slot, 1);
        }
    }

    public void UseItem(InventorySlotUI slot)
    {
        // Use item implementation
    }

    public void DropToWorld(InventorySlotUI slot, int quantity)
    {
        if (playerInventory != null)
        {
            if (slot is CraftingSlotUI)
            {
                ItemStack stackToDrop;
                if (isDragActive && slot == draggedSlot && !originalDraggedStack.IsEmpty)
                {
                    stackToDrop = originalDraggedStack;
                }
                else
                {
                    stackToDrop = slot.GetItemStack();
                }
                if (!stackToDrop.IsEmpty && quantity <= stackToDrop.quantity)
                {
                    playerInventory.DropItemFromCraftingSlot(stackToDrop.itemId, quantity, stackToDrop.itemData);
                    if (!isDragActive || slot != draggedSlot)
                    {
                        int remainingQuantity = stackToDrop.quantity - quantity;
                        if (remainingQuantity > 0)
                        {
                            slot.UpdateSlot(new ItemStack(stackToDrop.itemId, remainingQuantity, stackToDrop.itemData));
                        }
                        else
                        {
                            slot.UpdateSlot(new ItemStack());
                        }
                    }
                    if (craftingUI != null)
                    {
                        craftingUI.UpdateCraftingResult();
                    }
                }
            }
            else if (slot is ResultSlotUI)
            {
                Debug.LogWarning("Cannot drop from result slot directly");
            }
            else
            {
                playerInventory.DropItem(slot.SlotIndex, slot.IsHotbar, quantity);
            }
        }
    }

    private InventorySlotUI FindEmptySlot()
    {
        for (int i = 0; i < mainInventorySlots.Length; i++)
        {
            if (mainInventorySlots[i].GetItemStack().IsEmpty)
                return mainInventorySlots[i];
        }

        for (int i = 0; i < hotbarSlots.Length; i++)
        {
            if (hotbarSlots[i].GetItemStack().IsEmpty)
                return hotbarSlots[i];
        }

        return null;
    }

    // ===== HANDLE SLOT CLICK =====

    public void OnSlotClicked(InventorySlotUI slot)
    {
        if (playerInventory == null || !playerInventory.isLocalPlayer) return;
        if (slot is CraftingSlotUI)
        {
            craftingUI.UpdateCraftingResult();
            return;
        }
        if (slot is ResultSlotUI)
        {
            craftingUI.OnResultSlotClicked();
            return;
        }
        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
        {
            QuickMoveStack(slot);
            return;
        }
        float currentTime = Time.time;
        if (currentTime - lastClickTime <= doubleClickTime && slot == lastClickedSlot)
        {
            HandleDoubleClick(slot);
        }
        lastClickTime = currentTime;
        lastClickedSlot = slot;
        if (slot.IsHotbar)
        {
            SelectHotbarSlot(slot.SlotIndex);
        }
    }

    private void QuickMoveStack(InventorySlotUI fromSlot)
    {
        ItemStack stack = fromSlot.GetItemStack();
        if (stack.IsEmpty) return;
        bool fromIsHotbar = fromSlot.IsHotbar;
        playerInventory.CmdQuickMove(fromSlot.SlotIndex, fromIsHotbar);
    }


    private void HandleDoubleClick(InventorySlotUI slot)
    {
        ItemStack clickedStack = slot.GetItemStack();
        if (clickedStack.IsEmpty)
            return;
        if (Input.GetKey(KeyCode.LeftShift) && !slot.IsHotbar)
        {
            playerInventory.CombineAllStacksInInventory(slot.SlotIndex);
            return;
        }
        CollectSimilarItems(slot);
    }

    private void CollectSimilarItems(InventorySlotUI targetSlot)
    {
        ItemStack targetStack = targetSlot.GetItemStack();
        if (targetStack.IsEmpty)
            return;
        int targetItemId = targetStack.itemId;
        int maxStackSize = targetStack.GetMaxStackSize();
        int currentQuantity = targetStack.quantity;
        if (currentQuantity >= maxStackSize)
            return;
        playerInventory.CollectSimilarItemsToSlot(targetSlot.SlotIndex, targetSlot.IsHotbar, targetItemId);
    }


    // ===== INPUT HANDLING =====

    private void Update()
    {
        if (playerInventory == null || !playerInventory.isLocalPlayer) return;

        HandleKeyboardInput();
        HandleMouseInput();
    }

    private void HandleKeyboardInput()
    {
        if (Input.GetKeyDown(inventoryToggleKey))
        {
            ToggleInventoryPanel();
        }

        if (Input.GetKeyDown(dropKey))
        {
            DropSelectedHotbarItem();
        }

        for (int i = 1; i <= 9; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha0 + i))
            {
                hasUserSelectedHotbar = true;
                SelectHotbarSlot(i - 1);
                break;
            }
        }
    }

    private void HandleMouseInput()
    {
        if (!isInventoryPanelOpen)
        {
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.01f)
            {
                int currentSlot = playerInventory.GetSelectedHotbarSlot();
                int newSlot = scroll > 0 ? (currentSlot - 1 + 9) % 9 : (currentSlot + 1) % 9;
                hasUserSelectedHotbar = true;
                SelectHotbarSlot(newSlot);
            }
        }

        if (isDragActive && Input.GetKeyDown(KeyCode.Escape))
        {
            RestoreOriginalDraggedSlot();
            EndDrag(null);
        }
    }

    private void ToggleInventoryPanel()
    {
        isInventoryPanelOpen = !isInventoryPanelOpen;
        if (inventoryPanel != null)
            inventoryPanel.SetActive(isInventoryPanelOpen);

        if (!isInventoryPanelOpen && isDragActive)
        {
            RestoreOriginalDraggedSlot();
            EndDrag(null);
        }
        ResetAllSlotHighlights();
        //Cursor.lockState = isInventoryPanelOpen ? CursorLockMode.None : CursorLockMode.Locked;
        //Cursor.visible = isInventoryPanelOpen;
    }

    private void ResetAllSlotHighlights()
    {
        foreach (var slot in mainInventorySlots)
        {
            slot.SetSlotHighlight(slot.normalColor);
        }
        foreach (var slot in hotbarSlots)
        {
            slot.SetSlotHighlight(slot.normalColor);
        }
        //foreach (var slot in craftingUI.GetCraftingSlots())
        //{
        //    slot.SetSlotHighlight(slot.normalColor);
        //}
        //foreach (var slot in craftingUI.GetResultSlots())
        //{
        //    slot.SetSlotHighlight(slot.normalColor);
        //}
    }

    private void DropSelectedHotbarItem()
    {
        if (!hasUserSelectedHotbar) return;
        int selectedSlot = playerInventory.GetSelectedHotbarSlot();
        var selectedStack = playerInventory.GetHotbarSlot(selectedSlot);
        if (!selectedStack.IsEmpty)
        {
            int dropAmount = selectedStack.quantity;
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                dropAmount = 1;
            else if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                dropAmount = Mathf.CeilToInt(selectedStack.quantity / 2f);

            playerInventory.DropItem(selectedSlot, true, dropAmount);
        }
    }

    private void SelectHotbarSlot(int slotIndex)
    {
        if (playerInventory != null)
        {
            playerInventory.SetSelectedHotbarSlot(slotIndex);
        }
    }

    private void UpdateHotbarSelector(int slotIndex)
    {
        if (hotbarSelector == null || hotbarSlots == null || slotIndex >= hotbarSlots.Length)
        {
            return;
        }

        Vector3 targetPosition = hotbarSlots[slotIndex].transform.position;
        hotbarSelector.transform.position = targetPosition;
        hotbarSelector.color = selectorColor;
    }

    // ===== INITIALIZATION =====

    private void Start()
    {
        playerInventory = GetComponentInParent<PlayerInventory>();
        if (playerInventory == null)
        {
            playerInventory = GetComponent<PlayerInventory>();
        }
        if (playerInventory == null)
        {
            Debug.LogError("PlayerInventory not found for InventoryUI.");
            playerInventory = FindFirstObjectByType<PlayerInventory>();
        }

        if (playerInventory == null || !playerInventory.isLocalPlayer)
        {
            gameObject.SetActive(false);
            return;
        }

        InitializeUI();
        SubscribeToEvents();

        if (inventoryPanel != null)
            inventoryPanel.SetActive(false);
    }

    private void OnDestroy()
    {
        UnsubscribeFromEvents();
        CleanupDrag();
    }

    private void InitializeUI()
    {
        if (mainInventoryContainer == null || slotUIPrefab == null)
        {
            Debug.LogError("MainInventoryContainer or slotUIPrefab is not assigned.");
            return;
        }

        mainInventorySlots = new InventorySlotUI[20];
        for (int i = 0; i < 20; i++)
        {
            var slot = Instantiate(slotUIPrefab, mainInventoryContainer);
            slot.name = $"MainSlot_{i}";
            slot.Initialize(i, false, this);
            mainInventorySlots[i] = slot;
        }

        if (hotbarContainer == null)
        {
            Debug.LogError("HotbarContainer is not assigned.");
            return;
        }

        hotbarSlots = new InventorySlotUI[9];
        for (int i = 0; i < 9; i++)
        {
            var slot = Instantiate(slotUIPrefab, hotbarContainer);
            slot.name = $"HotbarSlot_{i}";
            slot.Initialize(i, true, this);
            hotbarSlots[i] = slot;
        }

        if (hotbarSelector == null)
        {
            Debug.LogWarning("[Client] InitializeUI: hotbarSelector is null");
        }
        else
        {
            UpdateHotbarSelector(0);
        }
    }

    // ===== EVENT SUBSCRIPTION =====

    private void SubscribeToEvents()
    {
        PlayerInventory.OnMainInventoryChanged += OnMainInventorySlotChanged;
        PlayerInventory.OnHotbarChanged += OnHotbarSlotChanged;
        PlayerInventory.OnSelectedHotbarSlotChanged += OnSelectedHotbarSlotChanged;
    }

    private void UnsubscribeFromEvents()
    {
        PlayerInventory.OnMainInventoryChanged -= OnMainInventorySlotChanged;
        PlayerInventory.OnHotbarChanged -= OnHotbarSlotChanged;
        PlayerInventory.OnSelectedHotbarSlotChanged -= OnSelectedHotbarSlotChanged;
    }

    private void OnMainInventorySlotChanged(int slotIndex, ItemStack itemStack)
    {
        if (mainInventorySlots != null && slotIndex < mainInventorySlots.Length)
        {
            mainInventorySlots[slotIndex].UpdateSlot(itemStack);
        }
    }

    private void OnHotbarSlotChanged(int slotIndex, ItemStack itemStack)
    {
        if (hotbarSlots != null && slotIndex < hotbarSlots.Length)
        {
            hotbarSlots[slotIndex].UpdateSlot(itemStack);
        }
    }

    private void OnSelectedHotbarSlotChanged(int slotIndex)
    {
        UpdateHotbarSelector(slotIndex);
    }
}