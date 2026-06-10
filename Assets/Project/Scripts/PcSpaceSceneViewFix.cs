using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class PcSpaceSceneViewFix : MonoBehaviour
{
    private const string SpaceSceneName = "Space";
    private static PcSpaceSceneViewFix instance;

    private readonly Dictionary<Renderer, bool> avatarRendererStates = new();
    private readonly Dictionary<Collider, bool> avatarColliderStates = new();
    private bool wasInSpace;
    private int lastSpaceRefreshFrame;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
#if UNITY_EDITOR || !UNITY_ANDROID
        if (instance != null)
            return;

        GameObject fixerObject = new GameObject("PcSpaceSceneViewFix");
        DontDestroyOnLoad(fixerObject);
        instance = fixerObject.AddComponent<PcSpaceSceneViewFix>();
        instance.ConfigureLoadedSpaceScenes("bootstrap");
#endif
    }

#if UNITY_EDITOR || !UNITY_ANDROID
    private void OnEnable()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
        SceneManager.activeSceneChanged += HandleActiveSceneChanged;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.activeSceneChanged -= HandleActiveSceneChanged;
        RestoreAvatarVisuals("fixer disabled");
    }

    private void Update()
    {
        bool isInSpace = TryGetLoadedSpaceScene(out Scene spaceScene);
        if (isInSpace)
        {
            wasInSpace = true;
            if (Time.frameCount - lastSpaceRefreshFrame > 30)
            {
                lastSpaceRefreshFrame = Time.frameCount;
                ConfigureSpaceScene(spaceScene, "periodic refresh", logResult: false);
            }

            return;
        }

        if (wasInSpace)
        {
            wasInSpace = false;
            RestoreAvatarVisuals("left Space scene");
        }
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (IsSpaceScene(scene))
            StartCoroutine(ConfigureSpaceAfterSceneSettles(scene, $"scene loaded ({mode})"));
    }

    private void HandleActiveSceneChanged(Scene oldScene, Scene newScene)
    {
        if (IsSpaceScene(newScene))
            StartCoroutine(ConfigureSpaceAfterSceneSettles(newScene, "active scene changed"));
        else
            RestoreAvatarVisuals("active scene changed away from Space");
    }

    private void ConfigureLoadedSpaceScenes(string reason)
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (IsSpaceScene(scene))
                StartCoroutine(ConfigureSpaceAfterSceneSettles(scene, reason));
        }
    }

    private IEnumerator ConfigureSpaceAfterSceneSettles(Scene scene, string reason)
    {
        yield return null;
        yield return new WaitForEndOfFrame();
        ConfigureSpaceScene(scene, reason, logResult: true);
    }

    private void ConfigureSpaceScene(Scene scene, string reason, bool logResult)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            return;

        Camera spaceCamera = FindOrCreateSpaceCamera(scene);
        if (spaceCamera == null)
        {
            Debug.LogWarning($"[PcSpaceSceneViewFix] Space scene has no display camera. reason={reason}");
            return;
        }

        SceneManager.SetActiveScene(scene);
        ConfigureDisplayCamera(scene, spaceCamera);
        DisableCompetingDisplayCameras(scene, spaceCamera);
        QuestExternalSceneVisuals.ConfigureSpace(scene, spaceCamera, reason);
        HideAvatarVisualsForSpace();

        if (logResult)
            Debug.Log($"[PcSpaceSceneViewFix] Space scene camera/visuals restored for PC. camera={spaceCamera.name}, reason={reason}");
    }

    private static Camera FindOrCreateSpaceCamera(Scene scene)
    {
        Camera camera = FindPrimarySpaceCamera(scene);
        if (camera != null)
            return camera;

        GameObject cameraObject = new GameObject("Space Main Camera");
        SceneManager.MoveGameObjectToScene(cameraObject, scene);
        cameraObject.tag = "MainCamera";
        cameraObject.transform.SetPositionAndRotation(new Vector3(0f, 1.65f, -5.5f), Quaternion.identity);

        camera = cameraObject.AddComponent<Camera>();
        cameraObject.AddComponent<AudioListener>();
        return camera;
    }

    private static void ConfigureDisplayCamera(Scene scene, Camera spaceCamera)
    {
        if (!spaceCamera.gameObject.activeSelf)
            spaceCamera.gameObject.SetActive(true);

        spaceCamera.enabled = true;
        spaceCamera.tag = "MainCamera";
        spaceCamera.rect = new Rect(0f, 0f, 1f, 1f);
        spaceCamera.targetTexture = null;
        spaceCamera.targetDisplay = 0;
        spaceCamera.stereoTargetEye = StereoTargetEyeMask.Both;
        spaceCamera.depth = 10f;
        spaceCamera.fieldOfView = 70f;
        spaceCamera.nearClipPlane = Mathf.Min(spaceCamera.nearClipPlane, 0.05f);
        spaceCamera.farClipPlane = Mathf.Max(spaceCamera.farClipPlane, 1000f);
        spaceCamera.transform.SetPositionAndRotation(new Vector3(0f, 1.65f, -5.5f), Quaternion.identity);

        if (!spaceCamera.TryGetComponent(out AudioListener listener))
            listener = spaceCamera.gameObject.AddComponent<AudioListener>();

        listener.enabled = true;
    }

    private static void DisableCompetingDisplayCameras(Scene spaceScene, Camera spaceCamera)
    {
        Camera[] cameras = FindObjectsOfType<Camera>(true);
        for (int i = 0; i < cameras.Length; i++)
        {
            Camera camera = cameras[i];
            if (camera == null || camera == spaceCamera)
                continue;

            if (camera.targetTexture != null)
                continue;

            if (camera.CompareTag("MainCamera"))
                camera.tag = "Untagged";

            camera.enabled = false;
            if (camera.TryGetComponent(out AudioListener listener))
                listener.enabled = false;
        }
    }

    private void HideAvatarVisualsForSpace()
    {
        int newlyHidden = 0;

        Renderer[] renderers = FindObjectsOfType<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !IsPlayerAvatarObject(renderer.gameObject))
                continue;

            if (!avatarRendererStates.ContainsKey(renderer))
                avatarRendererStates.Add(renderer, renderer.enabled);

            if (renderer.enabled)
            {
                renderer.enabled = false;
                newlyHidden++;
            }
        }

        Collider[] colliders = FindObjectsOfType<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null || !IsPlayerAvatarObject(collider.gameObject))
                continue;

            if (!avatarColliderStates.ContainsKey(collider))
                avatarColliderStates.Add(collider, collider.enabled);

            collider.enabled = false;
        }

        if (newlyHidden > 0)
            Debug.Log($"[PcSpaceSceneViewFix] Hidden player avatar visuals while Space scene is active. renderers={newlyHidden}");
    }

    private void RestoreAvatarVisuals(string reason)
    {
        if (avatarRendererStates.Count == 0 && avatarColliderStates.Count == 0)
            return;

        foreach (KeyValuePair<Renderer, bool> state in avatarRendererStates)
        {
            if (state.Key != null)
                state.Key.enabled = state.Value;
        }

        foreach (KeyValuePair<Collider, bool> state in avatarColliderStates)
        {
            if (state.Key != null)
                state.Key.enabled = state.Value;
        }

        avatarRendererStates.Clear();
        avatarColliderStates.Clear();
        Debug.Log($"[PcSpaceSceneViewFix] Restored player avatar visuals. reason={reason}");
    }

    private static Camera FindPrimarySpaceCamera(Scene scene)
    {
        Camera fallback = null;
        Camera[] cameras = FindObjectsOfType<Camera>(true);
        for (int i = 0; i < cameras.Length; i++)
        {
            Camera camera = cameras[i];
            if (camera == null || camera.gameObject.scene != scene)
                continue;

            if (string.Equals(camera.gameObject.name, "Space Main Camera", System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(camera.gameObject.name, "Main Camera", System.StringComparison.OrdinalIgnoreCase) ||
                camera.CompareTag("MainCamera"))
                return camera;

            fallback ??= camera;
        }

        return fallback;
    }

    private static bool TryGetLoadedSpaceScene(out Scene spaceScene)
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (IsSpaceScene(scene))
            {
                spaceScene = scene;
                return true;
            }
        }

        spaceScene = default;
        return false;
    }

    private static bool IsSpaceScene(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            return false;

        string scenePath = scene.path.Replace('\\', '/');
        return string.Equals(scene.name, SpaceSceneName, System.StringComparison.OrdinalIgnoreCase) ||
            scenePath.EndsWith("Assets/Scenes/Space.unity", System.StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPlayerAvatarObject(GameObject gameObject)
    {
        Transform current = gameObject != null ? gameObject.transform : null;
        while (current != null)
        {
            string objectName = current.gameObject.name;
            if (objectName.StartsWith("PlayerAvatar_", System.StringComparison.OrdinalIgnoreCase) ||
                objectName.IndexOf("XRPlayerAvatar", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (HasAvatarMarkerComponent(current.gameObject))
                return true;

            current = current.parent;
        }

        return false;
    }

    private static bool HasAvatarMarkerComponent(GameObject gameObject)
    {
        Component[] components = gameObject.GetComponents<Component>();
        for (int i = 0; i < components.Length; i++)
        {
            Component component = components[i];
            if (component == null)
                continue;

            string typeName = component.GetType().FullName;
            if (typeName == "VoiceSetup" ||
                typeName == "Photon.Voice.Fusion.PhotonVoiceView" ||
                typeName == "Photon.Voice.Unity.PhotonVoiceView")
                return true;
        }

        return false;
    }
#endif
}
