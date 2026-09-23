using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ClaudeUsageBar;

/// <summary>One usage limit. Length is the window's duration (5 hours, 7 days), used for labels and the pace marker.</summary>
public sealed record UsageWindow(double Percent, DateTimeOffset? ResetsAt, TimeSpan? Length = null);
public sealed record ExtraUsage(bool Enabled, double? LimitUsd, double? UsedUsd, double? Percent);

public sealed class UsageSnapshot
{
    public UsageWindow? FiveHour { get; init; }
    public UsageWindow? SevenDay { get; init; }
    public UsageWindow? SevenDayOpus { get; init; }
    public UsageWindow? SevenDaySonnet { get; init; }
    public ExtraUsage? Extra { get; init; }
    public DateTimeOffset FetchedAt { get; init; }

    public static UsageSnapshot Parse(JsonElement root, DateTimeOffset fetchedAt) => new()
    {
        FiveHour = Window(root, "five_hour", Fmt.FiveHours),
        SevenDay = Window(root, "seven_day", Fmt.SevenDays),
        SevenDayOpus = Window(root, "seven_day_opus", Fmt.SevenDays),
        SevenDaySonnet = Window(root, "seven_day_sonnet", Fmt.SevenDays),
        Extra = ParseExtra(root),
        FetchedAt = fetchedAt,
    };

    /// <summary>
    /// Codex's usage response: rate_limit.primary_window (the short one, 5 hours) goes in the session slot and
    /// secondary_window (weekly) in the weekly slot. Percent is used_percent; reset_at is Unix seconds.
    /// </summary>
    public static UsageSnapshot ParseChatGpt(JsonElement root, DateTimeOffset fetchedAt)
    {
        JsonElement rl = default;
        bool has = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("rate_limit", out rl) && rl.ValueKind == JsonValueKind.Object;
        return new()
        {
            FiveHour = has ? CodexWindow(rl, "primary_window", fetchedAt) : null,
            SevenDay = has ? CodexWindow(rl, "secondary_window", fetchedAt) : null,
            FetchedAt = fetchedAt,
        };
    }

    static UsageWindow? CodexWindow(JsonElement rl, string name, DateTimeOffset fetchedAt)
    {
        if (!rl.TryGetProperty(name, out var w) || w.ValueKind != JsonValueKind.Object) return null;
        var pct = Num(w, "used_percent");
        if (pct is null) return null;
        DateTimeOffset? reset = Num(w, "reset_at") is { } at && at > 0 ? DateTimeOffset.FromUnixTimeSeconds((long)at)
            : Num(w, "reset_after_seconds") is { } after ? fetchedAt.AddSeconds(after) : null;
        TimeSpan? length = Num(w, "limit_window_seconds") is { } secs && secs > 0 ? TimeSpan.FromSeconds(secs) : null;
        return new UsageWindow(Math.Clamp(pct.Value, 0, 100), reset, length);
    }

    static UsageWindow? Window(JsonElement root, string name, TimeSpan length)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var w) || w.ValueKind != JsonValueKind.Object)
            return null;
        var pct = Num(w, "utilization");
        if (pct is null) return null;
        DateTimeOffset? reset = null;
        if (w.TryGetProperty("resets_at", out var r) && r.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(r.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            reset = parsed;
        return new UsageWindow(Math.Clamp(pct.Value, 0, 100), reset, length);
    }

    static ExtraUsage? ParseExtra(JsonElement root)
    {
        if (!root.TryGetProperty("extra_usage", out var e) || e.ValueKind != JsonValueKind.Object) return null;
        bool enabled = e.TryGetProperty("is_enabled", out var en) && en.ValueKind == JsonValueKind.True;
        // Credits are reported in cents.
        var limit = Num(e, "monthly_limit") / 100.0;
        var used = Num(e, "used_credits") / 100.0;
        var pct = Num(e, "utilization") ?? (limit > 0 && used is not null ? used / limit * 100 : null);
        return new ExtraUsage(enabled, limit, used, pct is null ? null : Math.Clamp(pct.Value, 0, 100));
    }

    static double? Num(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
}

public enum FetchStatus { None, Ok, NoCredentials, Unauthorized, RateLimited, Error }

public sealed record FetchResult(FetchStatus Status, UsageSnapshot? Data, string? Plan, string? Message = null, TimeSpan? RetryAfter = null);

/// <summary>Whose plan usage the app shows. Picked from the right-click menu.</summary>
public enum Provider { Claude, ChatGpt }

/// <summary>
/// Fetches plan usage with a CLI's own login token (Claude Code or Codex). Subclasses say where the token lives,
/// how to ask for usage and how to read the answer; polling, errors and caching are shared.
/// </summary>
public abstract class UsageClient
{
    // Redirects are refused so the bearer token can only ever go to the provider's host, and responses are size-capped.
    static readonly HttpClient Http = new(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false })
    {
        Timeout = TimeSpan.FromSeconds(20),
        MaxResponseContentBufferSize = 256 * 1024,
    };
    string? _rejectedToken;

    public static UsageClient For(Provider p) => p == Provider.ChatGpt ? new ChatGptClient() : new ClaudeClient();

    public abstract Provider Provider { get; }
    /// <summary>Host the token is sent to, for error messages.</summary>
    protected abstract string Host { get; }
    protected abstract string Company { get; }
    protected abstract string CacheFile { get; }
    protected abstract string[] CliNames { get; }
    /// <summary>Install folders to check besides PATH.</summary>
    protected abstract string[] CliDirs { get; }
    protected abstract (string? token, string? plan, string? account) ReadCredentials();
    protected abstract HttpRequestMessage Request(string token, string? account);
    protected abstract UsageSnapshot Parse(JsonElement root, DateTimeOffset fetchedAt);
    /// <summary>Plan named in the usage response, when the credentials don't say.</summary>
    protected virtual string? PlanFrom(JsonElement root) => null;

    protected static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>Path to the provider's CLI, or null if it isn't installed.</summary>
    public string? FindCli()
    {
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries).Concat(CliDirs);
        foreach (var raw in dirs)
        {
            var dir = raw.Trim().Trim('"');
            // Relative PATH entries (e.g. ".") would resolve against our working directory; never launch from those.
            if (!Path.IsPathFullyQualified(dir)) continue;
            foreach (var name in CliNames)
            {
                try { var p = Path.Combine(dir, name); if (File.Exists(p)) return p; } catch { }
            }
        }
        return null;
    }

    string CachePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClaudeUsageBar", CacheFile);

    public async Task<FetchResult> FetchAsync()
    {
        var (token, plan, account) = ReadCredentials();
        if (string.IsNullOrEmpty(token))
            return new(FetchStatus.NoCredentials, null, plan, "Not signed in");
        // Don't keep hitting the API with a token it already rejected; wait until the CLI writes a new one.
        if (token == _rejectedToken)
            return new(FetchStatus.Unauthorized, null, plan, "Login expired");

        try
        {
            using var req = Request(token, account);
            req.Headers.UserAgent.ParseAdd("ClaudeUsageBar/1.0");
            using var res = await Http.SendAsync(req);
            var body = await res.Content.ReadAsStringAsync();

            if (res.StatusCode == HttpStatusCode.TooManyRequests)
            {
                TimeSpan? wait = res.Headers.RetryAfter?.Delta
                    ?? (res.Headers.RetryAfter?.Date is { } d ? d - DateTimeOffset.UtcNow : null);
                return new(FetchStatus.RateLimited, null, plan, $"Rate limited by {Company}", wait);
            }
            if (res.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                _rejectedToken = token;
                return new(FetchStatus.Unauthorized, null, plan, "Login expired");
            }
            if (!res.IsSuccessStatusCode)
                return new(FetchStatus.Error, null, plan, $"Server error ({(int)res.StatusCode})");

            var now = DateTimeOffset.Now;
            using var doc = JsonDocument.Parse(body);
            plan = PlanFrom(doc.RootElement) ?? plan;
            var snap = Parse(doc.RootElement, now);
            SaveCache(body, plan, now);
            return new(FetchStatus.Ok, snap, plan);
        }
        catch (TaskCanceledException) { return new(FetchStatus.Error, null, plan, "Request timed out"); }
        catch (HttpRequestException) { return new(FetchStatus.Error, null, plan, $"Can't reach {Host}"); }
        catch (JsonException) { return new(FetchStatus.Error, null, plan, "Unexpected response"); }
        catch (Exception) { return new(FetchStatus.Error, null, plan, "Couldn't update"); }
    }

    void SaveCache(string rawJson, string? plan, DateTimeOffset at)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
            File.WriteAllText(CachePath,
                $"{{\"fetchedAt\":{JsonSerializer.Serialize(at)},\"plan\":{JsonSerializer.Serialize(plan)},\"data\":{rawJson}}}");
        }
        catch { }
    }

    public (UsageSnapshot? data, string? plan) LoadCache()
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(CachePath));
            var root = doc.RootElement;
            var at = root.GetProperty("fetchedAt").GetDateTimeOffset();
            string? plan = root.TryGetProperty("plan", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
            return (Parse(root.GetProperty("data"), at), plan);
        }
        catch { return (null, ReadCredentials().plan); }
    }
}

/// <summary>Talks to the same endpoint Claude Code's /usage uses, with Claude Code's own OAuth token.</summary>
public sealed class ClaudeClient : UsageClient
{
    const string Endpoint = "https://api.anthropic.com/api/oauth/usage";

    public override Provider Provider => Provider.Claude;
    protected override string Host => "api.anthropic.com";
    protected override string Company => "Anthropic";
    protected override string CacheFile => "last.json";
    protected override string[] CliNames => new[] { "claude.exe", "claude.cmd" };
    protected override string[] CliDirs => new[] { Path.Combine(Home, ".local", "bin") };

    static string ConfigDir => Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } d
        ? d : Path.Combine(Home, ".claude");
    static string CredentialsPath => Path.Combine(ConfigDir, ".credentials.json");

    protected override (string? token, string? plan, string? account) ReadCredentials()
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(CredentialsPath));
            if (!doc.RootElement.TryGetProperty("claudeAiOauth", out var o)) return (null, null, null);
            string? token = o.TryGetProperty("accessToken", out var t) ? t.GetString() : null;
            string? plan = o.TryGetProperty("subscriptionType", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
            return (token, plan, null);
        }
        catch { return (null, null, null); }
    }

    protected override HttpRequestMessage Request(string token, string? account)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.Add("anthropic-beta", "oauth-2025-04-20");
        return req;
    }

    protected override UsageSnapshot Parse(JsonElement root, DateTimeOffset fetchedAt) => UsageSnapshot.Parse(root, fetchedAt);
}

/// <summary>
/// Talks to the endpoint Codex's /status uses, with the ChatGPT login Codex stores in ~/.codex/auth.json.
/// These are the Codex limits of the ChatGPT plan: a short (5-hour) and a weekly window.
/// </summary>
public sealed class ChatGptClient : UsageClient
{
    const string Endpoint = "https://chatgpt.com/backend-api/wham/usage";

    public override Provider Provider => Provider.ChatGpt;
    protected override string Host => "chatgpt.com";
    protected override string Company => "OpenAI";
    protected override string CacheFile => "last-chatgpt.json";
    protected override string[] CliNames => new[] { "codex.exe", "codex.cmd" };
    protected override string[] CliDirs => new[]
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm"),
        Path.Combine(Home, ".local", "bin"),
    };

    static string CodexHome => Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } d
        ? d : Path.Combine(Home, ".codex");
    static string AuthPath => Path.Combine(CodexHome, "auth.json");

    protected override (string? token, string? plan, string? account) ReadCredentials()
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(AuthPath));
            if (!doc.RootElement.TryGetProperty("tokens", out var t) || t.ValueKind != JsonValueKind.Object) return (null, null, null);
            string? token = t.TryGetProperty("access_token", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null;
            string? account = t.TryGetProperty("account_id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null;
            return (token, null, account);
        }
        catch { return (null, null, null); }
    }

    protected override HttpRequestMessage Request(string token, string? account)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (!string.IsNullOrEmpty(account)) req.Headers.TryAddWithoutValidation("ChatGPT-Account-Id", account);
        return req;
    }

    protected override string? PlanFrom(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty("plan_type", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    protected override UsageSnapshot Parse(JsonElement root, DateTimeOffset fetchedAt) => UsageSnapshot.ParseChatGpt(root, fetchedAt);
}

/// <summary>Everything the UI needs to draw itself.</summary>
public sealed class ViewState
{
    public Provider Provider;
    public UsageSnapshot? Data;
    public string? Plan;
    public FetchStatus Status;
    public string? Message;
    public DateTimeOffset? NextAttempt;
    public bool Fetching;
    public bool CliInstalled = true;
    public TimeSpan PollInterval = TimeSpan.FromMinutes(5);
}

static class Fmt
{
    public static readonly TimeSpan FiveHours = TimeSpan.FromHours(5), SevenDays = TimeSpan.FromDays(7);

    /// <summary>Compact countdown for the taskbar: 42m, 3h 5m, 4d 3h.</summary>
    public static string Short(TimeSpan t)
    {
        if (t <= TimeSpan.Zero) return "now";
        if (t.TotalHours < 1) return $"{Math.Max(1, (int)Math.Ceiling(t.TotalMinutes))}m";
        if (t.TotalDays < 1) return t.Minutes == 0 ? $"{(int)t.TotalHours}h" : $"{(int)t.TotalHours}h {t.Minutes}m";
        return t.Hours == 0 ? $"{(int)t.TotalDays}d" : $"{(int)t.TotalDays}d {t.Hours}h";
    }

    public static string Reset(DateTimeOffset? reset, DateTimeOffset now)
    {
        if (reset is null) return "";
        var left = reset.Value - now;
        if (left <= TimeSpan.Zero) return "Resetting…";
        if (left.TotalHours < 24) return "Resets in " + (left.TotalHours < 1 ? $"{Math.Max(1, (int)Math.Ceiling(left.TotalMinutes))}m" : $"{(int)left.TotalHours}h {left.Minutes:00}m");
        var local = reset.Value.ToLocalTime();
        return "Resets " + local.ToString("ddd", CultureInfo.CurrentCulture) + " " + local.ToString("t", CultureInfo.CurrentCulture);
    }

    public static string Ago(TimeSpan t)
    {
        if (t.TotalSeconds < 45) return "just now";
        if (t.TotalMinutes < 60) return $"{Math.Max(1, (int)Math.Round(t.TotalMinutes))} min ago";
        if (t.TotalHours < 24) return $"{(int)t.TotalHours} h ago";
        return $"{(int)t.TotalDays} d ago";
    }

    public static string In(TimeSpan t) =>
        t.TotalSeconds < 60 ? $"{Math.Max(1, (int)Math.Ceiling(t.TotalSeconds))}s" : $"{(int)Math.Ceiling(t.TotalMinutes)} min";

    public static string Money(double v) => v >= 1000 ? v.ToString("$#,0", CultureInfo.InvariantCulture) : v.ToString("$0.00", CultureInfo.InvariantCulture);

    public static string Plan(string? plan) => plan?.ToLowerInvariant() switch
    {
        null or "" => "",
        "pro" => "Pro",
        "plus" => "Plus",
        "max" => "Max",
        "team" => "Team",
        "enterprise" => "Enterprise",
        var p => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(p),
    };

    /// <summary>Short strip label for a window: "5h", "7d". Falls back when the length is unknown.</summary>
    public static string Label(UsageWindow? w, string fallback) => w?.Length switch
    {
        { TotalDays: >= 1 } l => $"{Math.Round(l.TotalDays):0}d",
        { TotalHours: >= 1 } l => $"{Math.Round(l.TotalHours):0}h",
        _ => fallback,
    };

    /// <summary>"5-hour rolling window", "Weekly window": describes a window by its length.</summary>
    public static string Describe(TimeSpan? length, string fallback) => length switch
    {
        { TotalDays: > 6.5 and < 7.5 } => "Weekly window",
        { TotalDays: >= 1 } l => $"{Math.Round(l.TotalDays):0}-day window",
        { TotalHours: >= 1 } l => $"{Math.Round(l.TotalHours):0}-hour rolling window",
        _ => fallback,
    };

    /// <summary>How far through the window we are (0..1), for the pace marker.</summary>
    public static float? Elapsed(DateTimeOffset? reset, TimeSpan length, DateTimeOffset now)
    {
        if (reset is null) return null;
        var left = reset.Value - now;
        return (float)Math.Clamp(1 - left.TotalSeconds / length.TotalSeconds, 0, 1);
    }
}
