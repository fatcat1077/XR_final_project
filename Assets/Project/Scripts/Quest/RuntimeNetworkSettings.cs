using UnityEngine;

public static class RuntimeNetworkSettings
{
    public const string SttServerUrlPlayerPrefsKey = "XR_STT_SERVER_URL";
    public const string DefaultEditorSttServerUrl = "http://127.0.0.1:5000/stt";

    public static string GetSttServerUrl(string serializedUrl)
    {
        string savedUrl = PlayerPrefs.GetString(SttServerUrlPlayerPrefsKey, string.Empty);

        if (!string.IsNullOrWhiteSpace(savedUrl))
            return NormalizeSttServerUrl(savedUrl);

        if (!string.IsNullOrWhiteSpace(serializedUrl))
            return NormalizeSttServerUrl(serializedUrl);

        return DefaultEditorSttServerUrl;
    }

    public static void SaveSttServerUrl(string url)
    {
        string normalizedUrl = NormalizeSttServerUrl(url);
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
}
