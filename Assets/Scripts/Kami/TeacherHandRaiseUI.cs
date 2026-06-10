using Fusion;
using TMPro;
using UnityEngine;

public class TeacherHandRaiseUI : MonoBehaviour
{
    [SerializeField] private GameObject handRaiseIcon;
    [SerializeField] private TMP_Text statusText;

    public void UpdateHandRaiseStatus(bool isRaised)
    {
        if (handRaiseIcon != null)
            handRaiseIcon.SetActive(isRaised);

        if (statusText != null)
            statusText.text = isRaised ? "Student raised hand" : string.Empty;

        Debug.Log($"[TeacherHandRaiseUI] Student hand raised={isRaised}");
    }

    public void UpdateWhoRaisedHand(PlayerRef player)
    {
        Debug.Log($"[TeacherHandRaiseUI] Student {player.PlayerId} raised hand.");
    }
}
