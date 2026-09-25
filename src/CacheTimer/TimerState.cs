namespace CodexCacheTimer;

internal enum WarmState { Cold, Warm, Running }

internal static class TimerState
{
    public static WarmState State(CodexSession session, TimerSettings settings, DateTimeOffset now)
    {
        if (session.Running) return WarmState.Running;
        if (session.Anchor is null) return WarmState.Cold;
        var end = Deadline(session, settings);
        return end > now ? WarmState.Warm : WarmState.Cold;
    }

    public static DateTimeOffset Deadline(CodexSession session, TimerSettings settings)
    {
        return (session.Anchor ?? session.LastActivity).AddMinutes(30);
    }

    public static CodexSession? Select(IReadOnlyList<CodexSession> sessions,
        string? preferredId, TimerSettings settings, DateTimeOffset now)
    {
        var preferred = sessions.FirstOrDefault(s => s.Id == preferredId);
        if (preferred is not null && State(preferred, settings, now) != WarmState.Cold)
            return preferred;

        var warm = sessions.Where(s => State(s, settings, now) == WarmState.Warm)
            .OrderBy(s => Deadline(s, settings)).FirstOrDefault();
        if (warm is not null) return warm;

        return sessions.Where(s => s.Running)
            .OrderByDescending(s => s.RunningSince).FirstOrDefault();
    }

    public static string TimeText(CodexSession session, TimerSettings settings, DateTimeOffset now)
    {
        if (session.Running)
        {
            var elapsed = now - (session.RunningSince ?? now);
            return $"Running · {FormatDuration(elapsed)}";
        }
        if (session.Anchor is null) return "Cold";
        var remaining = Deadline(session, settings) - now;
        if (remaining > TimeSpan.Zero) return FormatDuration(remaining);
        return $"Cold · {FormatDuration(-remaining)} ago";
    }

    public static string OverlayText(CodexSession? session, TimerSettings settings, DateTimeOffset now)
    {
        if (session is null) return "●";
        if (session.Running)
            return $"●  {FormatDuration(now - (session.RunningSince ?? now))}";
        var remaining = Deadline(session, settings) - now;
        return remaining > TimeSpan.Zero ? $"●  {FormatDuration(remaining)}" : "●";
    }

    public static System.Drawing.Color Color(CodexSession? session, TimerSettings settings, DateTimeOffset now)
    {
        if (session is null) return System.Drawing.Color.LightGray;
        if (session.Running) return System.Drawing.Color.DeepSkyBlue;
        var remaining = Deadline(session, settings) - now;
        if (remaining <= TimeSpan.Zero) return System.Drawing.Color.LightGray;
        if (remaining <= TimeSpan.FromMinutes(5)) return System.Drawing.Color.Tomato;
        if (remaining <= TimeSpan.FromMinutes(10)) return System.Drawing.Color.Gold;
        return System.Drawing.Color.LightGreen;
    }

    private static string FormatDuration(TimeSpan value)
    {
        if (value < TimeSpan.Zero) value = TimeSpan.Zero;
        return $"{(int)value.TotalMinutes:00}:{value.Seconds:00}";
    }
}
