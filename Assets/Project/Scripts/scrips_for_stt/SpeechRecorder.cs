using System.Collections;
using UnityEngine;
using TMPro;

public class SpeechRecorder : MonoBehaviour
{
    [Header("Recording Settings")]
    [SerializeField] private int recordingLengthSeconds = 10;
    [SerializeField] private int sampleRate = 16000;

    [Header("References")]
    [SerializeField] private SpeechToTextClient speechToTextClient;
    [SerializeField] private TMP_Text statusText;

    private bool isRecording = false;
    private Coroutine autoStopCoroutine;
    private string statusMessage = "Ready";

    public bool IsRecording => isRecording;
    public string StatusMessage => statusMessage;

    private void Awake()
    {
        EnsureRuntimeReferences();
    }

    public void Configure(SpeechToTextClient client, TMP_Text status)
    {
        if (client != null)
            speechToTextClient = client;

        if (status != null)
            statusText = status;

        EnsureRuntimeReferences();
    }

    public void EnsureRuntimeReferences()
    {
        if (!IsUsableClient(speechToTextClient))
            speechToTextClient = FindUsableSpeechToTextClient();

        if (!IsUsableClient(speechToTextClient))
            speechToTextClient = CreateRuntimeSpeechToTextClient();

        if (speechToTextClient != null)
        {
            speechToTextClient.enabled = true;
            speechToTextClient.EnsureRuntimeReferences();
        }

        if (statusText == null)
            statusText = FindTextObject("StatusText");
    }

    private bool IsUsableClient(SpeechToTextClient client)
    {
        return client != null && client.enabled && client.gameObject.activeInHierarchy;
    }

    private SpeechToTextClient FindUsableSpeechToTextClient()
    {
        SpeechToTextClient localClient = GetComponent<SpeechToTextClient>();
        if (IsUsableClient(localClient))
            return localClient;

        SpeechToTextClient[] clients = FindObjectsOfType<SpeechToTextClient>(true);
        for (int i = 0; i < clients.Length; i++)
        {
            if (IsUsableClient(clients[i]))
                return clients[i];
        }

        return null;
    }

    private SpeechToTextClient CreateRuntimeSpeechToTextClient()
    {
        GameObject targetObject = gameObject.activeInHierarchy
            ? gameObject
            : new GameObject("QuestRuntimeSpeechToTextClient");

        SpeechToTextClient client = targetObject.GetComponent<SpeechToTextClient>();
        if (client == null)
            client = targetObject.AddComponent<SpeechToTextClient>();

        client.enabled = true;
        Debug.Log($"[SpeechRecorder] Using runtime SpeechToTextClient on '{targetObject.name}'.");
        return client;
    }

    private void Start()
    {
        EnsureRuntimeReferences();

        if (Microphone.devices.Length > 0)
        {
            Debug.Log($"[SpeechRecorder] Shared microphone available: {Microphone.devices[0]}");
        }
        else
        {
            Debug.LogError("[SpeechRecorder] No microphone found.");
            SetStatus("No microphone found");
        }
    }

    public void StartRecording()
    {
        TryStartRecording();
    }

    public bool TryStartRecording()
    {
        EnsureRuntimeReferences();

        if (LocalUserProfile.Role == UserRole.Student)
        {
            Debug.LogWarning("[SpeechRecorder] Only Teacher can record speech.");
            SetStatus("Only Teacher can record");
            return false;
        }

        if (LocalUserProfile.Role == UserRole.None)
        {
            Debug.LogWarning("[SpeechRecorder] Local role is None; allowing local STT recording for direct scene testing.");
        }

        if (isRecording)
        {
            Debug.LogWarning("[SpeechRecorder] Already recording.");
            SetStatus("Already recording");
            return true;
        }

        if (!SharedMicrophoneCapture.Instance.BeginSegment(recordingLengthSeconds, sampleRate))
        {
            Debug.LogError($"[SpeechRecorder] Shared microphone capture failed: {SharedMicrophoneCapture.Instance.Error}");
            SetStatus($"Microphone unavailable: {SharedMicrophoneCapture.Instance.Error}");
            return false;
        }

        isRecording = true;
        if (autoStopCoroutine != null)
            StopCoroutine(autoStopCoroutine);

        autoStopCoroutine = StartCoroutine(AutoStopRecordingAfterLimit());

        Debug.Log("[SpeechRecorder] Recording started.");
        SetStatus("Recording... tap Caption again to stop");
        return true;
    }

    public void StopRecording()
    {
        TryStopRecording();
    }

    public bool TryStopRecording()
    {
        if (!isRecording)
        {
            Debug.LogWarning("[SpeechRecorder] Not currently recording.");
            SetStatus("Not recording");
            return false;
        }

        SharedMicrophoneSegment segment = SharedMicrophoneCapture.Instance.EndSegment();
        isRecording = false;
        if (autoStopCoroutine != null)
        {
            StopCoroutine(autoStopCoroutine);
            autoStopCoroutine = null;
        }

        if (!segment.HasSamples)
        {
            Debug.LogError("[SpeechRecorder] No shared microphone samples were captured.");
            SetStatus("Recording failed");
            return false;
        }

        byte[] wavData = WavUtility.FromSamples(segment.Samples, segment.SampleRate, segment.Channels);

        Debug.Log($"[SpeechRecorder] Recording stopped. duration={segment.DurationSeconds:0.00}s, sampleRate={segment.SampleRate}, channels={segment.Channels}, WAV bytes={wavData.Length}");

        if (segment.ReachedLimit)
            Debug.LogWarning("[SpeechRecorder] Recording reached the configured maximum length.");

        SetStatus("Uploading audio...");

        if (speechToTextClient != null)
        {
            speechToTextClient.SendWavToServer(wavData);
            return true;
        }

        Debug.LogError("[SpeechRecorder] speechToTextClient is not assigned.");
        SetStatus("Missing SpeechToTextClient");
        return false;
    }

    public void ToggleRecording()
    {
        TryToggleRecording(out _);
    }

    public bool TryToggleRecording(out bool isNowRecording)
    {
        bool success = isRecording ? TryStopRecording() : TryStartRecording();
        isNowRecording = isRecording;
        return success;
    }

    private void OnDisable()
    {
        if (!isRecording)
            return;

        SharedMicrophoneCapture.Instance.EndSegment();
        isRecording = false;
        if (autoStopCoroutine != null)
        {
            StopCoroutine(autoStopCoroutine);
            autoStopCoroutine = null;
        }
    }

    private IEnumerator AutoStopRecordingAfterLimit()
    {
        yield return new WaitForSeconds(Mathf.Max(1, recordingLengthSeconds) + 0.1f);

        if (!isRecording)
            yield break;

        Debug.Log("[SpeechRecorder] Auto-stopping recording at configured limit.");
        autoStopCoroutine = null;
        StopRecording();
    }

    private void SetStatus(string message)
    {
        statusMessage = string.IsNullOrWhiteSpace(message) ? string.Empty : message;

        if (statusText != null)
            statusText.text = statusMessage;

        QuestCaptionStatusReporter.SetStatus(statusMessage);
        Debug.Log($"[SpeechRecorder] Status = {statusMessage}");
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
}
