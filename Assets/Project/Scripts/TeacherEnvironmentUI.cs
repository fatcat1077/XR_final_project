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
        bool requested = QuestClassroomSceneTravel.RequestEnvironmentScene(classroomEnvironment, "TeacherEnvironmentUI environment button");
        Debug.Log($"[TeacherEnvironmentUI] Requested environment = {type}, requested={requested}");
    }
}
