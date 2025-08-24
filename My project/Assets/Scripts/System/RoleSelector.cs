using Mirror;
using UnityEngine;
using UnityEngine.UI;

public class RoleSelector : MonoBehaviour
{
    [Header("UI Buttons")]
    [SerializeField] private Button hostButton;
    [SerializeField] private Button clientButton;

    [Header("Optional")]
    [SerializeField] private GameObject uiPanelToHideOnConnect;

    private void Start()
    {
        // Thêm sự kiện khi bấm nút
        if (hostButton != null)
            hostButton.onClick.AddListener(StartAsHost);

        if (clientButton != null)
            clientButton.onClick.AddListener(StartAsClient);
    }

    // Bắt đầu với Host
    private void StartAsHost()
    {
        // Kiểm tra nếu chưa có server đang chạy
        if (!NetworkServer.active && !NetworkClient.isConnected)
        {
            NetworkManager.singleton.StartHost();  // Khởi tạo server và client cùng lúc

            if (uiPanelToHideOnConnect != null)
                uiPanelToHideOnConnect.SetActive(false);
        }
        else
        {
            Debug.LogWarning("Host is already running.");
        }
    }

    // Bắt đầu với Client
    private void StartAsClient()
    {
        // Kiểm tra nếu chưa kết nối tới server
        if (!NetworkClient.isConnected)
        {
            NetworkManager.singleton.StartClient();  // Khởi tạo client

            if (uiPanelToHideOnConnect != null)
                uiPanelToHideOnConnect.SetActive(false);
        }
        else
        {
            Debug.LogWarning("Client is already connected.");
        }
    }
}
