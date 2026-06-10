using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class StudentHandRaiseRuntimeBridge : MonoBehaviour
{
    private const int RefreshIntervalFrames = 30;

    private ClassroomSessionState sessionState;
    private Canvas overlayCanvas;
    private Text overlayText;
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
            RefreshSubscription("periodic refresh");
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
        TeacherHandRaiseUI[] handRaiseViews = FindObjectsOfType<TeacherHandRaiseUI>(true);
        for (int i = 0; i < handRaiseViews.Length; i++)
            handRaiseViews[i].UpdateHandRaiseStatus(raised);

        if (ShouldShowTeacherOverlay())
            SetOverlayVisible(raised);

        if (lastShownRaised != raised)
        {
            Debug.Log($"[StudentHandRaiseRuntimeBridge] Teacher/server received student hand raised={raised}");
            lastShownRaised = raised;
        }
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

    private void SetOverlayVisible(bool visible)
    {
        EnsureOverlay();

        if (overlayCanvas == null)
            return;

        overlayCanvas.gameObject.SetActive(visible);
        if (overlayText != null)
            overlayText.text = visible ? "Student raised hand" : string.Empty;
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
        panelRect.sizeDelta = new Vector2(360f, 54f);

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
        overlayText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        overlayText.fontSize = 26;
        overlayText.fontStyle = FontStyle.Bold;
        overlayText.alignment = TextAnchor.MiddleCenter;
        overlayText.color = Color.white;
        overlayText.raycastTarget = false;

        overlayCanvas.gameObject.SetActive(false);
    }
}
