using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;
using UnityEngine.XR;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public sealed class QuestScenePointer : MonoBehaviour
{
    [Header("Runtime")]
    [SerializeField] private bool enableInEditor;
    [SerializeField] private Camera sceneCamera;

    [Header("Controller Pointer")]
    [SerializeField] private XRNode pointerHand = XRNode.RightHand;
    [SerializeField] private float rayLength = 8f;
    [SerializeField] private float triggerThreshold = 0.65f;
    [SerializeField] private Vector3 rayOriginLocalOffset = new Vector3(0f, -0.015f, 0.095f);
    [SerializeField] private Vector3 rayRotationOffsetEuler;
    [SerializeField] private LayerMask physicsMask = ~0;

    [Header("Visuals")]
    [SerializeField] private bool showControllerVisual = true;
    [SerializeField] private Vector3 controllerVisualPositionOffset = new Vector3(0f, -0.02f, 0.05f);
    [SerializeField] private Vector3 controllerVisualRotationOffsetEuler;
    [SerializeField] private Vector3 controllerBodySize = new Vector3(0.08f, 0.06f, 0.16f);
    [SerializeField] private float rayStartWidth = 0.026f;
    [SerializeField] private float rayEndWidth = 0.01f;
    [SerializeField] private float reticleSize = 0.06f;

    [Header("Classroom Controls")]
    [SerializeField] private bool enableTeacherEnvironmentMenu = true;
    [SerializeField] private Vector2 teacherMenuSize = new Vector2(1120f, 560f);
    [SerializeField] private float teacherMenuScale = 0.0019f;
    [SerializeField] private float teacherMenuDistance = 1.8f;
    [SerializeField] private float teacherMenuHeightOffset = -0.08f;
    [SerializeField] private int menuRecenterSettleFrames = 8;
    [SerializeField] private string mockCaptionText = "Mock subtitle: welcome to today's XR classroom.";

    private readonly List<GraphicRaycaster> graphicRaycasters = new();
    private readonly List<RaycastResult> raycastResults = new();
    private PointerEventData pointerEventData;
    private LineRenderer pointerLine;
    private Transform reticle;
    private Transform controllerVisual;
    private Transform controllerRayAnchor;
    private Canvas teacherMenuCanvas;
    private RectTransform teacherMenuRect;
    private GraphicRaycaster teacherMenuRaycaster;
    private Text captionStatusText;
    private GameObject hoveredObject;
    private bool configured;
    private bool wasPressed;
    private bool wasMenuButtonPressed;
    private int pendingMenuRecenterFrames;
    private Vector2 lastPointerPosition;
    private bool hasLastPointerPosition;

#if ENABLE_INPUT_SYSTEM
    private InputAction pointerPositionAction;
    private InputAction pointerRotationAction;
    private InputAction isTrackedAction;
    private InputAction clickAction;
    private InputAction menuAction;
#endif

    private bool ShouldRun =>
#if UNITY_ANDROID && !UNITY_EDITOR
        true;
#else
        enableInEditor;
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        QuestExternalSceneViewBootstrap.ConfigureLoadedScenes("pointer bootstrap");
        EnsurePointerForLoadedScenes();
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        QuestExternalSceneViewBootstrap.ConfigureScene(scene, "pointer scene loaded");
        EnsurePointerForScene(scene);
    }

    private static void EnsurePointerForLoadedScenes()
    {
        if (!ShouldRunOnCurrentPlatform())
            return;

        for (int i = 0; i < SceneManager.sceneCount; i++)
            EnsurePointerForScene(SceneManager.GetSceneAt(i));
    }

    private static void EnsurePointerForScene(Scene scene)
    {
        if (!ShouldRunOnCurrentPlatform())
            return;

        if (!scene.IsValid() || IsLobbyScene(scene))
            return;

        if (SceneHasPointer(scene))
            return;

        GameObject pointerObject = new GameObject("QuestScenePointer");
        SceneManager.MoveGameObjectToScene(pointerObject, scene);
        pointerObject.AddComponent<QuestScenePointer>();
    }

    private static bool SceneHasPointer(Scene scene)
    {
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            if (roots[i] != null && roots[i].GetComponentInChildren<QuestScenePointer>(true) != null)
                return true;
        }

        return false;
    }

    private static bool ShouldRunOnCurrentPlatform()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        return true;
#else
        return false;
#endif
    }

    private static bool IsLobbyScene(Scene scene)
    {
        string sceneName = scene.name;
        return sceneName.IndexOf("Lobby", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void Awake()
    {
        if (!ShouldRun)
        {
            enabled = false;
            return;
        }
    }

    private void Start()
    {
        if (!ShouldRun)
            return;

        Configure();
    }

    private void OnDisable()
    {
        CancelPress();
        ClearHover();

#if ENABLE_INPUT_SYSTEM
        pointerPositionAction?.Disable();
        pointerRotationAction?.Disable();
        isTrackedAction?.Disable();
        clickAction?.Disable();
        menuAction?.Disable();
#endif
    }

    private void OnDestroy()
    {
#if ENABLE_INPUT_SYSTEM
        pointerPositionAction?.Dispose();
        pointerRotationAction?.Dispose();
        isTrackedAction?.Dispose();
        clickAction?.Dispose();
        menuAction?.Dispose();
#endif
    }

    private void Update()
    {
        if (!configured)
            return;

        if ((Time.frameCount & 63) == 0)
            RefreshGraphicRaycasters();

        if (ConsumeMenuButtonPressed())
        {
            if (ShouldReturnToClassroomOnMenuButton())
                RequestReturnToClassroom();
            else
                ShowTeacherMenuInFrontOfView("A button");
        }

        if (pendingMenuRecenterFrames > 0 && PlaceTeacherMenuInFrontOfView())
            pendingMenuRecenterFrames--;

        if (!TryGetPointerRay(out Ray pointerRay, out bool hasController, out Vector3 controllerPosition, out Quaternion controllerRotation))
        {
            UpdateVisual(pointerRay, false, pointerRay.origin + pointerRay.direction * rayLength);
            UpdateControllerVisual(Vector3.zero, Quaternion.identity, false);
            ClearHover();
            CancelPress();
            return;
        }

        PointerHit hit = FindBestTarget(pointerRay);
        UpdateVisual(pointerRay, hit.HasHit, hit.HasHit ? hit.WorldPosition : pointerRay.origin + pointerRay.direction * rayLength);
        UpdateControllerVisual(controllerPosition, controllerRotation, hasController);
        UpdateHover(hit.Target, hit.RaycastResult);

        bool isPressed = hasController && IsClickPressed();
        if (isPressed && !wasPressed)
            Press(hit.Target);
        else if (!isPressed && wasPressed)
            Release(hit.Target);

        wasPressed = isPressed;
    }

    private void Configure()
    {
        QuestExternalSceneViewBootstrap.ConfigureScene(gameObject.scene, "pointer configure");
        sceneCamera = ResolveSceneCamera(gameObject.scene);
        if (sceneCamera == null)
        {
            Debug.LogWarning("[QuestScenePointer] No scene camera found; pointer disabled.");
            enabled = false;
            return;
        }

        ConfigureSceneCamera(sceneCamera);
        DisableCompetingDisplayCameras(sceneCamera);
        QuestXrRenderGuard.ReconcileNow("QuestScenePointer configure");
        EnsureEventSystem();

        pointerEventData = new PointerEventData(EventSystem.current)
        {
            button = PointerEventData.InputButton.Left,
            pointerId = -300
        };

        RefreshGraphicRaycasters();
        ConfigureInputActions();
        CreatePointerVisuals();
        CreateControllerVisual();
        ConfigureTeacherEnvironmentMenu();

        configured = true;
        Debug.Log($"[QuestScenePointer] Scene pointer configured for '{gameObject.scene.path}' using camera '{sceneCamera.name}'.");
    }

    private static Camera ResolveSceneCamera(Scene ownerScene)
    {
        Camera[] cameras = Camera.allCameras;

        Camera preferredSceneCamera = null;
        for (int i = 0; i < cameras.Length; i++)
        {
            Camera camera = cameras[i];
            if (!IsUsableDisplayCamera(camera) || camera.gameObject.scene != ownerScene)
                continue;

            if (IsPreferredDisplayCamera(camera))
                return camera;

            preferredSceneCamera ??= camera;
        }

        if (preferredSceneCamera != null)
            return preferredSceneCamera;

        Camera main = Camera.main;
        if (IsUsableDisplayCamera(main))
            return main;

        for (int i = 0; i < cameras.Length; i++)
        {
            if (IsUsableDisplayCamera(cameras[i]))
                return cameras[i];
        }

        return main != null ? main : cameras.Length > 0 ? cameras[0] : null;
    }

    private static bool IsUsableDisplayCamera(Camera camera)
    {
        return camera != null &&
            camera.isActiveAndEnabled &&
            camera.targetTexture == null &&
            camera.stereoTargetEye != StereoTargetEyeMask.None;
    }

    private static bool IsPreferredDisplayCamera(Camera camera)
    {
        string cameraName = camera.gameObject.name;
        return camera.CompareTag("MainCamera") &&
            !cameraName.Contains("(1)") &&
            cameraName.IndexOf("Suimono", System.StringComparison.OrdinalIgnoreCase) < 0;
    }

    private static void ConfigureSceneCamera(Camera camera)
    {
        camera.rect = new Rect(0f, 0f, 1f, 1f);
        camera.targetTexture = null;
        camera.stereoTargetEye = StereoTargetEyeMask.Both;
        DisableQuestUnsafeImageEffects(camera);
    }

    private static void DisableQuestUnsafeImageEffects(Camera camera)
    {
        MonoBehaviour[] behaviours = camera.GetComponents<MonoBehaviour>();
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour == null || !behaviour.enabled)
                continue;

            string typeName = behaviour.GetType().FullName;
            if (typeName != "Suimono.Core.Suimono_UnderwaterFog" &&
                typeName != "Suimono.Core.Suimono_DistanceBlur")
                continue;

            behaviour.enabled = false;
            Debug.Log($"[QuestScenePointer] Disabled stereo-unsafe image effect '{typeName}' on camera '{camera.name}'.");
        }
    }

    private static void DisableCompetingDisplayCameras(Camera primaryCamera)
    {
        Camera[] cameras = Camera.allCameras;
        for (int i = 0; i < cameras.Length; i++)
        {
            Camera camera = cameras[i];
            if (camera == null || camera == primaryCamera)
                continue;

            if (camera.targetTexture != null)
            {
                ConfigureOffscreenCamera(camera);
                continue;
            }

            if (!camera.enabled && camera.stereoTargetEye == StereoTargetEyeMask.None)
                continue;

            camera.stereoTargetEye = StereoTargetEyeMask.None;
            camera.enabled = false;
            if (camera.TryGetComponent(out AudioListener listener))
                listener.enabled = false;

            Debug.Log($"[QuestScenePointer] Disabled competing XR display camera '{camera.name}'.");
        }
    }

    private static void ConfigureOffscreenCamera(Camera camera)
    {
        if (camera.stereoTargetEye == StereoTargetEyeMask.None && !camera.allowHDR && !camera.allowMSAA)
            return;

        camera.stereoTargetEye = StereoTargetEyeMask.None;
        camera.allowHDR = false;
        camera.allowMSAA = false;
        Debug.Log($"[QuestScenePointer] Configured offscreen camera '{camera.name}' as non-XR.");
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null)
        {
            if (EventSystem.current.GetComponent<BaseInputModule>() == null)
                EventSystem.current.gameObject.AddComponent<StandaloneInputModule>();

            return;
        }

        GameObject eventSystemObject = new GameObject("EventSystem");
        eventSystemObject.AddComponent<EventSystem>();
        eventSystemObject.AddComponent<StandaloneInputModule>();
    }

    private void RefreshGraphicRaycasters()
    {
        graphicRaycasters.Clear();
        Canvas[] canvases = FindObjectsOfType<Canvas>(true);
        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas canvas = canvases[i];
            if (canvas == null || !canvas.isActiveAndEnabled)
                continue;

            if (canvas.renderMode != RenderMode.ScreenSpaceOverlay && canvas.worldCamera == null)
                canvas.worldCamera = sceneCamera;

            GraphicRaycaster raycaster = canvas.GetComponent<GraphicRaycaster>();
            if (raycaster == null)
                raycaster = canvas.gameObject.AddComponent<GraphicRaycaster>();

            raycaster.ignoreReversedGraphics = false;
            raycaster.blockingObjects = GraphicRaycaster.BlockingObjects.None;
            graphicRaycasters.Add(raycaster);
        }
    }

    private void ConfigureInputActions()
    {
#if ENABLE_INPUT_SYSTEM
        string handUsage = pointerHand == XRNode.LeftHand ? "LeftHand" : "RightHand";
        string handBinding = $"<XRController>{{{handUsage}}}";

        pointerPositionAction = new InputAction("Quest Scene Pointer Position", InputActionType.Value);
        pointerPositionAction.AddBinding($"{handBinding}/pointerPosition");
        pointerPositionAction.AddBinding($"{handBinding}/devicePosition");

        pointerRotationAction = new InputAction("Quest Scene Pointer Rotation", InputActionType.Value);
        pointerRotationAction.AddBinding($"{handBinding}/pointerRotation");
        pointerRotationAction.AddBinding($"{handBinding}/deviceRotation");

        isTrackedAction = new InputAction("Quest Scene Pointer Tracked", InputActionType.Button);
        isTrackedAction.AddBinding($"{handBinding}/isTracked");

        clickAction = new InputAction("Quest Scene Pointer Click", InputActionType.Button);
        clickAction.AddBinding($"{handBinding}/triggerPressed");
        clickAction.AddBinding($"{handBinding}/trigger");
        clickAction.AddBinding($"{handBinding}/gripPressed");
        clickAction.AddBinding($"{handBinding}/pointerActivated");
        clickAction.AddBinding($"{handBinding}/pointerActivateValue");

        menuAction = new InputAction("Quest Scene Menu / Return", InputActionType.Button);
        AddMenuButtonBindings(menuAction, handBinding);

        pointerPositionAction.Enable();
        pointerRotationAction.Enable();
        isTrackedAction.Enable();
        clickAction.Enable();
        menuAction.Enable();
#endif
    }

#if ENABLE_INPUT_SYSTEM
    private static void AddMenuButtonBindings(InputAction action, string preferredHandBinding)
    {
        action.AddBinding($"{preferredHandBinding}/primaryButton");
        action.AddBinding($"{preferredHandBinding}/secondaryButton");
        action.AddBinding($"{preferredHandBinding}/menu");
        action.AddBinding($"{preferredHandBinding}/menuButton");
        action.AddBinding($"{preferredHandBinding}/primary2DAxisClick");
        action.AddBinding($"{preferredHandBinding}/secondary2DAxisClick");
        action.AddBinding("<XRController>{RightHand}/primaryButton");
        action.AddBinding("<XRController>{RightHand}/secondaryButton");
        action.AddBinding("<XRController>{RightHand}/menu");
        action.AddBinding("<XRController>{RightHand}/menuButton");
        action.AddBinding("<XRController>{RightHand}/primary2DAxisClick");
        action.AddBinding("<XRController>{RightHand}/secondary2DAxisClick");
        action.AddBinding("<XRController>{LeftHand}/primaryButton");
        action.AddBinding("<XRController>{LeftHand}/secondaryButton");
        action.AddBinding("<XRController>{LeftHand}/menu");
        action.AddBinding("<XRController>{LeftHand}/menuButton");
        action.AddBinding("<XRController>{LeftHand}/primary2DAxisClick");
        action.AddBinding("<XRController>{LeftHand}/secondary2DAxisClick");
        action.AddBinding("<OculusTouchController>{RightHand}/primaryButton");
        action.AddBinding("<OculusTouchController>{RightHand}/secondaryButton");
        action.AddBinding("<OculusTouchController>{RightHand}/menu");
        action.AddBinding("<OculusTouchController>{RightHand}/menuButton");
        action.AddBinding("<OculusTouchController>{RightHand}/primary2DAxisClick");
        action.AddBinding("<OculusTouchController>{RightHand}/secondary2DAxisClick");
        action.AddBinding("<OculusTouchController>{LeftHand}/primaryButton");
        action.AddBinding("<OculusTouchController>{LeftHand}/secondaryButton");
        action.AddBinding("<OculusTouchController>{LeftHand}/menu");
        action.AddBinding("<OculusTouchController>{LeftHand}/menuButton");
        action.AddBinding("<OculusTouchController>{LeftHand}/primary2DAxisClick");
        action.AddBinding("<OculusTouchController>{LeftHand}/secondary2DAxisClick");
    }
#endif

    private void ConfigureTeacherEnvironmentMenu()
    {
        if (ShouldCreateExternalReturnMenu())
        {
            ConfigureExternalReturnMenu();
            return;
        }

        if (!ShouldCreateClassroomMenu())
            return;

        CreateRuntimeMenuCanvas("QuestClassroomControlMenu", teacherMenuSize, new Color(0.07f, 0.08f, 0.09f, 0.86f));

        if (LocalUserProfile.Role == UserRole.Teacher)
            CreateTeacherControls();
        else if (LocalUserProfile.Role == UserRole.Student)
            CreateStudentControls();

        ShowTeacherMenuInFrontOfView("Initial placement");
        RefreshGraphicRaycasters();
        Debug.Log($"[QuestScenePointer] Quest classroom control menu created for Test_classroom. role={LocalUserProfile.Role}");
    }

    private void ConfigureExternalReturnMenu()
    {
        CreateRuntimeMenuCanvas("QuestExternalSceneReturnMenu", new Vector2(420f, 130f), new Color(0.07f, 0.08f, 0.09f, 0.74f));
        CreateMenuButton("Classroom", Vector2.zero, RequestReturnToClassroom, new Vector2(320f, 86f));
        ShowTeacherMenuInFrontOfView("External scene return placement");
        RefreshGraphicRaycasters();
        Debug.Log($"[QuestScenePointer] External scene return menu created for '{gameObject.scene.name}'.");
    }

    private void CreateRuntimeMenuCanvas(string canvasName, Vector2 size, Color backgroundColor)
    {
        GameObject canvasObject = new GameObject(
            canvasName,
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster),
            typeof(Image));
        MoveRuntimeObjectToOwnerScene(canvasObject);

        int uiLayer = LayerMask.NameToLayer("UI");
        if (uiLayer >= 0)
            canvasObject.layer = uiLayer;

        teacherMenuRect = canvasObject.GetComponent<RectTransform>();
        teacherMenuCanvas = canvasObject.GetComponent<Canvas>();
        teacherMenuRaycaster = canvasObject.GetComponent<GraphicRaycaster>();

        teacherMenuCanvas.renderMode = RenderMode.WorldSpace;
        teacherMenuCanvas.worldCamera = sceneCamera;
        teacherMenuCanvas.sortingOrder = 50;

        teacherMenuRect.sizeDelta = size;
        teacherMenuRect.localScale = Vector3.one * teacherMenuScale;
        teacherMenuRect.anchorMin = new Vector2(0.5f, 0.5f);
        teacherMenuRect.anchorMax = new Vector2(0.5f, 0.5f);
        teacherMenuRect.pivot = new Vector2(0.5f, 0.5f);

        Image background = canvasObject.GetComponent<Image>();
        background.color = backgroundColor;
        background.raycastTarget = true;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        scaler.scaleFactor = 1f;
        scaler.dynamicPixelsPerUnit = 10f;

        teacherMenuRaycaster.ignoreReversedGraphics = false;
        teacherMenuRaycaster.blockingObjects = GraphicRaycaster.BlockingObjects.None;
    }

    private bool ShouldCreateClassroomMenu()
    {
        if (!enableTeacherEnvironmentMenu || !IsTestClassroomScene(gameObject.scene))
            return false;

        return LocalUserProfile.Role == UserRole.Teacher || LocalUserProfile.Role == UserRole.Student;
    }

    private bool ShouldCreateExternalReturnMenu()
    {
        return enableTeacherEnvironmentMenu && IsExternalEnvironmentScene(gameObject.scene);
    }

    private void CreateTeacherControls()
    {
        CreateMenuLabel("Teacher Controls", new Vector2(0f, 218f), new Vector2(960f, 58f), 38, FontStyle.Bold);
        CreateMenuLabel("Environment", new Vector2(0f, 154f), new Vector2(960f, 42f), 26, FontStyle.Bold);
        CreateMenuButton("Default", new Vector2(-340f, 86f), () => RequestEnvironment(ClassroomEnvironment.Default));
        CreateMenuButton("Ocean", new Vector2(0f, 86f), () => RequestEnvironment(ClassroomEnvironment.Ocean));
        CreateMenuButton("Space", new Vector2(340f, 86f), () => RequestEnvironment(ClassroomEnvironment.Space));

        CreateMenuLabel("Classroom", new Vector2(0f, 14f), new Vector2(960f, 42f), 26, FontStyle.Bold);
        CreateMenuButton("Clear Board", new Vector2(-340f, -58f), RequestClearBlackboard);
        CreateMenuButton("Caption", new Vector2(0f, -58f), RequestMockCaption);
        CreateMenuButton("Video Panel", new Vector2(340f, -58f), RequestToggleVideoPanel);
        CreateMenuButton("Play/Pause", new Vector2(0f, -188f), RequestToggleVideoPlayback);

        captionStatusText = CreateTextObject("Caption: Ready", "CaptionStatusText", new Vector2(0f, -258f), new Vector2(980f, 40f), 22, FontStyle.Normal);
        captionStatusText.alignment = TextAnchor.MiddleCenter;
        captionStatusText.color = new Color(0.74f, 0.96f, 1f, 1f);
        QuestCaptionStatusReporter.Register(captionStatusText);
    }

    private void CreateStudentControls()
    {
        CreateMenuLabel("Student Controls", new Vector2(0f, 120f), new Vector2(960f, 64f), 40, FontStyle.Bold);
        CreateMenuButton("Raise Hand", new Vector2(-170f, -34f), () => RequestStudentHandRaised(true), new Vector2(300f, 96f));
        CreateMenuButton("Lower Hand", new Vector2(170f, -34f), () => RequestStudentHandRaised(false), new Vector2(300f, 96f));
    }

    private static bool IsTestClassroomScene(Scene scene)
    {
        return scene.name == "Test_classroom" ||
            scene.path.EndsWith("Assets/Scenes/Test_classroom.unity", System.StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExternalEnvironmentScene(Scene scene)
    {
        return scene.name == "Ocean" ||
            scene.name == "Space" ||
            scene.path.EndsWith("Assets/Scenes/Ocean.unity", System.StringComparison.OrdinalIgnoreCase) ||
            scene.path.EndsWith("Assets/Scenes/Space.unity", System.StringComparison.OrdinalIgnoreCase);
    }

    private void CreateMenuLabel(string text, Vector2 position, Vector2 size, int fontSize, FontStyle fontStyle)
    {
        Text label = CreateTextObject(text, "Label", position, size, fontSize, fontStyle);
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;
    }

    private void CreateMenuButton(string label, Vector2 position, UnityEngine.Events.UnityAction onClick)
    {
        CreateMenuButton(label, position, onClick, new Vector2(260f, 82f));
    }

    private void CreateMenuButton(string label, Vector2 position, UnityEngine.Events.UnityAction onClick, Vector2 size)
    {
        GameObject buttonObject = new GameObject(label + "Button", typeof(RectTransform), typeof(Image), typeof(Button));

        int uiLayer = LayerMask.NameToLayer("UI");
        if (uiLayer >= 0)
            buttonObject.layer = uiLayer;

        buttonObject.transform.SetParent(teacherMenuRect, false);

        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        Image image = buttonObject.GetComponent<Image>();
        image.color = new Color(0.18f, 0.23f, 0.28f, 0.96f);

        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(onClick);

        ColorBlock colors = button.colors;
        colors.normalColor = new Color(0.18f, 0.23f, 0.28f, 0.96f);
        colors.highlightedColor = new Color(0.12f, 0.62f, 0.78f, 1f);
        colors.pressedColor = new Color(0.08f, 0.48f, 0.64f, 1f);
        colors.selectedColor = colors.highlightedColor;
        button.colors = colors;

        Text text = CreateTextObject(label, "Text", Vector2.zero, rect.sizeDelta, 30, FontStyle.Bold, buttonObject.transform);
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
    }

    private Text CreateTextObject(string text, string objectName, Vector2 position, Vector2 size, int fontSize, FontStyle fontStyle, Transform parent = null)
    {
        GameObject textObject = new GameObject(objectName, typeof(RectTransform), typeof(Text));

        int uiLayer = LayerMask.NameToLayer("UI");
        if (uiLayer >= 0)
            textObject.layer = uiLayer;

        textObject.transform.SetParent(parent != null ? parent : teacherMenuRect, false);

        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        Text label = textObject.GetComponent<Text>();
        label.text = text;
        label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        label.fontSize = fontSize;
        label.fontStyle = fontStyle;
        label.resizeTextForBestFit = true;
        label.resizeTextMinSize = 18;
        label.resizeTextMaxSize = fontSize;
        label.raycastTarget = false;
        return label;
    }

    private void ShowTeacherMenuInFrontOfView(string reason)
    {
        if (teacherMenuCanvas == null || (!ShouldCreateClassroomMenu() && !ShouldCreateExternalReturnMenu()))
            return;

        teacherMenuCanvas.gameObject.SetActive(true);
        if (teacherMenuRaycaster != null)
            teacherMenuRaycaster.enabled = true;

        pendingMenuRecenterFrames = Mathf.Max(1, menuRecenterSettleFrames);
        PlaceTeacherMenuInFrontOfView();
        RefreshGraphicRaycasters();
        Debug.Log($"[QuestScenePointer] Runtime menu placed in front of view. Reason: {reason}");
    }

    private bool PlaceTeacherMenuInFrontOfView()
    {
        if (teacherMenuRect == null || sceneCamera == null)
            return false;

        Transform view = sceneCamera.transform;
        Vector3 viewForward = view.forward;
        if (viewForward.sqrMagnitude < 0.001f)
            return false;

        viewForward.Normalize();

        Vector3 flatForward = Vector3.ProjectOnPlane(viewForward, Vector3.up);
        if (flatForward.sqrMagnitude < 0.001f)
            flatForward = Vector3.forward;

        flatForward.Normalize();

        teacherMenuRect.position = view.position + viewForward * teacherMenuDistance + view.up * teacherMenuHeightOffset;
        teacherMenuRect.rotation = Quaternion.LookRotation(flatForward, Vector3.up);
        teacherMenuRect.localScale = Vector3.one * teacherMenuScale;
        return true;
    }

    private void RequestEnvironment(ClassroomEnvironment environment)
    {
        if (LocalUserProfile.Role != UserRole.Teacher)
        {
            Debug.LogWarning($"[QuestScenePointer] Ignored environment request '{environment}' because local role is {LocalUserProfile.Role}.");
            return;
        }

        if (environment == ClassroomEnvironment.Ocean || environment == ClassroomEnvironment.Space)
        {
            bool sceneLoadRequested = QuestClassroomSceneTravel.RequestEnvironmentScene(environment, "Quest teacher environment button");
            Debug.Log($"[QuestScenePointer] Teacher requested scene environment: {environment}. sceneLoadRequested={sceneLoadRequested}");
            return;
        }

        bool requested = false;
        if (TryGetValidSessionState(out ClassroomSessionState sessionState))
        {
            sessionState.RequestSetEnvironment(environment);
            requested = true;
        }

        EnvironmentManager environmentManager = FindObjectOfType<EnvironmentManager>();
        if (environmentManager != null)
        {
            environmentManager.SetEnvironment((EnvironmentType)(int)environment);
            requested = true;
        }

        EM_test[] localEnvironmentViews = FindObjectsOfType<EM_test>();
        for (int i = 0; i < localEnvironmentViews.Length; i++)
            localEnvironmentViews[i].HandleEnvironmentChanged(environment);

        Debug.Log($"[QuestScenePointer] Teacher requested environment: {environment}. networkRequestSent={requested}");
    }

    private void RequestClearBlackboard()
    {
        if (LocalUserProfile.Role != UserRole.Teacher)
        {
            Debug.LogWarning($"[QuestScenePointer] Ignored clear blackboard request because local role is {LocalUserProfile.Role}.");
            return;
        }

        bool requested = false;
        if (TryGetValidSessionState(out ClassroomSessionState sessionState))
        {
            sessionState.RequestClearBlackboard();
            requested = true;
        }

        SetLocalBlackboardText(string.Empty);
        Debug.Log($"[QuestScenePointer] Teacher requested blackboard clear. networkRequestSent={requested}");
    }

    private void RequestMockCaption()
    {
        if (LocalUserProfile.Role != UserRole.Teacher)
        {
            Debug.LogWarning($"[QuestScenePointer] Ignored mock caption request because local role is {LocalUserProfile.Role}.");
            return;
        }

        QuestCaptionStatusReporter.SetStatus("Caption: checking microphone and STT setup...");

        if (TryGetSpeechRecorder(out SpeechRecorder speechRecorder))
        {
            bool wasRecording = speechRecorder.IsRecording;
            bool toggled = speechRecorder.TryToggleRecording(out bool isNowRecording);
            string status = string.IsNullOrWhiteSpace(speechRecorder.StatusMessage)
                ? toggled ? isNowRecording ? "Recording caption..." : "Processing caption..." : "Caption failed"
                : speechRecorder.StatusMessage;

            QuestCaptionStatusReporter.SetStatus(status);
            SetLocalBlackboardText(status);

            Debug.Log($"[QuestScenePointer] Teacher toggled speech caption recording. toggled={toggled}, wasRecording={wasRecording}, isRecording={isNowRecording}, status={speechRecorder.StatusMessage}");
            return;
        }

        string fallbackText = string.IsNullOrWhiteSpace(mockCaptionText)
            ? "Speech recorder not found"
            : mockCaptionText;

        SetLocalBlackboardText(fallbackText);
        QuestCaptionStatusReporter.SetStatus(fallbackText);
        Debug.LogWarning($"[QuestScenePointer] SpeechRecorder not found for caption recording. fallbackText={fallbackText}");
    }

    private void RequestToggleVideoPanel()
    {
        if (LocalUserProfile.Role != UserRole.Teacher)
        {
            Debug.LogWarning($"[QuestScenePointer] Ignored video panel request because local role is {LocalUserProfile.Role}.");
            return;
        }

        bool visible = true;
        bool requested = false;
        if (TryGetValidSessionState(out ClassroomSessionState sessionState))
        {
            visible = !sessionState.IsVideoPanelVisible;
            sessionState.RequestSetVideoPanelVisible(visible);
            requested = true;
        }

        SetLocalVideoPanelVisible(visible);
        Debug.Log($"[QuestScenePointer] Teacher toggled video panel. visible={visible}, networkRequestSent={requested}");
    }

    private void RequestToggleVideoPlayback()
    {
        if (LocalUserProfile.Role != UserRole.Teacher)
        {
            Debug.LogWarning($"[QuestScenePointer] Ignored video playback request because local role is {LocalUserProfile.Role}.");
            return;
        }

        bool playing = true;
        float timeSeconds = GetCurrentVideoTime();
        bool requested = false;
        if (TryGetValidSessionState(out ClassroomSessionState sessionState))
        {
            playing = !sessionState.IsVideoPlaying;
            if (timeSeconds <= 0f)
                timeSeconds = sessionState.VideoPlaybackTimeSeconds;

            sessionState.RequestSyncVideo(playing, timeSeconds);
            requested = true;
        }
        else
        {
            playing = !IsAnyVideoPlaying();
        }

        SetLocalVideoPanelVisible(true);
        SetLocalVideoPlaying(playing);
        Debug.Log($"[QuestScenePointer] Teacher toggled video playback. playing={playing}, time={timeSeconds:0.00}, networkRequestSent={requested}");
    }

    private void RequestStudentHandRaised(bool raised)
    {
        if (LocalUserProfile.Role != UserRole.Student)
        {
            Debug.LogWarning($"[QuestScenePointer] Ignored student hand request because local role is {LocalUserProfile.Role}.");
            return;
        }

        bool requested = false;
        if (TryGetValidSessionState(out ClassroomSessionState sessionState))
        {
            sessionState.RequestSetStudentHandRaised(raised);
            requested = true;
        }
        else
        {
            Debug.LogWarning("[QuestScenePointer] Student hand request could not find a valid ClassroomSessionState.");
        }

        UpdateLocalHandRaiseUi(raised);
        Debug.Log($"[QuestScenePointer] Student hand raised={raised}. networkRequestSent={requested}");
    }

    private static bool TryGetValidSessionState(out ClassroomSessionState sessionState)
    {
        return ClassroomSessionState.TryGetActiveNetworked(out sessionState);
    }

    private bool ShouldReturnToClassroomOnMenuButton()
    {
        return IsExternalEnvironmentScene(gameObject.scene);
    }

    private void RequestReturnToClassroom()
    {
        bool sceneLoadRequested = QuestClassroomSceneTravel.RequestClassroomScene("Quest A button return");
        Debug.Log($"[QuestScenePointer] Requested return to Test_classroom from '{gameObject.scene.name}'. role={LocalUserProfile.Role}, sceneLoadRequested={sceneLoadRequested}");
    }

    private static bool TryGetSpeechRecorder(out SpeechRecorder speechRecorder)
    {
        SpeechRecorder[] recorders = FindObjectsOfType<SpeechRecorder>(true);
        for (int i = 0; i < recorders.Length; i++)
        {
            if (recorders[i] != null && recorders[i].isActiveAndEnabled)
            {
                speechRecorder = recorders[i];
                speechRecorder.EnsureRuntimeReferences();
                return true;
            }
        }

        GameObject recorderObject = new GameObject("QuestRuntimeSpeechRecorder");
        SpeechToTextClient speechToTextClient = recorderObject.AddComponent<SpeechToTextClient>();
        speechRecorder = recorderObject.AddComponent<SpeechRecorder>();
        speechToTextClient.EnsureRuntimeReferences();
        speechRecorder.Configure(speechToTextClient, FindTextObject("StatusText"));
        Debug.Log("[QuestScenePointer] Created runtime SpeechRecorder because none existed in the active scene.");
        return true;
    }

    private static void SetLocalBlackboardText(string text)
    {
        TMP_Text[] textObjects = FindObjectsOfType<TMP_Text>(true);
        for (int i = 0; i < textObjects.Length; i++)
        {
            TMP_Text textObject = textObjects[i];
            if (textObject != null && textObject.name == "SubtitleText")
                textObject.text = text ?? string.Empty;
        }
    }

    private static TMP_Text FindTextObject(string objectName)
    {
        TMP_Text[] textObjects = FindObjectsOfType<TMP_Text>(true);
        for (int i = 0; i < textObjects.Length; i++)
        {
            TMP_Text textObject = textObjects[i];
            if (textObject != null && textObject.name == objectName)
                return textObject;
        }

        return null;
    }

    private static void SetLocalVideoPanelVisible(bool visible)
    {
        VideoPanelController[] panels = FindObjectsOfType<VideoPanelController>(true);
        for (int i = 0; i < panels.Length; i++)
            panels[i].HandleVideoPanelVisibleChanged(visible);
    }

    private static void SetLocalVideoPlaying(bool playing)
    {
        VideoPanelController[] panels = FindObjectsOfType<VideoPanelController>(true);
        for (int i = 0; i < panels.Length; i++)
            panels[i].HandleVideoPlayingChanged(playing);
    }

    private static bool IsAnyVideoPlaying()
    {
        VideoPlayer[] players = FindObjectsOfType<VideoPlayer>(true);
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] != null && players[i].isPlaying)
                return true;
        }

        return false;
    }

    private static float GetCurrentVideoTime()
    {
        VideoPlayer[] players = FindObjectsOfType<VideoPlayer>(true);
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] != null && players[i].time > 0d)
                return (float)players[i].time;
        }

        return 0f;
    }

    private static void UpdateLocalHandRaiseUi(bool raised)
    {
        TeacherHandRaiseUI[] handRaiseViews = FindObjectsOfType<TeacherHandRaiseUI>(true);
        for (int i = 0; i < handRaiseViews.Length; i++)
            handRaiseViews[i].UpdateHandRaiseStatus(raised);
    }

    private void CreatePointerVisuals()
    {
        GameObject lineObject = new GameObject("QuestScenePointerLine");
        MoveRuntimeObjectToOwnerScene(lineObject);
        pointerLine = lineObject.AddComponent<LineRenderer>();
        pointerLine.useWorldSpace = true;
        pointerLine.positionCount = 2;
        pointerLine.startWidth = rayStartWidth;
        pointerLine.endWidth = rayEndWidth;
        pointerLine.numCapVertices = 4;
        pointerLine.material = new Material(Shader.Find("Sprites/Default"));
        pointerLine.startColor = new Color(0.1f, 0.85f, 1f, 0.95f);
        pointerLine.endColor = new Color(0.1f, 0.85f, 1f, 0.25f);

        GameObject reticleObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        reticleObject.name = "QuestScenePointerReticle";
        MoveRuntimeObjectToOwnerScene(reticleObject);
        reticleObject.transform.localScale = Vector3.one * reticleSize;

        if (reticleObject.TryGetComponent(out Collider reticleCollider))
            Destroy(reticleCollider);

        if (reticleObject.TryGetComponent(out Renderer renderer))
        {
            renderer.material = new Material(Shader.Find("Sprites/Default"))
            {
                color = new Color(0.1f, 0.85f, 1f, 0.95f)
            };
        }

        reticle = reticleObject.transform;
        reticle.gameObject.SetActive(false);
    }

    private void CreateControllerVisual()
    {
        if (!showControllerVisual || IsExternalEnvironmentScene(gameObject.scene))
            return;

        GameObject root = new GameObject("QuestSceneControllerVisual");
        MoveRuntimeObjectToOwnerScene(root);
        controllerVisual = root.transform;

        Material bodyMaterial = new Material(Shader.Find("Standard"))
        {
            color = new Color(0.16f, 0.18f, 0.2f, 1f)
        };

        Material accentMaterial = new Material(Shader.Find("Standard"))
        {
            color = new Color(0.1f, 0.85f, 1f, 1f)
        };

        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "ControllerBody";
        body.transform.SetParent(controllerVisual, false);
        body.transform.localScale = controllerBodySize;
        SetVisualMaterial(body, bodyMaterial);

        GameObject tip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        tip.name = "ControllerAimTip";
        tip.transform.SetParent(controllerVisual, false);
        tip.transform.localPosition = rayOriginLocalOffset;
        tip.transform.localScale = Vector3.one * 0.045f;
        SetVisualMaterial(tip, accentMaterial);
        controllerRayAnchor = tip.transform;

        GameObject trigger = GameObject.CreatePrimitive(PrimitiveType.Cube);
        trigger.name = "ControllerTrigger";
        trigger.transform.SetParent(controllerVisual, false);
        trigger.transform.localPosition = new Vector3(0f, -controllerBodySize.y * 0.65f, controllerBodySize.z * 0.08f);
        trigger.transform.localScale = new Vector3(0.035f, 0.025f, 0.06f);
        SetVisualMaterial(trigger, accentMaterial);

        controllerVisual.gameObject.SetActive(false);
    }

    private void MoveRuntimeObjectToOwnerScene(GameObject target)
    {
        Scene ownerScene = gameObject.scene;
        if (target == null || !ownerScene.IsValid() || !ownerScene.isLoaded || target.scene == ownerScene)
            return;

        SceneManager.MoveGameObjectToScene(target, ownerScene);
    }

    private static void SetVisualMaterial(GameObject target, Material material)
    {
        if (target.TryGetComponent(out Collider collider))
            Destroy(collider);

        if (target.TryGetComponent(out Renderer renderer))
            renderer.material = material;
    }

    private bool TryGetPointerRay(out Ray pointerRay, out bool hasController, out Vector3 controllerPosition, out Quaternion controllerRotation)
    {
        hasController = TryGetNodePose(pointerHand, out Vector3 handPosition, out Quaternion handRotation) ||
            TryGetInputSystemPointerPose(out handPosition, out handRotation);

        if (hasController)
        {
            MapTrackingPoseToScene(handPosition, handRotation, out controllerPosition, out controllerRotation);
            Quaternion visualRotation = GetControllerVisualRotation(controllerRotation);
            Vector3 visualPosition = GetControllerVisualPosition(controllerPosition, visualRotation);
            Vector3 rayOrigin = visualPosition + visualRotation * rayOriginLocalOffset;
            pointerRay = new Ray(rayOrigin, visualRotation * Vector3.forward);
            return true;
        }

        Transform cameraTransform = sceneCamera.transform;
        controllerPosition = cameraTransform.position +
            cameraTransform.right * 0.28f -
            cameraTransform.up * 0.22f +
            cameraTransform.forward * 0.18f;
        controllerRotation = cameraTransform.rotation;
        pointerRay = new Ray(controllerPosition, cameraTransform.forward);
        return true;
    }

    private void MapTrackingPoseToScene(Vector3 trackingPosition, Quaternion trackingRotation, out Vector3 scenePosition, out Quaternion sceneRotation)
    {
        if (TryGetNodePose(XRNode.CenterEye, out Vector3 headPosition, out Quaternion headRotation) ||
            TryGetNodePose(XRNode.Head, out headPosition, out headRotation))
        {
            Transform cameraTransform = sceneCamera.transform;
            Quaternion trackingToSceneRotation = cameraTransform.rotation * Quaternion.Inverse(headRotation);
            Vector3 trackingToScenePosition = cameraTransform.position - trackingToSceneRotation * headPosition;
            scenePosition = trackingToScenePosition + trackingToSceneRotation * trackingPosition;
            sceneRotation = trackingToSceneRotation * trackingRotation;
            return;
        }

        scenePosition = trackingPosition;
        sceneRotation = trackingRotation;
    }

    private Quaternion GetControllerVisualRotation(Quaternion controllerRotation)
    {
        return controllerRotation *
            Quaternion.Euler(rayRotationOffsetEuler) *
            Quaternion.Euler(controllerVisualRotationOffsetEuler);
    }

    private Vector3 GetControllerVisualPosition(Vector3 controllerPosition, Quaternion visualRotation)
    {
        return controllerPosition + visualRotation * controllerVisualPositionOffset;
    }

    private bool TryGetInputSystemPointerPose(out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;

#if ENABLE_INPUT_SYSTEM
        if (pointerPositionAction == null || pointerRotationAction == null)
            return false;

        if (pointerPositionAction.controls.Count == 0 || pointerRotationAction.controls.Count == 0)
            return false;

        if (isTrackedAction != null && isTrackedAction.controls.Count > 0 && !isTrackedAction.IsPressed())
            return false;

        position = pointerPositionAction.ReadValue<Vector3>();
        rotation = pointerRotationAction.ReadValue<Quaternion>();

        return rotation != default;
#else
        return false;
#endif
    }

    private PointerHit FindBestTarget(Ray pointerRay)
    {
        PointerHit bestHit = FindBestUiTarget(pointerRay);

        if (Physics.Raycast(pointerRay, out RaycastHit physicsHit, rayLength, physicsMask, QueryTriggerInteraction.Collide))
        {
            if (!bestHit.HasHit || physicsHit.distance < bestHit.Distance)
            {
                GameObject target = ExecuteEvents.GetEventHandler<IPointerClickHandler>(physicsHit.collider.gameObject);
                if (target == null)
                    target = physicsHit.collider.gameObject;

                Vector2 screenPoint = sceneCamera.WorldToScreenPoint(physicsHit.point);
                UpdatePointerPosition(screenPoint);
                bestHit = new PointerHit
                {
                    Target = target,
                    WorldPosition = physicsHit.point,
                    Distance = physicsHit.distance,
                    RaycastResult = new RaycastResult
                    {
                        gameObject = target,
                        worldPosition = physicsHit.point,
                        worldNormal = physicsHit.normal,
                        screenPosition = screenPoint,
                        distance = physicsHit.distance
                    }
                };
            }
        }

        return bestHit;
    }

    private PointerHit FindBestUiTarget(Ray pointerRay)
    {
        PointerHit bestHit = default;

        for (int i = 0; i < graphicRaycasters.Count; i++)
        {
            GraphicRaycaster raycaster = graphicRaycasters[i];
            if (raycaster == null || !raycaster.isActiveAndEnabled)
                continue;

            Canvas canvas = raycaster.GetComponent<Canvas>();
            if (canvas == null || canvas.renderMode != RenderMode.WorldSpace)
                continue;

            RectTransform canvasRect = canvas.transform as RectTransform;
            if (canvasRect == null)
                continue;

            Plane canvasPlane = new Plane(canvasRect.forward, canvasRect.position);
            if (!canvasPlane.Raycast(pointerRay, out float enter) || enter < 0f || enter > rayLength)
                continue;

            Vector3 hitWorld = pointerRay.GetPoint(enter);
            Vector3 localPoint = canvasRect.InverseTransformPoint(hitWorld);
            if (!canvasRect.rect.Contains(new Vector2(localPoint.x, localPoint.y)))
                continue;

            Camera eventCamera = canvas.worldCamera != null ? canvas.worldCamera : sceneCamera;
            Vector2 screenPoint = eventCamera.WorldToScreenPoint(hitWorld);
            UpdatePointerPosition(screenPoint);

            raycastResults.Clear();
            raycaster.Raycast(pointerEventData, raycastResults);
            for (int resultIndex = 0; resultIndex < raycastResults.Count; resultIndex++)
            {
                RaycastResult result = raycastResults[resultIndex];
                GameObject target = ExecuteEvents.GetEventHandler<IPointerClickHandler>(result.gameObject);
                if (target == null)
                    target = ExecuteEvents.GetEventHandler<ISelectHandler>(result.gameObject);
                if (target == null)
                    continue;

                if (target.TryGetComponent(out Selectable selectable) &&
                    (!selectable.IsActive() || !selectable.IsInteractable()))
                    continue;

                result.worldPosition = hitWorld;
                result.screenPosition = screenPoint;
                result.distance = enter;

                if (bestHit.HasHit && enter >= bestHit.Distance)
                    continue;

                bestHit = new PointerHit
                {
                    Target = target,
                    WorldPosition = hitWorld,
                    Distance = enter,
                    RaycastResult = result
                };
                break;
            }
        }

        return bestHit;
    }

    private void UpdatePointerPosition(Vector2 screenPoint)
    {
        pointerEventData.delta = hasLastPointerPosition
            ? screenPoint - lastPointerPosition
            : Vector2.zero;
        pointerEventData.position = screenPoint;
        lastPointerPosition = screenPoint;
        hasLastPointerPosition = true;
    }

    private bool IsClickPressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (clickAction != null && clickAction.controls.Count > 0)
        {
            if (clickAction.IsPressed())
                return true;

            try
            {
                if (clickAction.ReadValue<float>() >= triggerThreshold)
                    return true;
            }
            catch (System.InvalidOperationException)
            {
                // Some controller layouts expose button controls that cannot be read as floats.
            }
        }
#endif

        UnityEngine.XR.InputDevice device = InputDevices.GetDeviceAtXRNode(pointerHand);
        if (!device.isValid)
            return false;

        if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out bool triggerButton) && triggerButton)
            return true;

        if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.trigger, out float trigger) && trigger >= triggerThreshold)
            return true;

        return device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.gripButton, out bool gripButton) && gripButton;
    }

    private bool ConsumeMenuButtonPressed()
    {
        bool pressed = false;

#if ENABLE_INPUT_SYSTEM
        if (menuAction != null && menuAction.controls.Count > 0 && menuAction.IsPressed())
            pressed = true;
#endif

        if (IsMenuButtonPressed(pointerHand) ||
            IsMenuButtonPressed(XRNode.RightHand) ||
            IsMenuButtonPressed(XRNode.LeftHand))
            pressed = true;

        bool consumed = pressed && !wasMenuButtonPressed;
        wasMenuButtonPressed = pressed;
        return consumed;
    }

    private static bool IsMenuButtonPressed(XRNode hand)
    {
        UnityEngine.XR.InputDevice device = InputDevices.GetDeviceAtXRNode(hand);
        if (!device.isValid)
            return false;

        if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out bool primaryButton) && primaryButton)
            return true;

        if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondaryButton, out bool secondaryButton) && secondaryButton)
            return true;

        if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.menuButton, out bool menuButton) && menuButton)
            return true;

        if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxisClick, out bool primaryAxisClick) && primaryAxisClick)
            return true;

        return device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondary2DAxisClick, out bool secondaryAxisClick) && secondaryAxisClick;
    }

    private void UpdateHover(GameObject target, RaycastResult raycastResult)
    {
        pointerEventData.pointerCurrentRaycast = raycastResult;

        if (target == hoveredObject)
        {
            if (hoveredObject != null)
                ExecuteEvents.Execute(hoveredObject, pointerEventData, ExecuteEvents.pointerMoveHandler);

            return;
        }

        ClearHover();
        hoveredObject = target;
        pointerEventData.pointerEnter = hoveredObject;

        if (hoveredObject == null)
            return;

        ExecuteEvents.Execute(hoveredObject, pointerEventData, ExecuteEvents.pointerEnterHandler);
        ExecuteEvents.Execute(hoveredObject, pointerEventData, ExecuteEvents.pointerMoveHandler);

        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(hoveredObject, pointerEventData);
    }

    private void Press(GameObject target)
    {
        if (target == null)
            return;

        pointerEventData.pressPosition = pointerEventData.position;
        pointerEventData.pointerPressRaycast = pointerEventData.pointerCurrentRaycast;
        pointerEventData.eligibleForClick = true;
        pointerEventData.dragging = false;
        pointerEventData.useDragThreshold = true;
        pointerEventData.clickTime = Time.unscaledTime;
        pointerEventData.clickCount = 1;

        GameObject pointerPress = ExecuteEvents.ExecuteHierarchy(target, pointerEventData, ExecuteEvents.pointerDownHandler);
        if (pointerPress == null)
            pointerPress = ExecuteEvents.GetEventHandler<IPointerClickHandler>(target);

        pointerEventData.pointerPress = pointerPress;
        pointerEventData.rawPointerPress = target;
        pointerEventData.pointerDrag = ExecuteEvents.GetEventHandler<IDragHandler>(target);

        if (pointerEventData.pointerDrag != null)
            ExecuteEvents.Execute(pointerEventData.pointerDrag, pointerEventData, ExecuteEvents.initializePotentialDrag);

        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(target, pointerEventData);
    }

    private void Release(GameObject target)
    {
        GameObject pointerPress = pointerEventData.pointerPress;

        if (pointerPress != null)
            ExecuteEvents.Execute(pointerPress, pointerEventData, ExecuteEvents.pointerUpHandler);

        GameObject clickHandler = target != null
            ? ExecuteEvents.GetEventHandler<IPointerClickHandler>(target)
            : null;

        if (pointerPress != null && pointerPress == clickHandler && pointerEventData.eligibleForClick)
            ExecuteEvents.Execute(pointerPress, pointerEventData, ExecuteEvents.pointerClickHandler);

        if (pointerEventData.pointerDrag != null && pointerEventData.dragging)
            ExecuteEvents.Execute(pointerEventData.pointerDrag, pointerEventData, ExecuteEvents.endDragHandler);

        pointerEventData.eligibleForClick = false;
        pointerEventData.pointerPress = null;
        pointerEventData.rawPointerPress = null;
        pointerEventData.pointerDrag = null;
        pointerEventData.dragging = false;
    }

    private void CancelPress()
    {
        if (pointerEventData != null && (wasPressed || pointerEventData.pointerPress != null))
            Release(null);

        wasPressed = false;
    }

    private void ClearHover()
    {
        if (hoveredObject != null && pointerEventData != null)
            ExecuteEvents.Execute(hoveredObject, pointerEventData, ExecuteEvents.pointerExitHandler);

        hoveredObject = null;
        if (pointerEventData != null)
            pointerEventData.pointerEnter = null;
    }

    private void UpdateVisual(Ray pointerRay, bool hasHit, Vector3 endPoint)
    {
        if (pointerLine != null)
        {
            pointerLine.SetPosition(0, pointerRay.origin);
            pointerLine.SetPosition(1, endPoint);
            pointerLine.enabled = true;
        }

        if (reticle != null)
        {
            reticle.gameObject.SetActive(hasHit);
            reticle.position = endPoint;
        }
    }

    private void UpdateControllerVisual(Vector3 position, Quaternion rotation, bool visible)
    {
        if (controllerVisual == null)
            return;

        controllerVisual.gameObject.SetActive(visible);
        if (!visible)
            return;

        Quaternion visualRotation = GetControllerVisualRotation(rotation);
        Vector3 visualPosition = GetControllerVisualPosition(position, visualRotation);
        controllerVisual.SetPositionAndRotation(visualPosition, visualRotation);

        if (controllerRayAnchor != null)
            controllerRayAnchor.localPosition = rayOriginLocalOffset;
    }

    private static bool TryGetNodePose(XRNode node, out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;

        UnityEngine.XR.InputDevice device = InputDevices.GetDeviceAtXRNode(node);
        if (!device.isValid)
            return false;

        bool hasPosition = device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out position);
        bool hasRotation = device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out rotation);
        return hasPosition && hasRotation;
    }

    private struct PointerHit
    {
        public GameObject Target;
        public Vector3 WorldPosition;
        public float Distance;
        public RaycastResult RaycastResult;

        public bool HasHit => Target != null;
    }
}

internal static class QuestCaptionStatusReporter
{
    private const string CaptionStatusTextName = "CaptionStatusText";
    private const string ExistingStatusTextName = "StatusText";
    private static Text registeredStatusText;
    private static string latestStatus = "Caption: Ready";

    public static void Register(Text statusText)
    {
        registeredStatusText = statusText;
        ApplyToRegisteredText();
    }

    public static void SetStatus(string message)
    {
        latestStatus = string.IsNullOrWhiteSpace(message) ? "Caption: Ready" : message.Trim();
        ApplyToRegisteredText();
        ApplyToSceneTextObjects();
        Debug.Log($"[QuestCaptionStatus] {latestStatus}");
    }

    private static void ApplyToRegisteredText()
    {
        if (registeredStatusText != null)
            registeredStatusText.text = latestStatus;
    }

    private static void ApplyToSceneTextObjects()
    {
        Text[] uiTexts = Object.FindObjectsOfType<Text>(true);
        for (int i = 0; i < uiTexts.Length; i++)
        {
            Text uiText = uiTexts[i];
            if (uiText == null)
                continue;

            if (uiText.name == CaptionStatusTextName || uiText.name == ExistingStatusTextName)
                uiText.text = latestStatus;
        }

        TMP_Text[] tmpTexts = Object.FindObjectsOfType<TMP_Text>(true);
        for (int i = 0; i < tmpTexts.Length; i++)
        {
            TMP_Text tmpText = tmpTexts[i];
            if (tmpText == null)
                continue;

            if (tmpText.name == CaptionStatusTextName || tmpText.name == ExistingStatusTextName)
                tmpText.text = latestStatus;
        }
    }
}

internal static class QuestSttSmokeTestIntentLauncher
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (!ShouldRunOnCurrentPlatform())
            return;

        float seconds = ReadSmokeSeconds();
        if (seconds <= 0f)
            return;

        GameObject driverObject = new GameObject("QuestSttSmokeTestIntentLauncher");
        Object.DontDestroyOnLoad(driverObject);
        driverObject.AddComponent<Driver>().Initialize(seconds);
    }

    private static bool ShouldRunOnCurrentPlatform()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        return true;
#else
        return false;
#endif
    }

    private static float ReadSmokeSeconds()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            using (AndroidJavaObject intent = activity.Call<AndroidJavaObject>("getIntent"))
            {
                string value = intent.Call<string>("getStringExtra", "xr_stt_smoke_seconds");
                if (float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float seconds))
                    return seconds;
            }
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"[QuestSttSmokeTestIntentLauncher] Could not read smoke-test intent extra: {exception.Message}");
        }
#endif

        return 0f;
    }

    private sealed class Driver : MonoBehaviour
    {
        private float seconds;

        public void Initialize(float recordSeconds)
        {
            seconds = Mathf.Clamp(recordSeconds, 1f, 10f);
        }

        private IEnumerator Start()
        {
            yield return new WaitForSeconds(1.2f);

            SpeechRecorder recorder = FindRecorderOrCreateRuntime();
            if (recorder == null)
            {
                Debug.LogError("[QuestSttSmokeTestIntentLauncher] Could not create SpeechRecorder for smoke test.");
                yield break;
            }

            recorder.EnsureRuntimeReferences();
            bool started = recorder.TryStartRecording();
            Debug.Log($"[QuestSttSmokeTestIntentLauncher] STT smoke recording started={started}, seconds={seconds}, status={recorder.StatusMessage}");
            if (!started)
                yield break;

            yield return new WaitForSeconds(seconds);

            bool stopped = recorder.TryStopRecording();
            Debug.Log($"[QuestSttSmokeTestIntentLauncher] STT smoke recording stopped={stopped}, status={recorder.StatusMessage}");
        }

        private static SpeechRecorder FindRecorderOrCreateRuntime()
        {
            SpeechRecorder[] recorders = Object.FindObjectsOfType<SpeechRecorder>(true);
            for (int i = 0; i < recorders.Length; i++)
            {
                if (recorders[i] != null && recorders[i].isActiveAndEnabled)
                    return recorders[i];
            }

            GameObject recorderObject = new GameObject("QuestRuntimeSpeechRecorder");
            SpeechToTextClient client = recorderObject.AddComponent<SpeechToTextClient>();
            SpeechRecorder recorder = recorderObject.AddComponent<SpeechRecorder>();
            client.EnsureRuntimeReferences();
            recorder.Configure(client, FindTextObject("StatusText"));
            return recorder;
        }

        private static TMP_Text FindTextObject(string objectName)
        {
            TMP_Text[] textObjects = Object.FindObjectsOfType<TMP_Text>(true);
            for (int i = 0; i < textObjects.Length; i++)
            {
                TMP_Text textObject = textObjects[i];
                if (textObject != null && textObject.name == objectName)
                    return textObject;
            }

            return null;
        }
    }
}

internal static class QuestExternalSceneViewBootstrap
{
    public static void ConfigureLoadedScenes(string reason)
    {
        if (!ShouldRunOnCurrentPlatform())
            return;

        for (int i = 0; i < SceneManager.sceneCount; i++)
            ConfigureScene(SceneManager.GetSceneAt(i), reason);
    }

    public static void ConfigureScene(Scene scene, string reason)
    {
        if (!ShouldRunOnCurrentPlatform() || !scene.IsValid() || !scene.isLoaded)
            return;

        if (IsOceanScene(scene))
            ConfigureOceanScene(scene, reason);
        else if (IsSpaceScene(scene))
            ConfigureSpaceScene(scene, reason);
    }

    private static void ConfigureOceanScene(Scene scene, string reason)
    {
        Camera camera = FindPrimarySceneCamera(scene);
        if (camera == null)
        {
            Debug.LogWarning("[QuestExternalSceneViewBootstrap] Ocean scene has no display camera to configure.");
            return;
        }

        bool changed = false;

        if (!camera.gameObject.activeSelf)
        {
            camera.gameObject.SetActive(true);
            changed = true;
        }

        if (!camera.enabled)
        {
            camera.enabled = true;
            changed = true;
        }

        if (camera.stereoTargetEye != StereoTargetEyeMask.Both)
        {
            camera.stereoTargetEye = StereoTargetEyeMask.Both;
            changed = true;
        }

        if (camera.targetTexture != null)
        {
            camera.targetTexture = null;
            changed = true;
        }

        EnsureOceanSurfaceRootActive(scene);
        QuestExternalSceneVisuals.ConfigureOcean(scene, camera, reason);

        if (changed)
            Debug.Log($"[QuestExternalSceneViewBootstrap] Ocean camera display settings configured for Quest. camera='{camera.name}', reason={reason}");
    }

    private static void ConfigureSpaceScene(Scene scene, string reason)
    {
        Camera camera = FindPrimarySceneCamera(scene);
        if (camera == null)
            camera = QuestExternalSceneVisuals.CreateSceneCamera(scene, "Space Main Camera", new Vector3(0f, 1.65f, -5.5f), Quaternion.identity);

        if (camera == null)
        {
            Debug.LogWarning("[QuestExternalSceneViewBootstrap] Space scene has no display camera to configure.");
            return;
        }

        bool changed = false;

        if (!camera.gameObject.activeSelf)
        {
            camera.gameObject.SetActive(true);
            changed = true;
        }

        if (!camera.enabled)
        {
            camera.enabled = true;
            changed = true;
        }

        if (camera.stereoTargetEye != StereoTargetEyeMask.Both)
        {
            camera.stereoTargetEye = StereoTargetEyeMask.Both;
            changed = true;
        }

        if (camera.targetTexture != null)
        {
            camera.targetTexture = null;
            changed = true;
        }

        QuestExternalSceneVisuals.ConfigureSpace(scene, camera, reason);

        if (changed)
            Debug.Log($"[QuestExternalSceneViewBootstrap] Space camera display settings configured for Quest. camera='{camera.name}', reason={reason}");
    }

    private static void EnsureOceanSurfaceRootActive(Scene scene)
    {
        GameObject surfaceRoot = FindSceneObject(scene, "SUIMONO_Surface");
        if (surfaceRoot != null && !surfaceRoot.activeSelf)
            surfaceRoot.SetActive(true);

        GameObject moduleRoot = FindSceneObject(scene, "SUIMONO_Module");
        if (moduleRoot != null && !moduleRoot.activeSelf)
            moduleRoot.SetActive(true);
    }

    private static Camera FindPrimarySceneCamera(Scene scene)
    {
        Camera fallback = null;
        Camera[] cameras = Object.FindObjectsOfType<Camera>(true);

        for (int i = 0; i < cameras.Length; i++)
        {
            Camera camera = cameras[i];
            if (camera == null || camera.gameObject.scene != scene || IsSuimonoHelperCamera(camera))
                continue;

            if (camera.CompareTag("MainCamera") ||
                string.Equals(camera.gameObject.name, "Main Camera", System.StringComparison.OrdinalIgnoreCase))
                return camera;

            fallback ??= camera;
        }

        return fallback;
    }

    private static GameObject FindSceneObject(Scene scene, string objectName)
    {
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            GameObject result = FindChildRecursive(roots[i], objectName);
            if (result != null)
                return result;
        }

        return null;
    }

    private static GameObject FindChildRecursive(GameObject root, string objectName)
    {
        if (root == null)
            return null;

        if (string.Equals(root.name, objectName, System.StringComparison.OrdinalIgnoreCase))
            return root;

        Transform rootTransform = root.transform;
        for (int i = 0; i < rootTransform.childCount; i++)
        {
            GameObject result = FindChildRecursive(rootTransform.GetChild(i).gameObject, objectName);
            if (result != null)
                return result;
        }

        return null;
    }

    private static bool IsOceanScene(Scene scene)
    {
        string scenePath = scene.path.Replace('\\', '/');
        return string.Equals(scene.name, "Ocean", System.StringComparison.OrdinalIgnoreCase) ||
            scenePath.EndsWith("Assets/Scenes/Ocean.unity", System.StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSpaceScene(Scene scene)
    {
        string scenePath = scene.path.Replace('\\', '/');
        return string.Equals(scene.name, "Space", System.StringComparison.OrdinalIgnoreCase) ||
            scenePath.EndsWith("Assets/Scenes/Space.unity", System.StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSuimonoHelperCamera(Camera camera)
    {
        return camera != null &&
            camera.gameObject.name.IndexOf("cam_Suimono", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool ShouldRunOnCurrentPlatform()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        return true;
#else
        return false;
#endif
    }
}

internal static class QuestExternalSceneVisuals
{
    private const string OceanRootName = "QuestOceanUnderwaterVisuals";
    private const string SpaceRootName = "QuestSpaceVisuals";

    public static void ConfigureOcean(Scene scene, Camera camera, string reason)
    {
        if (camera == null || FindSceneObject(scene, OceanRootName) != null)
            return;

        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.004f, 0.025f, 0.08f, 1f);
        camera.nearClipPlane = Mathf.Min(camera.nearClipPlane, 0.05f);
        camera.farClipPlane = Mathf.Max(camera.farClipPlane, 1000f);

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.015f, 0.24f, 0.34f, 1f);
        RenderSettings.fogDensity = 0.018f;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.035f, 0.18f, 0.24f, 1f);

        GameObject root = new GameObject(OceanRootName);
        MoveToScene(root, scene);
        root.transform.SetParent(camera.transform, false);

        Material backdropMaterial = CreateUnlitMaterial(new Color(0.015f, 0.12f, 0.24f, 1f), transparent: false);
        backdropMaterial.mainTexture = CreateOceanBackdropTexture();
        backdropMaterial.mainTextureScale = new Vector2(1.45f, 1f);
        Material waterMaterial = CreateUnlitMaterial(new Color(0.2f, 0.92f, 1f, 0.58f), transparent: true);
        waterMaterial.mainTexture = CreateWaterTexture();
        waterMaterial.mainTextureScale = new Vector2(2.1f, 1.2f);
        Material shimmerMaterial = CreateUnlitMaterial(new Color(0.78f, 1f, 0.95f, 0.24f), transparent: true);
        shimmerMaterial.mainTexture = CreateCausticTexture();
        shimmerMaterial.mainTextureScale = new Vector2(3.1f, 1.1f);

        Transform backdrop = CreateCameraQuad("BlueOceanBackdrop", root.transform, new Vector3(0f, 0f, 16f), new Vector2(24f, 13.5f), backdropMaterial);
        Transform surface = CreateCameraQuad("WaterSurfaceTexture", root.transform, new Vector3(0f, 1.35f, 10.8f), new Vector2(20f, 7.2f), waterMaterial);
        Transform shimmer = CreateCameraQuad("WaterSurfaceShimmer", root.transform, new Vector3(0f, 0.65f, 10.6f), new Vector2(19f, 4.4f), shimmerMaterial);

        root.AddComponent<QuestTextureDrift>().Initialize(waterMaterial, shimmerMaterial);
        GameObject[] animalPrefabs = GetOceanAnimalPrefabs(scene);
        CreateOceanParticles(root.transform);
        if (!CreateOceanPrefabFishSchool(root.transform, animalPrefabs))
            CreateOceanFishSchool(root.transform);

        if (!CreateOceanPrefabBigFishPasses(root.transform, animalPrefabs))
            CreateOceanBigFishPasses(root.transform);

        DisableOceanForegroundDistractors(scene, root.transform);
        root.AddComponent<QuestSceneRendererSuppressor>().Initialize(scene, root.transform, 8f);

        Debug.Log($"[QuestExternalSceneVisuals] Ocean underwater fallback visuals created. reason={reason}, backdrop={backdrop.name}, surface={surface.name}, shimmer={shimmer.name}, prefabAnimals={animalPrefabs.Length}");
    }

    public static void ConfigureSpace(Scene scene, Camera camera, string reason)
    {
        if (camera == null || FindSceneObject(scene, SpaceRootName) != null)
            return;

        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.005f, 0.007f, 0.018f, 1f);
        camera.nearClipPlane = Mathf.Min(camera.nearClipPlane, 0.05f);
        camera.farClipPlane = Mathf.Max(camera.farClipPlane, 1000f);

        RenderSettings.fog = false;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.045f, 0.052f, 0.09f, 1f);

        GameObject root = new GameObject(SpaceRootName);
        MoveToScene(root, scene);
        root.transform.SetParent(camera.transform, false);

        Material backdropMaterial = CreateUnlitMaterial(new Color(0.005f, 0.006f, 0.018f, 1f), transparent: false);
        backdropMaterial.mainTexture = CreateSpaceTexture();
        backdropMaterial.mainTextureScale = new Vector2(1.5f, 1f);
        CreateCameraQuad("SpaceBackdrop", root.transform, new Vector3(0f, 0f, 16f), new Vector2(24f, 13.5f), backdropMaterial);

        CreateStarField(root.transform);
        CreateSpaceBody(root.transform, "DistantPlanet", new Vector3(4.7f, 2.3f, 13.5f), new Vector3(1.8f, 1.8f, 1.8f), new Color(0.2f, 0.5f, 1f, 1f));
        CreateSpaceBody(root.transform, "WarmMoon", new Vector3(-4.2f, -1.2f, 11.5f), new Vector3(0.75f, 0.75f, 0.75f), new Color(0.95f, 0.72f, 0.42f, 1f));
        CreateSpaceRing(root.transform);

        Debug.Log($"[QuestExternalSceneVisuals] Space fallback visuals created. reason={reason}");
    }

    public static Camera CreateSceneCamera(Scene scene, string cameraName, Vector3 position, Quaternion rotation)
    {
        GameObject cameraObject = new GameObject(cameraName);
        MoveToScene(cameraObject, scene);
        cameraObject.tag = "MainCamera";
        cameraObject.transform.SetPositionAndRotation(position, rotation);

        Camera camera = cameraObject.AddComponent<Camera>();
        camera.stereoTargetEye = StereoTargetEyeMask.Both;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.fieldOfView = 70f;
        camera.nearClipPlane = 0.05f;
        camera.farClipPlane = 1000f;

        if (Object.FindObjectOfType<AudioListener>() == null)
            cameraObject.AddComponent<AudioListener>();

        return camera;
    }

    private static Transform CreateCameraQuad(string name, Transform parent, Vector3 localPosition, Vector2 size, Material material)
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

    private static void CreateOceanParticles(Transform parent)
    {
        Material particleMaterial = CreateUnlitMaterial(new Color(0.22f, 0.88f, 1f, 0.22f), transparent: true);

        for (int i = 0; i < 54; i++)
        {
            float x = Mathf.Lerp(-8.5f, 8.5f, Halton(i + 1, 2));
            float y = Mathf.Lerp(-3.8f, 3.8f, Halton(i + 1, 3));
            float z = Mathf.Lerp(5.2f, 14.5f, Halton(i + 1, 5));
            float size = Mathf.Lerp(0.012f, 0.04f, Halton(i + 1, 7));
            Transform speck = CreateCameraQuad($"UnderwaterSpeck_{i:00}", parent, new Vector3(x, y, z), new Vector2(size, size), particleMaterial);
            speck.localRotation = Quaternion.Euler(0f, 180f, Mathf.Lerp(0f, 180f, Halton(i + 1, 11)));
        }
    }

    private static GameObject[] GetOceanAnimalPrefabs(Scene scene)
    {
        GameObject summonObject = FindSceneObject(scene, "Summon");
        if (summonObject == null)
            return System.Array.Empty<GameObject>();

        SummonAnimal summonAnimal = summonObject.GetComponent<SummonAnimal>();
        if (summonAnimal == null || summonAnimal.animals == null || summonAnimal.animals.Length == 0)
            return System.Array.Empty<GameObject>();

        List<GameObject> prefabs = new List<GameObject>();
        for (int i = 0; i < summonAnimal.animals.Length; i++)
        {
            GameObject prefab = summonAnimal.animals[i];
            if (prefab != null && !prefabs.Contains(prefab))
                prefabs.Add(prefab);
        }

        return prefabs.ToArray();
    }

    private static bool CreateOceanPrefabFishSchool(Transform parent, GameObject[] prefabs)
    {
        if (prefabs == null || prefabs.Length == 0)
            return false;

        for (int i = 0; i < 12; i++)
        {
            GameObject prefab = ChooseOceanAnimalPrefab(prefabs, i, preferLargeFish: false);
            if (prefab == null)
                continue;

            Vector3 basePosition = new Vector3(
                Mathf.Lerp(-5.8f, 5.8f, Halton(i + 2, 2)),
                Mathf.Lerp(-2.0f, 2.1f, Halton(i + 5, 3)),
                Mathf.Lerp(6.2f, 12.4f, Halton(i + 7, 5)));

            float scale = Mathf.Lerp(0.45f, 0.9f, Halton(i + 3, 7));
            float speed = Mathf.Lerp(0.35f, 0.85f, Halton(i + 4, 11));
            float width = Mathf.Lerp(0.55f, 1.6f, Halton(i + 6, 13));
            float height = Mathf.Lerp(0.06f, 0.24f, Halton(i + 8, 17));
            float phase = Mathf.Lerp(0f, Mathf.PI * 2f, Halton(i + 9, 19));

            GameObject fish = InstantiateOceanPrefabFish(prefab, parent, $"OceanPrefabFish_{i:00}", basePosition, scale);
            if (fish != null)
                fish.AddComponent<QuestPrefabFishSwim>().Initialize(basePosition, speed, width, height, phase);
        }

        return true;
    }

    private static bool CreateOceanPrefabBigFishPasses(Transform parent, GameObject[] prefabs)
    {
        if (prefabs == null || prefabs.Length == 0)
            return false;

        CreateOceanPrefabBigFish(parent, prefabs, "OceanPrefabBigFish_LeftPass", true, 0f, 1.72f, 5.2f, 0.85f, 0);
        CreateOceanPrefabBigFish(parent, prefabs, "OceanPrefabBigFish_RightPass", false, 4.4f, 1.55f, 6.6f, -0.25f, 1);
        CreateOceanPrefabBigFish(parent, prefabs, "OceanPrefabBigFish_ClosePass", true, 8.1f, 2.05f, 4.3f, 0.2f, 2);
        return true;
    }

    private static void CreateOceanPrefabBigFish(Transform parent, GameObject[] prefabs, string name, bool leftToRight, float phase, float scale, float depth, float height, int variant)
    {
        GameObject prefab = ChooseOceanAnimalPrefab(prefabs, variant, preferLargeFish: true);
        if (prefab == null)
            return;

        GameObject fish = InstantiateOceanPrefabFish(prefab, parent, name, Vector3.zero, scale);
        if (fish != null)
            fish.AddComponent<QuestPrefabFishPass>().Initialize(leftToRight, phase, depth, height);
    }

    private static GameObject InstantiateOceanPrefabFish(GameObject prefab, Transform parent, string name, Vector3 localPosition, float scale)
    {
        if (prefab == null || parent == null)
            return null;

        GameObject fish = Object.Instantiate(prefab, parent, false);
        fish.name = name;
        fish.transform.localPosition = localPosition;
        fish.transform.localRotation = Quaternion.identity;
        fish.transform.localScale = Vector3.one * Mathf.Max(0.01f, scale);

        PrepareOceanPrefabFish(fish);
        return fish;
    }

    private static void PrepareOceanPrefabFish(GameObject fish)
    {
        if (fish == null)
            return;

        SetLayerRecursively(fish, LayerMask.NameToLayer("Ignore Raycast"));

        Collider[] colliders = fish.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
                Object.Destroy(colliders[i]);
        }

        Rigidbody[] rigidbodies = fish.GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < rigidbodies.Length; i++)
        {
            Rigidbody rigidbody = rigidbodies[i];
            if (rigidbody == null)
                continue;

            rigidbody.isKinematic = true;
            rigidbody.useGravity = false;
            rigidbody.detectCollisions = false;
        }

        Animator[] animators = fish.GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < animators.Length; i++)
        {
            Animator animator = animators[i];
            if (animator == null)
                continue;

            animator.enabled = true;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        Renderer[] renderers = fish.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
                continue;

            renderer.enabled = true;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
    }

    private static GameObject ChooseOceanAnimalPrefab(GameObject[] prefabs, int variant, bool preferLargeFish)
    {
        if (prefabs == null || prefabs.Length == 0)
            return null;

        string[] preferredNames = preferLargeFish
            ? new[] { "Fish_6_v1", "Fish_6_v3", "Dolfin_v1", "fish_1_v2" }
            : new[] { "fish_1_v1", "fish_1_v2", "fish_2_v1", "Fish_3_v1", "Fish_4_v1", "Fish_6_v1", "Fish_6_v3", "Dolfin_v1" };

        int matches = 0;
        for (int i = 0; i < preferredNames.Length; i++)
        {
            GameObject candidate = FindPrefabByName(prefabs, preferredNames[i]);
            if (candidate == null)
                continue;

            if (matches == variant % Mathf.Max(1, CountMatchingPrefabs(prefabs, preferredNames)))
                return candidate;

            matches++;
        }

        return prefabs[Mathf.Abs(variant) % prefabs.Length];
    }

    private static int CountMatchingPrefabs(GameObject[] prefabs, string[] preferredNames)
    {
        int count = 0;
        for (int i = 0; i < preferredNames.Length; i++)
        {
            if (FindPrefabByName(prefabs, preferredNames[i]) != null)
                count++;
        }

        return count;
    }

    private static GameObject FindPrefabByName(GameObject[] prefabs, string prefabName)
    {
        if (prefabs == null || string.IsNullOrWhiteSpace(prefabName))
            return null;

        for (int i = 0; i < prefabs.Length; i++)
        {
            GameObject prefab = prefabs[i];
            if (prefab != null && string.Equals(prefab.name, prefabName, System.StringComparison.OrdinalIgnoreCase))
                return prefab;
        }

        return null;
    }

    private static void CreateOceanFishSchool(Transform parent)
    {
        Color[] colors =
        {
            new Color(0.26f, 0.88f, 1f, 1f),
            new Color(0.88f, 0.55f, 0.28f, 1f),
            new Color(0.48f, 0.76f, 1f, 1f),
            new Color(0.98f, 0.86f, 0.42f, 1f),
            new Color(0.24f, 0.64f, 0.95f, 1f)
        };

        for (int i = 0; i < 12; i++)
        {
            Vector3 basePosition = new Vector3(
                Mathf.Lerp(-5.8f, 5.8f, Halton(i + 2, 2)),
                Mathf.Lerp(-2.0f, 2.1f, Halton(i + 5, 3)),
                Mathf.Lerp(6.2f, 12.4f, Halton(i + 7, 5)));

            float scale = Mathf.Lerp(0.18f, 0.36f, Halton(i + 3, 7));
            float speed = Mathf.Lerp(0.45f, 0.95f, Halton(i + 4, 11));
            float width = Mathf.Lerp(0.45f, 1.45f, Halton(i + 6, 13));
            float height = Mathf.Lerp(0.05f, 0.22f, Halton(i + 8, 17));
            float phase = Mathf.Lerp(0f, Mathf.PI * 2f, Halton(i + 9, 19));
            CreateOceanFish(parent, $"OceanFish_{i:00}", basePosition, scale, colors[i % colors.Length], speed, width, height, phase);
        }
    }

    private static void CreateOceanFish(Transform parent, string name, Vector3 localPosition, float scale, Color color, float speed, float swimWidth, float swimHeight, float phase)
    {
        GameObject fish = new GameObject(name);
        SetLayerRecursively(fish, LayerMask.NameToLayer("Ignore Raycast"));
        fish.transform.SetParent(parent, false);
        fish.transform.localPosition = localPosition;
        fish.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        fish.transform.localScale = Vector3.one * scale;

        Material bodyMaterial = CreateUnlitMaterial(color, transparent: false);
        Material finMaterial = CreateUnlitMaterial(Color.Lerp(color, Color.white, 0.28f), transparent: false);

        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        body.name = "Body";
        SetNonInteractive(body);
        body.transform.SetParent(fish.transform, false);
        body.transform.localPosition = Vector3.zero;
        body.transform.localScale = new Vector3(0.95f, 0.32f, 0.18f);
        if (body.TryGetComponent(out Renderer bodyRenderer))
            bodyRenderer.sharedMaterial = bodyMaterial;

        GameObject tail = new GameObject("Tail");
        SetLayerRecursively(tail, LayerMask.NameToLayer("Ignore Raycast"));
        tail.transform.SetParent(fish.transform, false);
        tail.transform.localPosition = new Vector3(-0.55f, 0f, 0f);
        tail.transform.localScale = new Vector3(0.54f, 0.46f, 1f);
        MeshFilter tailFilter = tail.AddComponent<MeshFilter>();
        MeshRenderer tailRenderer = tail.AddComponent<MeshRenderer>();
        tailFilter.sharedMesh = CreateFishTailMesh();
        tailRenderer.sharedMaterial = finMaterial;

        GameObject fin = new GameObject("TopFin");
        SetLayerRecursively(fin, LayerMask.NameToLayer("Ignore Raycast"));
        fin.transform.SetParent(fish.transform, false);
        fin.transform.localPosition = new Vector3(0f, 0.2f, 0f);
        fin.transform.localRotation = Quaternion.Euler(0f, 0f, 180f);
        fin.transform.localScale = new Vector3(0.32f, 0.18f, 1f);
        MeshFilter finFilter = fin.AddComponent<MeshFilter>();
        MeshRenderer finRenderer = fin.AddComponent<MeshRenderer>();
        finFilter.sharedMesh = CreateFishTailMesh();
        finRenderer.sharedMaterial = finMaterial;

        fish.AddComponent<QuestFishSwim>().Initialize(localPosition, speed, swimWidth, swimHeight, phase);
    }

    private static void CreateOceanBigFishPasses(Transform parent)
    {
        CreateOceanBigFish(parent, "OceanBigFish_LeftPass", new Color(1f, 0.08f, 0.03f, 1f), new Color(0.02f, 0.82f, 0.72f, 1f), true, 0f, 1.32f, 6.4f, 0.9f);
        CreateOceanBigFish(parent, "OceanBigFish_RightPass", new Color(1f, 0.18f, 0.04f, 1f), new Color(0.03f, 0.72f, 0.92f, 1f), false, 4.6f, 1.18f, 7.2f, -0.35f);
        CreateOceanBigFish(parent, "OceanBigFish_ClosePass", new Color(0.95f, 0.04f, 0.02f, 1f), new Color(0.05f, 0.95f, 0.88f, 1f), true, 8.2f, 1.55f, 4.8f, 0.25f);
    }

    private static void CreateOceanBigFish(Transform parent, string name, Color bodyColor, Color patternColor, bool leftToRight, float phase, float scale, float depth, float height)
    {
        GameObject fish = new GameObject(name);
        SetLayerRecursively(fish, LayerMask.NameToLayer("Ignore Raycast"));
        fish.transform.SetParent(parent, false);
        fish.transform.localScale = Vector3.one * scale;

        Material bodyMaterial = CreateUnlitMaterial(bodyColor, transparent: false);
        Material patternMaterial = CreateUnlitMaterial(patternColor, transparent: false);
        Material finMaterial = CreateUnlitMaterial(Color.Lerp(bodyColor, Color.white, 0.08f), transparent: false);

        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        body.name = "Body";
        SetNonInteractive(body);
        body.transform.SetParent(fish.transform, false);
        body.transform.localScale = new Vector3(1.15f, 0.36f, 0.24f);
        if (body.TryGetComponent(out Renderer bodyRenderer))
            bodyRenderer.sharedMaterial = bodyMaterial;

        GameObject stripe = GameObject.CreatePrimitive(PrimitiveType.Cube);
        stripe.name = "SidePattern";
        SetNonInteractive(stripe);
        stripe.transform.SetParent(fish.transform, false);
        stripe.transform.localPosition = new Vector3(0.05f, 0.01f, -0.14f);
        stripe.transform.localScale = new Vector3(0.78f, 0.12f, 0.025f);
        if (stripe.TryGetComponent(out Renderer stripeRenderer))
            stripeRenderer.sharedMaterial = patternMaterial;

        GameObject tail = new GameObject("Tail");
        SetLayerRecursively(tail, LayerMask.NameToLayer("Ignore Raycast"));
        tail.transform.SetParent(fish.transform, false);
        tail.transform.localPosition = new Vector3(-0.72f, 0f, 0f);
        tail.transform.localScale = new Vector3(0.72f, 0.76f, 1f);
        MeshFilter tailFilter = tail.AddComponent<MeshFilter>();
        MeshRenderer tailRenderer = tail.AddComponent<MeshRenderer>();
        tailFilter.sharedMesh = CreateFishTailMesh();
        tailRenderer.sharedMaterial = finMaterial;

        GameObject topFin = new GameObject("TopFin");
        SetLayerRecursively(topFin, LayerMask.NameToLayer("Ignore Raycast"));
        topFin.transform.SetParent(fish.transform, false);
        topFin.transform.localPosition = new Vector3(-0.02f, 0.26f, 0f);
        topFin.transform.localRotation = Quaternion.Euler(0f, 0f, 180f);
        topFin.transform.localScale = new Vector3(0.48f, 0.32f, 1f);
        MeshFilter topFinFilter = topFin.AddComponent<MeshFilter>();
        MeshRenderer topFinRenderer = topFin.AddComponent<MeshRenderer>();
        topFinFilter.sharedMesh = CreateFishTailMesh();
        topFinRenderer.sharedMaterial = finMaterial;

        fish.AddComponent<QuestBigFishPass>().Initialize(leftToRight, phase, depth, height);
    }

    private static void DisableOceanForegroundDistractors(Scene scene, Transform allowedRoot)
    {
        GameObject summonObject = FindSceneObject(scene, "Summon");
        if (summonObject != null)
            summonObject.SetActive(false);

        SuppressSceneRenderers(scene, allowedRoot);
    }

    private static void SuppressSceneRenderers(Scene scene, Transform allowedRoot)
    {
        Renderer[] renderers = Object.FindObjectsOfType<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null ||
                renderer.gameObject.scene != scene ||
                renderer.transform.IsChildOf(allowedRoot) ||
                IsRuntimeInteractionObject(renderer.gameObject))
                continue;

            renderer.enabled = false;
        }
    }

    private static bool IsRuntimeInteractionObject(GameObject gameObject)
    {
        Transform current = gameObject.transform;
        while (current != null)
        {
            string objectName = current.gameObject.name;
            if (objectName.StartsWith("Quest", System.StringComparison.OrdinalIgnoreCase) ||
                current.GetComponent<Canvas>() != null)
                return true;

            current = current.parent;
        }

        return false;
    }

    private static void CreateStarField(Transform parent)
    {
        Material starMaterial = CreateUnlitMaterial(Color.white, transparent: false);

        for (int i = 0; i < 96; i++)
        {
            float x = Mathf.Lerp(-10f, 10f, Halton(i + 1, 2));
            float y = Mathf.Lerp(-5.5f, 5.5f, Halton(i + 1, 3));
            float z = Mathf.Lerp(9f, 18f, Halton(i + 1, 5));
            float size = Mathf.Lerp(0.012f, 0.045f, Halton(i + 1, 7));
            Transform star = CreateCameraQuad($"Star_{i:00}", parent, new Vector3(x, y, z), new Vector2(size, size), starMaterial);
            star.localRotation = Quaternion.Euler(0f, 180f, 0f);
        }
    }

    private static void CreateSpaceBody(Transform parent, string name, Vector3 localPosition, Vector3 localScale, Color color)
    {
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        body.name = name;
        SetNonInteractive(body);
        body.transform.SetParent(parent, false);
        body.transform.localPosition = localPosition;
        body.transform.localScale = localScale;

        if (body.TryGetComponent(out Renderer renderer))
            renderer.sharedMaterial = CreateUnlitMaterial(color, transparent: false);
    }

    private static void CreateSpaceRing(Transform parent)
    {
        GameObject ring = new GameObject("PlanetRing");
        SetLayerRecursively(ring, LayerMask.NameToLayer("Ignore Raycast"));
        ring.transform.SetParent(parent, false);
        ring.transform.localPosition = new Vector3(4.7f, 2.3f, 13.48f);
        ring.transform.localRotation = Quaternion.Euler(72f, 0f, 18f);
        ring.transform.localScale = Vector3.one;

        MeshFilter meshFilter = ring.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = ring.AddComponent<MeshRenderer>();
        meshFilter.sharedMesh = CreateRingMesh(1.15f, 1.72f, 72);
        meshRenderer.sharedMaterial = CreateUnlitMaterial(new Color(0.55f, 0.75f, 1f, 0.45f), transparent: true);
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

        Mesh mesh = new Mesh { name = "QuestSpaceRingMesh" };
        mesh.vertices = vertices;
        mesh.uv = uv;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Mesh CreateFishTailMesh()
    {
        Vector3[] vertices =
        {
            new Vector3(-0.5f, 0f, 0f),
            new Vector3(0.5f, 0.45f, 0f),
            new Vector3(0.5f, -0.45f, 0f)
        };

        int[] triangles =
        {
            0, 1, 2,
            2, 1, 0
        };

        Mesh mesh = new Mesh { name = "QuestOceanFishTailMesh" };
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Texture2D CreateWaterTexture()
    {
        const int width = 256;
        const int height = 256;
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = "QuestOceanWaterTexture",
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
                Color dark = new Color(0.02f, 0.26f, 0.28f, 0.55f);
                Color bright = new Color(0.22f, 1f, 0.86f, 0.9f);
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
            name = "QuestOceanCausticTexture",
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
                texture.SetPixel(x, y, new Color(0.85f, 1f, 0.88f, mask * 0.42f));
            }
        }

        texture.Apply(false, true);
        return texture;
    }

    private static Texture2D CreateOceanBackdropTexture()
    {
        const int width = 256;
        const int height = 128;
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = "QuestOceanBlueBackdropTexture",
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear
        };

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float u = x / (float)width;
                float v = y / (float)height;
                float depthGradient = Mathf.SmoothStep(0f, 1f, v);
                float nebula = Mathf.PerlinNoise(u * 3.0f + 4.7f, v * 2.2f + 12.3f);
                float flecks = Mathf.PerlinNoise(u * 78f + 0.2f, v * 78f + 3.8f);
                Color deepBlue = new Color(0.002f, 0.025f, 0.085f, 1f);
                Color tealBlue = new Color(0.015f, 0.18f, 0.30f, 1f);
                Color glow = new Color(0.05f, 0.42f, 0.55f, 1f);
                Color color = Color.Lerp(deepBlue, tealBlue, depthGradient * 0.55f);
                color = Color.Lerp(color, glow, Mathf.SmoothStep(0.54f, 0.92f, nebula) * 0.42f);

                if (flecks > 0.968f)
                    color = Color.Lerp(color, new Color(0.56f, 0.98f, 1f, 1f), 0.65f);

                texture.SetPixel(x, y, color);
            }
        }

        texture.Apply(false, true);
        return texture;
    }

    private static Texture2D CreateSpaceTexture()
    {
        const int width = 256;
        const int height = 128;
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = "QuestSpaceBackdropTexture",
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear
        };

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float u = x / (float)width;
                float v = y / (float)height;
                float nebula = Mathf.PerlinNoise(u * 3.2f + 1.7f, v * 2.4f + 9.1f);
                float stars = Mathf.PerlinNoise(u * 80f, v * 80f);
                Color color = Color.Lerp(new Color(0.004f, 0.005f, 0.015f, 1f), new Color(0.08f, 0.02f, 0.16f, 1f), Mathf.SmoothStep(0.55f, 0.9f, nebula));
                if (stars > 0.965f)
                    color = Color.Lerp(color, Color.white, 0.75f);

                texture.SetPixel(x, y, color);
            }
        }

        texture.Apply(false, true);
        return texture;
    }

    private static Material CreateUnlitMaterial(Color color, bool transparent)
    {
        Shader shader = Shader.Find(transparent ? "Unlit/Transparent" : "Unlit/Color");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");

        Material material = new Material(shader)
        {
            color = color
        };

        if (transparent)
        {
            material.renderQueue = 3000;
            material.SetFloat("_Mode", 3f);
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.EnableKeyword("_ALPHABLEND_ON");
        }

        return material;
    }

    private static float Halton(int index, int basis)
    {
        float result = 0f;
        float fraction = 1f / basis;

        while (index > 0)
        {
            result += fraction * (index % basis);
            index = Mathf.FloorToInt(index / (float)basis);
            fraction /= basis;
        }

        return result;
    }

    private static GameObject FindSceneObject(Scene scene, string objectName)
    {
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            GameObject result = FindChildRecursive(roots[i], objectName);
            if (result != null)
                return result;
        }

        return null;
    }

    private static GameObject FindChildRecursive(GameObject root, string objectName)
    {
        if (root == null)
            return null;

        if (string.Equals(root.name, objectName, System.StringComparison.OrdinalIgnoreCase))
            return root;

        Transform transform = root.transform;
        for (int i = 0; i < transform.childCount; i++)
        {
            GameObject result = FindChildRecursive(transform.GetChild(i).gameObject, objectName);
            if (result != null)
                return result;
        }

        return null;
    }

    private static void MoveToScene(GameObject gameObject, Scene scene)
    {
        if (gameObject != null && scene.IsValid() && scene.isLoaded)
            SceneManager.MoveGameObjectToScene(gameObject, scene);
    }

    private static void SetNonInteractive(GameObject gameObject)
    {
        if (gameObject.TryGetComponent(out Collider collider))
            Object.Destroy(collider);

        SetLayerRecursively(gameObject, LayerMask.NameToLayer("Ignore Raycast"));
    }

    private static void SetLayerRecursively(GameObject gameObject, int layer)
    {
        if (gameObject == null || layer < 0)
            return;

        gameObject.layer = layer;
        Transform transform = gameObject.transform;
        for (int i = 0; i < transform.childCount; i++)
            SetLayerRecursively(transform.GetChild(i).gameObject, layer);
    }

    private sealed class QuestPrefabFishSwim : MonoBehaviour
    {
        private Vector3 basePosition;
        private float speed = 0.7f;
        private float width = 0.8f;
        private float height = 0.12f;
        private float phase;

        public void Initialize(Vector3 localPosition, float swimSpeed, float swimWidth, float swimHeight, float startPhase)
        {
            basePosition = localPosition;
            speed = Mathf.Max(0.1f, swimSpeed);
            width = Mathf.Max(0f, swimWidth);
            height = Mathf.Max(0f, swimHeight);
            phase = startPhase;
        }

        private void Update()
        {
            float t = Time.time * speed + phase;
            float horizontal = Mathf.Sin(t) * width;
            float vertical = Mathf.Sin(t * 0.67f + phase) * height;
            Vector3 direction = Mathf.Cos(t) >= 0f ? Vector3.right : Vector3.left;

            transform.localPosition = basePosition + new Vector3(horizontal, vertical, 0f);
            transform.localRotation = Quaternion.LookRotation(direction, Vector3.up) * Quaternion.Euler(0f, 0f, Mathf.Sin(t * 1.3f) * 5f);
        }
    }

    private sealed class QuestPrefabFishPass : MonoBehaviour
    {
        private const float TravelWidth = 11.5f;
        private const float TravelDurationSeconds = 11.5f;

        private bool leftToRight = true;
        private float phase;
        private float depth = 6f;
        private float height;

        public void Initialize(bool travelsLeftToRight, float startPhase, float localDepth, float localHeight)
        {
            leftToRight = travelsLeftToRight;
            phase = startPhase;
            depth = Mathf.Max(3.8f, localDepth);
            height = localHeight;
        }

        private void Update()
        {
            float normalized = Mathf.Repeat((Time.time + phase) / TravelDurationSeconds, 1f);
            float eased = Mathf.SmoothStep(0f, 1f, normalized);
            float x = Mathf.Lerp(-TravelWidth, TravelWidth, leftToRight ? eased : 1f - eased);
            float y = height + Mathf.Sin((normalized * Mathf.PI * 2f) + phase) * 0.34f;
            float roll = Mathf.Sin((normalized * Mathf.PI * 2f) + phase) * 7.5f;
            Vector3 direction = leftToRight ? Vector3.right : Vector3.left;

            transform.localPosition = new Vector3(x, y, depth);
            transform.localRotation = Quaternion.LookRotation(direction, Vector3.up) * Quaternion.Euler(0f, 0f, roll);
        }
    }

    private sealed class QuestFishSwim : MonoBehaviour
    {
        private Vector3 basePosition;
        private Vector3 baseScale = Vector3.one;
        private float speed = 0.7f;
        private float width = 0.8f;
        private float height = 0.12f;
        private float phase;

        public void Initialize(Vector3 localPosition, float swimSpeed, float swimWidth, float swimHeight, float startPhase)
        {
            basePosition = localPosition;
            baseScale = transform.localScale;
            speed = Mathf.Max(0.1f, swimSpeed);
            width = Mathf.Max(0f, swimWidth);
            height = Mathf.Max(0f, swimHeight);
            phase = startPhase;
        }

        private void Update()
        {
            float t = Time.time * speed + phase;
            float horizontal = Mathf.Sin(t) * width;
            float vertical = Mathf.Sin(t * 0.67f + phase) * height;
            float facingSign = Mathf.Cos(t) >= 0f ? 1f : -1f;

            transform.localPosition = basePosition + new Vector3(horizontal, vertical, 0f);
            transform.localScale = new Vector3(Mathf.Abs(baseScale.x) * facingSign, baseScale.y, baseScale.z);
            transform.localRotation = Quaternion.Euler(0f, 180f, Mathf.Sin(t * 1.3f) * 4.5f);
        }
    }

    private sealed class QuestBigFishPass : MonoBehaviour
    {
        private const float TravelWidth = 10.8f;
        private const float TravelDurationSeconds = 11.5f;

        private bool leftToRight = true;
        private float phase;
        private float depth = 6f;
        private float height;

        public void Initialize(bool travelsLeftToRight, float startPhase, float localDepth, float localHeight)
        {
            leftToRight = travelsLeftToRight;
            phase = startPhase;
            depth = Mathf.Max(3.8f, localDepth);
            height = localHeight;
        }

        private void Update()
        {
            float normalized = Mathf.Repeat((Time.time + phase) / TravelDurationSeconds, 1f);
            float eased = Mathf.SmoothStep(0f, 1f, normalized);
            float x = Mathf.Lerp(-TravelWidth, TravelWidth, leftToRight ? eased : 1f - eased);
            float y = height + Mathf.Sin((normalized * Mathf.PI * 2f) + phase) * 0.32f;
            float roll = Mathf.Sin((normalized * Mathf.PI * 2f) + phase) * 5.5f;

            transform.localPosition = new Vector3(x, y, depth);
            transform.localRotation = Quaternion.Euler(0f, leftToRight ? 180f : 0f, roll);
        }
    }

    private sealed class QuestTextureDrift : MonoBehaviour
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

    private sealed class QuestSceneRendererSuppressor : MonoBehaviour
    {
        private Scene scene;
        private Transform allowedRoot;
        private float endTime;

        public void Initialize(Scene targetScene, Transform root, float durationSeconds)
        {
            scene = targetScene;
            allowedRoot = root;
            endTime = Time.time + durationSeconds;
        }

        private void LateUpdate()
        {
            if (!scene.IsValid() || allowedRoot == null)
            {
                enabled = false;
                return;
            }

            SuppressSceneRenderers(scene, allowedRoot);

            if (Time.time >= endTime)
                enabled = false;
        }
    }
}

internal static class QuestDirectSceneIntentLauncher
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (!ShouldRunOnCurrentPlatform())
            return;

        GameObject launcher = new GameObject("QuestDirectSceneIntentLauncher");
        Object.DontDestroyOnLoad(launcher);
        launcher.AddComponent<Driver>();
    }

    private static bool ShouldRunOnCurrentPlatform()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        return true;
#else
        return false;
#endif
    }

    private sealed class Driver : MonoBehaviour
    {
        private IEnumerator Start()
        {
            if (!IsDirectSceneLaunchRequested())
                yield break;

            string requestedScene = ReadRequestedScene();
            if (string.IsNullOrWhiteSpace(requestedScene))
                yield break;

            yield return null;
            yield return new WaitForSeconds(0.35f);

            string scenePath = ResolveScenePath(requestedScene);
            int buildIndex = ResolveBuildIndex(scenePath);
            if (buildIndex < 0)
            {
                Debug.LogWarning($"[QuestDirectSceneIntentLauncher] Requested scene '{requestedScene}' was not found in Build Settings.");
                yield break;
            }

            Debug.Log($"[QuestDirectSceneIntentLauncher] Loading requested scene '{scenePath}' for adb visual verification.");
            SceneManager.LoadScene(buildIndex, LoadSceneMode.Single);
        }

        private static bool IsDirectSceneLaunchRequested()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            string requested = ReadAndroidIntentStringExtra("xr_direct_scene");
            if (string.IsNullOrWhiteSpace(requested))
                requested = ReadAndroidIntentStringExtra("xr_scene_direct");

            return string.Equals(requested, "1", System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(requested, "true", System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(requested, "yes", System.StringComparison.OrdinalIgnoreCase);
#else
            return false;
#endif
        }

        private static string ReadRequestedScene()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return ReadAndroidIntentStringExtra("xr_scene");
#else
            return null;
#endif
        }

        private static string ReadAndroidIntentStringExtra(string key)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                using AndroidJavaObject intent = activity.Call<AndroidJavaObject>("getIntent");
                return intent.Call<string>("getStringExtra", key);
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning($"[QuestDirectSceneIntentLauncher] Could not read Android intent extra '{key}': {exception.Message}");
            }
#endif

            return null;
        }

        private static string ResolveScenePath(string requestedScene)
        {
            string normalized = requestedScene.Trim();
            if (normalized.EndsWith(".unity", System.StringComparison.OrdinalIgnoreCase))
                return normalized.Replace('\\', '/');

            if (string.Equals(normalized, "Ocean", System.StringComparison.OrdinalIgnoreCase))
                return QuestClassroomSceneTravel.OceanScenePath;

            if (string.Equals(normalized, "Space", System.StringComparison.OrdinalIgnoreCase))
                return QuestClassroomSceneTravel.SpaceScenePath;

            if (string.Equals(normalized, "Classroom", System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "Test_classroom", System.StringComparison.OrdinalIgnoreCase))
                return QuestClassroomSceneTravel.TestClassroomScenePath;

            return $"Assets/Scenes/{normalized}.unity";
        }

        private static int ResolveBuildIndex(string scenePath)
        {
            string expectedPath = scenePath.Replace('\\', '/');
            for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
            {
                string path = SceneUtility.GetScenePathByBuildIndex(i).Replace('\\', '/');
                if (string.Equals(path, expectedPath, System.StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return -1;
        }
    }
}

internal static class QuestXrRenderGuard
{
    private const int StartupReconcileFrames = 90;
    private static Driver driver;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        if (!ShouldRunOnCurrentPlatform() || driver != null)
            return;

        GameObject guardObject = new GameObject("QuestXrRenderGuard");
        Object.DontDestroyOnLoad(guardObject);
        driver = guardObject.AddComponent<Driver>();
    }

    public static void ReconcileNow(string reason)
    {
        if (!ShouldRunOnCurrentPlatform())
            return;

        if (driver == null)
            Install();

        if (driver != null)
            driver.ReconcileCameras(reason);
    }

    private static bool ShouldRunOnCurrentPlatform()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        return true;
#else
        return false;
#endif
    }

    private sealed class Driver : MonoBehaviour
    {
        private Coroutine reconcileCoroutine;

        private void OnEnable()
        {
            SceneManager.sceneLoaded += HandleSceneLoaded;
            SceneManager.sceneUnloaded += HandleSceneUnloaded;
            SceneManager.activeSceneChanged += HandleActiveSceneChanged;
            StartReconcileWindow("startup");
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneUnloaded -= HandleSceneUnloaded;
            SceneManager.activeSceneChanged -= HandleActiveSceneChanged;
        }

        private void LateUpdate()
        {
            if ((Time.frameCount & 31) == 0)
                ReconcileCameras("periodic camera check");
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            QuestExternalSceneViewBootstrap.ConfigureScene(scene, "render guard scene loaded");
            StartReconcileWindow($"scene loaded: {scene.name}");
        }

        private void HandleSceneUnloaded(Scene scene)
        {
            StartReconcileWindow($"scene unloaded: {scene.name}");
        }

        private void HandleActiveSceneChanged(Scene previousScene, Scene newScene)
        {
            QuestExternalSceneViewBootstrap.ConfigureScene(newScene, "active scene changed");
            StartReconcileWindow($"active scene changed: {newScene.name}");
        }

        private void StartReconcileWindow(string reason)
        {
            if (reconcileCoroutine != null)
                StopCoroutine(reconcileCoroutine);

            reconcileCoroutine = StartCoroutine(ReconcileForFrames(reason));
        }

        private IEnumerator ReconcileForFrames(string reason)
        {
            for (int i = 0; i < StartupReconcileFrames; i++)
            {
                ReconcileCameras(reason);
                yield return null;
            }

            reconcileCoroutine = null;
        }

        public void ReconcileCameras(string reason)
        {
            Camera[] cameras = FindObjectsOfType<Camera>(true);
            Camera primaryCamera = SelectPrimaryDisplayCamera(cameras);

            if (primaryCamera == null)
                return;

            ConfigurePrimaryDisplayCamera(primaryCamera);

            for (int i = 0; i < cameras.Length; i++)
            {
                Camera camera = cameras[i];

                if (camera == null || camera == primaryCamera)
                    continue;

                if (camera.targetTexture != null)
                {
                    ConfigureOffscreenCamera(camera, reason);
                    continue;
                }

                DisableCompetingDisplayCamera(camera, primaryCamera, reason);
            }
        }

        private static Camera SelectPrimaryDisplayCamera(Camera[] cameras)
        {
            Camera bestCamera = null;
            int bestScore = int.MinValue;
            Scene preferredScene = ResolvePreferredDisplayScene(cameras);

            for (int i = 0; i < cameras.Length; i++)
            {
                Camera camera = cameras[i];

                if (!IsDisplayCameraCandidate(camera))
                    continue;

                int score = ScoreDisplayCamera(camera, preferredScene);
                if (score <= bestScore)
                    continue;

                bestScore = score;
                bestCamera = camera;
            }

            return bestCamera;
        }

        private static Scene ResolvePreferredDisplayScene(Camera[] cameras)
        {
            Scene activeScene = SceneManager.GetActiveScene();

            if (!IsLobbyScene(activeScene) && HasDisplayCameraInScene(cameras, activeScene))
                return activeScene;

            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                Scene scene = SceneManager.GetSceneAt(i);

                if (!scene.IsValid() || !scene.isLoaded || IsLobbyScene(scene))
                    continue;

                if (HasDisplayCameraInScene(cameras, scene))
                    return scene;
            }

            return activeScene;
        }

        private static bool HasDisplayCameraInScene(Camera[] cameras, Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return false;

            for (int i = 0; i < cameras.Length; i++)
            {
                Camera camera = cameras[i];

                if (IsDisplayCameraCandidate(camera) && camera.gameObject.scene == scene)
                    return true;
            }

            return false;
        }

        private static bool IsDisplayCameraCandidate(Camera camera)
        {
            return camera != null &&
                camera.gameObject.scene.IsValid() &&
                camera.targetTexture == null &&
                !IsSuimonoHelperCamera(camera);
        }

        private static int ScoreDisplayCamera(Camera camera, Scene preferredScene)
        {
            int score = 0;

            if (camera.gameObject.scene == preferredScene)
                score += 1000;

            if (camera.isActiveAndEnabled)
                score += 150;
            else if (camera.gameObject.activeInHierarchy)
                score += 50;

            if (camera.CompareTag("MainCamera"))
                score += 300;

            if (string.Equals(camera.gameObject.name, "Main Camera", System.StringComparison.OrdinalIgnoreCase))
                score += 200;

            if (camera.gameObject.name.Contains("(1)"))
                score -= 250;

            if (IsLobbyScene(camera.gameObject.scene))
                score += IsLobbyScene(preferredScene) ? 100 : -1000;

            if (IsClassroomScene(camera.gameObject.scene))
                score += 300;

            return score;
        }

        private static void ConfigurePrimaryDisplayCamera(Camera camera)
        {
            bool changed = false;

            if (!camera.gameObject.activeSelf)
            {
                camera.gameObject.SetActive(true);
                changed = true;
            }

            if (!camera.enabled)
            {
                camera.enabled = true;
                changed = true;
            }

            if (camera.stereoTargetEye != StereoTargetEyeMask.Both)
            {
                camera.stereoTargetEye = StereoTargetEyeMask.Both;
                changed = true;
            }

            if (camera.targetTexture != null)
            {
                camera.targetTexture = null;
                changed = true;
            }

            if (camera.targetDisplay != 0)
            {
                camera.targetDisplay = 0;
                changed = true;
            }

            if (camera.rect != new Rect(0f, 0f, 1f, 1f))
            {
                camera.rect = new Rect(0f, 0f, 1f, 1f);
                changed = true;
            }

            if (camera.renderingPath != RenderingPath.Forward)
            {
                camera.renderingPath = RenderingPath.Forward;
                changed = true;
            }

            if (camera.allowHDR)
            {
                camera.allowHDR = false;
                changed = true;
            }

            if (camera.allowDynamicResolution)
            {
                camera.allowDynamicResolution = false;
                changed = true;
            }

            DisableQuestUnsafeImageEffects(camera, ref changed);

            if (changed)
                Debug.Log($"[QuestXrRenderGuard] Primary XR camera = '{camera.name}' in scene '{camera.gameObject.scene.name}'.");
        }

        private static void ConfigureOffscreenCamera(Camera camera, string reason)
        {
            bool changed = false;

            if (camera.stereoTargetEye != StereoTargetEyeMask.None)
            {
                camera.stereoTargetEye = StereoTargetEyeMask.None;
                changed = true;
            }

            if (camera.allowHDR)
            {
                camera.allowHDR = false;
                changed = true;
            }

            if (camera.allowMSAA)
            {
                camera.allowMSAA = false;
                changed = true;
            }

            if (changed)
                Debug.Log($"[QuestXrRenderGuard] Offscreen camera '{camera.name}' set to non-XR. Reason: {reason}");
        }

        private static void DisableCompetingDisplayCamera(Camera camera, Camera primaryCamera, string reason)
        {
            if (camera == null || camera == primaryCamera)
                return;

            bool changed = false;

            if (camera.stereoTargetEye != StereoTargetEyeMask.None)
            {
                camera.stereoTargetEye = StereoTargetEyeMask.None;
                changed = true;
            }

            if (camera.enabled)
            {
                camera.enabled = false;
                changed = true;
            }

            if (camera.TryGetComponent(out AudioListener listener) && listener.enabled)
            {
                listener.enabled = false;
                changed = true;
            }

            if (changed)
            {
                Debug.Log(
                    $"[QuestXrRenderGuard] Disabled competing display camera '{camera.name}' while primary is '{primaryCamera.name}'. Reason: {reason}");
            }
        }

        private static void DisableQuestUnsafeImageEffects(Camera camera, ref bool changed)
        {
            MonoBehaviour[] behaviours = camera.GetComponents<MonoBehaviour>();

            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];

                if (behaviour == null || !behaviour.enabled)
                    continue;

                string typeName = behaviour.GetType().FullName;
                if (typeName != "Suimono.Core.Suimono_UnderwaterFog" &&
                    typeName != "Suimono.Core.Suimono_DistanceBlur")
                    continue;

                behaviour.enabled = false;
                changed = true;
                Debug.Log($"[QuestXrRenderGuard] Disabled stereo-unsafe image effect '{typeName}' on camera '{camera.name}'.");
            }
        }

        private static bool IsSuimonoHelperCamera(Camera camera)
        {
            return camera.gameObject.name.IndexOf("cam_Suimono", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsLobbyScene(Scene scene)
        {
            string scenePath = scene.path.Replace('\\', '/');
            return scene.name.IndexOf("Lobby", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                scenePath.IndexOf("/Lobby.unity", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                scenePath.IndexOf("/Test_lobby.unity", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsClassroomScene(Scene scene)
        {
            string scenePath = scene.path.Replace('\\', '/');
            return scene.name.IndexOf("Classroom", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                scenePath.IndexOf("/Classroom.unity", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                scenePath.IndexOf("/Test_classroom.unity", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
