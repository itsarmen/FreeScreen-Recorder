using System.Drawing;

namespace FreeScreenRecorder.Models;

public sealed record RecordingOptions(
    MonitorInfo Monitor,
    AudioDeviceInfo? Microphone,
    bool CaptureMicrophone,
    bool CaptureSystemAudio,
    string OutputPath,
    string FfmpegPath,
    EncoderKind Encoder,
    VideoCodecKind Codec,
    QualityPreset Quality,
    int Fps,
    int AudioBitrateKbps,
    bool CursorHighlightEnabled,
    int CursorSize,
    Color CursorColor,
    bool ClickEffectsEnabled,
    Color ClickColor);
