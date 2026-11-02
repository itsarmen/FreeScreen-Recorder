using System.Diagnostics;
using System.Text;
using FreeScreenRecorder.Models;

namespace FreeScreenRecorder.Services;

public sealed class FfmpegRecorder : IDisposable
{
    private readonly object _gate = new();
    private readonly StringBuilder _log = new();

    private Process? _process;
    private AudioCapturePipeline? _audio;
    private bool _stopping;
    private bool _disposed;
    private string? _outputPath;

    public event EventHandler<string>? UnexpectedExit;

    public bool IsRecording { get; private set; }

    public string? OutputPath => _outputPath;

    public EncoderKind ResolvedEncoder { get; private set; }

    public string ResolvedEncoderName { get; private set; } = string.Empty;

    public string LastLog
    {
        get
        {
            lock (_gate)
            {
                return _log.ToString();
            }
        }
    }

    public async Task StartAsync(RecordingOptions options)
    {
        if (IsRecording)
        {
            throw new InvalidOperationException("A recording is already in progress.");
        }

        if (!File.Exists(options.FfmpegPath))
        {
            throw new FileNotFoundException("ffmpeg.exe was not found.", options.FfmpegPath);
        }

        var folder = Path.GetDirectoryName(options.OutputPath);
        if (string.IsNullOrWhiteSpace(folder))
        {
            throw new InvalidOperationException("The output folder is invalid.");
        }

        Directory.CreateDirectory(folder);

        ResolvedEncoder = await FfmpegService.ResolveEncoderAsync(
            options.FfmpegPath,
            options.Encoder,
            options.Codec);
        ResolvedEncoderName = FfmpegService.GetEncoderName(ResolvedEncoder, options.Codec);

        var arguments = BuildArguments(options, ResolvedEncoder);
        var info = new ProcessStartInfo(options.FfmpegPath)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        lock (_gate)
        {
            _log.Clear();
        }

        _stopping = false;
        _outputPath = options.OutputPath;

        var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        process.ErrorDataReceived += OnErrorDataReceived;
        process.Exited += OnProcessExited;

        _process = process;

        var startupClock = Stopwatch.StartNew();
        process.Start();
        process.BeginErrorReadLine();

        var startupDelay = startupClock.Elapsed;
        _audio = new AudioCapturePipeline(
            options.Microphone?.Id,
            options.CaptureMicrophone,
            options.CaptureSystemAudio,
            process.StandardInput.BaseStream,
            startupDelay);

        try
        {
            _audio.Start();
        }
        catch
        {
            process.Kill(true);
            process.Dispose();
            _process = null;
            _audio.Dispose();
            _audio = null;
            throw;
        }

        IsRecording = true;
    }

    public async Task<string?> StopAsync()
    {
        if (!IsRecording && _process is null)
        {
            return _outputPath;
        }

        _stopping = true;
        IsRecording = false;

        var process = _process;

        if (process is not null)
        {
            try
            {
                // Signal EOF immediately so ffmpeg can finalize while the
                // WASAPI capture threads are shutting down.
                process.StandardInput.Close();
            }
            catch
            {
                // ffmpeg may already have closed stdin.
            }
        }

        try
        {
            _audio?.Stop();
        }
        catch
        {
            // Ignore shutdown races.
        }

        try
        {
            _audio?.Dispose();
        }
        catch
        {
            // Ignore shutdown races.
        }

        _audio = null;

        if (process is not null)
        {
            try
            {
                if (!process.HasExited)
                {
                    var exitTask = process.WaitForExitAsync();
                    var completed = await Task.WhenAny(exitTask, Task.Delay(TimeSpan.FromSeconds(15)));
                    if (completed != exitTask)
                    {
                        process.Kill(true);
                        await process.WaitForExitAsync();
                    }
                }
            }
            catch
            {
                try
                {
                    process.Kill(true);
                }
                catch
                {
                    // Ignore.
                }
            }
            finally
            {
                process.ErrorDataReceived -= OnErrorDataReceived;
                process.Exited -= OnProcessExited;
                process.Dispose();
                _process = null;
            }
        }

        return _outputPath;
    }

    private void OnErrorDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Data))
        {
            return;
        }

        lock (_gate)
        {
            _log.AppendLine(e.Data);
            if (_log.Length > 16000)
            {
                _log.Remove(0, _log.Length - 12000);
            }
        }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (_stopping)
        {
            return;
        }

        IsRecording = false;
        UnexpectedExit?.Invoke(this, LastLog);
    }

    private static List<string> BuildArguments(RecordingOptions options, EncoderKind encoder)
    {
        var args = new List<string>
        {
            "-hide_banner",
            "-loglevel", "warning",
            "-stats",
            "-y",
            "-f", "lavfi",
            "-i", $"ddagrab=output_idx={options.Monitor.DxgiOutputIndex}:framerate={options.Fps}:draw_mouse=1",
            "-f", "s16le",
            "-ar", "48000",
            "-ac", "2",
            "-thread_queue_size", "1024",
            "-i", "pipe:0",
            "-map", "0:v:0",
            "-map", "1:a:0"
        };

        var crf = options.Codec == VideoCodecKind.H264
            ? options.Quality switch { QualityPreset.High => 20, QualityPreset.Balanced => 23, _ => 28 }
            : options.Quality switch { QualityPreset.High => 22, QualityPreset.Balanced => 26, _ => 30 };

        var hardwareQuality = options.Quality switch
        {
            QualityPreset.High => 19,
            QualityPreset.Balanced => 23,
            _ => 28
        };

        var encoderName = FfmpegService.GetEncoderName(encoder, options.Codec);

        switch (encoder)
        {
            case EncoderKind.IntelQuickSync:
                args.AddRange(new[]
                {
                    "-vf", "hwmap=derive_device=qsv,format=qsv",
                    "-c:v", encoderName,
                    "-preset", "veryfast",
                    "-global_quality", hardwareQuality.ToString()
                });
                break;

            case EncoderKind.NvidiaNvenc:
                args.AddRange(new[]
                {
                    "-c:v", encoderName,
                    "-preset", "p5",
                    "-tune", "hq",
                    "-rc", "vbr",
                    "-cq", hardwareQuality.ToString(),
                    "-b:v", "0"
                });
                break;

            case EncoderKind.AmdAmf:
                args.AddRange(new[]
                {
                    "-c:v", encoderName,
                    "-quality", "balanced",
                    "-rc", "cqp",
                    "-qp_i", hardwareQuality.ToString(),
                    "-qp_p", (hardwareQuality + 2).ToString()
                });
                break;

            default:
                if (options.Codec == VideoCodecKind.H264)
                {
                    args.AddRange(new[]
                    {
                        "-c:v", "libx264",
                        "-preset", "veryfast",
                        "-crf", crf.ToString(),
                        "-pix_fmt", "yuv420p",
                        "-profile:v", "high"
                    });
                }
                else
                {
                    args.AddRange(new[]
                    {
                        "-c:v", "libx265",
                        "-preset", "fast",
                        "-crf", crf.ToString(),
                        "-pix_fmt", "yuv420p",
                        "-tag:v", "hvc1"
                    });
                }
                break;
        }

        if (options.Codec == VideoCodecKind.Hevc && encoder != EncoderKind.Cpu)
        {
            args.AddRange(new[] { "-tag:v", "hvc1" });
        }

        args.AddRange(new[]
        {
            "-fps_mode", "cfr",
            "-r", options.Fps.ToString(),
            "-c:a", "aac",
            "-b:a", $"{options.AudioBitrateKbps}k",
            "-ar", "48000",
            "-ac", "2",
            "-movflags", "+faststart",
            "-shortest",
            options.OutputPath
        });

        return args;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            StopAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Ignore.
        }
    }
}
