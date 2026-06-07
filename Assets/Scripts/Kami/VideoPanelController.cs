using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;
public class VideoPanelController : MonoBehaviour
{
    private MeshRenderer mesh;

    private VideoPlayer unityVideoPlayer;

    void Awake()
    {
        CacheComponents();
    }

    void Start()
    {
        SetPanelVisible(false);
    }
    public void PlayVideo()
    {
        if (unityVideoPlayer != null)
        {
            unityVideoPlayer.Play();
        }
    }

    public void StopVideo()
    {
        if (unityVideoPlayer != null)
        {
            unityVideoPlayer.Pause();
        }
    }

    public void HandleVideoPanelVisibleChanged(bool visible)
    {
        SetPanelVisible(visible);
    }

    public void HandleVideoPlayingChanged(bool playing)
    {
        if (playing)
        {
            PlayVideo();
        }
        else
        {
            StopVideo();
        }
    }

    public void HandleVideoPlaybackTimeChanged(float timeSeconds)
    {
        if (unityVideoPlayer != null)
        {
            unityVideoPlayer.time = Mathf.Max(0f, timeSeconds);
        }
    }

    public void HandleVideoCommandReceived(int revision)
    {
        var sessionState = ClassroomSessionState.Instance;
        if (sessionState == null)
            return;

        SetPanelVisible(sessionState.IsVideoPanelVisible);
        HandleVideoPlaybackTimeChanged(sessionState.VideoPlaybackTimeSeconds);
        HandleVideoPlayingChanged(sessionState.IsVideoPlaying);
    }

    private void SetPanelVisible(bool visible)
    {
        if (mesh != null)
        {
            mesh.enabled = visible;
        }
    }

    private void CacheComponents()
    {
        mesh = GetComponent<MeshRenderer>();
        unityVideoPlayer = GetComponent<VideoPlayer>();

        if (unityVideoPlayer == null)
        {
            unityVideoPlayer = gameObject.AddComponent<VideoPlayer>();
        }
    }
}
