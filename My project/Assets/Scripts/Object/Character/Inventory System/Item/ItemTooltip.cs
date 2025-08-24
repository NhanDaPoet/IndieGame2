using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ItemTooltip : NetworkBehaviour
{
    [Header("UI Components")]
    [SerializeField] private GameObject tooltipPanel;
    [SerializeField] private TextMeshProUGUI itemNameText;
    [SerializeField] private TextMeshProUGUI itemDescriptionText;
    [SerializeField] private TextMeshProUGUI itemStatsText;
    [SerializeField] private Image itemIconImage;
    [SerializeField] private Image rarityBackground;

    [Header("Positioning")]
    [SerializeField] private Vector2 offset = new Vector2(10, 10);
    [SerializeField] private bool followMouse = true;

    [Header("Rarity Colors")]
    [SerializeField] private Color commonColor = Color.white;
    [SerializeField] private Color uncommonColor = Color.green;
    [SerializeField] private Color rareColor = Color.blue;
    [SerializeField] private Color epicColor = Color.magenta;
    [SerializeField] private Color legendaryColor = Color.yellow;

    private Canvas parentCanvas;
    private RectTransform tooltipRect;
    private Camera uiCamera;

    public static ItemTooltip Instance { get; private set; }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            Initialize();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Initialize()
    {
        parentCanvas = GetComponentInParent<Canvas>();
        tooltipRect = GetComponent<RectTransform>();

        if (parentCanvas.renderMode == RenderMode.ScreenSpaceCamera)
            uiCamera = parentCanvas.worldCamera;

        if (tooltipPanel != null)
            tooltipPanel.SetActive(false);
    }

    private void Update()
    {
        if (tooltipPanel.activeInHierarchy && followMouse)
        {
            UpdateTooltipPosition();
        }
    }

    public static void ShowTooltip(ItemData itemData, Vector3? worldPosition = null)
    {
        if (Instance != null && itemData != null)
        {
            Instance.DisplayTooltip(itemData, worldPosition);
        }
    }

    public static void HideTooltip()
    {
        if (Instance != null)
        {
            Instance.HideTooltipPanel();
        }
    }

    private void DisplayTooltip(ItemData itemData, Vector3? worldPosition = null)
    {
        if (tooltipPanel == null) return;

        // Set item name
        if (itemNameText != null)
        {
            itemNameText.text = itemData.itemName;
            itemNameText.color = GetRarityColor(itemData.rarity);
        }

        // Set item description
        if (itemDescriptionText != null)
        {
            itemDescriptionText.text = itemData.description;
        }

        // Set item stats/additional info
        if (itemStatsText != null)
        {
            string statsText = BuildStatsText(itemData);
            itemStatsText.text = statsText;
            itemStatsText.gameObject.SetActive(!string.IsNullOrEmpty(statsText));
        }

        // Set item icon
        if (itemIconImage != null)
        {
            itemIconImage.sprite = itemData.icon;
            itemIconImage.color = Color.white;
        }

        // Set rarity background
        if (rarityBackground != null)
        {
            rarityBackground.color = GetRarityColor(itemData.rarity);
        }

        // Position tooltip
        if (worldPosition.HasValue)
        {
            SetTooltipPosition(worldPosition.Value);
        }
        else
        {
            UpdateTooltipPosition();
        }

        tooltipPanel.SetActive(true);
    }

    private void HideTooltipPanel()
    {
        if (tooltipPanel != null)
            tooltipPanel.SetActive(false);
    }

    private Color GetRarityColor(ItemRarity rarity)
    {
        return rarity switch
        {
            ItemRarity.Common => commonColor,
            ItemRarity.Uncommon => uncommonColor,
            ItemRarity.Rare => rareColor,
            ItemRarity.Epic => epicColor,
            ItemRarity.Legendary => legendaryColor,
            _ => commonColor
        };
    }

    private string BuildStatsText(ItemData itemData)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();

        // Add max stack size
        if (itemData.maxStackSize > 1)
        {
            sb.AppendLine($"Max Stack: {itemData.maxStackSize}");
        }

        // Add rarity
        sb.AppendLine($"Rarity: {itemData.rarity}");

        // Add item type specific stats
        switch (itemData.itemType)
        {
            case ItemType.Weapon:
                // Add weapon stats if available
                sb.AppendLine("Type: Weapon");
                break;
            case ItemType.Consumable:
                sb.AppendLine("Type: Consumable");
                break;
            case ItemType.Material:
                sb.AppendLine("Type: Material");
                break;
            case ItemType.Tool:
                sb.AppendLine("Type: Tool");
                break;
        }

        // Add custom properties if ItemData has them
        // This would require extending ItemData with additional properties

        return sb.ToString().TrimEnd();
    }

    private void SetTooltipPosition(Vector3 worldPosition)
    {
        if (tooltipRect == null) return;

        Vector2 screenPosition;

        if (uiCamera != null)
        {
            screenPosition = uiCamera.WorldToScreenPoint(worldPosition);
        }
        else
        {
            screenPosition = worldPosition;
        }

        SetTooltipScreenPosition(screenPosition);
    }

    private void UpdateTooltipPosition()
    {
        SetTooltipScreenPosition(Input.mousePosition);
    }

    private void SetTooltipScreenPosition(Vector2 screenPosition)
    {
        if (tooltipRect == null || parentCanvas == null) return;

        Vector2 localPosition;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            parentCanvas.transform as RectTransform,
            screenPosition,
            uiCamera,
            out localPosition
        );

        // Apply offset
        localPosition += offset;

        // Clamp to screen bounds
        Vector2 canvasSize = (parentCanvas.transform as RectTransform).sizeDelta;
        Vector2 tooltipSize = tooltipRect.sizeDelta;

        // Adjust position to keep tooltip on screen
        if (localPosition.x + tooltipSize.x > canvasSize.x / 2)
        {
            localPosition.x = screenPosition.x - tooltipSize.x - offset.x;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parentCanvas.transform as RectTransform,
                new Vector2(localPosition.x, screenPosition.y),
                uiCamera,
                out localPosition
            );
        }

        if (localPosition.y + tooltipSize.y > canvasSize.y / 2)
        {
            localPosition.y = screenPosition.y - tooltipSize.y - offset.y;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parentCanvas.transform as RectTransform,
                new Vector2(screenPosition.x, localPosition.y),
                uiCamera,
                out localPosition
            );
        }

        tooltipRect.localPosition = localPosition;
    }

    public static void ShowTooltipWithCustomText(string title, string description, Vector3? worldPosition = null)
    {
        if (Instance != null)
        {
            Instance.DisplayCustomTooltip(title, description, worldPosition);
        }
    }

    private void DisplayCustomTooltip(string title, string description, Vector3? worldPosition = null)
    {
        if (tooltipPanel == null) return;

        // Set custom text
        if (itemNameText != null)
        {
            itemNameText.text = title;
            itemNameText.color = commonColor;
        }

        if (itemDescriptionText != null)
        {
            itemDescriptionText.text = description;
        }

        // Hide other elements
        if (itemStatsText != null)
            itemStatsText.gameObject.SetActive(false);

        if (itemIconImage != null)
            itemIconImage.color = Color.clear;

        if (rarityBackground != null)
            rarityBackground.color = Color.clear;

        // Position tooltip
        if (worldPosition.HasValue)
        {
            SetTooltipPosition(worldPosition.Value);
        }
        else
        {
            UpdateTooltipPosition();
        }

        tooltipPanel.SetActive(true);
    }
}