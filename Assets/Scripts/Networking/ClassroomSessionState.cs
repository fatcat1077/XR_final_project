using System;
using Fusion;
using UnityEngine;
using UnityEngine.Events;

public class ClassroomSessionState : NetworkBehaviour
{
    private const int BlackboardTextMaxLength = 512;
    private const float MinimumVideoTimeSeconds = 0f;
    private const float UnchangedVideoTime = -1f;

    [Serializable]
    public class ClassroomEnvironmentUnityEvent : UnityEvent<ClassroomEnvironment>
    {
    }

    [Serializable]
    public class ClassroomSubtitleSourceUnityEvent : UnityEvent<ClassroomSubtitleSource>
    {
    }

    [Serializable]
    public class VideoPlaybackUnityEvent : UnityEvent<bool, float>
    {
    }

    [Header("Environment Events")]
    [SerializeField] private ClassroomEnvironmentUnityEvent onEnvironmentChanged = new();

    [Header("Teacher / Student Events")]
    [SerializeField] private UnityEvent<bool> onStudentHandRaisedChanged = new();
    [SerializeField] private UnityEvent<int> onStudentHandRaisedPlayerChanged = new();

    [Header("Blackboard Events")]
    [SerializeField] private UnityEvent<string> onBlackboardTextChanged = new();
    [SerializeField] private ClassroomSubtitleSourceUnityEvent onSubtitleSourceChanged = new();
    [SerializeField] private UnityEvent<int> onBlackboardRevisionChanged = new();

    [Header("Video Events")]
    [SerializeField] private UnityEvent<bool> onVideoPanelVisibleChanged = new();
    [SerializeField] private UnityEvent<bool> onVideoPlayingChanged = new();
    [SerializeField] private UnityEvent<float> onVideoPlaybackTimeChanged = new();
    [SerializeField] private UnityEvent<int> onVideoCommandRevisionChanged = new();
    [SerializeField] private VideoPlaybackUnityEvent onVideoPlaybackChanged = new();

    [Networked, OnChangedRender(nameof(HandleRolePlayersChanged))]
    public PlayerRef TeacherPlayer { get; private set; }

    [Networked, OnChangedRender(nameof(HandleRolePlayersChanged))]
    public PlayerRef StudentPlayer { get; private set; }

    [Networked, OnChangedRender(nameof(HandleEnvironmentChanged))]
    public ClassroomEnvironment CurrentEnvironment { get; private set; }

    [Networked, OnChangedRender(nameof(HandleStudentHandRaisedChanged))]
    public NetworkBool IsStudentHandRaised { get; private set; }

    [Networked, OnChangedRender(nameof(HandleStudentHandRaisedChanged))]
    public PlayerRef StudentHandRaisedPlayer { get; private set; }

    [Networked, OnChangedRender(nameof(HandleBlackboardChanged))]
    public NetworkString<_512> BlackboardText { get; private set; }

    [Networked, OnChangedRender(nameof(HandleBlackboardChanged))]
    public ClassroomSubtitleSource SubtitleSource { get; private set; }

    [Networked, OnChangedRender(nameof(HandleBlackboardChanged))]
    public int BlackboardRevision { get; private set; }

    [Networked, OnChangedRender(nameof(HandleVideoStateChanged))]
    public NetworkBool IsVideoPanelVisible { get; private set; }

    [Networked, OnChangedRender(nameof(HandleVideoStateChanged))]
    public NetworkBool IsVideoPlaying { get; private set; }

    [Networked, OnChangedRender(nameof(HandleVideoStateChanged))]
    public float VideoPlaybackTimeSeconds { get; private set; }

    [Networked, OnChangedRender(nameof(HandleVideoStateChanged))]
    public int VideoCommandRevision { get; private set; }

    public bool IsStudentHandRaisedValue => IsStudentHandRaised;
    public int StudentHandRaisedPlayerId => StudentHandRaisedPlayer == PlayerRef.None ? -1 : StudentHandRaisedPlayer.PlayerId;
    public string BlackboardTextValue => BlackboardText.ToString();
    public bool IsVideoPanelVisibleValue => IsVideoPanelVisible;
    public bool IsVideoPlayingValue => IsVideoPlaying;
    public float VideoTimeSecondsValue => VideoPlaybackTimeSeconds;

    public event Action<PlayerRef, PlayerRef> RolePlayersChanged;
    public event Action<ClassroomEnvironment> EnvironmentChanged;
    public event Action<bool, PlayerRef> StudentHandRaisedChanged;
    public event Action<string, ClassroomSubtitleSource, int> BlackboardChanged;
    public event Action<bool> VideoPanelVisibleChanged;
    public event Action<bool> VideoPlayingChanged;
    public event Action<float> VideoPlaybackTimeChanged;
    public event Action<bool, float, int> VideoCommandReceived;
    public event Action<bool, float> VideoPlaybackChanged;

    public ClassroomEnvironmentUnityEvent OnEnvironmentChanged => onEnvironmentChanged;
    public UnityEvent<bool> OnStudentHandRaisedChanged => onStudentHandRaisedChanged;
    public UnityEvent<int> OnStudentHandRaisedPlayerChanged => onStudentHandRaisedPlayerChanged;
    public UnityEvent<string> OnBlackboardTextChanged => onBlackboardTextChanged;
    public ClassroomSubtitleSourceUnityEvent OnSubtitleSourceChanged => onSubtitleSourceChanged;
    public UnityEvent<int> OnBlackboardRevisionChanged => onBlackboardRevisionChanged;
    public UnityEvent<bool> OnVideoPanelVisibleChanged => onVideoPanelVisibleChanged;
    public UnityEvent<bool> OnVideoPlayingChanged => onVideoPlayingChanged;
    public UnityEvent<float> OnVideoPlaybackTimeChanged => onVideoPlaybackTimeChanged;
    public UnityEvent<int> OnVideoCommandRevisionChanged => onVideoCommandRevisionChanged;
    public VideoPlaybackUnityEvent OnVideoPlaybackChanged => onVideoPlaybackChanged;

    private bool hasPublishedRolePlayers;
    private bool hasPublishedEnvironment;
    private bool hasPublishedStudentHandRaised;
    private bool hasPublishedBlackboard;
    private bool hasPublishedVideoPanelVisible;
    private bool hasPublishedVideoPlaying;
    private bool hasPublishedVideoPlaybackTime;
    private bool hasPublishedVideoCommand;
    private bool hasPublishedVideoPlayback;

    private PlayerRef lastPublishedTeacherPlayer;
    private PlayerRef lastPublishedStudentPlayer;
    private ClassroomEnvironment lastPublishedEnvironment;
    private bool lastPublishedStudentHandRaised;
    private PlayerRef lastPublishedStudentHandRaisedPlayer;
    private string lastPublishedBlackboardText = string.Empty;
    private ClassroomSubtitleSource lastPublishedSubtitleSource;
    private int lastPublishedBlackboardRevision;
    private bool lastPublishedVideoPanelVisible;
    private bool lastPublishedVideoPlaying;
    private float lastPublishedVideoPlaybackTime;
    private int lastPublishedVideoCommandRevision;
    private bool lastPublishedLegacyVideoPlaying;
    private float lastPublishedLegacyVideoPlaybackTime;

    public override void Spawned()
    {
        PublishRolePlayersChanged(force: true);
        PublishEnvironmentChanged(force: true);
        PublishStudentHandRaisedChanged(force: true);
        PublishBlackboardChanged(force: true);
        PublishVideoStateChanged(force: true);
        RegisterLocalRoleIfKnown();
    }

    public static ClassroomSessionState FindInScene()
    {
        return FindObjectOfType<ClassroomSessionState>(true);
    }

    public void RequestRegisterLocalRole(UserRole role)
    {
        PlayerRef localPlayer = Runner != null ? Runner.LocalPlayer : PlayerRef.None;

        if (HasStateAuthority)
        {
            SetRolePlayer(role, localPlayer);
            return;
        }

        RPC_RequestRegisterRole((int)role);
    }

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
        PlayerRef localPlayer = Runner != null ? Runner.LocalPlayer : PlayerRef.None;

        if (HasStateAuthority)
        {
            SetStudentHandRaisedState(raised, raised ? localPlayer : PlayerRef.None);
            return;
        }

        RPC_RequestSetStudentHandRaised(raised);
    }

    public void RequestClearStudentHandRaised()
    {
        if (HasStateAuthority)
        {
            SetStudentHandRaisedState(false, PlayerRef.None);
            return;
        }

        RPC_RequestClearStudentHandRaised();
    }

    public void RequestSetBlackboardText(string text)
    {
        RequestSetBlackboardText(text, ClassroomSubtitleSource.Manual);
    }

    public void RequestSetSpeechToTextCaption(string text)
    {
        RequestSetBlackboardText(text, ClassroomSubtitleSource.SpeechToText);
    }

    public void RequestSetDemoSubtitle(string text)
    {
        RequestSetBlackboardText(text, ClassroomSubtitleSource.Demo);
    }

    public void RequestSetBlackboardText(string text, ClassroomSubtitleSource source)
    {
        if (HasStateAuthority)
        {
            SetBlackboardState(text, source);
            return;
        }

        RPC_RequestSetBlackboardText(text ?? string.Empty, source);
    }

    public void RequestClearBlackboard()
    {
        if (HasStateAuthority)
        {
            SetBlackboardState(string.Empty, ClassroomSubtitleSource.Empty);
            return;
        }

        RPC_RequestClearBlackboard();
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
        RequestSyncVideo(!IsVideoPlaying, VideoPlaybackTimeSeconds);
    }

    public void RequestSetVideoPlaybackTime(float playbackTimeSeconds)
    {
        RequestSyncVideo(IsVideoPlaying, playbackTimeSeconds);
    }

    public void RequestSyncVideo(bool playing, float playbackTimeSeconds)
    {
        if (HasStateAuthority)
        {
            SetVideoPlaybackState(playing, playbackTimeSeconds);
            return;
        }

        RPC_RequestSyncVideo(playing, playbackTimeSeconds);
    }

    public void RequestSetVideoPlayback(bool isPlaying, float timeSeconds)
    {
        float resolvedTimeSeconds = timeSeconds < 0f ? VideoPlaybackTimeSeconds : timeSeconds;
        RequestSyncVideo(isPlaying, resolvedTimeSeconds);
    }

    public void RequestPlayVideo(float timeSeconds = UnchangedVideoTime)
    {
        RequestSetVideoPlayback(true, timeSeconds);
    }

    public void RequestPauseVideo(float timeSeconds = UnchangedVideoTime)
    {
        RequestSetVideoPlayback(false, timeSeconds);
    }

    public void RequestSeekVideo(float timeSeconds)
    {
        RequestSetVideoPlayback(IsVideoPlayingValue, timeSeconds);
    }

    public void RequestResetClassroom()
    {
        if (HasStateAuthority)
        {
            ResetClassroomState();
            return;
        }

        RPC_RequestResetClassroom();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestRegisterRole(int roleValue, RpcInfo info = default)
    {
        SetRolePlayer((UserRole)roleValue, info.Source);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestSetEnvironment(ClassroomEnvironment environment, RpcInfo info = default)
    {
        if (!CanAcceptTeacherCommand(info.Source, nameof(RPC_RequestSetEnvironment)))
            return;

        SetEnvironmentState(environment);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestSetStudentHandRaised(bool raised, RpcInfo info = default)
    {
        PlayerRef raisedBy = raised ? info.Source : PlayerRef.None;
        SetStudentHandRaisedState(raised, raisedBy);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestClearStudentHandRaised(RpcInfo info = default)
    {
        if (!CanAcceptTeacherCommand(info.Source, nameof(RPC_RequestClearStudentHandRaised)))
            return;

        SetStudentHandRaisedState(false, PlayerRef.None);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestSetBlackboardText(string text, ClassroomSubtitleSource source, RpcInfo info = default)
    {
        if (!CanAcceptTeacherCommand(info.Source, nameof(RPC_RequestSetBlackboardText)))
            return;

        SetBlackboardState(text, source);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestClearBlackboard(RpcInfo info = default)
    {
        if (!CanAcceptTeacherCommand(info.Source, nameof(RPC_RequestClearBlackboard)))
            return;

        SetBlackboardState(string.Empty, ClassroomSubtitleSource.Empty);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestSetVideoPanelVisible(bool visible, RpcInfo info = default)
    {
        if (!CanAcceptTeacherCommand(info.Source, nameof(RPC_RequestSetVideoPanelVisible)))
            return;

        SetVideoPanelVisibleState(visible);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestSyncVideo(bool playing, float playbackTimeSeconds, RpcInfo info = default)
    {
        if (!CanAcceptTeacherCommand(info.Source, nameof(RPC_RequestSyncVideo)))
            return;

        SetVideoPlaybackState(playing, playbackTimeSeconds);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestResetClassroom(RpcInfo info = default)
    {
        if (!CanAcceptTeacherCommand(info.Source, nameof(RPC_RequestResetClassroom)))
            return;

        ResetClassroomState();
    }

    private void RegisterLocalRoleIfKnown()
    {
        if (LocalUserProfile.Role == UserRole.None)
        {
            Debug.LogWarning("[ClassroomSessionState] Local role is None. Role registration skipped.");
            return;
        }

        RequestRegisterLocalRole(LocalUserProfile.Role);
    }

    private void SetRolePlayer(UserRole role, PlayerRef player)
    {
        if (player == PlayerRef.None)
            return;

        if (role == UserRole.Teacher)
        {
            if (TeacherPlayer != PlayerRef.None && TeacherPlayer != player)
            {
                Debug.LogWarning($"[ClassroomSessionState] Teacher role is already assigned to {TeacherPlayer}. Ignored {player}.");
                return;
            }

            TeacherPlayer = player;
        }
        else if (role == UserRole.Student)
        {
            if (StudentPlayer != PlayerRef.None && StudentPlayer != player)
            {
                Debug.LogWarning($"[ClassroomSessionState] Student role is already assigned to {StudentPlayer}. Ignored {player}.");
                return;
            }

            StudentPlayer = player;
        }
        else
        {
            Debug.LogWarning($"[ClassroomSessionState] Unsupported role registration '{role}' from {player}.");
            return;
        }

        PublishRolePlayersChanged(force: false);
    }

    private void SetEnvironmentState(ClassroomEnvironment environment)
    {
        CurrentEnvironment = environment;
        PublishEnvironmentChanged(force: false);
    }

    private void SetStudentHandRaisedState(bool raised, PlayerRef player)
    {
        if (raised && StudentPlayer != PlayerRef.None && player != PlayerRef.None && player != StudentPlayer)
        {
            Debug.LogWarning($"[ClassroomSessionState] Rejected hand raise from {player}. StudentPlayer={StudentPlayer}");
            return;
        }

        IsStudentHandRaised = raised;
        StudentHandRaisedPlayer = raised ? player : PlayerRef.None;
        PublishStudentHandRaisedChanged(force: false);
    }

    private void SetBlackboardState(string text, ClassroomSubtitleSource source)
    {
        string normalizedText = NormalizeBlackboardText(text);

        BlackboardText = normalizedText;
        SubtitleSource = string.IsNullOrEmpty(normalizedText) ? ClassroomSubtitleSource.Empty : source;
        BlackboardRevision++;

        PublishBlackboardChanged(force: false);
    }

    private void SetVideoPanelVisibleState(bool visible)
    {
        IsVideoPanelVisible = visible;
        PublishVideoStateChanged(force: false);
    }

    private void SetVideoPlaybackState(bool playing, float playbackTimeSeconds)
    {
        IsVideoPlaying = playing;
        VideoPlaybackTimeSeconds = Mathf.Max(MinimumVideoTimeSeconds, playbackTimeSeconds);
        VideoCommandRevision++;

        PublishVideoStateChanged(force: false);
    }

    private void ResetClassroomState()
    {
        SetEnvironmentState(ClassroomEnvironment.Default);
        SetStudentHandRaisedState(false, PlayerRef.None);
        SetBlackboardState(string.Empty, ClassroomSubtitleSource.Empty);
        SetVideoPanelVisibleState(false);
        SetVideoPlaybackState(false, MinimumVideoTimeSeconds);
    }

    private bool CanAcceptTeacherCommand(PlayerRef source, string commandName)
    {
        if (source == PlayerRef.None)
            return true;

        if (TeacherPlayer != PlayerRef.None && source == TeacherPlayer)
            return true;

        Debug.LogWarning($"[ClassroomSessionState] Rejected teacher command '{commandName}' from {source}. TeacherPlayer={TeacherPlayer}");
        return false;
    }

    private void HandleRolePlayersChanged()
    {
        PublishRolePlayersChanged(force: false);
    }

    private void HandleEnvironmentChanged()
    {
        PublishEnvironmentChanged(force: false);
    }

    private void HandleStudentHandRaisedChanged()
    {
        PublishStudentHandRaisedChanged(force: false);
    }

    private void HandleBlackboardChanged()
    {
        PublishBlackboardChanged(force: false);
    }

    private void HandleVideoStateChanged()
    {
        PublishVideoStateChanged(force: false);
    }

    private void PublishRolePlayersChanged(bool force)
    {
        if (!force
            && hasPublishedRolePlayers
            && lastPublishedTeacherPlayer == TeacherPlayer
            && lastPublishedStudentPlayer == StudentPlayer)
        {
            return;
        }

        hasPublishedRolePlayers = true;
        lastPublishedTeacherPlayer = TeacherPlayer;
        lastPublishedStudentPlayer = StudentPlayer;
        RolePlayersChanged?.Invoke(TeacherPlayer, StudentPlayer);
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

        if (!force
            && hasPublishedStudentHandRaised
            && lastPublishedStudentHandRaised == raised
            && lastPublishedStudentHandRaisedPlayer == StudentHandRaisedPlayer)
        {
            return;
        }

        hasPublishedStudentHandRaised = true;
        lastPublishedStudentHandRaised = raised;
        lastPublishedStudentHandRaisedPlayer = StudentHandRaisedPlayer;
        StudentHandRaisedChanged?.Invoke(raised, StudentHandRaisedPlayer);
        onStudentHandRaisedChanged.Invoke(raised);
        onStudentHandRaisedPlayerChanged.Invoke(StudentHandRaisedPlayerId);
    }

    private void PublishBlackboardChanged(bool force)
    {
        string text = BlackboardText.ToString();

        if (!force
            && hasPublishedBlackboard
            && lastPublishedBlackboardText == text
            && lastPublishedSubtitleSource == SubtitleSource
            && lastPublishedBlackboardRevision == BlackboardRevision)
        {
            return;
        }

        hasPublishedBlackboard = true;
        lastPublishedBlackboardText = text;
        lastPublishedSubtitleSource = SubtitleSource;
        lastPublishedBlackboardRevision = BlackboardRevision;
        BlackboardChanged?.Invoke(text, SubtitleSource, BlackboardRevision);
        onBlackboardTextChanged.Invoke(text);
        onSubtitleSourceChanged.Invoke(SubtitleSource);
        onBlackboardRevisionChanged.Invoke(BlackboardRevision);
    }

    private void PublishVideoStateChanged(bool force)
    {
        PublishVideoPanelVisibleChanged(force);
        PublishVideoPlayingChanged(force);
        PublishVideoPlaybackTimeChanged(force);
        PublishVideoCommandChanged(force);
        PublishVideoPlaybackChanged(force);
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
        float playbackTime = VideoPlaybackTimeSeconds;

        if (!force && hasPublishedVideoPlaybackTime && Mathf.Approximately(lastPublishedVideoPlaybackTime, playbackTime))
            return;

        hasPublishedVideoPlaybackTime = true;
        lastPublishedVideoPlaybackTime = playbackTime;
        VideoPlaybackTimeChanged?.Invoke(playbackTime);
        onVideoPlaybackTimeChanged.Invoke(playbackTime);
    }

    private void PublishVideoCommandChanged(bool force)
    {
        if (!force && hasPublishedVideoCommand && lastPublishedVideoCommandRevision == VideoCommandRevision)
            return;

        hasPublishedVideoCommand = true;
        lastPublishedVideoCommandRevision = VideoCommandRevision;
        VideoCommandReceived?.Invoke(IsVideoPlaying, VideoPlaybackTimeSeconds, VideoCommandRevision);
        onVideoCommandRevisionChanged.Invoke(VideoCommandRevision);
    }

    private void PublishVideoPlaybackChanged(bool force)
    {
        bool playing = IsVideoPlaying;
        float playbackTime = VideoPlaybackTimeSeconds;

        if (!force
            && hasPublishedVideoPlayback
            && lastPublishedLegacyVideoPlaying == playing
            && Mathf.Approximately(lastPublishedLegacyVideoPlaybackTime, playbackTime))
        {
            return;
        }

        hasPublishedVideoPlayback = true;
        lastPublishedLegacyVideoPlaying = playing;
        lastPublishedLegacyVideoPlaybackTime = playbackTime;
        VideoPlaybackChanged?.Invoke(playing, playbackTime);
        onVideoPlaybackChanged.Invoke(playing, playbackTime);
    }

    private static string NormalizeBlackboardText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        string trimmed = text.Trim();
        return trimmed.Length <= BlackboardTextMaxLength
            ? trimmed
            : trimmed.Substring(0, BlackboardTextMaxLength);
    }
}

public enum ClassroomSubtitleSource
{
    Empty = 0,
    SpeechToText = 1,
    Manual = 2,
    Demo = 3
}
