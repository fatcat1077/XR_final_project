using UnityEngine;

public static class QuestRuntimeBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ConfigureRuntime()
    {
        Application.runInBackground = true;
        Screen.sleepTimeout = SleepTimeout.NeverSleep;

        RequestMicrophonePermissionIfNeeded();
        ApplySttServerUrlFromIntent();
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

    private static void ApplySttServerUrlFromIntent()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        string sttUrl = ReadAndroidIntentStringExtra("xr_stt_url");
        if (string.IsNullOrWhiteSpace(sttUrl))
            sttUrl = ReadAndroidIntentStringExtra("stt_url");

        if (string.IsNullOrWhiteSpace(sttUrl))
            return;

        RuntimeNetworkSettings.SaveSttServerUrl(sttUrl);
        Debug.Log($"[QuestRuntimeBootstrap] Applied STT server URL from Android intent: {RuntimeNetworkSettings.NormalizeSttServerUrl(sttUrl)}");
#endif
    }

    private static string ReadAndroidIntentStringExtra(string key)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            using (AndroidJavaObject intent = activity.Call<AndroidJavaObject>("getIntent"))
            {
                return intent.Call<string>("getStringExtra", key);
            }
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"[QuestRuntimeBootstrap] Could not read Android intent extra '{key}': {exception.Message}");
        }
#endif

        return null;
    }

    private static void WarnIfSttServerUsesLoopback()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        string serverUrl = RuntimeNetworkSettings.GetSttServerUrl(string.Empty);
        Debug.Log($"[QuestRuntimeBootstrap] STT server URL = {serverUrl}");

        if (RuntimeNetworkSettings.IsLoopbackUrl(serverUrl))
        {
            Debug.LogWarning("[QuestRuntimeBootstrap] STT server URL uses localhost/127.x. On Quest this points to the headset, not the PC. Set XR_STT_SERVER_URL to your PC LAN IP.");
        }
#endif
    }
}
