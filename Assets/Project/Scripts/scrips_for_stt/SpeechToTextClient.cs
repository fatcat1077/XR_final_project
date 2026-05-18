using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

public class SpeechToTextClient : MonoBehaviour
{
    [Header("Server Settings")]
    [SerializeField] private string serverUrl = "http://127.0.0.1:5000/stt";

    [Header("References")]
    [SerializeField] private ClassroomSessionState sessionState;
    [SerializeField] private BlackboardManager blackboardManager;
    [SerializeField] private TMP_Text statusText;

    public void SendWavToServer(byte[] wavData)
    {
        StartCoroutine(PostWavCoroutine(wavData));
    }

    private IEnumerator PostWavCoroutine(byte[] wavData)
    {
        WWWForm form = new WWWForm();
        form.AddBinaryData("file", wavData, "recording.wav", "audio/wav");

        string resolvedServerUrl = RuntimeNetworkSettings.GetSttServerUrl(serverUrl);

#if UNITY_ANDROID && !UNITY_EDITOR
        if (RuntimeNetworkSettings.IsLoopbackUrl(resolvedServerUrl))
        {
            Debug.LogWarning("[SpeechToTextClient] STT server URL uses localhost/127.x on Android. Set it to your PC LAN IP, for example http://192.168.1.23:5000/stt.");
        }
#endif

        using UnityWebRequest request = UnityWebRequest.Post(resolvedServerUrl, form);

        SetStatus("Sending audio to Whisper server...");
        Debug.Log($"[SpeechToTextClient] POST -> {resolvedServerUrl}");

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[SpeechToTextClient] Request failed: {request.error}");
            SetStatus("Speech-to-text failed");
            yield break;
        }

        string json = request.downloadHandler.text;
        Debug.Log($"[SpeechToTextClient] Server response = {json}");

        SpeechToTextResponse response = JsonUtility.FromJson<SpeechToTextResponse>(json);

        if (response == null || string.IsNullOrWhiteSpace(response.text))
        {
            Debug.LogWarning("[SpeechToTextClient] Empty transcription result.");
            SetStatus("No text recognized");
            yield break;
        }

        SetStatus("Speech recognized");

        PublishRecognizedText(response.text);
    }

    public void PublishRecognizedText(string text)
    {
        if (sessionState == null)
            sessionState = ClassroomSessionState.FindInScene();

        if (sessionState != null)
        {
            sessionState.RequestSetSpeechToTextCaption(text);
            Debug.Log($"[SpeechToTextClient] Published STT text through ClassroomSessionState: {text}");
        }
        else if (blackboardManager != null)
        {
            blackboardManager.SetText(text);
        }
        else
        {
            Debug.LogError("[SpeechToTextClient] No ClassroomSessionState or BlackboardManager is assigned.");
        }
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;

        Debug.Log($"[SpeechToTextClient] Status = {message}");
    }

    [System.Serializable]
    private class SpeechToTextResponse
    {
        public string text;
    }
}
