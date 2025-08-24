using Mirror;
using UnityEngine;

public class CustomNetworkManager : NetworkManager
{
    [Header("Debug Settings")]
    public bool enableDebugLogs = true;

    public override void OnServerAddPlayer(NetworkConnectionToClient conn)
    {
        Vector3 spawnPos = numPlayers == 0 ? new Vector3(-2f, 0f, 0f) : new Vector3(2f, 0f, 0f);
        GameObject playerGO = Instantiate(playerPrefab, spawnPos, Quaternion.identity);
        playerGO.name = $"Player_Conn_{conn.connectionId}";

        NetworkServer.AddPlayerForConnection(conn, playerGO);
    }

    // Override OnClientConnect để đảm bảo client setup đúng
    public override void OnClientConnect()
    {
        base.OnClientConnect();

        if (enableDebugLogs)

        // Đảm bảo client ready
        if (!NetworkClient.ready)
        {
            NetworkClient.Ready();
            if (enableDebugLogs)
                Debug.Log("[Client] NetworkClient.Ready() called");
        }
    }

    // Thêm callback để track khi local player được assign
    public override void OnClientChangeScene(string newSceneName, SceneOperation sceneOperation, bool customHandling)
    {
        base.OnClientChangeScene(newSceneName, sceneOperation, customHandling);
        if (enableDebugLogs)
            Debug.Log($"[Client] Scene changed to: {newSceneName}");
    }

    // Debug callback khi có NetworkIdentity spawn
    public override void OnClientNotReady()
    {
        base.OnClientNotReady();
        if (enableDebugLogs)
            Debug.Log("[Client] OnClientNotReady called");
    }
}
