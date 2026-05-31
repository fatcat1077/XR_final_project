using UnityEngine;
using TMPro;

public class TeacherHandRaiseUI : MonoBehaviour
{
    [SerializeField] private GameObject handRaiseIcon; // 舉手的小圖示
    [SerializeField] private TMP_Text statusText;      // 顯示「某某某正在舉手」

    // 對應第 5 點的 OnStudentHandRaisedChanged (bool)
    public void UpdateHandRaiseStatus(bool isRaised)
    {
        // 1. 顯示或隱藏圖示
        if (handRaiseIcon != null)
            handRaiseIcon.SetActive(isRaised);

        // 2. 更新文字
        if (statusText != null)
            statusText.text = isRaised ? "有學生舉手了！" : "";
    }

    // 進階：對應第 5 點的 OnStudentHandRaisedPlayerChanged (PlayerRef)
    // 如果你想知道「是誰」舉手，可以接這個事件
    public void UpdateWhoRaisedHand(Fusion.PlayerRef player)
    {
        Debug.Log($"玩家 {player.PlayerId} 舉手了");
        // 你可以根據 player 資訊去抓玩家名字
    }
}