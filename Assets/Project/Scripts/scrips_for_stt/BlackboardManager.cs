using Fusion;
using TMPro;
using UnityEngine;

public class BlackboardManager : NetworkBehaviour
{
    [Header("Blackboard UI")]
    [SerializeField] private TMP_Text subtitleText;

    [Networked, OnChangedRender(nameof(OnSyncedTextChanged))]
    public NetworkString<_512> SyncedText { get; set; }

    public override void Spawned()
    {
        ApplyText();
    }

    public void SetText(string newText)
    {
        if (!HasStateAuthority)
        {
            Debug.LogWarning("[BlackboardManager] No StateAuthority. Cannot set text.");
            return;
        }

        if (string.IsNullOrWhiteSpace(newText))
            newText = "(No speech recognized)";

        SyncedText = newText;
        ApplyText();

        Debug.Log($"[BlackboardManager] SetText = {newText}");
    }

    private void OnSyncedTextChanged()
    {
        ApplyText();
    }

    private void ApplyText()
    {
        if (subtitleText != null)
        {
            subtitleText.text = SyncedText.ToString();
            Debug.Log($"[BlackboardManager] ApplyText -> {subtitleText.text}");
        }
        else
        {
            Debug.LogWarning("[BlackboardManager] subtitleText is not assigned.");
        }
    }
}