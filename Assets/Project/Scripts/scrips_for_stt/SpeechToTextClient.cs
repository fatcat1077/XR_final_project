using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

public class SpeechToTextClient : MonoBehaviour
{
    [Header("Server Settings")]
    [SerializeField] private string serverUrl = "http://127.0.0.1:5055/stt";

    [Header("References")]
    [SerializeField] private BlackboardManager blackboardManager;
    [SerializeField] private TMP_Text statusText;

    private void Awake()
    {
        EnsureRuntimeReferences();
    }

    public void Configure(BlackboardManager manager, TMP_Text status)
    {
        if (manager != null)
            blackboardManager = manager;

        if (status != null)
            statusText = status;

        EnsureRuntimeReferences();
    }

    public void EnsureRuntimeReferences()
    {
        if (blackboardManager == null)
            blackboardManager = FindObjectOfType<BlackboardManager>(true);

        if (statusText == null)
            statusText = FindTextObject("StatusText");
    }

    public void SendWavToServer(byte[] wavData)
    {
        EnsureRuntimeReferences();

        if (wavData == null || wavData.Length == 0)
        {
            Debug.LogError("[SpeechToTextClient] Cannot send empty WAV data.");
            SetStatus("No audio to send");
            return;
        }

        Debug.Log($"[SpeechToTextClient] Queueing WAV upload. bytes={wavData.Length}");
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
            Debug.LogWarning("[SpeechToTextClient] STT server URL uses localhost/127.x on Quest. Set it to the PC LAN IP, for example http://192.168.1.23:5055/stt.");
        }
#endif

        using UnityWebRequest request = UnityWebRequest.Post(resolvedServerUrl, form);
        request.timeout = 25;

        SetStatus("Sending audio to STT server...");
        Debug.Log($"[SpeechToTextClient] POST -> {resolvedServerUrl}");

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[SpeechToTextClient] Request failed: result={request.result}, code={request.responseCode}, error={request.error}, body={request.downloadHandler?.text}");
            SetStatus($"STT connection failed: {DescribeRequestError(request)}");
            yield break;
        }

        string json = request.downloadHandler.text;
        Debug.Log($"[SpeechToTextClient] Server response = {json}");

        string recognizedText = ExtractRecognizedText(json);

        if (string.IsNullOrWhiteSpace(recognizedText))
        {
            Debug.LogWarning("[SpeechToTextClient] Empty transcription result.");
            SetStatus("No text recognized");
            yield break;
        }

        PublishRecognizedText(recognizedText);
        SetStatus("STT connected: caption updated");
    }

    private static string DescribeRequestError(UnityWebRequest request)
    {
        if (request == null)
            return "unknown error";

        if (!string.IsNullOrWhiteSpace(request.error))
            return request.error;

        if (request.responseCode > 0)
            return $"HTTP {request.responseCode}";

        return request.result.ToString();
    }

    private static string ExtractRecognizedText(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return string.Empty;

        try
        {
            SpeechToTextResponse response = JsonUtility.FromJson<SpeechToTextResponse>(json);
            if (response == null)
                return string.Empty;

            if (!string.IsNullOrWhiteSpace(response.text))
                return response.text;

            if (!string.IsNullOrWhiteSpace(response.transcript))
                return response.transcript;

            if (!string.IsNullOrWhiteSpace(response.result))
                return response.result;
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"[SpeechToTextClient] Could not parse STT response JSON: {exception.Message}");
        }

        return string.Empty;
    }

    private void PublishRecognizedText(string recognizedText)
    {
        string text = string.IsNullOrWhiteSpace(recognizedText)
            ? string.Empty
            : recognizedText.Trim();

        bool sentToClassroomState = false;
        ClassroomSessionState sessionState = ClassroomSessionState.Instance != null
            ? ClassroomSessionState.Instance
            : FindObjectOfType<ClassroomSessionState>();

        if (sessionState != null && sessionState.Object != null && sessionState.Object.IsValid)
        {
            sessionState.RequestSetBlackboardText(text);
            sentToClassroomState = true;
        }

        SetLocalSubtitleText(text);

        if (blackboardManager != null && blackboardManager.isActiveAndEnabled && blackboardManager.gameObject.activeInHierarchy)
            blackboardManager.SetText(text);

        Debug.Log($"[SpeechToTextClient] Published recognized text. classroomState={sentToClassroomState}, text={text}");
    }

    private static void SetLocalSubtitleText(string text)
    {
        TMP_Text[] textObjects = FindObjectsOfType<TMP_Text>(true);
        for (int i = 0; i < textObjects.Length; i++)
        {
            TMP_Text textObject = textObjects[i];
            if (textObject != null && textObject.name == "SubtitleText")
                textObject.text = text ?? string.Empty;
        }
    }

    private static TMP_Text FindTextObject(string objectName)
    {
        TMP_Text[] textObjects = FindObjectsOfType<TMP_Text>(true);
        for (int i = 0; i < textObjects.Length; i++)
        {
            TMP_Text textObject = textObjects[i];
            if (textObject != null && textObject.name == objectName)
                return textObject;
        }

        return null;
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;

        QuestCaptionStatusReporter.SetStatus(message);
        Debug.Log($"[SpeechToTextClient] Status = {message}");
    }

    [System.Serializable]
    private class SpeechToTextResponse
    {
        public string text;
        public string transcript;
        public string result;
    }
}
