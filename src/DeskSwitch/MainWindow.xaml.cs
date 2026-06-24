using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using WindowsVirtualDesktop.Interop;

namespace DeskSwitch;

public partial class MainWindow : Window
{
    private readonly VirtualDesktopService _vds;
    private List<DesktopItem> _allDesktops = [];
    private Guid _currentDesktopId;
    private bool _closingFromAction;
    private bool _renaming;
    private Guid _renamingId;

    public MainWindow(VirtualDesktopService vds)
    {
        _vds = vds;
        InitializeComponent();
    }

    public void ShowOverlay()
    {
        _renaming = false;
        PlaceholderText.Text = "Search desktops...";
        RefreshDesktopList();
        SearchBox.Text = "";
        Show();
        PositionAtCursor();
        Activate();
        SearchBox.Focus();
    }

    /// <summary>
    /// Centres the overlay on the monitor that currently contains the mouse
    /// cursor. Worked out in physical pixels via SetWindowPos so it is correct
    /// regardless of per-monitor DPI scaling. Must run after Show() so the
    /// window has a handle and SizeToContent has measured its height.
    /// </summary>
    private void PositionAtCursor()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        if (!NativeMethods.GetCursorPos(out var pt)) return;

        var hMonitor = NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var mi = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(hMonitor, ref mi)) return;

        if (!NativeMethods.GetWindowRect(hwnd, out var wr)) return;
        int winWidth = wr.Right - wr.Left;
        int winHeight = wr.Bottom - wr.Top;

        var work = mi.rcWork;
        int x = work.Left + ((work.Right - work.Left) - winWidth) / 2;
        int y = work.Top + ((work.Bottom - work.Top) - winHeight) / 2;

        NativeMethods.SetWindowPos(
            hwnd, IntPtr.Zero, x, y, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
    }

    public void HideOverlay()
    {
        _renaming = false;
        Hide();
    }

    private void RefreshDesktopList()
    {
        _currentDesktopId = _vds.GetCurrentDesktopId() ?? Guid.Empty;
        var desktops = _vds.GetAllDesktops();

        _allDesktops = desktops.Select((d, i) => new DesktopItem
        {
            Id = d.id,
            DisplayName = string.IsNullOrWhiteSpace(d.name) ? $"Desktop {i + 1}" : d.name,
            IsCurrent = d.id == _currentDesktopId,
        }).ToList();

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (_renaming) return;

        var query = SearchBox.Text.Trim();
        List<DesktopItem> filtered;

        if (string.IsNullOrEmpty(query))
        {
            filtered = _allDesktops.ToList();
        }
        else
        {
            filtered = _allDesktops
                .Select(d => (item: d, score: FuzzyMatcher.Score(d.DisplayName, query)))
                .Where(x => x.score >= 0)
                .OrderByDescending(x => x.score)
                .Select(x => x.item)
                .ToList();
        }

        DesktopList.ItemsSource = filtered;

        // Select current desktop if visible, otherwise first item
        var current = filtered.FirstOrDefault(d => d.IsCurrent);
        DesktopList.SelectedItem = current ?? filtered.FirstOrDefault();

        // Update placeholder visibility
        PlaceholderText.Visibility = string.IsNullOrEmpty(SearchBox.Text)
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SwitchToSelected()
    {
        // "+name" creates a new desktop with that name
        var query = SearchBox.Text.Trim();
        if (query.StartsWith('+') && query.Length > 1)
        {
            CreateNewDesktop(query[1..].Trim());
            return;
        }

        if (DesktopList.SelectedItem is not DesktopItem item) return;
        if (item.Id == _currentDesktopId)
        {
            HideOverlay();
            return;
        }

        var desktop = _vds.FindDesktop(item.Id);
        if (desktop == null) return;

        try
        {
            _closingFromAction = true;
            HideOverlay();
            _vds.SwitchToDesktop(desktop);
        }
        finally
        {
            Marshal.ReleaseComObject(desktop);
            _closingFromAction = false;
        }
    }

    private void RemoveSelected()
    {
        if (DesktopList.SelectedItem is not DesktopItem item) return;
        if (_allDesktops.Count <= 1) return; // can't remove last desktop

        bool removingCurrent = item.Id == _currentDesktopId;
        int prevIndex = DesktopList.SelectedIndex;

        var desktop = _vds.FindDesktop(item.Id);
        if (desktop == null) return;

        try
        {
            _vds.CloseWindowsOnDesktop(item.Id);
            _vds.RemoveDesktop(desktop);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"DeskSwitch: RemoveSelected failed: {ex.Message}");
        }
        finally
        {
            Marshal.ReleaseComObject(desktop);
        }

        if (removingCurrent)
        {
            // Removing the current desktop switches to an adjacent one,
            // which deactivates the overlay anyway — hide it cleanly.
            _closingFromAction = true;
            HideOverlay();
            _closingFromAction = false;
            return;
        }

        // Stay open so several desktops can be deleted in a row. Keep the
        // cursor at the same position rather than jumping back to the
        // current desktop.
        RefreshDesktopList();
        if (DesktopList.Items.Count > 0)
        {
            DesktopList.SelectedIndex = Math.Min(prevIndex, DesktopList.Items.Count - 1);
            DesktopList.ScrollIntoView(DesktopList.SelectedItem);
        }
    }

    private void CreateNewDesktop(string? name = null)
    {
        var (desktop, id) = _vds.CreateDesktop();
        if (desktop == null) return;

        try
        {
            if (!string.IsNullOrWhiteSpace(name))
                _vds.SetDesktopName(desktop, name);

            _closingFromAction = true;
            HideOverlay();
            _vds.SwitchToDesktop(desktop);
        }
        finally
        {
            Marshal.ReleaseComObject(desktop);
            _closingFromAction = false;
        }
    }

    private void StartRename()
    {
        if (DesktopList.SelectedItem is not DesktopItem item) return;

        _renaming = true;
        _renamingId = item.Id;
        PlaceholderText.Visibility = Visibility.Collapsed;
        SearchBox.Text = item.DisplayName;
        SearchBox.SelectAll();
    }

    private void CommitRename()
    {
        var newName = SearchBox.Text.Trim();
        if (string.IsNullOrEmpty(newName))
        {
            CancelRename();
            return;
        }

        var desktop = _vds.FindDesktop(_renamingId);
        if (desktop != null)
        {
            try
            {
                _vds.SetDesktopName(desktop, newName);
            }
            finally
            {
                Marshal.ReleaseComObject(desktop);
            }
        }

        _renaming = false;
        PlaceholderText.Text = "Search desktops...";
        SearchBox.Text = "";
        RefreshDesktopList();
    }

    private void CancelRename()
    {
        _renaming = false;
        PlaceholderText.Text = "Search desktops...";
        SearchBox.Text = "";
        ApplyFilter();
    }

    // --- Event handlers ---

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilter();
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                if (_renaming)
                    CommitRename();
                else
                    SwitchToSelected();
                break;

            case Key.Escape:
                e.Handled = true;
                if (_renaming)
                    CancelRename();
                else
                    HideOverlay();
                break;

            case Key.F2:
                e.Handled = true;
                if (!_renaming)
                    StartRename();
                break;

            case Key.Delete:
                if (!_renaming && string.IsNullOrEmpty(SearchBox.Text))
                {
                    e.Handled = true;
                    RemoveSelected();
                }
                break;

            case Key.Down:
            case Key.Up:
                if (_renaming) break;
                e.Handled = true;
                MoveSelection(e.Key == Key.Down ? 1 : -1);
                break;

            case Key.Tab:
                if (_renaming) break;
                e.Handled = true;
                if (Keyboard.Modifiers == ModifierKeys.Shift)
                    MoveSelection(-1);
                else
                    MoveSelection(1);
                break;
        }
    }

    private void MoveSelection(int delta)
    {
        if (DesktopList.Items.Count == 0) return;
        int newIndex = DesktopList.SelectedIndex + delta;
        if (newIndex < 0) newIndex = DesktopList.Items.Count - 1;
        if (newIndex >= DesktopList.Items.Count) newIndex = 0;
        DesktopList.SelectedIndex = newIndex;
        DesktopList.ScrollIntoView(DesktopList.SelectedItem);
    }

    private void DesktopList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Keep focus on search box
    }

    private void DesktopList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        SwitchToSelected();
    }

    private void NewDesktopButton_Click(object sender, RoutedEventArgs e)
    {
        CreateNewDesktop();
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (!_closingFromAction && IsVisible)
        {
            HideOverlay();
        }
    }
}

class DesktopItem
{
    public Guid Id { get; init; }
    public string DisplayName { get; init; } = "";
    public bool IsCurrent { get; init; }
    public Visibility CurrentVisibility => IsCurrent ? Visibility.Visible : Visibility.Collapsed;
}
