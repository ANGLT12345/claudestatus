namespace ClaudeUsageBar;

/// <summary>
/// Draws the taskbar meter in one of four sizes so it can squeeze into whatever gap the taskbar has:
/// Full (segmented bars + reset times), Compact (thin bars), Mini (just percentages), Micro (two vertical gauges).
/// </summary>
static class StripPainter
{
    const int Segments = 10;
    const float SegW = 5, SegH = 9, SegGap = 2;
    static float SegBarW => Segments * SegW + (Segments - 1) * SegGap;

    public static readonly StripSize[] BySize = { StripSize.Full, StripSize.Compact, StripSize.Mini, StripSize.Micro };

    public static int Width(StripSize size, float s) => (int)Math.Ceiling(s * size switch
    {
        StripSize.Compact => 8 + 16 + 30 + 6 + 30 + 8,
        StripSize.Mini => 7 + 16 + 30 + 7,
        StripSize.Micro => 7 + 6 + 4 + 6 + 7,
        _ => 10 + 17 + SegBarW + 8 + 30 + 5 + 40 + 10,
    });

    public static void Paint(Graphics g, Size size, float s, StripSize variant, bool lightTaskbar, bool hover, bool pressed, ViewState vs, DateTimeOffset now)
    {
        Gfx.Setup(g, clearType: false);
        var fg = lightTaskbar ? Color.FromArgb(26, 26, 26) : Color.White;
        var pal = lightTaskbar ? Palette.LightTheme : Palette.Dark;

        // Nearly-transparent fill so the whole strip receives mouse input.
        g.Clear(Color.FromArgb(1, 0, 0, 0));
        if (hover || pressed)
        {
            var bg = lightTaskbar ? Color.FromArgb(pressed ? 14 : 22, 0, 0, 0) : Color.FromArgb(pressed ? 12 : 20, 255, 255, 255);
            float inset = Math.Max(4 * s, (size.Height - 40 * s) / 2);
            Gfx.FillRound(g, bg, new RectangleF(1 * s, inset, size.Width - 2 * s, size.Height - inset * 2), 5 * s);
        }

        var ctx = new Ctx(g, s, fg, pal, vs.Data is { } d0 && vs.Status is not (FetchStatus.Ok or FetchStatus.None) && now - d0.FetchedAt > TimeSpan.FromMinutes(20), now);
        float cy = size.Height / 2f, rowGap = 9.5f * s;
        var d = vs.Data;

        if (variant == StripSize.Micro)
        {
            Micro(ctx, size, d?.FiveHour, d?.SevenDay);
            return;
        }

        using var label = Gfx.Semibold(11 * s);
        if (d is null)
        {
            // No numbers yet: say why instead of showing empty meters.
            using var small = Gfx.Ui(11 * s);
            float x = (variant == StripSize.Full ? 10 : 7) * s;
            string name = vs.Provider == Provider.ChatGpt ? "ChatGPT" : "Claude";
            Gfx.TextMid(g, variant == StripSize.Full ? name + " usage" : name, label, fg, x, cy - rowGap);
            Gfx.TextMid(g, Status(vs, now, variant == StripSize.Full), small, Gfx.A(fg, 150), x, cy + rowGap);
            return;
        }

        // Row labels come from the windows' lengths, so they stay right if a provider's windows differ.
        string top = Fmt.Label(d.FiveHour, "5h"), bottom = Fmt.Label(d.SevenDay, "7d");
        switch (variant)
        {
            case StripSize.Compact:
                Compact(ctx, top, d.FiveHour, cy - rowGap, label);
                Compact(ctx, bottom, d.SevenDay, cy + rowGap, label);
                break;
            case StripSize.Mini:
                Mini(ctx, top, d.FiveHour, cy - rowGap, label);
                Mini(ctx, bottom, d.SevenDay, cy + rowGap, label);
                break;
            default:
                Full(ctx, top, d.FiveHour, cy - rowGap, label);
                Full(ctx, bottom, d.SevenDay, cy + rowGap, label);
                break;
        }
    }

    public static string Status(ViewState vs, DateTimeOffset now, bool verbose)
    {
        if (vs.Fetching || vs.Status == FetchStatus.None) return verbose ? "Loading usage…" : "Loading…";
        return vs.Status switch
        {
            FetchStatus.RateLimited when vs.NextAttempt is { } n && n > now => verbose ? $"Rate limited · retry {Fmt.In(n - now)}" : $"Retry {Fmt.In(n - now)}",
            FetchStatus.RateLimited => verbose ? "Rate limited · retrying" : "Retrying",
            FetchStatus.NoCredentials => verbose ? "Click to set up" : "Set up",
            FetchStatus.Unauthorized => verbose ? "Click to sign in again" : "Sign in",
            _ => verbose ? "Offline · retrying" : "Offline",
        };
    }

    readonly record struct Ctx(Graphics G, float S, Color Fg, Palette Pal, bool Stale, DateTimeOffset Now)
    {
        public Color Dim => Gfx.A(Fg, 150);
        public Color Off => Gfx.A(Fg, 38);
        public Color On(UsageWindow? w)
        {
            var c = w is null ? Fg : Pal.Status(w.Percent);
            return Stale ? Gfx.Mix(c, Gfx.A(Fg, 120), 0.6f) : c;
        }
        public string Pct(UsageWindow? w) => w is null ? "–" : $"{Math.Round(w.Percent):0}%";
    }

    static void Full(Ctx c, string name, UsageWindow? w, float cy, Font label)
    {
        var (g, s) = (c.G, c.S);
        float x = 10 * s;
        Gfx.TextMid(g, name, label, c.Dim, x, cy);
        x += 17 * s;

        var on = c.On(w);
        double filled = w is null ? 0 : w.Percent / 100.0 * Segments;
        float top = cy - SegH * s / 2;
        for (int i = 0; i < Segments; i++)
        {
            var r = new RectangleF(x + i * (SegW + SegGap) * s, top, SegW * s, SegH * s);
            Gfx.FillRound(g, c.Off, r, 1.4f * s);
            double f = Math.Clamp(filled - i, 0, 1);
            if (f > 0.02)
            {
                // A partially-filled segment fills bottom-up so progress stays readable at a glance.
                var part = f >= 0.98 ? r : new RectangleF(r.X, r.Bottom - r.Height * (float)f, r.Width, r.Height * (float)f);
                var st = g.Save();
                g.SetClip(part);
                Gfx.FillRound(g, on, r, 1.4f * s);
                g.Restore(st);
            }
        }
        x += (SegBarW + 8) * s;

        using var pct = Gfx.Semibold(11.5f * s);
        Gfx.TextMid(g, c.Pct(w), pct, c.Stale ? c.Dim : c.Fg, x + 30 * s, cy, align: 1);
        x += 35 * s;

        if (w?.ResetsAt is { } reset)
        {
            using var small = Gfx.Ui(11 * s);
            Gfx.TextMid(g, Fmt.Short(reset - c.Now), small, c.Dim, x, cy);
        }
    }

    static void Compact(Ctx c, string name, UsageWindow? w, float cy, Font label)
    {
        var (g, s) = (c.G, c.S);
        float x = 8 * s;
        Gfx.TextMid(g, name, label, c.Dim, x, cy);
        x += 16 * s;

        var track = new RectangleF(x, cy - 2.5f * s, 30 * s, 5 * s);
        Gfx.FillRound(g, c.Off, track, 2.5f * s);
        if (w is { Percent: > 0 })
            Gfx.FillRound(g, c.On(w), new RectangleF(track.X, track.Y, Math.Max(track.Height, track.Width * (float)(w.Percent / 100)), track.Height), 2.5f * s);
        x += 36 * s;

        using var pct = Gfx.Semibold(11.5f * s);
        Gfx.TextMid(g, c.Pct(w), pct, c.Stale ? c.Dim : c.Fg, x + 30 * s, cy, align: 1);
    }

    static void Mini(Ctx c, string name, UsageWindow? w, float cy, Font label)
    {
        var (g, s) = (c.G, c.S);
        Gfx.TextMid(g, name, label, c.Dim, 7 * s, cy);
        // Only a number here, so it carries the status colour.
        using var pct = Gfx.Semibold(11.5f * s);
        Gfx.TextMid(g, c.Pct(w), pct, w is null ? c.Dim : c.On(w), (7 + 16 + 30) * c.S, cy, align: 1);
    }

    static void Micro(Ctx c, Size size, UsageWindow? five, UsageWindow? seven)
    {
        var (g, s) = (c.G, c.S);
        float h = Math.Min(28 * s, size.Height - 16 * s), top = (size.Height - h) / 2;
        float x = 7 * s;
        foreach (var w in new[] { five, seven })
        {
            var track = new RectangleF(x, top, 6 * s, h);
            Gfx.FillRound(g, c.Off, track, 3 * s);
            if (w is { Percent: > 0 })
            {
                float fh = Math.Max(track.Width, h * (float)(w.Percent / 100));
                Gfx.FillRound(g, c.On(w), new RectangleF(track.X, track.Bottom - fh, track.Width, fh), 3 * s);
            }
            x += 10 * s;
        }
    }
}
