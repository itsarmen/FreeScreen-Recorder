using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using FreeScreenRecorder.Interop;

namespace FreeScreenRecorder.Overlay;

/// <summary>
/// A click-through, always-on-top, per-pixel-alpha Win32 window. The window is
/// intentionally outside the WinUI visual tree so it can float over any app
/// (including context menus) without stealing input or focus.
/// </summary>
internal sealed class LayeredOverlayWindow : IDisposable
{
    private const string WindowClassName = "FreeScreenRecorder.LayeredOverlay";

    private static readonly object ClassGate = new();
    private static bool _classRegistered;
    private static readonly NativeMethods.WndProc WindowProc = StaticWindowProc;

    private IntPtr _hwnd;
    private bool _visible;
    private bool _disposed;

    public LayeredOverlayWindow(int width, int height)
    {
        Width = width;
        Height = height;
        EnsureWindowClass();
        CreateWindow();
    }

    public int Width { get; }

    public int Height { get; }

    public bool IsVisible => _visible;

    public void Draw(Bitmap bitmap, int x, int y)
    {
        if (_hwnd == IntPtr.Zero || _disposed)
        {
            return;
        }

        if (!_visible)
        {
            NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_SHOWNOACTIVATE);
            _visible = true;
        }

        var screenDc = NativeMethods.GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
        {
            return;
        }

        var memoryDc = NativeMethods.CreateCompatibleDC(screenDc);
        var hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
        var oldBitmap = NativeMethods.SelectObject(memoryDc, hBitmap);

        try
        {
            var destination = new NativeMethods.POINT(x, y);
            var size = new NativeMethods.SIZE(bitmap.Width, bitmap.Height);
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

    public void Hide()
    {
        if (_hwnd != IntPtr.Zero && _visible)
        {
            NativeMethods.ShowWindow(_hwnd, 0);
            _visible = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
    }

    private void CreateWindow()
    {
        const int exStyle =
            NativeMethods.WS_EX_LAYERED |
            NativeMethods.WS_EX_TRANSPARENT |
            NativeMethods.WS_EX_TOPMOST |
            NativeMethods.WS_EX_TOOLWINDOW |
            NativeMethods.WS_EX_NOACTIVATE;

        _hwnd = NativeMethods.CreateWindowExW(
            exStyle,
            WindowClassName,
            "FreeScreenRecorder Overlay",
            NativeMethods.WS_POPUP,
            0,
            0,
            Width,
            Height,
            IntPtr.Zero,
            IntPtr.Zero,
            NativeMethods.GetModuleHandleW(null),
            IntPtr.Zero);
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

            if (NativeMethods.RegisterClassExW(ref windowClass) == 0)
            {
                return;
            }

            _classRegistered = true;
        }
    }

    private static IntPtr StaticWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case NativeMethods.WM_NCHITTEST:
                return new IntPtr(NativeMethods.HTTRANSPARENT);

            case NativeMethods.WM_MOUSEACTIVATE:
                return new IntPtr(NativeMethods.MA_NOACTIVATE);

            case NativeMethods.WM_DESTROY:
                return IntPtr.Zero;
        }

        return NativeMethods.DefWindowProcW(hwnd, msg, wParam, lParam);
    }
}
