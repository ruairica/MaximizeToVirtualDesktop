using System.Windows;
using System.Windows.Interop;
using MaximizeToVirtualDesktop.Interop;

namespace DeskSwitch;

public partial class App : Application
{
    private const int HOTKEY_ID = 0x10;
    private const uint MOD_CTRL_ALT = NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT;
    private const uint VK_SPACE = 0x20;

    private HwndSource? _hwndSource;
    private VirtualDesktopService? _vds;
    private MainWindow? _overlay;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Check Windows version
        int build = GetWindowsBuildNumber();
        if (build < 22000)
        {
            MessageBox.Show("DeskSwitch requires Windows 11 (build 22000+).",
                "DeskSwitch", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        // Initialize virtual desktop COM
        _vds = new VirtualDesktopService();
        if (!_vds.Initialize(build))
        {
            MessageBox.Show("Failed to initialize virtual desktop COM APIs.",
                "DeskSwitch", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        // Create hidden window for hotkey messages
        var parameters = new HwndSourceParameters("DeskSwitchHotkey")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0x800000 // WS_BORDER to make it message-only compatible
        };
        _hwndSource = new HwndSource(parameters);
        _hwndSource.AddHook(WndProc);

        // Register Ctrl+Alt+Space
        if (!NativeMethods.RegisterHotKey(_hwndSource.Handle, HOTKEY_ID, MOD_CTRL_ALT, VK_SPACE))
        {
            MessageBox.Show("Failed to register Ctrl+Alt+Space hotkey.\nAnother app may have it registered.",
                "DeskSwitch", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        _overlay = new MainWindow(_vds);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            handled = true;
            ToggleOverlay();
        }
        return IntPtr.Zero;
    }

    private void ToggleOverlay()
    {
        if (_overlay == null) return;

        if (_overlay.IsVisible)
        {
            _overlay.HideOverlay();
        }
        else
        {
            _overlay.ShowOverlay();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_hwndSource != null)
        {
            NativeMethods.UnregisterHotKey(_hwndSource.Handle, HOTKEY_ID);
            _hwndSource.RemoveHook(WndProc);
            _hwndSource.Dispose();
        }
        _vds?.Dispose();
        base.OnExit(e);
    }

    private static int GetWindowsBuildNumber()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var build = key?.GetValue("CurrentBuildNumber")?.ToString();
            return int.TryParse(build, out var num) ? num : 0;
        }
        catch
        {
            return 0;
        }
    }
}
