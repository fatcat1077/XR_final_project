using System.Collections;
using Fusion;
using Photon.Voice.Unity;
using UnityEngine;

public class VoiceRuntimeDebug : NetworkBehaviour
{
    [SerializeField] private Recorder recorder;
    [SerializeField] private Speaker speaker;
    [SerializeField] private AudioSource audioSource;

    public override void Spawned()
    {
        StartCoroutine(LogRoutine());
    }

    private IEnumerator LogRoutine()
    {
        while (true)
        {
            bool isLocal = Object != null && Object.HasInputAuthority;

            bool recAssigned = recorder != null;
            bool spkAssigned = speaker != null;
            bool srcAssigned = audioSource != null;

            bool recEnabled = recAssigned && recorder.RecordingEnabled;
            bool txEnabled = recAssigned && recorder.TransmitEnabled;
            bool isTx = recAssigned && recorder.IsCurrentlyTransmitting;

            bool spkLinked = spkAssigned && speaker.IsLinked;
            bool spkPlaying = spkAssigned && speaker.IsPlaying;

            bool srcMute = srcAssigned && audioSource.mute;
            bool srcPlaying = srcAssigned && audioSource.isPlaying;

            Debug.Log(
                $"[VoiceRuntimeDebug] " +
                $"name={gameObject.name} " +
                $"isLocal={isLocal} " +
                $"recEnabled={recEnabled} " +
                $"txEnabled={txEnabled} " +
                $"isTx={isTx} " +
                $"spkLinked={spkLinked} " +
                $"spkPlaying={spkPlaying} " +
                $"srcMute={srcMute} " +
                $"srcPlaying={srcPlaying}"
            );

            yield return new WaitForSeconds(1f);
        }
    }
}