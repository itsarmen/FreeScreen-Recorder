using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using FreeScreenRecorder.Interop;
using FreeScreenRecorder.Models;
using Microsoft.UI.Dispatching;

namespace FreeScreenRecorder.Overlay;

/// <summary>
/// A small always-on-top recording bar that stays hidden above the recorded
/// monitor and slides into view when the mouse reaches the top edge. The bar
/// is excluded from capture, so it never appears in the recording. It has a
/// Stop button and is intentionally built as a no-activate Win32 window so it
/// never steals focus from the app being recorded.
/// </summary>
internal sealed class RecordingWidget : IDisposable
{
    private const int BaseWidth = 430;
    private const int BaseHeight = 74;
    private const string WindowClassName = "FreeScreenRecorder.RecordingWidget";
    private const double AnimationMilliseconds = 190;
    private const double HideDelayMilliseconds = 650;

    private static readonly object ClassGate = new();
    private static readonly NativeMethods.WndProc WindowProc = StaticWindowProc;
    private static bool _classRegistered;
    private static int _classError;
    private static RecordingWidget? _active;

    private readonly DispatcherQueue _dispatcher;
    private readonly Action _stopRequested;

    private DispatcherQueueTimer? _timer;
    private ScreenRect _monitor;
    private IntPtr _hwnd;
    private Bitmap? _bitmap;
    private int _width = BaseWidth;
    private int _height = BaseHeight;
    private int _x;
    private double _scale = 1;
    private RectangleF _stopRect;
    private bool _windowShown;
    private bool _animating;
    private bool _hideWhenDone;
    private double _fromY;
    private double _toY;
    private double _currentY;
    private DateTime _animationStartedAt;
    private DateTime _hideAt = DateTime.MaxValue;
    private bool _stopHover;
    private bool _dirty = true;
    private TimeSpan _elapsed;
    private string _lastTimerText = string.Empty;
    private bool _disposed;

    public RecordingWidget(DispatcherQueue dispatcher, Action stopRequested)
    {
        _dispatcher = dispatcher;
        _stopRequested = stopRequested;
    }

    private int HiddenY => _monitor.Y - _height;

    private int ShownY => _monitor.Y + (int)Math.Round(6 * _scale);

    private int BarX => _monitor.X + (_monitor.Width - _width) / 2;

    public void Start(ScreenRect monitor)
    {
        _monitor = monitor;
        EnsureWindowClass();

        _hwnd = NativeMethods.CreateWindowExW(
            NativeMethods.WS_EX_LAYERED |
            NativeMethods.WS_EX_TOPMOST |
            NativeMethods.WS_EX_TOOLWINDOW |
            NativeMethods.WS_EX_NOACTIVATE,
            WindowClassName,
            "FreeScreen Recorder",
            NativeMethods.WS_POPUP,
            monitor.X,
            monitor.Y,
            BaseWidth,
            BaseHeight,
            IntPtr.Zero,
            IntPtr.Zero,
            NativeMethods.GetModuleHandleW(null),
            IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException(
                _classRegistered
                    ? $"Could not create the recording widget window (Win32 error {error})."
                    : $"Could not register the recording widget window class (Win32 error {_classError}).");
        }

        _active = this;
        NativeMethods.SetWindowDisplayAffinity(_hwnd, NativeMethods.WDA_EXCLUDEFROMCAPTURE);

        NativeMethods.SetWindowPos(
            _hwnd,
            NativeMethods.HWND_TOPMOST,
            monitor.X,
            monitor.Y,
            BaseWidth,
            BaseHeight,
            NativeMethods.SWP_NOACTIVATE);

        var dpi = NativeMethods.GetDpiForWindow(_hwnd);
        _scale = Math.Clamp(dpi / 96.0, 0.75, 2.5);
        _width = (int)Math.Round(BaseWidth * _scale);
        _height = (int)Math.Round(BaseHeight * _scale);
        _x = BarX;
        _currentY = HiddenY;
        _bitmap = new Bitmap(_width, _height, PixelFormat.Format32bppPArgb);

        NativeMethods.SetWindowPos(
            _hwnd,
            NativeMethods.HWND_TOPMOST,
            _x,
            (int)_currentY,
            _width,
            _height,
            NativeMethods.SWP_NOACTIVATE);

        Draw();

        _timer = _dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(25);
        _timer.IsRepeating = true;
        _timer.Tick += OnTick;
        _timer.Start();
    }

    public void UpdateElapsed(TimeSpan elapsed)
    {
        _elapsed = elapsed;

        var text = elapsed.ToString(@"hh\:mm\:ss");
        if (text == _lastTimerText)
        {
            return;
        }

        _lastTimerText = text;
        _dirty = true;

        if (_windowShown)
        {
            Draw();
        }
    }

    private void OnTick(DispatcherQueueTimer sender, object args)
    {
        if (_disposed || _hwnd == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.GetCursorPos(out var cursor);
        var now = DateTime.UtcNow;

        var atTop = cursor.X >= _monitor.X &&
                    cursor.X < _monitor.Right &&
                    cursor.Y >= _monitor.Y - 2 &&
                    cursor.Y <= _monitor.Y + (int)Math.Round(3 * _scale);

        var overBar = _windowShown &&
                      cursor.X >= _x &&
                      cursor.X < _x + _width &&
                      cursor.Y >= _currentY &&
                      cursor.Y < _currentY + _height;

        if (atTop)
        {
            _hideAt = DateTime.MaxValue;

            if (!_windowShown)
            {
                ShowBar();
            }
            else if (_hideWhenDone)
            {
                _hideWhenDone = false;
                AnimateTo(ShownY, hideWhenDone: false);
            }
        }
        else if (overBar)
        {
            _hideAt = DateTime.MaxValue;
        }
        else if (_windowShown)
        {
            if (_hideAt == DateTime.MaxValue)
            {
                _hideAt = now.AddMilliseconds(HideDelayMilliseconds);
            }
            else if (now >= _hideAt)
            {
                _hideAt = DateTime.MaxValue;
                AnimateTo(HiddenY, hideWhenDone: true);
            }
        }

        if (_windowShown)
        {
            var stopHover = cursor.X >= _x + _stopRect.X &&
                            cursor.X <= _x + _stopRect.Right &&
                            cursor.Y >= _currentY + _stopRect.Top &&
                            cursor.Y <= _currentY + _stopRect.Bottom;

            if (stopHover != _stopHover)
            {
                _stopHover = stopHover;
                _dirty = true;
            }

            if (_dirty)
            {
                Draw();
            }
        }

        if (_animating)
        {
            UpdateAnimation(now);
        }
    }

    private void ShowBar()
    {
        if (_windowShown || _hwnd == IntPtr.Zero)
        {
            return;
        }

        _windowShown = true;
        _hideWhenDone = false;
        _currentY = HiddenY;
        _dirty = true;

        NativeMethods.SetWindowPos(
            _hwnd,
            NativeMethods.HWND_TOPMOST,
            _x,
            (int)_currentY,
            _width,
            _height,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);

        NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_SHOWNOACTIVATE);

        Draw();
        AnimateTo(ShownY, hideWhenDone: false);
    }

    private void AnimateTo(int targetY, bool hideWhenDone)
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        _fromY = _currentY;
        _toY = targetY;
        _hideWhenDone = hideWhenDone;
        _animationStartedAt = DateTime.UtcNow;
        _animating = true;
    }

    private void UpdateAnimation(DateTime now)
    {
        var progress = Math.Clamp(
            (now - _animationStartedAt).TotalMilliseconds / AnimationMilliseconds,
            0,
            1);
        var eased = 1 - Math.Pow(1 - progress, 3);

        _currentY = _fromY + (_toY - _fromY) * eased;

        NativeMethods.SetWindowPos(
            _hwnd,
            NativeMethods.HWND_TOPMOST,
            _x,
            (int)Math.Round(_currentY),
            _width,
            _height,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);

        if (progress < 1)
        {
            return;
        }

        _animating = false;
        _currentY = _toY;

        if (_hideWhenDone)
        {
            _hideWhenDone = false;
            _windowShown = false;
            _stopHover = false;
            NativeMethods.ShowWindow(_hwnd, 0);
        }
    }

    private void Draw()
    {
        if (_bitmap is null || _hwnd == IntPtr.Zero)
        {
            return;
        }

        _dirty = false;

        using var graphics = Graphics.FromImage(_bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        graphics.Clear(Color.Transparent);

        var scale = (float)_scale;
        var bounds = new RectangleF(1, 1, _width - 2, _height - 2);

        using (var backgroundPath = CreateRoundedRect(bounds, 15 * scale))
        using (var background = new SolidBrush(Color.FromArgb(238, 20, 21, 25)))
        using (var border = new Pen(Color.FromArgb(74, 255, 255, 255), 1.2f))
        {
            graphics.FillPath(background, backgroundPath);
            graphics.DrawPath(border, backgroundPath);
        }

        var centerY = _height / 2f;
        var dotRadius = 5.5f * scale;

        using (var red = new SolidBrush(Color.FromArgb(255, 235, 66, 72)))
        {
            graphics.FillEllipse(red, 22 * scale - dotRadius, centerY - dotRadius, dotRadius * 2, dotRadius * 2);
        }

        using (var recFont = new Font("Segoe UI Semibold", 13 * scale, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var textBrush = new SolidBrush(Color.FromArgb(235, 255, 255, 255)))
        {
            graphics.DrawString("REC", recFont, textBrush, 36 * scale, centerY - 8 * scale);

            using var timerFont = new Font("Consolas", 20 * scale, FontStyle.Bold, GraphicsUnit.Pixel);
            var timerText = _elapsed.ToString(@"hh\:mm\:ss");
            var timerSize = graphics.MeasureString(timerText, timerFont);
            graphics.DrawString(timerText, timerFont, textBrush, 82 * scale, centerY - timerSize.Height / 2);
        }

        var stopWidth = 96 * scale;
        var stopHeight = 42 * scale;
        var stopX = _width - stopWidth - 12 * scale;
        var stopY = (_height - stopHeight) / 2;
        _stopRect = new RectangleF(stopX, stopY, stopWidth, stopHeight);

        using (var stopPath = CreateRoundedRect(_stopRect, 10 * scale))
        using (var stopBrush = new SolidBrush(
                   _stopHover
                       ? Color.FromArgb(255, 235, 66, 72)
                       : Color.FromArgb(255, 188, 39, 45)))
        using (var stopFont = new Font("Segoe UI Semibold", 14 * scale, FontStyle.Bold, GraphicsUnit.Pixel))
        {
            graphics.FillPath(stopBrush, stopPath);

            const string stopText = "Stop";
            var stopSize = graphics.MeasureString(stopText, stopFont);
            graphics.DrawString(
                stopText,
                stopFont,
                Brushes.White,
                stopX + (stopWidth - stopSize.Width) / 2,
                stopY + (stopHeight - stopSize.Height) / 2);
        }

        var screenDc = NativeMethods.GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
        {
            return;
        }

        var memoryDc = NativeMethods.CreateCompatibleDC(screenDc);
        var hBitmap = _bitmap.GetHbitmap(Color.FromArgb(0));
        var oldBitmap = NativeMethods.SelectObject(memoryDc, hBitmap);

        try
        {
            var destination = new NativeMethods.POINT(_x, (int)Math.Round(_currentY));
            var size = new NativeMethods.SIZE(_width, _height);
            var source = new NativeMethods.POINT(0, 0);
            var blend = new NativeMethods.BLENDFUNCTION
            {
                BlendOp = NativeMethods.AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = NativeMethods.AC_SRC_ALPHA
            };

            NativeMethods.UpdateLayeredWindow(
                _hwnd,
                screenDc,
                ref destination,
                ref size,
                memoryDc,
                ref source,
                0,
                ref blend,
                NativeMethods.ULW_ALPHA);
        }
        finally
        {
            NativeMethods.SelectObject(memoryDc, oldBitmap);
            NativeMethods.DeleteObject(hBitmap);
            NativeMethods.DeleteDC(memoryDc);
            NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private static GraphicsPath CreateRoundedRect(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;

        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();

        return path;
    }

    private static void EnsureWindowClass()
    {
        lock (ClassGate)
        {
            if (_classRegistered)
            {
                return;
            }

            var windowClass = new NativeMethods.WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEX>(),
                style = 0,
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(WindowProc),
                cbClsExtra = 0,
                cbWndExtra = 0,
                hInstance = NativeMethods.GetModuleHandleW(null),
                hIcon = IntPtr.Zero,
                hCursor = IntPtr.Zero,
                hbrBackground = IntPtr.Zero,
                lpszMenuName = null,
                lpszClassName = WindowClassName,
                hIconSm = IntPtr.Zero
            };

            var atom = NativeMethods.RegisterClassExW(ref windowClass);
            if (atom == 0)
            {
                _classError = Marshal.GetLastWin32Error();
                return;
            }

            _classRegistered = true;
        }
    }

    private static IntPtr StaticWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        var widget = _active;
        if (widget is not null && widget._hwnd == hwnd)
        {
            return widget.HandleMessage(hwnd, msg, wParam, lParam);
        }

        return NativeMethods.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private IntPtr HandleMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case NativeMethods.WM_MOUSEACTIVATE:
                return new IntPtr(NativeMethods.MA_NOACTIVATE);

            case NativeMethods.WM_MOUSEMOVE:
            {
                var x = unchecked((short)(long)lParam);
                var y = unchecked((short)((long)lParam >> 16));
                var hover = _stopRect.Contains(x, y);
                if (hover != _stopHover)
                {
                    _stopHover = hover;
                    _dirty = true;
                }

                return IntPtr.Zero;
            }

            case NativeMethods.WM_SETCURSOR:
            {
                NativeMethods.GetCursorPos(out var cursor);
                var overStop = cursor.X >= _x + _stopRect.X &&
                               cursor.X <= _x + _stopRect.Right &&
                               cursor.Y >= _currentY + _stopRect.Top &&
                               cursor.Y <= _currentY + _stopRect.Bottom;

                if (overStop)
                {
                    NativeMethods.SetCursor(NativeMethods.LoadCursorW(
                        IntPtr.Zero,
                        new IntPtr(NativeMethods.IDC_HAND)));
                    return new IntPtr(1);
                }

                break;
            }

            case NativeMethods.WM_LBUTTONDOWN:
            {
                var x = unchecked((short)(long)lParam);
                var y = unchecked((short)((long)lParam >> 16));
                if (_stopRect.Contains(x, y))
                {
                    _stopHover = true;
                    _dirty = true;
                }

                return IntPtr.Zero;
            }

            case NativeMethods.WM_LBUTTONUP:
            {
                var x = unchecked((short)(long)lParam);
                var y = unchecked((short)((long)lParam >> 16));
                if (_stopRect.Contains(x, y))
                {
                    _stopRequested();
                }

                return IntPtr.Zero;
            }

            case NativeMethods.WM_DESTROY:
                return IntPtr.Zero;
        }

        return NativeMethods.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_timer is not null)
        {
            _timer.Stop();
            _timer.Tick -= OnTick;
            _timer = null;
        }

        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.ShowWindow(_hwnd, 0);
            NativeMethods.DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }

        if (ReferenceEquals(_active, this))
        {
            _active = null;
        }

        _bitmap?.Dispose();
        _bitmap = null;
    }
}
