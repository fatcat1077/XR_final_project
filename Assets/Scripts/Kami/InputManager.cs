using UnityEngine;

public class InputManager : MonoBehaviour
{
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Alpha0))
            RequestTeacherEnvironment(ClassroomEnvironment.Default);

        if (Input.GetKeyDown(KeyCode.Alpha1))
            RequestTeacherEnvironment(ClassroomEnvironment.Ocean);
        else if (Input.GetKeyDown(KeyCode.Alpha2))
            RequestTeacherEnvironment(ClassroomEnvironment.Space);
        else if (Input.GetKeyDown(KeyCode.Alpha3))
            RequestStudentHandRaised(true);
        else if (Input.GetKeyDown(KeyCode.Alpha4))
            RequestStudentHandRaised(false);
        else if (Input.GetKeyDown(KeyCode.Alpha5))
            RequestTeacherClearStudentHand();
        else if (Input.GetKeyDown(KeyCode.Alpha6))
            RequestTeacherVideoPanel(true);
        else if (Input.GetKeyDown(KeyCode.Alpha7))
            RequestTeacherVideoPanel(false);
        else if (Input.GetKeyDown(KeyCode.Alpha8))
            RequestTeacherVideoPlaying(true);
        else if (Input.GetKeyDown(KeyCode.Alpha9))
            RequestTeacherVideoPlaying(false);
    }

    private static void RequestTeacherEnvironment(ClassroomEnvironment environment)
    {
        if (!IsTeacher())
        {
            Debug.LogWarning($"[InputManager] Ignored environment switch to {environment}; local role is {LocalUserProfile.Role}.");
            return;
        }

        if (environment == ClassroomEnvironment.Ocean || environment == ClassroomEnvironment.Space)
        {
            bool sceneLoadRequested = QuestClassroomSceneTravel.RequestEnvironmentScene(environment, "InputManager keyboard shortcut");
            Debug.Log($"[InputManager] Teacher requested scene environment: {environment}, sceneLoadRequested={sceneLoadRequested}");
            return;
        }

        if (environment == ClassroomEnvironment.Default)
        {
            bool sceneLoadRequested = QuestClassroomSceneTravel.RequestClassroomScene("InputManager keyboard shortcut");
            Debug.Log($"[InputManager] Teacher requested classroom scene. sceneLoadRequested={sceneLoadRequested}");
        }

        if (!TryGetSessionState(out ClassroomSessionState sessionState))
            return;

        sessionState.RequestSetEnvironment(environment);
        Debug.Log($"[InputManager] Teacher requested environment: {environment}");
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
}
