using Fusion;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR;

public class BackendVerificationUI : MonoBehaviour
{
    private const float RefreshIntervalSeconds = 0.25f;

    [SerializeField] private ClassroomSessionState sessionState;

    private Canvas canvas;
    private GraphicRaycaster raycaster;
    private Camera uiCamera;
    private EventSystem eventSystem;
    private TMP_Text statusText;
    private TMP_InputField blackboardInput;
    private TMP_InputField sttServerInput;
    private readonly List<RaycastResult> gazeRaycastResults = new();
    private PointerEventData gazePointerEventData;
    private float nextRefreshTime;
    private bool subscribed;
    private bool isWorldSpaceCanvas;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallSceneHook()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        TryCreateForCurrentScene();
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        TryCreateForCurrentScene();
    }

    private static void TryCreateForCurrentScene()
    {
        if (FindObjectOfType<BackendVerificationUI>(true) != null)
            return;

        if (ClassroomSessionState.FindInScene() == null)
            return;

        GameObject uiObject = new("BackendVerificationUI");
        uiObject.AddComponent<BackendVerificationUI>();
    }

    private void Awake()
    {
        BuildUi();
        EnsureEventSystem();
    }

    private void Start()
    {
        BindSessionState();
        RefreshStatus();
    }

    private void Update()
    {
        if (!subscribed)
            BindSessionState();

        if (Time.unscaledTime >= nextRefreshTime)
        {
            nextRefreshTime = Time.unscaledTime + RefreshIntervalSeconds;
            RefreshStatus();
        }

        if (isWorldSpaceCanvas)
        {
            PositionCanvasInFrontOfCamera();
            HandleGazeSubmitFallback();
        }
    }

    private void OnDestroy()
    {
        if (sessionState != null)
        {
            sessionState.RolePlayersChanged -= HandleRolePlayersChanged;
            sessionState.EnvironmentChanged -= HandleEnvironmentChanged;
            sessionState.StudentHandRaisedChanged -= HandleStudentHandRaisedChanged;
            sessionState.BlackboardChanged -= HandleBlackboardChanged;
            sessionState.VideoCommandReceived -= HandleVideoCommandReceived;
        }
    }

    private void BindSessionState()
    {
        if (sessionState == null)
            sessionState = ClassroomSessionState.FindInScene();

        if (sessionState == null || subscribed)
            return;

        sessionState.RolePlayersChanged += HandleRolePlayersChanged;
        sessionState.EnvironmentChanged += HandleEnvironmentChanged;
        sessionState.StudentHandRaisedChanged += HandleStudentHandRaisedChanged;
        sessionState.BlackboardChanged += HandleBlackboardChanged;
        sessionState.VideoCommandReceived += HandleVideoCommandReceived;
        subscribed = true;

        RefreshStatus();
        Debug.Log("[BackendVerificationUI] Bound to ClassroomSessionState.");
    }

    private void BuildUi()
    {
        canvas = CreateCanvas();
        GameObject panel = CreatePanel(canvas.transform);

        statusText = CreateLabel(panel.transform, "Status", new Vector2(12f, -10f), new Vector2(336f, 128f), "Backend status loading...");

        float y = -148f;
        CreateButton(panel.transform, "Default", new Vector2(12f, y), () => RequestEnvironment(ClassroomEnvironment.Default));
        CreateButton(panel.transform, "Ocean", new Vector2(122f, y), () => RequestEnvironment(ClassroomEnvironment.Ocean));
        CreateButton(panel.transform, "Space", new Vector2(232f, y), () => RequestEnvironment(ClassroomEnvironment.Space));

        y -= 44f;
        CreateButton(panel.transform, "Raise Hand", new Vector2(12f, y), () => RequestHandRaised(true));
        CreateButton(panel.transform, "Lower Hand", new Vector2(122f, y), () => RequestHandRaised(false));
        CreateButton(panel.transform, "Clear Hand", new Vector2(232f, y), RequestClearHand);

        y -= 52f;
        blackboardInput = CreateInput(panel.transform, new Vector2(12f, y), "Text to sync");
        y -= 44f;
        CreateButton(panel.transform, "Send Text", new Vector2(12f, y), RequestSendBlackboard);
        CreateButton(panel.transform, "Demo Text", new Vector2(122f, y), RequestDemoBlackboard);
        CreateButton(panel.transform, "Clear Text", new Vector2(232f, y), RequestClearBlackboard);

        y -= 44f;
        CreateButton(panel.transform, "Video Panel", new Vector2(12f, y), RequestToggleVideoPanel);
        CreateButton(panel.transform, "Play/Pause", new Vector2(122f, y), RequestToggleVideoPlayback);
        CreateButton(panel.transform, "+10 sec", new Vector2(232f, y), RequestAdvanceVideo);

        y -= 52f;
        sttServerInput = CreateInput(panel.transform, new Vector2(12f, y), "STT server URL or PC LAN IP");
        sttServerInput.text = RuntimeNetworkSettings.GetSttServerUrl(string.Empty);
        y -= 44f;
        CreateButton(panel.transform, "Save STT", new Vector2(12f, y), RequestSaveSttServerUrl);
    }

    private void RequestEnvironment(ClassroomEnvironment environment)
    {
        if (!TryGetSessionState(out ClassroomSessionState state))
            return;

        state.RequestSetEnvironment(environment);
    }

    private void RequestHandRaised(bool raised)
    {
        if (!TryGetSessionState(out ClassroomSessionState state))
            return;

        state.RequestSetStudentHandRaised(raised);
    }

    private void RequestClearHand()
    {
        if (!TryGetSessionState(out ClassroomSessionState state))
            return;

        state.RequestClearStudentHandRaised();
    }

    private void RequestSendBlackboard()
    {
        if (!TryGetSessionState(out ClassroomSessionState state))
            return;

        string text = blackboardInput != null ? blackboardInput.text : string.Empty;
        state.RequestSetBlackboardText(text);
    }

    private void RequestDemoBlackboard()
    {
        if (!TryGetSessionState(out ClassroomSessionState state))
            return;

        state.RequestSetDemoSubtitle($"Backend sync OK {System.DateTime.Now:HH:mm:ss}");
    }

    private void RequestClearBlackboard()
    {
        if (!TryGetSessionState(out ClassroomSessionState state))
            return;

        state.RequestClearBlackboard();
    }

    private void RequestToggleVideoPanel()
    {
        if (!TryGetSessionState(out ClassroomSessionState state))
            return;

        state.RequestSetVideoPanelVisible(!state.IsVideoPanelVisibleValue);
    }

    private void RequestToggleVideoPlayback()
    {
        if (!TryGetSessionState(out ClassroomSessionState state))
            return;

        state.RequestToggleVideoPlayback();
    }

    private void RequestAdvanceVideo()
    {
        if (!TryGetSessionState(out ClassroomSessionState state))
            return;

        state.RequestSetVideoPlaybackTime(state.VideoPlaybackTimeSeconds + 10f);
    }

    private void RequestSaveSttServerUrl()
    {
        if (sttServerInput == null)
            return;

        RuntimeNetworkSettings.SaveSttServerUrl(sttServerInput.text);
        RefreshStatus("Saved STT server URL.");
    }

    private bool TryGetSessionState(out ClassroomSessionState state)
    {
        BindSessionState();
        state = sessionState;

        if (state != null)
            return true;

        Debug.LogError("[BackendVerificationUI] ClassroomSessionState not found.");
        RefreshStatus("ClassroomSessionState not found.");
        return false;
    }

    private void RefreshStatus()
    {
        RefreshStatus(null);
    }

    private void RefreshStatus(string overrideMessage)
    {
        if (statusText == null)
            return;

        if (!string.IsNullOrEmpty(overrideMessage))
        {
            statusText.text = overrideMessage;
            return;
        }

        if (sessionState == null)
        {
            statusText.text = "Waiting for ClassroomSessionState...";
            return;
        }

        NetworkRunner runner = sessionState.Runner;
        string runnerState = runner != null && runner.IsRunning ? "Running" : "Not running";
        string localPlayer = runner != null ? sessionState.Runner.LocalPlayer.ToString() : "None";
        string sessionName = runner != null && runner.SessionInfo.IsValid ? runner.SessionInfo.Name : "None";

        statusText.text =
            $"Role: {LocalUserProfile.Role}   Runner: {runnerState}\n" +
            $"Room: {sessionName}   Local: {localPlayer}\n" +
            $"Teacher: {FormatPlayer(sessionState.TeacherPlayer)}   Student: {FormatPlayer(sessionState.StudentPlayer)}\n" +
            $"Environment: {sessionState.CurrentEnvironment}   Hand: {sessionState.IsStudentHandRaisedValue}\n" +
            $"Blackboard[{sessionState.BlackboardRevision}]: {sessionState.BlackboardTextValue}\n" +
            $"Video: panel={sessionState.IsVideoPanelVisibleValue}, playing={sessionState.IsVideoPlayingValue}, t={sessionState.VideoPlaybackTimeSeconds:0.0}";
    }

    private static string FormatPlayer(PlayerRef player)
    {
        return player == PlayerRef.None ? "None" : player.PlayerId.ToString();
    }

    private void HandleRolePlayersChanged(PlayerRef teacher, PlayerRef student)
    {
        RefreshStatus();
    }

    private void HandleEnvironmentChanged(ClassroomEnvironment environment)
    {
        RefreshStatus();
    }

    private void HandleStudentHandRaisedChanged(bool raised, PlayerRef player)
    {
        RefreshStatus();
    }

    private void HandleBlackboardChanged(string text, ClassroomSubtitleSource source, int revision)
    {
        RefreshStatus();
    }

    private void HandleVideoCommandReceived(bool playing, float time, int revision)
    {
        RefreshStatus();
    }

    private Canvas CreateCanvas()
    {
        GameObject canvasObject = new("BackendVerificationCanvas");
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        isWorldSpaceCanvas = ShouldUseWorldSpaceCanvas();
        uiCamera = FindUiCamera();

        if (isWorldSpaceCanvas)
        {
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = uiCamera;

            RectTransform canvasRect = canvas.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(420f, 560f);
            canvasObject.transform.localScale = Vector3.one * 0.0026f;
            PositionCanvasInFrontOfCamera(canvasObject.transform);
        }
        else
        {
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        }

        canvas.sortingOrder = 200;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = isWorldSpaceCanvas
            ? CanvasScaler.ScaleMode.ConstantPixelSize
            : CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);

        raycaster = canvasObject.AddComponent<GraphicRaycaster>();
        return canvas;
    }

    private static GameObject CreatePanel(Transform parent)
    {
        GameObject panel = new("BackendVerificationPanel");
        panel.transform.SetParent(parent, false);

        RectTransform rect = panel.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-18f, -18f);
        rect.sizeDelta = new Vector2(360f, 480f);

        Image image = panel.AddComponent<Image>();
        image.color = new Color(0.05f, 0.06f, 0.07f, 0.88f);

        return panel;
    }

    private static bool ShouldUseWorldSpaceCanvas()
    {
        return Application.isMobilePlatform || XRSettings.enabled || XRSettings.isDeviceActive;
    }

    private static Camera FindUiCamera()
    {
        if (Camera.main != null)
            return Camera.main;

        return FindObjectOfType<Camera>();
    }

    private void PositionCanvasInFrontOfCamera()
    {
        if (canvas == null)
            return;

        if (uiCamera == null)
            uiCamera = FindUiCamera();

        PositionCanvasInFrontOfCamera(canvas.transform);
    }

    private void PositionCanvasInFrontOfCamera(Transform canvasTransform)
    {
        if (canvasTransform == null)
            return;

        if (uiCamera == null)
            uiCamera = FindUiCamera();

        if (uiCamera == null)
            return;

        Transform cameraTransform = uiCamera.transform;
        canvasTransform.position = cameraTransform.position + cameraTransform.forward * 1.8f + cameraTransform.up * -0.05f;
        canvasTransform.rotation = cameraTransform.rotation;
    }

    private void EnsureEventSystem()
    {
        eventSystem = EventSystem.current;

        if (eventSystem != null)
            return;

        GameObject eventSystemObject = new("BackendVerificationEventSystem");
        eventSystem = eventSystemObject.AddComponent<EventSystem>();
        eventSystemObject.AddComponent<StandaloneInputModule>();
    }

    private void HandleGazeSubmitFallback()
    {
        if (raycaster == null)
            return;

        EnsureEventSystem();

        if (eventSystem == null)
            return;

        gazePointerEventData ??= new PointerEventData(eventSystem);
        gazePointerEventData.Reset();
        gazePointerEventData.position = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

        gazeRaycastResults.Clear();
        raycaster.Raycast(gazePointerEventData, gazeRaycastResults);

        if (!WasSubmitPressed())
            return;

        for (int i = 0; i < gazeRaycastResults.Count; i++)
        {
            GameObject hitObject = gazeRaycastResults[i].gameObject;

            Button button = hitObject.GetComponentInParent<Button>();
            if (button != null && button.interactable)
            {
                button.onClick.Invoke();
                return;
            }

            TMP_InputField inputField = hitObject.GetComponentInParent<TMP_InputField>();
            if (inputField != null && inputField.interactable)
            {
                inputField.Select();
                inputField.ActivateInputField();
                return;
            }
        }
    }

    private static bool WasSubmitPressed()
    {
        return Input.GetMouseButtonDown(0)
            || Input.GetKeyDown(KeyCode.Return)
            || Input.GetKeyDown(KeyCode.Space)
            || TryGetButtonDown("Submit")
            || TryGetButtonDown("Fire1");
    }

    private static bool TryGetButtonDown(string buttonName)
    {
        try
        {
            return Input.GetButtonDown(buttonName);
        }
        catch
        {
            return false;
        }
    }

    private static TMP_Text CreateLabel(Transform parent, string name, Vector2 position, Vector2 size, string text)
    {
        GameObject labelObject = new(name);
        labelObject.transform.SetParent(parent, false);

        RectTransform rect = labelObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        TextMeshProUGUI label = labelObject.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = 14f;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.TopLeft;
        label.enableWordWrapping = true;
        return label;
    }

    private static Button CreateButton(Transform parent, string text, Vector2 position, UnityEngine.Events.UnityAction action)
    {
        GameObject buttonObject = new($"{text}Button");
        buttonObject.transform.SetParent(parent, false);

        RectTransform rect = buttonObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(98f, 34f);

        Image image = buttonObject.AddComponent<Image>();
        image.color = new Color(0.9f, 0.92f, 0.94f, 0.96f);

        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(action);

        TMP_Text label = CreateLabel(buttonObject.transform, "Label", Vector2.zero, Vector2.zero, text);
        RectTransform labelRect = label.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.pivot = new Vector2(0.5f, 0.5f);
        labelRect.anchoredPosition = Vector2.zero;
        labelRect.sizeDelta = Vector2.zero;
        label.fontSize = 13f;
        label.color = Color.black;
        label.alignment = TextAlignmentOptions.Center;
        label.enableWordWrapping = false;

        return button;
    }

    private static TMP_InputField CreateInput(Transform parent, Vector2 position, string placeholder)
    {
        GameObject inputObject = new("BlackboardInput");
        inputObject.transform.SetParent(parent, false);

        RectTransform rect = inputObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(336f, 36f);

        Image image = inputObject.AddComponent<Image>();
        image.color = new Color(1f, 1f, 1f, 0.96f);

        TMP_InputField input = inputObject.AddComponent<TMP_InputField>();
        input.targetGraphic = image;

        TMP_Text text = CreateLabel(inputObject.transform, "Text", new Vector2(10f, -5f), new Vector2(316f, 26f), string.Empty);
        text.color = Color.black;
        text.fontSize = 14f;
        text.alignment = TextAlignmentOptions.MidlineLeft;

        TMP_Text placeholderText = CreateLabel(inputObject.transform, "Placeholder", new Vector2(10f, -5f), new Vector2(316f, 26f), placeholder);
        placeholderText.color = new Color(0f, 0f, 0f, 0.45f);
        placeholderText.fontSize = 14f;
        placeholderText.alignment = TextAlignmentOptions.MidlineLeft;

        input.textComponent = text;
        input.placeholder = placeholderText;
        return input;
    }
}
