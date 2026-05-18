using Fusion;
using UnityEngine;

public enum EnvironmentType
{
    Default = 0,
    Ocean = 1,
    Space = 2
}

public class EnvironmentManager : NetworkBehaviour
{
    [Header("Environment Roots")]
    [SerializeField] private GameObject envDefault;
    [SerializeField] private GameObject envOcean;
    [SerializeField] private GameObject envSpace;

    [Header("Session State")]
    [SerializeField] private ClassroomSessionState sessionState;

    private bool subscribed;

    public override void Spawned()
    {
        BindSessionState();
        ApplyEnvironment(GetCurrentEnvironmentIndex());
    }

    private void Start()
    {
        BindSessionState();
        ApplyEnvironment(GetCurrentEnvironmentIndex());
    }

    private void Update()
    {
        if (!subscribed)
            BindSessionState();
    }

    private void OnDestroy()
    {
        if (sessionState != null)
            sessionState.EnvironmentChanged -= HandleEnvironmentChanged;
    }

    public void SetEnvironment(EnvironmentType type)
    {
        BindSessionState();

        if (sessionState == null)
        {
            Debug.LogWarning("[EnvironmentManager] ClassroomSessionState not found. Applying local environment only.");
            ApplyEnvironment((int)type);
            return;
        }

        if (LocalUserProfile.Role != UserRole.Teacher)
        {
            Debug.LogWarning("[EnvironmentManager] Only Teacher can request environment changes.");
            return;
        }

        sessionState.RequestSetEnvironment(ToClassroomEnvironment(type));
        Debug.Log($"[EnvironmentManager] RequestSetEnvironment -> {type}");
    }

    private void BindSessionState()
    {
        if (sessionState == null)
            sessionState = GetComponent<ClassroomSessionState>();

        if (sessionState == null)
            sessionState = ClassroomSessionState.FindInScene();

        if (sessionState == null || subscribed)
            return;

        sessionState.EnvironmentChanged += HandleEnvironmentChanged;
        subscribed = true;
        ApplyEnvironment((int)sessionState.CurrentEnvironment);

        Debug.Log("[EnvironmentManager] Bound to ClassroomSessionState.");
    }

    private void HandleEnvironmentChanged(ClassroomEnvironment environment)
    {
        ApplyEnvironment((int)environment);
    }

    private int GetCurrentEnvironmentIndex()
    {
        return sessionState != null ? (int)sessionState.CurrentEnvironment : (int)EnvironmentType.Default;
    }

    private void ApplyEnvironment(int envIndex)
    {
        if (envDefault != null)
            envDefault.SetActive(envIndex == (int)EnvironmentType.Default);

        if (envOcean != null)
            envOcean.SetActive(envIndex == (int)EnvironmentType.Ocean);

        if (envSpace != null)
            envSpace.SetActive(envIndex == (int)EnvironmentType.Space);

        Debug.Log($"[EnvironmentManager] ApplyEnvironment -> {(EnvironmentType)envIndex}");
    }

    private static ClassroomEnvironment ToClassroomEnvironment(EnvironmentType type)
    {
        return type switch
        {
            EnvironmentType.Ocean => ClassroomEnvironment.Ocean,
            EnvironmentType.Space => ClassroomEnvironment.Space,
            _ => ClassroomEnvironment.Default
        };
    }
}
