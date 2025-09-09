using Mirror;
using UnityEngine;
using UnityEngine.UI;

public class CharacterCustomizePanel : MonoBehaviour
{
    [Header("UI Refs")]
    [SerializeField] private InputField nameInput;
    [SerializeField] private Slider colorHueSlider;
    [SerializeField] private Dropdown skinDropdown; 
    [SerializeField] private Button startButton;

    private void Awake()
    {
        startButton.onClick.AddListener(OnClickStart);
    }

    private void OnClickStart()
    {
        if (!NetworkClient.active)
        {
            Debug.LogWarning("Client not connected.");
            return;
        }

        var nameText = string.IsNullOrWhiteSpace(nameInput.text) ? "Player" : nameInput.text.Trim();
        var color = Color.HSVToRGB(Mathf.Clamp01(colorHueSlider.value), 0.7f, 0.9f);
        int skinIndex = skinDropdown != null ? skinDropdown.value : 0;

        var msg = new CharacterSettingsMessage
        {
            playerName = nameText,
            color = color,
            skinIndex = skinIndex
        };

        NetworkClient.Send(msg);

        // Optionally hide panel immediately or wait for server spawn confirmation
        gameObject.SetActive(false);
    }
}
