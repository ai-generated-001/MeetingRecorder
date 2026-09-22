# Technical Specification: MeetingRecorder (.NET 10/WPF)

## 1. Project Overview
MeetingRecorder is a lightweight WPF desktop app that runs primarily from the system tray, detects active meetings by monitoring Windows audio sessions, and automatically records mixed audio (system loopback + microphone) into a single output file.

### Tech Stack
- **Framework:** .NET 10
- **Language:** C# 14
- **UI Framework:** WPF + MVVM (CommunityToolkit.Mvvm)
- **Audio Library:** NAudio (WASAPI + LAME)
- **AI Speech Recognition:** Alibaba Cloud DashScope WebSocket API (Paraformer-realtime-v2)
- **AI Contextual Insights:** Alibaba Cloud DashScope Text Generation / Qwen LLM API
- **NotebookLM Sync:** `notebooklm-py` CLI (Python, managed venv)
- **DI Container:** Microsoft.Extensions.DependencyInjection
- **Target OS:** Windows 10/11

## 2. Architecture and Design Approach
The implementation follows an **MVVM + service-layer** design with event-driven coordination.

- **UI Layer:** `MainWindow` + `MainViewModel`, `SettingsWindow` + `SettingsViewModel`, `LogWindow` + `LogViewModel`, and `TranscriptionOverlayWindow` + `TranscriptionOverlayViewModel` expose status and commands.
- **Coordination Layer:** `SessionCoordinator` manages app session state transitions; `TranscriptionCoordinator` owns the transcription lifecycle, mention detection, and transcript context.
- **Infrastructure Layer:** Audio session monitoring, recording, transcription, AI insight analysis, filesystem, cloud sync, and platform services are injected as interfaces.
- **Composition Root:** `App.xaml.cs` wires all dependencies as singletons/transients, enforces single-instance via a named `Mutex`, applies theme and startup settings, and initialises tray behaviour.
- **Autostart support:** When launched with `--autostart` (e.g. from the Windows startup registry key), the main window is suppressed and the app runs silently from the tray.

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
   - Subscribes to detector events, applies file-naming (sanitizes window title into filename), and triggers cloud sync.
   - **Recording discard logic:** after stopping, recordings with < 10 s active duration or file size below `AppSettings.MinFileSizeMb` are automatically deleted (audio + transcript).
   - Emits:
     - `RecordingRequested(RecordingRequestedEventArgs)`
     - `RecordingStopped`
     - `StateChanged(SessionStateChangedEventArgs)`
   - Applies debounce and supports manual stop behaviour while continuing monitoring.
   - Optionally accepts an `ICloudSyncService` for NotebookLM uploads (injected as `NotebookLmSyncService`).

3. **WasapiRecorder (`IAudioRecorder`)**
   - Captures system audio via `WasapiLoopbackCapture`.
   - Captures microphone via `WasapiCapture` using the device selected in `AppSettings.MicrophoneDeviceId` (falls back to the system default when empty).
   - Resamples streams to a common format and mixes in real time.
   - Writes output as MP3 (`LameMP3FileWriter`) or WAV (`WaveFileWriter`).

4. **AudioDeviceService (`IAudioDeviceService`)**
   - Enumerates active capture (microphone) endpoints via `MMDeviceEnumerator`.
   - Implements `IMMNotificationClient` to raise `DevicesChanged` and `DefaultDeviceChanged` events when the OS audio device topology changes (device added/removed/renamed, default changed).
   - Exposes `GetAvailableMicrophones()` returning a list of `AudioDeviceItem` (name + device ID), always prepending the localised "System Default" entry.

5. **GlobalHotkeyService (`IGlobalHotkeyService`)**
   - Registers and unregisters system-wide hotkeys via `RegisterHotKey` / `UnregisterHotKey` Win32 APIs.
   - Raises `HotkeyPressed(int hotkeyId)` when a registered hotkey fires.
   - Tracks all registered IDs and automatically unregisters them on `Dispose`.

6. **ScreenCaptureService (`IScreenCaptureService`)**
   - Captures the primary screen as a PNG byte array using `System.Drawing` + `System.Windows.Forms.Screen`.

7. **DashScopeTranscriptionService (`ITranscriptionService`)**
   - Subscribes to audio data from `IAudioRecorder` via `AudioDataAvailable`.
   - Downsamples stereo 44.1 kHz float samples to mono 16 kHz 16-bit PCM in 40 ms frames (1280 bytes) using pooled buffers (`ArrayPool<byte>`) and lock-free staging.
   - Manages duplex WebSocket connection to DashScope (`wss://dashscope.aliyuncs.com/api-ws/v1/inference`) with resilient channel buffering (250 frames / 10 s headroom).
   - Streams audio frames and performs zero-allocation UTF-8 parsing of `result-generated` events directly from memory buffers.
   - Fires `PartialSegmentTranscribed` for instantaneous interim results and `SegmentTranscribed` when sentence endpoints are detected.

8. **TranscriptionCoordinator**
   - Coordinates the real-time transcription session lifecycle: starts/stops `ITranscriptionService`, feeds audio, and clears/saves the transcript.
   - Maintains a rolling buffer of up to 200 finalized `TranscriptionSegment` objects.
   - On each finalized segment, checks for configured mention names/aliases via `IsUserMentioned` (supports `Name|Alias` / `Name/Alias` syntax) and enforces a 5-second debounce before invoking `IInsightService.AnalyzeAsync`.
   - Provides `BuildRecentTranscriptContext(segmentEndTime, contextSeconds)` for the insight prompt.
   - Extracted from `MainViewModel` to keep the ViewModel lean and improve testability.

9. **QwenInsightService (`IInsightService`)**
   - Provides contextual meeting intelligence powered by Qwen LLMs (`qwen-turbo`, `qwen-plus`, `qwen-max`).
   - Supports native DashScope text generation and OpenAI-compatible `/chat/completions` proxy endpoints.
   - Given a detected mention and rolling transcript context window, generates a concise 1–2 sentence actionable insight in the spoken language.
   - Emits `InsightGenerated` with `InsightEventArgs` (containing insight text and mention snippet).
   - Provides `TestConnectionAsync` for credential and connectivity testing.

10. **MainViewModel**
    - Bridges coordinator, recorder, transcription, and AI insight services.
    - Starts monitoring on app startup (outside design mode).
    - Tracks cloud sync status, Google Drive organise progress, and mic-warning transient messages.
    - Exposes commands: start/stop monitoring, stop recording, open folder/settings/log, exit, toggle AI features.

11. **TranscriptionOverlayWindow & ViewModel**
    - A floating, transparent, always-on-top window draggable by its title bar.
    - Displays live scrolling subtitles with instantaneous word-by-word streaming updates (`CurrentLiveText`) and finalized sentence segments.
    - Features an AI Insight card ("💡 You were mentioned") displaying actionable summaries with a dismiss button.

12. **AppLogger / LogViewModel / LogWindow**
    - `AppLogger`: Thread-safe singleton that maintains a rolling in-memory buffer of up to 100 `LogEntry` objects (Info / Success / Warning / Error), marshalled to the UI thread automatically.
    - `LogViewModel`: Wraps `AppLogger.Entries` for data binding.
    - `LogWindow`: Standalone, auto-scrolling activity log window with a **Clear** button; accessible from the main window.

13. **Tray and App Host (`App.xaml.cs`)**
    - Enforces single-instance launch via a named `Mutex`.
    - Configures culture, DI services (including `HttpClient`, `IInsightService`, `ITranscriptionService`, `IGlobalHotkeyService`, `IAudioDeviceService`, `IScreenCaptureService`, `IPythonEnvSetupService`), and theme.
    - Initializes `H.NotifyIcon.TaskbarIcon` as the tray entry point.
    - Suppresses the main window when launched with `--autostart`.

14. **Dynamic UI Localizer**
    - Manages localized text strings (`Resources.resx` and `Resources.zh-CN.resx`) for UI controls.
    - Binds UI headers, buttons, and status labels to dynamic properties in `MainViewModel` that raise `PropertyChanged` events when the UI culture changes, enabling instant runtime translation updates without application restarts.

15. **GoogleDriveSyncService (`ICloudSyncService`, `IGoogleDriveAccountService`)**
    - Implements a thread-safe, non-blocking background queue using `System.Threading.Channels.Channel<string>`.
    - Authenticates silently to Google Drive using `GoogleWebAuthorizationBroker` with access tokens securely encrypted locally using Windows DPAPI (`DpapiFileDataStore`) under the local application data directory (`%LocalAppData%\MeetingRecorder\token.json`).
    - Exposes `IGoogleDriveAccountService` for account status queries, interactive login, and credential reset.
    - Supports manual user authentication ("Sign in" button in the settings window) allowing immediate login testing with default (built-in) credentials.
    - Automatically finds or creates a target folder named `"Meeting_Auto_Sync"` (or the path configured in `GoogleDriveFolderPath`) and uploads files asynchronously.
    - Caches the resolved Drive folder ID in `AppSettings.GoogleDriveFolderId` to avoid re-querying on every restart.

16. **NotebookLmSyncService (`ICloudSyncService`)**
    - Uploads audio recordings and transcript files to Google NotebookLM via the `notebooklm-py` CLI tool (`notebooklm source add`).
    - Uses a `System.Threading.Channels.Channel<string>` background queue (same pattern as `GoogleDriveSyncService`).
    - Resolves or creates a target notebook using a date-based pattern (e.g. `"Meetings {Year}-{Month}"`).
    - Authentication state is tracked via `~/.notebooklm/profiles/default/storage_state.json`.
    - Provides static helpers: `RunLoginAsync`, `RunLogoutAsync`, `IsNotebookLmAuthenticated`, `FindNotebookLmExecutable`, `ResolveNotebookName`.

17. **PythonEnvSetupService (`IPythonEnvSetupService`)**
    - Creates an isolated Python virtual environment under `%LocalAppData%\MeetingRecorder\python_env` using the host Python runtime detected on `PATH`.
    - Installs and upgrades `notebooklm-py[browser]` and Playwright's Chromium browser component inside the venv.
    - Runs the setup in the background, reporting progress via `SetupProgressChanged` events; supports cancellation.
    - Exposes `IsVenvReady()` to check whether the `notebooklm` executable is installed.

18. **FileIOService (`IFileIOService`)**
    - Thin abstraction for file-system operations (`EnsureDirectory`, `AppendAllTextAsync`, `DeleteFile`, `GetFileSize`).
    - Injected into `SessionCoordinator` and `TranscriptionCoordinator` to keep them testable.

19. **SystemDateTimeProvider (`IDateTimeProvider`)**
    - Wraps `DateTime.Now` so session timing logic in `SessionCoordinator` and `TranscriptionCoordinator` can be tested with fake timestamps.

20. **SettingsViewModel**
    - Bridges settings configuration state with `SettingsWindow`.
    - Provides controls for directory browsing, microphone device selection, theme selection, token clearing, Google Drive OAuth, Python environment setup, NotebookLM login/logout, and the **AI & Transcription** tab (DashScope API key, Base URL, Language Hint, Connection Testing, Mention Names, Qwen Model, Context Window).
    - Saves settings on request and handles language transitions at runtime.

21. **DashScopePhraseService (`IDashScopePhraseService`)**
    - Interacts with DashScope ASR Customization / Phrase API (`POST /api/v1/services/audio/asr/phrase`).
    - Compiles custom hotword dictionaries with integer weights ([1..5]) to create a `Vocabulary ID` (`phrase_id`).
    - Parses both user-specified hotwords and configured mention names into phrase weight dictionaries.

22. **GitHubUpdateService (`IUpdateService`)**
    - Checks for updates from GitHub releases, compares versions, and downloads/extracts updates.
    - Spawns a background self-replacing batch script to perform file copying and app restart upon update completion.

## 4. State and Event Flow
1. App startup enforces single-instance mutex, configures DI, applies culture/theme/startup-key, and creates `MainViewModel`.
2. `MainViewModel` starts `SessionCoordinator`, moving state to `Detecting`.
3. `AudioSessionDetector` finds active whitelisted meeting audio and raises `MeetingStarted`.
4. `SessionCoordinator` raises `RecordingRequested` (with the sanitized window-title filename) and transitions to `Recording`.
5. `MainViewModel` starts `IAudioRecorder` with generated output path/format, delegates to `TranscriptionCoordinator` to start `DashScopeTranscriptionService` (passing `VocabularyId` for boosted hotwords recognition), and opens `TranscriptionOverlayWindow`.
6. Live audio is recorded and simultaneously streamed to DashScope for transcription.
7. `TranscriptionCoordinator` checks each finalized segment for mention names/aliases. On a match, it extracts recent transcript context and invokes `QwenInsightService.AnalyzeAsync`; the resulting insight is surfaced in the overlay.
8. On meeting inactivity beyond debounce, `AudioSessionDetector` triggers `MeetingEnded`.
9. `SessionCoordinator` transitions to `Saving`, raises `RecordingStopped` (flushing recorder files), evaluates discard conditions (duration < 10 s or file too small), and — if kept — enqueues files for Google Drive and/or NotebookLM sync, then returns to `Detecting` (or `Idle` if monitoring stopped).

## 5. Configuration Model
`AppSettings` controls:
- `WhitelistedProcesses` (default: wemeetapp, Zoom, ms-teams, ms-teams_modulehost, Teams, Feishu, DingTalk, Webex)
- `OutputDirectory` (default under `Documents\MeetingRecordings`)
- `DebounceSeconds` (default: 20)
- `MinFileSizeMb` (default: 1.0 MB — recordings below this threshold are discarded)
- `OutputFormat` (`Mp3` or `Wav`)
- `MicrophoneDeviceId` (specific capture device ID; empty string means System Default)
- `UiLanguage` (UI translation language)
- `Theme` (`"Light"`, `"Dark"`, or `"System"`)
- `GoogleDriveEnabled` (enable/disable sync; defaults to false)
- `GoogleClientId` & `GoogleClientSecret` (optional custom API keys)
- `GoogleDriveFolderPath` (remote upload directory, default: `"Meeting_Auto_Sync"`)
- `GoogleDriveFolderId` (cached Drive folder ID to avoid re-querying on restart)
- `StartWithWindows` (enable/disable auto-start with Windows via HKCU Registry Run key)
- `AutoCheckUpdates` (enable/disable automatic checking for updates on startup)
- `SkippedVersion` (version string the user decided to skip prompting)
- `TranscriptionEnabled` (enable/disable real-time transcription)
- `DashScopeApiKey` (Alibaba Cloud DashScope API Key; `[JsonIgnore]` — stored encrypted)
- `EncryptedDashScopeApiKey` (Windows DPAPI-encrypted ciphertext of `DashScopeApiKey` in Base64)
- `DashScopeBaseUrl` (DashScope or OpenAI-compatible proxy endpoint)
- `TranscriptionLanguage` (audio language hint, default: `"auto"`)
- `ShowTranscriptionOverlay` (enable/disable the live subtitle and insight window)
- `VocabularyId` (custom hotwords / vocabulary ID for DashScope ASR)
- `Hotwords` (custom hotwords text with weights, e.g. `"张伟:5, Alex:5"`)
- `InsightsEnabled` (enable/disable AI contextual mention insights)
- `MentionNames` (list of user/team names and aliases that trigger AI insights, supporting `"Name|Alias"` syntax)
- `InsightContextSeconds` (duration of prior transcript context window in seconds, default: 30)
- `QwenModel` (Qwen LLM model name, default: `"qwen-turbo"`)
- `NotebookLmEnabled` (enable/disable NotebookLM sync)
- `NotebookLmNotebookPattern` (notebook name pattern with `{Year}`, `{Month}`, `{Day}` tokens; default: `"Meetings {Year}-{Month}"`)
- `NotebookLmCliPath` (optional override path to the `notebooklm` CLI executable)

### Persistence
Settings are automatically saved as JSON in the local application data directory (`%LocalAppData%\MeetingRecorder\settings.json`) whenever they are updated from the UI or Settings Window. On application startup, settings are loaded from this file or default settings are created if it does not exist.

The `DashScopeApiKey` is **never written in plaintext**. On save it is encrypted with Windows DPAPI and stored as `EncryptedDashScopeApiKey`. On load, it is transparently decrypted back into the in-memory `DashScopeApiKey` field. Legacy plaintext keys are migrated automatically on first load.

### Google Drive Path Resolution
Folder existence checks are case-insensitive. If a user specifies a target folder path like `work/meetings` but the folders exist on Google Drive as `Work/Meetings`, the upload service resolves the path to the correct existing folders using their actual casing, and the local settings are automatically updated to match the correct casing found on Google Drive.

## 6. Current Project Structure (Implemented)

    MeetingRecorder/
    ├── Models/
    │   ├── AppSettings.cs
    │   ├── AudioDeviceItem.cs
    │   ├── TranscriptionSegment.cs
    │   └── MeetingDetectedEventArgs.cs
    ├── Services/
    │   ├── AppLogger.cs
    │   ├── AudioDataEventArgs.cs
    │   ├── AudioDeviceService.cs
    │   ├── AudioSessionDetector.cs
    │   ├── DashScopePhraseService.cs
    │   ├── DashScopeTranscriptionService.cs
    │   ├── DpapiFileDataStore.cs
    │   ├── FileIOService.cs
    │   ├── GitHubUpdateService.cs
    │   ├── GlobalHotkeyService.cs
    │   ├── GoogleDriveSyncService.cs
    │   ├── IAudioDeviceService.cs
    │   ├── IAudioRecorder.cs
    │   ├── IAudioSessionMonitor.cs
    │   ├── ICloudSyncService.cs
    │   ├── IDateTimeProvider.cs
    │   ├── IFileIOService.cs
    │   ├── IGlobalHotkeyService.cs
    │   ├── IGoogleDriveAccountService.cs
    │   ├── IInsightService.cs
    │   ├── IPythonEnvSetupService.cs
    │   ├── IScreenCaptureService.cs
    │   ├── ITranscriptionService.cs
    │   ├── IUpdateService.cs
    │   ├── InsightEventArgs.cs
    │   ├── NotebookLmSyncService.cs
    │   ├── PythonEnvSetupService.cs
    │   ├── QwenInsightService.cs
    │   ├── RecordingRequestedEventArgs.cs
    │   ├── ScreenCaptureService.cs
    │   ├── SessionCoordinator.cs
    │   ├── SessionState.cs
    │   ├── SessionStateChangedEventArgs.cs
    │   ├── SystemDateTimeProvider.cs
    │   ├── TranscriptionCoordinator.cs
    │   ├── TranscriptionSegmentEventArgs.cs
    │   ├── UpdateInfo.cs
    │   └── WasapiRecorder.cs
    ├── ViewModels/
    │   ├── LogViewModel.cs
    │   ├── MainViewModel.cs
    │   ├── SettingsViewModel.cs
    │   ├── TranscriptionOverlayViewModel.cs
    │   └── UpdateViewModel.cs
    ├── LogWindow.xaml
    ├── MainWindow.xaml
    ├── SettingsWindow.xaml
    ├── UpdateWindow.xaml
    ├── TranscriptionOverlayWindow.xaml
    ├── App.xaml.cs
    └── credentials.json (Embedded Resource)

## 7. Engineering Constraints and Goals
- Keep monitoring overhead low (polling loop + debounce).
- Ensure deterministic cleanup for capture/writer resources on stop/exit.
- Preserve reliable unattended tray-first operation (including `--autostart` mode).
- Keep boundaries testable through service abstractions and `IDateTimeProvider` / `IFileIOService` injection (`MeetingRecorder.Tests` covers coordinator, note writer, and service behaviour).

## 8. Specification Maintenance Guideline (CRITICAL)
- **Developer and AI Agent Responsibility:** Every developer and AI agent working on this codebase must update this technical specification (`Architecture_Spec.md`) whenever architectural changes, configuration options, process whitelists, UI behaviours, or component responsibilities are modified. This maintains the integrity and correctness of the design documentation as a single source of truth.
