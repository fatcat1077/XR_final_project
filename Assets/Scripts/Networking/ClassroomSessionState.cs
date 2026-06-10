using System;
using Fusion;
using UnityEngine;
using UnityEngine.Events;

public class ClassroomSessionState : NetworkBehaviour
{
    private const int BlackboardTextMaxLength = 512;
    private const float MinimumVideoTimeSeconds = 0f;

    [Serializable]
    public class ClassroomEnvironmentUnityEvent : UnityEvent<ClassroomEnvironment>
    {
    }

    [Header("State Change Events")]
    [SerializeField] private ClassroomEnvironmentUnityEvent onEnvironmentChanged = new();
    [SerializeField] private UnityEvent<bool> onStudentHandRaisedChanged = new();
    [SerializeField] private UnityEvent<string> onBlackboardTextChanged = new();
    [SerializeField] private UnityEvent<bool> onVideoPanelVisibleChanged = new();
    [SerializeField] private UnityEvent<bool> onVideoPlayingChanged = new();
    [SerializeField] private UnityEvent<float> onVideoPlaybackTimeChanged = new();
    [SerializeField] private UnityEvent<int> onVideoCommandRevisionChanged = new();

    [Networked, OnChangedRender(nameof(HandleEnvironmentChanged))]
    public ClassroomEnvironment CurrentEnvironment { get; private set; }

    [Networked, OnChangedRender(nameof(HandleStudentHandRaisedChanged))]
    public NetworkBool IsStudentHandRaised { get; private set; }

    [Networked, OnChangedRender(nameof(HandleBlackboardTextChanged))]
    public NetworkString<_512> BlackboardText { get; private set; }

    [Networked, OnChangedRender(nameof(HandleVideoStateChanged))]
    public NetworkBool IsVideoPanelVisible { get; private set; }

    [Networked, OnChangedRender(nameof(HandleVideoStateChanged))]
    public NetworkBool IsVideoPlaying { get; private set; }

    [Networked, OnChangedRender(nameof(HandleVideoStateChanged))]
    public float VideoPlaybackTimeSeconds { get; private set; }

    [Networked, OnChangedRender(nameof(HandleVideoStateChanged))]
    public int VideoCommandRevision { get; private set; }

    public event Action<ClassroomEnvironment> EnvironmentChanged;
    public event Action<bool> StudentHandRaisedChanged;
    public event Action<string> BlackboardTextChanged;
    public event Action<bool> VideoPanelVisibleChanged;
    public event Action<bool> VideoPlayingChanged;
    public event Action<float> VideoPlaybackTimeChanged;
    public event Action<int> VideoCommandReceived;

    public ClassroomEnvironmentUnityEvent OnEnvironmentChanged => onEnvironmentChanged;
    public UnityEvent<bool> OnStudentHandRaisedChanged => onStudentHandRaisedChanged;
    public UnityEvent<string> OnBlackboardTextChanged => onBlackboardTextChanged;
    public UnityEvent<bool> OnVideoPanelVisibleChanged => onVideoPanelVisibleChanged;
    public UnityEvent<bool> OnVideoPlayingChanged => onVideoPlayingChanged;
    public UnityEvent<float> OnVideoPlaybackTimeChanged => onVideoPlaybackTimeChanged;
    public UnityEvent<int> OnVideoCommandRevisionChanged => onVideoCommandRevisionChanged;

    private bool hasPublishedEnvironment;
    private bool hasPublishedStudentHandRaised;
    private bool hasPublishedBlackboardText;
    private bool hasPublishedVideoPanelVisible;
    private bool hasPublishedVideoPlaying;
    private bool hasPublishedVideoPlaybackTime;
    private bool hasPublishedVideoCommandRevision;
    private ClassroomEnvironment lastPublishedEnvironment;
    private bool lastPublishedStudentHandRaised;
    private string lastPublishedBlackboardText = string.Empty;
    private bool lastPublishedVideoPanelVisible;
    private bool lastPublishedVideoPlaying;
    private float lastPublishedVideoPlaybackTime;
    private int lastPublishedVideoCommandRevision;
    public static ClassroomSessionState Instance;

    private void Awake()
    {
        if (Instance == null || !IsUsableInstance(Instance))
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public override void Spawned()
    {
        Instance = this;
        PublishEnvironmentChanged(force: true);
        PublishStudentHandRaisedChanged(force: true);
        PublishBlackboardTextChanged(force: true);
        PublishVideoStateChanged(force: true);
        Debug.Log($"[ClassroomSessionState] Spawned. stateAuthority={HasStateAuthority}");
    }

    // UI usage:
    // sessionState.RequestSetEnvironment(ClassroomEnvironment.Ocean);
    // sessionState.RequestSetStudentHandRaised(true);
    // sessionState.RequestSetBlackboardText("Welcome to class.");
    // sessionState.RequestSetVideoPanelVisible(true);
    // sessionState.RequestSetVideoPlaying(true);
    // sessionState.RequestSetVideoPlaybackTime(30f);
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
            Debug.Log($"[ClassroomSessionState] Applying local student hand request. raised={raised}");
            SetStudentHandRaisedState(raised);
            return;
        }

        Debug.Log($"[ClassroomSessionState] Sending student hand request to state authority. raised={raised}");
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

    public void RequestSetVideoPanelVisible(bool visible)
    {
        if (HasStateAuthority)
        {
            SetVideoPanelVisibleState(visible);
            return;
        }

        RPC_RequestSetVideoPanelVisible(visible);
    }

    public void RequestSetVideoPlaying(bool playing)
    {
        RequestSyncVideo(playing, VideoPlaybackTimeSeconds);
    }

    public void RequestToggleVideoPlayback()
    {
        RequestSetVideoPlaying(!IsVideoPlaying);
    }

    public void RequestSetVideoPlaybackTime(float timeSeconds)
    {
        RequestSyncVideo(IsVideoPlaying, timeSeconds);
    }

    public void RequestSyncVideo(bool playing, float timeSeconds)
    {
        if (HasStateAuthority)
        {
            SetVideoPlaybackState(playing, timeSeconds);
            return;
        }

        RPC_RequestSyncVideo(playing, timeSeconds);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestSetEnvironment(ClassroomEnvironment environment, RpcInfo info = default)
    {
        SetEnvironmentState(environment);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestSetStudentHandRaised(bool raised, RpcInfo info = default)
    {
        Debug.Log($"[ClassroomSessionState] Received student hand RPC. raised={raised}, source={info.Source}");
        SetStudentHandRaisedState(raised);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestSetBlackboardText(string text, RpcInfo info = default)
    {
        SetBlackboardTextState(text);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestSetVideoPanelVisible(bool visible, RpcInfo info = default)
    {
        SetVideoPanelVisibleState(visible);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestSyncVideo(bool playing, float timeSeconds, RpcInfo info = default)
    {
        SetVideoPlaybackState(playing, timeSeconds);
    }

    private void SetEnvironmentState(ClassroomEnvironment environment)
    {
        CurrentEnvironment = environment;
        PublishEnvironmentChanged(force: false);
    }

    private void SetStudentHandRaisedState(bool raised)
    {
        IsStudentHandRaised = raised;
        Debug.Log($"[ClassroomSessionState] Student hand state set. raised={raised}");
        PublishStudentHandRaisedChanged(force: false);
    }

    private void SetBlackboardTextState(string text)
    {
        BlackboardText = NormalizeBlackboardText(text);
        PublishBlackboardTextChanged(force: false);
    }

    private void SetVideoPanelVisibleState(bool visible)
    {
        IsVideoPanelVisible = visible;
        IncrementVideoCommandRevision();
        PublishVideoStateChanged(force: false);
    }

    private void SetVideoPlaybackState(bool playing, float timeSeconds)
    {
        IsVideoPlaying = playing;
        VideoPlaybackTimeSeconds = NormalizeVideoTime(timeSeconds);
        IncrementVideoCommandRevision();
        PublishVideoStateChanged(force: false);
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

    private void HandleVideoStateChanged()
    {
        PublishVideoStateChanged(force: false);
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

    private void PublishVideoStateChanged(bool force)
    {
        PublishVideoPanelVisibleChanged(force);
        PublishVideoPlayingChanged(force);
        PublishVideoPlaybackTimeChanged(force);
        PublishVideoCommandChanged(force);
    }

    private void PublishVideoPanelVisibleChanged(bool force)
    {
        bool visible = IsVideoPanelVisible;

        if (!force && hasPublishedVideoPanelVisible && lastPublishedVideoPanelVisible == visible)
            return;

        hasPublishedVideoPanelVisible = true;
        lastPublishedVideoPanelVisible = visible;
        VideoPanelVisibleChanged?.Invoke(visible);
        onVideoPanelVisibleChanged.Invoke(visible);
    }

    private void PublishVideoPlayingChanged(bool force)
    {
        bool playing = IsVideoPlaying;

        if (!force && hasPublishedVideoPlaying && lastPublishedVideoPlaying == playing)
            return;

        hasPublishedVideoPlaying = true;
        lastPublishedVideoPlaying = playing;
        VideoPlayingChanged?.Invoke(playing);
        onVideoPlayingChanged.Invoke(playing);
    }

    private void PublishVideoPlaybackTimeChanged(bool force)
    {
        float timeSeconds = VideoPlaybackTimeSeconds;

        if (!force && hasPublishedVideoPlaybackTime && Mathf.Approximately(lastPublishedVideoPlaybackTime, timeSeconds))
            return;

        hasPublishedVideoPlaybackTime = true;
        lastPublishedVideoPlaybackTime = timeSeconds;
        VideoPlaybackTimeChanged?.Invoke(timeSeconds);
        onVideoPlaybackTimeChanged.Invoke(timeSeconds);
    }

    private void PublishVideoCommandChanged(bool force)
    {
        int revision = VideoCommandRevision;

        if (!force && hasPublishedVideoCommandRevision && lastPublishedVideoCommandRevision == revision)
            return;

        hasPublishedVideoCommandRevision = true;
        lastPublishedVideoCommandRevision = revision;
        VideoCommandReceived?.Invoke(revision);
        onVideoCommandRevisionChanged.Invoke(revision);
    }

    private static string NormalizeBlackboardText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return text.Length <= BlackboardTextMaxLength
            ? text
            : text.Substring(0, BlackboardTextMaxLength);
    }

    private void IncrementVideoCommandRevision()
    {
        VideoCommandRevision = unchecked(VideoCommandRevision + 1);
    }

    private static float NormalizeVideoTime(float timeSeconds)
    {
        if (float.IsNaN(timeSeconds) || float.IsInfinity(timeSeconds))
            return MinimumVideoTimeSeconds;

        return Mathf.Max(MinimumVideoTimeSeconds, timeSeconds);
    }

    public static bool TryGetActiveNetworked(out ClassroomSessionState sessionState)
    {
        if (IsNetworkReady(Instance))
        {
            sessionState = Instance;
            return true;
        }

        ClassroomSessionState[] states = FindObjectsOfType<ClassroomSessionState>(true);
        for (int i = 0; i < states.Length; i++)
        {
            ClassroomSessionState candidate = states[i];
            if (!IsNetworkReady(candidate))
                continue;

            Instance = candidate;
            sessionState = candidate;
            return true;
        }

        sessionState = null;
        return false;
    }

    private static bool IsUsableInstance(ClassroomSessionState state)
    {
        return state != null && state.gameObject != null;
    }

    private static bool IsNetworkReady(ClassroomSessionState state)
    {
        return IsUsableInstance(state) && state.Object != null && state.Object.IsValid;
    }
}
