using System.IO;
using System.Text.Json;

namespace CodexCacheTimer;

internal sealed record CodexSession(
    string Id,
    string Title,
    string Model,
    DateTimeOffset LastActivity,
    DateTimeOffset? Anchor,
    bool Running,
    DateTimeOffset? RunningSince);

internal sealed class CodexSessionReader
{
    private readonly string home;

    public CodexSessionReader(string? home = null)
    {
        this.home = home ?? Environment.GetEnvironmentVariable("CODEX_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
    }

    public IReadOnlyList<CodexSession> ReadRecent(TimeSpan window)
    {
        var cutoff = DateTimeOffset.UtcNow - window;
        var sessionsRoot = Path.Combine(home, "sessions");
        if (!Directory.Exists(sessionsRoot)) return [];

        var titles = ReadTitles();
        var byId = new Dictionary<string, CodexSession>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(sessionsRoot, "rollout-*.jsonl", SearchOption.AllDirectories))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(path) < cutoff.UtcDateTime) continue;
                var session = ReadFile(path, titles);
                if (session is null || session.LastActivity < cutoff) continue;
                if (!byId.TryGetValue(session.Id, out var previous)
                    || previous.LastActivity < session.LastActivity)
                    byId[session.Id] = session;
            }
            catch (IOException) { /* A live rollout may be replaced during a scan. */ }
            catch (UnauthorizedAccessException) { /* Ignore inaccessible old sessions. */ }
        }
        return byId.Values.OrderBy(s => s.LastActivity).ToArray();
    }

    private Dictionary<string, string> ReadTitles()
    {
        var titles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var path = Path.Combine(home, "session_index.jsonl");
        if (!File.Exists(path)) return titles;
        try
        {
            foreach (var line in File.ReadLines(path))
            {
                try
                {
                    using var json = JsonDocument.Parse(line);
                    var root = json.RootElement;
                    var id = GetString(root, "id");
                    var title = GetString(root, "thread_name");
                    if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(title))
                        titles[id] = title;
                }
                catch (JsonException) { /* The final append may be incomplete. */ }
            }
        }
        catch (IOException) { /* Titles remain Untitled until the next scan. */ }
        return titles;
    }

    private static CodexSession? ReadFile(string path, IReadOnlyDictionary<string, string> titles)
    {
        string? id = null;
        string model = "";
        long ownHistoryStart = 0;
        DateTimeOffset lastActivity = DateTimeOffset.MinValue;
        DateTimeOffset? preRequest = null;
        DateTimeOffset? anchor = null;
        DateTimeOffset? runningSince = null;
        bool running = false;

        // FileShare.ReadWrite allows this read-only companion to inspect live rollouts.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            if (!IsRelevant(line)) continue;
            try
            {
                using var json = JsonDocument.Parse(line);
                var root = json.RootElement;
                var type = GetString(root, "type");
                var payload = root.TryGetProperty("payload", out var p) ? p : default;
                if (type == "session_meta")
                {
                    // Only user tasks belong in this list. Codex also writes
                    // rollouts for auto-review and other internal workers.
                    // Older user rollouts can omit thread_source entirely.
                    // User-created forks have distinct IDs and remain rows.
                    var source = GetString(payload, "thread_source");
                    if (!string.IsNullOrEmpty(source) && source != "user"
                        || !string.IsNullOrEmpty(GetString(payload, "agent_path")))
                        return null;
                    id = GetString(payload, "id");
                    if (payload.TryGetProperty("subagent_history_start_ordinal", out var start)
                        && start.ValueKind == JsonValueKind.Number)
                        ownHistoryStart = start.GetInt64();
                    continue;
                }

                if (id is null) continue;
                if (root.TryGetProperty("ordinal", out var ordinal)
                    && ordinal.ValueKind == JsonValueKind.Number
                    && ordinal.GetInt64() < ownHistoryStart)
                    continue;
                if (!root.TryGetProperty("timestamp", out var timestampValue)
                    || !DateTimeOffset.TryParse(timestampValue.GetString(), out var timestamp))
                    continue;

                string subtype = GetString(payload, "type");
                switch (type)
                {
                    case "turn_context":
                        var nextModel = GetString(payload, "model");
                        if (!string.IsNullOrEmpty(nextModel))
                        {
                            if (!string.Equals(model, nextModel, StringComparison.OrdinalIgnoreCase))
                                anchor = null; // Previous model's cache is not this model's cache.
                            model = nextModel;
                        }
                        preRequest = timestamp;
                        lastActivity = timestamp;
                        break;
                    case "event_msg" when subtype == "task_started":
                        running = true;
                        runningSince = timestamp;
                        preRequest = timestamp;
                        lastActivity = timestamp;
                        break;
                    case "event_msg" when subtype == "task_complete":
                        running = false;
                        runningSince = null;
                        lastActivity = timestamp;
                        break;
                    case "response_item" when subtype is "function_call_output" or "custom_tool_call_output":
                        preRequest = timestamp;
                        lastActivity = timestamp;
                        break;
                    case "token_usage_record":
                        // This record is written after a model response. The most recent
                        // earlier durable event is a conservative lower bound on its start.
                        if (preRequest is { } before && before <= timestamp)
                            anchor = before;
                        preRequest = timestamp; // Safe lower bound for a later response.
                        lastActivity = timestamp;
                        break;
                }
            }
            catch (JsonException) { /* Skip a partially written final record. */ }
            catch (InvalidOperationException) { /* Ignore a changed internal shape. */ }
        }

        if (id is null || lastActivity == DateTimeOffset.MinValue) return null;
        return new CodexSession(id, titles.GetValueOrDefault(id, "Untitled"), model,
            lastActivity, anchor, running, runningSince);
    }

    private static bool IsRelevant(string line)
    {
        // Avoid parsing prompt bodies, encrypted reasoning, and image data.
        var head = line.AsSpan(0, Math.Min(256, line.Length));
        return head.Contains("\"type\":\"session_meta\"", StringComparison.Ordinal)
            || head.Contains("\"type\":\"turn_context\"", StringComparison.Ordinal)
            || head.Contains("\"type\":\"token_usage_record\"", StringComparison.Ordinal)
            || head.Contains("\"type\":\"task_started\"", StringComparison.Ordinal)
            || head.Contains("\"type\":\"task_complete\"", StringComparison.Ordinal)
            || head.Contains("\"type\":\"function_call_output\"", StringComparison.Ordinal)
            || head.Contains("\"type\":\"custom_tool_call_output\"", StringComparison.Ordinal);
    }

    private static string GetString(JsonElement element, string property)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? "" : "";
    }
}
