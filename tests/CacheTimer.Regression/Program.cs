using CodexCacheTimer;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        ApplicationConfiguration.Initialize();
        try
        {
            CheckPopupScrolling();
            CheckTitleAlignment();
            using var popup = new SessionsPopupForm();
            var now = new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);
            var settings = new TimerSettings();
            var session = new CodexSession("task-1", "Example", "model", now, now, false, null);
            popup.SetSessions([session], settings, null, now);
            var rows = popup.SessionRows;
            var row = rows.Controls[0];
            var handle = row.Handle;
            int removed = 0;
            rows.ControlRemoved += (_, _) => removed++;

            for (int scan = 1; scan <= 25; scan++)
                popup.SetSessions([session with { }], settings, null, now.AddSeconds(scan * 4));

            Check(removed == 0 && ReferenceEquals(row, rows.Controls[0]) && row.Handle == handle,
                $"Unchanged scans must retain row controls and handles (removed={removed}).");
            Console.WriteLine("PASS: repeated scans retain existing popup rows and handles.");

            var title = row.Controls.OfType<Label>().Single(label => label.Name == "SessionTitle");
            var time = row.Controls.OfType<Label>().Single(label => label.Name == "SessionTime");
            var pin = row.Controls.OfType<Button>().Single();
            int textChanges = 0;
            time.TextChanged += (_, _) => textChanges++;
            popup.RefreshTimes(now.AddSeconds(100));
            Check(textChanges == 0, "An unchanged clock tick must not rewrite the time label.");
            session = session with { Title = "Renamed", Running = true, RunningSince = now };
            popup.SetSessions([session], settings, "task-1", now.AddMinutes(1));
            Check(ReferenceEquals(row, rows.Controls[0]) && title.Text == "Renamed"
                && time.Text == "Running · 01:00", "Updated scan data must update the retained row.");
            Check(pin.BackColor == Color.FromArgb(70, 80, 92), "Preferred row must be highlighted.");
            Check(DotColor(row) == Color.DeepSkyBlue.ToArgb(), "Running row must paint a blue dot.");
            popup.SetPreferred(null);
            Check(pin.BackColor == row.BackColor, "Unpinning must remove the highlight.");

            session = session with { Running = false, Anchor = now };
            popup.SetSessions([session], settings, null, now);
            foreach (var (minutes, color) in new[] { (0, Color.LightGreen), (20, Color.Gold),
                (25, Color.Tomato), (30, Color.LightGray) })
            {
                popup.RefreshTimes(now.AddMinutes(minutes));
                Check(DotColor(row) == color.ToArgb(), $"Clock tick at {minutes} minutes must update the dot.");
            }
            Console.WriteLine("PASS: titles, countdowns, pin state and status colors update in place.");

            var second = session with { Id = "task-2", Title = "Second", Anchor = now.AddMinutes(1) };
            popup.SetSessions([second, session], settings, null, now);
            var secondRow = rows.Controls[1];
            second = second with { Anchor = now.AddMinutes(-1) };
            popup.SetSessions([session, second], settings, null, now);
            Check(ReferenceEquals(rows.Controls[0], secondRow) && ReferenceEquals(rows.Controls[1], row),
                "Changed deadlines must reorder retained rows.");
            popup.SetSessions([second], settings, null, now);
            Check(row.IsDisposed && !secondRow.IsDisposed && rows.Controls.Count == 1,
                "Removing a task must dispose only its row.");
            popup.SetSessions([], settings, null, now);
            var emptyMessage = rows.Controls[0];
            popup.SetSessions([], settings, null, now.AddSeconds(4));
            Check(secondRow.IsDisposed && ReferenceEquals(emptyMessage, rows.Controls[0]),
                "Repeated empty scans must retain the empty message.");
            popup.SetSessions([session], settings, null, now);
            Check(!emptyMessage.IsDisposed && rows.Controls.Count == 1 && rows.Controls[0] is Panel,
                "A new task must replace the empty message.");
            Console.WriteLine("PASS: sorting, additions, removals and empty-list transitions.");

            var many = Enumerable.Range(0, 20).Select(index => session with
            {
                Id = $"many-{index}", Title = $"Task {index}", Anchor = now.AddMinutes(index)
            }).ToArray();
            // WinForms computes real scroll ranges only for a visible control tree.
            popup.Location = new Point(-32000, -32000);
            popup.Show();
            popup.SetSessions(many, settings, null, now);
            Application.DoEvents();
            popup.ScrollListTo(150);
            var scroll = popup.ScrollOffset;
            Check(scroll > 0, "Scroll regression must exercise a scrolled list.");
            var retained = rows.Controls.Cast<Control>().ToArray();
            popup.SetSessions(many.Select(item => item with { }).ToArray(), settings, null, now.AddSeconds(4));
            Check(popup.ScrollOffset == scroll && rows.Controls.Cast<Control>().SequenceEqual(retained),
                "Refreshing a scrolled list must preserve its scroll position and rows.");
            popup.SetSessions(many.Append(second).ToArray(), settings, null, now.AddSeconds(8));
            Check(popup.ScrollOffset == scroll && retained.All(control => !control.IsDisposed),
                "Adding a task must preserve scroll position and existing rows.");
            Console.WriteLine("PASS: scrolled lists retain their position during scans and additions.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"FAIL: {error.Message}");
            return 1;
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);

    private static void CheckPopupScrolling()
    {
        using var popup = new SessionsPopupForm();
        var now = new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);
        var settings = new TimerSettings();
        var many = Enumerable.Range(0, 20).Select(index => new CodexSession(
            $"scroll-{index}", $"Scroll task {index}", "model", now.AddMinutes(index),
            now, false, null)).ToArray();
        popup.Location = new Point(-32000, -32000);
        popup.Show();
        popup.SetSessions(many, settings, many[^1].Id, now);
        Application.DoEvents();
        var rows = (FlowLayoutPanel)typeof(SessionsPopupForm)
            .GetField("rows", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(popup)!;
        var bar = (Control)typeof(SessionsPopupForm)
            .GetField("scrollBar", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(popup)!;
        Check(bar.Visible, "Overflow must keep the dark scrollbar visible.");
        int previousTop = rows.Controls[0].Top + rows.Top;
        var wheel = bar.GetType().GetMethod("OnMouseWheel", BindingFlags.Instance | BindingFlags.NonPublic)!;
        for (int step = 0; step < 3; step++)
        {
            wheel.Invoke(bar, [new MouseEventArgs(MouseButtons.None, 0, 5, 5, -120)]);
            Application.DoEvents();
            int top = rows.Controls[0].Top + rows.Top;
            Check(top < previousTop, $"Wheel step {step} must advance the list (before={previousTop}, after={top}).");
            previousTop = top;
            Check(bar.Visible, "Scrolling must not hide the dark scrollbar.");
            Check((GetWindowLongPtr(rows.Handle, -16).ToInt64() & 0x00300000) == 0,
                "Scrolling must not create native horizontal or vertical scrollbars.");
            popup.SetSessions(many.Select(session => session with { }).ToArray(), settings, many[^1].Id, now);
            Application.DoEvents();
            Check(rows.Controls[0].Top + rows.Top == top, "A scan must not snap the list back after a wheel step.");
        }
        Console.WriteLine("PASS: wheel scrolling advances without native bars, hiding the theme or snapping back.");
        foreach (Control surface in new Control[] { rows, rows.Controls[0],
            rows.Controls[0].Controls.OfType<Label>().First(),
            rows.Controls[0].Controls.OfType<Button>().Single() })
        {
            popup.ScrollListTo(0);
            SendMessage(surface.Handle, 0x020A, new nint(-120 << 16), 0);
            Application.DoEvents();
            int expected = 44 * Math.Max(1, SystemInformation.MouseWheelScrollLines);
            Check(popup.ScrollOffset == expected,
                $"Wheel messages over {surface.GetType().Name} must scroll exactly once (offset={popup.ScrollOffset}, expected={expected}).");
        }
        Console.WriteLine("PASS: native wheel messages over the list, row, label and pin button scroll exactly once.");
        var thumb = (Rectangle)bar.GetType().GetMethod("ThumbBounds", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(bar, null)!;
        void Mouse(string method, int x, int y) => bar.GetType()
            .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(bar, [new MouseEventArgs(MouseButtons.Left, 1, x, y, 0)]);
        Mouse("OnMouseDown", thumb.Left + thumb.Width / 2, thumb.Top + thumb.Height / 2);
        Mouse("OnMouseMove", thumb.Left + thumb.Width / 2, bar.Height - 1);
        Mouse("OnMouseUp", thumb.Left + thumb.Width / 2, bar.Height - 1);
        Application.DoEvents();
        Check(popup.ScrollOffset == popup.MaximumScroll, "Dragging the dark thumb to the bottom must reach the maximum offset.");
        var last = rows.Controls[^1];
        Check(last.Top + rows.Top >= 0 && last.Bottom + rows.Top <= rows.Parent!.ClientSize.Height,
            "The pinned final row must fit completely in the viewport at maximum scroll.");
        int bottomOffset = popup.ScrollOffset;
        wheel.Invoke(bar, [new MouseEventArgs(MouseButtons.None, 0, 5, 5, 120)]);
        Application.DoEvents();
        Check(popup.ScrollOffset < bottomOffset && bar.Visible, "Reverse wheel scrolling must move away from the bottom and keep the dark scrollbar.");
        Mouse("OnMouseDown", 1, 0);
        Mouse("OnMouseUp", 1, 0);
        Check(popup.ScrollOffset == 0, "Clicking the track above the thumb must page toward the top.");
        var key = typeof(SessionsPopupForm).GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic)!;
        key.Invoke(popup, [new Message(), Keys.End]);
        Check(popup.ScrollOffset == popup.MaximumScroll, "End must reach the bottom.");
        key.Invoke(popup, [new Message(), Keys.Home]);
        Check(popup.ScrollOffset == 0, "Home must reach the top.");
        key.Invoke(popup, [new Message(), Keys.PageDown]);
        Check(popup.ScrollOffset == rows.Parent!.ClientSize.Height, "PageDown must advance one viewport.");
        key.Invoke(popup, [new Message(), Keys.PageUp]);
        Check(popup.ScrollOffset == 0, "PageUp must return to the top.");
        popup.SetSessions([many[0]], settings, null, now);
        Check(!bar.Visible && popup.ScrollOffset == 0, "A short list must hide the dark scrollbar and reset the offset.");
        Console.WriteLine("PASS: thumb drag, track paging, keyboard input and pinned final-row visibility.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void CheckTitleAlignment()
    {
        using var popup = new SessionsPopupForm();
        var now = new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);
        var session = new CodexSession("alignment", "Example", "model", now, now, false, null);
        popup.SetSessions([session], new TimerSettings(), null, now);
        var rows = popup.SessionRows;
        var row = rows.Controls[0];
        var title = row.Controls.OfType<Label>().Single(label => label.Name == "SessionTitle");
        var shortBounds = TitleInkBounds(title);
        int dotCenter = row.Height / 2 - 2;
        Check(Math.Abs((shortBounds.Top + shortBounds.Bottom) / 2d - dotCenter) <= 2,
            $"Title ink must align with the status dot (title={shortBounds}, dotCenter={dotCenter}, fontHeight={title.Font.Height}, dpi={title.DeviceDpi}).");
        var longTitle = "Example uncommitted changes";
        // Ensure truncation is exercised at any display DPI, including 100% scaling.
        while (TextRenderer.MeasureText(longTitle, title.Font).Width <= title.Width)
            longTitle += " changes";
        popup.SetSessions([session with { Title = longTitle }], new TimerSettings(), null, now);
        var longBounds = TitleInkBounds(title);
        Check(shortBounds == longBounds,
            $"Short and ellipsized titles must share vertical ink bounds (short={shortBounds}, long={longBounds}).");
        var cold = session with { Anchor = now.AddMinutes(-90) };
        popup.SetSessions([cold], new TimerSettings(), null, now);
        var time = row.Controls.OfType<Label>().Single(label => label.Name == "SessionTime");
        int timeWidth = TextRenderer.MeasureText(time.Text, time.Font,
            Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
        Check(timeWidth <= time.ClientSize.Width,
            $"Cold time label must fit without clipping (textWidth={timeWidth}, labelWidth={time.ClientSize.Width}, text={time.Text}).");
        Console.WriteLine("PASS: short and ellipsized popup titles share vertical alignment.");
    }

    private static (int Top, int Bottom) TitleInkBounds(Label title)
    {
        using var bitmap = new Bitmap(title.Width, title.Height);
        title.DrawToBitmap(bitmap, title.ClientRectangle);
        int top = title.Height, bottom = -1;
        // Inspect only the shared 'Example' prefix so different glyphs cannot affect the result.
        int prefixWidth = TextRenderer.MeasureText("Example", title.Font,
            Size.Empty, TextFormatFlags.NoPadding).Width;
        for (int y = 0; y < bitmap.Height; y++)
        for (int x = 0; x < prefixWidth; x++)
        {
            var pixel = bitmap.GetPixel(x, y);
            if (pixel.R < 150 || pixel.G < 150 || pixel.B < 150) continue;
            top = Math.Min(top, y);
            bottom = Math.Max(bottom, y);
        }
        Check(bottom >= top, "Title alignment check must capture rendered text.");
        return (top, bottom);
    }

    private static int DotColor(Control row)
    {
        using var bitmap = new Bitmap(row.Width, row.Height);
        row.DrawToBitmap(bitmap, row.ClientRectangle);
        return bitmap.GetPixel(20, row.Height / 2).ToArgb();
    }
}
