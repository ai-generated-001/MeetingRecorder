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

## License

This project is open-source and available under the MIT License.
