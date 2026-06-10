using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class QuestLobbyPointer : MonoBehaviour
{
    [Header("Lobby UI")]
    [SerializeField] private Canvas lobbyCanvas;
    [SerializeField] private Camera lobbyCamera;

    [Header("Quest Layout")]
    [SerializeField] private bool enableInEditor;
    [SerializeField] private Vector2 worldCanvasSize = new Vector2(1200f, 720f);
    [SerializeField] private float worldCanvasScale = 0.0025f;
    [SerializeField] private float cameraHeight = 1.6f;
    [SerializeField] private float canvasDistance = 1.8f;
    [SerializeField] private float canvasHeightOffset = -0.02f;
    [SerializeField] private bool keepCanvasCenteredOnView;
    [SerializeField] private int recenterSettleFrames = 8;

    [Header("Quest UI Size")]
    [SerializeField] private Vector2 buttonSize = new Vector2(420f, 112f);
    [SerializeField] private Vector2 serverButtonPosition = new Vector2(-260f, -45f);
    [SerializeField] private Vector2 clientButtonPosition = new Vector2(260f, -45f);
    [SerializeField] private Vector2 roomInputSize = new Vector2(640f, 88f);
    [SerializeField] private Vector2 roomInputPosition = new Vector2(0f, -185f);
    [SerializeField] private Vector2 statusSize = new Vector2(980f, 108f);
    [SerializeField] private Vector2 statusPosition = new Vector2(0f, 150f);
    [SerializeField] private float buttonFontSize = 48f;
    [SerializeField] private float inputFontSize = 34f;
    [SerializeField] private float statusFontSize = 44f;

    [Header("Controller Pointer")]
    [SerializeField] private XRNode pointerHand = XRNode.RightHand;
    [SerializeField] private float rayLength = 5f;
    [SerializeField] private float triggerThreshold = 0.65f;
    [SerializeField] private Vector3 pointerPositionOffset;
    [SerializeField] private Vector3 pointerRotationOffsetEuler;
    [SerializeField] private float rayStartWidth = 0.018f;
    [SerializeField] private float rayEndWidth = 0.006f;
    [SerializeField] private float reticleSize = 0.07f;
    [SerializeField] private bool fallbackToHeadGaze = true;

    [Header("Controller Visual")]
    [SerializeField] private bool showControllerVisual = true;
    [SerializeField] private Vector3 controllerVisualPositionOffset = new Vector3(0f, -0.02f, 0.05f);
    [SerializeField] private Vector3 controllerVisualRotationOffsetEuler;
    [SerializeField] private Vector3 controllerBodySize = new Vector3(0.08f, 0.06f, 0.16f);

    private static readonly Vector3 DefaultPointerPositionOffset = new Vector3(0f, -0.015f, 0.095f);

    private readonly List<RaycastResult> raycastResults = new();
    private RectTransform canvasRect;
    private GraphicRaycaster graphicRaycaster;
    private PointerEventData pointerEventData;
    private LineRenderer pointerLine;
    private Transform reticle;
    private Transform controllerVisual;
    private Transform controllerRayAnchor;
    private GameObject hoveredObject;
    private bool wasPressed;
    private bool wasRecenterPressed;
    private bool configured;
    private int pendingRecenterFrames;
    private bool loggedMissingViewPose;
    private Vector2 lastPointerPosition;
    private bool hasLastPointerPosition;

#if ENABLE_INPUT_SYSTEM
    private InputAction pointerPositionAction;
    private InputAction pointerRotationAction;
    private InputAction isTrackedAction;
    private InputAction clickAction;
    private InputAction recenterAction;
#endif

    private bool ShouldRun =>
#if UNITY_ANDROID && !UNITY_EDITOR
        true;
#else
        enableInEditor;
#endif

    private void Awake()
    {
        if (!ShouldRun)
        {
            enabled = false;
            return;
        }
    }

    private IEnumerator Start()
    {
        if (!ShouldRun)
            yield break;

        for (int i = 0; i < 60; i++)
        {
            if (TryGetNodePose(XRNode.CenterEye, out _, out _) || TryGetNodePose(pointerHand, out _, out _))
                break;

            yield return null;
        }

        Configure();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;

#if ENABLE_INPUT_SYSTEM
        pointerPositionAction?.Enable();
        pointerRotationAction?.Enable();
        isTrackedAction?.Enable();
        clickAction?.Enable();
        recenterAction?.Enable();
#endif
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        CancelPress();
        ClearHover();

#if ENABLE_INPUT_SYSTEM
        pointerPositionAction?.Disable();
        pointerRotationAction?.Disable();
        isTrackedAction?.Disable();
        clickAction?.Disable();
        recenterAction?.Disable();
#endif
    }

    private void Update()
    {
        if (!configured)
            return;

        if (keepCanvasCenteredOnView)
            PlaceCanvasInFrontOfUser();

        if (ConsumeRecenterPressed())
        {
            ShowOptionsInFrontOfView("A button");
            ClearHover();
            CancelPress();
        }

        if (pendingRecenterFrames > 0 && PlaceCanvasInFrontOfUser())
            pendingRecenterFrames--;

        if (!TryGetPointerRay(out Ray pointerRay, out bool hasController, out Vector3 controllerPosition, out Quaternion controllerRotation))
        {
            UpdateVisual(pointerRay, false, pointerRay.origin + pointerRay.direction * rayLength);
            UpdateControllerVisual(Vector3.zero, Quaternion.identity, false);
            ClearHover();
            CancelPress();
            return;
        }

        bool hasHit = TryGetCanvasHit(pointerRay, out Vector3 hitWorld, out Vector2 screenPoint);
        RaycastResult raycastResult = default;
        GameObject target = hasHit ? FindTargetAt(screenPoint, hitWorld, out raycastResult) : null;

        UpdateVisual(pointerRay, hasHit, hasHit ? hitWorld : pointerRay.origin + pointerRay.direction * rayLength);

        UpdateControllerVisual(controllerPosition, controllerRotation, hasController);

        UpdateHover(target, raycastResult);

        bool isPressed = hasController && IsClickPressed();

        if (isPressed && !wasPressed)
            Press(target);
        else if (!isPressed && wasPressed)
            Release(target);

        wasPressed = isPressed;
    }

    private void Configure()
    {
        if (lobbyCanvas == null)
            lobbyCanvas = FindObjectOfType<Canvas>();

        if (lobbyCamera == null)
            lobbyCamera = Camera.main;

        if (lobbyCanvas == null || lobbyCamera == null)
        {
            Debug.LogWarning("[QuestLobbyPointer] Missing lobby canvas or camera; Quest lobby pointer disabled.");
            enabled = false;
            return;
        }

        lobbyCanvas.gameObject.SetActive(true);
        lobbyCamera.gameObject.SetActive(true);
        lobbyCamera.enabled = true;

        canvasRect = lobbyCanvas.transform as RectTransform;
        EnsureEventSystem();
        graphicRaycaster = lobbyCanvas.GetComponent<GraphicRaycaster>();

        if (graphicRaycaster == null)
            graphicRaycaster = lobbyCanvas.gameObject.AddComponent<GraphicRaycaster>();

        graphicRaycaster.ignoreReversedGraphics = false;
        graphicRaycaster.blockingObjects = GraphicRaycaster.BlockingObjects.None;

        pointerEventData = new PointerEventData(EventSystem.current)
        {
            button = PointerEventData.InputButton.Left,
            pointerId = -200
        };

        ConfigureLobbyCamera();
        ConfigureCanvas();
        ConfigureQuestUiLayout();
        ConfigureInputActions();
        CreatePointerVisuals();
        CreateControllerVisual();

        configured = true;
        ShowOptionsInFrontOfView("Initial placement");
        Debug.Log("[QuestLobbyPointer] Quest lobby pointer configured.");
    }

    public void HideLobbyUi(string reason = "Scene transition")
    {
        CancelPress();
        configured = false;
        pendingRecenterFrames = 0;
        wasRecenterPressed = false;
        ClearHover();

        if (pointerLine != null)
            pointerLine.gameObject.SetActive(false);

        if (reticle != null)
            reticle.gameObject.SetActive(false);

        if (controllerVisual != null)
            controllerVisual.gameObject.SetActive(false);

        if (lobbyCanvas != null)
            lobbyCanvas.gameObject.SetActive(false);

        Debug.Log($"[QuestLobbyPointer] Lobby UI hidden. Reason: {reason}");
        enabled = false;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (IsClassroomScene(scene))
        {
            HideLobbyUi($"Loaded scene '{scene.name}'");
            DisableLobbyCamera($"Loaded scene '{scene.name}'");
            QuestXrRenderGuard.ReconcileNow($"QuestLobbyPointer detected classroom scene '{scene.name}'");
        }
    }

    private static bool IsClassroomScene(Scene scene)
    {
        string scenePath = scene.path.Replace('\\', '/');
        return string.Equals(scene.name, "Classroom", System.StringComparison.OrdinalIgnoreCase) ||
            string.Equals(scene.name, "Test_classroom", System.StringComparison.OrdinalIgnoreCase) ||
            string.Equals(scenePath, "Assets/Project/Scenes/Classroom.unity", System.StringComparison.OrdinalIgnoreCase) ||
            string.Equals(scenePath, "Assets/Scenes/Test_classroom.unity", System.StringComparison.OrdinalIgnoreCase);
    }

    private void DisableLobbyCamera(string reason)
    {
        if (lobbyCamera == null)
            return;

        lobbyCamera.stereoTargetEye = StereoTargetEyeMask.None;
        lobbyCamera.enabled = false;
        lobbyCamera.gameObject.SetActive(false);

        if (lobbyCamera.TryGetComponent(out AudioListener listener))
            listener.enabled = false;

        Debug.Log($"[QuestLobbyPointer] Lobby camera disabled. Reason: {reason}");
    }

    private void ConfigureLobbyCamera()
    {
        lobbyCamera.transform.SetPositionAndRotation(new Vector3(0f, cameraHeight, -0.15f), Quaternion.identity);
        lobbyCamera.nearClipPlane = 0.05f;
        lobbyCamera.farClipPlane = 100f;
        lobbyCamera.fieldOfView = 70f;
    }

    private void EnsureEventSystem()
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

    private void ConfigureCanvas()
    {
        lobbyCanvas.renderMode = RenderMode.WorldSpace;
        lobbyCanvas.worldCamera = lobbyCamera;
        lobbyCanvas.sortingOrder = 10;

        canvasRect.SetParent(null, true);
        canvasRect.anchorMin = new Vector2(0.5f, 0.5f);
        canvasRect.anchorMax = new Vector2(0.5f, 0.5f);
        canvasRect.pivot = new Vector2(0.5f, 0.5f);
        canvasRect.sizeDelta = worldCanvasSize;
        canvasRect.localScale = Vector3.one * worldCanvasScale;

        ShowOptionsInFrontOfView("Canvas configured");

        if (lobbyCanvas.TryGetComponent(out CanvasScaler scaler))
        {
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            scaler.dynamicPixelsPerUnit = 10f;
        }
    }

    private bool PlaceCanvasInFrontOfUser()
    {
        if (!TryGetViewPose(out Vector3 headPosition, out Quaternion headRotation))
        {
            if (!loggedMissingViewPose)
            {
                Debug.LogWarning("[QuestLobbyPointer] Cannot recenter lobby canvas because no reliable HMD pose is available.");
                loggedMissingViewPose = true;
            }

            return false;
        }

        loggedMissingViewPose = false;

        Vector3 viewForward = headRotation * Vector3.forward;
        if (viewForward.sqrMagnitude < 0.001f)
        {
            Debug.LogWarning("[QuestLobbyPointer] Cannot recenter lobby canvas because HMD forward vector is invalid.");
            return false;
        }

        viewForward.Normalize();

        Vector3 flatForward = Vector3.ProjectOnPlane(viewForward, Vector3.up);
        if (flatForward.sqrMagnitude < 0.001f)
            flatForward = lobbyCamera != null ? Vector3.ProjectOnPlane(lobbyCamera.transform.forward, Vector3.up) : Vector3.forward;

        if (flatForward.sqrMagnitude < 0.001f)
            flatForward = Vector3.forward;

        flatForward.Normalize();

        canvasRect.position = headPosition + viewForward * canvasDistance + Vector3.up * canvasHeightOffset;
        canvasRect.rotation = Quaternion.LookRotation(flatForward, Vector3.up);
        return true;
    }

    private void RequestCanvasRecenter()
    {
        pendingRecenterFrames = Mathf.Max(1, recenterSettleFrames);
    }

    private void ShowOptionsInFrontOfView(string reason)
    {
        if (lobbyCanvas != null)
            lobbyCanvas.gameObject.SetActive(true);

        if (graphicRaycaster != null)
            graphicRaycaster.enabled = true;

        if (pointerLine != null)
            pointerLine.gameObject.SetActive(true);

        RequestCanvasRecenter();
        PlaceCanvasInFrontOfUser();
        Debug.Log($"[QuestLobbyPointer] Lobby options placed in front of view. Reason: {reason}");
    }

    private void ConfigureQuestUiLayout()
    {
        ConfigureRect("TeacherHostButton", buttonSize, serverButtonPosition);
        ConfigureRect("StudentJoinButton", buttonSize, clientButtonPosition);
        ConfigureRect("RoomNameInput", roomInputSize, roomInputPosition);
        ConfigureRect("StatusText", statusSize, statusPosition);

        SetTextSize("TeacherHostButton", buttonFontSize);
        SetTextSize("StudentJoinButton", buttonFontSize);
        SetTextSize("RoomNameInput", inputFontSize);
        SetTextSize("StatusText", statusFontSize);
    }

    private void ConfigureRect(string objectName, Vector2 size, Vector2 anchoredPosition)
    {
        Transform target = FindChildRecursive(lobbyCanvas.transform, objectName);

        RectTransform rectTransform = target as RectTransform;

        if (rectTransform == null)
            return;

        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = anchoredPosition;
        rectTransform.sizeDelta = size;
    }

    private void SetTextSize(string objectName, float fontSize)
    {
        Transform target = FindChildRecursive(lobbyCanvas.transform, objectName);

        if (target == null)
            return;

        TMP_Text[] texts = target.GetComponentsInChildren<TMP_Text>(true);

        foreach (TMP_Text text in texts)
        {
            text.enableAutoSizing = false;
            text.fontSize = fontSize;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
        }
    }

    private static Transform FindChildRecursive(Transform root, string objectName)
    {
        if (root.name == objectName)
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = FindChildRecursive(root.GetChild(i), objectName);

            if (child != null)
                return child;
        }

        return null;
    }

    private void ConfigureInputActions()
    {
#if ENABLE_INPUT_SYSTEM
        string handUsage = pointerHand == XRNode.LeftHand ? "LeftHand" : "RightHand";
        string handBinding = $"<XRController>{{{handUsage}}}";

        pointerPositionAction = new InputAction("Quest Lobby Pointer Position", InputActionType.Value);
        pointerPositionAction.AddBinding($"{handBinding}/pointerPosition");
        pointerPositionAction.AddBinding($"{handBinding}/devicePosition");

        pointerRotationAction = new InputAction("Quest Lobby Pointer Rotation", InputActionType.Value);
        pointerRotationAction.AddBinding($"{handBinding}/pointerRotation");
        pointerRotationAction.AddBinding($"{handBinding}/deviceRotation");

        isTrackedAction = new InputAction("Quest Lobby Pointer Tracked", InputActionType.Button);
        isTrackedAction.AddBinding($"{handBinding}/isTracked");

        clickAction = new InputAction("Quest Lobby Pointer Click", InputActionType.Button);
        clickAction.AddBinding($"{handBinding}/triggerPressed");
        clickAction.AddBinding($"{handBinding}/trigger");
        clickAction.AddBinding($"{handBinding}/gripPressed");
        clickAction.AddBinding($"{handBinding}/pointerActivated");
        clickAction.AddBinding($"{handBinding}/pointerActivateValue");
        clickAction.AddBinding($"{handBinding}/pinchValue");
        clickAction.AddBinding($"{handBinding}/pinchTouched");
        clickAction.AddBinding($"{handBinding}/graspValue");

        recenterAction = new InputAction("Quest Lobby Recenter", InputActionType.Button);
        recenterAction.AddBinding($"{handBinding}/primaryButton");
        recenterAction.AddBinding("<XRController>{RightHand}/primaryButton");
        recenterAction.AddBinding("<OculusTouchController>{RightHand}/primaryButton");

        pointerPositionAction.Enable();
        pointerRotationAction.Enable();
        isTrackedAction.Enable();
        clickAction.Enable();
        recenterAction.Enable();
#endif
    }

    private void CreatePointerVisuals()
    {
        GameObject lineObject = new GameObject("QuestLobbyPointerLine");
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
        reticleObject.name = "QuestLobbyPointerReticle";
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
    }

    private void CreateControllerVisual()
    {
        if (!showControllerVisual)
            return;

        GameObject root = new GameObject("QuestLobbyControllerVisual");
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

        GameObject nose = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        nose.name = "ControllerAimTip";
        nose.transform.SetParent(controllerVisual, false);
        nose.transform.localPosition = GetPointerPositionOffset();
        nose.transform.localScale = Vector3.one * 0.045f;
        SetVisualMaterial(nose, accentMaterial);
        controllerRayAnchor = nose.transform;

        GameObject trigger = GameObject.CreatePrimitive(PrimitiveType.Cube);
        trigger.name = "ControllerTrigger";
        trigger.transform.SetParent(controllerVisual, false);
        trigger.transform.localPosition = new Vector3(0f, -controllerBodySize.y * 0.65f, controllerBodySize.z * 0.08f);
        trigger.transform.localScale = new Vector3(0.035f, 0.025f, 0.06f);
        SetVisualMaterial(trigger, accentMaterial);

        controllerVisual.gameObject.SetActive(false);
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
        controllerPosition = handPosition;
        controllerRotation = handRotation;

        if (hasController)
        {
            Quaternion visualRotation = GetControllerVisualRotation(handRotation);
            Vector3 visualPosition = GetControllerVisualPosition(handPosition, visualRotation);
            Vector3 rayOrigin = visualPosition + visualRotation * GetPointerPositionOffset();
            pointerRay = new Ray(rayOrigin, visualRotation * Vector3.forward);
            controllerPosition = handPosition;
            controllerRotation = handRotation;
            return true;
        }

        if (fallbackToHeadGaze)
        {
            GetHeadPose(out Vector3 headPosition, out Quaternion headRotation);
            pointerRay = new Ray(headPosition, headRotation * Vector3.forward);
            controllerPosition = headPosition;
            controllerRotation = headRotation;
            return true;
        }

        pointerRay = new Ray(Vector3.zero, Vector3.forward);
        controllerPosition = Vector3.zero;
        controllerRotation = Quaternion.identity;
        return false;
    }

    private Vector3 GetPointerPositionOffset()
    {
        return pointerPositionOffset.sqrMagnitude > 0.000001f
            ? pointerPositionOffset
            : DefaultPointerPositionOffset;
    }

    private Quaternion GetControllerVisualRotation(Quaternion controllerRotation)
    {
        return controllerRotation *
            Quaternion.Euler(pointerRotationOffsetEuler) *
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

    private bool TryGetCanvasHit(Ray pointerRay, out Vector3 hitWorld, out Vector2 screenPoint)
    {
        screenPoint = default;
        Plane canvasPlane = new Plane(canvasRect.forward, canvasRect.position);

        if (!canvasPlane.Raycast(pointerRay, out float enter) || enter < 0f || enter > rayLength)
        {
            hitWorld = default;
            return false;
        }

        hitWorld = pointerRay.GetPoint(enter);
        Vector3 localPoint = canvasRect.InverseTransformPoint(hitWorld);
        Rect canvasBounds = canvasRect.rect;

        if (!canvasBounds.Contains(new Vector2(localPoint.x, localPoint.y)))
            return false;

        Vector3 screenPosition = lobbyCamera.WorldToScreenPoint(hitWorld);

        if (screenPosition.z < 0f)
            return false;

        screenPoint = new Vector2(screenPosition.x, screenPosition.y);
        return true;
    }

    private GameObject FindTargetAt(Vector2 screenPoint, Vector3 hitWorld, out RaycastResult bestResult)
    {
        bestResult = default;
        raycastResults.Clear();

        UpdatePointerPosition(screenPoint);
        graphicRaycaster.Raycast(pointerEventData, raycastResults);

        for (int i = 0; i < raycastResults.Count; i++)
        {
            RaycastResult result = raycastResults[i];
            GameObject clickTarget = ExecuteEvents.GetEventHandler<IPointerClickHandler>(result.gameObject);

            if (clickTarget == null)
                continue;

            if (clickTarget.TryGetComponent(out Selectable selectable) &&
                (!selectable.IsActive() || !selectable.IsInteractable()))
                continue;

            bestResult = result;
            bestResult.worldPosition = hitWorld;
            bestResult.screenPosition = screenPoint;
            bestResult.gameObject = result.gameObject;
            pointerEventData.pointerCurrentRaycast = bestResult;
            return clickTarget;
        }

        GameObject selectableTarget = FindSelectableAtWorldPoint(hitWorld, screenPoint, out RaycastResult selectableResult);

        if (selectableTarget != null)
        {
            bestResult = selectableResult;
            pointerEventData.pointerCurrentRaycast = bestResult;
            return selectableTarget;
        }

        pointerEventData.pointerCurrentRaycast = new RaycastResult
        {
            worldPosition = hitWorld,
            screenPosition = screenPoint,
            distance = Vector3.Distance(PointerRayOriginFallback(), hitWorld)
        };
        bestResult = pointerEventData.pointerCurrentRaycast;
        return null;
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

    private GameObject FindSelectableAtWorldPoint(Vector3 hitWorld, Vector2 screenPoint, out RaycastResult result)
    {
        result = default;
        Selectable bestSelectable = null;
        float bestArea = float.MaxValue;
        Selectable[] selectables = lobbyCanvas.GetComponentsInChildren<Selectable>(true);

        foreach (Selectable selectable in selectables)
        {
            if (!selectable.IsActive() || !selectable.IsInteractable())
                continue;

            RectTransform rectTransform = selectable.transform as RectTransform;

            if (rectTransform == null)
                continue;

            Vector3 localPoint = rectTransform.InverseTransformPoint(hitWorld);

            if (!rectTransform.rect.Contains(new Vector2(localPoint.x, localPoint.y)))
                continue;

            float area = Mathf.Abs(rectTransform.rect.width * rectTransform.rect.height);

            if (area >= bestArea)
                continue;

            bestSelectable = selectable;
            bestArea = area;
        }

        if (bestSelectable == null)
            return null;

        result = new RaycastResult
        {
            gameObject = bestSelectable.gameObject,
            module = graphicRaycaster,
            worldPosition = hitWorld,
            screenPosition = screenPoint,
            distance = Vector3.Distance(PointerRayOriginFallback(), hitWorld),
            sortingOrder = lobbyCanvas.sortingOrder
        };

        return bestSelectable.gameObject;
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
                // Some controller layouts expose a button control that cannot be read as a float.
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

    private bool ConsumeRecenterPressed()
    {
        bool isPressed = IsRecenterPressed();
        bool shouldRecenter = isPressed && !wasRecenterPressed;
        wasRecenterPressed = isPressed;
        return shouldRecenter;
    }

    private bool IsRecenterPressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (recenterAction != null && recenterAction.controls.Count > 0 && recenterAction.IsPressed())
            return true;
#endif

        UnityEngine.XR.InputDevice device = InputDevices.GetDeviceAtXRNode(pointerHand);
        return device.isValid &&
            device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out bool primaryButton) &&
            primaryButton;
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
            controllerRayAnchor.localPosition = GetPointerPositionOffset();
    }

    private Vector3 PointerRayOriginFallback()
    {
        return pointerLine != null ? pointerLine.GetPosition(0) : canvasRect.position;
    }

    private bool TryGetViewPose(out Vector3 position, out Quaternion rotation)
    {
        if (TryGetNodePose(XRNode.CenterEye, out position, out rotation) ||
            TryGetNodePose(XRNode.Head, out position, out rotation))
            return true;

        if (TryGetNodeRotation(XRNode.CenterEye, out rotation) ||
            TryGetNodeRotation(XRNode.Head, out rotation))
        {
            position = GetViewFallbackPosition();
            return true;
        }

        if (lobbyCamera != null)
        {
            Transform cameraTransform = lobbyCamera.transform;
            position = cameraTransform.position;
            rotation = cameraTransform.rotation;
            return true;
        }

        position = Vector3.zero;
        rotation = Quaternion.identity;
        return false;
    }

    private Vector3 GetViewFallbackPosition()
    {
        if (TryGetNodePosition(XRNode.CenterEye, out Vector3 position) ||
            TryGetNodePosition(XRNode.Head, out position))
            return position;

        return lobbyCamera != null ? lobbyCamera.transform.position : Vector3.zero;
    }

    private void GetHeadPose(out Vector3 position, out Quaternion rotation)
    {
        if (TryGetViewPose(out position, out rotation))
            return;

        if (lobbyCamera != null)
        {
            Transform cameraTransform = lobbyCamera.transform;
            position = cameraTransform.position;
            rotation = cameraTransform.rotation;
            return;
        }

        position = Vector3.zero;
        rotation = Quaternion.identity;
    }

    private static bool TryGetNodePosition(XRNode node, out Vector3 position)
    {
        position = Vector3.zero;

        UnityEngine.XR.InputDevice device = InputDevices.GetDeviceAtXRNode(node);
        return device.isValid &&
            device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out position);
    }

    private static bool TryGetNodeRotation(XRNode node, out Quaternion rotation)
    {
        rotation = Quaternion.identity;

        UnityEngine.XR.InputDevice device = InputDevices.GetDeviceAtXRNode(node);
        return device.isValid &&
            device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out rotation);
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
}
