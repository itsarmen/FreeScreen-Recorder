using System.Drawing;
using System.Drawing.Drawing2D;
using FreeScreenRecorder.Interop;
using FreeScreenRecorder.Models;
using Microsoft.UI.Dispatching;

namespace FreeScreenRecorder.Overlay;

/// <summary>
/// Draws a coloured halo around the system cursor and expanding rings on mouse
/// clicks using click-through layered windows. Context menus and other app
/// windows remain fully interactive while the overlay is visible.
/// </summary>
public sealed class CursorOverlay : IDisposable
{
    private const int DefaultHaloSize = 144;
    private const int MinHaloSize = 48;
    private const int MaxHaloSize = 320;
    private static readonly TimeSpan RippleDuration = TimeSpan.FromMilliseconds(520);

    private int _haloSize = DefaultHaloSize;
    private int _rippleSize = 300;

    private readonly DispatcherQueue _dispatcherQueue;
    private readonly List<Ripple> _ripples = new();

    private DispatcherQueueTimer? _timer;
    private LayeredOverlayWindow? _haloWindow;
    private Bitmap? _haloBitmap;
    private ScreenRect _monitorBounds;
    private Color _cursorColor = Color.Cyan;
    private Color _clickColor = Color.OrangeRed;
    private bool _highlightEnabled = true;
    private bool _clickEnabled = true;
    private bool _leftWasDown;
    private bool _rightWasDown;
    private bool _running;
    private bool _disposed;

    public CursorOverlay(DispatcherQueue dispatcherQueue)
    {
        _dispatcherQueue = dispatcherQueue;
    }

    public void Start(
        ScreenRect monitorBounds,
        int haloSize,
        bool highlightEnabled,
        Color cursorColor,
        bool clickEnabled,
        Color clickColor)
    {
        if (_running)
        {
            Stop();
        }

        _monitorBounds = monitorBounds;
        _haloSize = Math.Clamp(haloSize, MinHaloSize, MaxHaloSize);
        _rippleSize = Math.Clamp((int)Math.Round(_haloSize * 2.08), 180, 440);
        _highlightEnabled = highlightEnabled;
        _cursorColor = cursorColor;
        _clickEnabled = clickEnabled;
        _clickColor = clickColor;

        _haloWindow = new LayeredOverlayWindow(_haloSize, _haloSize);
        _haloBitmap = new Bitmap(_haloSize, _haloSize, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);

        _leftWasDown = NativeMethods.IsKeyDown(NativeMethods.VK_LBUTTON);
        _rightWasDown = NativeMethods.IsKeyDown(NativeMethods.VK_RBUTTON);

        _timer = _dispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(16);
        _timer.IsRepeating = true;
        _timer.Tick += OnTick;
        _timer.Start();

        _running = true;
    }

    public void Stop()
    {
        if (!_running)
        {
            return;
        }

        _running = false;

        if (_timer is not null)
        {
            _timer.Stop();
            _timer.Tick -= OnTick;
            _timer = null;
        }

        foreach (var ripple in _ripples)
        {
            ripple.Dispose();
        }

        _ripples.Clear();

        _haloWindow?.Dispose();
        _haloWindow = null;
        _haloBitmap?.Dispose();
        _haloBitmap = null;
    }

    private void OnTick(DispatcherQueueTimer sender, object args)
    {
        if (!_running)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var cursorInfo = new NativeMethods.CURSORINFO
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.CURSORINFO>()
        };

        var hasCursor = NativeMethods.GetCursorInfo(ref cursorInfo) &&
                        (cursorInfo.flags & NativeMethods.CURSOR_SHOWING) != 0;

        if (hasCursor)
        {
            var cursor = cursorInfo.ptScreenPos;
            var insideMonitor = _monitorBounds.Contains(cursor.X, cursor.Y);

            if (insideMonitor && _highlightEnabled)
            {
                DrawHalo(cursor.X, cursor.Y);
            }
            else
            {
                _haloWindow?.Hide();
            }

            var leftDown = NativeMethods.IsKeyDown(NativeMethods.VK_LBUTTON);
            var rightDown = NativeMethods.IsKeyDown(NativeMethods.VK_RBUTTON);

            if (insideMonitor && _clickEnabled)
            {
                if (leftDown && !_leftWasDown)
                {
                    AddRipple(cursor.X, cursor.Y, isRightClick: false, now);
                }

                if (rightDown && !_rightWasDown)
                {
                    AddRipple(cursor.X, cursor.Y, isRightClick: true, now);
                }
            }

            _leftWasDown = leftDown;
            _rightWasDown = rightDown;
        }
        else
        {
            _haloWindow?.Hide();
        }

        UpdateRipples(now);
    }

    private void DrawHalo(int cursorX, int cursorY)
    {
        if (_haloWindow is null || _haloBitmap is null)
        {
            return;
        }

        using (var graphics = Graphics.FromImage(_haloBitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);

            var color = _cursorColor;
            var size = (float)_haloSize;
            var scale = size / DefaultHaloSize;
            var center = size / 2f;
            var glowInset = 8 * scale;
            var ringInset = 34 * scale;
            var innerInset = 48 * scale;
            var dotRadius = Math.Max(2f, 2.5f * scale);

            var glowRect = new RectangleF(glowInset, glowInset, size - glowInset * 2, size - glowInset * 2);
            var ringRect = new RectangleF(ringInset, ringInset, size - ringInset * 2, size - ringInset * 2);
            var innerRect = new RectangleF(innerInset, innerInset, size - innerInset * 2, size - innerInset * 2);

            using (var glow = new SolidBrush(Color.FromArgb(52, color)))
            {
                graphics.FillEllipse(glow, glowRect);
            }

            using (var ring = new Pen(Color.FromArgb(235, color), Math.Max(1.5f, 3.6f * scale)))
            {
                graphics.DrawEllipse(ring, ringRect);
            }

            using (var inner = new Pen(Color.FromArgb(120, color), Math.Max(0.8f, 1.4f * scale)))
            {
                graphics.DrawEllipse(inner, innerRect);
            }

            using (var dot = new SolidBrush(Color.FromArgb(255, color)))
            {
                graphics.FillEllipse(dot, center - dotRadius, center - dotRadius, dotRadius * 2, dotRadius * 2);
            }
        }

        _haloWindow.Draw(_haloBitmap, cursorX - _haloSize / 2, cursorY - _haloSize / 2);
    }

    private void AddRipple(int x, int y, bool isRightClick, DateTime now)
    {
        if (_ripples.Count >= 8)
        {
            _ripples[0].Dispose();
            _ripples.RemoveAt(0);
        }

        var color = isRightClick ? Blend(_clickColor, Color.Orange, 0.35f) : _clickColor;
        _ripples.Add(new Ripple(x, y, color, now, _rippleSize));
    }

    private void UpdateRipples(DateTime now)
    {
        for (var i = _ripples.Count - 1; i >= 0; i--)
        {
            var ripple = _ripples[i];
            var progress = (now - ripple.StartedAt).TotalMilliseconds / RippleDuration.TotalMilliseconds;

            if (progress >= 1)
            {
                ripple.Dispose();
                _ripples.RemoveAt(i);
                continue;
            }

            ripple.Draw(progress);
        }
    }

    private static Color Blend(Color first, Color second, float amount)
    {
        var a = amount;
        var b = 1 - a;
        return Color.FromArgb(
            (int)(first.A * b + second.A * a),
            (int)(first.R * b + second.R * a),
            (int)(first.G * b + second.G * a),
            (int)(first.B * b + second.B * a));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }

    private sealed class Ripple : IDisposable
    {
        private readonly LayeredOverlayWindow _window;
        private readonly Bitmap _bitmap;
        private readonly Color _color;
        private readonly int _centerX;
        private readonly int _centerY;
        private readonly int _size;
        private readonly float _lineWidth;

        public Ripple(int centerX, int centerY, Color color, DateTime startedAt, int size)
        {
            _centerX = centerX;
            _centerY = centerY;
            _color = color;
            StartedAt = startedAt;
            _size = size;
            _lineWidth = Math.Clamp(size / 300f * 4.2f, 2.5f, 6f);
            _window = new LayeredOverlayWindow(_size, _size);
            _bitmap = new Bitmap(_size, _size, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        }

        public DateTime StartedAt { get; }

        public void Draw(double progress)
        {
            var scale = _size / 300.0;
            var eased = 1 - Math.Pow(1 - progress, 3);
            var radius = 14 * scale + eased * (_size / 2.0 - 26 * scale);
            var alpha = (int)(225 * Math.Pow(1 - progress, 1.15));

            using (var graphics = Graphics.FromImage(_bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.Clear(Color.Transparent);

                var center = _size / 2f;
                var rect = new RectangleF(
                    (float)(center - radius),
                    (float)(center - radius),
                    (float)(radius * 2),
                    (float)(radius * 2));

                using (var pen = new Pen(Color.FromArgb(Math.Clamp(alpha, 0, 255), _color), _lineWidth))
                {
                    graphics.DrawEllipse(pen, rect);
                }

                if (progress < 0.45)
                {
                    using var inner = new Pen(
                        Color.FromArgb(Math.Clamp(alpha / 2, 0, 255), _color),
                        Math.Max(1f, _lineWidth * 0.43f));
                    graphics.DrawEllipse(inner, rect);
                }
            }

            _window.Draw(_bitmap, _centerX - _size / 2, _centerY - _size / 2);
        }

        public void Dispose()
        {
            _window.Dispose();
            _bitmap.Dispose();
        }
    }
}
