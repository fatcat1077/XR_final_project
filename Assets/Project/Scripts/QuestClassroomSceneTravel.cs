using UnityEngine;

public static class QuestClassroomSceneTravel
{
    public const string TestClassroomScenePath = "Assets/Scenes/Test_classroom.unity";
    public const string OceanScenePath = "Assets/Scenes/Ocean.unity";
    public const string SpaceScenePath = "Assets/Scenes/Space.unity";

    public static bool RequestEnvironmentScene(ClassroomEnvironment environment, string reason)
    {
        return RequestEnvironmentState(environment, reason);
    }

    public static bool RequestClassroomScene(string reason)
    {
        return RequestEnvironmentState(ClassroomEnvironment.Default, reason);
    }

    public static bool RequestSceneByPath(string scenePath, string reason)
    {
        if (string.IsNullOrWhiteSpace(scenePath))
            return RequestEnvironmentState(ClassroomEnvironment.Default, reason);

        string normalizedPath = NormalizeScenePath(scenePath);
        if (IsScenePath(normalizedPath, OceanScenePath))
            return RequestEnvironmentState(ClassroomEnvironment.Ocean, reason);

        if (IsScenePath(normalizedPath, SpaceScenePath))
            return RequestEnvironmentState(ClassroomEnvironment.Space, reason);

        if (IsScenePath(normalizedPath, TestClassroomScenePath))
            return RequestEnvironmentState(ClassroomEnvironment.Default, reason);

        Debug.LogWarning($"[QuestClassroomSceneTravel] Unknown environment scene path '{scenePath}'. Keeping current Unity scene loaded. Reason: {reason}");
        return false;
    }

    private static bool RequestEnvironmentState(ClassroomEnvironment environment, string reason)
    {
        bool requestedNetworkState = false;

        if (ClassroomSessionState.TryGetActiveNetworked(out ClassroomSessionState sessionState))
        {
            sessionState.RequestSetEnvironment(environment);
            requestedNetworkState = true;
        }

        bool appliedLocal = EnvironmentContentManager.ApplyEnvironmentLocal(environment, reason);
        Debug.Log($"[QuestClassroomSceneTravel] Environment swap requested. environment={environment}, networkRequestSent={requestedNetworkState}, localApplied={appliedLocal}, reason={reason}");
        return requestedNetworkState || appliedLocal;
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

    private static bool IsScenePath(string path, string expectedPath)
    {
        return string.Equals(path, NormalizeScenePath(expectedPath), System.StringComparison.OrdinalIgnoreCase);
    }
}
