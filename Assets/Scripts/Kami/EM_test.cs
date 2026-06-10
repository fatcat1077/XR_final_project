using UnityEngine;

public class EM_test : MonoBehaviour
{
    [Header("Legacy Environment Roots")]
    [SerializeField] private GameObject envDefault;
    [SerializeField] private GameObject envOcean;
    [SerializeField] private GameObject envSpace;

    private bool isInitialized;

    public void HandleEnvironmentChanged(ClassroomEnvironment environment)
    {
        ApplyEnvironment(environment);
    }

    private void ApplyEnvironment(ClassroomEnvironment environment)
    {
        if (envOcean != null)
            envOcean.SetActive(false);

        if (envSpace != null)
            envSpace.SetActive(false);

        EnvironmentContentManager.ApplyEnvironmentLocal(environment, "legacy EM_test state");
        Debug.Log($"[EM_test] Environment -> {environment}");
    }

    private void OnDestroy()
    {
        if (isInitialized && ClassroomSessionState.Instance != null)
            ClassroomSessionState.Instance.OnEnvironmentChanged.RemoveListener(HandleEnvironmentChanged);
    }
}
