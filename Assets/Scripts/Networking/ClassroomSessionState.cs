using System;
using Fusion;
using UnityEngine;
using UnityEngine.Events;

public class ClassroomSessionState : NetworkBehaviour
{
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

    public override void Spawned()
    {
        NotifyEnvironmentChanged();
        NotifyStudentHandRaisedChanged();
        NotifyBlackboardTextChanged();
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
        NotifyEnvironmentChanged();
    }

    private void SetStudentHandRaisedState(bool raised)
    {
        IsStudentHandRaised = raised;
        NotifyStudentHandRaisedChanged();
    }

    private void SetBlackboardTextState(string text)
    {
        BlackboardText = text ?? string.Empty;
        NotifyBlackboardTextChanged();
    }

    private void HandleEnvironmentChanged()
    {
        NotifyEnvironmentChanged();
    }

    private void HandleStudentHandRaisedChanged()
    {
        NotifyStudentHandRaisedChanged();
    }

    private void HandleBlackboardTextChanged()
    {
        NotifyBlackboardTextChanged();
    }

    private void NotifyEnvironmentChanged()
    {
        EnvironmentChanged?.Invoke(CurrentEnvironment);
        onEnvironmentChanged.Invoke(CurrentEnvironment);
    }

    private void NotifyStudentHandRaisedChanged()
    {
        bool raised = IsStudentHandRaised;
        StudentHandRaisedChanged?.Invoke(raised);
        onStudentHandRaisedChanged.Invoke(raised);
    }

    private void NotifyBlackboardTextChanged()
    {
        string text = BlackboardText.ToString();
        BlackboardTextChanged?.Invoke(text);
        onBlackboardTextChanged.Invoke(text);
    }
}
