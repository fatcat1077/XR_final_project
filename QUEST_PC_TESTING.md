# PC + Meta Quest Testing

This project is configured for a two-device classroom test:

- PC: Unity Editor or Windows build.
- Quest: Android APK using OpenXR.

## Project Settings Applied

- Android package id: `com.xrcourseproject.classroom`
- Android min SDK: API 23
- Android architecture: ARM64
- Android scripting backend: IL2CPP
- Android custom manifest enabled
- Internet, network state, microphone, and audio settings permissions
- Cleartext HTTP allowed for local STT testing
- XR Plug-in Management Android loader: OpenXR
- OpenXR Android features: Meta Quest Support, Oculus Touch Controller Profile, Meta Quest Touch Pro, Meta Quest Touch Plus
- Input handling: Both old and new input systems
- STT URL for Quest defaults to `http://192.168.0.100:5055/stt`.

## Before Testing

1. Install Unity `2022.3.52f1` with Android Build Support, Android SDK & NDK Tools, and OpenJDK.
2. Put the Quest in Developer Mode and allow USB debugging.
3. Keep PC and Quest on the same Wi-Fi.
4. Start the STT server on the PC:

```powershell
python python_scripts/server.py
```

The server prints a `Quest STT URL candidate` on startup. From the PC, verify:

```powershell
curl http://127.0.0.1:5055/health
```

5. Allow inbound Windows Firewall access for TCP `5055`.
6. If the PC Wi-Fi IP changes, launch the Quest APK with `xr_stt_url=http://<PC LAN IP>:5055/stt`, or update `DefaultQuestSttServerUrl` in `RuntimeNetworkSettings`.

## Build Quest APK

1. Open Unity.
2. Go to `XR Course > Configure PC + Quest Test Build` if settings need to be re-applied.
3. Go to `File > Build Settings`.
4. Select `Android`.
5. Click `Switch Platform`.
6. Connect the Quest through USB.
7. Click `Build And Run`.

## Two-Device Test Flow

1. Start the PC side first in Unity Editor.
2. In the lobby, choose `Teacher`.
3. Start the Quest APK.
4. In the Quest lobby, aim the right controller ray at `Student` and press the trigger.
5. Use the same room name on both sides. The current default room is `XRRoom01_voice`.
6. Confirm both devices enter `Classroom`.
7. Test environment switching from PC to Quest.
8. Test microphone / Photon Voice both directions.
9. Test speech-to-text from the Quest; the POST target should be the PC LAN URL, not `127.0.0.1`.

For a reverse role test, choose `Teacher` in the Quest lobby and `Student` on PC.
