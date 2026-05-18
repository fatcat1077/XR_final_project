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
        if (environmentManager != null)
            environmentManager.SetEnvironment(EnvironmentType.Default);
    }

    public void OnClickOcean()
    {
        if (environmentManager != null)
            environmentManager.SetEnvironment(EnvironmentType.Ocean);
    }

    public void OnClickSpace()
    {
        if (environmentManager != null)
            environmentManager.SetEnvironment(EnvironmentType.Space);
    }
}