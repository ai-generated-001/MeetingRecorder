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
- **DI Container:** Microsoft.Extensions.DependencyInjection
- **Target OS:** Windows 10/11

## 2. Architecture and Design Approach
The implementation follows an **MVVM + service-layer** design with event-driven coordination.

- **UI Layer:** `MainWindow` + `MainViewModel` and `SettingsWindow` + `SettingsViewModel` expose status and commands.
- **Coordination Layer:** `SessionCoordinator` manages app session state transitions.
- **Infrastructure Layer:** Audio session monitoring, recording, transcription, AI insight analysis, filesystem, and platform services are injected as interfaces.
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

4. **DashScopeTranscriptionService (`ITranscriptionService`)**
   - Subscribes to audio data from `IAudioRecorder` via `AudioDataAvailable`.
   - Downsamples stereo 44.1kHz float samples to mono 16kHz 16-bit PCM in 40ms frames (1280 bytes) using pooled buffers (`ArrayPool<byte>`) and lock-free staging.
   - Manages duplex WebSocket connection to DashScope (`wss://dashscope.aliyuncs.com/api-ws/v1/inference`) with resilient channel buffering (250 frames / 10s headroom).
   - Streams audio frames and performs zero-allocation UTF-8 parsing of `result-generated` events directly from memory buffers.
   - Fires `PartialSegmentTranscribed` for instantaneous interim results and `SegmentTranscribed` when sentence endpoints are detected.

5. **QwenInsightService (`IInsightService`)**
   - Provides contextual meeting intelligence powered by Qwen LLMs (`qwen-turbo`, `qwen-plus`, `qwen-max`).
   - Supports native DashScope text generation and OpenAI-compatible `/chat/completions` proxy endpoints.
   - Given a detected mention and rolling transcript context window, generates a concise 1–2 sentence actionable insight in the spoken language.
   - Emits `InsightGenerated` with `InsightEventArgs` (containing insight text and mention snippet).
   - Provides `TestConnectionAsync` for credential and connectivity testing.

6. **MainViewModel**
   - Bridges coordinator, recorder, transcription, and AI insight services.
   - Starts monitoring on app startup (outside design mode).
   - Maintains a rolling transcript context window and checks each transcribed segment for user mentions (`AppSettings.MentionNames`).
   - Debounces rapid consecutive mentions and triggers `IInsightService.AnalyzeAsync`.
   - On meeting stop, saves full timestamped transcript (`.txt`) alongside the audio file.
   - Exposes commands: start/stop monitoring, stop recording, open folder/settings, exit.

7. **TranscriptionOverlayWindow & ViewModel**
   - A floating, transparent, always-on-top window draggable by its title bar.
   - Displays live scrolling subtitles with instantaneous word-by-word streaming updates (`CurrentLiveText`) and finalized sentence segments.
   - Features an AI Insight card ("💡 You were mentioned") displaying actionable summaries with a dismiss button.

8. **Tray and App Host (`App.xaml.cs`)**
   - Configures culture, DI services (including `HttpClient`, `IInsightService`, `ITranscriptionService`), and theme.
   - Initializes `H.NotifyIcon.TaskbarIcon` as the tray entry point.
   - Shows and positions the floating main window near the bottom-right work area.

9. **Dynamic UI Localizer**
   - Manages localized text strings (`Resources.resx` and `Resources.zh-CN.resx`) for UI controls.
   - Binds UI headers, buttons, and status labels to dynamic properties in `MainViewModel` that raise `PropertyChanged` events when the UI culture changes, enabling instant runtime translation updates without application restarts.

10. **GoogleDriveSyncService (`ICloudSyncService`)**
    - Implements a thread-safe, non-blocking background queue using `System.Threading.Channels.Channel<string>`.
    - Authenticates silently to Google Drive using `GoogleWebAuthorizationBroker` with access tokens securely encrypted locally using Windows DPAPI (`DpapiFileDataStore`) under the local application data directory (`%LocalAppData%\MeetingRecorder\token.json`).
    - Supports manual user authentication ("Sign in" button in the settings window) allowing immediate login testing with default (built-in) credentials.
    - Automatically finds or creates a target folder named `"Meeting_Auto_Sync"` and uploads files asynchronously.

11. **SettingsViewModel**
    - Bridges settings configuration state with `SettingsWindow`.
    - Provides controls for directory browsing, token clearing, Google Drive OAuth, and the **AI & Transcription** tab (DashScope API key, Base URL, Language Hint, Connection Testing, Mention Names, Qwen Model, Context Window).
    - Saves settings on request and handles language transitions at runtime.

12. **DashScopePhraseService (`IDashScopePhraseService`)**
    - Interacts with DashScope ASR Customization / Phrase API (`POST /api/v1/services/audio/asr/phrase`).
    - Compiles custom hotword dictionaries with integer weights ([1..5]) to create a `Vocabulary ID` (`phrase_id`).
    - Parses both user-specified hotwords and configured mention names into phrase weight dictionaries.

13. **GitHubUpdateService (`IUpdateService`)**
    - Checks for updates from GitHub releases, compares versions, and downloads/extracts updates.
    - Spawns a background self-replacing batch script to perform file copying and app restart upon update completion.

## 4. State and Event Flow
1. App startup configures DI and creates `MainViewModel`.
2. `MainViewModel` starts `SessionCoordinator`, moving state to `Detecting`.
3. `AudioSessionDetector` finds active whitelisted meeting audio and raises `MeetingStarted`.
4. `SessionCoordinator` raises `RecordingRequested` and transitions to `Recording`.
5. `MainViewModel` starts `IAudioRecorder` with generated output path/format, connects to `DashScopeTranscriptionService` (passing `VocabularyId` for boosted hotwords recognition), and opens `TranscriptionOverlayWindow`.
6. Live audio is recorded and simultaneously streamed to DashScope for transcription.
7. If a mention name or alias is detected in the speech, `MainViewModel` extracts recent transcript context and invokes `QwenInsightService` to display an insight card in the overlay.
8. On meeting inactivity beyond debounce, detector triggers `MeetingEnded`.
9. `SessionCoordinator` transitions to `Saving`, raises `RecordingStopped` (flushing recorder files), saves the full transcript `.txt`, enqueues files in the background sync service, and returns to `Detecting` (or `Idle` if monitoring stopped).

## 5. Configuration Model
`AppSettings` controls:
- `WhitelistedProcesses` (default: wemeetapp, Zoom, ms-teams, ms-teams_modulehost, Teams, Feishu, DingTalk, Webex)
- `OutputDirectory` (default under Documents\MeetingRecordings)
- `DebounceSeconds` (default: 5)
- `OutputFormat` (`Mp3` or `Wav`)
- `UiLanguage` (UI translation language)
- `GoogleDriveEnabled` (enable/disable sync)
- `GoogleClientId` & `GoogleClientSecret` (optional custom API keys)
- `GoogleDriveFolderPath` (remote upload directory)
- `StartWithWindows` (enable/disable auto-start with Windows via HKCU Registry Run key)
- `AutoCheckUpdates` (enable/disable automatic checking for updates on startup)
- `SkippedVersion` (version string the user decided to skip prompting)
- `TranscriptionEnabled` (enable/disable real-time transcription)
- `DashScopeApiKey` (Alibaba Cloud DashScope API Key)
- `DashScopeBaseUrl` (DashScope or OpenAI-compatible proxy endpoint)
- `TranscriptionLanguage` (audio language hint, default: "auto")
- `ShowTranscriptionOverlay` (enable/disable the live subtitle and insight window)
- `VocabularyId` (custom hotwords / vocabulary ID for DashScope ASR)
- `Hotwords` (custom hotwords text with weights, e.g. "张伟:5, Alex:5")
- `InsightsEnabled` (enable/disable AI contextual mention insights)
- `MentionNames` (list of user/team names and aliases that trigger AI insights, supporting "Name|Alias" syntax)
- `InsightContextSeconds` (duration of prior transcript context window in seconds, default: 30)
- `QwenModel` (Qwen LLM model name, default: "qwen-turbo")

### Persistence
Settings are automatically saved as JSON in the local application data directory (`%LocalAppData%\MeetingRecorder\settings.json`) whenever they are updated from the UI or Settings Window. On application startup, settings are loaded from this file or default settings are created if it does not exist.

### Google Drive Path Resolution
Folder existence checks are case-insensitive. If a user specifies a target folder path like `work/meetings` but the folders exist on Google Drive as `Work/Meetings`, the upload service resolves the path to the correct existing folders using their actual casing, and the local settings are automatically updated to match the correct casing found on Google Drive.

## 6. Current Project Structure (Implemented)

    MeetingRecorder/
    ├── Models/
    │   ├── AppSettings.cs
    │   ├── TranscriptionSegment.cs
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
    │   ├── RecordingRequestedEventArgs.cs
    │   ├── AudioDataEventArgs.cs
    │   ├── ITranscriptionService.cs
    │   ├── DashScopeTranscriptionService.cs
    │   ├── TranscriptionSegmentEventArgs.cs
    │   ├── IDashScopePhraseService.cs
    │   ├── DashScopePhraseService.cs
    │   ├── IInsightService.cs
    │   ├── QwenInsightService.cs
    │   ├── InsightEventArgs.cs
    │   ├── IUpdateService.cs
    │   ├── UpdateInfo.cs
    │   └── GitHubUpdateService.cs
    ├── ViewModels/
    │   ├── MainViewModel.cs
    │   ├── SettingsViewModel.cs
    │   ├── TranscriptionOverlayViewModel.cs
    │   └── UpdateViewModel.cs
    ├── MainWindow.xaml
    ├── SettingsWindow.xaml
    ├── UpdateWindow.xaml
    ├── TranscriptionOverlayWindow.xaml
    ├── App.xaml.cs
    └── credentials.json (Embedded Resource)

## 7. Engineering Constraints and Goals
- Keep monitoring overhead low (polling loop + debounce).
- Ensure deterministic cleanup for capture/writer resources on stop/exit.
- Preserve reliable unattended tray-first operation.
- Keep boundaries testable through service abstractions (`MeetingRecorder.Tests` covers coordinator and note writer behavior).

## 8. Specification Maintenance Guideline (CRITICAL)
- **Developer and AI Agent Responsibility:** Every developer and AI agent working on this codebase must update this technical specification (`Architecture_Spec.md`) whenever architectural changes, configuration options, process whitelists, UI behaviors, or component responsibilities are modified. This maintains the integrity and correctness of the design documentation as a single source of truth.
