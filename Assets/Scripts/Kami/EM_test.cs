using UnityEngine;

public class EM_test : MonoBehaviour 
{
    [Header("Environment Roots")]
    [SerializeField] private GameObject envDefault;
    [SerializeField] private GameObject envOcean;
    [SerializeField] private GameObject envSpace;

    private bool _isInitialized = false;

    public void HandleEnvironmentChanged(ClassroomEnvironment env)
    {
        Debug.Log($"[EM_test] 收到環境通知: {env}");
        ApplyEnvironment(env);
    }

    private void ApplyEnvironment(ClassroomEnvironment env)
    {
        if (envDefault != null) envDefault.SetActive(env == ClassroomEnvironment.Default);
        if (envOcean != null) envOcean.SetActive(env == ClassroomEnvironment.Ocean);
        if (envSpace != null) envSpace.SetActive(env == ClassroomEnvironment.Space);
    }
    
    private void OnDestroy()
    {
        if (_isInitialized && ClassroomSessionState.Instance != null)
        {
            ClassroomSessionState.Instance.OnEnvironmentChanged.RemoveListener(HandleEnvironmentChanged);
        }
    }
}