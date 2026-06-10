using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class QuestClassroomSceneTravel
{
    public const string TestClassroomScenePath = "Assets/Scenes/Test_classroom.unity";
    public const string OceanScenePath = "Assets/Scenes/Ocean.unity";
    public const string SpaceScenePath = "Assets/Scenes/Space.unity";

    public static bool RequestEnvironmentScene(ClassroomEnvironment environment, string reason)
    {
        return environment switch
        {
            ClassroomEnvironment.Default => RequestClassroomScene(reason),
            ClassroomEnvironment.Ocean => RequestSceneByPath(OceanScenePath, reason),
            ClassroomEnvironment.Space => RequestSceneByPath(SpaceScenePath, reason),
            _ => false
        };
    }

    public static bool RequestClassroomScene(string reason)
    {
        return RequestSceneByPath(TestClassroomScenePath, reason);
    }

    public static bool RequestSceneByPath(string scenePath, string reason)
    {
        int buildIndex = ResolveSceneBuildIndex(scenePath);
        if (buildIndex < 0)
        {
            Debug.LogError($"[QuestClassroomSceneTravel] Scene path is not in Build Settings: {scenePath}");
            return false;
        }

        if (TryGetRunningRunner(out NetworkRunner runner))
            return RequestNetworkSceneLoad(runner, buildIndex, scenePath, reason);

        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.IsValid() && activeScene.buildIndex == buildIndex)
        {
            Debug.Log($"[QuestClassroomSceneTravel] Already in scene '{scenePath}'. Reason: {reason}");
            return true;
        }

        Debug.Log($"[QuestClassroomSceneTravel] No running NetworkRunner found. Loading local scene '{scenePath}' with Single mode. Reason: {reason}");
        SceneManager.LoadScene(buildIndex, LoadSceneMode.Single);
        return true;
    }

    private static bool RequestNetworkSceneLoad(NetworkRunner runner, int buildIndex, string scenePath, string reason)
    {
        if (!runner.IsSceneAuthority)
        {
            Debug.LogWarning($"[QuestClassroomSceneTravel] Ignored scene load '{scenePath}' because this runner does not have scene authority. Reason: {reason}");
            return false;
        }

        if (runner.IsSceneManagerBusy)
        {
            Debug.LogWarning($"[QuestClassroomSceneTravel] Ignored scene load '{scenePath}' because the scene manager is busy. Reason: {reason}");
            return false;
        }

        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.IsValid() && activeScene.buildIndex == buildIndex)
        {
            Debug.Log($"[QuestClassroomSceneTravel] Already in scene '{scenePath}'. Reason: {reason}");
            return true;
        }

        try
        {
            SceneRef sceneRef = SceneRef.FromIndex(buildIndex);
            NetworkSceneAsyncOp loadOp = runner.LoadScene(sceneRef, LoadSceneMode.Single, LocalPhysicsMode.None, true);
            Debug.Log($"[QuestClassroomSceneTravel] Network scene load requested: {scenePath} index={buildIndex}, opValid={loadOp.IsValid}. Reason: {reason}");
            return loadOp.IsValid;
        }
        catch (System.Exception exception)
        {
            Debug.LogError($"[QuestClassroomSceneTravel] Network scene load failed for '{scenePath}': {exception}");
            return false;
        }
    }

    private static bool TryGetRunningRunner(out NetworkRunner runner)
    {
        runner = null;

        NetworkRunner[] runners = UnityEngine.Object.FindObjectsOfType<NetworkRunner>(true);
        for (int i = 0; i < runners.Length; i++)
        {
            NetworkRunner candidate = runners[i];
            if (candidate == null || !candidate.IsRunning || candidate.IsShutdown)
                continue;

            if (candidate.IsSceneAuthority)
            {
                runner = candidate;
                return true;
            }

            runner ??= candidate;
        }

        return runner != null;
    }

    private static int ResolveSceneBuildIndex(string scenePath)
    {
        string expectedPath = NormalizeScenePath(scenePath);
        for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(i).Replace('\\', '/');
            if (string.Equals(path, expectedPath, System.StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private static string NormalizeScenePath(string scenePath)
    {
        if (string.IsNullOrWhiteSpace(scenePath))
            return string.Empty;

        string normalized = scenePath.Replace('\\', '/').Trim();
        return normalized.EndsWith(".unity", System.StringComparison.OrdinalIgnoreCase)
            ? normalized
            : normalized + ".unity";
    }
}
