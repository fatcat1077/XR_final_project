using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class InputManager : MonoBehaviour
{
    void Update()
    {
        // default
        if (Input.GetKeyDown(KeyCode.Alpha0))
        {
            var sessionState = FindObjectOfType<ClassroomSessionState>();
            if (sessionState != null && sessionState.Object != null && sessionState.Object.IsValid) {
                Debug.Log("switch to default environment");
                sessionState.RequestSetEnvironment(ClassroomEnvironment.Default);
            } else {
                Debug.LogWarning("ClassroomSessionState 還沒準備好！");
            }
        }
        // ocean
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            var sessionState = FindObjectOfType<ClassroomSessionState>();
            if (sessionState != null && sessionState.Object != null && sessionState.Object.IsValid) {
                Debug.Log("switch to ocean environment");
                sessionState.RequestSetEnvironment(ClassroomEnvironment.Ocean);
            } else {
                Debug.LogWarning("ClassroomSessionState 還沒準備好！");
            }
        }
        // space
        else if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            var sessionState = FindObjectOfType<ClassroomSessionState>();
            if (sessionState != null && sessionState.Object != null && sessionState.Object.IsValid) {
                Debug.Log("switch to space environment");
                sessionState.RequestSetEnvironment(ClassroomEnvironment.Space);
            } else {
                Debug.LogWarning("ClassroomSessionState 還沒準備好！");
            }
        }
        // raise hand
        else if(Input.GetKeyDown(KeyCode.Alpha3))
        {
            if (LocalUserProfile.Role != UserRole.Student) {
                Debug.LogWarning("[權限不足] 只有學生可以舉手。");
                return;
            }

            var sessionState = FindObjectOfType<ClassroomSessionState>();
            if (sessionState != null && sessionState.Object != null && sessionState.Object.IsValid) {
                Debug.Log("raise hand");
                sessionState.RequestSetStudentHandRaised(true);
            } else {
                Debug.LogWarning("ClassroomSessionState 還沒準備好！");
            }
        }
        // student put hand down
        else if(Input.GetKeyDown(KeyCode.Alpha4))
        {
            if (LocalUserProfile.Role != UserRole.Student) {
                Debug.LogWarning("[權限不足] 只有學生可以放下自己的手。");
                return;
            }

            var sessionState = FindObjectOfType<ClassroomSessionState>();
            if (sessionState != null && sessionState.Object != null && sessionState.Object.IsValid) {
                Debug.Log("put hand down");
                sessionState.RequestSetStudentHandRaised(false);
            } else {
                Debug.LogWarning("ClassroomSessionState 還沒準備好！");
            }
        }
        // teacher put hand down
        else if(Input.GetKeyDown(KeyCode.Alpha5))
        {
            if (LocalUserProfile.Role != UserRole.Teacher) {
                Debug.LogWarning("[權限不足] 只有老師可以清除舉手狀態。");
                return;
            }
            
            var sessionState = FindObjectOfType<ClassroomSessionState>();
            if (sessionState != null && sessionState.Object != null && sessionState.Object.IsValid) {
                Debug.Log("put student's hand down");
                sessionState.RequestClearStudentHandRaised();
            } else {
                Debug.LogWarning("ClassroomSessionState 還沒準備好！");
            }
        }
        // show video panel
        else if(Input.GetKeyDown(KeyCode.Alpha6))
        {
            if (LocalUserProfile.Role != UserRole.Teacher) {
                Debug.LogWarning("[權限不足] 只有老師可以顯示影片面板。");
                return;
            }
            
            var sessionState = FindObjectOfType<ClassroomSessionState>();
            if (sessionState != null && sessionState.Object != null && sessionState.Object.IsValid) {
                Debug.Log("show video panel");
                sessionState.RequestSetVideoPanelVisible(true);
            } else {
                Debug.LogWarning("ClassroomSessionState 還沒準備好！");
            }
        }
        // close video panel
        else if(Input.GetKeyDown(KeyCode.Alpha7))
        {
            if (LocalUserProfile.Role != UserRole.Teacher) {
                Debug.LogWarning("[權限不足] 只有老師可以關閉影片面板。");
                return;
            }
            
            var sessionState = FindObjectOfType<ClassroomSessionState>();
            if (sessionState != null && sessionState.Object != null && sessionState.Object.IsValid) {
                Debug.Log("close video panel");
                sessionState.RequestSetVideoPanelVisible(false);
            } else {
                Debug.LogWarning("ClassroomSessionState 還沒準備好！");
            }
        }
        // play video
        else if(Input.GetKeyDown(KeyCode.Alpha8))
        {
            if (LocalUserProfile.Role != UserRole.Teacher) {
                Debug.LogWarning("[權限不足] 只有老師可以播放影片。");
                return;
            }
            
            var sessionState = FindObjectOfType<ClassroomSessionState>();
            if (sessionState != null && sessionState.Object != null && sessionState.Object.IsValid) {
                Debug.Log("play video");
                sessionState.RequestSetVideoPlaying(true);
            } else {
                Debug.LogWarning("ClassroomSessionState 還沒準備好！");
            }
        }
        // pause video
        else if(Input.GetKeyDown(KeyCode.Alpha9))
        {
            if (LocalUserProfile.Role != UserRole.Teacher) {
                Debug.LogWarning("[權限不足] 只有老師可以暫停影片。");
                return;
            }
            
            var sessionState = FindObjectOfType<ClassroomSessionState>();
            if (sessionState != null && sessionState.Object != null && sessionState.Object.IsValid) {
                Debug.Log("stop video");
                sessionState.RequestSetVideoPlaying(false);
            } else {
                Debug.LogWarning("ClassroomSessionState 還沒準備好！");
            }
        }

    }
}
