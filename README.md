# MeetingRecorder

MeetingRecorder is a modern Windows application built with .NET 10 and WPF designed to automatically record your online meetings by detecting active audio sessions from popular meeting applications.

## Key Features

- **Automatic Detection**: Automatically starts recording when a supported meeting application (like Zoom, Microsoft Teams, Webex, etc.) begins an active audio session.
- **Dual-Channel Recording**: Captures both system audio (loopback) and your microphone, mixing them into a single high-quality stream.
- **Real-Time AI Transcription**: Streams real-time speech-to-text recognition via Alibaba Cloud DashScope (`paraformer-realtime-v2`) over WebSockets.
- **AI Contextual Mention Insights**: Powered by Qwen LLM (`qwen-turbo` / `qwen-plus` / `qwen-max`), detects when you or your team are mentioned in the meeting, analyzes the surrounding context, and surfaces real-time actionable insights in the overlay window.
- **Floating Subtitle & Insight Overlay**: A lightweight, always-on-top, draggable overlay window that shows live transcripts and mention alerts.
- **Multiple Formats**: Supports saving recordings in both **MP3** (using LAME) and high-fidelity **WAV** formats.
- **Tray Integration**: Runs quietly in the system tray with notifications for recording status.
- **Process Whitelisting**: Pre-configured to recognize common meeting software including:
  - Zoom
  - Microsoft Teams
  - WeChat (wemeetapp)
  - Feishu / Lark
  - DingTalk
  - Webex

## Technical Stack

- **Framework**: .NET 10.0 (Windows)
- **UI**: WPF (Windows Presentation Foundation) + MVVM (CommunityToolkit.Mvvm)
- **Audio Engine**: [NAudio](https://github.com/naudio/NAudio) for WASAPI loopback and microphone capture.
- **MP3 Encoding**: [NAudio.Lame](https://github.com/corey84/NAudio.Lame) for LAME MP3 conversion.
- **AI & Transcription**: Alibaba Cloud DashScope WebSocket API (Paraformer-realtime ASR) + Qwen LLM API (Text Generation).
- **Tray Icon**: [H.NotifyIcon](https://github.com/HavenDV/H.NotifyIcon) for system tray management.

## Project Structure

- **MeetingRecorder/Services**: Core logic for audio detection (`AudioSessionDetector`) and recording (`WasapiRecorder`).
- **MeetingRecorder/ViewModels**: MVVM implementation including `MainViewModel` for state management.
- **MeetingRecorder/Models**: Application configuration and settings.
- **MeetingRecorder/App.xaml**: Handles system tray icon and global application lifecycle.

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Windows 10 or 11 (required for WASAPI loopback capture)

### Installation & Run

1. Clone the repository:
   ```powershell
   git clone https://github.com/ai-generated-001/MeetingRecorder
   ```
2. Build and run the project:
   ```powershell
   dotnet run --project MeetingRecorder\MeetingRecorder.csproj
   ```

## Configuration

Recording settings and the process whitelist can be found in `MeetingRecorder/Models/AppSettings.cs`. By default, recordings are saved to your `Documents\MeetingRecordings` folder.

## AI & Real-Time Transcription Setup

MeetingRecorder provides real-time transcription and contextual AI mention insights powered by Alibaba Cloud DashScope.

### Configuration in Settings
1. Open the application and click **Settings** (or right-click the tray icon and select **Settings**).
2. Go to the **AI & Transcription** tab:
   - **Enable Real-Time Transcription**: Turn on live speech-to-text.
   - **DashScope API Key**: Enter your Alibaba Cloud DashScope API Key (e.g. `sk-...`).
   - **API Base URL (Optional)**: Defaults to `https://dashscope.aliyuncs.com`. You can specify a custom or OpenAI-compatible proxy URL if desired.
   - **Audio Language Hint**: Choose Auto Detect, Chinese, English, Japanese, Korean, French, German, or Spanish.
   - **Show Floating Subtitle Overlay**: Displays real-time subtitles and mention alerts during the meeting.
   - **ASR Hotwords & Custom Vocabulary**: Input custom hotwords with optional weights (e.g. `张伟:5, 李娜:5, Alex:5, ProjectAlpha:4`) to significantly boost ASR recognition accuracy for names and domain terminology.
   - **Compile Hotwords**: Click **Compile Hotwords** to automatically sync and compile the hotword dictionary into DashScope, generating a `Vocabulary ID` used during live transcription.
   - **Mention Names & Aliases**: Enter comma-separated names/nicknames (e.g. `Alex|Alec, 张伟|张维, 团队`) that trigger real-time AI assistance when spoken.
   - **Qwen Model**: Choose between `qwen-turbo` (fastest & cost-effective), `qwen-plus`, or `qwen-max`.
   - **Context Window**: Specify how many seconds of prior transcript context to send to the LLM (default: `30` seconds).
3. Click **Test API Connection** to verify your API credentials, then click **Save**.

### Real-Time Workflow
- When a meeting begins, speech is streamed to DashScope Paraformer in 100ms PCM chunks.
- Subtitles appear live in the floating overlay window.
- When any configured name is mentioned in the meeting, Qwen LLM analyzes the context and presents an actionable summary card (e.g., *"You were asked to review the Q3 budget slides by Friday"*) directly inside the overlay.
- On meeting conclusion, the full transcript is automatically saved as a `.txt` file alongside the audio recording.

## Google Drive Synchronization & OAuth Setup

The application features automated Google Drive synchronization. At the end of every meeting, the audio recording and generated markdown notes file are uploaded in the background to a folder named `"Meeting_Auto_Sync"` in the user's Google Drive. 

For safety, the authentication tokens are encrypted locally on your machine using Windows Data Protection API (DPAPI) and saved under `token.json/` inside the Local AppData directory (`%LocalAppData%\MeetingRecorder\token.json`).

To enable this feature, the application requires a Google Cloud project client ID and client secret. This can be configured in two ways:

1. **Build-Time Injection (GitHub Actions)**:
   Define the following repository secrets in your GitHub repository:
   - `GOOGLE_CLIENT_ID`
   - `GOOGLE_CLIENT_SECRET`
   
   The GitHub Actions workflow will automatically pass these to MSBuild during compilation via:
   ```powershell
   dotnet build -p:GoogleClientId="YOUR_CLIENT_ID" -p:GoogleClientSecret="YOUR_CLIENT_SECRET"
   ```
   
2. **User-Supplied settings ("Bring Your Own Key")**:
   Users can manually input their own Google Client ID and Client Secret directly in the application's Settings window.

### How to Create OAuth Client Credentials

Follow these steps to generate a valid OAuth client ID and client secret:

1. **Go to the Google Cloud Console**:
   Open [Google Cloud Console](https://console.cloud.google.com/).
2. **Create a New Project**:
   Click the project dropdown in the top bar, select **New Project**, name it (e.g., `MeetingRecorderSync`), and click **Create**.
3. **Enable the Google Drive API**:
   - Go to **APIs & Services > Library** via the left menu.
   - Search for `"Google Drive API"`.
   - Click **Google Drive API** and click **Enable**.
4. **Configure the OAuth Consent Screen**:
   - Go to **APIs & Services > OAuth consent screen**.
   - Select **External** (or **Internal** if using a workspace account) and click **Create**.
   - Fill in the required fields (App name: `MeetingRecorder`, User support email, Developer contact email) and click **Save and Continue**.
   - Under **Scopes**, click **Add or Remove Scopes**, search for or select `https://www.googleapis.com/auth/drive.file` (this scope restricts the app to only view and manage files/folders it creates, protecting other files in your Drive), click **Add to table**, then click **Save and Continue**.
   - Under **Test Users**, add your Google email address (the account you want to sync to) so you can authenticate during testing. Click **Save and Continue**.
   - Review the summary and click **Back to Dashboard**.
5. **Create OAuth Client Credentials**:
   - Go to **APIs & Services > Credentials**.
   - Click **Create Credentials** at the top and select **OAuth client ID**.
   - Set **Application type** to **Desktop app**.
   - Name it (e.g., `MeetingRecorder Desktop Client`).
   - Click **Create**.
6. **Obtain Client ID and Secret**:
   - A dialog will appear showing the Client ID and Client Secret. Copy these values to inject during build time, or input them directly into the application's Settings screen.

## License

This project is open-source and available under the MIT License.
