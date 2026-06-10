using Fusion;
using TMPro;
using UnityEngine;

public class TeacherHandRaiseUI : MonoBehaviour
{
    private const string RaisedPromptText = "Student raised hand\nPress 5 to clear";

    [SerializeField] private GameObject handRaiseIcon;
    [SerializeField] private TMP_Text statusText;

    public void UpdateHandRaiseStatus(bool isRaised)
    {
        bool shouldShow = isRaised && ShouldShowTeacherNotification();

        if (handRaiseIcon != null)
            handRaiseIcon.SetActive(shouldShow);

        if (statusText != null)
            statusText.text = shouldShow ? RaisedPromptText : string.Empty;

        Debug.Log($"[TeacherHandRaiseUI] Student hand raised={isRaised}, shown={shouldShow}");
    }

    public void UpdateWhoRaisedHand(PlayerRef player)
    {
        Debug.Log($"[TeacherHandRaiseUI] Student {player.PlayerId} raised hand.");
    }

    private static bool ShouldShowTeacherNotification()
    {
        if (LocalUserProfile.Role == UserRole.Teacher)
            return true;

        NetworkRunner[] runners = FindObjectsOfType<NetworkRunner>(true);
        for (int i = 0; i < runners.Length; i++)
        {
            NetworkRunner runner = runners[i];
            if (runner != null && runner.IsRunning && runner.IsServer)
                return true;
        }

        return false;
    }
}
