using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace FreeScreenRecorder.Services;

/// <summary>
/// Captures the selected microphone and/or the default playback device (WASAPI loopback),
/// mixes them to 48 kHz stereo PCM16, and writes the result to ffmpeg's stdin.
/// </summary>
public sealed class AudioCapturePipeline : IDisposable
{
    private const int TargetSampleRate = 48000;
    private const int TargetChannels = 2;
    private const int BytesPerFrame = TargetChannels * sizeof(short);

    private readonly Stream _output;
    private readonly TimeSpan _initialSilence;
    private readonly CancellationTokenSource _cts = new();

    private readonly MMDevice? _microphoneDevice;
    private readonly MMDevice? _renderDevice;
    private readonly WasapiCapture? _microphoneCapture;
    private readonly WasapiLoopbackCapture? _systemCapture;
    private readonly BufferedWaveProvider? _microphoneBuffer;
    private readonly BufferedWaveProvider? _systemBuffer;

    private readonly MixingSampleProvider _mixer;
    private readonly SampleToWaveProvider16 _pcm16;

    private Thread? _writerThread;
    private bool _started;
    private bool _disposed;

    public AudioCapturePipeline(
        string? microphoneDeviceId,
        bool captureMicrophone,
        bool captureSystemAudio,
        Stream output,
        TimeSpan initialSilence)
    {
        _output = output;
        _initialSilence = initialSilence < TimeSpan.Zero ? TimeSpan.Zero : initialSilence;

        var sources = new List<ISampleProvider>();

        if (captureMicrophone && !string.IsNullOrWhiteSpace(microphoneDeviceId))
        {
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                _microphoneDevice = enumerator.GetDevice(microphoneDeviceId);
                _microphoneCapture = new WasapiCapture(_microphoneDevice);
                _microphoneBuffer = CreateBuffer(_microphoneCapture.WaveFormat);
                _microphoneCapture.DataAvailable += (_, e) => AddSamples(_microphoneBuffer, e);
                sources.Add(CreateSource(_microphoneBuffer));
            }
            catch
            {
                _microphoneCapture?.Dispose();
                _microphoneCapture = null;
                _microphoneDevice?.Dispose();
                _microphoneDevice = null;
                _microphoneBuffer = null;
            }
        }

        if (captureSystemAudio)
        {
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                _renderDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                _systemCapture = new WasapiLoopbackCapture(_renderDevice);
                _systemBuffer = CreateBuffer(_systemCapture.WaveFormat);
                _systemCapture.DataAvailable += (_, e) => AddSamples(_systemBuffer, e);
                sources.Add(CreateSource(_systemBuffer));
            }
            catch
            {
                _systemCapture?.Dispose();
                _systemCapture = null;
                _renderDevice?.Dispose();
                _renderDevice = null;
                _systemBuffer = null;
            }
        }

        if (sources.Count == 0)
        {
            sources.Add(new SilenceProvider(
                WaveFormat.CreateIeeeFloatWaveFormat(TargetSampleRate, TargetChannels)).ToSampleProvider());
        }

        _mixer = new MixingSampleProvider(sources)
        {
            ReadFully = true
        };

        _pcm16 = new SampleToWaveProvider16(_mixer);
    }

    public bool HasMicrophone => _microphoneCapture is not null;

    public bool HasSystemAudio => _systemCapture is not null;

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;

        _microphoneCapture?.StartRecording();
        _systemCapture?.StartRecording();

        _writerThread = new Thread(WriterLoop)
        {
            IsBackground = true,
            Name = "FreeScreenRecorder.AudioWriter",
            Priority = ThreadPriority.AboveNormal
        };
        _writerThread.Start();
    }

    public void Stop()
    {
        if (!_started)
        {
            return;
        }

        _started = false;
        _cts.Cancel();

        try
        {
            _microphoneCapture?.StopRecording();
            _systemCapture?.StopRecording();
        }
        catch
        {
            // The capture may already be stopped.
        }

        if (_writerThread is not null && !_writerThread.Join(3000))
        {
            try
            {
                _output.Dispose();
            }
            catch
            {
                // Ignore shutdown races.
            }

            _writerThread.Join(1000);
        }

        try
        {
            _output.Flush();
        }
        catch
        {
            // ffmpeg may already have exited.
        }

        try
        {
            _output.Close();
        }
        catch
        {
            // ffmpeg may already have exited.
        }
    }

    private void WriterLoop()
    {
        var buffer = new byte[TargetSampleRate / 50 * BytesPerFrame]; // 20 ms
        var totalFrames = 0L;
        var clock = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            WriteSilence(_initialSilence, clock, ref totalFrames);

            while (!_cts.IsCancellationRequested)
            {
                var bytesRead = ReadFull(_pcm16, buffer, 0, buffer.Length);
                if (bytesRead <= 0)
                {
                    break;
                }

                _output.Write(buffer, 0, bytesRead);
                _output.Flush();

                totalFrames += bytesRead / BytesPerFrame;
                Pace(clock, totalFrames);
            }
        }
        catch
        {
            // Closing ffmpeg's stdin during shutdown raises IOException/InvalidOperationException.
        }
    }

    private void WriteSilence(TimeSpan duration, System.Diagnostics.Stopwatch clock, ref long totalFrames)
    {
        var frames = (long)(duration.TotalSeconds * TargetSampleRate);
        if (frames <= 0)
        {
            return;
        }

        var chunk = new byte[TargetSampleRate / 10 * BytesPerFrame]; // 100 ms
        while (frames > 0 && !_cts.IsCancellationRequested)
        {
            var framesThisChunk = (int)Math.Min(frames, chunk.Length / BytesPerFrame);
            var bytes = framesThisChunk * BytesPerFrame;
            _output.Write(chunk, 0, bytes);
            frames -= framesThisChunk;
            totalFrames += framesThisChunk;
            Pace(clock, totalFrames);
        }

        _output.Flush();
    }

    private static void Pace(System.Diagnostics.Stopwatch clock, long totalFrames)
    {
        var target = TimeSpan.FromSeconds(totalFrames / (double)TargetSampleRate);
        var remaining = target - clock.Elapsed;
        if (remaining > TimeSpan.FromMilliseconds(2))
        {
            Thread.Sleep(remaining);
        }
    }

    private static int ReadFull(IWaveProvider provider, byte[] buffer, int offset, int count)
    {
        var total = 0;
        while (total < count)
        {
            var read = provider.Read(buffer, offset + total, count - total);
            if (read <= 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    private static BufferedWaveProvider CreateBuffer(WaveFormat format) =>
        new(format)
        {
            BufferDuration = TimeSpan.FromMilliseconds(750),
            DiscardOnBufferOverflow = true,
            ReadFully = true
        };

    private static void AddSamples(BufferedWaveProvider? buffer, WaveInEventArgs e)
    {
        if (buffer is null || e.BytesRecorded <= 0)
        {
            return;
        }

        buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
    }

    private static ISampleProvider CreateSource(BufferedWaveProvider buffer)
    {
        ISampleProvider source = buffer.ToSampleProvider();

        if (source.WaveFormat.Channels == 1)
        {
            source = new MonoToStereoSampleProvider(source);
        }
        else if (source.WaveFormat.Channels > 2)
        {
            source = new FirstTwoChannelsSampleProvider(source);
        }

        if (source.WaveFormat.SampleRate != TargetSampleRate)
        {
            source = new WdlResamplingSampleProvider(source, TargetSampleRate);
        }

        return source;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();

        _microphoneCapture?.Dispose();
        _systemCapture?.Dispose();
        _microphoneDevice?.Dispose();
        _renderDevice?.Dispose();
        _cts.Dispose();
    }

    private sealed class FirstTwoChannelsSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly int _sourceChannels;
        private float[] _scratch = [];

        public FirstTwoChannelsSampleProvider(ISampleProvider source)
        {
            _source = source;
            _sourceChannels = source.WaveFormat.Channels;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 2);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            var frames = count / 2;
            var scratchLength = frames * _sourceChannels;
            if (_scratch.Length < scratchLength)
            {
                _scratch = new float[scratchLength];
            }

            var read = _source.Read(_scratch, 0, scratchLength);
            var framesRead = read / _sourceChannels;

            for (var frame = 0; frame < framesRead; frame++)
            {
                var sourceIndex = frame * _sourceChannels;
                var targetIndex = offset + frame * 2;
                buffer[targetIndex] = _scratch[sourceIndex];
                buffer[targetIndex + 1] = _scratch[sourceIndex + 1];
            }

            return framesRead * 2;
        }
    }
}
