using UnityEngine;

public class SimpleCameraController : MonoBehaviour
{
    [Header("移動與轉向速度")]
    public float moveSpeed = 5f;
    public float mouseSensitivity = 2f;

    // 紀錄目前的旋轉角度
    private float pitch = 0f; // 上下 (繞 X 軸)
    private float yaw = 0f;   // 左右 (繞 Y 軸)

    void Start()
    {
        // 遊戲開始時，隱藏滑鼠游標並將其鎖定在畫面中央
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // 初始化角度，避免攝影機一開始會突然彈到 0 度
        Vector3 rot = transform.eulerAngles;
        yaw = rot.y;
        pitch = rot.x;
    }

    void Update()
    {
        // =========================
        // 1. 滑鼠轉向 (視角控制)
        // =========================
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

        yaw += mouseX;
        pitch -= mouseY; // 這裡用減的，否則滑鼠上下會是反轉的 (Inverted)

        // 限制上下看的角度 (限制在 -90 到 90 度)，避免脖子向後折斷
        pitch = Mathf.Clamp(pitch, -90f, 90f);

        // 套用旋轉角度到攝影機上 (Z軸固定為 0，避免畫面傾斜)
        transform.eulerAngles = new Vector3(pitch, yaw, 0f);

        // =========================
        // 2. WASD 移動 (位置控制)
        // =========================
        float moveX = Input.GetAxis("Horizontal"); // A, D 鍵
        float moveZ = Input.GetAxis("Vertical");   // W, S 鍵

        // 計算移動方向 (根據攝影機目前的「正前方」和「正右方」來決定)
        Vector3 moveDirection = (transform.right * moveX) + (transform.forward * moveZ);

        // 套用移動 (乘上 Time.deltaTime 確保移動速度不會受到畫面幀率 FPS 影響)
        transform.position += moveDirection * moveSpeed * Time.deltaTime;
    }
}