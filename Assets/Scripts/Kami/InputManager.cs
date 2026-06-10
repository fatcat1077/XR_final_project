using UnityEngine;

public class InputManager : MonoBehaviour
{
    private void Update()
    {
        if (WasPressed(KeyCode.Alpha0, KeyCode.Keypad0))
            RequestTeacherEnvironment(ClassroomEnvironment.Default);

        if (WasPressed(KeyCode.Alpha1, KeyCode.Keypad1))
            RequestTeacherEnvironment(ClassroomEnvironment.Ocean);
        else if (WasPressed(KeyCode.Alpha2, KeyCode.Keypad2))
            RequestTeacherEnvironment(ClassroomEnvironment.Space);
        else if (WasPressed(KeyCode.Alpha3, KeyCode.Keypad3))
            RequestStudentHandRaised(true);
        else if (WasPressed(KeyCode.Alpha4, KeyCode.Keypad4))
            RequestStudentHandRaised(false);
        else if (WasPressed(KeyCode.Alpha5, KeyCode.Keypad5))
            RequestTeacherClearStudentHand();
        else if (WasPressed(KeyCode.Alpha6, KeyCode.Keypad6))
            RequestTeacherVideoPanel(true);
        else if (WasPressed(KeyCode.Alpha7, KeyCode.Keypad7))
            RequestTeacherVideoPanel(false);
        else if (WasPressed(KeyCode.Alpha8, KeyCode.Keypad8))
            RequestTeacherVideoPlaying(true);
        else if (WasPressed(KeyCode.Alpha9, KeyCode.Keypad9))
            RequestTeacherVideoPlaying(false);
    }

    private static void RequestTeacherEnvironment(ClassroomEnvironment environment)
    {
        if (!IsTeacher())
        {
            Debug.LogWarning($"[InputManager] Ignored environment switch to {environment}; local role is {LocalUserProfile.Role}.");
            return;
        }

        bool requested = QuestClassroomSceneTravel.RequestEnvironmentScene(environment, "InputManager keyboard shortcut");
        Debug.Log($"[InputManager] Teacher requested environment: {environment}, requested={requested}");
    }

    private static void RequestStudentHandRaised(bool raised)
    {
        if (LocalUserProfile.Role != UserRole.Student)
        {
            Debug.LogWarning($"[InputManager] Ignored student hand request; local role is {LocalUserProfile.Role}.");
            return;
        }

        if (!TryGetSessionState(out ClassroomSessionState sessionState))
            return;

        sessionState.RequestSetStudentHandRaised(raised);
        Debug.Log($"[InputManager] Student hand raised={raised}");
    }

    private static void RequestTeacherClearStudentHand()
    {
        if (!IsTeacher())
        {
            Debug.LogWarning($"[InputManager] Ignored clear student hand request; local role is {LocalUserProfile.Role}.");
            return;
        }

        if (!TryGetSessionState(out ClassroomSessionState sessionState))
            return;

        sessionState.RequestClearStudentHandRaised();
        Debug.Log("[InputManager] Teacher cleared student hand raise.");
    }

    private static void RequestTeacherVideoPanel(bool visible)
    {
        if (!IsTeacher())
        {
            Debug.LogWarning($"[InputManager] Ignored video panel request; local role is {LocalUserProfile.Role}.");
            return;
        }

        if (!TryGetSessionState(out ClassroomSessionState sessionState))
            return;

        sessionState.RequestSetVideoPanelVisible(visible);
        Debug.Log($"[InputManager] Teacher set video panel visible={visible}");
    }

    private static void RequestTeacherVideoPlaying(bool playing)
    {
        if (!IsTeacher())
        {
            Debug.LogWarning($"[InputManager] Ignored video playback request; local role is {LocalUserProfile.Role}.");
            return;
        }

        if (!TryGetSessionState(out ClassroomSessionState sessionState))
            return;

        sessionState.RequestSetVideoPlaying(playing);
        Debug.Log($"[InputManager] Teacher set video playing={playing}");
    }

    private static bool TryGetSessionState(out ClassroomSessionState sessionState)
    {
        if (ClassroomSessionState.TryGetActiveNetworked(out sessionState))
            return true;

        Debug.LogWarning("[InputManager] ClassroomSessionState is not ready.");
        return false;
    }

    private static bool IsTeacher()
    {
        return LocalUserProfile.Role == UserRole.Teacher;
    }

    private static bool WasPressed(KeyCode primaryKey, KeyCode alternateKey)
    {
        return Input.GetKeyDown(primaryKey) || Input.GetKeyDown(alternateKey);
    }
}
