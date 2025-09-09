using Mirror;
using UnityEngine;
using UnityEngine.UI;

public class RoleSelector : MonoBehaviour
{
    [Header("UI Buttons")]
    [SerializeField] private Button hostButton;
    [SerializeField] private Button clientButton;

    [Header("UI Panels")]
    [SerializeField] private GameObject uiPanelToHideOnConnect;
    [SerializeField] private GameObject worldSettingsPanel; 
    [SerializeField] private GameObject waitingPanel;     
    private void Start()
    {
        if (hostButton != null)
            hostButton.onClick.AddListener(StartAsHost);
        if (clientButton != null)
            clientButton.onClick.AddListener(StartAsClient);
        if (worldSettingsPanel) worldSettingsPanel.SetActive(false);
        if (waitingPanel) waitingPanel.SetActive(false);
    }

    private void StartAsHost()
    {
        if (!NetworkServer.active && !NetworkClient.isConnected)
        {
            NetworkManager.singleton.StartHost();
            if (uiPanelToHideOnConnect != null)
                uiPanelToHideOnConnect.SetActive(false);
            if (worldSettingsPanel) worldSettingsPanel.SetActive(true);
        }
        else
        {
            Debug.LogWarning("Host is already running.");
        }
    }

    private void StartAsClient()
    {
        if (!NetworkClient.isConnected)
        {
            NetworkManager.singleton.StartClient();
            if (uiPanelToHideOnConnect != null)
                uiPanelToHideOnConnect.SetActive(false);
            if (waitingPanel) waitingPanel.SetActive(true);
        }
        else
        {
            Debug.LogWarning("Client is already connected.");
        }
    }
}
