using System;
using System.Collections;
using Photon.Realtime;
using Photon.Voice;
using Photon.Voice.Fusion;
using Fusion;
using Photon.Voice.Unity;
using UnityEngine;

public class VoiceSetup : NetworkBehaviour
{
    private const float MicrophoneWarningIntervalSeconds = 5f;

    [Header("Voice Components")]
    [SerializeField] private Recorder recorder;
    [SerializeField] private Speaker speaker;
    [SerializeField] private AudioSource speakerAudioSource;

    private Coroutine enableRecorderCoroutine;
    private Coroutine voiceRecoveryCoroutine;
    private FusionVoiceClient fusionVoiceClient;
    private VoiceNetworkObject voiceNetworkObject;
    private Func<IAudioDesc> sharedMicrophoneInputFactory;
    private object recorderUserData;
    private bool recorderRegistrationRetried;
    private float lastMicrophoneWarningTime = -999f;
    private string lastMicrophoneWarning;

    private void Awake()
    {
        if (recorder == null)
            return;

        ConfigureRecorderSharedMicrophoneInput();
        recorder.DebugEchoMode = false;
        recorder.RecordWhenJoined = false;
        recorder.RecordingEnabled = false;
        recorder.TransmitEnabled = false;
    }

    public override void Spawned()
    {
        ConfigureVoice();
        StartVoiceRecovery("Spawned");
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        StopVoiceRecovery();
    }

    private void ConfigureVoice()
    {
        bool isLocalPlayer = Object != null && Object.HasInputAuthority;
        ResolveVoiceRuntimeReferences();

        Debug.Log($"[VoiceSetup] Spawned on {gameObject.name}");
        Debug.Log($"[VoiceSetup] isLocalPlayer = {isLocalPlayer}");
        Debug.Log($"[VoiceSetup] recorder assigned = {recorder != null}");
        Debug.Log($"[VoiceSetup] speaker assigned = {speaker != null}");
        Debug.Log($"[VoiceSetup] audioSource assigned = {speakerAudioSource != null}");
        Debug.Log($"[VoiceSetup] fusionVoiceClient assigned = {fusionVoiceClient != null}");
        Debug.Log($"[VoiceSetup] voiceNetworkObject assigned = {voiceNetworkObject != null}");
        Debug.Log($"[VoiceSetup] mic device count = {Microphone.devices.Length}");

        foreach (var d in Microphone.devices)
        {
            Debug.Log($"[VoiceSetup] mic device = {d}");
        }

        if (recorder != null)
        {
            recorder.RecordWhenJoined = isLocalPlayer;
            recorder.TransmitEnabled = isLocalPlayer;
            recorder.DebugEchoMode = false;

            if (isLocalPlayer)
            {
                SetRecorderUserData(GetRecorderUserData());
                ConfigureRecorderSharedMicrophoneInput();
                recorder.RecordingEnabled = false;
                RestartEnableRecorderWhenMicrophoneReady();
            }
            else
            {
                recorder.RecordingEnabled = false;
            }

            Debug.Log($"[VoiceSetup] recorder.RecordWhenJoined = {recorder.RecordWhenJoined}");
            Debug.Log($"[VoiceSetup] recorder.RecordingEnabled = {recorder.RecordingEnabled}");
            Debug.Log($"[VoiceSetup] recorder.TransmitEnabled = {recorder.TransmitEnabled}");
            Debug.Log($"[VoiceSetup] recorder.SourceType = {recorder.SourceType}");
            Debug.Log($"[VoiceSetup] recorder uses shared microphone = {isLocalPlayer}");
        }

        if (speakerAudioSource != null)
        {
            speakerAudioSource.playOnAwake = false;
            speakerAudioSource.mute = isLocalPlayer;
            speakerAudioSource.spatialBlend = 0f;

            Debug.Log($"[VoiceSetup] audioSource.mute = {speakerAudioSource.mute}");
            Debug.Log($"[VoiceSetup] audioSource.spatialBlend = {speakerAudioSource.spatialBlend}");
        }

        if (speaker != null)
        {
            speaker.enabled = !isLocalPlayer;
            Debug.Log($"[VoiceSetup] speaker.enabled = {speaker.enabled}");
        }

        Debug.Log(isLocalPlayer
            ? "[VoiceSetup] Local player voice configured."
            : "[VoiceSetup] Remote player voice configured.");
    }

    private void ResolveVoiceRuntimeReferences()
    {
        if (fusionVoiceClient == null)
        {
            fusionVoiceClient = Runner != null
                ? Runner.GetComponent<FusionVoiceClient>()
                : FindObjectOfType<FusionVoiceClient>(true);
        }

        if (voiceNetworkObject == null)
            voiceNetworkObject = GetComponent<VoiceNetworkObject>();
    }

    private object GetRecorderUserData()
    {
        return Object != null ? Object.Id : null;
    }

    private int GetRequestedSamplingRate()
    {
        return recorder != null
            ? Mathf.Max(8000, (int)recorder.SamplingRate)
            : 24000;
    }

    private void ConfigureRecorderSharedMicrophoneInput()
    {
        if (recorder == null)
            return;

        sharedMicrophoneInputFactory ??= () => SharedMicrophoneCapture.Instance.CreatePhotonReader(GetRequestedSamplingRate());

        if (recorder.InputFactory != sharedMicrophoneInputFactory)
            recorder.InputFactory = sharedMicrophoneInputFactory;

        if (recorder.SourceType != Recorder.InputSourceType.Factory)
            recorder.SourceType = Recorder.InputSourceType.Factory;
    }

    private void SetRecorderUserData(object userData)
    {
        if (recorder == null)
            return;

        if (Equals(recorderUserData, userData) && Equals(recorder.UserData, userData))
            return;

        recorderUserData = userData;
        recorder.UserData = userData;
    }

    private void StartVoiceRecovery(string reason)
    {
        StopVoiceRecovery();
        recorderRegistrationRetried = false;
        voiceRecoveryCoroutine = StartCoroutine(VoiceRecoveryRoutine(reason));
    }

    private void StopVoiceRecovery()
    {
        if (voiceRecoveryCoroutine != null)
        {
            StopCoroutine(voiceRecoveryCoroutine);
            voiceRecoveryCoroutine = null;
        }

        if (enableRecorderCoroutine != null)
        {
            StopCoroutine(enableRecorderCoroutine);
            enableRecorderCoroutine = null;
        }
    }

    private IEnumerator VoiceRecoveryRoutine(string reason)
    {
        Debug.Log($"[VoiceSetup] Voice recovery started. reason={reason}, object={gameObject.name}");

        while (isActiveAndEnabled && Object != null)
        {
            ResolveVoiceRuntimeReferences();

            bool isLocalPlayer = Object.HasInputAuthority;
            if (isLocalPlayer)
                MaintainLocalRecorder();
            else
                MaintainRemoteSpeaker();

            yield return new WaitForSeconds(1f);
        }
    }

    private void MaintainLocalRecorder()
    {
        if (recorder == null)
            return;

        SetRecorderUserData(GetRecorderUserData());
        recorder.RecordWhenJoined = true;
        recorder.TransmitEnabled = true;
        recorder.DebugEchoMode = false;
        ConfigureRecorderSharedMicrophoneInput();

        int requestedFrequency = GetRequestedSamplingRate();
        if (!SharedMicrophoneCapture.Instance.EnsureStarted(requestedFrequency))
        {
            LogMicrophoneWaitWarning($"Local voice waiting for shared microphone after scene change. error={SharedMicrophoneCapture.Instance.Error}");
            recorder.RecordingEnabled = false;
            return;
        }

        if (voiceNetworkObject != null && voiceNetworkObject.RecorderInUse != recorder && fusionVoiceClient != null && !recorderRegistrationRetried)
        {
            bool registered = fusionVoiceClient.AddRecorder(recorder);
            recorderRegistrationRetried = true;
            Debug.Log($"[VoiceSetup] Retried recorder registration after scene change. registered={registered}, voiceState={fusionVoiceClient.ClientState}");
        }

        if (fusionVoiceClient != null && fusionVoiceClient.ClientState == ClientState.Joined)
        {
            if (!recorder.RecordingEnabled)
            {
                recorder.RecordingEnabled = true;
                Debug.Log("[VoiceSetup] Re-enabled Photon Voice recorder after scene change.");
            }

            if (!recorder.IsCurrentlyTransmitting)
                recorder.RestartRecording();
        }
    }

    private void MaintainRemoteSpeaker()
    {
        if (speakerAudioSource != null)
        {
            speakerAudioSource.playOnAwake = false;
            speakerAudioSource.mute = false;
            speakerAudioSource.spatialBlend = 0f;
        }

        if (speaker == null)
            return;

        speaker.enabled = true;

        if (speaker.IsLinked || fusionVoiceClient == null || fusionVoiceClient.ClientState != ClientState.Joined || Object == null)
            return;

        bool linked = fusionVoiceClient.AddSpeaker(speaker, Object.Id);
        if (linked)
            Debug.Log($"[VoiceSetup] Re-linked remote Photon Voice speaker after scene change. objectId={Object.Id}");
    }

    private void RestartEnableRecorderWhenMicrophoneReady()
    {
        if (enableRecorderCoroutine != null)
            StopCoroutine(enableRecorderCoroutine);

        enableRecorderCoroutine = StartCoroutine(EnableRecorderWhenMicrophoneReady());
    }

    private IEnumerator EnableRecorderWhenMicrophoneReady()
    {
        const int maxAttempts = 40;
        const float retryDelaySeconds = 0.25f;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            if (recorder == null)
                yield break;

            int requestedFrequency = GetRequestedSamplingRate();
            if (SharedMicrophoneCapture.Instance.EnsureStarted(requestedFrequency))
            {
                recorder.RecordWhenJoined = true;
                recorder.TransmitEnabled = true;
                recorder.RecordingEnabled = true;
                Debug.Log("[VoiceSetup] Shared microphone is ready; local Photon Voice recorder enabled.");
                enableRecorderCoroutine = null;
                yield break;
            }

            LogMicrophoneWaitWarning($"Waiting for shared microphone. Attempt={attempt}/{maxAttempts}, error={SharedMicrophoneCapture.Instance.Error}");
            yield return new WaitForSeconds(retryDelaySeconds);
        }

        Debug.LogError($"[VoiceSetup] Shared microphone did not become ready. Last error={SharedMicrophoneCapture.Instance.Error}");
        enableRecorderCoroutine = null;
    }

    private void LogMicrophoneWaitWarning(string message)
    {
        float now = Time.realtimeSinceStartup;
        if (string.Equals(lastMicrophoneWarning, message, StringComparison.Ordinal) &&
            now - lastMicrophoneWarningTime < MicrophoneWarningIntervalSeconds)
            return;

        lastMicrophoneWarning = message;
        lastMicrophoneWarningTime = now;
        Debug.LogWarning($"[VoiceSetup] {message}");
    }

    public void SetMute(bool mute)
    {
        if (Object == null || !Object.HasInputAuthority || recorder == null)
            return;

        recorder.TransmitEnabled = !mute;
        Debug.Log($"[VoiceSetup] SetMute({mute}) -> recorder.TransmitEnabled = {!mute}");
    }
}
