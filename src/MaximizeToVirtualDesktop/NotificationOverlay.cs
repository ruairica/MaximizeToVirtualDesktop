using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace MaximizeToVirtualDesktop;

/// <summary>
/// Translucent overlay notification that replaces balloon tips.
/// Shows centered on the active monitor with Win11 Mica backdrop,
/// fades out after 1.5 seconds. Click-through and non-activating.
/// </summary>
internal sealed class NotificationOverlay : Form
{
    private const int ShowDurationMs = 1500;
    private const int FadeStepMs = 20;
    private const double FadeStepAmount = 0.06;
    private const double InitialOpacity = 0.92;
    private const int PaddingH = 32;
    private const int MinWidth = 300;
    private const int MaxWidth = 520;

    private readonly System.Windows.Forms.Timer _hideTimer;
    private readonly System.Windows.Forms.Timer _fadeTimer;
    private string _title = "";
    private string _subtitle = "";

    private static NotificationOverlay? _instance;

    private NotificationOverlay()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(30, 30, 46);
        ForeColor = Color.White;
        DoubleBuffered = true;
        Size = new Size(MinWidth, 80);

        _fadeTimer = new System.Windows.Forms.Timer { Interval = FadeStepMs };
        _hideTimer = new System.Windows.Forms.Timer { Interval = ShowDurationMs };

        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            _fadeTimer.Start();
        };

        _fadeTimer.Tick += (_, _) =>
        {
            if (Opacity <= FadeStepAmount)
            {
                _fadeTimer.Stop();
                Hide();
                Opacity = InitialOpacity;
            }
            else
            {
                Opacity -= FadeStepAmount;
            }
        };
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x00000080;  // WS_EX_TOOLWINDOW — hide from Alt+Tab
            cp.ExStyle |= 0x08000000;  // WS_EX_NOACTIVATE — don't steal focus
            cp.ExStyle |= 0x00000020;  // WS_EX_TRANSPARENT — click-through
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        // Win11 rounded corners
        int cornerPref = 2; // DWMWCP_ROUND
        DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPref, sizeof(int));

        // Dark mode (affects DWM border and backdrop tint)
        int darkMode = 1;
        DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));

        // Try Mica Alt backdrop — falls back gracefully to solid BackColor
        int backdropType = 4; // DWMSBT_TABBEDWINDOW (Mica Alt)
        if (DwmSetWindowAttribute(Handle, DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, sizeof(int)) == 0)
        {
            // Extend frame so backdrop fills client area
            var margins = new MARGINS(-1);
            DwmExtendFrameIntoClientArea(Handle, ref margins);
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // Dark fill — if Mica is active, DWM composites beneath this
        using var brush = new SolidBrush(BackColor);
        e.Graphics.FillRectangle(brush, ClientRectangle);
    }

    // Prefer Segoe UI Variable Display (Win11 native), fall back to Segoe UI
    private static readonly string FontFamily = IsFontInstalled("Segoe UI Variable Display")
        ? "Segoe UI Variable Display"
        : "Segoe UI";

    private static bool IsFontInstalled(string name)
    {
        using var f = new Font(name, 10f);
        return f.Name.Equals(name, StringComparison.OrdinalIgnoreCase);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using var titleFont = new Font(FontFamily, 15f, FontStyle.Bold);
        using var subtitleFont = new Font(FontFamily, 10.5f);

        var hasSubtitle = !string.IsNullOrEmpty(_subtitle);
        var maxTextWidth = Width - PaddingH * 2;
        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                  | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;

        var titleSize = TextRenderer.MeasureText(g, _title, titleFont);
        var subtitleSize = hasSubtitle
            ? TextRenderer.MeasureText(g, _subtitle, subtitleFont)
            : Size.Empty;

        int totalHeight = titleSize.Height + (hasSubtitle ? subtitleSize.Height + 2 : 0);
        int y = (Height - totalHeight) / 2;

        TextRenderer.DrawText(g, _title, titleFont,
            new Rectangle(PaddingH, y, maxTextWidth, titleSize.Height),
            Color.White, flags);

        if (hasSubtitle)
        {
            TextRenderer.DrawText(g, _subtitle, subtitleFont,
                new Rectangle(PaddingH, y + titleSize.Height + 2, maxTextWidth, subtitleSize.Height),
                Color.FromArgb(180, 180, 180), flags);
        }
    }

    /// <summary>
    /// Show a notification overlay centered on the monitor containing the given window (or cursor).
    /// </summary>
    public static void ShowNotification(string title, string subtitle = "", IntPtr hwnd = default)
    {
        if (_instance == null || _instance.IsDisposed)
            _instance = new NotificationOverlay();

        _instance._title = title;
        _instance._subtitle = subtitle;
        _instance.FitToContent(title, subtitle);
        _instance.PositionOnScreen(hwnd);
        _instance.Opacity = InitialOpacity;
        _instance._fadeTimer.Stop();
        _instance._hideTimer.Stop();
        _instance.Invalidate();
        _instance.Visible = true;
        _instance._hideTimer.Start();
    }

    private void FitToContent(string title, string subtitle)
    {
        using var g = CreateGraphics();
        using var titleFont = new Font(FontFamily, 15f, FontStyle.Bold);
        using var subtitleFont = new Font(FontFamily, 10.5f);

        var titleWidth = TextRenderer.MeasureText(g, title, titleFont).Width;
        var subtitleWidth = string.IsNullOrEmpty(subtitle)
            ? 0
            : TextRenderer.MeasureText(g, subtitle, subtitleFont).Width;

        int needed = Math.Max(titleWidth, subtitleWidth) + PaddingH * 2;
        Width = Math.Clamp(needed, MinWidth, MaxWidth);
    }

    private void PositionOnScreen(IntPtr hwnd)
    {
        var screen = hwnd != IntPtr.Zero
            ? Screen.FromHandle(hwnd)
            : Screen.FromPoint(Cursor.Position);

        // Upper third of screen — visible but not blocking center content
        Location = new Point(
            screen.WorkingArea.Left + (screen.WorkingArea.Width - Width) / 2,
            screen.WorkingArea.Top + screen.WorkingArea.Height / 4 - Height / 2);
    }

    // --- DWM interop (overlay-specific, not shared with NativeMethods) ---

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS
    {
        public int Left, Right, Top, Bottom;
        public MARGINS(int all) => Left = Right = Top = Bottom = all;
    }
}
