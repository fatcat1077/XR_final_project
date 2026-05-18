using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ClassroomHandRaiseUI : MonoBehaviour
{
    [Header("Session State")]
    [SerializeField] private ClassroomSessionState sessionState;

    [Header("Existing UI References")]
    [SerializeField] private GameObject studentRoot;
    [SerializeField] private Button raiseHandButton;
    [SerializeField] private Button lowerHandButton;
    [SerializeField] private TMP_Text studentStatusText;

    [SerializeField] private GameObject teacherRoot;
    [SerializeField] private Button clearHandButton;
    [SerializeField] private TMP_Text teacherStatusText;

    [Header("Fallback UI")]
    [SerializeField] private bool createFallbackUi = true;

    private bool subscribed;

    private void Start()
    {
        EnsureFallbackUi();
        BindButtons();
        BindSessionState();
        RefreshRoleVisibility();
        RefreshHandRaiseStatus();
    }

    private void Update()
    {
        if (!subscribed)
            BindSessionState();
    }

    private void OnDestroy()
    {
        if (sessionState != null)
            sessionState.StudentHandRaisedChanged -= HandleStudentHandRaisedChanged;

        if (raiseHandButton != null)
            raiseHandButton.onClick.RemoveListener(RaiseHand);

        if (lowerHandButton != null)
            lowerHandButton.onClick.RemoveListener(LowerHand);

        if (clearHandButton != null)
            clearHandButton.onClick.RemoveListener(ClearHandRaise);
    }

    public void RaiseHand()
    {
        BindSessionState();

        if (LocalUserProfile.Role != UserRole.Student)
        {
            Debug.LogWarning("[ClassroomHandRaiseUI] Only Student can raise hand.");
            return;
        }

        if (sessionState == null)
        {
            Debug.LogError("[ClassroomHandRaiseUI] ClassroomSessionState not found. Cannot raise hand.");
            return;
        }

        sessionState.RequestSetStudentHandRaised(true);
        SetStudentStatus("Hand raised");
    }

    public void LowerHand()
    {
        BindSessionState();

        if (LocalUserProfile.Role != UserRole.Student)
        {
            Debug.LogWarning("[ClassroomHandRaiseUI] Only Student can lower hand.");
            return;
        }

        if (sessionState == null)
        {
            Debug.LogError("[ClassroomHandRaiseUI] ClassroomSessionState not found. Cannot lower hand.");
            return;
        }

        sessionState.RequestSetStudentHandRaised(false);
        SetStudentStatus("Hand lowered");
    }

    public void ToggleHand()
    {
        BindSessionState();

        if (sessionState == null)
            return;

        if (sessionState.IsStudentHandRaisedValue)
            LowerHand();
        else
            RaiseHand();
    }

    public void ClearHandRaise()
    {
        BindSessionState();

        if (LocalUserProfile.Role != UserRole.Teacher)
        {
            Debug.LogWarning("[ClassroomHandRaiseUI] Only Teacher can clear hand raise.");
            return;
        }

        if (sessionState == null)
        {
            Debug.LogError("[ClassroomHandRaiseUI] ClassroomSessionState not found. Cannot clear hand raise.");
            return;
        }

        sessionState.RequestClearStudentHandRaised();
    }

    private void BindSessionState()
    {
        if (sessionState == null)
            sessionState = GetComponent<ClassroomSessionState>();

        if (sessionState == null)
            sessionState = ClassroomSessionState.FindInScene();

        if (sessionState == null || subscribed)
            return;

        sessionState.StudentHandRaisedChanged += HandleStudentHandRaisedChanged;
        subscribed = true;
        RefreshHandRaiseStatus();

        Debug.Log("[ClassroomHandRaiseUI] Bound to ClassroomSessionState.");
    }

    private void BindButtons()
    {
        if (raiseHandButton != null)
        {
            raiseHandButton.onClick.RemoveListener(RaiseHand);
            raiseHandButton.onClick.AddListener(RaiseHand);
        }

        if (lowerHandButton != null)
        {
            lowerHandButton.onClick.RemoveListener(LowerHand);
            lowerHandButton.onClick.AddListener(LowerHand);
        }

        if (clearHandButton != null)
        {
            clearHandButton.onClick.RemoveListener(ClearHandRaise);
            clearHandButton.onClick.AddListener(ClearHandRaise);
        }
    }

    private void HandleStudentHandRaisedChanged(bool raised, PlayerRef player)
    {
        RefreshHandRaiseStatus();
        Debug.Log($"[ClassroomHandRaiseUI] Student hand raised changed: raised={raised}, player={player}");
    }

    private void RefreshRoleVisibility()
    {
        bool isStudent = LocalUserProfile.Role == UserRole.Student;
        bool isTeacher = LocalUserProfile.Role == UserRole.Teacher;

        if (studentRoot != null)
            studentRoot.SetActive(isStudent);

        if (teacherRoot != null)
            teacherRoot.SetActive(isTeacher);
    }

    private void RefreshHandRaiseStatus()
    {
        bool raised = sessionState != null && sessionState.IsStudentHandRaisedValue;

        SetStudentStatus(raised ? "Hand raised" : "Hand lowered");
        SetTeacherStatus(raised ? "Student raised hand" : "No hand raised");

        if (raiseHandButton != null)
            raiseHandButton.interactable = !raised;

        if (lowerHandButton != null)
            lowerHandButton.interactable = raised;

        if (clearHandButton != null)
            clearHandButton.interactable = raised;
    }

    private void SetStudentStatus(string message)
    {
        if (studentStatusText != null)
            studentStatusText.text = message;
    }

    private void SetTeacherStatus(string message)
    {
        if (teacherStatusText != null)
            teacherStatusText.text = message;
    }

    private void EnsureFallbackUi()
    {
        if (!createFallbackUi || studentRoot != null || teacherRoot != null)
            return;

        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
            canvas = CreateFallbackCanvas();

        studentRoot = CreatePanel(canvas.transform, "StudentHandRaisePanel", new Vector2(20f, -20f));
        studentStatusText = CreateLabel(studentRoot.transform, "StudentStatusText", "Hand lowered", new Vector2(0f, -8f));
        raiseHandButton = CreateButton(studentRoot.transform, "RaiseHandButton", "Raise Hand", new Vector2(0f, -42f));
        lowerHandButton = CreateButton(studentRoot.transform, "LowerHandButton", "Lower Hand", new Vector2(0f, -84f));

        teacherRoot = CreatePanel(canvas.transform, "TeacherHandRaisePanel", new Vector2(20f, -20f));
        teacherStatusText = CreateLabel(teacherRoot.transform, "TeacherStatusText", "No hand raised", new Vector2(0f, -8f));
        clearHandButton = CreateButton(teacherRoot.transform, "ClearHandRaiseButton", "Clear Hand", new Vector2(0f, -42f));
    }

    private static Canvas CreateFallbackCanvas()
    {
        GameObject canvasObject = new("ClassroomRuntimeUICanvas");
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        canvasObject.AddComponent<CanvasScaler>();
        canvasObject.AddComponent<GraphicRaycaster>();
        return canvas;
    }

    private static GameObject CreatePanel(Transform parent, string name, Vector2 anchoredPosition)
    {
        GameObject panel = new(name);
        panel.transform.SetParent(parent, false);

        RectTransform rectTransform = panel.AddComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0f, 1f);
        rectTransform.anchorMax = new Vector2(0f, 1f);
        rectTransform.pivot = new Vector2(0f, 1f);
        rectTransform.anchoredPosition = anchoredPosition;
        rectTransform.sizeDelta = new Vector2(190f, 130f);

        Image image = panel.AddComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0.45f);

        return panel;
    }

    private static TMP_Text CreateLabel(Transform parent, string name, string text, Vector2 anchoredPosition)
    {
        GameObject labelObject = new(name);
        labelObject.transform.SetParent(parent, false);

        RectTransform rectTransform = labelObject.AddComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0f, 1f);
        rectTransform.anchorMax = new Vector2(1f, 1f);
        rectTransform.pivot = new Vector2(0f, 1f);
        rectTransform.anchoredPosition = anchoredPosition;
        rectTransform.sizeDelta = new Vector2(-16f, 28f);

        TextMeshProUGUI label = labelObject.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = 18f;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.MidlineLeft;

        return label;
    }

    private static Button CreateButton(Transform parent, string name, string text, Vector2 anchoredPosition)
    {
        GameObject buttonObject = new(name);
        buttonObject.transform.SetParent(parent, false);

        RectTransform rectTransform = buttonObject.AddComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0f, 1f);
        rectTransform.anchorMax = new Vector2(0f, 1f);
        rectTransform.pivot = new Vector2(0f, 1f);
        rectTransform.anchoredPosition = anchoredPosition;
        rectTransform.sizeDelta = new Vector2(160f, 34f);

        Image image = buttonObject.AddComponent<Image>();
        image.color = new Color(1f, 1f, 1f, 0.9f);

        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;

        TMP_Text label = CreateLabel(buttonObject.transform, $"{name}Label", text, Vector2.zero);
        RectTransform labelRect = label.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.pivot = new Vector2(0.5f, 0.5f);
        labelRect.anchoredPosition = Vector2.zero;
        labelRect.sizeDelta = Vector2.zero;
        label.color = Color.black;
        label.alignment = TextAlignmentOptions.Center;

        return button;
    }
}
