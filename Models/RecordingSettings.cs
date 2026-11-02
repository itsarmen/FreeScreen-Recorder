namespace FreeScreenRecorder.Models;

public enum EncoderKind
{
    Auto,
    IntelQuickSync,
    NvidiaNvenc,
    AmdAmf,
    Cpu
}

public enum VideoCodecKind
{
    H264,
    Hevc
}

public enum QualityPreset
{
    High,
    Balanced,
    Small
}

public sealed class RecordingSettings
{
    public string OutputFolder { get; set; } = DefaultOutputFolder();

    public string? MonitorDeviceName { get; set; }

    public string? MicrophoneDeviceId { get; set; }

    public bool MicrophoneEnabled { get; set; } = true;

    public bool SystemAudioEnabled { get; set; } = true;

    public string FfmpegPath { get; set; } = string.Empty;

    public EncoderKind Encoder { get; set; } = EncoderKind.Auto;

    public VideoCodecKind Codec { get; set; } = VideoCodecKind.H264;

    public QualityPreset Quality { get; set; } = QualityPreset.Balanced;

    public int Fps { get; set; } = 30;

    public bool CursorHighlightEnabled { get; set; } = true;

    public int CursorSize { get; set; } = 96;

    public string CursorColor { get; set; } = "#FF22D3EE";

    public bool ClickEffectsEnabled { get; set; } = true;

    public string ClickColor { get; set; } = "#FFFF5252";

    public int AudioBitrateKbps { get; set; } = 192;

    public static string DefaultOutputFolder()
    {
        var videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        if (string.IsNullOrWhiteSpace(videos))
        {
            videos = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        return Path.Combine(videos, "FreeScreenRecorder");
    }
}
