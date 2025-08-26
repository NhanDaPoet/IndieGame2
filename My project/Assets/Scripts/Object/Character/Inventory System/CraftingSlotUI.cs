using UnityEngine;
using UnityEngine.EventSystems;

public class CraftingSlotUI : InventorySlotUI
{
    private CraftingUI craftingUI;
    private int draggedQuantity;

    private void Awake()
    {
        CraftingSlotUI[] craftingSlots = GetComponents<CraftingSlotUI>();
        if (craftingSlots.Length > 1)
        {
            for (int i = 1; i < craftingSlots.Length; i++)
            {
                Destroy(craftingSlots[i]);
            }
        }
    }

    public void Initialize(int indexX, int indexY, CraftingUI craftingUI, InventoryUI inventoryUI)
    {
        this.craftingUI = craftingUI;
        base.Initialize(indexX * 3 + indexY, false, inventoryUI);
    }

    public override void OnBeginDrag(PointerEventData eventData)
    {
        //if (eventData.button != PointerEventData.InputButton.Left || GetItemStack().IsEmpty || inventoryUI == null)
        //    return;
        //base.OnBeginDrag(eventData);
        //draggedQuantity = Mathf.Min(GetItemStack().quantity, draggedQuantity);
        //if (craftingUI != null)
        //{
        //    StartCoroutine(UpdateCraftingAfterFrame());
        //}

        if (eventData.button != PointerEventData.InputButton.Left || GetItemStack().IsEmpty || inventoryUI == null)
            return;

        base.OnBeginDrag(eventData);

        if (craftingUI != null)
        {
            StartCoroutine(UpdateCraftingAfterFrame());
        }
    }

    public override void OnEndDrag(PointerEventData eventData)
    {
        //if (!isDragging || inventoryUI == null)
        //{
        //    isDragging = false;
        //    return;
        //}
        //base.OnEndDrag(eventData);
        //if (craftingUI != null)
        //{
        //    StartCoroutine(UpdateCraftingAfterFrame());
        //}
        //if (eventData.pointerCurrentRaycast.gameObject == null)
        //{
        //    inventoryUI.DropToWorld(this, draggedQuantity);
        //}

        if (!isDragging || inventoryUI == null)
        {
            isDragging = false;
            return;
        }
        base.OnEndDrag(eventData);
        if (craftingUI != null)
        {
            StartCoroutine(UpdateCraftingAfterFrame());
        }
    }

    public override void OnPointerDown(PointerEventData eventData)
    {
        base.OnPointerDown(eventData);
        if (craftingUI != null)
            craftingUI.UpdateCraftingResult();
    }

    private System.Collections.IEnumerator UpdateCraftingAfterFrame()
    {
        yield return null;
        if (craftingUI != null)
            craftingUI.UpdateCraftingResult();
    }
}