using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fusion;
using Fusion.Sockets;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class FusionLauncher : MonoBehaviour, INetworkRunnerCallbacks
{
    [Header("UI")]
    [SerializeField] private TMP_InputField roomNameInput;
    [SerializeField] private TMP_Text statusText;

    [Header("Lobby Objects To Hide")]
    [SerializeField] private GameObject lobbyCanvas;
    [SerializeField] private GameObject lobbyEventSystem;

    [Header("Lobby Camera To Disable")]
    [SerializeField] private Camera lobbyCamera;

    [Header("Runner Prefab")]
    [SerializeField] private NetworkRunner runnerPrefab;

    [Header("Scene Settings")]
    [SerializeField] private int classroomSceneBuildIndex = 1;

    private NetworkRunner runner;
    private bool isStartingGame = false;

    private void Awake()
    {
        Debug.Log("[FusionLauncher] Awake()");
        DebugLogCurrentConfig();

        if (statusText != null)
            statusText.text = "Ready";
    }

    public async void StartAsTeacherHost()
    {
        if (isStartingGame)
        {
            Debug.LogWarning("[FusionLauncher] StartAsTeacherHost ignored: already starting game.");
            return;
        }

        LocalUserProfile.Role = UserRole.Teacher;
        LocalUserProfile.RoomName = GetRoomName();

        Debug.Log($"[FusionLauncher] StartAsTeacherHost clicked. RoomName = {LocalUserProfile.RoomName}");
        await StartGame(GameMode.Host);
    }

    public async void StartAsStudentClient()
    {
        if (isStartingGame)
        {
            Debug.LogWarning("[FusionLauncher] StartAsStudentClient ignored: already starting game.");
            return;
        }

        LocalUserProfile.Role = UserRole.Student;
        LocalUserProfile.RoomName = GetRoomName();

        Debug.Log($"[FusionLauncher] StartAsStudentClient clicked. RoomName = {LocalUserProfile.RoomName}");
        await StartGame(GameMode.Client);
    }

    private string GetRoomName()
    {
        if (roomNameInput != null && !string.IsNullOrWhiteSpace(roomNameInput.text))
        {
            string trimmed = roomNameInput.text.Trim();
            Debug.Log($"[FusionLauncher] GetRoomName() from input = '{trimmed}'");
            return trimmed;
        }

        Debug.Log("[FusionLauncher] GetRoomName() fallback to default = 'XRRoom01'");
        return "XRRoom01";
    }

    private async Task StartGame(GameMode mode)
    {
        Debug.Log("====================================================");
        Debug.Log("[FusionLauncher] StartGame() BEGIN");
        Debug.Log($"[FusionLauncher] Mode = {mode}");
        Debug.Log($"[FusionLauncher] Role = {LocalUserProfile.Role}");
        Debug.Log($"[FusionLauncher] RoomName = {LocalUserProfile.RoomName}");
        Debug.Log($"[FusionLauncher] classroomSceneBuildIndex = {classroomSceneBuildIndex}");

        if (runnerPrefab == null)
        {
            SetStatus("Runner Prefab is missing!");
            Debug.LogError("[FusionLauncher] runnerPrefab is not assigned.");
            isStartingGame = false;
            return;
        }

        if (!IsSceneBuildIndexValid(classroomSceneBuildIndex))
        {
            SetStatus("Start failed: Invalid Scene Index");
            Debug.LogError($"[FusionLauncher] Invalid scene build index: {classroomSceneBuildIndex}");
            isStartingGame = false;
            return;
        }

        string scenePath = SceneUtility.GetScenePathByBuildIndex(classroomSceneBuildIndex);
        Debug.Log($"[FusionLauncher] Scene path for build index {classroomSceneBuildIndex} = {scenePath}");

        isStartingGame = true;
        SetStatus($"Starting as {mode}...");

        // 清理舊 runner
        if (runner != null)
        {
            Debug.Log("[FusionLauncher] Existing runner found. Cleaning old runner...");

            try
            {
                runner.RemoveCallbacks(this);

                var spawnerOld = runner.GetComponent<PlayerSpawner>();
                if (spawnerOld != null)
                {
                    runner.RemoveCallbacks(spawnerOld);
                }

                if (runner.IsRunning)
                {
                    Debug.Log("[FusionLauncher] Old runner is running. Shutting down...");
                    await runner.Shutdown();
                    Debug.Log("[FusionLauncher] Old runner shutdown complete.");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FusionLauncher] Shutdown old runner failed: {e}");
            }

            if (runner != null)
            {
                Destroy(runner.gameObject);
                Debug.Log("[FusionLauncher] Old runner GameObject destroyed.");
            }

            runner = null;
        }

        // 建立新 runner
        try
        {
            runner = Instantiate(runnerPrefab);
            runner.name = "NetworkRunner";
            runner.ProvideInput = true;

            // 註冊 FusionLauncher 自己
            runner.AddCallbacks(this);

            // 註冊 PlayerSpawner
            var spawner = runner.GetComponent<PlayerSpawner>();
            if (spawner != null)
            {
                runner.AddCallbacks(spawner);
                Debug.Log("[FusionLauncher] PlayerSpawner callbacks registered.");
            }
            else
            {
                Debug.LogError("[FusionLauncher] PlayerSpawner not found on runner prefab!");
            }

            DontDestroyOnLoad(runner.gameObject);

            Debug.Log("[FusionLauncher] Runner instantiated and registered.");
        }
        catch (Exception e)
        {
            Debug.LogError($"[FusionLauncher] Failed while creating runner: {e}");
            SetStatus("Start failed: Runner Creation Exception");
            isStartingGame = false;
            return;
        }

        var sceneInfo = new NetworkSceneInfo();
        sceneInfo.AddSceneRef(SceneRef.FromIndex(classroomSceneBuildIndex), LoadSceneMode.Single);
        Debug.Log($"[FusionLauncher] NetworkSceneInfo created with SceneRef.FromIndex({classroomSceneBuildIndex}) and LoadSceneMode.Single");

        StartGameResult result;
        try
        {
            Debug.Log("[FusionLauncher] Calling runner.StartGame(...)");

            result = await runner.StartGame(new StartGameArgs
            {
                GameMode = mode,
                SessionName = LocalUserProfile.RoomName,
                Scene = sceneInfo,
                PlayerCount = 2
            });

            Debug.Log("[FusionLauncher] runner.StartGame(...) returned.");
        }
        catch (Exception e)
        {
            Debug.LogError($"[FusionLauncher] StartGame exception: {e}");
            SetStatus("Start failed: Exception");
            isStartingGame = false;
            return;
        }

        Debug.Log($"[FusionLauncher] StartGame result.Ok = {result.Ok}");
        Debug.Log($"[FusionLauncher] StartGame result.ShutdownReason = {result.ShutdownReason}");
        Debug.Log($"[FusionLauncher] StartGame result.ErrorMessage = {result.ErrorMessage}");

        if (result.Ok)
        {
            SetStatus($"Connected: {LocalUserProfile.RoomName}");
            Debug.Log($"[FusionLauncher] StartGame SUCCESS. Mode = {mode}, Room = {LocalUserProfile.RoomName}");

            if (lobbyCanvas != null)
            {
                lobbyCanvas.SetActive(false);
                Debug.Log("[FusionLauncher] Lobby Canvas hidden.");
            }
            else
            {
                Debug.LogWarning("[FusionLauncher] lobbyCanvas is not assigned.");
            }

            if (lobbyEventSystem != null)
            {
                lobbyEventSystem.SetActive(false);
                Debug.Log("[FusionLauncher] Lobby EventSystem hidden.");
            }
            else
            {
                Debug.LogWarning("[FusionLauncher] lobbyEventSystem is not assigned.");
            }

            if (lobbyCamera != null)
            {
                lobbyCamera.gameObject.SetActive(false);
                Debug.Log("[FusionLauncher] Lobby Camera disabled.");
            }
            else
            {
                Debug.LogWarning("[FusionLauncher] lobbyCamera is not assigned.");
            }

            gameObject.SetActive(false);
            Debug.Log("[FusionLauncher] FusionLauncherObject disabled.");
        }
        else
        {
            string finalMessage = string.IsNullOrWhiteSpace(result.ErrorMessage)
                ? $"Start failed: {result.ShutdownReason}"
                : $"Start failed: {result.ShutdownReason}\n{result.ErrorMessage}";

            SetStatus(finalMessage);
            Debug.LogError($"[FusionLauncher] StartGame FAILED. ShutdownReason = {result.ShutdownReason}, ErrorMessage = {result.ErrorMessage}");
        }

        isStartingGame = false;
        Debug.Log("[FusionLauncher] StartGame() END");
        Debug.Log("====================================================");
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;

        Debug.Log($"[FusionLauncher] Status = {message}");
    }

    private bool IsSceneBuildIndexValid(int buildIndex)
    {
        if (buildIndex < 0 || buildIndex >= SceneManager.sceneCountInBuildSettings)
            return false;

        string path = SceneUtility.GetScenePathByBuildIndex(buildIndex);
        return !string.IsNullOrEmpty(path);
    }

    private void DebugLogCurrentConfig()
    {
        Debug.Log("--------------- FusionLauncher Config ---------------");
        Debug.Log($"[FusionLauncher] runnerPrefab assigned = {runnerPrefab != null}");
        Debug.Log($"[FusionLauncher] lobbyCanvas assigned = {lobbyCanvas != null}");
        Debug.Log($"[FusionLauncher] lobbyEventSystem assigned = {lobbyEventSystem != null}");
        Debug.Log($"[FusionLauncher] lobbyCamera assigned = {lobbyCamera != null}");
        Debug.Log($"[FusionLauncher] classroomSceneBuildIndex = {classroomSceneBuildIndex}");
        Debug.Log($"[FusionLauncher] sceneCountInBuildSettings = {SceneManager.sceneCountInBuildSettings}");

        for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
        {
            Debug.Log($"[FusionLauncher] BuildSettings Scene[{i}] = {SceneUtility.GetScenePathByBuildIndex(i)}");
        }

        Debug.Log("-----------------------------------------------------");
    }

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"[FusionLauncher] OnPlayerJoined: {player}");
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"[FusionLauncher] OnPlayerLeft: {player}");
    }

    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        Debug.LogWarning($"[FusionLauncher] OnShutdown: {shutdownReason}");
        isStartingGame = false;
    }

    public void OnConnectedToServer(NetworkRunner runner)
    {
        Debug.Log("[FusionLauncher] OnConnectedToServer");
    }

    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
        Debug.LogWarning($"[FusionLauncher] OnDisconnectedFromServer: {reason}");
        isStartingGame = false;
    }

    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }

    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
    {
        Debug.LogError($"[FusionLauncher] OnConnectFailed: {reason}");
        isStartingGame = false;
    }

    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }

    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList)
    {
        Debug.Log($"[FusionLauncher] OnSessionListUpdated: count = {sessionList?.Count ?? 0}");
    }

    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }

    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken)
    {
        Debug.LogWarning("[FusionLauncher] OnHostMigration");
    }

    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }

    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }

    public void OnInput(NetworkRunner runner, NetworkInput input) { }

    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input)
    {
        Debug.LogWarning($"[FusionLauncher] OnInputMissing from {player}");
    }

    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }

    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }

    public void OnSceneLoadDone(NetworkRunner runner)
    {
        Debug.Log("[FusionLauncher] OnSceneLoadDone");

        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            Debug.Log($"[FusionLauncher] Loaded Scene[{i}] = {scene.name}, isLoaded={scene.isLoaded}");
        }

        Scene activeScene = SceneManager.GetActiveScene();
        Debug.Log($"[FusionLauncher] Active Scene = {activeScene.name}");

        AudioListener[] listeners = FindObjectsOfType<AudioListener>(true);
        Debug.Log($"[FusionLauncher] AudioListener count = {listeners.Length}");

        foreach (var listener in listeners)
        {
            Debug.Log($"[FusionLauncher] AudioListener on object = {listener.gameObject.name}, active={listener.gameObject.activeInHierarchy}");
        }

        Camera[] cameras = FindObjectsOfType<Camera>(true);
        Debug.Log($"[FusionLauncher] Camera count = {cameras.Length}");

        foreach (var cam in cameras)
        {
            Debug.Log($"[FusionLauncher] Camera = {cam.gameObject.name}, active={cam.gameObject.activeInHierarchy}");
        }
    }

    public void OnSceneLoadStart(NetworkRunner runner)
    {
        Debug.Log("[FusionLauncher] OnSceneLoadStart");
    }
}