# Codex Cache Timer

A Windows companion for Codex Desktop. It places a small estimated cache timer in the taskbar gap immediately left of the hidden-icons chevron. Codex plugins do not have a supported way to add persistent desktop UI there, so the visible part runs as a C# app.

## Disclaimer - 100% Vibe coded

## Run

Run the published app:

```powershell
.\dist\CodexCacheTimer\CodexCacheTimer.exe
```

Click the timer to see the recent tasks. Click a row to open that Codex task, or click its pin icon to prefer its timer. The preferred task shows whenever it is estimated warm; when it is cold, the earliest-expiring warm task shows instead. Click `Quit` in the popup to exit.

The app requires Windows and the .NET 10 desktop runtime. It reads `%CODEX_HOME%` if set, otherwise `%USERPROFILE%\.codex`. It reads session rollouts and `session_index.jsonl` without modifying them. Codex's background workers, including subagents and auto-review, are excluded; user-created forks have their own rows.

Settings live in `%LOCALAPPDATA%\CodexCacheTimer\settings.json`. The only setting is `RecentHours`, initially `3`. Restart the app after editing the file. Pin choice is intentionally not saved.

Diagnostic logs are written to `%LOCALAPPDATA%\CodexCacheTimer\logs\cache-timer-YYYY-MM-DD.log`. They record selected task IDs, timer state, window ownership/visibility/position, popup layout, scan results, and exceptions. They do not record prompt contents or task titles.

## What the time means

The countdown is a conservative **30-minute estimate** for every model. It starts at the latest durable local event known to precede the final model request, such as a tool result, preceding response record, or turn context. Codex does not expose the server's exact cache write/reuse timestamp. A zero timer means *estimated cold*, not a confirmed eviction. See [OpenAI's prompt caching guide](https://developers.openai.com/api/docs/guides/prompt-caching) for the cache lifetime rules.

The app never sends keep-warm requests and does not track actual cache hits or misses. It sends no five-minute or unknown-model notifications.

## Build

```powershell
dotnet restore .\src\CacheTimer\CacheTimer.csproj
dotnet publish .\src\CacheTimer\CacheTimer.csproj -c Release -o .\dist\CodexCacheTimer
```

The current app targets `net10.0-windows` and uses WinForms and Windows UI Automation. The taskbar-owned overlay and Alt+Tab-hidden window style were tested on the user's Windows desktop. Windows does not provide a documented slot in this exact taskbar gap, so other topmost surfaces can temporarily cover the timer. The app detects Explorer-only occlusion and attempts to restore its Z order; it does not raise itself over capture tools such as Snipping Tool. See [TASKBAR_RESEARCH.md](TASKBAR_RESEARCH.md). Multiple monitors, auto-hidden taskbars, and Explorer restarts need further live validation.

Run the popup regression checks on Windows:

```powershell
dotnet run --project .\tests\CacheTimer.Regression\CacheTimer.Regression.csproj
```

The checks exercise actual WinForms controls: rendered title alignment across short and ellipsized text, long time-label fit, row and handle retention across scans, live status updates, sorting, task additions/removals, empty lists, and scroll preservation. The scroll check briefly shows the test popup offscreen.

Design decisions and the Q1–Q35 record are in [DESIGN_DECISIONS.md](DESIGN_DECISIONS.md).
