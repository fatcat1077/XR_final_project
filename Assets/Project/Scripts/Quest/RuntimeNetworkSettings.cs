using UnityEngine;

public static class RuntimeNetworkSettings
{
    public const string SttServerUrlPlayerPrefsKey = "XR_STT_SERVER_URL";
    public const int LegacyDefaultSttServerPort = 5000;
    public const int DefaultSttServerPort = 5055;
    public const string DefaultEditorSttServerUrl = "http://127.0.0.1:5055/stt";
    public const string DefaultQuestSttServerUrl = "http://192.168.0.100:5055/stt";

    public static string GetSttServerUrl(string serializedUrl)
    {
        string savedUrl = PlayerPrefs.GetString(SttServerUrlPlayerPrefsKey, string.Empty);

        if (!string.IsNullOrWhiteSpace(savedUrl))
        {
            string normalizedSavedUrl = NormalizeSttServerUrl(savedUrl);
            normalizedSavedUrl = MigrateLegacySttServerPort(normalizedSavedUrl, persist: true);
#if UNITY_ANDROID && !UNITY_EDITOR
            if (IsLoopbackUrl(normalizedSavedUrl))
                return NormalizeSttServerUrl(DefaultQuestSttServerUrl);
#endif
            return normalizedSavedUrl;
        }

        if (!string.IsNullOrWhiteSpace(serializedUrl))
        {
            string normalizedSerializedUrl = NormalizeSttServerUrl(serializedUrl);
            normalizedSerializedUrl = MigrateLegacySttServerPort(normalizedSerializedUrl, persist: false);
#if UNITY_ANDROID && !UNITY_EDITOR
            if (IsLoopbackUrl(normalizedSerializedUrl))
                return NormalizeSttServerUrl(DefaultQuestSttServerUrl);
#endif
            return normalizedSerializedUrl;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        return NormalizeSttServerUrl(DefaultQuestSttServerUrl);
#else
        return DefaultEditorSttServerUrl;
#endif
    }

    public static void SaveSttServerUrl(string url)
    {
        string normalizedUrl = NormalizeSttServerUrl(url);
        normalizedUrl = MigrateLegacySttServerPort(normalizedUrl, persist: false);
        PlayerPrefs.SetString(SttServerUrlPlayerPrefsKey, normalizedUrl);
        PlayerPrefs.Save();
        Debug.Log($"[RuntimeNetworkSettings] Saved STT server URL: {normalizedUrl}");
    }

    public static string NormalizeSttServerUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return DefaultEditorSttServerUrl;

        string normalized = url.Trim();

        if (!normalized.StartsWith("http://") && !normalized.StartsWith("https://"))
            normalized = $"http://{normalized}";

        if (!normalized.EndsWith("/stt"))
            normalized = normalized.TrimEnd('/') + "/stt";

        return normalized;
    }

    public static bool IsLoopbackUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        string lower = url.ToLowerInvariant();
        return lower.Contains("://127.") || lower.Contains("://localhost") || lower.Contains("://0.0.0.0");
    }

    private static string MigrateLegacySttServerPort(string normalizedUrl, bool persist)
    {
        if (string.IsNullOrWhiteSpace(normalizedUrl) ||
            !System.Uri.TryCreate(normalizedUrl, System.UriKind.Absolute, out System.Uri uri) ||
            uri.Port != LegacyDefaultSttServerPort)
            return normalizedUrl;

        System.UriBuilder builder = new System.UriBuilder(uri)
        {
            Port = DefaultSttServerPort
        };

        string migratedUrl = NormalizeSttServerUrl(builder.Uri.ToString());
        Debug.LogWarning($"[RuntimeNetworkSettings] Migrated legacy STT server URL from {normalizedUrl} to {migratedUrl}");

        if (persist)
        {
            PlayerPrefs.SetString(SttServerUrlPlayerPrefsKey, migratedUrl);
            PlayerPrefs.Save();
        }

        return migratedUrl;
    }
}
