using TMPro;
using UnityEngine;

public class BlackboardSyncBridge : MonoBehaviour
{
    public void SendTextToNetwork(string transcribedText)
    {
        string text = transcribedText ?? string.Empty;
        bool sentToClassroomState = false;

        ClassroomSessionState sessionState = ClassroomSessionState.Instance != null
            ? ClassroomSessionState.Instance
            : FindObjectOfType<ClassroomSessionState>();

        if (sessionState != null && sessionState.Object != null && sessionState.Object.IsValid)
        {
            sessionState.RequestSetBlackboardText(text);
            sentToClassroomState = true;
        }

        SetLocalSubtitleText(text);
        Debug.Log($"[BlackboardSyncBridge] Published subtitle. classroomState={sentToClassroomState}, text={text}");
    }

    private static void SetLocalSubtitleText(string text)
    {
        TMP_Text[] textObjects = FindObjectsOfType<TMP_Text>(true);
        for (int i = 0; i < textObjects.Length; i++)
        {
            TMP_Text textObject = textObjects[i];
            if (textObject != null && textObject.name == "SubtitleText")
                textObject.text = text ?? string.Empty;
        }
    }
}
