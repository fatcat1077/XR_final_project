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
    [Header("Legacy Environment Roots")]
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
        ApplyEnvironment(CurrentEnvironment);
        Debug.Log($"[EnvironmentManager] SetEnvironment -> {type}");
    }

    private void OnEnvironmentChanged()
    {
        ApplyEnvironment(CurrentEnvironment);
    }

    private void ApplyEnvironment(int envIndex)
    {
        if (envOcean != null)
            envOcean.SetActive(false);

        if (envSpace != null)
            envSpace.SetActive(false);

        EnvironmentContentManager.ApplyEnvironmentLocal((ClassroomEnvironment)envIndex, "legacy EnvironmentManager state");
        Debug.Log($"[EnvironmentManager] ApplyEnvironment -> {(EnvironmentType)envIndex}");
    }
}
