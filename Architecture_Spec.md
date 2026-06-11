# Technical Specification: MeetingRecorder (.NET 10/WPF)

## 1. Project Overview
MeetingRecorder is a lightweight WPF desktop app that runs primarily from the system tray, detects active meetings by monitoring Windows audio sessions, and automatically records mixed audio (system loopback + microphone) into a single output file.

### Tech Stack
- **Framework:** .NET 10
- **Language:** C# 14
- **UI Framework:** WPF + MVVM (CommunityToolkit.Mvvm)
- **Audio Library:** NAudio (WASAPI + LAME)
- **DI Container:** Microsoft.Extensions.DependencyInjection
- **Target OS:** Windows 10/11

## 2. Architecture and Design Approach
The implementation follows an **MVVM + service-layer** design with event-driven coordination.

- **UI Layer:** `MainWindow` + `MainViewModel` and `SettingsWindow` + `SettingsViewModel` expose status and commands.
- **Coordination Layer:** `SessionCoordinator` manages app session state transitions.
- **Infrastructure Layer:** Audio session monitoring, recording, filesystem, and platform services are injected as interfaces.
- **Composition Root:** `App.xaml.cs` wires all dependencies as singletons/transients and initializes tray behavior.

## 3. Runtime Components

1. **AudioSessionDetector (`IAudioSessionMonitor`)**
   - Polls active communication audio sessions every ~3 seconds.
   - Checks process names against `AppSettings.WhitelistedProcesses` (e.g. handles `ms-teams_modulehost` for new Teams audio sessions).
   - Resolves window titles from sibling/parent processes if the active session process has no main window (e.g. gets the main `ms-teams` window title for `ms-teams_modulehost` sessions).
   - Guarantees full disposal of all retrieved audio session COM wrappers to prevent memory/handle leaks.
   - Raises `MeetingStarted` when a whitelisted process has an active session.
   - Raises `MeetingEnded` after inactivity exceeds debounce configuration.

2. **SessionCoordinator**
   - Maintains `SessionState` (`Idle`, `Detecting`, `Recording`, `Saving`).
   - Subscribes to detector events, coordinates note generation, and triggers cloud sync.
   - Emits:
     - `RecordingRequested(RecordingRequestedEventArgs)`
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

6. **Dynamic UI Localizer**
   - Manages localized text strings (`Resources.resx` and `Resources.zh-CN.resx`) for UI controls.
   - Binds UI headers, buttons, and status labels to dynamic properties in `MainViewModel` that raise `PropertyChanged` events when the UI culture changes, enabling instant runtime translation updates without application restarts.

7. **GoogleDriveSyncService (`ICloudSyncService`)**
   - Implements a thread-safe, non-blocking background queue using `System.Threading.Channels.Channel<string>`.
   - Authenticates silently to Google Drive using `GoogleWebAuthorizationBroker` with access tokens securely encrypted locally using Windows DPAPI (`DpapiFileDataStore`) under the local application data directory (`%LocalAppData%\MeetingRecorder\token.json`).
   - Supports manual user authentication ("Sign in" button in the settings window) allowing immediate login testing with default (built-in) credentials (custom BYOK credentials are temporarily disabled).
   - Automatically finds or creates a target folder named `"Meeting_Auto_Sync"` and uploads files asynchronously.

8. **SettingsViewModel**
   - Bridges settings configuration state with `SettingsWindow`.
   - Provides commands for directory browsing, token clearing, and manual Google Drive OAuth sign-in.
   - Saves settings on request and handles language transitions at runtime.

## 4. State and Event Flow
1. App startup configures DI and creates `MainViewModel`.
2. `MainViewModel` starts `SessionCoordinator`, moving state to `Detecting`.
3. `AudioSessionDetector` finds active whitelisted meeting audio and raises `MeetingStarted`.
4. `SessionCoordinator` raises `RecordingRequested` and transitions to `Recording`.
5. `MainViewModel` starts `IAudioRecorder` with generated output path/format.
6. On meeting inactivity beyond debounce, detector triggers `MeetingEnded`.
7. `SessionCoordinator` transitions to `Saving`, raises `RecordingStopped` (flushing recorder files), writes the markdown notes file using `INoteWriterService` and `IFileIOService`, enqueues both the audio and markdown files in the background sync service, and returns to `Detecting` (or `Idle` if monitoring stopped).

## 5. Configuration Model
`AppSettings` controls:
- `WhitelistedProcesses` (default: wemeetapp, Zoom, ms-teams, ms-teams_modulehost, Teams, Feishu, DingTalk, Webex)
- `OutputDirectory` (default under Documents\MeetingRecordings)
- `DebounceSeconds` (default: 5)
- `OutputFormat` (`Mp3` or `Wav`)
- `UiLanguage` (UI translation language)
- `GoogleDriveEnabled` (enable/disable sync)
- `GoogleClientId` & `GoogleClientSecret` (optional custom API keys — temporarily disabled, built-in credentials are used instead)
- `GoogleDriveFolderPath` (remote upload directory)

### Persistence
Settings are automatically saved as JSON in the local application data directory (`%LocalAppData%\MeetingRecorder\settings.json`) whenever they are updated from the UI or Settings Window. On application startup, settings are loaded from this file or default settings are created if it does not exist.

### Google Drive Path Resolution
Folder existence checks are case-insensitive. If a user specifies a target folder path like `work/meetings` but the folders exist on Google Drive as `Work/Meetings`, the upload service resolves the path to the correct existing folders using their actual casing, and the local settings are automatically updated to match the correct casing found on Google Drive.

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
    │   ├── WasapiRecorder.cs
    │   ├── ICloudSyncService.cs
    │   ├── GoogleDriveSyncService.cs
    │   ├── DpapiFileDataStore.cs
    │   └── RecordingRequestedEventArgs.cs
    ├── ViewModels/
    │   ├── MainViewModel.cs
    │   └── SettingsViewModel.cs
    ├── MainWindow.xaml
    ├── SettingsWindow.xaml
    ├── App.xaml.cs
    └── credentials.json (Embedded Resource)

## 7. Engineering Constraints and Goals
- Keep monitoring overhead low (polling loop + debounce).
- Ensure deterministic cleanup for capture/writer resources on stop/exit.
- Preserve reliable unattended tray-first operation.
- Keep boundaries testable through service abstractions (`MeetingRecorder.Tests` covers coordinator and note writer behavior).

## 8. Specification Maintenance Guideline (CRITICAL)
- **Developer and AI Agent Responsibility:** Every developer and AI agent working on this codebase must update this technical specification (`Architecture_Spec.md`) whenever architectural changes, configuration options, process whitelists, UI behaviors, or component responsibilities are modified. This maintains the integrity and correctness of the design documentation as a single source of truth.
