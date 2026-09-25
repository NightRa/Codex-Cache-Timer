using System.IO;
using System.Diagnostics;
using System.Windows.Forms;

namespace CodexCacheTimer;

internal sealed class TimerApplicationContext : ApplicationContext
{
    private readonly CodexSessionReader reader = new();
    private readonly TimerSettings settings = TimerSettings.Load();
    private readonly System.Windows.Forms.Timer clock = new() { Interval = 1000 };
    private readonly System.Windows.Forms.Timer scanner = new() { Interval = 4000 };
    private readonly System.Windows.Forms.Timer placement = new() { Interval = 5000 };
    private TaskbarOverlayForm? overlay;
    private SessionsPopupForm? popup;
    private IReadOnlyList<CodexSession> sessions = [];
    private string? preferredId;
    private bool quitting;
    private string lastSelection = "";
    private DateTimeOffset lastHeartbeat = DateTimeOffset.MinValue;
    private string lastHit = "";

    public TimerApplicationContext()
    {
        DiagnosticLog.Info("context-created", $"recentHours={settings.RecentHours} log={DiagnosticLog.CurrentPath}");
        EnsureOverlay();
        RefreshSessions();
        clock.Tick += (_, _) => UpdateClock();
        scanner.Tick += (_, _) => RefreshSessions();
        placement.Tick += (_, _) => EnsureOverlay();
        clock.Start();
        scanner.Start();
        placement.Start();
    }

    private void EnsureOverlay()
    {
        if (quitting) return;
        if (overlay is null || overlay.IsDisposed || !overlay.IsHandleCreated)
        {
            DiagnosticLog.Warn("overlay-create", $"previousExists={overlay is not null} "
                + $"previousDisposed={overlay?.IsDisposed} previousHandle={overlay?.IsHandleCreated}");
            overlay = new TaskbarOverlayForm();
            overlay.OpenRequested += (_, _) => { DiagnosticLog.Info("overlay-click"); OpenPopup(); };
            overlay.FormClosed += (_, e) =>
            {
                DiagnosticLog.Warn("context-overlay-closed", $"reason={e.CloseReason} quitting={quitting}");
                if (!quitting) popup?.Hide();
            };
            overlay.UpdatePosition();
            overlay.Show();
            UpdateClock();
        }
        overlay.UpdatePosition();
    }

    private void RefreshSessions()
    {
        var watch = Stopwatch.StartNew();
        try
        {
            sessions = reader.ReadRecent(TimeSpan.FromHours(settings.RecentHours));
            DiagnosticLog.Info("scan", $"elapsedMs={watch.ElapsedMilliseconds} count={sessions.Count} "
                + string.Join(";", sessions.Select(s => $"id={s.Id},model={s.Model},running={s.Running},"
                    + $"anchor={s.Anchor:O},last={s.LastActivity:O},titled={s.Title != "Untitled"}")));
            UpdateClock();
            if (popup is { Visible: true })
            {
                popup.SetSessions(sessions, settings, preferredId, DateTimeOffset.Now);
                popup.ShowAbove(overlay!);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            DiagnosticLog.Error("scan-io-failed", error);
        }
    }

    private void UpdateClock()
    {
        if (overlay is null || overlay.IsDisposed) return;
        var now = DateTimeOffset.Now;
        var chosen = TimerState.Select(sessions, preferredId, settings, now);
        overlay.UpdateDisplay(chosen, settings, now);
        var hit = overlay.HitTestCenter();
        if (hit != "self" && overlay.RestoreIfTaskbarCovers())
            hit = overlay.HitTestCenter();
        if (hit != lastHit)
        {
            if (hit == "self") DiagnosticLog.Info("overlay-hit-recovered", overlay.DescribeWindow());
            else DiagnosticLog.Warn("overlay-hit-lost", $"hit={hit} {overlay.DescribeWindow()}");
            lastHit = hit;
        }
        var selection = chosen is null ? "none" :
            $"id={chosen.Id} state={TimerState.State(chosen, settings, now)} "
            + $"model={chosen.Model} anchor={chosen.Anchor:O} deadline={TimerState.Deadline(chosen, settings):O}";
        if (selection != lastSelection)
        {
            DiagnosticLog.Info("selection-changed", $"preferred={preferredId ?? "none"} {selection} "
                + $"hit={hit} " + overlay.DescribeWindow());
            lastSelection = selection;
        }
        if (now - lastHeartbeat >= TimeSpan.FromSeconds(30))
        {
            DiagnosticLog.Info("heartbeat", $"preferred={preferredId ?? "none"} {selection} "
                + $"hit={hit} " + overlay.DescribeWindow());
            lastHeartbeat = now;
        }
        if (popup is { Visible: true }) popup.RefreshTimes(now);
    }

    private void OpenPopup()
    {
        if (overlay is null || overlay.IsDisposed) return;
        DiagnosticLog.Info("popup-open", $"rows={sessions.Count} preferred={preferredId ?? "none"}");
        if (popup is null || popup.IsDisposed)
        {
            popup = new SessionsPopupForm();
            popup.PinRequested += id =>
            {
                DiagnosticLog.Info("pin-changed", $"old={preferredId ?? "none"} new={id ?? "none"}");
                preferredId = id;
                popup.SetPreferred(id);
                UpdateClock();
            };
            popup.QuitRequested += Quit;
        }
        popup.SetSessions(sessions, settings, preferredId, DateTimeOffset.Now);
        popup.ShowAbove(overlay);
    }

    private void Quit()
    {
        DiagnosticLog.Info("quit-requested");
        quitting = true;
        clock.Stop();
        scanner.Stop();
        placement.Stop();
        popup?.Close();
        overlay?.Close();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            clock.Dispose();
            scanner.Dispose();
            placement.Dispose();
            popup?.Dispose();
            overlay?.Dispose();
        }
        base.Dispose(disposing);
    }
}
