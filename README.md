# MeetingRecorder

MeetingRecorder is a modern Windows application built with .NET 10 and WPF designed to automatically record your online meetings by detecting active audio sessions from popular meeting applications.

## Key Features

- **Automatic Detection**: Automatically starts recording when a supported meeting application (like Zoom, Microsoft Teams, Webex, etc.) begins an active audio session.
- **Dual-Channel Recording**: Captures both system audio (loopback) and your microphone, mixing them into a single high-quality stream.
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
- **UI**: WPF (Windows Presentation Foundation)
- **Audio Engine**: [NAudio](https://github.com/naudio/NAudio) for WASAPI loopback and microphone capture.
- **MP3 Encoding**: [NAudio.Lame](https://github.com/corey84/NAudio.Lame) for LAME MP3 conversion.
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

## Google Drive Synchronization & OAuth Setup

The application features automated Google Drive synchronization. At the end of every meeting, the audio recording and generated markdown notes file are uploaded in the background to a folder named `"Meeting_Auto_Sync"` in the user's Google Drive. 

For safety, the authentication tokens are encrypted locally on your machine using Windows Data Protection API (DPAPI) and saved under `token.json/`.

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
