using CodexCacheTimer;
using System.Drawing;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        ApplicationConfiguration.Initialize();
        try
        {
            CheckTitleAlignment();
            using var popup = new SessionsPopupForm();
            var now = new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);
            var settings = new TimerSettings();
            var session = new CodexSession("task-1", "Example", "model", now, now, false, null);
            popup.SetSessions([session], settings, null, now);
            var rows = popup.Controls.OfType<FlowLayoutPanel>().Single();
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
            rows.AutoScrollPosition = new Point(0, 150);
            var scroll = rows.AutoScrollPosition;
            Check(scroll.Y < 0, "Scroll regression must exercise a scrolled list.");
            var retained = rows.Controls.Cast<Control>().ToArray();
            popup.SetSessions(many.Select(item => item with { }).ToArray(), settings, null, now.AddSeconds(4));
            Check(rows.AutoScrollPosition == scroll && rows.Controls.Cast<Control>().SequenceEqual(retained),
                "Refreshing a scrolled list must preserve its scroll position and rows.");
            popup.SetSessions(many.Append(second).ToArray(), settings, null, now.AddSeconds(8));
            Check(rows.AutoScrollPosition == scroll && retained.All(control => !control.IsDisposed),
                "Adding a task must preserve scroll position and existing rows.");
            popup.SetSessions(many, settings, "many-19", now.AddSeconds(12));
            Application.DoEvents();
            int maxScroll = Math.Max(0, rows.VerticalScroll.Maximum - rows.VerticalScroll.LargeChange + 1);
            Check(maxScroll > 0, "Bottom-scroll regression must have vertical overflow.");
            rows.AutoScrollPosition = new Point(0, maxScroll);
            Application.DoEvents();

            var pinnedLastRow = rows.Controls[rows.Controls.Count - 1];
            var pinnedFinalButton = pinnedLastRow.Controls.OfType<Button>().Single();
            Check(pinnedFinalButton.BackColor == Color.FromArgb(70, 80, 92),
                "The final row must be the pinned row.");
            Check(pinnedLastRow.Top >= rows.ClientRectangle.Top
                && pinnedLastRow.Bottom <= rows.ClientRectangle.Bottom,
                $"The pinned final row must be fully visible at the bottom (row={pinnedLastRow.Bounds}, viewport={rows.ClientRectangle}).");
            Console.WriteLine("PASS: pinned final row is fully visible at maximum scroll.");
            Console.WriteLine("PASS: scrolled lists retain their position during scans and additions.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"FAIL: {error.Message}");
            return 1;
        }
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
        var rows = popup.Controls.OfType<FlowLayoutPanel>().Single();
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
