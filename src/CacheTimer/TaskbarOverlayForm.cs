using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using System.Windows.Forms;

namespace CodexCacheTimer;

internal sealed class TaskbarOverlayForm : Form
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindow(string className, string? windowName);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint window, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint window, uint command);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint window, StringBuilder className, int capacity);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(nint window, nint insertAfter,
        int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint window, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint window, int index, nint value);

    [DllImport("user32.dll")]
    public static extern bool SetProcessDpiAwarenessContext(nint context);

    private const int GwlpHwndParent = -8;
    private const int GwlExstyle = -20;
    private const uint GwOwner = 4;
    private const uint GaRoot = 2;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const int WsExToolwindow = 0x00000080;
    private const int WsExAppwindow = 0x00040000;
    private const int WmMouseActivate = 0x0021;
    private const int MaNoActivate = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }

    private readonly Font timerFont = new("Segoe UI Semibold", 11f, FontStyle.Bold);
    private string displayTime = "";
    private Color displayColor = Color.LightGray;
    private long paintCount;
    private DateTimeOffset lastPaint;
    private nint taskbarOwner;
    private string lastPositionSource = "";
    private bool reportedMissingTaskbar;
    private Func<bool>? popupIsOpen;

    public event EventHandler? OpenRequested;
    public void SetPopupStateProvider(Func<bool> provider) => popupIsOpen = provider;

    public TaskbarOverlayForm()
    {
        Text = "Codex cache timer";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        // WinForms creates a hidden owner when this is false; the tool-window
        // style below already keeps this window off the taskbar and Alt+Tab.
        ShowInTaskbar = true;
        TopMost = true;
        Size = new Size(126, 36);
        BackColor = Color.FromArgb(30, 33, 38);
        DoubleBuffered = true;
        Cursor = Cursors.Hand;
        AccessibleName = "Codex cache timer";

        MouseUp += OnMouseUp;
        HandleCreated += (_, _) => DiagnosticLog.Info("overlay-handle-created", DescribeWindow());
        HandleDestroyed += (_, _) => DiagnosticLog.Warn("overlay-handle-destroyed", DescribeWindow());
        Shown += (_, _) =>
        {
            DiagnosticLog.Info("overlay-shown", DescribeWindow());
            BeginInvoke((Action)(() =>
            {
                AttachToTaskbar();
                UpdatePosition();
                DiagnosticLog.Info("overlay-after-shown", DescribeWindow());
            }));
        };
        VisibleChanged += (_, _) => DiagnosticLog.Info("overlay-visible-changed", DescribeWindow());
        Activated += (_, _) => DiagnosticLog.Info("overlay-activated", DescribeWindow());
        Deactivate += (_, _) => DiagnosticLog.Info("overlay-deactivated", DescribeWindow());
        FormClosing += (_, e) => DiagnosticLog.Warn("overlay-closing", $"reason={e.CloseReason} {DescribeWindow()}");
        FormClosed += (_, e) => DiagnosticLog.Warn("overlay-closed", $"reason={e.CloseReason} {DescribeWindow()}");
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            var taskbar = FindWindow("Shell_TrayWnd", null);
            if (taskbar != nint.Zero) parameters.Parent = taskbar;
            parameters.ExStyle |= WsExToolwindow;
            parameters.ExStyle &= ~WsExAppwindow;
            return parameters;
        }
    }

    protected override void WndProc(ref Message message)
    {
        // Keep the popup active when its taskbar timer is clicked. Windows still
        // delivers the mouse click, so the timer can handle it without a hide/show.
        if (message.Msg == WmMouseActivate && popupIsOpen?.Invoke() == true)
        {
            message.Result = new nint(MaNoActivate);
            DiagnosticLog.Info("overlay-click-noactivate");
            return;
        }
        base.WndProc(ref message);
    }

    private void OnMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) OpenRequested?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        using (var brush = new SolidBrush(displayColor))
            e.Graphics.FillEllipse(brush, displayTime.Length == 0 ? (Width - 10) / 2 : 12,
                (Height - 10) / 2, 10, 10);
        if (displayTime.Length > 0)
            TextRenderer.DrawText(e.Graphics, displayTime, timerFont,
                new Rectangle(34, 0, Width - 38, Height), displayColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        paintCount++;
        lastPaint = DateTimeOffset.UtcNow;
        base.OnPaint(e);
    }

    public void UpdateDisplay(CodexSession? session, TimerSettings settings, DateTimeOffset now)
    {
        var text = TimerState.OverlayText(session, settings, now);
        displayTime = text.StartsWith("●  ", StringComparison.Ordinal) ? text[3..] : "";
        displayColor = TimerState.Color(session, settings, now);
        var width = session is null ? 32 : 126;
        if (Width != width)
        {
            Width = width;
            UpdatePosition();
        }
        Invalidate();
    }

    public void UpdatePosition()
    {
        if (IsDisposed) return;
        var taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar == nint.Zero)
        {
            if (!reportedMissingTaskbar)
                DiagnosticLog.Warn("taskbar-missing", DescribeWindow());
            reportedMissingTaskbar = true;
            return;
        }
        reportedMissingTaskbar = false;
        if (IsHandleCreated && GetWindow(Handle, GwOwner) != taskbar) AttachToTaskbar();
        if (!GetWindowRect(taskbar, out var rect)) return;

        var chevronLeft = FindChevronLeft(taskbar);
        int rightEdge = chevronLeft ?? rect.Right - 480;
        var source = chevronLeft.HasValue ? "UIA-chevron" : "fallback-480";
        var target = new Point(rightEdge - Width - 8,
            rect.Top + Math.Max(0, (rect.Bottom - rect.Top - Height) / 2));
        if (Location != target || lastPositionSource != source)
        {
            DiagnosticLog.Info("overlay-position", $"source={source} taskbar={taskbar} "
                + $"taskbarRect={rect.Left},{rect.Top},{rect.Right},{rect.Bottom} "
                + $"chevronLeft={chevronLeft?.ToString() ?? "none"} old={Location} target={target} width={Width}");
            if (Location != target)
            {
                Location = target;
                RestoreIfTaskbarCovers();
            }
            lastPositionSource = source;
        }
    }

    private void AttachToTaskbar()
    {
        if (!IsHandleCreated || IsDisposed) return;
        var taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar == nint.Zero) return;
        var before = GetWindow(Handle, GwOwner);
        nint previous = nint.Zero;
        int error = 0;
        if (before != taskbar)
        {
            previous = SetWindowLongPtr(Handle, GwlpHwndParent, taskbar);
            error = Marshal.GetLastWin32Error();
        }
        if (GetWindow(Handle, GwOwner) == taskbar)
            taskbarOwner = taskbar;
        DiagnosticLog.Info("overlay-owner", $"requested={taskbar} before={before} previous={previous} "
            + $"after={GetWindow(Handle, GwOwner)} win32={error} {DescribeWindow()}");
    }

    public string DescribeWindow()
    {
        if (!IsHandleCreated || IsDisposed)
            return $"handle=none formVisible={Visible} disposed={IsDisposed} location={Location} size={Size} time='{displayTime}'";
        GetWindowRect(Handle, out var rect);
        return $"handle={Handle} owner={GetWindow(Handle, GwOwner)} expectedOwner={taskbarOwner} "
            + $"nativeVisible={IsWindowVisible(Handle)} formVisible={Visible} "
            + $"exstyle=0x{GetWindowLongPtr(Handle, GwlExstyle).ToInt64():X} "
            + $"rect={rect.Left},{rect.Top},{rect.Right},{rect.Bottom} size={Size} time='{displayTime}' "
            + $"paintCount={paintCount} lastPaint={lastPaint:O}";
    }

    public string HitTestCenter()
    {
        if (!IsHandleCreated || IsDisposed || !GetWindowRect(Handle, out var rect))
            return "unavailable";
        var hit = WindowFromPoint(new NativePoint
        {
            X = (rect.Left + rect.Right) / 2,
            Y = (rect.Top + rect.Bottom) / 2,
        });
        if (hit == Handle) return "self";
        GetWindowThreadProcessId(hit, out var processId);
        var className = new StringBuilder(128);
        GetClassName(hit, className, className.Capacity);
        if (processId == Environment.ProcessId
            && className.ToString().Contains("tooltips_class32", StringComparison.OrdinalIgnoreCase))
            return "self";
        return $"other hwnd={hit} pid={processId} class={className}";
    }

    public bool RestoreIfTaskbarCovers()
    {
        if (!IsHandleCreated || IsDisposed || !GetWindowRect(Handle, out var rect)) return false;
        var hit = WindowFromPoint(new NativePoint
        {
            X = (rect.Left + rect.Right) / 2,
            Y = (rect.Top + rect.Bottom) / 2,
        });
        var taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar == nint.Zero || GetAncestor(hit, GaRoot) != taskbar) return false;
        RaiseAboveTaskbar("taskbar-covered");
        return true;
    }

    private void RaiseAboveTaskbar(string reason)
    {
        if (!IsHandleCreated || !Visible) return;
        var succeeded = SetWindowPos(Handle, new nint(-1), 0, 0, 0, 0,
            SwpNoSize | SwpNoMove | SwpNoActivate);
        DiagnosticLog.Info("overlay-raise", $"reason={reason} success={succeeded} "
            + $"win32={Marshal.GetLastWin32Error()} hit={HitTestCenter()} {DescribeWindow()}");
    }

    private static int? FindChevronLeft(nint taskbarHandle)
    {
        try
        {
            var taskbar = AutomationElement.FromHandle(taskbarHandle);
            var buttons = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button);
            foreach (AutomationElement button in taskbar.FindAll(TreeScope.Descendants, buttons))
            {
                var name = button.Current.Name.ToLowerInvariant();
                if (!name.Contains("hidden icons") && !name.Contains("system tray overflow")) continue;
                var bounds = button.Current.BoundingRectangle;
                if (bounds.Width > 0 && bounds.Height > 0) return (int)bounds.Left;
            }
        }
        catch (ElementNotAvailableException) { /* Explorer changed the taskbar tree. */ }
        catch (COMException) { /* Explorer is refreshing. */ }
        return null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            timerFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
