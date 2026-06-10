using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.SceneManagement;

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
        PruneInvalidSpawnedPlayers();

        if (!runner.IsServer)
            return;

        // 場景準備好後，把還沒 Spawn 的玩家全部補上
        foreach (PlayerRef player in runner.ActivePlayers)
            SpawnPlayer(runner, player);

        pendingPlayers.Clear();
    }

    private void SpawnPlayer(NetworkRunner runner, PlayerRef player)
    {
        if (TryGetSpawnedPlayer(player, out NetworkObject existingPlayerObject))
        {
            Debug.Log($"[PlayerSpawner] Player {player} already spawned on {existingPlayerObject.name}, skip.");
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
        DontDestroyOnLoad(playerObject.gameObject);
        spawnedPlayers[player] = playerObject;

        Debug.Log($"[PlayerSpawner] SUCCESS spawn for {player}");
        Debug.Log($"[PlayerSpawner] spawned object name = {playerObject.name}");
        Debug.Log($"[PlayerSpawner] spawned object position = {playerObject.transform.position}");
        Debug.Log($"[PlayerSpawner] spawnedPlayers.Count = {spawnedPlayers.Count}");
        Debug.Log($"[PlayerSpawner] {playerObject.name} marked DontDestroyOnLoad so Photon Voice survives scene travel.");
    }

    private bool TryGetSpawnedPlayer(PlayerRef player, out NetworkObject playerObject)
    {
        if (spawnedPlayers.TryGetValue(player, out playerObject) && playerObject != null && playerObject.IsValid)
            return true;

        spawnedPlayers.Remove(player);
        playerObject = null;
        return false;
    }

    private void PruneInvalidSpawnedPlayers()
    {
        s_pruneBuffer.Clear();

        foreach (KeyValuePair<PlayerRef, NetworkObject> entry in spawnedPlayers)
        {
            NetworkObject playerObject = entry.Value;
            if (playerObject == null || !playerObject.IsValid)
                s_pruneBuffer.Add(entry.Key);
        }

        for (int i = 0; i < s_pruneBuffer.Count; i++)
        {
            spawnedPlayers.Remove(s_pruneBuffer[i]);
            Debug.LogWarning($"[PlayerSpawner] Removed stale spawned player reference for {s_pruneBuffer[i]}.");
        }

        s_pruneBuffer.Clear();
    }

    private static readonly List<PlayerRef> s_pruneBuffer = new();

    private Transform GetSpawnPointFor(NetworkRunner runner, PlayerRef player)
    {
        Transform teacherSpawn = ResolveOrCreateSpawnPoint("TeacherSpawnPoint", true);
        Transform studentSpawn = ResolveOrCreateSpawnPoint("StudentSpawnPoint", false);

        Debug.Log($"[PlayerSpawner] runner.LocalPlayer = {runner.LocalPlayer}");
        Debug.Log($"[PlayerSpawner] deciding spawn for player = {player}");

        // Host 自己 = Teacher
        if (player == runner.LocalPlayer)
        {
            Debug.Log($"[PlayerSpawner] {player} -> TeacherSpawnPoint");
            return teacherSpawn;
        }

        // 其他加入者 = Student
        Debug.Log($"[PlayerSpawner] {player} -> StudentSpawnPoint");
        return studentSpawn;
    }

    private static Transform ResolveOrCreateSpawnPoint(string spawnPointName, bool isTeacher)
    {
        GameObject existing = GameObject.Find(spawnPointName);
        if (existing != null)
            return existing.transform;

        GameObject spawnPoint = new GameObject(spawnPointName);
        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.IsValid() && activeScene.isLoaded)
            SceneManager.MoveGameObjectToScene(spawnPoint, activeScene);

        Camera camera = Camera.main != null ? Camera.main : UnityEngine.Object.FindObjectOfType<Camera>();
        Vector3 basePosition = camera != null ? camera.transform.position : new Vector3(0f, 1.6f, 0f);
        basePosition.y = Mathf.Max(basePosition.y, 1.6f);

        Quaternion baseRotation = camera != null
            ? Quaternion.Euler(0f, camera.transform.eulerAngles.y, 0f)
            : Quaternion.identity;

        Vector3 offset = isTeacher ? Vector3.zero : baseRotation * new Vector3(0.7f, 0f, 0.7f);
        spawnPoint.transform.SetPositionAndRotation(basePosition + offset, baseRotation);

        Debug.LogWarning($"[PlayerSpawner] {spawnPointName} not found; created runtime fallback at {spawnPoint.transform.position} in scene '{spawnPoint.scene.name}'.");
        return spawnPoint.transform;
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
    public void OnSceneLoadStart(NetworkRunner runner)
    {
        Debug.Log("[PlayerSpawner] OnSceneLoadStart");
        sceneReady = false;
        pendingPlayers.Clear();
        PruneInvalidSpawnedPlayers();
    }
}
