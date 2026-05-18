# Quest Build Setup

This project has Photon/Fusion backend validation logic that can run on PC first, then on Quest as an Android APK.

## Already configured in project files

- Android manifest at `Assets/Plugins/Android/AndroidManifest.xml`
  - Internet permission
  - Network state permission
  - Microphone permission
  - Audio settings permission
  - Cleartext HTTP enabled for LAN STT testing
  - Quest supported device metadata
- Android PlayerSettings in `ProjectSettings/ProjectSettings.asset`
  - Android package id: `com.xrcourseproject.classroom`
  - Min SDK: API 23
  - ARM64 target architecture
  - IL2CPP backend
  - Force Internet permission
  - Custom Android manifest enabled
  - Insecure HTTP allowed for local STT testing
- Runtime Quest bootstrap at `Assets/Project/Scripts/Quest/QuestRuntimeBootstrap.cs`
  - Keeps the app awake
  - Keeps app running in background
  - Requests microphone permission on Android
  - Warns if STT URL is still localhost on Quest
- STT server URL handling at `Assets/Project/Scripts/Quest/RuntimeNetworkSettings.cs`
  - Stores runtime STT URL in PlayerPrefs key `XR_STT_SERVER_URL`
  - Normalizes plain IP/host input into a URL ending with `/stt`
- Backend validation UI at `Assets/Project/Scripts/BackendVerificationUI.cs`
  - Uses screen-space UI in Editor/PC
  - Uses world-space UI on Android/XR
  - Adds basic gaze/submit fallback for simple button validation
  - Lets you save an STT server URL at runtime

## Manual Unity setup still required

1. Install Android support in Unity Hub for Unity `2022.3.52f1`.
   - Android Build Support
   - Android SDK & NDK Tools
   - OpenJDK

2. Install XR packages from Unity Package Manager.
   - `XR Plug-in Management`
   - `OpenXR Plugin`
   - `Meta OpenXR` if available for this Unity version
   - Optional but recommended: `XR Interaction Toolkit`

3. Enable OpenXR for Android.
   - Open `Edit > Project Settings > XR Plug-in Management`
   - Select the Android tab
   - Enable `OpenXR`

4. Configure OpenXR features.
   - Open `Project Settings > XR Plug-in Management > OpenXR`
   - Select Android settings
   - Add/enable `Oculus Touch Controller Profile`
   - Enable `Meta Quest Support` or Meta Quest feature group if shown

5. Add a real XR rig to the scene.
   - Add `XR Origin` to `Assets/Project/Scenes/Classroom.unity`
   - Make the XR camera the local player camera
   - Disable desktop-only cameras/listeners for Quest runtime
   - Keep remote avatars visual-only without camera/audio listener

6. If using XR Interaction Toolkit UI input:
   - Add `XR UI Input Module` to the EventSystem
   - Add ray interactors to controller objects
   - Add tracked device raycaster support to world-space canvases

7. Set the STT server URL for Quest.
   - Run `python_scripts/server.py` on your PC
   - Find the PC LAN IP, for example `192.168.1.23`
   - In the in-game validation UI, save:
     `http://192.168.1.23:5000/stt`
   - Make sure Windows Firewall allows inbound TCP `5000`
   - Quest and PC must be on the same Wi-Fi

8. Build and test.
   - `File > Build Settings > Android`
   - `Switch Platform`
   - Connect Quest through USB with developer mode enabled
   - Use `Build And Run`
   - Test PC Teacher + Quest Student first
   - Then test Quest Teacher + PC Student
   - Finally test Quest + Quest

## Validation checklist

- Quest APK launches without permission errors.
- Microphone permission prompt appears and is allowed.
- Photon room connects in region `hk`.
- Teacher and Student both appear in the validation UI.
- Environment buttons sync across devices.
- Raise/lower/clear hand syncs across devices.
- Demo blackboard text syncs across devices.
- Photon Voice sends audio both ways.
- STT only uses `127.0.0.1` in Editor/PC testing, never on Quest.
