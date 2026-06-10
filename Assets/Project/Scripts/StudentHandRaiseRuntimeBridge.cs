using System.Collections;
using Fusion;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class StudentHandRaiseRuntimeBridge : MonoBehaviour
{
    private const int RefreshIntervalFrames = 30;
    private const float TeacherNotificationSeconds = 3f;
    private const string RaisedPromptText = "Student raised hand";

    private ClassroomSessionState sessionState;
    private Canvas overlayCanvas;
    private Text overlayText;
    private Canvas studentControlCanvas;
    private Button studentHandButton;
    private Text studentHandButtonText;
    private Image studentHandButtonImage;
    private Coroutine teacherNotificationRoutine;
    private bool lastShownRaised;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindObjectOfType<StudentHandRaiseRuntimeBridge>() != null)
            return;

        GameObject bridgeObject = new GameObject("StudentHandRaiseRuntimeBridge");
        DontDestroyOnLoad(bridgeObject);
        bridgeObject.AddComponent<StudentHandRaiseRuntimeBridge>();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
        RefreshSubscription("enable");
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        Unsubscribe();
    }

    private void Update()
    {
        if ((Time.frameCount % RefreshIntervalFrames) == 0)
        {
            RefreshSubscription("periodic refresh");
            RefreshStudentControl();
        }
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        RefreshSubscription($"scene loaded: {scene.name}");
    }

    private void RefreshSubscription(string reason)
    {
        if (sessionState != null && sessionState.Object != null && sessionState.Object.IsValid)
            return;

        Unsubscribe();

        if (!ClassroomSessionState.TryGetActiveNetworked(out ClassroomSessionState activeState))
            return;

        sessionState = activeState;
        sessionState.StudentHandRaisedChanged += HandleStudentHandRaisedChanged;
        HandleStudentHandRaisedChanged(sessionState.IsStudentHandRaised);
        Debug.Log($"[StudentHandRaiseRuntimeBridge] Subscribed to ClassroomSessionState. reason={reason}");
    }

    private void Unsubscribe()
    {
        if (sessionState == null)
            return;

        sessionState.StudentHandRaisedChanged -= HandleStudentHandRaisedChanged;
        sessionState = null;
    }

    private void HandleStudentHandRaisedChanged(bool raised)
    {
        UpdateStudentControlState(raised);

        if (ShouldShowTeacherOverlay())
            SetTeacherNotificationVisible(raised);

        if (lastShownRaised != raised)
        {
            Debug.Log($"[StudentHandRaiseRuntimeBridge] Teacher/server received student hand raised={raised}");
            lastShownRaised = raised;
        }
    }

    private void SetTeacherNotificationVisible(bool visible)
    {
        if (teacherNotificationRoutine != null)
        {
            StopCoroutine(teacherNotificationRoutine);
            teacherNotificationRoutine = null;
        }

        ApplyTeacherNotificationVisible(visible);

        if (visible)
            teacherNotificationRoutine = StartCoroutine(HideTeacherNotificationAfterDelay());
    }

    private IEnumerator HideTeacherNotificationAfterDelay()
    {
        yield return new WaitForSeconds(TeacherNotificationSeconds);
        teacherNotificationRoutine = null;
        ApplyTeacherNotificationVisible(false);
    }

    private void ApplyTeacherNotificationVisible(bool visible)
    {
        TeacherHandRaiseUI[] handRaiseViews = FindObjectsOfType<TeacherHandRaiseUI>(true);
        for (int i = 0; i < handRaiseViews.Length; i++)
            handRaiseViews[i].UpdateHandRaiseStatus(visible);

        SetOverlayVisible(visible);
    }

    private static bool ShouldShowTeacherOverlay()
    {
        if (LocalUserProfile.Role == UserRole.Teacher)
            return true;

        NetworkRunner[] runners = FindObjectsOfType<NetworkRunner>(true);
        for (int i = 0; i < runners.Length; i++)
        {
            NetworkRunner runner = runners[i];
            if (runner != null && runner.IsRunning && runner.IsServer)
                return true;
        }

        return false;
    }

    private void RefreshStudentControl()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (studentControlCanvas != null)
            studentControlCanvas.gameObject.SetActive(false);

        return;
#else
        ClassroomSessionState activeState = null;
        if (LocalUserProfile.Role != UserRole.Student ||
            !ClassroomSessionState.TryGetActiveNetworked(out activeState))
        {
            if (studentControlCanvas != null)
                studentControlCanvas.gameObject.SetActive(false);

            return;
        }

        EnsureStudentControl();
        if (studentControlCanvas == null)
            return;

        studentControlCanvas.gameObject.SetActive(true);
        studentHandButton.interactable = true;
        UpdateStudentControlState(activeState.IsStudentHandRaised);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
#endif
    }

    private void EnsureStudentControl()
    {
        if (studentControlCanvas != null)
            return;

        EnsureEventSystem();

        GameObject canvasObject = new GameObject(
            "PcStudentHandRaiseControls",
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        DontDestroyOnLoad(canvasObject);

        studentControlCanvas = canvasObject.GetComponent<Canvas>();
        studentControlCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        studentControlCanvas.sortingOrder = 480;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.matchWidthOrHeight = 0.5f;

        GameObject buttonObject = new GameObject("HandRaiseToggleButton", typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(canvasObject.transform, false);

        RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(0.5f, 0f);
        buttonRect.anchorMax = new Vector2(0.5f, 0f);
        buttonRect.pivot = new Vector2(0.5f, 0f);
        buttonRect.anchoredPosition = new Vector2(0f, 118f);
        buttonRect.sizeDelta = new Vector2(260f, 68f);

        studentHandButtonImage = buttonObject.GetComponent<Image>();
        studentHandButtonImage.color = new Color(0.08f, 0.44f, 0.56f, 0.94f);

        studentHandButton = buttonObject.GetComponent<Button>();
        studentHandButton.targetGraphic = studentHandButtonImage;
        studentHandButton.onClick.AddListener(ToggleStudentHandRaised);

        ColorBlock colors = studentHandButton.colors;
        colors.normalColor = new Color(0.08f, 0.44f, 0.56f, 0.94f);
        colors.highlightedColor = new Color(0.12f, 0.62f, 0.74f, 1f);
        colors.pressedColor = new Color(0.05f, 0.32f, 0.42f, 1f);
        colors.selectedColor = colors.highlightedColor;
        studentHandButton.colors = colors;

        GameObject textObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
        textObject.transform.SetParent(buttonObject.transform, false);

        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        studentHandButtonText = textObject.GetComponent<Text>();
        studentHandButtonText.font = GetRuntimeFont();
        studentHandButtonText.fontSize = 28;
        studentHandButtonText.fontStyle = FontStyle.Bold;
        studentHandButtonText.alignment = TextAnchor.MiddleCenter;
        studentHandButtonText.color = Color.white;
        studentHandButtonText.raycastTarget = false;

        UpdateStudentControlState(false);
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current == null)
        {
            GameObject eventSystemObject = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            DontDestroyOnLoad(eventSystemObject);
            return;
        }

        if (EventSystem.current.GetComponent<BaseInputModule>() == null)
            EventSystem.current.gameObject.AddComponent<StandaloneInputModule>();
    }

    private void ToggleStudentHandRaised()
    {
        if (LocalUserProfile.Role != UserRole.Student)
        {
            Debug.LogWarning($"[StudentHandRaiseRuntimeBridge] Ignored PC hand button; local role is {LocalUserProfile.Role}.");
            return;
        }

        if (!ClassroomSessionState.TryGetActiveNetworked(out ClassroomSessionState activeState))
        {
            Debug.LogWarning("[StudentHandRaiseRuntimeBridge] PC hand button could not find ClassroomSessionState.");
            return;
        }

        bool raised = !activeState.IsStudentHandRaised;
        activeState.RequestSetStudentHandRaised(raised);
        UpdateStudentControlState(raised);
        Debug.Log($"[StudentHandRaiseRuntimeBridge] PC student hand button clicked. raised={raised}");
    }

    private void UpdateStudentControlState(bool raised)
    {
        if (studentHandButtonText != null)
            studentHandButtonText.text = raised ? "Lower Hand" : "Raise Hand";

        if (studentHandButtonImage != null)
            studentHandButtonImage.color = raised ? new Color(0.62f, 0.16f, 0.12f, 0.96f) : new Color(0.08f, 0.44f, 0.56f, 0.94f);
    }

    private void SetOverlayVisible(bool visible)
    {
        EnsureOverlay();

        if (overlayCanvas == null)
            return;

        overlayCanvas.gameObject.SetActive(visible);
        if (overlayText != null)
            overlayText.text = visible ? RaisedPromptText : string.Empty;
    }

    private void EnsureOverlay()
    {
        if (overlayCanvas != null)
            return;

        GameObject canvasObject = new GameObject(
            "StudentHandRaiseOverlay",
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        DontDestroyOnLoad(canvasObject);

        overlayCanvas = canvasObject.GetComponent<Canvas>();
        overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        overlayCanvas.sortingOrder = 500;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.matchWidthOrHeight = 0.5f;

        GameObject panelObject = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        panelObject.transform.SetParent(canvasObject.transform, false);

        RectTransform panelRect = panelObject.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 1f);
        panelRect.anchorMax = new Vector2(0.5f, 1f);
        panelRect.pivot = new Vector2(0.5f, 1f);
        panelRect.anchoredPosition = new Vector2(0f, -28f);
        panelRect.sizeDelta = new Vector2(420f, 82f);

        Image panelImage = panelObject.GetComponent<Image>();
        panelImage.color = new Color(0.08f, 0.1f, 0.12f, 0.86f);

        GameObject textObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
        textObject.transform.SetParent(panelObject.transform, false);

        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        overlayText = textObject.GetComponent<Text>();
        overlayText.font = GetRuntimeFont();
        overlayText.fontSize = 24;
        overlayText.fontStyle = FontStyle.Bold;
        overlayText.alignment = TextAnchor.MiddleCenter;
        overlayText.color = Color.white;
        overlayText.raycastTarget = false;

        overlayCanvas.gameObject.SetActive(false);
    }

    private static Font GetRuntimeFont()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        return font != null ? font : Font.CreateDynamicFontFromOSFont("Arial", 16);
    }
}
