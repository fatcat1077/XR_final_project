using UnityEngine;

public static class QuestRuntimeBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ConfigureRuntime()
    {
        Application.runInBackground = true;
        Screen.sleepTimeout = SleepTimeout.NeverSleep;

        RequestMicrophonePermissionIfNeeded();
        WarnIfSttServerUsesLoopback();
    }

    private static void RequestMicrophonePermissionIfNeeded()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
        {
            UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone);
            Debug.Log("[QuestRuntimeBootstrap] Requested Android microphone permission.");
        }
#endif
    }

    private static void WarnIfSttServerUsesLoopback()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        string serverUrl = RuntimeNetworkSettings.GetSttServerUrl(string.Empty);

        if (RuntimeNetworkSettings.IsLoopbackUrl(serverUrl))
        {
            Debug.LogWarning("[QuestRuntimeBootstrap] STT server URL uses localhost/127.x. On Quest this points to the headset, not the PC. Set XR_STT_SERVER_URL to your PC LAN IP.");
        }
#endif
    }
}
