using System.Runtime.InteropServices;

namespace PoproshaykaBot.Wpf.Infrastructure.Diagnostics;

public sealed record DisplayMonitorInfo(
    int Index,
    bool IsPrimary,
    int Left,
    int Top,
    int Width,
    int Height,
    uint? DpiX,
    uint? DpiY);

internal static class DisplayMonitorProbe
{
    private const uint MonitorInfoFlagPrimary = 1;
    private const int MdtEffectiveDpi = 0;

    public static IReadOnlyList<DisplayMonitorInfo> Capture()
    {
        var result = new List<DisplayMonitorInfo>();

        try
        {
            var index = 0;

            bool Callback(IntPtr hMonitor, IntPtr hdcMonitor, ref Rect lprcMonitor, IntPtr dwData)
            {
                index++;
                var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };

                if (!GetMonitorInfoW(hMonitor, ref info))
                {
                    return true;
                }

                uint? dpiX = null;
                uint? dpiY = null;

                try
                {
                    if (GetDpiForMonitor(hMonitor, MdtEffectiveDpi, out var x, out var y) == 0 && x > 0 && y > 0)
                    {
                        dpiX = x;
                        dpiY = y;
                    }
                }
                catch (Exception)
                {
                    dpiX = null;
                    dpiY = null;
                }

                var rect = info.Monitor;
                result.Add(new(
                    index,
                    (info.Flags & MonitorInfoFlagPrimary) != 0,
                    rect.Left,
                    rect.Top,
                    rect.Right - rect.Left,
                    rect.Bottom - rect.Top,
                    dpiX,
                    dpiY));

                return true;
            }

            _ = EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, Callback, IntPtr.Zero);
        }
        catch (Exception)
        {
            return result;
        }

        return result;
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref Rect lprcMonitor, IntPtr dwData);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MonitorInfo lpmi);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }
}
