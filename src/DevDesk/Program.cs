using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using MaximizeToVirtualDesktop.Interop;

namespace DevDesk;

static class Program
{
    private const string TerminalWindowClass = "CASCADIA_HOSTING_WINDOW_CLASS";
    private const string ElectronWindowClass = "Chrome_WidgetWin_1";

    [STAThread]
    static int Main(string[] args)
    {
        string dir = Directory.GetCurrentDirectory();
        string desktopName = args.Length > 0 ? args[0] : Path.GetFileName(dir);

        Console.WriteLine($"Creating dev desktop: {desktopName}");

        // 1. Check Windows version
        int build = GetWindowsBuildNumber();
        if (build < 22000)
        {
            Console.Error.WriteLine("Error: Requires Windows 11 (build 22000+).");
            return 1;
        }

        // 2. Initialize COM
        using var vds = new VirtualDesktopService();
        if (!vds.Initialize(build))
        {
            Console.Error.WriteLine("Error: Failed to initialize virtual desktop COM APIs.");
            return 1;
        }

        // 3. Snapshot existing windows before launch
        var existingTerminals = GetWindowsByClass(TerminalWindowClass);
        var existingCodeWindows = GetCodeWindows();
        var existingChromeWindows = GetWindowsByClassAndProcess(ElectronWindowClass, "chrome");

        // 4. Create and switch to new desktop
        var (desktop, desktopId) = vds.CreateDesktop();
        if (desktop == null)
        {
            Console.Error.WriteLine("Error: Failed to create virtual desktop.");
            return 2;
        }

        try
        {
            vds.SetDesktopName(desktop, desktopName);
            vds.SwitchToDesktop(desktop);
        }
        finally
        {
            Marshal.ReleaseComObject(desktop);
        }

        Console.WriteLine("Switched to new desktop.");

        // 5. Launch Windows Terminal with copilot split pane
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "wt.exe",
                Arguments = $"-d \"{dir}\" ; split-pane -H -d \"{dir}\" pwsh -NoLogo -NoExit -Command copilot",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: Failed to launch Windows Terminal: {ex.Message}");
        }

        // 6. Launch VS Code
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c code \"{dir}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: Failed to launch VS Code: {ex.Message}");
        }

        // 6b. Launch Chrome
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "chrome.exe",
                Arguments = "--new-window",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: Failed to launch Chrome: {ex.Message}");
        }

        // 7. Wait for windows and snap them
        Console.Write("Waiting for windows...");

        var termHwnd = WaitForNewWindow(
            TerminalWindowClass, null, existingTerminals, TimeSpan.FromSeconds(15));
        var codeHwnd = WaitForNewWindow(
            ElectronWindowClass, "Code", existingCodeWindows, TimeSpan.FromSeconds(15));
        var chromeHwnd = WaitForNewWindow(
            ElectronWindowClass, "chrome", existingChromeWindows, TimeSpan.FromSeconds(15));

        Console.WriteLine();

        // Maximize Chrome behind first so it's at the back
        if (chromeHwnd != IntPtr.Zero)
            WindowSnapper.MaximizeBehind(chromeHwnd);
        else
            Console.Error.WriteLine("Warning: Chrome window not detected within timeout.");

        if (termHwnd != IntPtr.Zero)
            WindowSnapper.SnapLeft(termHwnd);
        else
            Console.Error.WriteLine("Warning: Terminal window not detected within timeout.");

        if (codeHwnd != IntPtr.Zero)
            WindowSnapper.SnapRight(codeHwnd);
        else
            Console.Error.WriteLine("Warning: VS Code window not detected within timeout.");

        if (termHwnd != IntPtr.Zero && codeHwnd != IntPtr.Zero)
            Console.WriteLine("Done — Terminal (left) + VS Code (right) + Chrome (behind).");

        return 0;
    }

    /// <summary>
    /// Collects all visible window handles with the given class name.
    /// </summary>
    private static HashSet<IntPtr> GetWindowsByClass(string className)
    {
        var result = new HashSet<IntPtr>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (NativeMethods.IsWindowVisible(hwnd) && GetWindowClassName(hwnd) == className)
                result.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        return result;
    }

    /// <summary>
    /// Collects all visible windows matching a class name and process name.
    /// </summary>
    private static HashSet<IntPtr> GetWindowsByClassAndProcess(string className, string processNameContains)
    {
        var result = new HashSet<IntPtr>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (NativeMethods.IsWindowVisible(hwnd)
                && GetWindowClassName(hwnd) == className
                && IsProcessMatch(hwnd, processNameContains))
                result.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        return result;
    }

    /// <summary>
    /// Collects all visible VS Code windows (Electron class + Code process).
    /// </summary>
    private static HashSet<IntPtr> GetCodeWindows()
    {
        var result = new HashSet<IntPtr>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (NativeMethods.IsWindowVisible(hwnd)
                && GetWindowClassName(hwnd) == ElectronWindowClass
                && IsCodeProcess(hwnd))
                result.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        return result;
    }

    /// <summary>
    /// Polls for a new window matching the class (and optionally process name) that wasn't in the snapshot.
    /// </summary>
    private static IntPtr WaitForNewWindow(
        string className, string? processNameContains,
        HashSet<IntPtr> existingWindows, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            IntPtr found = IntPtr.Zero;
            NativeMethods.EnumWindows((hwnd, _) =>
            {
                if (!existingWindows.Contains(hwnd)
                    && NativeMethods.IsWindowVisible(hwnd)
                    && NativeMethods.GetWindowTextLength(hwnd) > 0
                    && GetWindowClassName(hwnd) == className)
                {
                    if (processNameContains == null || IsProcessMatch(hwnd, processNameContains))
                    {
                        found = hwnd;
                        return false; // stop enumeration
                    }
                }
                return true;
            }, IntPtr.Zero);

            if (found != IntPtr.Zero) return found;
            Thread.Sleep(150);
            Console.Write(".");
        }
        return IntPtr.Zero;
    }

    private static string? GetWindowClassName(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        int len = NativeMethods.GetClassName(hwnd, sb, sb.Capacity);
        return len > 0 ? sb.ToString() : null;
    }

    private static bool IsCodeProcess(IntPtr hwnd)
    {
        return IsProcessMatch(hwnd, "Code");
    }

    private static bool IsProcessMatch(IntPtr hwnd, string nameContains)
    {
        try
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out int pid);
            if (pid == 0) return false;
            using var proc = Process.GetProcessById(pid);
            return proc.ProcessName.Contains(nameContains, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
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
