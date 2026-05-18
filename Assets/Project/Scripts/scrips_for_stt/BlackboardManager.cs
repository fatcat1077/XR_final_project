using Fusion;
using TMPro;
using UnityEngine;

public class BlackboardManager : NetworkBehaviour
{
    [Header("Blackboard UI")]
    [SerializeField] private TMP_Text subtitleText;

    [Header("Session State")]
    [SerializeField] private ClassroomSessionState sessionState;

    private bool subscribed;
    private string lastAppliedText = string.Empty;

    public override void Spawned()
    {
        BindSessionState();
        ApplyText(GetCurrentText());
    }

    private void Start()
    {
        BindSessionState();
        ApplyText(GetCurrentText());
    }

    private void Update()
    {
        if (!subscribed)
            BindSessionState();
    }

    private void OnDestroy()
    {
        if (sessionState != null)
            sessionState.BlackboardChanged -= HandleBlackboardChanged;
    }

    public void SetText(string newText)
    {
        BindSessionState();

        if (sessionState != null)
        {
            sessionState.RequestSetSpeechToTextCaption(newText);
            Debug.Log($"[BlackboardManager] RequestSetSpeechToTextCaption = {newText}");
            return;
        }

        ApplyText(NormalizeFallbackText(newText));
        Debug.LogWarning("[BlackboardManager] ClassroomSessionState not found. Applied text locally only.");
    }

    public void SetManualText(string newText)
    {
        BindSessionState();

        if (sessionState != null)
        {
            sessionState.RequestSetBlackboardText(newText);
            return;
        }

        ApplyText(NormalizeFallbackText(newText));
    }

    public void ClearText()
    {
        BindSessionState();

        if (sessionState != null)
        {
            sessionState.RequestClearBlackboard();
            return;
        }

        ApplyText(string.Empty);
    }

    private void BindSessionState()
    {
        if (sessionState == null)
            sessionState = GetComponent<ClassroomSessionState>();

        if (sessionState == null)
            sessionState = ClassroomSessionState.FindInScene();

        if (sessionState == null || subscribed)
            return;

        sessionState.BlackboardChanged += HandleBlackboardChanged;
        subscribed = true;
        ApplyText(sessionState.BlackboardTextValue);

        Debug.Log("[BlackboardManager] Bound to ClassroomSessionState.");
    }

    private void HandleBlackboardChanged(string text, ClassroomSubtitleSource source, int revision)
    {
        ApplyText(text);
        Debug.Log($"[BlackboardManager] Blackboard changed. source={source}, revision={revision}");
    }

    private string GetCurrentText()
    {
        return sessionState != null ? sessionState.BlackboardTextValue : lastAppliedText;
    }

    private void ApplyText(string text)
    {
        string normalizedText = NormalizeFallbackText(text);

        if (lastAppliedText == normalizedText)
            return;

        lastAppliedText = normalizedText;

        if (subtitleText != null)
        {
            subtitleText.text = normalizedText;
            Debug.Log($"[BlackboardManager] ApplyText -> {subtitleText.text}");
        }
        else
        {
            Debug.LogWarning("[BlackboardManager] subtitleText is not assigned.");
        }
    }

    private static string NormalizeFallbackText(string text)
    {
        return string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim();
    }
}
