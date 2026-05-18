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

    [Networked, OnChangedRender(nameof(OnEnvironmentChanged))]
    public int CurrentEnvironment { get; set; }

    public override void Spawned()
    {
        ApplyEnvironment(CurrentEnvironment);
    }

    public void SetEnvironment(EnvironmentType type)
    {
        if (!HasStateAuthority)
        {
            Debug.LogWarning("[EnvironmentManager] No StateAuthority, cannot change environment.");
            return;
        }

        CurrentEnvironment = (int)type;
        ApplyEnvironment(CurrentEnvironment); // Host 本地先立即更新
        Debug.Log($"[EnvironmentManager] SetEnvironment -> {type}");
    }

    private void OnEnvironmentChanged()
    {
        ApplyEnvironment(CurrentEnvironment);
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
}