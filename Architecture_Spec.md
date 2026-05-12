# Technical Specification: Automated Meeting Recorder (.NET 10/WPF)

## 1. Project Overview
An automated, lightweight WPF background application designed to detect online meetings and record high-quality audio (System Loopback + Microphone) without manual intervention.

### Tech Stack
- **Framework:** .NET 10
- **Language:** C# 14
- **UI Framework:** WPF (System Tray based)
- **Audio Library:** NAudio or CSCore (Wrappers for WASAPI)
- **Target OS:** Windows 10/11

## 2. Architectural Design
The application follows the **MVVM (Model-View-ViewModel)** pattern and a **Service-Oriented** approach for hardware/system monitoring.

### Core Components
1. **MeetingDetectorService:** Monitors system audio sessions to detect if a whitelisted process (e.g., Zoom, Teams) is actively using audio streams.
2. **AudioRecordingService:** Handles concurrent recording of system output and microphone input, including real-time mixing.
3. **SessionCoordinator:** A state-machine manager that orchestrates the flow between Idle, Recording, and Saving states.
4. **TrayNotifyIcon:** The primary UI element for status feedback and configuration access.

## 3. Key Logic & Implementation

### A. Detection Mechanism (Audio Session Monitoring)
Instead of relying on camera status or simple process detection, the app monitors **Active Audio Sessions** via Windows IAudioSessionManager2.

- **Condition to Start:** A process in the Whitelist creates an audio session with state AudioSessionStateActive.
- **Condition to Stop:** All whitelisted audio sessions transition to AudioSessionStateInactive or are destroyed for a duration > 5 seconds (to handle network jitter).
- **Process Whitelist:** wemeetapp.exe, Zoom.exe, Teams.exe, Feishu.exe, DingTalk.exe, etc.

### B. Audio Recording (WASAPI)
- **System Audio:** Use WasapiLoopbackCapture to grab what the user hears.
- **Microphone:** Use WasapiCapture for the user's voice.
- **Mixing:** Combine both streams into a single WaveProvider and encode to .mp3 or .m4a using Media Foundation or a LAME encoder.

## 4. Proposed Project Structure

    MeetingRecorder.App/
    ├── Models/
    │   ├── AppSettings.cs
    │   └── RecordingSession.cs
    ├── Services/
    │   ├── IConferenceDetector.cs
    │   ├── IAudioRecorder.cs
    │   ├── AudioSessionDetector.cs
    │   └── WasapiRecorder.cs
    ├── ViewModels/
    │   ├── MainViewModel.cs
    │   └── SettingsViewModel.cs
    ├── Views/
    │   ├── MainWindow.xaml
    │   └── TrayView.xaml
    └── Helpers/
        ├── NativeMethods.cs
        └── PathHelper.cs

## 5. Technical Constraints & Goals
- **Low Footprint:** The detector service should poll every 3-5 seconds to minimize CPU usage.
- **Robustness:** Ensure file streams are flushed and closed gracefully if the application is terminated or the system sleeps.
- **No Privacy Popups:** As this is a personal tool, focus on seamless background operation.
- **Async/Await:** Use modern C# asynchronous patterns for all I/O and hardware monitoring tasks.
