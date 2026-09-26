using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CodexCacheTimer;

internal sealed class SessionsPopupForm : Form
{
    private const int WsExToolwindow = 0x00000080;
    private const int WsExAppwindow = 0x00040000;
    private const int MaxVisibleRows = 9;

    private readonly BufferedFlowLayoutPanel rows;
    private readonly PopupScrollBar scrollBar;
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
            BackColor = BackColor,
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
        scrollBar = new PopupScrollBar(rows)
        {
            Visible = false,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right,
        };
        Controls.Add(scrollBar);
        rows.ClientSizeChanged += (_, _) => ResizeRowsToViewport();
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
        header.BringToFront();

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
                int visibleRows = Math.Clamp(sorted.Length, 1, MaxVisibleRows);
                ClientSize = new Size(560, 48 + 56 + visibleRows * 44 + 12);
                DiagnosticLog.Info("popup-layout", $"rows={sorted.Length} visibleRows={visibleRows} "
                    + $"client={ClientSize} rowPanel={rows.Bounds} footerTop={ClientSize.Height - 56}");
            }
            finally
            {
                rows.ResumeLayout(true);
                rows.AutoScrollPosition = new Point(-scroll.X, -scroll.Y);
            }
            ResizeRowsToViewport();
            int scrollContentHeight = sorted.Length > MaxVisibleRows
                ? rows.Padding.Vertical + sessionRows.Values.Sum(row => row.Height + row.Margin.Vertical)
                : 0;
            rows.AutoScrollMinSize = new Size(0, scrollContentHeight);
            rows.PerformLayout();
            ResizeRowsToViewport();
            scrollBar.Sync();
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

    private void ResizeRowsToViewport()
    {
        if (rows.IsDisposed || rows.ClientSize.Width <= 0) return;
        foreach (var row in sessionRows.Values)
        {
            int width = Math.Max(1, rows.ClientSize.Width - rows.Padding.Horizontal - row.Margin.Horizontal);
            row.ResizeForList(width);
        }
        emptyMessage.Width = Math.Max(1,
            rows.ClientSize.Width - rows.Padding.Horizontal - emptyMessage.Margin.Horizontal);
        scrollBar.Bounds = new Rectangle(rows.Right - SystemInformation.VerticalScrollBarWidth,
            rows.Top, SystemInformation.VerticalScrollBarWidth, rows.Height);
        scrollBar.Sync();
    }

    private sealed class PopupScrollBar : Control
    {
        private const int SbVert = 1;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowScrollBar(nint hWnd, int bar, bool show);

        private readonly FlowLayoutPanel target;
        private bool hovered;
        private bool dragging;
        private int dragOffset;

        public PopupScrollBar(FlowLayoutPanel target)
        {
            this.target = target;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(30, 33, 38);
            Cursor = Cursors.SizeNS;
            target.Scroll += (_, _) => Sync();
            target.Layout += (_, _) => Sync();
            target.ClientSizeChanged += (_, _) => Sync();
        }

        public void Sync()
        {
            if (target.IsDisposed || !target.IsHandleCreated) return;
            bool needsScroll = target.VerticalScroll.Visible;
            // AutoScroll still needs its native range for wheel and keyboard input,
            // but its OS-drawn scrollbar ignores the popup's dark theme. Keep the
            // native bar hidden and draw the themed thumb in this sibling control.
            ShowScrollBar(target.Handle, SbVert, false);
            if (Visible != needsScroll) Visible = needsScroll;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.Clear(BackColor);
            var thumb = ThumbBounds();
            using var brush = new SolidBrush(hovered || dragging
                ? Color.FromArgb(119, 129, 140)
                : Color.FromArgb(82, 90, 100));
            using var path = RoundedRectangle(thumb, Scale(4));
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.FillPath(brush, path);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (!dragging) hovered = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            var thumb = ThumbBounds();
            if (thumb.Contains(e.Location))
            {
                dragging = true;
                dragOffset = e.Y - thumb.Top;
                Capture = true;
            }
            else
            {
                int page = Math.Max(1, target.ClientSize.Height - Scale(44));
                ScrollBy(e.Y < thumb.Top ? -page : page);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!dragging) return;
            int trackTop = Scale(8);
            int trackHeight = Math.Max(1, Height - Scale(16));
            int travel = Math.Max(1, trackHeight - ThumbBounds().Height);
            double fraction = Math.Clamp((e.Y - dragOffset - trackTop) / (double)travel, 0, 1);
            SetScrollValue((int)Math.Round(ScrollRange() * fraction));
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            dragging = false;
            Capture = false;
            hovered = ClientRectangle.Contains(e.Location);
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            ScrollBy(-Math.Sign(e.Delta) * Math.Max(1, target.ClientSize.Height / 3));
            base.OnMouseWheel(e);
        }

        private Rectangle ThumbBounds()
        {
            int trackTop = Scale(8);
            int trackHeight = Math.Max(1, Height - Scale(16));
            int range = ScrollRange();
            int viewport = Math.Max(1, target.ClientSize.Height);
            int content = Math.Max(viewport, range + viewport);
            int thumbHeight = Math.Clamp((int)Math.Round(trackHeight * (double)viewport / content),
                Scale(30), trackHeight);
            int travel = Math.Max(0, trackHeight - thumbHeight);
            int value = Math.Clamp(target.VerticalScroll.Value, 0, range);
            int top = trackTop + (range == 0 ? 0 : (int)Math.Round(travel * (double)value / range));
            int width = Scale(7);
            return new Rectangle((Width - width) / 2, top, width, thumbHeight);
        }

        private int ScrollRange() => Math.Max(0,
            target.VerticalScroll.Maximum - target.VerticalScroll.LargeChange + 1);

        private void ScrollBy(int amount)
        {
            SetScrollValue(target.VerticalScroll.Value + amount);
        }

        private void SetScrollValue(int value)
        {
            int range = ScrollRange();
            int next = Math.Clamp(value, 0, range);
            if (next == target.VerticalScroll.Value) return;
            try { target.VerticalScroll.Value = next; }
            catch (ArgumentOutOfRangeException) { target.AutoScrollPosition = new Point(0, next); }
            Sync();
        }

        private int Scale(int value) => (int)Math.Round(value * DeviceDpi / 96d);

        private static System.Drawing.Drawing2D.GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
        {
            int diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    private sealed class SingleLineLabel : Label
    {
        public SingleLineLabel() => DoubleBuffered = true;

        protected override void OnPaint(PaintEventArgs e)
        {
            // Label's default word wrapping can center a clipped multi-line block,
            // shifting long titles upward even when AutoEllipsis is enabled.
            int verticalShift = (int)Math.Round(Font.Height / 9d);
            var textBounds = ClientRectangle;
            textBounds.Y -= verticalShift;
            var flags = TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter
                | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
            if (AutoEllipsis) flags |= TextFormatFlags.EndEllipsis;
            if (TextAlign == ContentAlignment.MiddleRight) flags |= TextFormatFlags.Right;
            TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, ForeColor, flags);
        }
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
            title = new SingleLineLabel
            {
                Name = "SessionTitle",
                ForeColor = Color.WhiteSmoke,
                Font = new Font("Segoe UI", 9f),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Bounds = new Rectangle(38, 0, 300, 42),
                Cursor = Cursors.Hand,
            };
            time = new SingleLineLabel
            {
                Name = "SessionTime",
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
            if (time.Text != text)
            {
                time.Text = text;
                FitTimeLabel();
            }
            var color = TimerState.Color(session, settings, now);
            if (statusColor != color)
            {
                statusColor = color;
                Invalidate(new Rectangle(15, (Height - 10) / 2 - 2, 10, 10));
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
            const int dotSize = 10;
            // GDI's visible letter ink sits slightly above its line-box center.
            e.Graphics.FillEllipse(brush, 15, (Height - dotSize) / 2 - 2, dotSize, dotSize);
        }

        private void FitTimeLabel()
        {
            const int titleLeft = 38;
            const int minimumTitleWidth = 96;
            const int trailingGap = 2;
            int timeRight = pin.Left - trailingGap;
            int textWidth = TextRenderer.MeasureText(time.Text, time.Font,
                Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
            int maximumTimeWidth = Math.Max(148, timeRight - titleLeft - minimumTitleWidth);
            int timeWidth = Math.Clamp(Math.Max(148, textWidth + 8), 148, maximumTimeWidth);
            int timeLeft = timeRight - timeWidth;
            time.Bounds = new Rectangle(timeLeft, 0, timeWidth, Height);
            title.Width = Math.Max(minimumTitleWidth, timeLeft - titleLeft);
        }

        public void ResizeForList(int width)
        {
            if (Width == width && pin.Left == Width - pin.Width - 8) return;
            Width = width;
            pin.Left = Width - pin.Width - 8;
            FitTimeLabel();
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
