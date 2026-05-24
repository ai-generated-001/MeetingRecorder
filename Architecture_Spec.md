# Technical Specification: MeetingRecorder (.NET 10/WPF)

## 1. Project Overview
MeetingRecorder is a lightweight WPF desktop app that runs primarily from the system tray, detects active meetings by monitoring Windows audio sessions, and automatically records mixed audio (system loopback + microphone) into a single output file.

### Tech Stack
- **Framework:** .NET 10
- **Language:** C# 14
- **UI Framework:** WPF + MVVM
- **Audio Library:** NAudio (WASAPI + LAME)
- **DI Container:** Microsoft.Extensions.DependencyInjection
- **Target OS:** Windows 10/11

## 2. Architecture and Design Approach
The implementation follows an **MVVM + service-layer** design with event-driven coordination.

- **UI Layer:** `MainWindow` + `MainViewModel` expose status and commands.
- **Coordination Layer:** `SessionCoordinator` manages app session state transitions.
- **Infrastructure Layer:** Audio session monitoring, recording, filesystem, and platform services are injected as interfaces.
- **Composition Root:** `App.xaml.cs` wires all dependencies as singletons/transients and initializes tray behavior.

## 3. Runtime Components

1. **AudioSessionDetector (`IAudioSessionMonitor`)**
   - Polls active communication audio sessions every ~3 seconds.
   - Checks process names against `AppSettings.WhitelistedProcesses`.
   - Raises `MeetingStarted` when a whitelisted process has an active session.
   - Raises `MeetingEnded` after inactivity exceeds debounce configuration.

2. **SessionCoordinator**
   - Maintains `SessionState` (`Idle`, `Detecting`, `Recording`).
   - Subscribes to detector events and emits:
     - `RecordingRequested(MeetingDetectedEventArgs)`
     - `RecordingStopped`
     - `StateChanged`
   - Applies debounce and supports manual stop behavior while continuing monitoring.

3. **WasapiRecorder (`IAudioRecorder`)**
   - Captures system audio via `WasapiLoopbackCapture`.
   - Captures microphone via `WasapiCapture`.
   - Resamples streams to a common format and mixes in real time.
   - Writes output as MP3 (`LameMP3FileWriter`) or WAV (`WaveFileWriter`).

4. **MainViewModel**
   - Bridges coordinator and recorder.
   - Starts monitoring on app startup (outside design mode).
   - Builds timestamped output filenames (optionally prefixed by sanitized window title).
   - Exposes commands: start/stop monitoring, stop recording, open folder/settings, exit.

5. **Tray and App Host (`App.xaml.cs`)**
   - Configures culture and service provider.
   - Initializes `H.NotifyIcon.TaskbarIcon` as the tray entry point.
   - Shows and positions the floating main window near the bottom-right work area.

## 4. State and Event Flow
1. App startup configures DI and creates `MainViewModel`.
2. `MainViewModel` starts `SessionCoordinator`, moving state to `Detecting`.
3. `AudioSessionDetector` finds active whitelisted meeting audio and raises `MeetingStarted`.
4. `SessionCoordinator` raises `RecordingRequested` and transitions to `Recording`.
5. `MainViewModel` starts `IAudioRecorder` with generated output path/format.
6. On meeting inactivity beyond debounce, detector triggers `MeetingEnded`.
7. `SessionCoordinator` raises `RecordingStopped` and returns to `Detecting` (or `Idle` if monitoring stopped).

## 5. Configuration Model
`AppSettings` currently controls:
- `WhitelistedProcesses` (default: wemeetapp, Zoom, ms-teams, Feishu, DingTalk, Webex)
- `OutputDirectory` (default under Documents\MeetingRecordings)
- `DebounceSeconds` (default: 5)
- `OutputFormat` (`Mp3` or `Wav`)

## 6. Current Project Structure (Implemented)

    MeetingRecorder/
    ├── Models/
    │   ├── AppSettings.cs
    │   └── MeetingDetectedEventArgs.cs
    ├── Services/
    │   ├── IAudioSessionMonitor.cs
    │   ├── IAudioRecorder.cs
    │   ├── AudioSessionDetector.cs
    │   ├── SessionCoordinator.cs
    │   └── WasapiRecorder.cs
    ├── ViewModels/
    │   └── MainViewModel.cs
    ├── MainWindow.xaml
    ├── SettingsWindow.xaml
    └── App.xaml.cs

## 7. Engineering Constraints and Goals
- Keep monitoring overhead low (polling loop + debounce).
- Ensure deterministic cleanup for capture/writer resources on stop/exit.
- Preserve reliable unattended tray-first operation.
- Keep boundaries testable through service abstractions (`MeetingRecorder.Tests` covers coordinator and note writer behavior).
