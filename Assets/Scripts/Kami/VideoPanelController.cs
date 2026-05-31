using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;
public class VideoPanelController : MonoBehaviour
{
    private MeshRenderer mesh;

    private VideoPlayer unityVideoPlayer;

    void Start()
    {
        mesh = GetComponent<MeshRenderer>();

        unityVideoPlayer = GetComponent<VideoPlayer>();

        if (unityVideoPlayer == null)
        {
            unityVideoPlayer = gameObject.AddComponent<VideoPlayer>();
        }
        HidePanel();
    }

    public void ShowPanel()
    {
        mesh.enabled = true;
    }

    public void HidePanel()
    {
        mesh.enabled = false;
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
}