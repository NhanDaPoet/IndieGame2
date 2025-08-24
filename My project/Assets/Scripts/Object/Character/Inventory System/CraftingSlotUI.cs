using UnityEngine;
using UnityEngine.EventSystems;

public class CraftingSlotUI : InventorySlotUI
{
    private CraftingUI craftingUI;

    private void Awake()
    {
        CraftingSlotUI[] craftingSlots = GetComponents<CraftingSlotUI>();
        if (craftingSlots.Length > 1)
        {
            for (int i = 1; i < craftingSlots.Length; i++)
            {
                Destroy(craftingSlots[i]);
            }
            Debug.LogWarning($"Multiple CraftingSlotUI components found on {gameObject.name}. Removed duplicates.");
        }
    }

    public void Initialize(int indexX, int indexY, CraftingUI craftingUI, InventoryUI inventoryUI)
    {
        this.craftingUI = craftingUI;
        base.Initialize(indexX * 3 + indexY, false, inventoryUI);
    }

    public override void OnBeginDrag(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left || GetItemStack().IsEmpty || inventoryUI == null)
            return;

        base.OnBeginDrag(eventData);

        // Update crafting result after starting drag
        if (craftingUI != null)
        {
            // Delay the update to ensure drag state is properly set
            StartCoroutine(UpdateCraftingAfterFrame());
        }
    }

    public override void OnEndDrag(PointerEventData eventData)
    {
        if (!isDragging || inventoryUI == null)
        {
            isDragging = false;
            return;
        }

        base.OnEndDrag(eventData);

        // Update crafting result after ending drag
        if (craftingUI != null)
        {
            // Delay the update to ensure all slot states are properly updated
            StartCoroutine(UpdateCraftingAfterFrame());
        }
    }

    public override void OnPointerDown(PointerEventData eventData)
    {
        base.OnPointerDown(eventData);

        // Update crafting result immediately on pointer down
        if (craftingUI != null)
            craftingUI.UpdateCraftingResult();
    }

    // Coroutine to update crafting result after frame to ensure all states are updated
    private System.Collections.IEnumerator UpdateCraftingAfterFrame()
    {
        yield return null; // Wait one frame
        if (craftingUI != null)
            craftingUI.UpdateCraftingResult();
    }
}