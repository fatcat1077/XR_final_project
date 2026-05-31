using UnityEngine;
using Oculus.Voice;

public class VoiceController : MonoBehaviour
{
    public AppVoiceExperience appVoiceExperience;

    void Start()
    {
        StartContinuousListening();
    }
    public void StartContinuousListening()
    {
        appVoiceExperience.Activate(); 
    }

    public void ReactivateListening()
    {
        Invoke("DoActivate", 0.1f);
    }

    private void DoActivate()
    {
        appVoiceExperience.Activate();
    }
}