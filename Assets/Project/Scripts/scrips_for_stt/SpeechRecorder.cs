using System;
using System.IO;
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

    private AudioClip recordedClip;
    private string microphoneDevice;
    private bool isRecording = false;

    private void Start()
    {
        if (Microphone.devices.Length > 0)
        {
            microphoneDevice = Microphone.devices[0];
            Debug.Log($"[SpeechRecorder] Using microphone: {microphoneDevice}");
        }
        else
        {
            Debug.LogError("[SpeechRecorder] No microphone found.");
            SetStatus("No microphone found");
        }
    }

    public void StartRecording()
    {
        if (LocalUserProfile.Role != UserRole.Teacher)
        {
            Debug.LogWarning("[SpeechRecorder] Only Teacher can record speech.");
            SetStatus("Only Teacher can record");
            return;
        }

        if (isRecording)
        {
            Debug.LogWarning("[SpeechRecorder] Already recording.");
            return;
        }

        if (string.IsNullOrEmpty(microphoneDevice))
        {
            Debug.LogError("[SpeechRecorder] No microphone device available.");
            SetStatus("No microphone device");
            return;
        }

        recordedClip = Microphone.Start(microphoneDevice, false, recordingLengthSeconds, sampleRate);
        isRecording = true;

        Debug.Log("[SpeechRecorder] Recording started.");
        SetStatus("Recording...");
    }

    public void StopRecording()
    {
        if (!isRecording)
        {
            Debug.LogWarning("[SpeechRecorder] Not currently recording.");
            return;
        }

        int position = Microphone.GetPosition(microphoneDevice);
        Microphone.End(microphoneDevice);
        isRecording = false;

        if (position <= 0)
        {
            Debug.LogError("[SpeechRecorder] Recording position invalid.");
            SetStatus("Recording failed");
            return;
        }

        float[] samples = new float[position * recordedClip.channels];
        recordedClip.GetData(samples, 0);

        AudioClip trimmedClip = AudioClip.Create(
            "TrimmedRecording",
            position,
            recordedClip.channels,
            recordedClip.frequency,
            false
        );
        trimmedClip.SetData(samples, 0);

        byte[] wavData = WavUtility.FromAudioClip(trimmedClip);

        Debug.Log($"[SpeechRecorder] Recording stopped. WAV bytes = {wavData.Length}");
        SetStatus("Uploading audio...");

        if (speechToTextClient != null)
        {
            speechToTextClient.SendWavToServer(wavData);
        }
        else
        {
            Debug.LogError("[SpeechRecorder] speechToTextClient is not assigned.");
            SetStatus("Missing SpeechToTextClient");
        }
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;

        Debug.Log($"[SpeechRecorder] Status = {message}");
    }
}