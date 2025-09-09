using Mirror;
using UnityEngine;

public class CustomNetworkManager : NetworkManager
{
    [Header("Debug Settings")]
    public bool enableDebugLogs = true;

    public override void OnServerAddPlayer(NetworkConnectionToClient conn)
    {
        if (enableDebugLogs)
            Debug.Log($"[Server] Client {conn.connectionId} connected. waiting for spawn player...");
    }

    [Server]
    public void SpawnPlayerForConnection(NetworkConnectionToClient conn, GameObject prefab, Vector3 position, Quaternion rotation)
    {
        if (conn.identity != null)
        {
            if (enableDebugLogs)
                Debug.LogWarning($"[Server] Connection {conn.connectionId} already have player, doesnt spawn again.");
            return;
        }
        GameObject playerGO = Instantiate(prefab, position, rotation);
        playerGO.name = $"Player_Conn_{conn.connectionId}";
        NetworkServer.AddPlayerForConnection(conn, playerGO);
        if (enableDebugLogs)
            Debug.Log($"[Server] Spawned player for connection {conn.connectionId}");
    }

    public override void OnClientConnect()
    {
        base.OnClientConnect();

        if (enableDebugLogs)
        if (!NetworkClient.ready)
        {
            NetworkClient.Ready();
            if (enableDebugLogs)
                Debug.Log("[Client] NetworkClient.Ready() called");
        }
    }

    public override void OnClientChangeScene(string newSceneName, SceneOperation sceneOperation, bool customHandling)
    {
        base.OnClientChangeScene(newSceneName, sceneOperation, customHandling);
        if (enableDebugLogs)
            Debug.Log($"[Client] Scene changed to: {newSceneName}");
    }

    public override void OnClientNotReady()
    {
        base.OnClientNotReady();
        if (enableDebugLogs)
            Debug.Log("[Client] OnClientNotReady called");
    }
}
