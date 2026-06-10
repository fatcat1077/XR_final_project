using UnityEngine;

public class TeacherEnvironmentUI : MonoBehaviour
{
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private EnvironmentManager environmentManager;

    private void Start()
    {
        bool isTeacher = LocalUserProfile.Role == UserRole.Teacher;

        if (panelRoot != null)
            panelRoot.SetActive(isTeacher);

        Debug.Log($"[TeacherEnvironmentUI] isTeacher = {isTeacher}");
    }

    public void OnClickDefault()
    {
        RequestEnvironment(EnvironmentType.Default);
    }

    public void OnClickOcean()
    {
        RequestEnvironment(EnvironmentType.Ocean);
    }

    public void OnClickSpace()
    {
        RequestEnvironment(EnvironmentType.Space);
    }

    private void RequestEnvironment(EnvironmentType type)
    {
        ClassroomEnvironment classroomEnvironment = (ClassroomEnvironment)(int)type;

        if (classroomEnvironment == ClassroomEnvironment.Ocean || classroomEnvironment == ClassroomEnvironment.Space)
        {
            bool sceneLoadRequested = QuestClassroomSceneTravel.RequestEnvironmentScene(classroomEnvironment, "TeacherEnvironmentUI environment button");
            Debug.Log($"[TeacherEnvironmentUI] Requested scene environment = {type}, sceneLoadRequested={sceneLoadRequested}");
            return;
        }

        if (ClassroomSessionState.TryGetActiveNetworked(out ClassroomSessionState sessionState))
            sessionState.RequestSetEnvironment(classroomEnvironment);

        if (environmentManager != null)
            environmentManager.SetEnvironment(type);

        EM_test[] localEnvironmentViews = FindObjectsOfType<EM_test>();
        for (int i = 0; i < localEnvironmentViews.Length; i++)
            localEnvironmentViews[i].HandleEnvironmentChanged(classroomEnvironment);

        Debug.Log($"[TeacherEnvironmentUI] Requested environment = {type}");
    }
}
