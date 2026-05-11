using System;
using Fusion;
using UnityEngine;
using UnityEngine.Events;

public class ClassroomSessionState : NetworkBehaviour
{
    private const int BlackboardTextMaxLength = 512;

    [Serializable]
    public class ClassroomEnvironmentUnityEvent : UnityEvent<ClassroomEnvironment>
    {
    }

    [Header("State Change Events")]
    [SerializeField] private ClassroomEnvironmentUnityEvent onEnvironmentChanged = new();
    [SerializeField] private UnityEvent<bool> onStudentHandRaisedChanged = new();
    [SerializeField] private UnityEvent<string> onBlackboardTextChanged = new();

    [Networked, OnChangedRender(nameof(HandleEnvironmentChanged))]
    public ClassroomEnvironment CurrentEnvironment { get; private set; }

    [Networked, OnChangedRender(nameof(HandleStudentHandRaisedChanged))]
    public NetworkBool IsStudentHandRaised { get; private set; }

    [Networked, OnChangedRender(nameof(HandleBlackboardTextChanged))]
    public NetworkString<_512> BlackboardText { get; private set; }

    public event Action<ClassroomEnvironment> EnvironmentChanged;
    public event Action<bool> StudentHandRaisedChanged;
    public event Action<string> BlackboardTextChanged;

    public ClassroomEnvironmentUnityEvent OnEnvironmentChanged => onEnvironmentChanged;
    public UnityEvent<bool> OnStudentHandRaisedChanged => onStudentHandRaisedChanged;
    public UnityEvent<string> OnBlackboardTextChanged => onBlackboardTextChanged;

    private bool hasPublishedEnvironment;
    private bool hasPublishedStudentHandRaised;
    private bool hasPublishedBlackboardText;
    private ClassroomEnvironment lastPublishedEnvironment;
    private bool lastPublishedStudentHandRaised;
    private string lastPublishedBlackboardText = string.Empty;

    public override void Spawned()
    {
        PublishEnvironmentChanged(force: true);
        PublishStudentHandRaisedChanged(force: true);
        PublishBlackboardTextChanged(force: true);
    }

    // UI usage:
    // sessionState.RequestSetEnvironment(ClassroomEnvironment.Ocean);
    // sessionState.RequestSetStudentHandRaised(true);
    // sessionState.RequestSetBlackboardText("Welcome to class.");
    public void RequestSetEnvironment(ClassroomEnvironment environment)
    {
        if (HasStateAuthority)
        {
            SetEnvironmentState(environment);
            return;
        }

        RPC_RequestSetEnvironment(environment);
    }

    public void RequestSetStudentHandRaised(bool raised)
    {
        if (HasStateAuthority)
        {
            SetStudentHandRaisedState(raised);
            return;
        }

        RPC_RequestSetStudentHandRaised(raised);
    }

    public void RequestClearStudentHandRaised()
    {
        RequestSetStudentHandRaised(false);
    }

    public void RequestSetBlackboardText(string text)
    {
        if (HasStateAuthority)
        {
            SetBlackboardTextState(text);
            return;
        }

        RPC_RequestSetBlackboardText(text ?? string.Empty);
    }

    public void RequestClearBlackboard()
    {
        RequestSetBlackboardText(string.Empty);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestSetEnvironment(ClassroomEnvironment environment, RpcInfo info = default)
    {
        SetEnvironmentState(environment);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestSetStudentHandRaised(bool raised, RpcInfo info = default)
    {
        SetStudentHandRaisedState(raised);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestSetBlackboardText(string text, RpcInfo info = default)
    {
        SetBlackboardTextState(text);
    }

    private void SetEnvironmentState(ClassroomEnvironment environment)
    {
        CurrentEnvironment = environment;
        PublishEnvironmentChanged(force: false);
    }

    private void SetStudentHandRaisedState(bool raised)
    {
        IsStudentHandRaised = raised;
        PublishStudentHandRaisedChanged(force: false);
    }

    private void SetBlackboardTextState(string text)
    {
        BlackboardText = NormalizeBlackboardText(text);
        PublishBlackboardTextChanged(force: false);
    }

    private void HandleEnvironmentChanged()
    {
        PublishEnvironmentChanged(force: false);
    }

    private void HandleStudentHandRaisedChanged()
    {
        PublishStudentHandRaisedChanged(force: false);
    }

    private void HandleBlackboardTextChanged()
    {
        PublishBlackboardTextChanged(force: false);
    }

    private void PublishEnvironmentChanged(bool force)
    {
        if (!force && hasPublishedEnvironment && lastPublishedEnvironment == CurrentEnvironment)
            return;

        hasPublishedEnvironment = true;
        lastPublishedEnvironment = CurrentEnvironment;
        EnvironmentChanged?.Invoke(CurrentEnvironment);
        onEnvironmentChanged.Invoke(CurrentEnvironment);
    }

    private void PublishStudentHandRaisedChanged(bool force)
    {
        bool raised = IsStudentHandRaised;

        if (!force && hasPublishedStudentHandRaised && lastPublishedStudentHandRaised == raised)
            return;

        hasPublishedStudentHandRaised = true;
        lastPublishedStudentHandRaised = raised;
        StudentHandRaisedChanged?.Invoke(raised);
        onStudentHandRaisedChanged.Invoke(raised);
    }

    private void PublishBlackboardTextChanged(bool force)
    {
        string text = BlackboardText.ToString();

        if (!force && hasPublishedBlackboardText && lastPublishedBlackboardText == text)
            return;

        hasPublishedBlackboardText = true;
        lastPublishedBlackboardText = text;
        BlackboardTextChanged?.Invoke(text);
        onBlackboardTextChanged.Invoke(text);
    }

    private static string NormalizeBlackboardText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return text.Length <= BlackboardTextMaxLength
            ? text
            : text.Substring(0, BlackboardTextMaxLength);
    }
}
