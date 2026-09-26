using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace CodexCacheTimer;

internal sealed class SessionsPopupForm : Form
{
    private const int WsExToolwindow = 0x00000080;
    private const int WsExAppwindow = 0x00040000;

    private readonly FlowLayoutPanel rows;
    private readonly ToolTip tooltip = new();
    private readonly Dictionary<string, SessionRow> sessionRows = new(StringComparer.OrdinalIgnoreCase);
    private readonly Label emptyMessage;
    private IReadOnlyList<CodexSession> sessions = [];
    private TimerSettings settings = new();
    private string? preferredId;
    private bool closeQueued;

    public event Action<string?>? PinRequested;
    public event Action? QuitRequested;

    public SessionsPopupForm()
    {
        Text = "Codex cache timer";
        FormBorderStyle = FormBorderStyle.None;
        ControlBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(30, 33, 38);
        DoubleBuffered = true;
        ForeColor = Color.WhiteSmoke;
        ClientSize = new Size(560, 246);

        var header = new Label
        {
            Bounds = new Rectangle(0, 0, 560, 48),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Padding = new Padding(14, 10, 0, 0),
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            Text = "Recent Codex tasks",
            ForeColor = Color.WhiteSmoke,
        };
        Controls.Add(header);

        rows = new BufferedFlowLayoutPanel
        {
            Bounds = new Rectangle(0, 48, 560, 142),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(4, 6, 4, 6),
            BackColor = BackColor,
        };
        Controls.Add(rows);
        emptyMessage = new Label
        {
            Text = "No Codex tasks active in the last three hours",
            ForeColor = Color.LightGray,
            Font = new Font("Segoe UI", 9f),
            Width = 540,
            Height = 42,
            TextAlign = ContentAlignment.MiddleCenter,
        };

        var footer = new Panel
        {
            Bounds = new Rectangle(0, ClientSize.Height - 56, 560, 56),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            BackColor = Color.FromArgb(40, 43, 48),
        };
        var settingsButton = FooterButton("Settings", 12, 120);
        settingsButton.Click += (_, _) => OpenSettings();
        var quitButton = FooterButton("Quit", 464, 82);
        quitButton.Click += (_, _) => QuitRequested?.Invoke();
        footer.Controls.Add(settingsButton);
        footer.Controls.Add(quitButton);
        Controls.Add(footer);

        Shown += (_, _) => DiagnosticLog.Info("popup-shown", $"bounds={Bounds}");
        VisibleChanged += (_, _) => DiagnosticLog.Info("popup-visible", $"visible={Visible} bounds={Bounds}");
        Deactivate += (_, _) =>
        {
            DiagnosticLog.Info("popup-deactivated", $"bounds={Bounds}");
            if (closeQueued || !IsHandleCreated) return;
            closeQueued = true;
            // Let the click that changed focus reach its target before hiding.
            BeginInvoke((Action)(() =>
            {
                closeQueued = false;
                if (!IsDisposed && Visible)
                {
                    DiagnosticLog.Info("popup-hide-after-deactivate", $"bounds={Bounds}");
                    Hide();
                }
            }));
        };
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= WsExToolwindow;
            parameters.ExStyle &= ~WsExAppwindow;
            return parameters;
        }
    }

    public void SetSessions(IReadOnlyList<CodexSession> sessions, TimerSettings settings,
        string? preferredId, DateTimeOffset now)
    {
        this.sessions = sessions;
        this.settings = settings;
        this.preferredId = preferredId;
        var sorted = sessions.OrderBy(s => TimerState.Deadline(s, settings))
            .ThenBy(s => s.Id, StringComparer.Ordinal).ToArray();
        var desired = new List<Control>();
        foreach (var session in sorted)
        {
            if (!sessionRows.TryGetValue(session.Id, out var row))
            {
                row = new SessionRow(session.Id, tooltip,
                    id => PinRequested?.Invoke(id == this.preferredId ? null : id));
                sessionRows.Add(session.Id, row);
            }
            row.Update(session, settings, preferredId, now);
            desired.Add(row);
        }
        if (desired.Count == 0) desired.Add(emptyMessage);

        // Leave the control tree (and scroll/focus state) alone on ordinary scans.
        if (!rows.Controls.Cast<Control>().SequenceEqual(desired))
        {
            var scroll = rows.AutoScrollPosition;
            rows.SuspendLayout();
            try
            {
                var retainedIds = sorted.Select(s => s.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var id in sessionRows.Keys.Where(id => !retainedIds.Contains(id)).ToArray())
                {
                    var row = sessionRows[id];
                    rows.Controls.Remove(row);
                    row.Dispose();
                    sessionRows.Remove(id);
                }
                if (sorted.Length > 0) rows.Controls.Remove(emptyMessage);
                for (int index = 0; index < desired.Count; index++)
                {
                    var control = desired[index];
                    if (control.Parent != rows) rows.Controls.Add(control);
                    if (rows.Controls.GetChildIndex(control) != index)
                        rows.Controls.SetChildIndex(control, index);
                }
                int visibleRows = Math.Clamp(sorted.Length, 1, 9);
                ClientSize = new Size(560, 48 + 56 + visibleRows * 44 + 12);
                DiagnosticLog.Info("popup-layout", $"rows={sorted.Length} visibleRows={visibleRows} "
                    + $"client={ClientSize} rowPanel={rows.Bounds} footerTop={ClientSize.Height - 56}");
            }
            finally
            {
                rows.ResumeLayout(true);
                rows.AutoScrollPosition = new Point(-scroll.X, -scroll.Y);
            }
        }
    }

    public void RefreshTimes(DateTimeOffset now)
    {
        foreach (var session in sessions)
        {
            if (sessionRows.TryGetValue(session.Id, out var row))
                row.Update(session, settings, preferredId, now);
        }
    }

    public void SetPreferred(string? id)
    {
        preferredId = id;
        RefreshTimes(DateTimeOffset.Now);
    }

    public void ShowAbove(TaskbarOverlayForm overlay)
    {
        var working = Screen.FromControl(overlay).WorkingArea;
        int left = Math.Clamp(overlay.Right - Width, working.Left, working.Right - Width);
        int top = Math.Max(working.Top, working.Bottom - Height - 8);
        Location = new Point(left, top);
        DiagnosticLog.Info("popup-position", $"overlay={overlay.Bounds} working={working} target={Bounds}");
        if (!Visible)
        {
            Show(overlay);
            Activate();
        }
    }

    private sealed class BufferedFlowLayoutPanel : FlowLayoutPanel
    {
        public BufferedFlowLayoutPanel() => DoubleBuffered = true;
    }

    private sealed class BufferedLabel : Label
    {
        public BufferedLabel() => DoubleBuffered = true;
    }

    private sealed class SessionRow : Panel
    {
        private readonly Label title;
        private readonly Label time;
        private readonly Button pin;
        private readonly ToolTip tooltip;
        private Color statusColor;
        private bool? pinned;

        public SessionRow(string id, ToolTip tooltip, Action<string> pinRequested)
        {
            this.tooltip = tooltip;
            DoubleBuffered = true;
            Width = 544;
            Height = 42;
            Margin = new Padding(2, 1, 2, 1);
            BackColor = Color.FromArgb(39, 42, 47);
            Cursor = Cursors.Hand;
            title = new BufferedLabel
            {
                ForeColor = Color.WhiteSmoke,
                Font = new Font("Segoe UI", 9f),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Bounds = new Rectangle(38, 0, 300, 42),
                Cursor = Cursors.Hand,
            };
            time = new BufferedLabel
            {
                ForeColor = Color.Gainsboro,
                Font = new Font("Segoe UI", 8.5f),
                TextAlign = ContentAlignment.MiddleRight,
                Bounds = new Rectangle(338, 0, 148, 42),
                Cursor = Cursors.Hand,
            };
            pin = new Button
            {
                Text = "📌",
                Font = new Font("Segoe UI Emoji", 9f),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Bounds = new Rectangle(488, 0, 48, 42),
                Cursor = Cursors.Hand,
                BackColor = BackColor,
            };
            pin.FlatAppearance.BorderSize = 0;
            pin.Click += (_, _) => pinRequested(id);
            Click += (_, _) => OpenTask(id);
            title.Click += (_, _) => OpenTask(id);
            time.Click += (_, _) => OpenTask(id);
            Controls.Add(title);
            Controls.Add(time);
            Controls.Add(pin);
        }

        public void Update(CodexSession session, TimerSettings settings, string? preferredId, DateTimeOffset now)
        {
            if (title.Text != session.Title)
            {
                title.Text = session.Title;
                tooltip.SetToolTip(title, session.Title);
            }
            var text = TimerState.TimeText(session, settings, now);
            if (time.Text != text) time.Text = text;
            var color = TimerState.Color(session, settings, now);
            if (statusColor != color)
            {
                statusColor = color;
                Invalidate(new Rectangle(15, 13, 10, 10));
            }
            bool isPinned = string.Equals(session.Id, preferredId, StringComparison.OrdinalIgnoreCase);
            if (pinned != isPinned)
            {
                pinned = isPinned;
                pin.BackColor = isPinned ? Color.FromArgb(70, 80, 92) : BackColor;
                tooltip.SetToolTip(pin, isPinned ? "Unpin preferred task" : "Pin preferred task");
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var brush = new SolidBrush(statusColor);
            e.Graphics.FillEllipse(brush, 15, 13, 10, 10);
        }
    }

    private static Button FooterButton(string text, int x, int width)
    {
        var button = new Button
        {
            Text = text,
            Bounds = new Rectangle(x, 8, width, 40),
            TextAlign = ContentAlignment.MiddleCenter,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.WhiteSmoke,
            BackColor = Color.FromArgb(50, 54, 61),
        };
        button.FlatAppearance.BorderSize = 0;
        return button;
    }

    private static void OpenTask(string id)
    {
        DiagnosticLog.Info("open-task", $"id={id}");
        try { Process.Start(new ProcessStartInfo($"codex://threads/{id}") { UseShellExecute = true }); }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
        { /* The deep link is a nice-to-have on hosts without a handler. */ }
    }

    private static void OpenSettings()
    {
        DiagnosticLog.Info("open-settings", $"path={TimerSettings.PathOnDisk}");
        try { Process.Start(new ProcessStartInfo(TimerSettings.PathOnDisk) { UseShellExecute = true }); }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
        { /* The file remains editable at the documented path. */ }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            tooltip.Dispose();
            emptyMessage.Dispose();
        }
        base.Dispose(disposing);
    }
}
