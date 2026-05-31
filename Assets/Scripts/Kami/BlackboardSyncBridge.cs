using UnityEngine;

public class BlackboardSyncBridge : MonoBehaviour
{
    // 這個方法用來接住 STT 傳出來的字串，並送往網路
    public void SendTextToNetwork(string transcribedText)
    {
        if (ClassroomSessionState.Instance != null)
        {
            // 呼叫講義第 4 點指定的接口
            ClassroomSessionState.Instance.RequestSetBlackboardText(transcribedText);
            Debug.Log($"[SyncBridge] 已將語音文字送往網路: {transcribedText}");
        }
    }
}