# FreeScreen Recorder

A WinUI 3 screen recorder for Windows. Record a selected monitor with system audio, a microphone, cursor highlight, and click-ring effects. An auto-hiding control bar appears when the mouse reaches the top edge and stays out of the recording.

Recording is handled by [ffmpeg](https://ffmpeg.org/) using Desktop Duplication (`ddagrab`) and hardware encoders where available.

## Features

- Record any connected monitor at its native resolution.
- H.264 or HEVC output with MP4 fast-start.
- Encoder selection: Auto, Intel Quick Sync, NVIDIA NVENC, AMD AMF, or CPU (x264/x265).
- Quality presets and 30/60 fps.
- Microphone and system-audio loopback mixed into one AAC track.
- Cursor highlight with adjustable size and colour.
- Click rings with adjustable colour.
- Auto-hiding top recording bar with timer and Stop button.
- Output folder and ffmpeg path selectors.
- Settings persisted to `%LOCALAPPDATA%\FreeScreenRecorder\settings.json`.

## Requirements

- Windows 10 2004 or newer, or Windows 11.
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
- Windows App SDK 1.8 (restored automatically by NuGet).
- ffmpeg built with `ddagrab` support (the gyan.dev full build works).
- Optional: a GPU with Quick Sync, NVENC, or AMF for hardware encoding.

`ffmpeg.exe` is detected from `FFMPEG_PATH`, `PATH`, the app folder, or the WinGet links folder. You can also browse to it in the app.

## Run

```powershell
python run.py
```

`run.py` builds the project and launches the app. Useful options:

```powershell
python run.py --no-build
python run.py --wait
python run.py -c Release
python run.py -p x86
```

Or build and launch manually:

```powershell
dotnet build FreeScreenRecorder.csproj -c Debug -p:Platform=x64
.\bin\x64\Debug\net8.0-windows10.0.26100.0\win-x64\FreeScreenRecorder.exe
```

## Usage

1. Select a display, codec, encoder, quality, and frame rate.
2. Choose a microphone and/or enable system audio.
3. Pick an output folder.
4. Press **Start recording**.
5. Move the mouse to the top of the recorded monitor to reveal the recording bar.
6. Press **Stop** on the bar or **Stop and save** in the main window.

The recording bar is excluded from capture with `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)`, so it should not appear in the video. On older Windows versions that do not support this flag, the bar may be captured while visible.

## Project layout

- `Services/FfmpegRecorder.cs` - ffmpeg process, arguments, and shutdown.
- `Services/AudioCapturePipeline.cs` - WASAPI microphone and loopback mixing.
- `Services/MonitorService.cs` - monitor enumeration and DXGI output mapping.
- `Overlay/CursorOverlay.cs` - cursor halo and click ripple rendering.
- `Overlay/RecordingWidget.cs` - auto-hiding top recording bar.
- `Overlay/LayeredOverlayWindow.cs` - click-through layered window.
- `MainPage.xaml` and `MainPage.xaml.cs` - main UI and recording workflow.

## Notes

- This project does not use Bandicam's proprietary DLLs. It uses ffmpeg and the same underlying GPU encoder drivers.
- CPU HEVC encoding is significantly slower than hardware encoding.
- Region selection, webcam overlays, and pause/resume are not implemented yet.
