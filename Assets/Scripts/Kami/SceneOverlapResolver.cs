using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

public class SceneOverlapResolver : MonoBehaviour
{
    [Header("Managed Overlaps")]
    [SerializeField] private bool manageMainCameras = true;
    [SerializeField] private bool manageAllEnabledCameras;
    [SerializeField] private bool manageAudioListeners = true;
    [SerializeField] private bool manageEventSystems = true;

    private readonly Dictionary<Behaviour, bool> originalEnabledStates = new();
    private readonly List<Scene> foregroundSceneStack = new();

    private void OnEnable()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
        SceneManager.sceneUnloaded += HandleSceneUnloaded;

        CacheCurrentlyLoadedScenes();
        ResolveForForeground(SceneManager.GetActiveScene());
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneUnloaded -= HandleSceneUnloaded;

        RestoreAllManagedBehaviours();
        foregroundSceneStack.Clear();
    }

    public void ResolveForActiveScene()
    {
        ResolveForForeground(SceneManager.GetActiveScene());
    }

    public void ResolveForForeground(Scene foregroundScene)
    {
        ResolveForForeground(foregroundScene, foregroundScene.name);
    }

    private void ResolveForForeground(Scene foregroundScene, string foregroundSceneName)
    {
        if (string.IsNullOrWhiteSpace(foregroundSceneName))
        {
            foregroundScene = FindFallbackForegroundScene();
            foregroundSceneName = foregroundScene.name;
        }

        if (string.IsNullOrWhiteSpace(foregroundSceneName))
            return;

        if (IsUsableScene(foregroundScene))
            RememberForegroundScene(foregroundScene);

        ResolveMainCameraOverlap(foregroundScene, foregroundSceneName);
        ResolveAudioListenerOverlap(foregroundScene, foregroundSceneName);
        ResolveEventSystemOverlap(foregroundScene, foregroundSceneName);
        RemoveDestroyedManagedBehaviours();
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!IsUsableScene(scene))
            return;

        RememberForegroundScene(scene);
        StartCoroutine(ResolveAfterSceneActivation(scene, scene.name));
    }

    private void HandleSceneUnloaded(Scene scene)
    {
        foregroundSceneStack.Remove(scene);
        RemoveDestroyedManagedBehaviours();
        ResolveForForeground(FindFallbackForegroundScene());
    }

    private IEnumerator ResolveAfterSceneActivation(Scene loadedScene, string loadedSceneName)
    {
        yield return null;

        ResolveForForeground(loadedScene, loadedSceneName);
    }

    private void ResolveMainCameraOverlap(Scene foregroundScene, string foregroundSceneName)
    {
        if (!manageMainCameras)
            return;

        Camera[] cameras = FindObjectsOfType<Camera>(true);
        bool foregroundHasCamera = false;

        foreach (Camera camera in cameras)
        {
            if (IsManagedMainCamera(camera) && IsInScene(camera, foregroundScene, foregroundSceneName))
            {
                foregroundHasCamera = true;
                RestoreManagedBehaviour(camera);
            }
        }

        if (!foregroundHasCamera)
            return;

        foreach (Camera camera in cameras)
        {
            if (IsManagedMainCamera(camera) && !IsInScene(camera, foregroundScene, foregroundSceneName))
                DisableManagedBehaviour(camera, $"foreground scene '{foregroundSceneName}' has a main camera");
        }
    }

    private void ResolveAudioListenerOverlap(Scene foregroundScene, string foregroundSceneName)
    {
        if (!manageAudioListeners)
            return;

        AudioListener[] listeners = FindObjectsOfType<AudioListener>(true);
        bool foregroundHasListener = false;

        foreach (AudioListener listener in listeners)
        {
            if (IsUsableBehaviour(listener) && IsInScene(listener, foregroundScene, foregroundSceneName))
            {
                foregroundHasListener = true;
                RestoreManagedBehaviour(listener);
            }
        }

        if (!foregroundHasListener)
            return;

        foreach (AudioListener listener in listeners)
        {
            if (IsUsableBehaviour(listener) && !IsInScene(listener, foregroundScene, foregroundSceneName))
                DisableManagedBehaviour(listener, $"foreground scene '{foregroundSceneName}' has an AudioListener");
        }
    }

    private void ResolveEventSystemOverlap(Scene foregroundScene, string foregroundSceneName)
    {
        if (!manageEventSystems)
            return;

        EventSystem[] eventSystems = FindObjectsOfType<EventSystem>(true);
        bool foregroundHasEventSystem = false;

        foreach (EventSystem eventSystem in eventSystems)
        {
            if (IsUsableBehaviour(eventSystem) && IsInScene(eventSystem, foregroundScene, foregroundSceneName))
            {
                foregroundHasEventSystem = true;
                RestoreManagedBehaviour(eventSystem);
                RestoreInputModules(eventSystem);
            }
        }

        if (!foregroundHasEventSystem)
            return;

        foreach (EventSystem eventSystem in eventSystems)
        {
            if (IsUsableBehaviour(eventSystem) && !IsInScene(eventSystem, foregroundScene, foregroundSceneName))
            {
                DisableManagedBehaviour(eventSystem, $"foreground scene '{foregroundSceneName}' has an EventSystem");
                DisableInputModules(eventSystem, $"foreground scene '{foregroundSceneName}' has an EventSystem");
            }
        }
    }

    private void DisableInputModules(EventSystem eventSystem, string reason)
    {
        BaseInputModule[] inputModules = eventSystem.GetComponents<BaseInputModule>();

        foreach (BaseInputModule inputModule in inputModules)
            DisableManagedBehaviour(inputModule, reason);
    }

    private void RestoreInputModules(EventSystem eventSystem)
    {
        BaseInputModule[] inputModules = eventSystem.GetComponents<BaseInputModule>();

        foreach (BaseInputModule inputModule in inputModules)
            RestoreManagedBehaviour(inputModule);
    }

    private bool IsManagedMainCamera(Camera camera)
    {
        if (!IsUsableBehaviour(camera))
            return false;

        if (manageAllEnabledCameras)
            return true;

        return camera.CompareTag("MainCamera") ||
            string.Equals(camera.gameObject.name, "Main Camera", System.StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUsableBehaviour(Behaviour behaviour)
    {
        return behaviour != null && behaviour.gameObject.scene.IsValid();
    }

    private static bool IsInScene(Component component, Scene scene, string sceneName)
    {
        if (component == null)
            return false;

        if (IsUsableScene(scene) && component.gameObject.scene == scene)
            return true;

        return IsUnderFusionSceneRoot(component.transform, sceneName);
    }

    private static bool IsUnderFusionSceneRoot(Transform transform, string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
            return false;

        string sceneRootName = $"[{sceneName}]";

        while (transform != null)
        {
            if (string.Equals(transform.name, sceneRootName, System.StringComparison.OrdinalIgnoreCase))
                return true;

            transform = transform.parent;
        }

        return false;
    }

    private void DisableManagedBehaviour(Behaviour behaviour, string reason)
    {
        if (behaviour == null || !behaviour.enabled)
            return;

        if (!originalEnabledStates.ContainsKey(behaviour))
            originalEnabledStates.Add(behaviour, behaviour.enabled);

        behaviour.enabled = false;
        Debug.Log($"[SceneOverlapResolver] Disabled {behaviour.GetType().Name} on '{behaviour.gameObject.name}'. Reason: {reason}.");
    }

    private void RestoreManagedBehaviour(Behaviour behaviour)
    {
        if (behaviour == null || !originalEnabledStates.TryGetValue(behaviour, out bool originalEnabled))
            return;

        behaviour.enabled = originalEnabled;
        originalEnabledStates.Remove(behaviour);
        Debug.Log($"[SceneOverlapResolver] Restored {behaviour.GetType().Name} on '{behaviour.gameObject.name}'.");
    }

    private void RestoreAllManagedBehaviours()
    {
        List<Behaviour> behaviours = new(originalEnabledStates.Keys);

        foreach (Behaviour behaviour in behaviours)
            RestoreManagedBehaviour(behaviour);
    }

    private void RemoveDestroyedManagedBehaviours()
    {
        List<Behaviour> destroyedBehaviours = null;

        foreach (Behaviour behaviour in originalEnabledStates.Keys)
        {
            if (behaviour != null)
                continue;

            destroyedBehaviours ??= new List<Behaviour>();
            destroyedBehaviours.Add(behaviour);
        }

        if (destroyedBehaviours == null)
            return;

        foreach (Behaviour behaviour in destroyedBehaviours)
            originalEnabledStates.Remove(behaviour);
    }

    private void CacheCurrentlyLoadedScenes()
    {
        foregroundSceneStack.Clear();

        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);

            if (IsUsableScene(scene))
                foregroundSceneStack.Add(scene);
        }
    }

    private void RememberForegroundScene(Scene scene)
    {
        if (!IsUsableScene(scene))
            return;

        foregroundSceneStack.Remove(scene);
        foregroundSceneStack.Add(scene);
    }

    private Scene FindFallbackForegroundScene()
    {
        for (int i = foregroundSceneStack.Count - 1; i >= 0; i--)
        {
            Scene scene = foregroundSceneStack[i];

            if (IsUsableScene(scene))
                return scene;

            foregroundSceneStack.RemoveAt(i);
        }

        Scene activeScene = SceneManager.GetActiveScene();
        if (IsUsableScene(activeScene))
            return activeScene;

        return default;
    }

    private static bool IsUsableScene(Scene scene)
    {
        return scene.IsValid() && scene.isLoaded;
    }
}
