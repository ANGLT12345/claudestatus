using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ClaudeUsageBar;

public sealed record UsageWindow(double Percent, DateTimeOffset? ResetsAt);
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
        FiveHour = Window(root, "five_hour"),
        SevenDay = Window(root, "seven_day"),
        SevenDayOpus = Window(root, "seven_day_opus"),
        SevenDaySonnet = Window(root, "seven_day_sonnet"),
        Extra = ParseExtra(root),
        FetchedAt = fetchedAt,
    };

    static UsageWindow? Window(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var w) || w.ValueKind != JsonValueKind.Object)
            return null;
        var pct = Num(w, "utilization");
        if (pct is null) return null;
        DateTimeOffset? reset = null;
        if (w.TryGetProperty("resets_at", out var r) && r.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(r.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            reset = parsed;
        return new UsageWindow(Math.Clamp(pct.Value, 0, 100), reset);
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

/// <summary>Talks to the same endpoint Claude Code's /usage uses, with Claude Code's own OAuth token.</summary>
public sealed class UsageClient
{
    const string Endpoint = "https://api.anthropic.com/api/oauth/usage";
    // Redirects are refused so the bearer token can only ever go to the host above, and responses are size-capped.
    static readonly HttpClient Http = new(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false })
    {
        Timeout = TimeSpan.FromSeconds(20),
        MaxResponseContentBufferSize = 256 * 1024,
    };
    string? _rejectedToken;

    /// <summary>Path to the Claude Code CLI, or null if it isn't installed.</summary>
    public static string? FindCli()
    {
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin"));
        foreach (var raw in dirs)
        {
            var dir = raw.Trim().Trim('"');
            // Relative PATH entries (e.g. ".") would resolve against our working directory; never launch from those.
            if (!Path.IsPathFullyQualified(dir)) continue;
            foreach (var name in new[] { "claude.exe", "claude.cmd" })
            {
                try { var p = Path.Combine(dir, name); if (File.Exists(p)) return p; } catch { }
            }
        }
        return null;
    }

    static string ConfigDir => Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } d
        ? d : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
    static string CredentialsPath => Path.Combine(ConfigDir, ".credentials.json");
    static string CachePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClaudeUsageBar", "last.json");

    static (string? token, string? plan) ReadCredentials()
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(CredentialsPath));
            if (!doc.RootElement.TryGetProperty("claudeAiOauth", out var o)) return (null, null);
            string? token = o.TryGetProperty("accessToken", out var t) ? t.GetString() : null;
            string? plan = o.TryGetProperty("subscriptionType", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
            return (token, plan);
        }
        catch { return (null, null); }
    }

    public async Task<FetchResult> FetchAsync()
    {
        var (token, plan) = ReadCredentials();
        if (string.IsNullOrEmpty(token))
            return new(FetchStatus.NoCredentials, null, plan, "Not signed in");
        // Don't keep hitting the API with a token it already rejected; wait until Claude Code writes a new one.
        if (token == _rejectedToken)
            return new(FetchStatus.Unauthorized, null, plan, "Login expired");

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, Endpoint);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.Add("anthropic-beta", "oauth-2025-04-20");
            req.Headers.UserAgent.ParseAdd("ClaudeUsageBar/1.0");
            using var res = await Http.SendAsync(req);
            var body = await res.Content.ReadAsStringAsync();

            if (res.StatusCode == HttpStatusCode.TooManyRequests)
            {
                TimeSpan? wait = res.Headers.RetryAfter?.Delta
                    ?? (res.Headers.RetryAfter?.Date is { } d ? d - DateTimeOffset.UtcNow : null);
                return new(FetchStatus.RateLimited, null, plan, "Rate limited by Anthropic", wait);
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
            var snap = UsageSnapshot.Parse(doc.RootElement, now);
            SaveCache(body, plan, now);
            return new(FetchStatus.Ok, snap, plan);
        }
        catch (TaskCanceledException) { return new(FetchStatus.Error, null, plan, "Request timed out"); }
        catch (HttpRequestException) { return new(FetchStatus.Error, null, plan, "Can't reach api.anthropic.com"); }
        catch (JsonException) { return new(FetchStatus.Error, null, plan, "Unexpected response"); }
        catch (Exception) { return new(FetchStatus.Error, null, plan, "Couldn't update"); }
    }

    static void SaveCache(string rawJson, string? plan, DateTimeOffset at)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
            File.WriteAllText(CachePath,
                $"{{\"fetchedAt\":{JsonSerializer.Serialize(at)},\"plan\":{JsonSerializer.Serialize(plan)},\"data\":{rawJson}}}");
        }
        catch { }
    }

    public static (UsageSnapshot? data, string? plan) LoadCache()
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(CachePath));
            var root = doc.RootElement;
            var at = root.GetProperty("fetchedAt").GetDateTimeOffset();
            string? plan = root.TryGetProperty("plan", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
            return (UsageSnapshot.Parse(root.GetProperty("data"), at), plan);
        }
        catch { return (null, ReadCredentials().plan); }
    }
}

/// <summary>Everything the UI needs to draw itself.</summary>
public sealed class ViewState
{
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
        "max" => "Max",
        "team" => "Team",
        "enterprise" => "Enterprise",
        var p => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(p),
    };

    /// <summary>How far through the window we are (0..1), for the pace marker.</summary>
    public static float? Elapsed(DateTimeOffset? reset, TimeSpan length, DateTimeOffset now)
    {
        if (reset is null) return null;
        var left = reset.Value - now;
        return (float)Math.Clamp(1 - left.TotalSeconds / length.TotalSeconds, 0, 1);
    }
}
