using System.Collections;
using Suimono.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class PcOceanSceneViewFix : MonoBehaviour
{
    private const string OceanSceneName = "Ocean";
    private static PcOceanSceneViewFix instance;

    private readonly System.Collections.Generic.Dictionary<Renderer, bool> avatarRendererStates = new();
    private readonly System.Collections.Generic.Dictionary<Collider, bool> avatarColliderStates = new();
    private bool wasInOcean;
    private int lastOceanRefreshFrame;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
#if UNITY_EDITOR || !UNITY_ANDROID
        if (instance != null)
            return;

        GameObject fixerObject = new GameObject("PcOceanSceneViewFix");
        DontDestroyOnLoad(fixerObject);
        instance = fixerObject.AddComponent<PcOceanSceneViewFix>();
        instance.ConfigureLoadedOceanScenes("bootstrap");
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
        bool isInOcean = TryGetLoadedOceanScene(out Scene oceanScene);
        if (isInOcean)
        {
            wasInOcean = true;
            if (Time.frameCount - lastOceanRefreshFrame > 30)
            {
                lastOceanRefreshFrame = Time.frameCount;
                ConfigureOceanScene(oceanScene, "periodic refresh", logResult: false);
            }

            return;
        }

        if (wasInOcean)
        {
            wasInOcean = false;
            RestoreAvatarVisuals("left Ocean scene");
        }
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (IsOceanScene(scene))
            StartCoroutine(ConfigureOceanAfterSceneSettles(scene, $"scene loaded ({mode})"));
    }

    private void HandleActiveSceneChanged(Scene oldScene, Scene newScene)
    {
        if (IsOceanScene(newScene))
            StartCoroutine(ConfigureOceanAfterSceneSettles(newScene, "active scene changed"));
        else
            RestoreAvatarVisuals("active scene changed away from Ocean");
    }

    private void ConfigureLoadedOceanScenes(string reason)
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (IsOceanScene(scene))
                StartCoroutine(ConfigureOceanAfterSceneSettles(scene, reason));
        }
    }

    private IEnumerator ConfigureOceanAfterSceneSettles(Scene scene, string reason)
    {
        yield return null;
        yield return new WaitForEndOfFrame();
        ConfigureOceanScene(scene, reason, logResult: true);
    }

    private void ConfigureOceanScene(Scene scene, string reason, bool logResult)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            return;

        Camera oceanCamera = FindOceanMainCamera(scene);
        if (oceanCamera == null)
        {
            Debug.LogWarning($"[PcOceanSceneViewFix] Ocean scene has no Main Camera. reason={reason}");
            return;
        }

        SceneManager.SetActiveScene(scene);
        ConfigureDisplayCamera(scene, oceanCamera);
        DisableCompetingDisplayCameras(scene, oceanCamera);
        ConfigureSuimono(scene, oceanCamera);
        HideAvatarVisualsForOcean();

        if (logResult)
            Debug.Log($"[PcOceanSceneViewFix] Ocean scene camera restored for PC. camera={oceanCamera.name}, reason={reason}");
    }

    private static void ConfigureDisplayCamera(Scene scene, Camera oceanCamera)
    {
        if (!oceanCamera.gameObject.activeSelf)
            oceanCamera.gameObject.SetActive(true);

        oceanCamera.enabled = true;
        oceanCamera.tag = "MainCamera";
        oceanCamera.rect = new Rect(0f, 0f, 1f, 1f);
        oceanCamera.targetTexture = null;
        oceanCamera.targetDisplay = 0;
        oceanCamera.stereoTargetEye = StereoTargetEyeMask.Both;
        oceanCamera.depth = 10f;
        oceanCamera.fieldOfView = 35f;
        oceanCamera.nearClipPlane = Mathf.Min(oceanCamera.nearClipPlane, 0.05f);
        oceanCamera.farClipPlane = Mathf.Max(oceanCamera.farClipPlane, 10000f);
        oceanCamera.clearFlags = CameraClearFlags.Skybox;
        oceanCamera.transform.SetPositionAndRotation(
            new Vector3(0f, -5f, 0f),
            Quaternion.Euler(4.9f, 0f, 0f));

        if (oceanCamera.TryGetComponent(out AudioListener listener))
            listener.enabled = true;

        if (oceanCamera.TryGetComponent(out Suimono_UnderwaterFog underwaterFog))
            underwaterFog.enabled = true;

        if (oceanCamera.TryGetComponent(out Suimono_DistanceBlur distanceBlur))
        {
            if (distanceBlur.blurShader == null)
                distanceBlur.blurShader = Shader.Find("Hidden/SuimonoDistanceBlur");

            distanceBlur.enabled = distanceBlur.blurShader != null;
        }
    }

    private static void DisableCompetingDisplayCameras(Scene oceanScene, Camera oceanCamera)
    {
        Camera[] cameras = FindObjectsOfType<Camera>(true);
        for (int i = 0; i < cameras.Length; i++)
        {
            Camera camera = cameras[i];
            if (camera == null || camera == oceanCamera)
                continue;

            if (camera.targetTexture != null)
                continue;

            if (camera.gameObject.scene == oceanScene && IsSuimonoHelperCamera(camera))
                continue;

            if (camera.CompareTag("MainCamera"))
                camera.tag = "Untagged";

            camera.enabled = false;
            if (camera.TryGetComponent(out AudioListener listener))
                listener.enabled = false;
        }
    }

    private static void ConfigureSuimono(Scene scene, Camera oceanCamera)
    {
        SuimonoModule module = FindSceneComponent<SuimonoModule>(scene);
        if (module == null)
        {
            Debug.LogWarning("[PcOceanSceneViewFix] SUIMONO_Module component was not found in Ocean scene.");
            return;
        }

        module.cameraTypeIndex = 1;
        module.manualCamera = oceanCamera.transform;
        module.mainCamera = oceanCamera.transform;
        module.setCamera = oceanCamera.transform;
        module.setTrack = oceanCamera.transform;
        module.setCameraComponent = oceanCamera;
        module.autoSetCameraFX = true;
        module.enableUnderwaterFX = true;
        module.objectEnableUnderwaterFX = 1f;

        if (oceanCamera.GetComponent<Suimono_UnderwaterFog>() == null)
            oceanCamera.gameObject.AddComponent<Suimono_UnderwaterFog>();

        Suimono_DistanceBlur distanceBlur = oceanCamera.GetComponent<Suimono_DistanceBlur>();
        if (distanceBlur == null)
            distanceBlur = oceanCamera.gameObject.AddComponent<Suimono_DistanceBlur>();

        if (distanceBlur.blurShader == null)
            distanceBlur.blurShader = Shader.Find("Hidden/SuimonoDistanceBlur");

        distanceBlur.enabled = distanceBlur.blurShader != null;
    }

    private void HideAvatarVisualsForOcean()
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
            Debug.Log($"[PcOceanSceneViewFix] Hidden player avatar visuals while Ocean scene is active. renderers={newlyHidden}");
    }

    private void RestoreAvatarVisuals(string reason)
    {
        if (avatarRendererStates.Count == 0 && avatarColliderStates.Count == 0)
            return;

        foreach (System.Collections.Generic.KeyValuePair<Renderer, bool> state in avatarRendererStates)
        {
            if (state.Key != null)
                state.Key.enabled = state.Value;
        }

        foreach (System.Collections.Generic.KeyValuePair<Collider, bool> state in avatarColliderStates)
        {
            if (state.Key != null)
                state.Key.enabled = state.Value;
        }

        avatarRendererStates.Clear();
        avatarColliderStates.Clear();
        Debug.Log($"[PcOceanSceneViewFix] Restored player avatar visuals. reason={reason}");
    }

    private static Camera FindOceanMainCamera(Scene scene)
    {
        Camera fallback = null;
        Camera[] cameras = FindObjectsOfType<Camera>(true);
        for (int i = 0; i < cameras.Length; i++)
        {
            Camera camera = cameras[i];
            if (camera == null || camera.gameObject.scene != scene || IsSuimonoHelperCamera(camera))
                continue;

            if (string.Equals(camera.gameObject.name, "Main Camera", System.StringComparison.OrdinalIgnoreCase))
                return camera;

            if (camera.CompareTag("MainCamera"))
                return camera;

            fallback ??= camera;
        }

        return fallback;
    }

    private static bool TryGetLoadedOceanScene(out Scene oceanScene)
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (IsOceanScene(scene))
            {
                oceanScene = scene;
                return true;
            }
        }

        oceanScene = default;
        return false;
    }

    private static T FindSceneComponent<T>(Scene scene) where T : Component
    {
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            T component = roots[i].GetComponentInChildren<T>(true);
            if (component != null)
                return component;
        }

        return null;
    }

    private static bool IsOceanScene(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            return false;

        string scenePath = scene.path.Replace('\\', '/');
        return string.Equals(scene.name, OceanSceneName, System.StringComparison.OrdinalIgnoreCase) ||
            scenePath.EndsWith("Assets/Scenes/Ocean.unity", System.StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSuimonoHelperCamera(Camera camera)
    {
        return camera != null &&
            camera.gameObject.name.IndexOf("cam_Suimono", System.StringComparison.OrdinalIgnoreCase) >= 0;
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
