using Fusion;
using Photon.Voice.Unity;
using UnityEngine;

public class VoiceSetup : NetworkBehaviour
{
    [Header("Voice Components")]
    [SerializeField] private Recorder recorder;
    [SerializeField] private Speaker speaker;
    [SerializeField] private AudioSource speakerAudioSource;

    public override void Spawned()
    {
        ConfigureVoice();
    }

    private void ConfigureVoice()
    {
        bool isLocalPlayer = Object != null && Object.HasInputAuthority;

        Debug.Log($"[VoiceSetup] Spawned on {gameObject.name}");
        Debug.Log($"[VoiceSetup] isLocalPlayer = {isLocalPlayer}");
        Debug.Log($"[VoiceSetup] recorder assigned = {recorder != null}");
        Debug.Log($"[VoiceSetup] speaker assigned = {speaker != null}");
        Debug.Log($"[VoiceSetup] audioSource assigned = {speakerAudioSource != null}");
        Debug.Log($"[VoiceSetup] mic device count = {Microphone.devices.Length}");

        foreach (var d in Microphone.devices)
        {
            Debug.Log($"[VoiceSetup] mic device = {d}");
        }

        if (recorder != null)
        {
            recorder.SourceType = Recorder.InputSourceType.Microphone;
            recorder.RecordWhenJoined = isLocalPlayer;
            recorder.RecordingEnabled = isLocalPlayer;
            recorder.TransmitEnabled = isLocalPlayer;
            recorder.DebugEchoMode = false;

            Debug.Log($"[VoiceSetup] recorder.RecordWhenJoined = {recorder.RecordWhenJoined}");
            Debug.Log($"[VoiceSetup] recorder.RecordingEnabled = {recorder.RecordingEnabled}");
            Debug.Log($"[VoiceSetup] recorder.TransmitEnabled = {recorder.TransmitEnabled}");
            Debug.Log($"[VoiceSetup] recorder.SourceType = {recorder.SourceType}");
        }

        if (speakerAudioSource != null)
        {
            speakerAudioSource.playOnAwake = false;
            //speakerAudioSource.mute = isLocalPlayer;
            speakerAudioSource.spatialBlend = 0f;

            Debug.Log($"[VoiceSetup] audioSource.mute = {speakerAudioSource.mute}");
            Debug.Log($"[VoiceSetup] audioSource.spatialBlend = {speakerAudioSource.spatialBlend}");
        }

        if (speaker != null)
        {
            speaker.enabled = true;
            Debug.Log("[VoiceSetup] speaker.enabled = true");
        }

        Debug.Log(isLocalPlayer
            ? "[VoiceSetup] Local player voice configured."
            : "[VoiceSetup] Remote player voice configured.");
    }

    public void SetMute(bool mute)
    {
        if (Object == null || !Object.HasInputAuthority || recorder == null)
            return;

        recorder.TransmitEnabled = !mute;
        Debug.Log($"[VoiceSetup] SetMute({mute}) -> recorder.TransmitEnabled = {!mute}");
    }
}