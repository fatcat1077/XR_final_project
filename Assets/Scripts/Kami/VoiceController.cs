using UnityEngine;
using Oculus.Voice;

public class VoiceController : MonoBehaviour
{
    public AppVoiceExperience appVoiceExperience;
    [SerializeField] private bool activateOnStart;
    [SerializeField] private bool reactivateAfterStop;

    void Start()
    {
        if (activateOnStart)
            StartContinuousListening();
    }

    public void StartContinuousListening()
    {
        if (appVoiceExperience == null)
        {
            Debug.LogWarning("[VoiceController] AppVoiceExperience is not assigned.");
            return;
        }

        try
        {
            appVoiceExperience.Activate();
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"[VoiceController] Failed to activate voice input: {exception.Message}");
        }
    }

    public void ReactivateListening()
    {
        if (!reactivateAfterStop)
            return;

        Invoke(nameof(DoActivate), 0.1f);
    }

    private void DoActivate()
    {
        StartContinuousListening();
    }
}
