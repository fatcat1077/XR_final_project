#if UNITY_STANDALONE_WIN
#define ENABLE_WINDOWS_DICTATION
#endif

using TMPro;
using UnityEngine;

#if ENABLE_WINDOWS_DICTATION
using UnityEngine.Windows.Speech;
#endif

public class SpeechTest : MonoBehaviour
{
    [SerializeField] private TMP_Text subtitleText;

#if ENABLE_WINDOWS_DICTATION
    private DictationRecognizer dictationRecognizer;
#endif

    private void Start()
    {
#if ENABLE_WINDOWS_DICTATION
        StartWindowsDictation();
#else
        if (subtitleText != null)
            subtitleText.text = string.Empty;

        Debug.Log("[SpeechTest] Windows DictationRecognizer is unavailable on this build target; speech test disabled.");
#endif
    }

#if ENABLE_WINDOWS_DICTATION
    private void StartWindowsDictation()
    {
        dictationRecognizer = new DictationRecognizer();
        dictationRecognizer.DictationHypothesis += OnDictationHypothesis;
        dictationRecognizer.DictationResult += OnDictationResult;
        dictationRecognizer.DictationComplete += OnDictationComplete;
        dictationRecognizer.DictationError += OnDictationError;
        dictationRecognizer.Start();

        if (subtitleText != null)
            subtitleText.text = "Listening...";
    }

    private void OnDictationHypothesis(string text)
    {
        if (subtitleText != null)
            subtitleText.text = text + "...";
    }

    private void OnDictationResult(string text, ConfidenceLevel confidence)
    {
        if (subtitleText != null)
            subtitleText.text = text;
    }

    private void OnDictationComplete(DictationCompletionCause cause)
    {
        if (cause == DictationCompletionCause.Complete && dictationRecognizer != null)
            dictationRecognizer.Start();
    }

    private void OnDictationError(string error, int hresult)
    {
        Debug.LogError($"[SpeechTest] Dictation error: {error} (HResult: {hresult})");
    }
#endif

    private void OnDestroy()
    {
#if ENABLE_WINDOWS_DICTATION
        if (dictationRecognizer == null)
            return;

        dictationRecognizer.DictationHypothesis -= OnDictationHypothesis;
        dictationRecognizer.DictationResult -= OnDictationResult;
        dictationRecognizer.DictationComplete -= OnDictationComplete;
        dictationRecognizer.DictationError -= OnDictationError;

        if (dictationRecognizer.Status == SpeechSystemStatus.Running)
            dictationRecognizer.Stop();

        dictationRecognizer.Dispose();
        dictationRecognizer = null;
#endif
    }
}
