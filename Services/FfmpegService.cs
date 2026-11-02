using System.Diagnostics;
using System.Text;
using FreeScreenRecorder.Models;

namespace FreeScreenRecorder.Services;

public static class FfmpegService
{
    public static string? FindFfmpeg(string? preferredPath = null)
    {
        if (!string.IsNullOrWhiteSpace(preferredPath) && File.Exists(preferredPath))
        {
            return preferredPath;
        }

        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe"),
            Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg.exe")
        };

        var environmentPath = Environment.GetEnvironmentVariable("FFMPEG_PATH");
        if (!string.IsNullOrWhiteSpace(environmentPath))
        {
            candidates.Add(environmentPath);
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        candidates.AddRange(path
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(folder => Path.Combine(folder.Trim(), "ffmpeg.exe")));

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        candidates.Add(Path.Combine(localAppData, "Microsoft", "WinGet", "Links", "ffmpeg.exe"));
        candidates.Add(@"C:\ffmpeg\bin\ffmpeg.exe");

        return candidates.FirstOrDefault(File.Exists);
    }

    public static async Task<HashSet<string>> GetAvailableEncodersAsync(string ffmpegPath)
    {
        var encoders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var output = await RunCaptureAsync(ffmpegPath, "-hide_banner -encoders", TimeSpan.FromSeconds(15));

        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed[0] != 'V')
            {
                continue;
            }

            var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                encoders.Add(parts[1]);
            }
        }

        return encoders;
    }

    public static async Task<bool> TestEncoderAsync(string ffmpegPath, string encoder)
    {
        var args = $"-hide_banner -loglevel error -f lavfi -i color=c=black:s=320x180:d=0.05 " +
                   $"-frames:v 1 -c:v {encoder} -f null -";

        var result = await RunAsync(ffmpegPath, args, TimeSpan.FromSeconds(10));
        return result.ExitCode == 0;
    }

    public static string GetEncoderName(EncoderKind kind, VideoCodecKind codec) => (kind, codec) switch
    {
        (EncoderKind.IntelQuickSync, VideoCodecKind.H264) => "h264_qsv",
        (EncoderKind.IntelQuickSync, VideoCodecKind.Hevc) => "hevc_qsv",
        (EncoderKind.NvidiaNvenc, VideoCodecKind.H264) => "h264_nvenc",
        (EncoderKind.NvidiaNvenc, VideoCodecKind.Hevc) => "hevc_nvenc",
        (EncoderKind.AmdAmf, VideoCodecKind.H264) => "h264_amf",
        (EncoderKind.AmdAmf, VideoCodecKind.Hevc) => "hevc_amf",
        (_, VideoCodecKind.H264) => "libx264",
        _ => "libx265"
    };

    public static async Task<EncoderKind> ResolveEncoderAsync(
        string ffmpegPath,
        EncoderKind requested,
        VideoCodecKind codec)
    {
        if (requested != EncoderKind.Auto)
        {
            var name = GetEncoderName(requested, codec);
            if (await TestEncoderAsync(ffmpegPath, name))
            {
                return requested;
            }

            throw new InvalidOperationException(
                $"The {name} encoder is not available on this system. Choose Auto or CPU (x264/x265).");
        }

        var available = await GetAvailableEncodersAsync(ffmpegPath);
        var candidates = new[]
        {
            EncoderKind.IntelQuickSync,
            EncoderKind.NvidiaNvenc,
            EncoderKind.AmdAmf
        };

        foreach (var candidate in candidates)
        {
            var name = GetEncoderName(candidate, codec);
            if (available.Contains(name) && await TestEncoderAsync(ffmpegPath, name))
            {
                return candidate;
            }
        }

        return EncoderKind.Cpu;
    }

    public static string GetEncoderDisplayName(EncoderKind kind, VideoCodecKind codec) => (kind, codec) switch
    {
        (EncoderKind.IntelQuickSync, VideoCodecKind.H264) => "Intel Quick Sync (H.264)",
        (EncoderKind.IntelQuickSync, VideoCodecKind.Hevc) => "Intel Quick Sync (HEVC)",
        (EncoderKind.NvidiaNvenc, VideoCodecKind.H264) => "NVIDIA NVENC (H.264)",
        (EncoderKind.NvidiaNvenc, VideoCodecKind.Hevc) => "NVIDIA NVENC (HEVC)",
        (EncoderKind.AmdAmf, VideoCodecKind.H264) => "AMD AMF / VCE-VCN (H.264)",
        (EncoderKind.AmdAmf, VideoCodecKind.Hevc) => "AMD AMF / VCE-VCN (HEVC)",
        (_, VideoCodecKind.H264) => "CPU (libx264)",
        _ => "CPU (libx265)"
    };

    private static async Task<(int ExitCode, string Output)> RunAsync(
        string fileName,
        string arguments,
        TimeSpan timeout)
    {
        var info = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = info };
        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(true);
            }
            catch
            {
                // Ignore.
            }

            return (-1, "Timed out.");
        }

        var output = await outputTask + await errorTask;
        return (process.ExitCode, output);
    }

    private static async Task<string> RunCaptureAsync(string fileName, string arguments, TimeSpan timeout)
    {
        var result = await RunAsync(fileName, arguments, timeout);
        return result.Output;
    }
}
