using UnityEngine;

public class MicLoopbackTest : MonoBehaviour
{
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private string preferredDevice = "";
    [SerializeField] private int lengthSec = 10;
    [SerializeField] private int frequency = 16000;

    private string selectedDevice;
    private AudioClip micClip;

    void Start()
    {
        Debug.Log("[MicLoopbackTest] Start");

        foreach (var d in Microphone.devices)
        {
            Debug.Log("[MicLoopbackTest] Found mic: " + d);
        }

        if (Microphone.devices.Length == 0)
        {
            Debug.LogError("[MicLoopbackTest] 沒有找到任何麥克風裝置");
            return;
        }

        selectedDevice = string.IsNullOrWhiteSpace(preferredDevice)
            ? Microphone.devices[0]
            : preferredDevice;

        Debug.Log("[MicLoopbackTest] Using mic: " + selectedDevice);

        micClip = Microphone.Start(selectedDevice, true, lengthSec, frequency);

        if (micClip == null)
        {
            Debug.LogError("[MicLoopbackTest] Microphone.Start 回傳 null，代表啟動失敗");
            return;
        }

        while (Microphone.GetPosition(selectedDevice) <= 0) { }

        audioSource.clip = micClip;
        audioSource.loop = true;
        audioSource.Play();

        Debug.Log("[MicLoopbackTest] 麥克風已開始錄音並 loopback 播放");
    }

    void OnDisable()
    {
        if (!string.IsNullOrEmpty(selectedDevice) && Microphone.IsRecording(selectedDevice))
        {
            Microphone.End(selectedDevice);
            Debug.Log("[MicLoopbackTest] 停止麥克風");
        }
    }
}