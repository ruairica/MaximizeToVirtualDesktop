using System.Runtime.InteropServices;
using MaximizeToVirtualDesktop.Interop;

namespace DevDesk;

static class WindowSnapper
{
    public static void SnapLeft(IntPtr hwnd)
    {
        var workArea = GetWorkArea(hwnd);
        int halfWidth = (workArea.Right - workArea.Left) / 2;
        int height = workArea.Bottom - workArea.Top;

        // Restore if maximized — SetWindowPos doesn't work well on maximized windows
        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
        Thread.Sleep(50);

        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero,
            workArea.Left, workArea.Top,
            halfWidth, height,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
    }

    public static void SnapRight(IntPtr hwnd)
    {
        var workArea = GetWorkArea(hwnd);
        int halfWidth = (workArea.Right - workArea.Left) / 2;
        int height = workArea.Bottom - workArea.Top;

        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
        Thread.Sleep(50);

        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero,
            workArea.Left + halfWidth, workArea.Top,
            halfWidth, height,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
    }

    public static void MaximizeBehind(IntPtr hwnd)
    {
        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_MAXIMIZE);
        Thread.Sleep(50);

        // Push behind other windows
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_BOTTOM,
            0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    private static NativeMethods.RECT GetWorkArea(IntPtr hwnd)
    {
        var monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTOPRIMARY);
        var info = new NativeMethods.MONITORINFO
        {
            cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>()
        };
        NativeMethods.GetMonitorInfo(monitor, ref info);
        return info.rcWork;
    }
}
