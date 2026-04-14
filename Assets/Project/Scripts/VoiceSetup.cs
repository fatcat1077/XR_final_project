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

        // 本地玩家：開錄音與傳輸
        if (recorder != null)
        {
            recorder.SourceType = Recorder.InputSourceType.Microphone;
            recorder.RecordWhenJoined = isLocalPlayer;
            recorder.RecordingEnabled = isLocalPlayer;
            recorder.TransmitEnabled = isLocalPlayer;
            recorder.DebugEchoMode = false;
        }

        // AudioSource：本地自己不播自己的聲音，遠端才播
        if (speakerAudioSource != null)
        {
            speakerAudioSource.playOnAwake = false;
            speakerAudioSource.mute = isLocalPlayer;
            speakerAudioSource.spatialBlend = 0f; // 先做 2D 音效，最穩
        }

        if (speaker != null)
        {
            speaker.enabled = true;
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
    }
}