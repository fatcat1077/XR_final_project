using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using UnityEngine;

public class PlayerSpawner : MonoBehaviour, INetworkRunnerCallbacks
{
    [Header("Player Prefab")]
    public NetworkPrefabRef playerPrefab;

    private readonly Dictionary<PlayerRef, NetworkObject> spawnedPlayers = new();
    private readonly List<PlayerRef> pendingPlayers = new();

    private bool sceneReady = false;

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"[PlayerSpawner] OnPlayerJoined: {player}");

        // 只有 Host / Server 負責 Spawn
        if (!runner.IsServer)
            return;

        // 場景還沒載入完，先排隊
        if (!sceneReady)
        {
            Debug.Log($"[PlayerSpawner] Scene not ready, queue player {player}");

            if (!pendingPlayers.Contains(player))
                pendingPlayers.Add(player);

            return;
        }

        SpawnPlayer(runner, player);
    }

    public void OnSceneLoadDone(NetworkRunner runner)
    {
        Debug.Log("[PlayerSpawner] OnSceneLoadDone");
        sceneReady = true;

        if (!runner.IsServer)
            return;

        // 場景準備好後，把還沒 Spawn 的玩家全部補上
        for (int i = 0; i < pendingPlayers.Count; i++)
        {
            SpawnPlayer(runner, pendingPlayers[i]);
        }

        pendingPlayers.Clear();
    }

    private void SpawnPlayer(NetworkRunner runner, PlayerRef player)
    {
        if (spawnedPlayers.ContainsKey(player))
        {
            Debug.Log($"[PlayerSpawner] Player {player} already spawned, skip.");
            return;
        }

        Transform spawnPoint = GetSpawnPointFor(runner, player);

        if (spawnPoint == null)
        {
            Debug.LogError($"[PlayerSpawner] SpawnPoint not found for {player}");
            return;
        }

        Debug.Log($"[PlayerSpawner] TRY spawn for {player} at {spawnPoint.name} pos={spawnPoint.position}");

        NetworkObject playerObject = runner.Spawn(
            playerPrefab,
            spawnPoint.position,
            spawnPoint.rotation,
            player
        );

        if (playerObject == null)
        {
            Debug.LogError($"[PlayerSpawner] runner.Spawn returned NULL for {player}");
            return;
        }

        playerObject.name = $"PlayerAvatar_{player.PlayerId}";
        spawnedPlayers[player] = playerObject;

        Debug.Log($"[PlayerSpawner] SUCCESS spawn for {player}");
        Debug.Log($"[PlayerSpawner] spawned object name = {playerObject.name}");
        Debug.Log($"[PlayerSpawner] spawned object position = {playerObject.transform.position}");
        Debug.Log($"[PlayerSpawner] spawnedPlayers.Count = {spawnedPlayers.Count}");
    }

    private Transform GetSpawnPointFor(NetworkRunner runner, PlayerRef player)
    {
        GameObject teacherSpawn = GameObject.Find("TeacherSpawnPoint");
        GameObject studentSpawn = GameObject.Find("StudentSpawnPoint");

        if (teacherSpawn == null)
        {
            Debug.LogError("[PlayerSpawner] TeacherSpawnPoint not found in scene!");
            return null;
        }

        if (studentSpawn == null)
        {
            Debug.LogError("[PlayerSpawner] StudentSpawnPoint not found in scene!");
            return null;
        }

        Debug.Log($"[PlayerSpawner] runner.LocalPlayer = {runner.LocalPlayer}");
        Debug.Log($"[PlayerSpawner] deciding spawn for player = {player}");

        // Host 自己 = Teacher
        if (player == runner.LocalPlayer)
        {
            Debug.Log($"[PlayerSpawner] {player} -> TeacherSpawnPoint");
            return teacherSpawn.transform;
        }

        // 其他加入者 = Student
        Debug.Log($"[PlayerSpawner] {player} -> StudentSpawnPoint");
        return studentSpawn.transform;
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"[PlayerSpawner] OnPlayerLeft: {player}");

        if (!runner.IsServer)
            return;

        if (spawnedPlayers.TryGetValue(player, out NetworkObject playerObject))
        {
            runner.Despawn(playerObject);
            spawnedPlayers.Remove(player);
            Debug.Log($"[PlayerSpawner] Despawned player object for {player}");
        }

        pendingPlayers.Remove(player);
    }

    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        Debug.Log($"[PlayerSpawner] OnShutdown: {shutdownReason}");
        sceneReady = false;
        spawnedPlayers.Clear();
        pendingPlayers.Clear();
    }

    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, System.ArraySegment<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) 
    {
        Debug.LogWarning($"[PlayerSpawner] OnInputMissing from {player}");
    }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
}