using Mirror;
using System;
using System.Collections.Generic;
using UnityEngine;

public class PlayerInventory : NetworkBehaviour
{
    [Header("Cài đặt kho đồ")]
    [SerializeField] private int mainInventorySize = 20;
    [SerializeField] private int hotbarSize = 9;

    [Header("Cài đặt thả vật phẩm")]
    [SerializeField] private float dropDistance = 1.5f;
    [SerializeField] private float dropForce = 3f;

    [Header("Cài đặt Hotbar Selector")]
    [SerializeField] private float scrollSensitivity = 0.1f;

    private ItemStack[] mainInventory;
    private ItemStack[] hotbar;
    [SyncVar(hook = nameof(OnSelectedSlotChanged))]
    private int selectedHotbarSlot = 0;

    public static event Action<int, ItemStack> OnMainInventoryChanged;
    public static event Action<int, ItemStack> OnHotbarChanged;
    public static event Action<int> OnSelectedHotbarSlotChanged;

    [SerializeField] private ItemDatabase itemDatabase;

    public override void OnStartLocalPlayer()
    {
        InitializeInventory();
    }

    private void InitializeInventory()
    {
        if (mainInventory != null && hotbar != null) return;
        mainInventory = new ItemStack[mainInventorySize];
        hotbar = new ItemStack[hotbarSize];

        for (int i = 0; i < mainInventorySize; i++)
            mainInventory[i] = new ItemStack();

        for (int i = 0; i < hotbarSize; i++)
            hotbar[i] = new ItemStack();
    }

    // ===== DROP SYSTEM =====

    public void DropItem(int slotIndex, bool isHotbar, int quantity = 0)
    {
        if (!isLocalPlayer) return;

        ItemStack sourceStack = isHotbar ? GetHotbarSlot(slotIndex) : GetMainInventorySlot(slotIndex);
        if (sourceStack.IsEmpty) return;

        int dropQuantity = quantity > 0 ? Mathf.Min(quantity, sourceStack.quantity) : sourceStack.quantity;

        CmdDropItem(slotIndex, isHotbar, dropQuantity);
    }

    [Command]
    private void CmdDropItem(int slotIndex, bool isHotbar, int quantity)
    {
        ItemStack sourceStack = isHotbar ? hotbar[slotIndex] : mainInventory[slotIndex];
        if (sourceStack.IsEmpty || quantity <= 0) return;

        ItemStack droppedStack = new ItemStack(sourceStack.itemId, quantity, sourceStack.itemData);
        Vector3 dropPosition = transform.position + GetDropDirection() * dropDistance;
        Vector3 dropVelocity = GetDropDirection() * dropForce;

        WorldItem.SpawnWorldItem(droppedStack, dropPosition, dropVelocity);
        if (isHotbar)
        {
            hotbar[slotIndex].quantity -= quantity;
            if (hotbar[slotIndex].quantity <= 0)
                hotbar[slotIndex].Clear();
            RpcUpdateHotbarSlot(slotIndex, hotbar[slotIndex].itemId, hotbar[slotIndex].quantity);
        }
        else
        {
            mainInventory[slotIndex].quantity -= quantity;
            if (mainInventory[slotIndex].quantity <= 0)
                mainInventory[slotIndex].Clear();
            RpcUpdateMainInventorySlot(slotIndex, mainInventory[slotIndex].itemId, mainInventory[slotIndex].quantity);
        }
    }

    [Command]
    public void CmdQuickMove(int fromIndex, bool fromIsHotbar)
    {
        ItemStack[] fromInv = fromIsHotbar ? hotbar : mainInventory;
        ItemStack[] toInv = fromIsHotbar ? mainInventory : hotbar;
        int fromMax = fromIsHotbar ? hotbarSize : mainInventorySize;
        int toMax = fromIsHotbar ? mainInventorySize : hotbarSize;
        if (fromIndex < 0 || fromIndex >= fromMax) return;
        ItemStack src = fromInv[fromIndex];
        if (src.IsEmpty || src.itemData == null) return;
        int remaining = src.quantity;
        for (int i = 0; i < toMax && remaining > 0; i++)
        {
            if (!toInv[i].IsEmpty && toInv[i].itemId == src.itemId)
            {
                int space = toInv[i].GetMaxStackSize() - toInv[i].quantity;
                if (space > 0)
                {
                    int add = Mathf.Min(space, remaining);
                    toInv[i].quantity += add;
                    remaining -= add;

                    if (fromIsHotbar) RpcUpdateMainInventorySlot(i, toInv[i].itemId, toInv[i].quantity);
                    else RpcUpdateHotbarSlot(i, toInv[i].itemId, toInv[i].quantity);
                }
            }
        }
        for (int i = 0; i < toMax && remaining > 0; i++)
        {
            if (toInv[i].IsEmpty)
            {
                int add = Mathf.Min(remaining, src.GetMaxStackSize());
                toInv[i] = new ItemStack(src.itemId, add, src.itemData);
                remaining -= add;

                if (fromIsHotbar) RpcUpdateMainInventorySlot(i, toInv[i].itemId, toInv[i].quantity);
                else RpcUpdateHotbarSlot(i, toInv[i].itemId, toInv[i].quantity);
            }
        }
        src.quantity = remaining;
        if (src.quantity <= 0) src.Clear();
        fromInv[fromIndex] = src;
        if (fromIsHotbar) RpcUpdateHotbarSlot(fromIndex, src.itemId, src.quantity);
        else RpcUpdateMainInventorySlot(fromIndex, src.itemId, src.quantity);
    }


    public void DropItemFromCraftingSlot(int itemId, int quantity, ItemData itemData)
    {
        if (!isLocalPlayer) return;

        CmdDropItemFromCraftingSlot(itemId, quantity);
    }

    [Command]
    private void CmdDropItemFromCraftingSlot(int itemId, int quantity)
    {
        if (quantity <= 0) return;

        ItemData itemData = itemDatabase.GetItemData(itemId);
        if (itemData == null) return;

        ItemStack droppedStack = new ItemStack(itemId, quantity, itemData);
        Vector3 dropPosition = transform.position + GetDropDirection() * dropDistance;
        Vector3 dropVelocity = GetDropDirection() * dropForce;

        WorldItem.SpawnWorldItem(droppedStack, dropPosition, dropVelocity);
    }

    [Command]
    public void CmdTakeResultItem(int itemId, int quantity, ItemData itemData, int targetSlotIndex, bool targetIsHotbar)
    {
        ItemStack resultStack = new ItemStack(itemId, quantity, itemData);
        bool added = TryAddItemStack(resultStack, targetSlotIndex, targetIsHotbar);
        if (added)
        {
            RpcConsumeCraftingMaterials();
        }
    }


    [Command]
    public void CmdTakeResultItemAndDrop(int itemId, int quantity, ItemData itemData)
    {
        if (quantity <= 0) return;
        ItemStack resultStack = new ItemStack(itemId, quantity, itemData);
        bool added = TryAddItemStack(resultStack);
        if (added)
        {
            CmdDropItemFromCraftingSlot(itemId, quantity);
            RpcConsumeCraftingMaterials();
        }
    }

    [ClientRpc]
    private void RpcConsumeCraftingMaterials()
    {
        // Tìm CraftingUI trên client
        CraftingUI craftingUI = FindFirstObjectByType<CraftingUI>();
        if (craftingUI != null)
        {
            craftingUI.ConsumeCraftingMaterials();
            craftingUI.UpdateCraftingResult();
        }
    }


    private Vector3 GetDropDirection()
    {
        PlayerMovement movement = GetComponent<PlayerMovement>();
        if (movement != null)
        {
            return movement.FacingDirection;
        }
        float angle = UnityEngine.Random.Range(0f, 360f) * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0);
    }

    // ===== SLOT OPERATIONS =====

    public void SwapSlots(int slotIndexA, int slotIndexB, bool isHotbarA, bool isHotbarB)
    {
        if (!isLocalPlayer) return;
        CmdSwapSlots(slotIndexA, slotIndexB, isHotbarA, isHotbarB);
    }

    [Command]
    private void CmdSwapSlots(int slotIndexA, int slotIndexB, bool isHotbarA, bool isHotbarB)
    {
        ItemStack[] inventoryA = isHotbarA ? hotbar : mainInventory;
        ItemStack[] inventoryB = isHotbarB ? hotbar : mainInventory;

        int maxSizeA = isHotbarA ? hotbarSize : mainInventorySize;
        int maxSizeB = isHotbarB ? hotbarSize : mainInventorySize;

        if (slotIndexA < 0 || slotIndexA >= maxSizeA || slotIndexB < 0 || slotIndexB >= maxSizeB)
            return;

        if (!inventoryA[slotIndexA].IsEmpty && !inventoryB[slotIndexB].IsEmpty &&
            inventoryA[slotIndexA].CanStackWith(inventoryB[slotIndexB]))
        {
            CombineSlots(slotIndexA, slotIndexB, isHotbarA, isHotbarB);
        }
        else
        {
            ItemStack temp = inventoryA[slotIndexA];
            inventoryA[slotIndexA] = inventoryB[slotIndexB];
            inventoryB[slotIndexB] = temp;

            if (isHotbarA)
                RpcUpdateHotbarSlot(slotIndexA, inventoryA[slotIndexA].itemId, inventoryA[slotIndexA].quantity);
            else
                RpcUpdateMainInventorySlot(slotIndexA, inventoryA[slotIndexA].itemId, inventoryA[slotIndexA].quantity);

            if (isHotbarB)
                RpcUpdateHotbarSlot(slotIndexB, inventoryB[slotIndexB].itemId, inventoryB[slotIndexB].quantity);
            else
                RpcUpdateMainInventorySlot(slotIndexB, inventoryB[slotIndexB].itemId, inventoryB[slotIndexB].quantity);
        }
    }

    public void SplitStack(int slotIndex, bool isHotbar, int splitAmount)
    {
        if (!isLocalPlayer) return;
        CmdSplitStack(slotIndex, isHotbar, splitAmount);
    }

    [Command]
    private void CmdSplitStack(int slotIndex, bool isHotbar, int splitAmount)
    {
        ItemStack[] inventory = isHotbar ? hotbar : mainInventory;
        int maxSize = isHotbar ? hotbarSize : mainInventorySize;

        if (slotIndex < 0 || slotIndex >= maxSize || inventory[slotIndex].IsEmpty)
            return;

        ItemStack sourceStack = inventory[slotIndex];
        if (sourceStack.quantity <= splitAmount)
            return;

        int emptySlotIndex = FindEmptySlotIndex(isHotbar);
        if (emptySlotIndex == -1)
        {
            emptySlotIndex = FindEmptySlotIndex(!isHotbar);
            if (emptySlotIndex == -1)
                return;

            ItemStack[] targetInventory = !isHotbar ? hotbar : mainInventory;
            targetInventory[emptySlotIndex] = new ItemStack(sourceStack.itemId, splitAmount, sourceStack.itemData);
            sourceStack.quantity -= splitAmount;

            if (isHotbar)
            {
                RpcUpdateHotbarSlot(slotIndex, sourceStack.itemId, sourceStack.quantity);
                RpcUpdateMainInventorySlot(emptySlotIndex, targetInventory[emptySlotIndex].itemId, targetInventory[emptySlotIndex].quantity);
            }
            else
            {
                RpcUpdateMainInventorySlot(slotIndex, sourceStack.itemId, sourceStack.quantity);
                RpcUpdateHotbarSlot(emptySlotIndex, targetInventory[emptySlotIndex].itemId, targetInventory[emptySlotIndex].quantity);
            }
        }
        else
        {
            inventory[emptySlotIndex] = new ItemStack(sourceStack.itemId, splitAmount, sourceStack.itemData);
            sourceStack.quantity -= splitAmount;

            if (isHotbar)
            {
                RpcUpdateHotbarSlot(slotIndex, sourceStack.itemId, sourceStack.quantity);
                RpcUpdateHotbarSlot(emptySlotIndex, inventory[emptySlotIndex].itemId, inventory[emptySlotIndex].quantity);
            }
            else
            {
                RpcUpdateMainInventorySlot(slotIndex, sourceStack.itemId, sourceStack.quantity);
                RpcUpdateMainInventorySlot(emptySlotIndex, inventory[emptySlotIndex].itemId, inventory[emptySlotIndex].quantity);
            }
        }
    }

    private void CombineSlots(int slotIndexA, int slotIndexB, bool isHotbarA, bool isHotbarB)
    {
        ItemStack[] inventoryA = isHotbarA ? hotbar : mainInventory;
        ItemStack[] inventoryB = isHotbarB ? hotbar : mainInventory;

        ItemStack stackA = inventoryA[slotIndexA];
        ItemStack stackB = inventoryB[slotIndexB];

        if (!stackA.CanStackWith(stackB))
            return;

        int maxStackSize = stackB.GetMaxStackSize();
        int availableSpace = maxStackSize - stackB.quantity;
        int transferAmount = Mathf.Min(stackA.quantity, availableSpace);

        stackB.quantity += transferAmount;
        stackA.quantity -= transferAmount;

        if (stackA.quantity <= 0)
            stackA.Clear();

        if (isHotbarA)
            RpcUpdateHotbarSlot(slotIndexA, stackA.itemId, stackA.quantity);
        else
            RpcUpdateMainInventorySlot(slotIndexA, stackA.itemId, stackA.quantity);

        if (isHotbarB)
            RpcUpdateHotbarSlot(slotIndexB, stackB.itemId, stackB.quantity);
        else
            RpcUpdateMainInventorySlot(slotIndexB, stackB.itemId, stackB.quantity);
    }

    private int FindEmptySlotIndex(bool searchHotbar)
    {
        if (searchHotbar)
        {
            for (int i = 0; i < hotbarSize; i++)
            {
                if (hotbar[i].IsEmpty)
                    return i;
            }
        }
        else
        {
            for (int i = 0; i < mainInventorySize; i++)
            {
                if (mainInventory[i].IsEmpty)
                    return i;
            }
        }
        return -1;
    }

    public void CollectSimilarItemsToSlot(int targetSlotIndex, bool targetIsHotbar, int itemId)
    {
        if (!isLocalPlayer) return;
        CmdCollectSimilarItemsToSlot(targetSlotIndex, targetIsHotbar, itemId);
    }

    // ===== PUBLIC INVENTORY METHODS =====

    public ItemStack GetMainInventorySlot(int index)
    {
        if (index < 0 || index >= mainInventorySize || mainInventory == null) return new ItemStack();
        return mainInventory[index];
    }

    public ItemStack GetHotbarSlot(int index)
    {
        if (index < 0 || index >= hotbarSize || hotbar == null) return new ItemStack();
        return hotbar[index];
    }

    public void SetSelectedHotbarSlot(int index)
    {
        if (index < 0 || index >= hotbarSize || !isLocalPlayer) return;
        CmdSetSelectedHotbarSlot(index);
    }

    public ItemStack GetSelectedHotbarItem()
    {
        return GetHotbarSlot(selectedHotbarSlot);
    }

    public int GetSelectedHotbarSlot() => selectedHotbarSlot;

    // ===== INVENTORY OPERATIONS =====

    public bool TryAddItemStack(ItemStack itemStack, int targetSlotIndex = -1, bool targetIsHotbar = false)
    {
        if (itemStack.IsEmpty)
        {
            return false;
        }

        if (mainInventory == null || hotbar == null)
        {
            InitializeInventory();
        }

        // Nếu chỉ định slot đích cụ thể
        if (targetSlotIndex >= 0)
        {
            ItemStack[] targetInventory = targetIsHotbar ? hotbar : mainInventory;
            int maxSize = targetIsHotbar ? hotbarSize : mainInventorySize;

            if (targetSlotIndex < maxSize)
            {
                ItemStack targetStack = targetInventory[targetSlotIndex];
                if (targetStack.IsEmpty)
                {
                    targetInventory[targetSlotIndex] = new ItemStack(itemStack.itemId, itemStack.quantity, itemStack.itemData);
                    if (targetIsHotbar)
                        RpcUpdateHotbarSlot(targetSlotIndex, itemStack.itemId, itemStack.quantity);
                    else
                        RpcUpdateMainInventorySlot(targetSlotIndex, itemStack.itemId, itemStack.quantity);
                    return true;
                }
                else if (targetStack.CanStackWith(itemStack))
                {
                    int availableSpace = targetStack.GetMaxStackSize() - targetStack.quantity;
                    int addAmount = Mathf.Min(itemStack.quantity, availableSpace);
                    if (addAmount > 0)
                    {
                        targetStack.quantity += addAmount;
                        if (targetIsHotbar)
                            RpcUpdateHotbarSlot(targetSlotIndex, targetStack.itemId, targetStack.quantity);
                        else
                            RpcUpdateMainInventorySlot(targetSlotIndex, targetStack.itemId, targetStack.quantity);
                        return true;
                    }
                }
            }
            return false;
        }

        // Thêm vào slot có thể xếp chồng hoặc slot trống
        int remainingQuantity = itemStack.quantity;
        for (int i = 0; i < hotbarSize && remainingQuantity > 0; i++)
        {
            remainingQuantity = TryAddToSlot(ref hotbar[i], itemStack.itemData, remainingQuantity, true, i);
        }

        for (int i = 0; i < mainInventorySize && remainingQuantity > 0; i++)
        {
            remainingQuantity = TryAddToSlot(ref mainInventory[i], itemStack.itemData, remainingQuantity, false, i);
        }

        for (int i = 0; i < hotbarSize && remainingQuantity > 0; i++)
        {
            if (hotbar[i].IsEmpty)
            {
                int addAmount = Mathf.Min(remainingQuantity, itemStack.itemData.maxStackSize);
                hotbar[i] = new ItemStack(itemStack.itemData.id, addAmount, itemStack.itemData);
                remainingQuantity -= addAmount;
                RpcUpdateHotbarSlot(i, hotbar[i].itemId, hotbar[i].quantity);
            }
        }

        for (int i = 0; i < mainInventorySize && remainingQuantity > 0; i++)
        {
            if (mainInventory[i].IsEmpty)
            {
                int addAmount = Mathf.Min(remainingQuantity, itemStack.itemData.maxStackSize);
                mainInventory[i] = new ItemStack(itemStack.itemData.id, addAmount, itemStack.itemData);
                remainingQuantity -= addAmount;
                RpcUpdateMainInventorySlot(i, mainInventory[i].itemId, mainInventory[i].quantity);
            }
        }

        return remainingQuantity == 0;
    }

    private bool TryFullyAddItemStack(ItemStack stack)
    {
        if (stack.IsEmpty || stack.quantity <= 0) return false;

        // Ưu tiên stack với slot hiện có
        for (int i = 0; i < hotbarSize; i++)
        {
            if (!hotbar[i].IsEmpty && hotbar[i].CanStackWith(stack))
            {
                int available = hotbar[i].GetMaxStackSize() - hotbar[i].quantity;
                if (available >= stack.quantity)
                {
                    hotbar[i].quantity += stack.quantity;
                    RpcUpdateHotbarSlot(i, hotbar[i].itemId, hotbar[i].quantity);
                    return true;
                }
            }
        }
        for (int i = 0; i < mainInventorySize; i++)
        {
            if (!mainInventory[i].IsEmpty && mainInventory[i].CanStackWith(stack))
            {
                int available = mainInventory[i].GetMaxStackSize() - mainInventory[i].quantity;
                if (available >= stack.quantity)
                {
                    mainInventory[i].quantity += stack.quantity;
                    RpcUpdateMainInventorySlot(i, mainInventory[i].itemId, mainInventory[i].quantity);
                    return true;
                }
            }
        }

        // Nếu không, tìm slot empty
        for (int i = 0; i < hotbarSize; i++)
        {
            if (hotbar[i].IsEmpty)
            {
                hotbar[i] = new ItemStack(stack.itemId, stack.quantity, stack.itemData);
                RpcUpdateHotbarSlot(i, stack.itemId, stack.quantity);
                return true;
            }
        }
        for (int i = 0; i < mainInventorySize; i++)
        {
            if (mainInventory[i].IsEmpty)
            {
                mainInventory[i] = new ItemStack(stack.itemId, stack.quantity, stack.itemData);
                RpcUpdateMainInventorySlot(i, stack.itemId, stack.quantity);
                return true;
            }
        }

        return false;
    }

    public void CombineStacksPartial(int fromSlotIndex, int toSlotIndex, bool fromIsHotbar, bool toIsHotbar, int quantity)
    {
        if (!isLocalPlayer) return;
        CmdCombineStacksPartial(fromSlotIndex, toSlotIndex, fromIsHotbar, toIsHotbar, quantity);
    }

    [Command]
    private void CmdCombineStacksPartial(int fromSlotIndex, int toSlotIndex, bool fromIsHotbar, bool toIsHotbar, int quantity)
    {
        ItemStack[] fromInventory = fromIsHotbar ? hotbar : mainInventory;
        ItemStack[] toInventory = toIsHotbar ? hotbar : mainInventory;

        int fromMaxSize = fromIsHotbar ? hotbarSize : mainInventorySize;
        int toMaxSize = toIsHotbar ? hotbarSize : mainInventorySize;

        if (fromSlotIndex < 0 || fromSlotIndex >= fromMaxSize ||
            toSlotIndex < 0 || toSlotIndex >= toMaxSize)
            return;

        ItemStack fromStack = fromInventory[fromSlotIndex];
        ItemStack toStack = toInventory[toSlotIndex];

        if (fromStack.IsEmpty || !fromStack.CanStackWith(toStack))
            return;

        int maxStackSize = toStack.GetMaxStackSize();
        int availableSpace = maxStackSize - toStack.quantity;
        int transferAmount = Mathf.Min(quantity, Mathf.Min(fromStack.quantity, availableSpace));

        if (transferAmount <= 0) return;

        toStack.quantity += transferAmount;
        fromStack.quantity -= transferAmount;

        if (fromStack.quantity <= 0)
            fromStack.Clear();

        if (fromIsHotbar)
            RpcUpdateHotbarSlot(fromSlotIndex, fromStack.itemId, fromStack.quantity);
        else
            RpcUpdateMainInventorySlot(fromSlotIndex, fromStack.itemId, fromStack.quantity);

        if (toIsHotbar)
            RpcUpdateHotbarSlot(toSlotIndex, toStack.itemId, toStack.quantity);
        else
            RpcUpdateMainInventorySlot(toSlotIndex, toStack.itemId, toStack.quantity);
    }

    public void SplitStackToSpecificSlot(int sourceSlotIndex, int targetSlotIndex, bool sourceIsHotbar, bool targetIsHotbar, int splitAmount)
    {
        if (!isLocalPlayer) return;
        CmdSplitStackToSpecificSlot(sourceSlotIndex, targetSlotIndex, sourceIsHotbar, targetIsHotbar, splitAmount);
    }

    [Command]
    public void CmdTakeCraftingResultDrag(int itemId, int quantity, int targetSlotIndex, bool targetIsHotbar, int recipesToConsume)
    {
        ItemData itemData = itemDatabase.GetItemData(itemId);
        if (itemData == null || quantity <= 0) return;

        ItemStack takeStack = new ItemStack(itemId, quantity, itemData);
        bool fullyAdded = false;

        if (targetSlotIndex >= 0)
        {
            ItemStack[] inventory = targetIsHotbar ? hotbar : mainInventory;
            int maxSize = targetIsHotbar ? hotbarSize : mainInventorySize;
            if (targetSlotIndex < 0 || targetSlotIndex >= maxSize) return;

            ItemStack current = inventory[targetSlotIndex];
            if (current.IsEmpty)
            {
                inventory[targetSlotIndex] = takeStack;
                if (targetIsHotbar)
                    RpcUpdateHotbarSlot(targetSlotIndex, itemId, quantity);
                else
                    RpcUpdateMainInventorySlot(targetSlotIndex, itemId, quantity);
                fullyAdded = true;
            }
            else if (current.CanStackWith(takeStack))
            {
                int available = current.GetMaxStackSize() - current.quantity;
                if (available >= quantity)
                {
                    current.quantity += quantity;
                    if (targetIsHotbar)
                        RpcUpdateHotbarSlot(targetSlotIndex, current.itemId, current.quantity);
                    else
                        RpcUpdateMainInventorySlot(targetSlotIndex, current.itemId, current.quantity);
                    fullyAdded = true;
                }
            }
        }
        else
        {
            fullyAdded = TryFullyAddItemStack(takeStack);
        }

        if (fullyAdded)
        {
            TargetConsumeRecipeMaterials(connectionToClient, recipesToConsume);
        }
    }

    [Command]
    public void CmdTakeCraftingResultDrop(int itemId, int quantity, int recipesToConsume)
    {
        ItemData itemData = itemDatabase.GetItemData(itemId);
        if (itemData == null || quantity <= 0) return;

        CmdDropItemFromCraftingSlot(itemId, quantity);  
        TargetConsumeRecipeMaterials(connectionToClient, recipesToConsume);
    }

    [Command]
    public void CmdTakeCraftingResultClick(int itemId, int singleQuantity, int maxRecipes)
    {
        ItemData itemData = itemDatabase.GetItemData(itemId);
        if (itemData == null || singleQuantity <= 0 || maxRecipes <= 0) return;

        int fullQuantity = singleQuantity * maxRecipes;
        ItemStack fullStack = new ItemStack(itemId, fullQuantity, itemData);

        if (TryFullyAddItemStack(fullStack))
        {
            TargetConsumeRecipeMaterials(connectionToClient, maxRecipes);
            return;
        }

        ItemStack singleStack = new ItemStack(itemId, singleQuantity, itemData);
        if (TryFullyAddItemStack(singleStack))
        {
            TargetConsumeRecipeMaterials(connectionToClient, 1);
        }
    }

    [Command]
    private void CmdSplitStackToSpecificSlot(int sourceSlotIndex, int targetSlotIndex, bool sourceIsHotbar, bool targetIsHotbar, int splitAmount)
    {
        ItemStack[] sourceInventory = sourceIsHotbar ? hotbar : mainInventory;
        ItemStack[] targetInventory = targetIsHotbar ? hotbar : mainInventory;

        int sourceMaxSize = sourceIsHotbar ? hotbarSize : mainInventorySize;
        int targetMaxSize = targetIsHotbar ? hotbarSize : mainInventorySize;

        if (sourceSlotIndex < 0 || sourceSlotIndex >= sourceMaxSize ||
            targetSlotIndex < 0 || targetSlotIndex >= targetMaxSize)
            return;

        ItemStack sourceStack = sourceInventory[sourceSlotIndex];
        ItemStack targetStack = targetInventory[targetSlotIndex];

        if (sourceStack.IsEmpty || sourceStack.quantity <= splitAmount)
            return;

        if (!targetStack.IsEmpty)
        {
            if (!sourceStack.CanStackWith(targetStack))
                return;

            int availableSpace = targetStack.GetMaxStackSize() - targetStack.quantity;
            if (availableSpace <= 0)
                return;

            splitAmount = Mathf.Min(splitAmount, availableSpace);
        }

        if (targetStack.IsEmpty)
        {
            targetInventory[targetSlotIndex] = new ItemStack(sourceStack.itemId, splitAmount, sourceStack.itemData);
        }
        else
        {
            targetStack.quantity += splitAmount;
        }

        sourceStack.quantity -= splitAmount;

        if (sourceIsHotbar)
            RpcUpdateHotbarSlot(sourceSlotIndex, sourceStack.itemId, sourceStack.quantity);
        else
            RpcUpdateMainInventorySlot(sourceSlotIndex, sourceStack.itemId, sourceStack.quantity);

        if (targetIsHotbar)
            RpcUpdateHotbarSlot(targetSlotIndex, targetInventory[targetSlotIndex].itemId, targetInventory[targetSlotIndex].quantity);
        else
            RpcUpdateMainInventorySlot(targetSlotIndex, targetInventory[targetSlotIndex].itemId, targetInventory[targetSlotIndex].quantity);
    }

    [Command]
    private void CmdCollectSimilarItemsToSlot(int targetSlotIndex, bool targetIsHotbar, int itemId)
    {
        ItemStack[] targetInventory = targetIsHotbar ? hotbar : mainInventory;
        int targetMaxSize = targetIsHotbar ? hotbarSize : mainInventorySize;

        if (targetSlotIndex < 0 || targetSlotIndex >= targetMaxSize)
            return;

        ItemStack targetStack = targetInventory[targetSlotIndex];
        if (targetStack.IsEmpty || targetStack.itemId != itemId)
            return;

        int maxStackSize = targetStack.GetMaxStackSize();
        int availableSpace = maxStackSize - targetStack.quantity;

        if (availableSpace <= 0)
            return;

        List<SlotInfo> sourceSlots = new List<SlotInfo>();

        // Find all slots with the same item (excluding target slot)
        for (int i = 0; i < hotbarSize; i++)
        {
            if ((targetIsHotbar && i == targetSlotIndex) || hotbar[i].IsEmpty || hotbar[i].itemId != itemId)
                continue;

            sourceSlots.Add(new SlotInfo { index = i, isHotbar = true, stack = hotbar[i] });
        }

        for (int i = 0; i < mainInventorySize; i++)
        {
            if ((!targetIsHotbar && i == targetSlotIndex) || mainInventory[i].IsEmpty || mainInventory[i].itemId != itemId)
                continue;

            sourceSlots.Add(new SlotInfo { index = i, isHotbar = false, stack = mainInventory[i] });
        }

        // Sort by distance from target (prioritize closer slots)
        sourceSlots.Sort((a, b) => GetSlotDistance(a, targetSlotIndex, targetIsHotbar).CompareTo(GetSlotDistance(b, targetSlotIndex, targetIsHotbar)));

        int remainingSpace = availableSpace;

        foreach (var sourceSlot in sourceSlots)
        {
            if (remainingSpace <= 0)
                break;

            int transferAmount = Mathf.Min(sourceSlot.stack.quantity, remainingSpace);

            // Update source slot
            ItemStack[] sourceInventory = sourceSlot.isHotbar ? hotbar : mainInventory;
            sourceInventory[sourceSlot.index].quantity -= transferAmount;

            if (sourceInventory[sourceSlot.index].quantity <= 0)
            {
                sourceInventory[sourceSlot.index].Clear();
            }
            targetStack.quantity += transferAmount;
            remainingSpace -= transferAmount;
            if (sourceSlot.isHotbar)
            {
                RpcUpdateHotbarSlot(sourceSlot.index, sourceInventory[sourceSlot.index].itemId, sourceInventory[sourceSlot.index].quantity);
            }
            else
            {
                RpcUpdateMainInventorySlot(sourceSlot.index, sourceInventory[sourceSlot.index].itemId, sourceInventory[sourceSlot.index].quantity);
            }
        }
        if (targetIsHotbar)
        {
            RpcUpdateHotbarSlot(targetSlotIndex, targetStack.itemId, targetStack.quantity);
        }
        else
        {
            RpcUpdateMainInventorySlot(targetSlotIndex, targetStack.itemId, targetStack.quantity);
        }
    }

    [Command]
    public void CmdUseSelectedConsumable()
    {
        ServerUseSelectedConsumable();        // (Tuỳ chọn) áp dụng hiệu ứng tiêu thụ lên Player ở đây
        // Ví dụ: Heal/Stamina/Buffer... nếu bạn đã có component Health hay tương tự
        // var health = GetComponent<PlayerHealth>(); if (health) health.ServerHeal(amount);

        // (Tuỳ chọn) báo lại riêng cho owner nếu cần hiển thị hiệu ứng/âm thanh
        // TargetRpcPlayConsumableSfx(connectionToClient);
    }

    [Server]
    public void ServerUseSelectedConsumable()
    {
        if (hotbar == null || selectedHotbarSlot < 0 || selectedHotbarSlot >= hotbarSize)
            return;
        ItemStack stack = hotbar[selectedHotbarSlot];
        if (stack.IsEmpty || stack.itemData == null)
            return;
        if (stack.itemData.itemType != ItemType.Consumable)
            return;
        stack.quantity -= 1;
        if (stack.quantity <= 0)
            stack.Clear();
        hotbar[selectedHotbarSlot] = stack;
        RpcUpdateHotbarSlot(selectedHotbarSlot, stack.itemId, stack.quantity);

        // Áp dụng hiệu ứng nếu cần
        // ApplyConsumableEffect(stack.itemData);
    }

    [Command]
    public void CmdApplyEnchant(
    int slotIndex, bool isHotbar,
    int enchantId, int level,
    int catalystItemId, int catalystCost)
    {
        // Validate server-side
        ItemStack[] inv = isHotbar ? hotbar : mainInventory;
        int max = isHotbar ? hotbarSize : mainInventorySize;
        if (slotIndex < 0 || slotIndex >= max) return;

        ItemStack target = inv[slotIndex];
        if (target.IsEmpty || target.itemData == null) return;
        if (level < 1) return;

        // Lookup catalyst
        ItemData catalystData = itemDatabase.GetItemData(catalystItemId);
        if (catalystData == null || !catalystData.isCatalyst) return;

        // Đếm catalyst đủ không
        int totalCatalyst = GetTotalItemCount(catalystItemId); // đếm cả hotbar+inventory
        if (totalCatalyst < catalystCost) return;

        // Lookup enchant def (tạm: quét Resources; bạn có thể thay bằng Database riêng)
        EnchantmentDefinition def = FindEnchantDef(enchantId);
        if (def == null) return;

        // Target type hợp lệ?
        if (!def.IsAllowedFor(target.itemData.itemType)) return;

        // Level hợp lệ? (Ngoài ra, bạn có thể ràng buộc level tối đa theo catalystTier)
        level = Mathf.Clamp(level, def.minLevel, def.maxLevel);

        // Sockets:
        int socketsMax = GetMaxSocketsFor(target.itemData);
        if (target.enchantments == null) target.enchantments = new List<EnchantmentInstance>();
        if (target.enchantments.Count >= socketsMax) return;

        // Conflict check
        foreach (var e in target.enchantments)
        {
            var exist = FindEnchantDef(e.enchantId);
            if (exist != null && (exist.conflicts.Contains(def) || def.conflicts.Contains(exist)))
                return;
        }

        // Tiêu catalyst trước (an toàn)
        ServerConsumeItems(catalystItemId, catalystCost);
        // Áp enchant
        target.enchantments.Add(new EnchantmentInstance { enchantId = enchantId, level = level });
        inv[slotIndex] = target;
        if (isHotbar) RpcUpdateHotbarSlot(slotIndex, target.itemId, target.quantity);
        else RpcUpdateMainInventorySlot(slotIndex, target.itemId, target.quantity);
    }

    [Server]
    private int GetMaxSocketsFor(ItemData data)
    {
        if (data == null) return 0;
        if (data.overrideMaxSockets > -1) return data.overrideMaxSockets;
        switch (data.rarity)
        {
            case ItemRarity.Common: return 2;
            case ItemRarity.Uncommon: return 2;
            case ItemRarity.Rare: return 3;
            case ItemRarity.Epic: return 4;
            case ItemRarity.Legendary: return 5;
            default: return 2;
        }
    }

    [Server]
    private void ServerConsumeItems(int itemId, int amount)
    {
        for (int i = 0; i < hotbarSize && amount > 0; i++)
        {
            if (!hotbar[i].IsEmpty && hotbar[i].itemId == itemId)
            {
                int take = Mathf.Min(hotbar[i].quantity, amount);
                hotbar[i].quantity -= take;
                amount -= take;
                if (hotbar[i].quantity <= 0) hotbar[i].Clear();
                RpcUpdateHotbarSlot(i, hotbar[i].itemId, hotbar[i].quantity);
            }
        }
        for (int i = 0; i < mainInventorySize && amount > 0; i++)
        {
            if (!mainInventory[i].IsEmpty && mainInventory[i].itemId == itemId)
            {
                int take = Mathf.Min(mainInventory[i].quantity, amount);
                mainInventory[i].quantity -= take;
                amount -= take;
                if (mainInventory[i].quantity <= 0) mainInventory[i].Clear();
                RpcUpdateMainInventorySlot(i, mainInventory[i].itemId, mainInventory[i].quantity);
            }
        }
    }

    [Server]
    private EnchantmentDefinition FindEnchantDef(int enchantId)
    {
        var all = Resources.FindObjectsOfTypeAll<EnchantmentDefinition>();
        foreach (var e in all) if (e.id == enchantId) return e;
        return null;
    }


    public bool CanStackWith(int slotA, int slotB, bool isHotbarA, bool isHotbarB)
    {
        ItemStack stackA = isHotbarA ? GetHotbarSlot(slotA) : GetMainInventorySlot(slotA);
        ItemStack stackB = isHotbarB ? GetHotbarSlot(slotB) : GetMainInventorySlot(slotB);

        if (stackA.IsEmpty || stackB.IsEmpty) return false;
        return stackA.CanStackWith(stackB);
    }

    public bool HasSpaceForStack(int slotIndex, bool isHotbar, ItemStack itemStack, int quantity)
    {
        ItemStack targetStack = isHotbar ? GetHotbarSlot(slotIndex) : GetMainInventorySlot(slotIndex);

        if (targetStack.IsEmpty) return true;
        if (!targetStack.CanStackWith(itemStack)) return false;

        int availableSpace = targetStack.GetMaxStackSize() - targetStack.quantity;
        return availableSpace >= quantity;
    }

    public int GetAvailableStackSpace(int slotIndex, bool isHotbar, ItemStack itemStack)
    {
        ItemStack targetStack = isHotbar ? GetHotbarSlot(slotIndex) : GetMainInventorySlot(slotIndex);

        if (targetStack.IsEmpty) return itemStack.GetMaxStackSize();
        if (!targetStack.CanStackWith(itemStack)) return 0;

        return targetStack.GetMaxStackSize() - targetStack.quantity;
    }

    // ===== INVENTORY QUERY METHODS =====

    public List<int> FindSlotsWithItem(int itemId, bool searchHotbar)
    {
        List<int> slots = new List<int>();
        ItemStack[] inventory = searchHotbar ? hotbar : mainInventory;
        int maxSize = searchHotbar ? hotbarSize : mainInventorySize;

        for (int i = 0; i < maxSize; i++)
        {
            if (!inventory[i].IsEmpty && inventory[i].itemId == itemId)
            {
                slots.Add(i);
            }
        }

        return slots;
    }

    public int FindFirstEmptySlot(bool searchHotbar)
    {
        ItemStack[] inventory = searchHotbar ? hotbar : mainInventory;
        int maxSize = searchHotbar ? hotbarSize : mainInventorySize;

        for (int i = 0; i < maxSize; i++)
        {
            if (inventory[i].IsEmpty)
                return i;
        }

        return -1;
    }

    public int FindFirstStackableSlot(ItemStack itemStack, bool searchHotbar)
    {
        ItemStack[] inventory = searchHotbar ? hotbar : mainInventory;
        int maxSize = searchHotbar ? hotbarSize : mainInventorySize;

        for (int i = 0; i < maxSize; i++)
        {
            if (!inventory[i].IsEmpty &&
                inventory[i].CanStackWith(itemStack) &&
                inventory[i].quantity < inventory[i].GetMaxStackSize())
            {
                return i;
            }
        }

        return -1;
    }

    public int GetTotalItemCount(int itemId)
    {
        int totalCount = 0;
        for (int i = 0; i < hotbarSize; i++)
        {
            if (!hotbar[i].IsEmpty && hotbar[i].itemId == itemId)
            {
                totalCount += hotbar[i].quantity;
            }
        }
        for (int i = 0; i < mainInventorySize; i++)
        {
            if (!mainInventory[i].IsEmpty && mainInventory[i].itemId == itemId)
            {
                totalCount += mainInventory[i].quantity;
            }
        }

        return totalCount;
    }

    // ===== BATCH OPERATIONS =====

    public bool TryConsumeItems(int itemId, int requiredAmount)
    {
        if (!isLocalPlayer) return false;

        int availableAmount = GetTotalItemCount(itemId);
        if (availableAmount < requiredAmount) return false;

        CmdConsumeItems(itemId, requiredAmount);
        return true;
    }

    [Command]
    private void CmdConsumeItems(int itemId, int amount)
    {
        int remainingToConsume = amount;
        for (int i = 0; i < hotbarSize && remainingToConsume > 0; i++)
        {
            if (!hotbar[i].IsEmpty && hotbar[i].itemId == itemId)
            {
                int consumeFromSlot = Mathf.Min(remainingToConsume, hotbar[i].quantity);
                hotbar[i].quantity -= consumeFromSlot;
                remainingToConsume -= consumeFromSlot;

                if (hotbar[i].quantity <= 0)
                    hotbar[i].Clear();

                RpcUpdateHotbarSlot(i, hotbar[i].itemId, hotbar[i].quantity);
            }
        }
        for (int i = 0; i < mainInventorySize && remainingToConsume > 0; i++)
        {
            if (!mainInventory[i].IsEmpty && mainInventory[i].itemId == itemId)
            {
                int consumeFromSlot = Mathf.Min(remainingToConsume, mainInventory[i].quantity);
                mainInventory[i].quantity -= consumeFromSlot;
                remainingToConsume -= consumeFromSlot;

                if (mainInventory[i].quantity <= 0)
                    mainInventory[i].Clear();

                RpcUpdateMainInventorySlot(i, mainInventory[i].itemId, mainInventory[i].quantity);
            }
        }
    }

    // ===== ADVANCED DROP SYSTEM =====

    public void DropAllItems(int itemId)
    {
        if (!isLocalPlayer) return;
        CmdDropAllItems(itemId);
    }

    [Command]
    private void CmdDropAllItems(int itemId)
    {
        for (int i = 0; i < hotbarSize; i++)
        {
            if (!hotbar[i].IsEmpty && hotbar[i].itemId == itemId)
            {
                ItemStack droppedStack = new ItemStack(hotbar[i].itemId, hotbar[i].quantity, hotbar[i].itemData);
                Vector3 dropPosition = transform.position + GetDropDirection() * dropDistance;
                Vector3 dropVelocity = GetDropDirection() * dropForce;

                WorldItem.SpawnWorldItem(droppedStack, dropPosition, dropVelocity);

                hotbar[i].Clear();
                RpcUpdateHotbarSlot(i, 0, 0);
            }
        }
        for (int i = 0; i < mainInventorySize; i++)
        {
            if (!mainInventory[i].IsEmpty && mainInventory[i].itemId == itemId)
            {
                ItemStack droppedStack = new ItemStack(mainInventory[i].itemId, mainInventory[i].quantity, mainInventory[i].itemData);
                Vector3 dropPosition = transform.position + GetDropDirection() * dropDistance;
                Vector3 dropVelocity = GetDropDirection() * dropForce;

                WorldItem.SpawnWorldItem(droppedStack, dropPosition, dropVelocity);

                mainInventory[i].Clear();
                RpcUpdateMainInventorySlot(i, 0, 0);
            }
        }
    }

    // ===== INVENTORY ORGANIZATION =====

    public void SortInventory(bool sortHotbar = false)
    {
        if (!isLocalPlayer) return;
        CmdSortInventory(sortHotbar);
    }

    [Command]
    private void CmdSortInventory(bool sortHotbar)
    {
        if (sortHotbar)
        {
            SortInventoryArray(hotbar, hotbarSize, true);
        }
        else
        {
            SortInventoryArray(mainInventory, mainInventorySize, false);
        }
    }

    [Command]
    public void CmdTryAddItem(int itemId, int quantity)
    {
        ItemData itemData = itemDatabase.GetItemData(itemId);
        if (itemData == null) return;

        ItemStack stack = new ItemStack(itemId, quantity, itemData);
        TryAddItemStack(stack);
    }

    private void SortInventoryArray(ItemStack[] inventory, int size, bool isHotbar)
    {
        for (int i = 0; i < size - 1; i++)
        {
            for (int j = 0; j < size - i - 1; j++)
            {
                bool shouldSwap = false;

                if (inventory[j].IsEmpty && !inventory[j + 1].IsEmpty)
                {
                    shouldSwap = true;
                }
                else if (!inventory[j].IsEmpty && !inventory[j + 1].IsEmpty)
                {
                    if (inventory[j].itemId > inventory[j + 1].itemId ||
                        (inventory[j].itemId == inventory[j + 1].itemId && inventory[j].quantity < inventory[j + 1].quantity))
                    {
                        shouldSwap = true;
                    }
                }
                if (shouldSwap)
                {
                    ItemStack temp = inventory[j];
                    inventory[j] = inventory[j + 1];
                    inventory[j + 1] = temp;
                    if (isHotbar)
                    {
                        RpcUpdateHotbarSlot(j, inventory[j].itemId, inventory[j].quantity);
                        RpcUpdateHotbarSlot(j + 1, inventory[j + 1].itemId, inventory[j + 1].quantity);
                    }
                    else
                    {
                        RpcUpdateMainInventorySlot(j, inventory[j].itemId, inventory[j].quantity);
                        RpcUpdateMainInventorySlot(j + 1, inventory[j + 1].itemId, inventory[j + 1].quantity);
                    }
                }
            }
        }
    }

    // ===== NEW: COMBINE ALL STACKS IN INVENTORY =====

    public void CombineAllStacksInInventory(int targetSlotIndex)
    {
        if (!isLocalPlayer) return;
        CmdCombineAllStacksInInventory(targetSlotIndex);
    }

    [Command]
    private void CmdCombineAllStacksInInventory(int targetSlotIndex)
    {
        if (targetSlotIndex < 0 || targetSlotIndex >= mainInventorySize || mainInventory[targetSlotIndex].IsEmpty)
        {
            return;
        }

        int targetItemId = mainInventory[targetSlotIndex].itemId;
        ItemData itemData = mainInventory[targetSlotIndex].itemData;
        int maxStackSize = itemData.maxStackSize;

        int totalQuantity = mainInventory[targetSlotIndex].quantity;
        List<int> slotsToClear = new List<int>();

        for (int i = 0; i < mainInventorySize; i++)
        {
            if (i == targetSlotIndex || mainInventory[i].IsEmpty || mainInventory[i].itemId != targetItemId)
                continue;

            totalQuantity += mainInventory[i].quantity;
            slotsToClear.Add(i);
        }
        foreach (int slot in slotsToClear)
        {
            mainInventory[slot].Clear();
            RpcUpdateMainInventorySlot(slot, 0, 0);
        }

        int remainingQuantity = totalQuantity;
        int currentSlot = targetSlotIndex;
        int addAmount = Mathf.Min(remainingQuantity, maxStackSize);
        mainInventory[targetSlotIndex] = new ItemStack(targetItemId, addAmount, itemData);
        RpcUpdateMainInventorySlot(targetSlotIndex, targetItemId, addAmount);
        remainingQuantity -= addAmount;

        for (int i = 0; i < mainInventorySize && remainingQuantity > 0; i++)
        {
            if (i == targetSlotIndex || !mainInventory[i].IsEmpty)
                continue;

            addAmount = Mathf.Min(remainingQuantity, maxStackSize);
            mainInventory[i] = new ItemStack(targetItemId, addAmount, itemData);
            RpcUpdateMainInventorySlot(i, targetItemId, addAmount);
            remainingQuantity -= addAmount;
        }
    }

    private int TryAddToSlot(ref ItemStack slot, ItemData itemData, int quantity, bool isHotbar, int slotIndex)
    {
        if (slot.IsEmpty || !slot.CanStackWith(new ItemStack(itemData.id, 1, itemData)))
            return quantity;

        int maxAdd = slot.GetMaxStackSize() - slot.quantity;
        int addAmount = Mathf.Min(quantity, maxAdd);

        if (addAmount > 0)
        {
            slot.quantity += addAmount;
            if (isHotbar)
                RpcUpdateHotbarSlot(slotIndex, slot.itemId, slot.quantity);
            else
                RpcUpdateMainInventorySlot(slotIndex, slot.itemId, slot.quantity);
        }

        return quantity - addAmount;
    }

    private float GetSlotDistance(SlotInfo sourceSlot, int targetSlotIndex, bool targetIsHotbar)
    {
        // Simple distance calculation - prioritize same inventory type
        if (sourceSlot.isHotbar == targetIsHotbar)
        {
            return Mathf.Abs(sourceSlot.index - targetSlotIndex);
        }
        else
        {
            // Different inventory types get higher distance (lower priority)
            return 100f + Mathf.Abs(sourceSlot.index - targetSlotIndex);
        }
    }

    public void RemoveItems(int slotIndex, bool isHotbar, int quantity)
    {
        if (!isLocalPlayer) return;
        CmdRemoveItems(slotIndex, isHotbar, quantity);
    }

    // ===== NETWORK COMMANDS =====

    [Command]
    private void CmdSetSelectedHotbarSlot(int index)
    {
        if (index >= 0 && index < hotbarSize)
            selectedHotbarSlot = index;
    }

    [Command]
    private void CmdRemoveItems(int slotIndex, bool isHotbar, int quantity)
    {
        ItemStack[] inventory = isHotbar ? hotbar : mainInventory;
        int maxSize = isHotbar ? hotbarSize : mainInventorySize;

        if (slotIndex < 0 || slotIndex >= maxSize || inventory[slotIndex].IsEmpty) return;

        ItemStack sourceStack = inventory[slotIndex];
        int removeAmount = Mathf.Min(quantity, sourceStack.quantity);
        sourceStack.quantity -= removeAmount;

        if (sourceStack.quantity <= 0)
            sourceStack.Clear();

        if (isHotbar)
            RpcUpdateHotbarSlot(slotIndex, sourceStack.itemId, sourceStack.quantity);
        else
            RpcUpdateMainInventorySlot(slotIndex, sourceStack.itemId, sourceStack.quantity);
    }

    [Command]
    public void CmdTryAddToSpecificSlot(int itemId, int quantity, int slotIndex, bool isHotbar)
    {
        ItemData itemData = itemDatabase.GetItemData(itemId);
        if (itemData == null) return;

        ItemStack[] inventory = isHotbar ? hotbar : mainInventory;
        int maxSize = isHotbar ? hotbarSize : mainInventorySize;

        if (slotIndex < 0 || slotIndex >= maxSize) return;

        if (inventory[slotIndex].IsEmpty)
        {
            // Add to empty slot
            inventory[slotIndex] = new ItemStack(itemId, quantity, itemData);

            if (isHotbar)
                RpcUpdateHotbarSlot(slotIndex, itemId, quantity);
            else
                RpcUpdateMainInventorySlot(slotIndex, itemId, quantity);
        }
    }

    [Command]
    public void CmdAddToSpecificSlot(int itemId, int quantity, int slotIndex, bool isHotbar)
    {
        ItemData itemData = itemDatabase.GetItemData(itemId);
        if (itemData == null) return;

        ItemStack[] inventory = isHotbar ? hotbar : mainInventory;
        int maxSize = isHotbar ? hotbarSize : mainInventorySize;

        if (slotIndex < 0 || slotIndex >= maxSize) return;

        ItemStack currentStack = inventory[slotIndex];
        if (!currentStack.IsEmpty && currentStack.itemId == itemId)
        {
            int maxStackSize = currentStack.GetMaxStackSize();
            int availableSpace = maxStackSize - currentStack.quantity;
            int actualAdd = Mathf.Min(quantity, availableSpace);

            if (actualAdd > 0)
            {
                currentStack.quantity += actualAdd;

                if (isHotbar)
                    RpcUpdateHotbarSlot(slotIndex, currentStack.itemId, currentStack.quantity);
                else
                    RpcUpdateMainInventorySlot(slotIndex, currentStack.itemId, currentStack.quantity);
            }
        }
    }

    [Command]
    public void CmdReplaceSlotFromCrafting(int itemId, int quantity, int slotIndex, bool isHotbar)
    {
        ItemData itemData = itemDatabase.GetItemData(itemId);
        if (itemData == null) return;

        ItemStack[] inventory = isHotbar ? hotbar : mainInventory;
        int maxSize = isHotbar ? hotbarSize : mainInventorySize;

        if (slotIndex < 0 || slotIndex >= maxSize) return;

        inventory[slotIndex] = new ItemStack(itemId, quantity, itemData);

        if (isHotbar)
            RpcUpdateHotbarSlot(slotIndex, itemId, quantity);
        else
            RpcUpdateMainInventorySlot(slotIndex, itemId, quantity);
    }

    // ===== NETWORK RPC =====

    [ClientRpc]
    private void RpcUpdateMainInventorySlot(int index, int itemId, int quantity)
    {
        if (mainInventory == null) InitializeInventory();
        ItemData itemData = itemDatabase.GetItemData(itemId);
        mainInventory[index] = new ItemStack(itemId, quantity, itemData);
        if (isLocalPlayer)
        {
            OnMainInventoryChanged?.Invoke(index, mainInventory[index]);
        }
    }

    [ClientRpc]
    private void RpcUpdateHotbarSlot(int index, int itemId, int quantity)
    {
        if (hotbar == null) InitializeInventory();

        ItemData itemData = null;
        if (itemId > 0)
        {
            itemData = itemDatabase.GetItemData(itemId);
            if (itemData == null)
            {
                Debug.LogError($"ItemData not found for itemId: {itemId}");
            }
        }

        hotbar[index] = new ItemStack(itemId, quantity, itemData);

        if (isLocalPlayer)
        {
            OnHotbarChanged?.Invoke(index, hotbar[index]);
        }
    }

    [TargetRpc]
    private void TargetConsumeRecipeMaterials(NetworkConnection target, int recipeCount)
    {
        CraftingUI craftingUI = FindFirstObjectByType<CraftingUI>();
        if (craftingUI != null)
        {
            craftingUI.ConsumeRecipeMaterials(recipeCount);
            craftingUI.UpdateCraftingResult();
        }
    }

    private void OnSelectedSlotChanged(int oldValue, int newValue)
    {
        if (isLocalPlayer)
            OnSelectedHotbarSlotChanged?.Invoke(newValue);
    }

    private struct SlotInfo
    {
        public int index;
        public bool isHotbar;
        public ItemStack stack;
    }
}