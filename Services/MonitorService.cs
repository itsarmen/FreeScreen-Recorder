using FreeScreenRecorder.Interop;
using FreeScreenRecorder.Models;
using Vortice.DXGI;

namespace FreeScreenRecorder.Services;

public sealed class MonitorService
{
    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var gdiMonitors = new List<(string DeviceName, NativeMethods.RECT Bounds, bool IsPrimary)>();

        NativeMethods.MonitorEnumProc callback = (IntPtr hMonitor, IntPtr hdc, ref NativeMethods.RECT rect, IntPtr data) =>
        {
            var info = new NativeMethods.MONITORINFOEX
            {
                cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFOEX>(),
                szDevice = string.Empty
            };

            if (NativeMethods.GetMonitorInfoW(hMonitor, ref info))
            {
                gdiMonitors.Add((info.szDevice, info.rcMonitor, (info.dwFlags & 1) != 0));
            }

            return true;
        };

        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);

        var dxgiOutputs = GetDxgiOutputs();
        var monitors = new List<MonitorInfo>();
        var displayNumber = 1;

        foreach (var (deviceName, bounds, isPrimary) in gdiMonitors)
        {
            var dxgi = dxgiOutputs.FirstOrDefault(o =>
                string.Equals(o.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase));

            var width = bounds.Right - bounds.Left;
            var height = bounds.Bottom - bounds.Top;
            var index = dxgi.Index >= 0 ? dxgi.Index : displayNumber - 1;
            var suffix = isPrimary ? " - Primary" : string.Empty;

            monitors.Add(new MonitorInfo(
                deviceName,
                $"Display {displayNumber}{suffix}  -  {width} x {height}",
                bounds.Left,
                bounds.Top,
                width,
                height,
                isPrimary,
                index));

            displayNumber++;
        }

        if (monitors.Count == 0)
        {
            monitors.Add(new MonitorInfo(
                "\\\\.\\DISPLAY1",
                "Display 1 - Primary  -  1920 x 1080",
                0,
                0,
                1920,
                1080,
                true,
                0));
        }

        return monitors;
    }

    private static List<(int Index, string DeviceName)> GetDxgiOutputs()
    {
        var outputs = new List<(int Index, string DeviceName)>();

        try
        {
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            var outputIndex = 0;

            for (uint adapterIndex = 0; ; adapterIndex++)
            {
                var adapterResult = factory.EnumAdapters1(adapterIndex, out var adapter);
                if (adapterResult.Failure)
                {
                    break;
                }

                using (adapter)
                {
                    for (uint outputNumber = 0; ; outputNumber++)
                    {
                        var outputResult = adapter.EnumOutputs(outputNumber, out var output);
                        if (outputResult.Failure)
                        {
                            break;
                        }

                        using (output)
                        {
                            var description = output.Description;
                            outputs.Add((outputIndex++, description.DeviceName));
                        }
                    }
                }
            }
        }
        catch
        {
            // DXGI enumeration is a best-effort mapping. GDI order is used as a fallback.
        }

        return outputs;
    }
}
