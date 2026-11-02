using System.Diagnostics;
using System.Drawing;
using FreeScreenRecorder.Models;
using FreeScreenRecorder.Overlay;
using FreeScreenRecorder.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using WinRT.Interop;
using DrawingColor = System.Drawing.Color;
using UiColor = Windows.UI.Color;

namespace FreeScreenRecorder;

public sealed partial class MainPage : Page
{
    private readonly MonitorService _monitorService = new();
    private readonly AudioDeviceService _audioDeviceService = new();
    private readonly SettingsStore _settingsStore = new();
    private readonly FfmpegRecorder _recorder = new();
    private readonly CursorOverlay _overlay;
    private readonly DispatcherQueue _dispatcher;

    private RecordingSettings _settings = new();
    private RecordingWidget? _recordingWidget;
    private DispatcherQueueTimer? _elapsedTimer;
    private DateTimeOffset _recordingStarted;
    private bool _isRecording;
    private bool _stopInProgress;
    private bool _initializing = true;
    private bool _hasMicrophones;
    private bool _updatingFfmpegBox;
    private string? _lastOutputPath;

    public MainPage()
    {
        InitializeComponent();

        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _overlay = new CursorOverlay(_dispatcher);
        _settings = _settingsStore.Load();

        _recorder.UnexpectedExit += Recorder_UnexpectedExit;

        PopulateStaticOptions();

        Loaded += MainPage_Loaded;
        Unloaded += MainPage_Unloaded;
    }

    public bool IsRecording => _isRecording;

    public Task StopForShutdownAsync() => StopRecordingAsync(userInitiated: false);

    private async void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        _initializing = true;

        OutputFolderBox.Text = _settings.OutputFolder;
        FfmpegBox.Text = _settings.FfmpegPath;
        MicrophoneToggle.IsOn = _settings.MicrophoneEnabled;
        SystemAudioToggle.IsOn = _settings.SystemAudioEnabled;
        CursorHighlightToggle.IsOn = _settings.CursorHighlightEnabled;
        ClickEffectsToggle.IsOn = _settings.ClickEffectsEnabled;

        var cursorColor = ParseColor(_settings.CursorColor, UiColor.FromArgb(255, 34, 211, 238));
        var clickColor = ParseColor(_settings.ClickColor, UiColor.FromArgb(255, 255, 82, 82));
        CursorColorPicker.Color = cursorColor;
        ClickColorPicker.Color = clickColor;
        CursorColorSwatch.Background = new SolidColorBrush(cursorColor);
        ClickColorSwatch.Background = new SolidColorBrush(clickColor);
        CursorSizeSlider.Value = Math.Clamp(_settings.CursorSize, 48, 320);
        CursorSizeText.Text = $"{(int)Math.Round(CursorSizeSlider.Value)} px";

        RefreshMonitors();
        RefreshMicrophones();

        if (string.IsNullOrWhiteSpace(FfmpegBox.Text) || !File.Exists(FfmpegBox.Text))
        {
            var detected = await Task.Run(() => FfmpegService.FindFfmpeg(_settings.FfmpegPath));
            if (!string.IsNullOrWhiteSpace(detected))
            {
                _updatingFfmpegBox = true;
                FfmpegBox.Text = detected;
                _updatingFfmpegBox = false;
            }
        }

        UpdateFfmpegStatus();
        UpdateOutputPreview();
        MicrophoneComboBox.IsEnabled = MicrophoneToggle.IsOn;
        CursorSizeSlider.IsEnabled = CursorHighlightToggle.IsOn;

        _initializing = false;
        UpdateRecordButtonState();
    }

    private void MainPage_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_isRecording)
        {
            _ = StopRecordingAsync(userInitiated: false);
        }
    }

    private void PopulateStaticOptions()
    {
        CodecComboBox.Items.Add(new CodecOption(VideoCodecKind.H264, "H.264 (AVC) - most compatible"));
        CodecComboBox.Items.Add(new CodecOption(VideoCodecKind.Hevc, "HEVC (H.265) - smaller files"));

        EncoderComboBox.Items.Add(new EncoderOption(EncoderKind.Auto, "Auto (hardware first)"));
        EncoderComboBox.Items.Add(new EncoderOption(EncoderKind.IntelQuickSync, "Intel Quick Sync Video"));
        EncoderComboBox.Items.Add(new EncoderOption(EncoderKind.NvidiaNvenc, "NVIDIA NVENC"));
        EncoderComboBox.Items.Add(new EncoderOption(EncoderKind.AmdAmf, "AMD AMF / VCE-VCN"));
        EncoderComboBox.Items.Add(new EncoderOption(EncoderKind.Cpu, "CPU (libx264 / libx265)"));

        QualityComboBox.Items.Add(new QualityOption(QualityPreset.High, "High - best quality"));
        QualityComboBox.Items.Add(new QualityOption(QualityPreset.Balanced, "Balanced - recommended"));
        QualityComboBox.Items.Add(new QualityOption(QualityPreset.Small, "Small - smallest file"));

        FpsComboBox.Items.Add(new FpsOption(30, "30 fps"));
        FpsComboBox.Items.Add(new FpsOption(60, "60 fps"));

        SelectItem<CodecOption>(CodecComboBox, item => item.Kind == _settings.Codec);
        SelectItem<EncoderOption>(EncoderComboBox, item => item.Kind == _settings.Encoder);
        SelectItem<QualityOption>(QualityComboBox, item => item.Preset == _settings.Quality);
        SelectItem<FpsOption>(FpsComboBox, item => item.Fps == _settings.Fps);
    }

    private void RefreshMonitors()
    {
        var selectedDevice = (MonitorComboBox.SelectedItem as MonitorInfo)?.DeviceName
                             ?? _settings.MonitorDeviceName;

        var monitors = _monitorService.GetMonitors();
        MonitorComboBox.Items.Clear();

        foreach (var monitor in monitors)
        {
            MonitorComboBox.Items.Add(monitor);
        }

        var selected = monitors.FirstOrDefault(m => m.DeviceName == selectedDevice)
                       ?? monitors.FirstOrDefault(m => m.IsPrimary)
                       ?? monitors.FirstOrDefault();

        if (selected is not null)
        {
            MonitorComboBox.SelectedItem = selected;
        }
    }

    private void RefreshMicrophones()
    {
        var selectedId = (MicrophoneComboBox.SelectedItem as AudioDeviceInfo)?.Id
                         ?? _settings.MicrophoneDeviceId;

        var microphones = _audioDeviceService.GetMicrophones();
        _hasMicrophones = microphones.Count > 0;
        MicrophoneComboBox.Items.Clear();

        foreach (var microphone in microphones)
        {
            MicrophoneComboBox.Items.Add(microphone);
        }

        if (!_hasMicrophones)
        {
            MicrophoneToggle.IsOn = false;
            MicrophoneToggle.IsEnabled = false;
            MicrophoneComboBox.PlaceholderText = "No microphone found";
            MicrophoneComboBox.IsEnabled = false;
            return;
        }

        MicrophoneComboBox.PlaceholderText = "Select a microphone";
        MicrophoneToggle.IsEnabled = !_isRecording;

        var selected = microphones.FirstOrDefault(m => m.Id == selectedId)
                       ?? _audioDeviceService.GetDefaultMicrophone()
                       ?? microphones.FirstOrDefault();

        if (selected is not null)
        {
            MicrophoneComboBox.SelectedItem = selected;
        }

        MicrophoneComboBox.IsEnabled = MicrophoneToggle.IsOn;
    }

    private async void RecordButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRecording)
        {
            return;
        }

        try
        {
            var options = CreateRecordingOptions();
            CaptureSettingsFromUi();
            _settingsStore.Save(_settings);

            ErrorInfoBar.IsOpen = false;
            StatusText.Text = "Starting...";
            RecordingRing.IsActive = true;
            SetInputsEnabled(false);
            RecordButton.IsEnabled = false;
            StopButton.IsEnabled = false;

            await _recorder.StartAsync(options);

            _isRecording = true;
            _recordingStarted = DateTimeOffset.Now;
            _lastOutputPath = options.OutputPath;

            _overlay.Start(
                options.Monitor.Bounds,
                options.CursorSize,
                options.CursorHighlightEnabled,
                options.CursorColor,
                options.ClickEffectsEnabled,
                options.ClickColor);

            _recordingWidget = new RecordingWidget(_dispatcher, StopFromWidget);
            _recordingWidget.Start(options.Monitor.Bounds);
            _recordingWidget.UpdateElapsed(TimeSpan.Zero);

            StartElapsedTimer();

            StopButton.IsEnabled = true;
            StatusText.Text = "Recording";
            OutputFilePreviewText.Text = "Recording to: " + options.OutputPath;
            OpenOutputButton.IsEnabled = true;
            EncoderHintText.Text = "Encoding with " +
                                   FfmpegService.GetEncoderDisplayName(_recorder.ResolvedEncoder, options.Codec) +
                                   ".";
            UpdateOutputFolderFromPath(options.OutputPath);
        }
        catch (Exception ex)
        {
            await SafeStopAsync();
            RecordingRing.IsActive = false;
            StatusText.Text = "Ready to record";
            SetInputsEnabled(true);
            UpdateRecordButtonState();
            ShowError(ex.Message);
        }
    }

    private async void StopButton_Click(object sender, RoutedEventArgs e)
    {
        await StopRecordingAsync(userInitiated: true);
    }

    private async Task StopRecordingAsync(bool userInitiated)
    {
        if (_stopInProgress)
        {
            return;
        }

        _stopInProgress = true;

        try
        {
            DisposeRecordingWidget();
            _overlay.Stop();
            StopElapsedTimer();

            if (userInitiated && (_isRecording || _recorder.IsRecording))
            {
                StatusText.Text = "Saving...";
                RecordingRing.IsActive = true;
            }

            var path = await _recorder.StopAsync();
            if (!string.IsNullOrWhiteSpace(path))
            {
                _lastOutputPath = path;
            }

            _isRecording = false;
            RecordingRing.IsActive = false;
            StopButton.IsEnabled = false;
            SetInputsEnabled(true);
            UpdateRecordButtonState();

            if (userInitiated)
            {
                StatusText.Text = File.Exists(_lastOutputPath) ? "Saved" : "Ready to record";
            }

            if (!string.IsNullOrWhiteSpace(_lastOutputPath))
            {
                OutputFilePreviewText.Text = "Saved: " + _lastOutputPath;
                OpenOutputButton.IsEnabled = true;
                UpdateOutputFolderFromPath(_lastOutputPath);
            }
        }
        catch (Exception ex)
        {
            _isRecording = false;
            RecordingRing.IsActive = false;
            StatusText.Text = "Ready to record";
            StopButton.IsEnabled = false;
            SetInputsEnabled(true);
            UpdateRecordButtonState();
            ShowError(ex.Message);
        }
        finally
        {
            _stopInProgress = false;
        }
    }

    private async Task SafeStopAsync()
    {
        try
        {
            await _recorder.StopAsync();
        }
        catch
        {
            // Preserve the original error.
        }

        DisposeRecordingWidget();
        _overlay.Stop();
        StopElapsedTimer();
        _isRecording = false;
    }

    private void DisposeRecordingWidget()
    {
        _recordingWidget?.Dispose();
        _recordingWidget = null;
    }

    private void StopFromWidget()
    {
        _dispatcher.TryEnqueue(async () => await StopRecordingAsync(userInitiated: true));
    }

    private RecordingOptions CreateRecordingOptions()
    {
        if (MonitorComboBox.SelectedItem is not MonitorInfo monitor)
        {
            throw new InvalidOperationException("Select a display to record.");
        }

        var ffmpegPath = FfmpegBox.Text.Trim();
        if (!File.Exists(ffmpegPath))
        {
            throw new FileNotFoundException("ffmpeg.exe was not found. Install ffmpeg or browse to ffmpeg.exe.");
        }

        var folder = OutputFolderBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(folder))
        {
            throw new InvalidOperationException("Select an output folder.");
        }

        var microphone = MicrophoneComboBox.SelectedItem as AudioDeviceInfo;
        if (MicrophoneToggle.IsOn && microphone is null)
        {
            throw new InvalidOperationException("Select a microphone, or turn the microphone off.");
        }

        Directory.CreateDirectory(folder);

        var codec = (CodecComboBox.SelectedItem as CodecOption)?.Kind ?? VideoCodecKind.H264;
        var encoder = (EncoderComboBox.SelectedItem as EncoderOption)?.Kind ?? EncoderKind.Auto;
        var quality = (QualityComboBox.SelectedItem as QualityOption)?.Preset ?? QualityPreset.Balanced;
        var fps = (FpsComboBox.SelectedItem as FpsOption)?.Fps ?? 30;

        var fileName = $"Recording_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.mp4";
        var outputPath = Path.Combine(folder, fileName);

        return new RecordingOptions(
            monitor,
            microphone,
            MicrophoneToggle.IsOn,
            SystemAudioToggle.IsOn,
            outputPath,
            ffmpegPath,
            encoder,
            codec,
            quality,
            fps,
            Math.Clamp(_settings.AudioBitrateKbps, 96, 320),
            CursorHighlightToggle.IsOn,
            (int)Math.Round(CursorSizeSlider.Value),
            ToDrawingColor(CursorColorPicker.Color),
            ClickEffectsToggle.IsOn,
            ToDrawingColor(ClickColorPicker.Color));
    }

    private void CaptureSettingsFromUi()
    {
        _settings.OutputFolder = OutputFolderBox.Text.Trim();
        _settings.FfmpegPath = FfmpegBox.Text.Trim();
        _settings.MonitorDeviceName = (MonitorComboBox.SelectedItem as MonitorInfo)?.DeviceName;
        _settings.MicrophoneDeviceId = (MicrophoneComboBox.SelectedItem as AudioDeviceInfo)?.Id;
        _settings.MicrophoneEnabled = MicrophoneToggle.IsOn;
        _settings.SystemAudioEnabled = SystemAudioToggle.IsOn;
        _settings.Encoder = (EncoderComboBox.SelectedItem as EncoderOption)?.Kind ?? EncoderKind.Auto;
        _settings.Codec = (CodecComboBox.SelectedItem as CodecOption)?.Kind ?? VideoCodecKind.H264;
        _settings.Quality = (QualityComboBox.SelectedItem as QualityOption)?.Preset ?? QualityPreset.Balanced;
        _settings.Fps = (FpsComboBox.SelectedItem as FpsOption)?.Fps ?? 30;
        _settings.CursorHighlightEnabled = CursorHighlightToggle.IsOn;
        _settings.CursorSize = (int)Math.Round(CursorSizeSlider.Value);
        _settings.ClickEffectsEnabled = ClickEffectsToggle.IsOn;
        _settings.CursorColor = ToHex(CursorColorPicker.Color);
        _settings.ClickColor = ToHex(ClickColorPicker.Color);
    }

    private void SetInputsEnabled(bool enabled)
    {
        MonitorComboBox.IsEnabled = enabled;
        RefreshMonitorsButton.IsEnabled = enabled;
        CodecComboBox.IsEnabled = enabled;
        EncoderComboBox.IsEnabled = enabled;
        QualityComboBox.IsEnabled = enabled;
        FpsComboBox.IsEnabled = enabled;
        MicrophoneToggle.IsEnabled = enabled && _hasMicrophones;
        MicrophoneComboBox.IsEnabled = enabled && _hasMicrophones && MicrophoneToggle.IsOn;
        RefreshMicrophonesButton.IsEnabled = enabled;
        SystemAudioToggle.IsEnabled = enabled;
        OutputFolderBox.IsEnabled = enabled;
        FfmpegBox.IsEnabled = enabled;
        CursorHighlightToggle.IsEnabled = enabled;
        CursorSizeSlider.IsEnabled = enabled && CursorHighlightToggle.IsOn;
        ClickEffectsToggle.IsEnabled = enabled;
        CursorColorButton.IsEnabled = enabled;
        ClickColorButton.IsEnabled = enabled;
    }

    private void UpdateRecordButtonState()
    {
        if (_initializing)
        {
            return;
        }

        var hasFfmpeg = File.Exists(FfmpegBox.Text.Trim());
        var hasMonitor = MonitorComboBox.SelectedItem is MonitorInfo;
        var hasFolder = !string.IsNullOrWhiteSpace(OutputFolderBox.Text);

        RecordButton.IsEnabled = !_isRecording && hasFfmpeg && hasMonitor && hasFolder;
    }

    private void StartElapsedTimer()
    {
        StopElapsedTimer();
        _elapsedTimer = _dispatcher.CreateTimer();
        _elapsedTimer.Interval = TimeSpan.FromMilliseconds(200);
        _elapsedTimer.IsRepeating = true;
        _elapsedTimer.Tick += ElapsedTimer_Tick;
        _elapsedTimer.Start();
        UpdateElapsedText();
    }

    private void StopElapsedTimer()
    {
        if (_elapsedTimer is null)
        {
            return;
        }

        _elapsedTimer.Stop();
        _elapsedTimer.Tick -= ElapsedTimer_Tick;
        _elapsedTimer = null;
    }

    private void ElapsedTimer_Tick(DispatcherQueueTimer sender, object args) => UpdateElapsedText();

    private void UpdateElapsedText()
    {
        var elapsed = DateTimeOffset.Now - _recordingStarted;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        TimerText.Text = elapsed.ToString(@"hh\:mm\:ss");
        _recordingWidget?.UpdateElapsed(elapsed);
    }

    private void Recorder_UnexpectedExit(object? sender, string log)
    {
        _dispatcher.TryEnqueue(async () =>
        {
            await StopRecordingAsync(userInitiated: false);
            StatusText.Text = "Stopped";
            var tail = log.Length > 1200 ? log[^1200..] : log;
            ShowError("ffmpeg stopped unexpectedly.\n" + tail);
        });
    }

    private void RefreshMonitorsButton_Click(object sender, RoutedEventArgs e) => RefreshMonitors();

    private void RefreshMicrophonesButton_Click(object sender, RoutedEventArgs e) => RefreshMicrophones();

    private void MicrophoneToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (MicrophoneComboBox is not null)
        {
            MicrophoneComboBox.IsEnabled = _hasMicrophones && MicrophoneToggle.IsOn && !_isRecording;
        }
    }

    private void CodecComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EncoderHintText is null)
        {
            return;
        }

        var codec = (CodecComboBox.SelectedItem as CodecOption)?.Kind ?? VideoCodecKind.H264;
        EncoderHintText.Text = codec == VideoCodecKind.Hevc
            ? "HEVC is 30-50% smaller than H.264, but older players may not open it. Hardware HEVC depends on the GPU."
            : "H.264 is the safest choice for sharing. Auto picks the best hardware encoder available, then falls back to CPU.";
    }

    private void OutputFolderBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_initializing)
        {
            return;
        }

        UpdateOutputPreview();
        UpdateRecordButtonState();
    }

    private void FfmpegBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_initializing || _updatingFfmpegBox)
        {
            return;
        }

        UpdateFfmpegStatus();
    }

    private void CursorSizeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_initializing || CursorSizeText is null)
        {
            return;
        }

        _settings.CursorSize = (int)Math.Round(e.NewValue);
        CursorSizeText.Text = $"{_settings.CursorSize} px";
    }

    private void CursorHighlightToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (CursorSizeSlider is null)
        {
            return;
        }

        CursorSizeSlider.IsEnabled = CursorHighlightToggle.IsOn && !_isRecording;
    }

    private void CursorColorPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs e)
    {
        CursorColorSwatch.Background = new SolidColorBrush(e.NewColor);
        _settings.CursorColor = ToHex(e.NewColor);
    }

    private void ClickColorPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        ClickColorSwatch.Background = new SolidColorBrush(args.NewColor);
        _settings.ClickColor = ToHex(args.NewColor);
    }

    private async void BrowseFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow!));

        var folder = await picker.PickSingleFolderAsync();
        if (folder is null)
        {
            return;
        }

        OutputFolderBox.Text = folder.Path;
        _settings.OutputFolder = folder.Path;
        UpdateOutputPreview();
        UpdateRecordButtonState();
    }

    private async void BrowseFfmpegButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".exe");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow!));

        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        _updatingFfmpegBox = true;
        FfmpegBox.Text = file.Path;
        _updatingFfmpegBox = false;
        UpdateFfmpegStatus();
    }

    private void OpenOutputButton_Click(object sender, RoutedEventArgs e)
    {
        var path = _lastOutputPath;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
            {
                UseShellExecute = true
            });
            return;
        }

        var folder = OutputFolderBox.Text.Trim();
        if (Directory.Exists(folder))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"")
            {
                UseShellExecute = true
            });
        }
    }

    private void UpdateFfmpegStatus()
    {
        var path = FfmpegBox.Text.Trim();
        if (File.Exists(path))
        {
            FfmpegStatusText.Text = "ffmpeg found. Hardware encoding is selected automatically.";
            UpdateRecordButtonState();
            return;
        }

        var detected = FfmpegService.FindFfmpeg(path);
        if (!string.IsNullOrWhiteSpace(detected))
        {
            _updatingFfmpegBox = true;
            FfmpegBox.Text = detected;
            _updatingFfmpegBox = false;
            FfmpegStatusText.Text = "ffmpeg found. Hardware encoding is selected automatically.";
        }
        else
        {
            FfmpegStatusText.Text = "ffmpeg.exe not found. Install ffmpeg or browse to ffmpeg.exe.";
        }

        UpdateRecordButtonState();
    }

    private void UpdateOutputPreview()
    {
        var folder = OutputFolderBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(folder))
        {
            OutputFilePreviewText.Text = "Output: select a folder";
            return;
        }

        var fileName = $"Recording_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.mp4";
        OutputFilePreviewText.Text = "Next file: " + Path.Combine(folder, fileName);
    }

    private void UpdateOutputFolderFromPath(string path)
    {
        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
        {
            _settings.OutputFolder = folder;
            OutputFolderBox.Text = folder;
        }
    }

    private void ShowError(string message)
    {
        ErrorInfoBar.Message = message.Trim();
        ErrorInfoBar.IsOpen = true;
    }

    private static void SelectItem<T>(ComboBox comboBox, Func<T, bool> predicate)
    {
        var item = comboBox.Items.OfType<T>().FirstOrDefault(predicate);
        if (item is not null)
        {
            comboBox.SelectedItem = item;
        }
        else if (comboBox.Items.Count > 0)
        {
            comboBox.SelectedIndex = 0;
        }
    }

    private static DrawingColor ToDrawingColor(UiColor color) =>
        DrawingColor.FromArgb(color.A, color.R, color.G, color.B);

    private static string ToHex(UiColor color) =>
        $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

    private static UiColor ParseColor(string? hex, UiColor fallback)
    {
        try
        {
            var value = (hex ?? string.Empty).Trim().TrimStart('#');
            if (value.Length == 6)
            {
                value = "FF" + value;
            }

            if (value.Length != 8)
            {
                return fallback;
            }

            return UiColor.FromArgb(
                Convert.ToByte(value.Substring(0, 2), 16),
                Convert.ToByte(value.Substring(2, 2), 16),
                Convert.ToByte(value.Substring(4, 2), 16),
                Convert.ToByte(value.Substring(6, 2), 16));
        }
        catch
        {
            return fallback;
        }
    }

    private sealed record EncoderOption(EncoderKind Kind, string Name)
    {
        public override string ToString() => Name;
    }

    private sealed record CodecOption(VideoCodecKind Kind, string Name)
    {
        public override string ToString() => Name;
    }

    private sealed record QualityOption(QualityPreset Preset, string Name)
    {
        public override string ToString() => Name;
    }

    private sealed record FpsOption(int Fps, string Name)
    {
        public override string ToString() => Name;
    }
}
