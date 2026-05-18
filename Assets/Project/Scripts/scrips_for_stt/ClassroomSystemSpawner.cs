using Fusion;
using UnityEngine;

public class ClassroomSystemSpawner : MonoBehaviour
{
    [SerializeField] private NetworkRunner runner;
    [SerializeField] private NetworkPrefabRef blackboardManagerPrefab;

    private bool spawned = false;

    private void Update()
    {
        if (spawned || runner == null)
            return;

        if (!runner.IsRunning || !runner.IsServer)
            return;

        runner.Spawn(blackboardManagerPrefab, Vector3.zero, Quaternion.identity);
        spawned = true;

        Debug.Log("[ClassroomSystemSpawner] BlackboardManager spawned.");
    }
}