using Mirror;
using UnityEngine;
using UnityEngine.UI;

public class WorldSettingsPanel : MonoBehaviour
{
    [Header("UI Refs")]
    [SerializeField] private Dropdown mapSizeDropdown;
    [SerializeField] private InputField seedInput;
    [SerializeField] private Button generateButton;
    [SerializeField] private GameObject generatingPanel;
    [SerializeField] private Slider generatingProgress;
    [SerializeField] private Text generatingStageText;
    [SerializeField] private GameObject worldSettingPanel;

    [Header("Next Step")]
    [SerializeField] private GameObject characterCustomizePanel;

    private bool isGenerating = false;

    private void Awake()
    {
        generateButton.onClick.AddListener(OnClickGenerate);
        SetupInitialState();
    }

    private void SetupInitialState()
    {
        if (generatingPanel) generatingPanel.SetActive(false);
        if (worldSettingPanel) worldSettingPanel.SetActive(true);
        if (characterCustomizePanel) characterCustomizePanel.SetActive(false);
        isGenerating = false;
        Debug.Log("WorldSettingsPanel initialized");
    }

    private void OnEnable()
    {
        if (NetworkClient.active)
        {
            NetworkClient.RegisterHandler<WorldGeneratingMessage>(OnWorldGenerating);
            NetworkClient.RegisterHandler<WorldReadyMessage>(OnWorldReady);
        }
    }

    private void OnDisable()
    {
        if (NetworkClient.active)
        {
            NetworkClient.UnregisterHandler<WorldGeneratingMessage>();
            NetworkClient.UnregisterHandler<WorldReadyMessage>();
        }
    }

    private void Start()
    {
        if (NetworkClient.active)
        {
            NetworkClient.RegisterHandler<WorldGeneratingMessage>(OnWorldGenerating);
            NetworkClient.RegisterHandler<WorldReadyMessage>(OnWorldReady);
        }
    }

    private void OnClickGenerate()
    {
        if (!NetworkClient.active)
        {
            Debug.LogWarning("Client not connected.");
            return;
        }
        if (isGenerating)
        {
            Debug.LogWarning("Already generating world!");
            return;
        }
        var size = (MapSize)Mathf.Clamp(mapSizeDropdown.value, 0, 2);
        int seed = 0;
        if (!string.IsNullOrWhiteSpace(seedInput.text))
        {
            if (!int.TryParse(seedInput.text, out seed))
            {
                seed = seedInput.text.GetHashCode();
            }
        }
        var req = new WorldSettingsRequest
        {
            mapSize = size,
            seed = seed
        };
        if (generatingPanel) generatingPanel.SetActive(true);
        if (worldSettingPanel) worldSettingPanel.SetActive(false);

        if (generatingProgress) generatingProgress.value = 0f;
        if (generatingStageText) generatingStageText.text = "Requesting generation...";

        isGenerating = true;

        NetworkClient.Send(req);
        Debug.Log("World generation request sent");
    }

    private void OnWorldGenerating(WorldGeneratingMessage msg)
    {
        if (!isGenerating)
        {
            if (generatingPanel) generatingPanel.SetActive(true);
            if (worldSettingPanel) worldSettingPanel.SetActive(false);
            isGenerating = true;
        }
        if (generatingPanel && generatingPanel.activeSelf)
        {
            if (generatingProgress)
            {
                generatingProgress.value = Mathf.Clamp01(msg.progress01);
            }
            if (generatingStageText)
            {
                generatingStageText.text = msg.stage ?? "Generating...";
            }
        }
    }

    private void OnWorldReady(WorldReadyMessage msg)
    {
        isGenerating = false;
        if (generatingPanel) generatingPanel.SetActive(false);
        if (characterCustomizePanel)
        {
            characterCustomizePanel.SetActive(true);
        }
        else
        {
            Debug.LogWarning("Character customize panel is not assigned!");
        }
    }

    public void ResetPanel()
    {
        SetupInitialState();
        Debug.Log("WorldSettingsPanel reset");
    }

    private void Update()
    {
        if (NetworkClient.active && !NetworkClient.isConnected)
        {
            NetworkClient.RegisterHandler<WorldGeneratingMessage>(OnWorldGenerating);
            NetworkClient.RegisterHandler<WorldReadyMessage>(OnWorldReady);
        }
    }
}