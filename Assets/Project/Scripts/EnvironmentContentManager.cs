using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

public sealed class EnvironmentContentManager : MonoBehaviour
{
    private const string ManagerObjectName = "RuntimeEnvironmentContentManager";
    private const string GeneratedRootName = "RuntimeEnvironmentContent";
    private const string CameraRootName = "RuntimeEnvironmentCameraContent";

    private static readonly string[] LegacyEnvironmentRootNames =
    {
        "Env_Ocean",
        "Ocean",
        "OceanBackground",
        "OceanFloor",
        "SUIMONO_Surface",
        "SUIMONO_Module",
        "Suimono_Object",
        "Suimono_ObjectScale",
        "_particle_effects",
        "_caustic_effects",
        "_sound_effects",
        "mainCausticObject",
        "cam_SuimonoCaustic",
        "cam_SuimonoNormals",
        "cam_SuimonoWake",
        "cam_SuimonoTrans",
        "cam_SuimonoShoreline",
        "Env_star",
        "star"
    };

    private static EnvironmentContentManager instance;

    private readonly List<GameObject> hiddenLegacyObjects = new();
    private readonly Dictionary<Camera, CameraSnapshot> cameraSnapshots = new();
    private readonly Dictionary<Renderer, bool> coveredRendererStates = new();
    private readonly Dictionary<Collider, bool> coveredColliderStates = new();
    private readonly Dictionary<Light, bool> coveredLightStates = new();

    private GameObject contentRoot;
    private GameObject cameraRoot;
    private ClassroomSessionState boundSessionState;
    private Coroutine bindRoutine;
    private bool hasCurrentEnvironment;
    private ClassroomEnvironment currentEnvironment;
    private RenderSettingsSnapshot renderSettingsSnapshot;
    private bool renderSettingsCaptured;

    public static EnvironmentContentManager Instance => instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        EnsureForScene(SceneManager.GetActiveScene(), "bootstrap");
    }

    public static bool ApplyEnvironmentLocal(ClassroomEnvironment environment, string reason)
    {
        if (instance == null)
            EnsureForScene(SceneManager.GetActiveScene(), reason);

        if (instance == null)
            return false;

        instance.ApplyEnvironment(environment, reason, forceRebuild: false);
        return true;
    }

    public static bool RebuildEnvironmentLocal(ClassroomEnvironment environment, string reason)
    {
        if (instance == null)
            EnsureForScene(SceneManager.GetActiveScene(), reason);

        if (instance == null)
            return false;

        instance.ApplyEnvironment(environment, reason, forceRebuild: true);
        return true;
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EnsureForScene(scene, $"scene loaded ({mode})");
    }

    private static void EnsureForScene(Scene scene, string reason)
    {
        if (!ShouldManageScene(scene))
            return;

        EnvironmentContentManager existing = FindExistingManager(scene);
        if (existing != null)
        {
            instance = existing;
            return;
        }

        GameObject managerObject = new GameObject(ManagerObjectName);
        SceneManager.MoveGameObjectToScene(managerObject, scene);
        instance = managerObject.AddComponent<EnvironmentContentManager>();
        Debug.Log($"[EnvironmentContentManager] Created runtime manager in scene '{scene.name}'. reason={reason}");
    }

    private static EnvironmentContentManager FindExistingManager(Scene scene)
    {
        if (scene.IsValid() && scene.isLoaded)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                EnvironmentContentManager manager = roots[i].GetComponentInChildren<EnvironmentContentManager>(true);
                if (manager != null)
                    return manager;
            }
        }

        EnvironmentContentManager[] managers = FindObjectsOfType<EnvironmentContentManager>(true);
        return managers.Length > 0 ? managers[0] : null;
    }

    private static bool ShouldManageScene(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            return false;

        string scenePath = scene.path.Replace('\\', '/');
        if (scene.name.IndexOf("Lobby", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            scenePath.EndsWith("/Test_lobby.unity", System.StringComparison.OrdinalIgnoreCase) ||
            scenePath.EndsWith("/Lobby.unity", System.StringComparison.OrdinalIgnoreCase))
            return false;

        return scene.name.IndexOf("Classroom", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            scenePath.EndsWith("/Test_classroom.unity", System.StringComparison.OrdinalIgnoreCase) ||
            scenePath.EndsWith("/Classroom.unity", System.StringComparison.OrdinalIgnoreCase);
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        CaptureRenderSettings();
        HideLegacyEnvironmentRoots();
    }

    private void OnEnable()
    {
        bindRoutine = StartCoroutine(BindSessionStateRoutine());
    }

    private void OnDisable()
    {
        if (bindRoutine != null)
        {
            StopCoroutine(bindRoutine);
            bindRoutine = null;
        }

        UnbindSessionState();
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;

        RestoreCoveredClassroomVisuals();
        RestoreCameraSnapshots();
        RestoreRenderSettings();
        CleanupGeneratedContent();
        UnbindSessionState();
    }

    private IEnumerator BindSessionStateRoutine()
    {
        while (isActiveAndEnabled)
        {
            ClassroomSessionState sessionState = ClassroomSessionState.Instance;
            if (sessionState == null || sessionState.Object == null || !sessionState.Object.IsValid)
                ClassroomSessionState.TryGetActiveNetworked(out sessionState);

            if (sessionState != boundSessionState)
            {
                UnbindSessionState();
                boundSessionState = sessionState;

                if (boundSessionState != null)
                {
                    boundSessionState.OnEnvironmentChanged.AddListener(HandleNetworkEnvironmentChanged);
                    ApplyEnvironment(boundSessionState.CurrentEnvironment, "session state bound", forceRebuild: false);
                    Debug.Log("[EnvironmentContentManager] Bound to ClassroomSessionState.");
                }
            }

            if (boundSessionState == null && !hasCurrentEnvironment)
                ApplyEnvironment(ClassroomEnvironment.Default, "no session state yet", forceRebuild: false);

            yield return new WaitForSeconds(0.5f);
        }
    }

    private void UnbindSessionState()
    {
        if (boundSessionState != null)
            boundSessionState.OnEnvironmentChanged.RemoveListener(HandleNetworkEnvironmentChanged);

        boundSessionState = null;
    }

    private void HandleNetworkEnvironmentChanged(ClassroomEnvironment environment)
    {
        ApplyEnvironment(environment, "network state changed", forceRebuild: false);
    }

    private void ApplyEnvironment(ClassroomEnvironment environment, string reason, bool forceRebuild)
    {
        HideLegacyEnvironmentRoots();

        if (!forceRebuild && hasCurrentEnvironment && currentEnvironment == environment && contentRoot != null)
        {
            if (environment == ClassroomEnvironment.Default)
                RestoreCoveredClassroomVisuals();
            else
                CoverClassroomVisualsForImmersiveEnvironment(environment);

            return;
        }

        CleanupGeneratedContent();
        RestoreCameraSnapshots();
        RestoreRenderSettings();
        RestoreCoveredClassroomVisuals();

        hasCurrentEnvironment = true;
        currentEnvironment = environment;

        contentRoot = new GameObject($"{GeneratedRootName}_{environment}");
        SceneManager.MoveGameObjectToScene(contentRoot, gameObject.scene);
        contentRoot.transform.SetParent(transform, false);

        Camera displayCamera = ResolveDisplayCamera();
        if (displayCamera != null)
            CaptureCameraSnapshot(displayCamera);

        switch (environment)
        {
            case ClassroomEnvironment.Ocean:
                CoverClassroomVisualsForImmersiveEnvironment(environment);
                BuildOceanEnvironment(displayCamera);
                break;
            case ClassroomEnvironment.Space:
                CoverClassroomVisualsForImmersiveEnvironment(environment);
                BuildSpaceEnvironment(displayCamera);
                break;
            default:
                BuildDefaultEnvironment(displayCamera);
                break;
        }

        Debug.Log($"[EnvironmentContentManager] Applied environment={environment}. reason={reason}");
    }

    private void CleanupGeneratedContent()
    {
        if (contentRoot != null)
        {
            Destroy(contentRoot);
            contentRoot = null;
        }

        if (cameraRoot != null)
        {
            Destroy(cameraRoot);
            cameraRoot = null;
        }
    }

    private void HideLegacyEnvironmentRoots()
    {
        Transform[] transforms = FindObjectsOfType<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform target = transforms[i];
            if (target == null || target.gameObject.scene != gameObject.scene)
                continue;

            if (!IsLegacyEnvironmentRootName(target.name))
                continue;

            if (target.GetComponentInParent<EnvironmentContentManager>() != null)
                continue;

            GameObject legacyObject = target.gameObject;
            if (!hiddenLegacyObjects.Contains(legacyObject))
                hiddenLegacyObjects.Add(legacyObject);

            if (legacyObject.activeSelf)
                legacyObject.SetActive(false);
        }
    }

    private static bool IsLegacyEnvironmentRootName(string objectName)
    {
        if (string.IsNullOrWhiteSpace(objectName))
            return false;

        for (int i = 0; i < LegacyEnvironmentRootNames.Length; i++)
        {
            if (string.Equals(objectName, LegacyEnvironmentRootNames[i], System.StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private void CoverClassroomVisualsForImmersiveEnvironment(ClassroomEnvironment environment)
    {
        int rendererCount = 0;
        int colliderCount = 0;
        int lightCount = 0;

        Renderer[] renderers = FindObjectsOfType<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null ||
                renderer.gameObject.scene != gameObject.scene ||
                ShouldPreserveSceneObject(renderer.gameObject))
                continue;

            if (!coveredRendererStates.ContainsKey(renderer))
                coveredRendererStates.Add(renderer, renderer.enabled);

            if (renderer.enabled)
            {
                renderer.enabled = false;
                rendererCount++;
            }
        }

        Collider[] colliders = FindObjectsOfType<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null ||
                collider.gameObject.scene != gameObject.scene ||
                ShouldPreserveSceneObject(collider.gameObject))
                continue;

            if (!coveredColliderStates.ContainsKey(collider))
                coveredColliderStates.Add(collider, collider.enabled);

            if (collider.enabled)
            {
                collider.enabled = false;
                colliderCount++;
            }
        }

        Light[] lights = FindObjectsOfType<Light>(true);
        for (int i = 0; i < lights.Length; i++)
        {
            Light light = lights[i];
            if (light == null ||
                light.gameObject.scene != gameObject.scene ||
                ShouldPreserveSceneObject(light.gameObject))
                continue;

            if (!coveredLightStates.ContainsKey(light))
                coveredLightStates.Add(light, light.enabled);

            if (light.enabled)
            {
                light.enabled = false;
                lightCount++;
            }
        }

        if (rendererCount > 0 || colliderCount > 0 || lightCount > 0)
        {
            Debug.Log($"[EnvironmentContentManager] Covered classroom visuals for {environment}. renderers={rendererCount}, colliders={colliderCount}, lights={lightCount}");
        }
    }

    private void RestoreCoveredClassroomVisuals()
    {
        foreach (KeyValuePair<Renderer, bool> state in coveredRendererStates)
        {
            if (state.Key != null)
                state.Key.enabled = state.Value;
        }

        foreach (KeyValuePair<Collider, bool> state in coveredColliderStates)
        {
            if (state.Key != null)
                state.Key.enabled = state.Value;
        }

        foreach (KeyValuePair<Light, bool> state in coveredLightStates)
        {
            if (state.Key != null)
                state.Key.enabled = state.Value;
        }

        coveredRendererStates.Clear();
        coveredColliderStates.Clear();
        coveredLightStates.Clear();
    }

    private bool ShouldPreserveSceneObject(GameObject target)
    {
        if (target == null)
            return true;

        Transform current = target.transform;
        while (current != null)
        {
            GameObject currentObject = current.gameObject;
            if (currentObject == gameObject ||
                current.IsChildOf(transform) ||
                IsRuntimeEnvironmentObject(currentObject) ||
                IsPersistentPlayerOrSystemObject(currentObject))
                return true;

            current = current.parent;
        }

        return false;
    }

    private static bool IsRuntimeEnvironmentObject(GameObject target)
    {
        if (target == null)
            return false;

        return target.name.StartsWith(GeneratedRootName, System.StringComparison.OrdinalIgnoreCase) ||
            target.name.StartsWith(CameraRootName, System.StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPersistentPlayerOrSystemObject(GameObject target)
    {
        if (target == null)
            return false;

        string objectName = target.name;
        if (objectName.StartsWith("PlayerAvatar_", System.StringComparison.OrdinalIgnoreCase) ||
            objectName.IndexOf("XRPlayerAvatar", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            objectName.StartsWith("Quest", System.StringComparison.OrdinalIgnoreCase) ||
            objectName.StartsWith("Runtime", System.StringComparison.OrdinalIgnoreCase) ||
            string.Equals(objectName, "Main Camera", System.StringComparison.OrdinalIgnoreCase) ||
            string.Equals(objectName, "EventSystem", System.StringComparison.OrdinalIgnoreCase) ||
            string.Equals(objectName, "VoiceManager", System.StringComparison.OrdinalIgnoreCase) ||
            string.Equals(objectName, "NetworkRunnerObject", System.StringComparison.OrdinalIgnoreCase) ||
            string.Equals(objectName, "TeacherSpawnPoint", System.StringComparison.OrdinalIgnoreCase) ||
            string.Equals(objectName, "StudentSpawnPoint", System.StringComparison.OrdinalIgnoreCase))
            return true;

        if (target.GetComponent<Camera>() != null ||
            target.GetComponent<Canvas>() != null ||
            target.GetComponent<EventSystem>() != null)
            return true;

        MonoBehaviour[] behaviours = target.GetComponents<MonoBehaviour>();
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour == null)
                continue;

            string typeName = behaviour.GetType().Name;
            if (typeName == "VoiceSetup" ||
                typeName == "VoiceNetworkObject" ||
                typeName == "FusionVoiceClient" ||
                typeName == "Recorder" ||
                typeName == "Speaker" ||
                typeName == "NetworkRunner" ||
                typeName == "NetworkObject" ||
                typeName == "PlayerSpawner" ||
                typeName == "FusionLauncher" ||
                typeName == "ClassroomSessionState" ||
                typeName == "EnvironmentContentManager" ||
                typeName == "QuestScenePointer" ||
                typeName == "QuestLobbyPointer" ||
                typeName == "SpeechRecorder" ||
                typeName == "SpeechToTextClient" ||
                typeName == "SharedMicrophoneCapture")
                return true;
        }

        return false;
    }

    private void BuildDefaultEnvironment(Camera displayCamera)
    {
        RestoreCoveredClassroomVisuals();
        RestoreRenderSettings();
        RestoreCameraSnapshots();

        if (displayCamera != null)
        {
            displayCamera.clearFlags = CameraClearFlags.Skybox;
            displayCamera.backgroundColor = new Color(0.56f, 0.66f, 0.78f, 1f);
        }

        GameObject marker = new GameObject("DefaultClassroomEnvironmentMarker");
        marker.transform.SetParent(contentRoot.transform, false);
    }

    private void BuildOceanEnvironment(Camera displayCamera)
    {
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.01f, 0.28f, 0.38f, 1f);
        RenderSettings.fogDensity = 0.026f;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.025f, 0.28f, 0.32f, 1f);

        if (displayCamera != null)
        {
            displayCamera.clearFlags = CameraClearFlags.SolidColor;
            displayCamera.backgroundColor = new Color(0.002f, 0.035f, 0.08f, 1f);
            displayCamera.nearClipPlane = Mathf.Min(displayCamera.nearClipPlane, 0.05f);
            displayCamera.farClipPlane = Mathf.Max(displayCamera.farClipPlane, 1000f);
        }

        Transform overlay = CreateCameraOverlayRoot(displayCamera, "OceanCameraOverlay");

        Material backdropMaterial = CreateUnlitMaterial(new Color(0.015f, 0.22f, 0.34f, 1f), false);
        backdropMaterial.mainTexture = CreateOceanBackdropTexture();
        Material waterMaterial = CreateUnlitMaterial(new Color(0.12f, 0.95f, 1f, 0.72f), true);
        waterMaterial.mainTexture = CreateWaterTexture();
        Material shimmerMaterial = CreateUnlitMaterial(new Color(0.82f, 1f, 0.94f, 0.42f), true);
        shimmerMaterial.mainTexture = CreateCausticTexture();
        Material bubbleMaterial = CreateUnlitMaterial(new Color(0.72f, 0.98f, 1f, 0.52f), true);
        Material floorMaterial = CreateUnlitMaterial(new Color(0.02f, 0.38f, 0.34f, 1f), false);

        CreateQuad("OceanBackdrop", overlay, new Vector3(0f, 0f, 14f), new Vector2(54f, 32f), backdropMaterial);
        CreateQuad("WaterSurface", overlay, new Vector3(0f, 3.2f, 8f), new Vector2(42f, 7.5f), waterMaterial);
        CreateQuad("CausticShimmer", overlay, new Vector3(0f, -0.4f, 5.6f), new Vector2(38f, 13f), shimmerMaterial);
        CreateQuad("OceanFloorCurtain", overlay, new Vector3(0f, -4.6f, 7.8f), new Vector2(48f, 8f), floorMaterial);

        GameObject driftObject = new GameObject("OceanMaterialDrift");
        driftObject.transform.SetParent(contentRoot.transform, false);
        driftObject.AddComponent<RuntimeMaterialDrift>().Initialize(waterMaterial, shimmerMaterial);

        for (int i = 0; i < 110; i++)
        {
            float x = Mathf.Lerp(-14.5f, 14.5f, Halton(i + 1, 2));
            float y = Mathf.Lerp(-5.2f, 5.4f, Halton(i + 1, 3));
            float z = Mathf.Lerp(3.6f, 13.5f, Halton(i + 1, 5));
            float size = Mathf.Lerp(0.025f, 0.11f, Halton(i + 1, 7));
            CreateQuad($"Bubble_{i:00}", overlay, new Vector3(x, y, z), new Vector2(size, size), bubbleMaterial);
        }

        Color[] fishColors =
        {
            new Color(0.25f, 0.88f, 1f, 1f),
            new Color(0.95f, 0.68f, 0.28f, 1f),
            new Color(0.98f, 0.88f, 0.42f, 1f),
            new Color(0.45f, 0.75f, 1f, 1f)
        };

        for (int i = 0; i < 22; i++)
        {
            Vector3 basePosition = new Vector3(
                Mathf.Lerp(-9.2f, 9.2f, Halton(i + 3, 2)),
                Mathf.Lerp(-2.7f, 2.5f, Halton(i + 6, 3)),
                Mathf.Lerp(4.5f, 10.5f, Halton(i + 9, 5)));

            CreateFish(overlay, $"OceanFish_{i:00}", basePosition, Mathf.Lerp(0.2f, 0.48f, Halton(i + 4, 7)), fishColors[i % fishColors.Length]);
        }

        CreateLargeFish(overlay, "LargeFish_LeftPass", true, 0f, 1.75f, 4.7f, 0.85f);
        CreateLargeFish(overlay, "LargeFish_RightPass", false, 4.2f, 1.45f, 6.1f, -0.35f);
    }

    private void BuildSpaceEnvironment(Camera displayCamera)
    {
        RenderSettings.fog = false;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.035f, 0.04f, 0.085f, 1f);

        if (displayCamera != null)
        {
            displayCamera.clearFlags = CameraClearFlags.SolidColor;
            displayCamera.backgroundColor = new Color(0.005f, 0.007f, 0.018f, 1f);
            displayCamera.nearClipPlane = Mathf.Min(displayCamera.nearClipPlane, 0.05f);
            displayCamera.farClipPlane = Mathf.Max(displayCamera.farClipPlane, 1000f);
        }

        Transform overlay = CreateCameraOverlayRoot(displayCamera, "SpaceCameraOverlay");
        Material backdropMaterial = CreateUnlitMaterial(new Color(0.006f, 0.007f, 0.02f, 1f), false);
        backdropMaterial.mainTexture = CreateSpaceTexture();
        Material starMaterial = CreateUnlitMaterial(Color.white, false);

        CreateQuad("SpaceBackdrop", overlay, new Vector3(0f, 0f, 14f), new Vector2(54f, 32f), backdropMaterial);

        for (int i = 0; i < 220; i++)
        {
            float x = Mathf.Lerp(-18f, 18f, Halton(i + 1, 2));
            float y = Mathf.Lerp(-9f, 9f, Halton(i + 1, 3));
            float z = Mathf.Lerp(4.5f, 15f, Halton(i + 1, 5));
            float size = Mathf.Lerp(0.018f, 0.085f, Halton(i + 1, 7));
            CreateQuad($"Star_{i:000}", overlay, new Vector3(x, y, z), new Vector2(size, size), starMaterial);
        }

        GameObject planet = CreateSphere("DistantPlanet", overlay, new Vector3(4.8f, 2.25f, 9.2f), Vector3.one * 2.25f, new Color(0.22f, 0.48f, 1f, 1f));
        planet.AddComponent<RuntimeSlowSpin>().Initialize(new Vector3(0f, 18f, 0f));

        CreateSphere("WarmMoon", overlay, new Vector3(-4.4f, -1.45f, 8.6f), Vector3.one * 0.96f, new Color(0.95f, 0.72f, 0.42f, 1f));
        CreateRing(overlay, new Vector3(4.8f, 2.25f, 9.15f), Quaternion.Euler(72f, 0f, 18f), new Color(0.55f, 0.75f, 1f, 0.56f));
    }

    private Transform CreateCameraOverlayRoot(Camera displayCamera, string name)
    {
        cameraRoot = new GameObject($"{CameraRootName}_{name}");
        SetNonInteractive(cameraRoot);

        if (displayCamera != null)
        {
            cameraRoot.transform.SetParent(displayCamera.transform, false);
            cameraRoot.transform.localPosition = Vector3.zero;
            cameraRoot.transform.localRotation = Quaternion.identity;
        }
        else
        {
            SceneManager.MoveGameObjectToScene(cameraRoot, gameObject.scene);
            cameraRoot.transform.SetParent(contentRoot.transform, false);
        }

        return cameraRoot.transform;
    }

    private static Transform CreateQuad(string name, Transform parent, Vector3 localPosition, Vector2 size, Material material)
    {
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = name;
        SetNonInteractive(quad);
        quad.transform.SetParent(parent, false);
        quad.transform.localPosition = localPosition;
        quad.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        quad.transform.localScale = new Vector3(size.x, size.y, 1f);

        if (quad.TryGetComponent(out Renderer renderer))
            renderer.sharedMaterial = material;

        return quad.transform;
    }

    private static GameObject CreateSphere(string name, Transform parent, Vector3 localPosition, Vector3 localScale, Color color)
    {
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = name;
        SetNonInteractive(sphere);
        sphere.transform.SetParent(parent, false);
        sphere.transform.localPosition = localPosition;
        sphere.transform.localScale = localScale;

        if (sphere.TryGetComponent(out Renderer renderer))
            renderer.sharedMaterial = CreateUnlitMaterial(color, false);

        return sphere;
    }

    private static void CreateFish(Transform parent, string name, Vector3 localPosition, float scale, Color color)
    {
        GameObject fish = new GameObject(name);
        SetNonInteractive(fish);
        fish.transform.SetParent(parent, false);
        fish.transform.localPosition = localPosition;
        fish.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        fish.transform.localScale = Vector3.one * scale;

        Material bodyMaterial = CreateUnlitMaterial(color, false);
        Material finMaterial = CreateUnlitMaterial(Color.Lerp(color, Color.white, 0.25f), false);

        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        body.name = "Body";
        SetNonInteractive(body);
        body.transform.SetParent(fish.transform, false);
        body.transform.localScale = new Vector3(0.95f, 0.32f, 0.18f);
        if (body.TryGetComponent(out Renderer bodyRenderer))
            bodyRenderer.sharedMaterial = bodyMaterial;

        GameObject tail = new GameObject("Tail");
        SetNonInteractive(tail);
        tail.transform.SetParent(fish.transform, false);
        tail.transform.localPosition = new Vector3(-0.55f, 0f, 0f);
        tail.transform.localScale = new Vector3(0.52f, 0.46f, 1f);
        MeshFilter tailFilter = tail.AddComponent<MeshFilter>();
        MeshRenderer tailRenderer = tail.AddComponent<MeshRenderer>();
        tailFilter.sharedMesh = CreateTriangleMesh();
        tailRenderer.sharedMaterial = finMaterial;

        fish.AddComponent<RuntimeFishSwim>().Initialize(localPosition, Random.Range(0.35f, 0.8f), Random.Range(0.45f, 1.4f), Random.Range(0.06f, 0.22f), Random.Range(0f, Mathf.PI * 2f));
    }

    private static void CreateLargeFish(Transform parent, string name, bool leftToRight, float phase, float scale, float depth, float height)
    {
        GameObject fish = new GameObject(name);
        SetNonInteractive(fish);
        fish.transform.SetParent(parent, false);
        fish.transform.localScale = Vector3.one * scale;
        fish.AddComponent<RuntimeLargeFishPass>().Initialize(leftToRight, phase, depth, height);

        Material bodyMaterial = CreateUnlitMaterial(new Color(0.96f, 0.22f, 0.08f, 1f), false);
        Material finMaterial = CreateUnlitMaterial(new Color(0.08f, 0.86f, 0.88f, 1f), false);

        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        body.name = "Body";
        SetNonInteractive(body);
        body.transform.SetParent(fish.transform, false);
        body.transform.localScale = new Vector3(1.15f, 0.36f, 0.24f);
        if (body.TryGetComponent(out Renderer bodyRenderer))
            bodyRenderer.sharedMaterial = bodyMaterial;

        GameObject tail = new GameObject("Tail");
        SetNonInteractive(tail);
        tail.transform.SetParent(fish.transform, false);
        tail.transform.localPosition = new Vector3(-0.7f, 0f, 0f);
        tail.transform.localScale = new Vector3(0.72f, 0.72f, 1f);
        MeshFilter tailFilter = tail.AddComponent<MeshFilter>();
        MeshRenderer tailRenderer = tail.AddComponent<MeshRenderer>();
        tailFilter.sharedMesh = CreateTriangleMesh();
        tailRenderer.sharedMaterial = finMaterial;
    }

    private static void CreateRing(Transform parent, Vector3 localPosition, Quaternion localRotation, Color color)
    {
        GameObject ring = new GameObject("PlanetRing");
        SetNonInteractive(ring);
        ring.transform.SetParent(parent, false);
        ring.transform.localPosition = localPosition;
        ring.transform.localRotation = localRotation;

        MeshFilter meshFilter = ring.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = ring.AddComponent<MeshRenderer>();
        meshFilter.sharedMesh = CreateRingMesh(1.1f, 1.7f, 72);
        meshRenderer.sharedMaterial = CreateUnlitMaterial(color, true);
    }

    private static Material CreateUnlitMaterial(Color color, bool transparent)
    {
        Shader shader = ResolveRuntimeShader();
        if (shader == null)
        {
            Debug.LogError("[EnvironmentContentManager] Could not resolve a runtime shader for generated environment content.");
            return null;
        }

        Material material = new Material(shader);
        ApplyMaterialColor(material, color);

        if (transparent)
        {
            if (material.HasProperty("_Surface"))
                material.SetFloat("_Surface", 1f);

            if (material.HasProperty("_Blend"))
                material.SetFloat("_Blend", 0f);

            if (material.HasProperty("_SrcBlend"))
                material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);

            if (material.HasProperty("_DstBlend"))
                material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);

            if (material.HasProperty("_ZWrite"))
                material.SetInt("_ZWrite", 0);

            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        return material;
    }

    private static Shader ResolveRuntimeShader()
    {
        string[] shaderNames =
        {
            "Sprites/Default",
            "Unlit/Texture",
            "Unlit/Color",
            "Universal Render Pipeline/Unlit",
            "Mobile/Unlit (Supports Lightmap)",
            "Mobile/Diffuse",
            "Hidden/Internal-Colored",
            "Standard"
        };

        for (int i = 0; i < shaderNames.Length; i++)
        {
            Shader shader = Shader.Find(shaderNames[i]);
            if (shader != null)
                return shader;
        }

        return null;
    }

    private static void ApplyMaterialColor(Material material, Color color)
    {
        if (material == null)
            return;

        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);

        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
    }

    private static Texture2D CreateWaterTexture()
    {
        const int width = 256;
        const int height = 256;
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = "RuntimeOceanWaterTexture",
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear
        };

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float u = x / (float)width;
                float v = y / (float)height;
                float waveA = Mathf.Sin((u * 22f + Mathf.Sin(v * 7.5f) * 1.2f) * Mathf.PI);
                float waveB = Mathf.Sin((u * 11f - v * 6.5f) * Mathf.PI);
                float noise = Mathf.PerlinNoise(u * 7.5f, v * 12.5f);
                float mask = Mathf.SmoothStep(0.42f, 0.92f, Mathf.Abs(waveA * 0.55f + waveB * 0.28f) + noise * 0.34f);
                Color dark = new Color(0.02f, 0.26f, 0.32f, 0.65f);
                Color bright = new Color(0.18f, 1f, 0.9f, 0.95f);
                texture.SetPixel(x, y, Color.Lerp(dark, bright, mask));
            }
        }

        texture.Apply(false, true);
        return texture;
    }

    private static Texture2D CreateCausticTexture()
    {
        const int width = 256;
        const int height = 128;
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = "RuntimeOceanCausticTexture",
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear
        };

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float u = x / (float)width;
                float v = y / (float)height;
                float line = Mathf.Sin((u * 18f + v * 4.5f + Mathf.PerlinNoise(u * 5f, v * 6f)) * Mathf.PI);
                float mask = Mathf.Pow(Mathf.Clamp01(line * 0.5f + 0.5f), 5f);
                texture.SetPixel(x, y, new Color(0.82f, 1f, 0.88f, mask * 0.58f));
            }
        }

        texture.Apply(false, true);
        return texture;
    }

    private static Texture2D CreateOceanBackdropTexture()
    {
        const int width = 256;
        const int height = 160;
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = "RuntimeOceanBackdropTexture",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float u = x / (float)width;
                float v = y / (float)height;
                float depth = Mathf.SmoothStep(0f, 1f, v);
                float glowNoise = Mathf.PerlinNoise(u * 3f + 4.7f, v * 2.1f + 12.3f);
                float flecks = Mathf.PerlinNoise(u * 82f + 0.2f, v * 82f + 3.8f);
                Color deep = new Color(0.002f, 0.025f, 0.085f, 1f);
                Color teal = new Color(0.01f, 0.22f, 0.36f, 1f);
                Color glow = new Color(0.05f, 0.48f, 0.58f, 1f);
                Color color = Color.Lerp(deep, teal, depth * 0.72f);
                color = Color.Lerp(color, glow, Mathf.SmoothStep(0.54f, 0.92f, glowNoise) * 0.48f);

                if (flecks > 0.968f)
                    color = Color.Lerp(color, new Color(0.56f, 0.98f, 1f, 1f), 0.72f);

                texture.SetPixel(x, y, color);
            }
        }

        texture.Apply(false, true);
        return texture;
    }

    private static Texture2D CreateSpaceTexture()
    {
        const int width = 256;
        const int height = 160;
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = "RuntimeSpaceBackdropTexture",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float u = x / (float)width;
                float v = y / (float)height;
                float nebulaA = Mathf.PerlinNoise(u * 3.2f + 1.7f, v * 2.4f + 9.1f);
                float nebulaB = Mathf.PerlinNoise(u * 6.8f + 12.5f, v * 5.4f + 2.6f);
                float stars = Mathf.PerlinNoise(u * 90f, v * 90f);
                Color color = Color.Lerp(
                    new Color(0.004f, 0.005f, 0.015f, 1f),
                    new Color(0.09f, 0.025f, 0.18f, 1f),
                    Mathf.SmoothStep(0.5f, 0.9f, nebulaA));

                color = Color.Lerp(color, new Color(0.02f, 0.1f, 0.28f, 1f), Mathf.SmoothStep(0.58f, 0.96f, nebulaB) * 0.55f);
                if (stars > 0.965f)
                    color = Color.Lerp(color, Color.white, 0.82f);

                texture.SetPixel(x, y, color);
            }
        }

        texture.Apply(false, true);
        return texture;
    }

    private static Mesh CreateTriangleMesh()
    {
        Mesh mesh = new Mesh
        {
            name = "RuntimeEnvironmentTriangle"
        };
        mesh.vertices = new[]
        {
            new Vector3(0.5f, 0f, 0f),
            new Vector3(-0.5f, 0.5f, 0f),
            new Vector3(-0.5f, -0.5f, 0f)
        };
        mesh.triangles = new[] { 0, 1, 2 };
        mesh.RecalculateNormals();
        return mesh;
    }

    private static Mesh CreateRingMesh(float innerRadius, float outerRadius, int segments)
    {
        Vector3[] vertices = new Vector3[(segments + 1) * 2];
        Vector2[] uv = new Vector2[vertices.Length];
        int[] triangles = new int[segments * 12];

        for (int i = 0; i <= segments; i++)
        {
            float angle = (i / (float)segments) * Mathf.PI * 2f;
            float sin = Mathf.Sin(angle);
            float cos = Mathf.Cos(angle);
            int index = i * 2;
            vertices[index] = new Vector3(cos * innerRadius, sin * innerRadius, 0f);
            vertices[index + 1] = new Vector3(cos * outerRadius, sin * outerRadius, 0f);
            uv[index] = new Vector2(i / (float)segments, 0f);
            uv[index + 1] = new Vector2(i / (float)segments, 1f);
        }

        int triangle = 0;
        for (int i = 0; i < segments; i++)
        {
            int a = i * 2;
            int b = a + 1;
            int c = a + 2;
            int d = a + 3;

            triangles[triangle++] = a;
            triangles[triangle++] = b;
            triangles[triangle++] = c;
            triangles[triangle++] = c;
            triangles[triangle++] = b;
            triangles[triangle++] = d;
            triangles[triangle++] = c;
            triangles[triangle++] = b;
            triangles[triangle++] = a;
            triangles[triangle++] = d;
            triangles[triangle++] = b;
            triangles[triangle++] = c;
        }

        Mesh mesh = new Mesh
        {
            name = "RuntimeEnvironmentRing"
        };
        mesh.vertices = vertices;
        mesh.uv = uv;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void SetNonInteractive(GameObject target)
    {
        if (target == null)
            return;

        int ignoreRaycastLayer = LayerMask.NameToLayer("Ignore Raycast");
        if (ignoreRaycastLayer >= 0)
            SetLayerRecursively(target, ignoreRaycastLayer);

        Collider[] colliders = target.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
                Destroy(colliders[i]);
        }
    }

    private static void SetLayerRecursively(GameObject target, int layer)
    {
        if (target == null || layer < 0)
            return;

        target.layer = layer;
        for (int i = 0; i < target.transform.childCount; i++)
            SetLayerRecursively(target.transform.GetChild(i).gameObject, layer);
    }

    private Camera ResolveDisplayCamera()
    {
        Camera main = Camera.main;
        if (IsUsableDisplayCamera(main))
            return main;

        Camera[] cameras = Camera.allCameras;
        for (int i = 0; i < cameras.Length; i++)
        {
            if (IsUsableDisplayCamera(cameras[i]))
                return cameras[i];
        }

        return null;
    }

    private static bool IsUsableDisplayCamera(Camera camera)
    {
        return camera != null &&
            camera.gameObject.activeInHierarchy &&
            camera.enabled &&
            camera.targetTexture == null;
    }

    private void CaptureRenderSettings()
    {
        if (renderSettingsCaptured)
            return;

        renderSettingsSnapshot = RenderSettingsSnapshot.Capture();
        renderSettingsCaptured = true;
    }

    private void RestoreRenderSettings()
    {
        if (!renderSettingsCaptured)
            return;

        renderSettingsSnapshot.Restore();
    }

    private void CaptureCameraSnapshot(Camera camera)
    {
        if (camera == null || cameraSnapshots.ContainsKey(camera))
            return;

        cameraSnapshots.Add(camera, new CameraSnapshot(camera));
    }

    private void RestoreCameraSnapshots()
    {
        foreach (KeyValuePair<Camera, CameraSnapshot> entry in cameraSnapshots)
        {
            if (entry.Key != null)
                entry.Value.Restore(entry.Key);
        }
    }

    private static float Halton(int index, int radix)
    {
        float result = 0f;
        float fraction = 1f / radix;

        while (index > 0)
        {
            result += fraction * (index % radix);
            index /= radix;
            fraction /= radix;
        }

        return result;
    }

    private readonly struct RenderSettingsSnapshot
    {
        private readonly bool fog;
        private readonly FogMode fogMode;
        private readonly Color fogColor;
        private readonly float fogDensity;
        private readonly UnityEngine.Rendering.AmbientMode ambientMode;
        private readonly Color ambientLight;
        private readonly Material skybox;

        private RenderSettingsSnapshot(
            bool fog,
            FogMode fogMode,
            Color fogColor,
            float fogDensity,
            UnityEngine.Rendering.AmbientMode ambientMode,
            Color ambientLight,
            Material skybox)
        {
            this.fog = fog;
            this.fogMode = fogMode;
            this.fogColor = fogColor;
            this.fogDensity = fogDensity;
            this.ambientMode = ambientMode;
            this.ambientLight = ambientLight;
            this.skybox = skybox;
        }

        public static RenderSettingsSnapshot Capture()
        {
            return new RenderSettingsSnapshot(
                RenderSettings.fog,
                RenderSettings.fogMode,
                RenderSettings.fogColor,
                RenderSettings.fogDensity,
                RenderSettings.ambientMode,
                RenderSettings.ambientLight,
                RenderSettings.skybox);
        }

        public void Restore()
        {
            RenderSettings.fog = fog;
            RenderSettings.fogMode = fogMode;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogDensity = fogDensity;
            RenderSettings.ambientMode = ambientMode;
            RenderSettings.ambientLight = ambientLight;
            RenderSettings.skybox = skybox;
        }
    }

    private readonly struct CameraSnapshot
    {
        private readonly CameraClearFlags clearFlags;
        private readonly Color backgroundColor;
        private readonly float nearClipPlane;
        private readonly float farClipPlane;

        public CameraSnapshot(Camera camera)
        {
            clearFlags = camera.clearFlags;
            backgroundColor = camera.backgroundColor;
            nearClipPlane = camera.nearClipPlane;
            farClipPlane = camera.farClipPlane;
        }

        public void Restore(Camera camera)
        {
            camera.clearFlags = clearFlags;
            camera.backgroundColor = backgroundColor;
            camera.nearClipPlane = nearClipPlane;
            camera.farClipPlane = farClipPlane;
        }
    }

    private sealed class RuntimeMaterialDrift : MonoBehaviour
    {
        private Material waterMaterial;
        private Material shimmerMaterial;

        public void Initialize(Material water, Material shimmer)
        {
            waterMaterial = water;
            shimmerMaterial = shimmer;
        }

        private void Update()
        {
            float time = Time.time;
            if (waterMaterial != null)
                waterMaterial.mainTextureOffset = new Vector2(time * 0.018f, Mathf.Sin(time * 0.21f) * 0.035f);

            if (shimmerMaterial != null)
                shimmerMaterial.mainTextureOffset = new Vector2(-time * 0.031f, time * 0.014f);
        }
    }

    private sealed class RuntimeFishSwim : MonoBehaviour
    {
        private Vector3 basePosition;
        private float speed;
        private float width;
        private float height;
        private float phase;

        public void Initialize(Vector3 position, float localSpeed, float localWidth, float localHeight, float localPhase)
        {
            basePosition = position;
            speed = localSpeed;
            width = localWidth;
            height = localHeight;
            phase = localPhase;
        }

        private void Update()
        {
            float t = Time.time * speed + phase;
            transform.localPosition = basePosition + new Vector3(Mathf.Sin(t) * width, Mathf.Sin(t * 1.7f) * height, 0f);
            transform.localRotation = Quaternion.Euler(0f, Mathf.Sin(t) > 0f ? 180f : 0f, Mathf.Sin(t * 1.2f) * 6f);
        }
    }

    private sealed class RuntimeLargeFishPass : MonoBehaviour
    {
        private const float TravelDurationSeconds = 18f;
        private const float TravelWidth = 9.5f;
        private bool leftToRight;
        private float phase;
        private float depth;
        private float height;

        public void Initialize(bool travelsLeftToRight, float startPhase, float localDepth, float localHeight)
        {
            leftToRight = travelsLeftToRight;
            phase = startPhase;
            depth = localDepth;
            height = localHeight;
        }

        private void Update()
        {
            float normalized = Mathf.Repeat((Time.time + phase) / TravelDurationSeconds, 1f);
            float eased = Mathf.SmoothStep(0f, 1f, normalized);
            float x = Mathf.Lerp(-TravelWidth, TravelWidth, leftToRight ? eased : 1f - eased);
            float y = height + Mathf.Sin((normalized * Mathf.PI * 2f) + phase) * 0.25f;
            transform.localPosition = new Vector3(x, y, depth);
            transform.localRotation = Quaternion.Euler(0f, leftToRight ? 180f : 0f, Mathf.Sin(normalized * Mathf.PI * 2f) * 5f);
        }
    }

    private sealed class RuntimeSlowSpin : MonoBehaviour
    {
        private Vector3 eulerSpeed;

        public void Initialize(Vector3 speed)
        {
            eulerSpeed = speed;
        }

        private void Update()
        {
            transform.Rotate(eulerSpeed * Time.deltaTime, Space.Self);
        }
    }
}
