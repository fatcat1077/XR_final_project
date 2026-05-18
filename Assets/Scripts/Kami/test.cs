using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class test : MonoBehaviour
{
    void Update()
    {
        // switch environment
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            var sessionState = FindObjectOfType<ClassroomSessionState>();
            if (sessionState != null && sessionState.Object != null && sessionState.Object.IsValid) {
                sessionState.RequestSetEnvironment(ClassroomEnvironment.Ocean);
            } else {
                Debug.LogWarning("ClassroomSessionState 還沒準備好！");
            }
        }
        // raise hand
        else if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            var sessionState = FindObjectOfType<ClassroomSessionState>();
            if (sessionState != null && sessionState.Object != null && sessionState.Object.IsValid) {
                sessionState.RequestSetStudentHandRaised(true);
            } else {
                Debug.LogWarning("ClassroomSessionState 還沒準備好！");
            }
        }
    }
}