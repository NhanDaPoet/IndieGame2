using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class InventorySlotUI : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerEnterHandler, IPointerExitHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [Header("UI Components")]
    [SerializeField] private Image itemIcon;
    [SerializeField] private TextMeshProUGUI quantityText;
    [SerializeField] private Image slotBackground;

    [SerializeField] public Color normalColor = new Color(1f, 1f, 1f, 1f);

    [SerializeField] public Color highlightColor = new Color(1f, 1f, 1f, 0.2f);
    [SerializeField] public Color validDropColor = new Color(0f, 1f, 0f, 0.3f);
    [SerializeField] public Color invalidDropColor = new Color(1f, 0f, 0f, 0.3f);

    private ItemStack currentItemStack;
    private int slotIndex;
    private bool isHotbar;

    public InventoryUI inventoryUI;

    public bool isDragging = false;
    public Vector3 originalPosition;

    public bool dragStarted = false;
    public Vector3 pointerDownPosition;
    public const float DRAG_THRESHOLD = 10f;

    public int SlotIndex => slotIndex;
    public bool IsHotbar => isHotbar;
    public bool IsDragging => isDragging;

    public void Initialize(int index, bool hotbar, InventoryUI ui)
    {
        slotIndex = index;
        isHotbar = hotbar;
        inventoryUI = ui;

        if (itemIcon == null) itemIcon = transform.Find("ItemIcon")?.GetComponent<Image>();
        if (quantityText == null) quantityText = transform.Find("QuantityText")?.GetComponent<TextMeshProUGUI>();
        if (slotBackground == null) slotBackground = GetComponent<Image>();
        if (itemIcon != null) itemIcon.raycastTarget = true;
        if (slotBackground != null) slotBackground.raycastTarget = true;
        SetSlotHighlight(normalColor);
        UpdateSlot(new ItemStack());
    }

    public void UpdateSlot(ItemStack itemStack)
    {
        currentItemStack = itemStack;

        if (itemStack.IsEmpty)
        {
            if (itemIcon != null)
            {
                itemIcon.sprite = null;
                itemIcon.color = Color.clear;
            }
            if (quantityText != null)
                quantityText.text = "";
        }
        else
        {
            if (itemIcon != null)
            {
                itemIcon.sprite = itemStack.itemData?.icon;
                itemIcon.color = itemStack.itemData != null ? Color.white : Color.clear;
            }
            if (quantityText != null)
                quantityText.text = itemStack.quantity > 1 ? itemStack.quantity.ToString() : "";
        }
        SetSlotHighlight(normalColor);
    }

    public ItemStack GetItemStack() => currentItemStack;

    // ===== POINTER EVENTS =====

    public virtual void OnPointerDown(PointerEventData eventData)
    {
        pointerDownPosition = eventData.position;
        dragStarted = false;

        if (eventData.button == PointerEventData.InputButton.Left)
        {
            // Don't handle left click immediately - wait to see if it's a drag
            // Click will be handled in OnPointerUp if no drag occurred
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

    public virtual void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left && !dragStarted)
        {
            // This was a click, not a drag
            if (!currentItemStack.IsEmpty)
            {
                inventoryUI.OnSlotClicked(this);
            }
        }
    }

    // ===== DRAG AND DROP EVENTS =====

    public virtual void OnBeginDrag(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left || currentItemStack.IsEmpty)
        {
            return;
        }

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

        int dragQuantity = currentItemStack.quantity;

        if (isSplitDrag && currentItemStack.quantity > 1)
        {
            dragQuantity = 1; // Ctrl + Drag: Take 1 item
        }
        else if (isHalfSplit && currentItemStack.quantity > 1)
        {
            dragQuantity = Mathf.CeilToInt(currentItemStack.quantity / 2f); // Shift + Drag: Take half stack
        }

        inventoryUI.StartDrag(this, dragQuantity);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!dragStarted)
        {
            // Check if we should start dragging now
            float dragDistance = Vector3.Distance(eventData.position, pointerDownPosition);
            if (dragDistance >= DRAG_THRESHOLD && eventData.button == PointerEventData.InputButton.Left && !currentItemStack.IsEmpty)
            {
                OnBeginDrag(eventData);
            }
        }

        if (!isDragging || inventoryUI == null) return;
        inventoryUI.UpdateDrag();
    }

    public virtual void OnEndDrag(PointerEventData eventData)
    {
        if (!isDragging)
        {
            return;
        }

        isDragging = false;
        dragStarted = false;
        InventorySlotUI targetSlot = GetSlotUnderMouse();
        inventoryUI.EndDrag(targetSlot);
        SetSlotHighlight(normalColor);
    }

    // ===== HOVER EVENTS =====

    public virtual void OnPointerEnter(PointerEventData eventData)
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
        if (!currentItemStack.IsEmpty && currentItemStack.itemData != null && !isDragging)
        {
            ShowTooltip();
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        SetSlotHighlight(normalColor);
        HideTooltip();
    }

    // ===== SPECIAL ACTIONS =====

    private void HandleRightClick()
    {
        if (!currentItemStack.IsEmpty)
        {
            if (currentItemStack.quantity > 1)
            {
                int splitAmount = Mathf.CeilToInt(currentItemStack.quantity / 2f);
                inventoryUI.RequestSplitStack(this, splitAmount);
            }
            else
            {
                inventoryUI.UseItem(this);
            }
        }
    }

    private void HandleMiddleClick()
    {
        if (!currentItemStack.IsEmpty)
        {
            inventoryUI.DropSingleItem(this);
        }
    }



    // ===== HELPER METHODS =====

    public InventorySlotUI GetSlotUnderMouse()
    {
        PointerEventData pointerData = new PointerEventData(EventSystem.current);
        pointerData.position = Input.mousePosition;

        var results = new System.Collections.Generic.List<RaycastResult>();
        EventSystem.current.RaycastAll(pointerData, results);

        // Find the closest valid slot (prioritize non-dragging slots)
        InventorySlotUI closestSlot = null;
        foreach (var result in results)
        {
            InventorySlotUI slot = result.gameObject.GetComponent<InventorySlotUI>();
            if (slot != null && slot != this && !slot.IsDragging)
            {
                closestSlot = slot;
                break; // Take the first valid slot found
            }
        }

        return closestSlot;
    }

    public void SetSlotHighlight(Color color)
    {
        if (slotBackground != null)
        {
            slotBackground.color = color;
        }
    }

    public void ShowTooltip()
    {
        if (ItemTooltip.Instance != null)
        {
            ItemTooltip.ShowTooltip(currentItemStack.itemData, transform.position);
        }
    }

    public void HideTooltip()
    {
        if (ItemTooltip.Instance != null)
        {
            ItemTooltip.HideTooltip();
        }
    }

    // ===== PUBLIC METHODS FOR INVENTORY UI =====

    public void SetDragVisuals(bool dragging)
    {
        if (itemIcon != null)
        {
            var canvasGroup = itemIcon.GetComponent<CanvasGroup>();
            if (canvasGroup == null)
                canvasGroup = itemIcon.gameObject.AddComponent<CanvasGroup>();

            canvasGroup.alpha = dragging ? 0.5f : 1f;
        }
    }

    public void ResetPosition()
    {
        transform.position = originalPosition;
    }
}