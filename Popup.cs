using System.Drawing.Drawing2D;

namespace ClaudeUsageBar;

enum RowKind { Session, Weekly, Sonnet, Opus, Extra }

sealed record RowModel(RowKind Kind, string Title, string Subtitle, double? Percent, string Detail, float? Pace);

/// <summary>Draws the detail flyout. Kept separate from the Form so it can also render to PNG.</summary>
static class PopupPainter
{
    public const float W = 348, Pad = 20, HeaderH = 62, RowH = 72, BannerH = 46, FooterH = 44;

    public static List<RowModel> Rows(ViewState vs, DateTimeOffset now)
    {
        var d = vs.Data;
        bool chatGpt = vs.Provider == Provider.ChatGpt;
        var rows = new List<RowModel>
        {
            Window(RowKind.Session, "Session", Fmt.Describe(d?.FiveHour?.Length ?? Fmt.FiveHours, ""), d?.FiveHour, Fmt.FiveHours, now),
            Window(RowKind.Weekly, "Weekly", chatGpt ? "Codex · all models" : "All models", d?.SevenDay, Fmt.SevenDays, now),
        };
        if (d?.SevenDaySonnet is { } son) rows.Add(Window(RowKind.Sonnet, "Sonnet", "Weekly · Sonnet only", son, Fmt.SevenDays, now));
        if (d?.SevenDayOpus is { } opus) rows.Add(Window(RowKind.Opus, "Opus", "Weekly · Opus only", opus, Fmt.SevenDays, now));
        if (d?.Extra is { Enabled: true } x)
        {
            string sub = x.UsedUsd is { } u && x.LimitUsd is { } l ? $"{Fmt.Money(u)} of {Fmt.Money(l)}" : "Pay-as-you-go credits";
            rows.Add(new RowModel(RowKind.Extra, "Extra usage", sub, x.Percent, "This month", null));
        }
        return rows;
    }

    static RowModel Window(RowKind k, string title, string sub, UsageWindow? w, TimeSpan length, DateTimeOffset now) =>
        new(k, title, sub, w?.Percent, w is null ? "" : Fmt.Reset(w.ResetsAt, now), w is null ? null : Fmt.Elapsed(w.ResetsAt, w.Length ?? length, now));

    public const float SetupH = 212;

    static bool ShowBanner(ViewState vs) => vs.Status is FetchStatus.RateLimited or FetchStatus.Error;
    public static bool NeedsSetup(ViewState vs) => vs.Status is FetchStatus.NoCredentials or FetchStatus.Unauthorized;
    static bool ShowRows(ViewState vs) => vs.Data is not null || !NeedsSetup(vs);
    static float RowsH(ViewState vs, DateTimeOffset now) => ShowRows(vs) ? Rows(vs, now).Count * RowH + 6 : 0;

    public static Size Measure(ViewState vs, float s, DateTimeOffset now)
    {
        float h = HeaderH + RowsH(vs, now) + (NeedsSetup(vs) ? SetupH : 0) + (ShowBanner(vs) ? BannerH : 0) + FooterH;
        return new Size((int)Math.Ceiling(W * s), (int)Math.Ceiling(h * s));
    }

    public static RectangleF SetupButton(ViewState vs, DateTimeOffset now, float s)
    {
        if (!NeedsSetup(vs)) return RectangleF.Empty;
        float top = HeaderH + RowsH(vs, now);
        return new RectangleF((Pad + 16) * s, (top + 152) * s, (W - Pad * 2 - 32) * s, 34 * s);
    }

    public static RectangleF RefreshButton(float s) => new((W - Pad - 32 + 6) * s, 15 * s, 32 * s, 32 * s);

    public static void Paint(Graphics g, Size size, float s, Palette p, ViewState vs, DateTimeOffset now, bool refreshHover, float spin, bool setupHover = false)
    {
        Gfx.Setup(g, clearType: true);
        using (var bg = new LinearGradientBrush(new Rectangle(0, 0, size.Width, size.Height), p.BgTop, p.BgBottom, 90f))
            g.FillRectangle(bg, 0, 0, size.Width, size.Height);

        Header(g, s, p, vs, refreshHover, spin);

        float y = HeaderH * s;
        if (ShowRows(vs))
        {
            foreach (var row in Rows(vs, now))
            {
                Row(g, s, p, row, y);
                y += RowH * s;
            }
            y += 6 * s;
        }

        if (NeedsSetup(vs))
        {
            Setup(g, s, p, vs, y, SetupButton(vs, now, s), setupHover);
            y += SetupH * s;
        }

        if (ShowBanner(vs))
        {
            Banner(g, s, p, vs, now, y);
            y += BannerH * s;
        }

        Footer(g, s, p, vs, now, y, size.Width);
    }

    static void Header(Graphics g, float s, Palette p, ViewState vs, bool hover, float spin)
    {
        float cy = 31 * s;
        using var title = Gfx.Display(17 * s, FontStyle.Bold);
        float tx = Pad * s;
        string name = vs.Provider == Provider.ChatGpt ? "ChatGPT" : "Claude";
        Gfx.TextMid(g, name, title, p.Text, tx, cy);
        float tw = Gfx.Measure(g, name, title).Width;

        string plan = Fmt.Plan(vs.Plan);
        if (plan.Length > 0)
        {
            using var pf = Gfx.Semibold(10.5f * s);
            float pw = Gfx.Measure(g, plan, pf).Width + 14 * s;
            var pill = new RectangleF(tx + tw + 9 * s, cy - 9.5f * s, pw, 19 * s);
            Gfx.FillRound(g, Gfx.A(Palette.Accent(vs.Provider), p.Light ? 30 : 42), pill, 9.5f * s);
            Gfx.TextMid(g, plan, pf, p.AccentText(vs.Provider), pill.X + pill.Width / 2, cy, 0.5f);
        }

        var btn = RefreshButton(s);
        if (hover) { using var b = new SolidBrush(p.Hover); g.FillEllipse(b, btn); }
        using var icon = Gfx.Icon(14 * s);
        var c = new PointF(btn.X + btn.Width / 2, btn.Y + btn.Height / 2);
        var st = g.Save();
        g.TranslateTransform(c.X, c.Y);
        g.RotateTransform(spin);
        var sz = Gfx.Measure(g, "", icon);
        using (var ib = new SolidBrush(hover ? p.Text : p.Sub))
            g.DrawString("", icon, ib, -sz.Width / 2, -icon.Size * 0.5f, Gfx.Typo);
        g.Restore(st);
    }

    static void Row(Graphics g, float s, Palette p, RowModel r, float y)
    {
        var color = r.Percent is { } pc ? p.Status(pc) : p.Muted;

        // Icon badge
        var badge = new RectangleF(Pad * s, y + 7 * s, 34 * s, 34 * s);
        Gfx.FillRound(g, Gfx.A(color, p.Light ? 26 : 34), badge, 10 * s);
        RowIcon(g, r.Kind, new PointF(badge.X + badge.Width / 2, badge.Y + badge.Height / 2), s, color);

        float tx = (Pad + 46) * s;
        using var title = Gfx.Semibold(14.5f * s);
        using var sub = Gfx.Ui(12 * s);
        Gfx.TextMid(g, r.Title, title, p.Text, tx, y + 16 * s);
        Gfx.TextMid(g, r.Subtitle, sub, p.Sub, tx, y + 34 * s);

        float right = (W - Pad) * s;
        using var pct = Gfx.Display(22 * s, FontStyle.Bold);
        Gfx.TextMid(g, r.Percent is { } v ? $"{Math.Round(v):0}%" : "—", pct, r.Percent is null ? p.Muted : color, right, y + 16 * s, 1);
        if (r.Detail.Length > 0)
        {
            using var det = Gfx.Ui(11.5f * s);
            Gfx.TextMid(g, r.Detail, det, p.Muted, right, y + 35 * s, 1);
        }

        // Progress bar
        var track = new RectangleF(Pad * s, y + 52 * s, (W - Pad * 2) * s, 6 * s);
        Gfx.FillRound(g, p.Track, track, 3 * s);
        if (r.Percent is { } val && val > 0)
        {
            float fw = Math.Max(track.Height, track.Width * (float)(val / 100));
            var fill = new RectangleF(track.X, track.Y, fw, track.Height);
            using var brush = new LinearGradientBrush(new RectangleF(track.X - 1, track.Y, fw + 2, track.Height),
                Gfx.Mix(color, p.Light ? Color.White : Color.Black, 0.18f), color, 0f);
            using var path = Gfx.Round(fill, 3 * s);
            g.FillPath(brush, path);
        }

        // Pace marker: where usage would be if spread evenly across the window.
        if (r.Pace is { } pace && pace > 0.01f && pace < 0.99f)
        {
            float mx = track.X + track.Width * pace;
            var mark = new RectangleF(mx - 1 * s, track.Y - 3 * s, 2 * s, track.Height + 6 * s);
            Gfx.FillRound(g, p.Light ? p.BgBottom : p.BgBottom, RectangleF.Inflate(mark, 1.5f * s, 0), 2 * s);
            Gfx.FillRound(g, Gfx.A(p.Text, 150), mark, 1 * s);
        }
    }

    static void RowIcon(Graphics g, RowKind k, PointF c, float s, Color color)
    {
        using var pen = new Pen(color, 1.7f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var brush = new SolidBrush(color);
        switch (k)
        {
            case RowKind.Session: // stopwatch
            {
                float r = 6.5f * s;
                var cc = new PointF(c.X, c.Y + 1 * s);
                g.DrawEllipse(pen, cc.X - r, cc.Y - r, r * 2, r * 2);
                g.DrawLine(pen, cc.X, cc.Y, cc.X, cc.Y - r * 0.55f);
                g.DrawLine(pen, cc.X, cc.Y, cc.X + r * 0.45f, cc.Y + r * 0.2f);
                g.DrawLine(pen, cc.X - 2 * s, cc.Y - r - 2.4f * s, cc.X + 2 * s, cc.Y - r - 2.4f * s);
                break;
            }
            case RowKind.Weekly: // ascending bars
            {
                float bw = 3.2f * s, gap = 1.9f * s, baseY = c.Y + 6.5f * s;
                float[] hs = { 6f, 9.5f, 13f };
                float x = c.X - (bw * 3 + gap * 2) / 2;
                foreach (var h in hs)
                {
                    Gfx.FillRound(g, color, new RectangleF(x, baseY - h * s, bw, h * s), 1 * s);
                    x += bw + gap;
                }
                break;
            }
            case RowKind.Sonnet: // sparkle
            {
                Sparkle(g, brush, new PointF(c.X - 1.2f * s, c.Y + 1.2f * s), 6.5f * s);
                Sparkle(g, brush, new PointF(c.X + 5 * s, c.Y - 5 * s), 3 * s);
                break;
            }
            case RowKind.Opus: // diamond
            {
                var pts = new[] { new PointF(c.X, c.Y - 7 * s), new PointF(c.X + 6.5f * s, c.Y - 1.5f * s), new PointF(c.X, c.Y + 7 * s), new PointF(c.X - 6.5f * s, c.Y - 1.5f * s) };
                g.DrawPolygon(pen, pts);
                g.DrawLine(pen, pts[3], pts[1]);
                break;
            }
            case RowKind.Extra: // card
            {
                var r = new RectangleF(c.X - 8 * s, c.Y - 5.5f * s, 16 * s, 11 * s);
                using var path = Gfx.Round(r, 2 * s);
                g.DrawPath(pen, path);
                g.DrawLine(pen, r.X, r.Y + 3.8f * s, r.Right, r.Y + 3.8f * s);
                g.DrawLine(pen, r.X + 3 * s, r.Bottom - 3 * s, r.X + 6 * s, r.Bottom - 3 * s);
                break;
            }
        }
    }

    static void Sparkle(Graphics g, Brush b, PointF c, float r)
    {
        float k = r * 0.22f;
        using var path = new GraphicsPath();
        path.AddBezier(c.X, c.Y - r, c.X + k * 0.4f, c.Y - k, c.X + k, c.Y - k * 0.4f, c.X + r, c.Y);
        path.AddBezier(c.X + r, c.Y, c.X + k, c.Y + k * 0.4f, c.X + k * 0.4f, c.Y + k, c.X, c.Y + r);
        path.AddBezier(c.X, c.Y + r, c.X - k * 0.4f, c.Y + k, c.X - k, c.Y + k * 0.4f, c.X - r, c.Y);
        path.AddBezier(c.X - r, c.Y, c.X - k, c.Y - k * 0.4f, c.X - k * 0.4f, c.Y - k, c.X, c.Y - r);
        g.FillPath(b, path);
    }

    /// <summary>Walks the user through signing in to Claude Code (or Codex, for ChatGPT), which is where the usage token comes from.</summary>
    static void Setup(Graphics g, float s, Palette p, ViewState vs, float y, RectangleF button, bool hover)
    {
        bool expired = vs.Status == FetchStatus.Unauthorized;
        bool chatGpt = vs.Provider == Provider.ChatGpt;
        string cliName = chatGpt ? "Codex" : "Claude Code";
        var card = new RectangleF(Pad * s, y, (W - Pad * 2) * s, (SetupH - 12) * s);
        Gfx.FillRound(g, Gfx.Mix(p.BgTop, p.Text, p.Light ? 0.03f : 0.035f), card, 12 * s);
        using (var pen = new Pen(p.Divider, Math.Max(1, s)))
        using (var path = Gfx.Round(card, 12 * s))
            g.DrawPath(pen, path);

        float x = card.X + 16 * s;
        using var title = Gfx.Semibold(14.5f * s);
        using var sub = Gfx.Ui(12 * s);
        Gfx.TextMid(g, expired ? "Sign in again" : $"Connect {cliName}", title, p.Text, x, y + 24 * s);
        Gfx.TextMid(g, expired ? $"Your {cliName} login has expired." : $"Usage comes from your {cliName} login.", sub, p.Sub, x, y + 44 * s);

        var steps = chatGpt
            ? vs.CliInstalled
                ? new[]
                {
                    new (string, bool)[] { ("Run ", false), ("codex login", true), (" in a terminal", false) },
                    new (string, bool)[] { ("Sign in with your ChatGPT account", false) },
                    new (string, bool)[] { ("Finish in the browser — this updates itself", false) },
                }
                : new[]
                {
                    new (string, bool)[] { ("Install Codex (button below)", false) },
                    new (string, bool)[] { ("Run ", false), ("codex login", true), (" in a terminal", false) },
                    new (string, bool)[] { ("Finish in the browser — this updates itself", false) },
                }
            : vs.CliInstalled
            ? new[]
            {
                new (string, bool)[] { ("Run ", false), ("claude", true), (" in a terminal", false) },
                new (string, bool)[] { ("Type ", false), ("/login", true), (" and choose your plan", false) },
                new (string, bool)[] { ("Finish in the browser — this updates itself", false) },
            }
            : new[]
            {
                new (string, bool)[] { ("Install Claude Code (button below)", false) },
                new (string, bool)[] { ("Run ", false), ("claude", true), (", then type ", false), ("/login", true) },
                new (string, bool)[] { ("Finish in the browser — this updates itself", false) },
            };

        using var num = Gfx.Semibold(10.5f * s);
        using var body = Gfx.Ui(12.5f * s);
        using var code = new Font(CodeFamily, 11.5f * s, FontStyle.Regular, GraphicsUnit.Pixel);
        var brand = Palette.Accent(vs.Provider);
        var accent = p.AccentText(vs.Provider);
        for (int i = 0; i < steps.Length; i++)
        {
            float cy = y + (76 + i * 25) * s;
            float r = 9 * s;
            using (var b = new SolidBrush(Gfx.A(brand, p.Light ? 34 : 48)))
                g.FillEllipse(b, x, cy - r, r * 2, r * 2);
            Gfx.TextMid(g, (i + 1).ToString(), num, accent, x + r, cy, 0.5f);

            float tx = x + r * 2 + 10 * s;
            foreach (var (text, isCode) in steps[i])
            {
                if (isCode)
                {
                    float cw = Gfx.Measure(g, text, code).Width;
                    Gfx.FillRound(g, p.Track, new RectangleF(tx, cy - 9.5f * s, cw + 10 * s, 19 * s), 5 * s);
                    Gfx.TextMid(g, text, code, p.Text, tx + 5 * s, cy);
                    tx += cw + 10 * s;
                }
                else
                {
                    Gfx.TextMid(g, text, body, p.Sub, tx, cy);
                    tx += Gfx.Measure(g, text, body).Width;
                }
            }
        }

        var fill = hover ? Gfx.Mix(brand, Color.White, 0.12f) : brand;
        Gfx.FillRound(g, fill, button, 8 * s);
        string label = vs.CliInstalled ? (expired ? "Open terminal to sign in" : "Open terminal") : $"Install {cliName}";
        using var bf = Gfx.Semibold(13 * s);
        using var icon = Gfx.Icon(13 * s);
        string glyph = vs.CliInstalled ? "" : ""; // CommandPrompt / Download
        float lw = Gfx.Measure(g, label, bf).Width, iw = 13 * s, gap = 8 * s;
        float bx = button.X + (button.Width - lw - iw - gap) / 2, bcy = button.Y + button.Height / 2;
        Gfx.TextMid(g, glyph, icon, Color.White, bx, bcy);
        Gfx.TextMid(g, label, bf, Color.White, bx + iw + gap, bcy);
    }

    static readonly string CodeFamily = FontFamily.Families.Any(f => f.Name == "Cascadia Mono") ? "Cascadia Mono" : "Consolas";

    static void Banner(Graphics g, float s, Palette p, ViewState vs, DateTimeOffset now, float y)
    {
        bool severe = vs.Status is FetchStatus.Unauthorized or FetchStatus.NoCredentials;
        var color = severe ? p.Red : p.Amber;
        var r = new RectangleF(Pad * s, y, (W - Pad * 2) * s, (BannerH - 10) * s);
        Gfx.FillRound(g, Gfx.A(color, p.Light ? 24 : 30), r, 8 * s);

        float cy = r.Y + r.Height / 2;
        using var icon = Gfx.Icon(13 * s);
        Gfx.TextMid(g, "", icon, color, r.X + 12 * s, cy);

        string msg = vs.Message ?? "Couldn't update";
        if (!severe && vs.NextAttempt is { } next && next > now)
            msg += $" · retry in {Fmt.In(next - now)}";
        using var f = Gfx.Ui(12 * s);
        Gfx.TextMid(g, msg, f, p.Light ? Gfx.Mix(color, Color.Black, 0.25f) : Gfx.Mix(color, Color.White, 0.2f), r.X + 34 * s, cy);
    }

    static void Footer(Graphics g, float s, Palette p, ViewState vs, DateTimeOffset now, float y, int width)
    {
        using (var pen = new Pen(p.Divider, Math.Max(1, s)))
            g.DrawLine(pen, Pad * s, y, width - Pad * s, y);
        float cy = y + FooterH * s / 2;
        using var f = Gfx.Ui(11.5f * s);

        string left = vs.Fetching ? "Refreshing…"
            : vs.Data is { } d ? "Updated " + Fmt.Ago(now - d.FetchedAt)
            : NeedsSetup(vs) ? "Waiting for sign-in…"
            : "Waiting for first update";
        Gfx.TextMid(g, left, f, p.Muted, Pad * s, cy);

        if (NeedsSetup(vs)) return;
        bool ok = vs.Status is FetchStatus.Ok or FetchStatus.None;
        string right = $"Every {(int)vs.PollInterval.TotalMinutes} min";
        float rx = (W - Pad) * s;
        float tw = Gfx.Measure(g, right, f).Width;
        Gfx.TextMid(g, right, f, p.Muted, rx, cy, 1);
        using var dot = new SolidBrush(ok ? p.Green : p.Amber);
        float dr = 3.2f * s;
        g.FillEllipse(dot, rx - tw - 8 * s - dr, cy - dr, dr * 2, dr * 2);
    }
}

sealed class PopupForm : Form
{
    public event Action? RefreshRequested;
    public event Action? SetupRequested;

    ViewState _state;
    float _scale = 1;
    Palette _palette = Palette.ForApps();
    bool _refreshHover, _setupHover;
    float _spin;
    Rectangle _anchor;
    readonly System.Windows.Forms.Timer _anim = new() { Interval = 15 };
    readonly System.Windows.Forms.Timer _outside = new() { Interval = 50 };
    int _slideFrom, _slideTo;
    DateTime _slideStart;

    public DateTime LastHidden { get; private set; }

    public PopupForm(ViewState state)
    {
        _state = state;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.None;
        KeyPreview = true;
        Text = "Claude usage";
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

        _anim.Tick += (_, _) => Animate();
        _outside.Tick += (_, _) =>
        {
            if (Visible && MouseButtons != MouseButtons.None && !Bounds.Contains(Cursor.Position) && !_anchor.Contains(Cursor.Position))
                HidePopup();
        };
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= Native.CS_DROPSHADOW;
            cp.ExStyle |= Native.WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => false;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyChrome();
    }

    void ApplyChrome()
    {
        Native.SetDwmInt(Handle, Native.DWMWA_WINDOW_CORNER_PREFERENCE, Native.DWMWCP_ROUND);
        Native.SetDwmInt(Handle, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, _palette.Light ? 0 : 1);
        Native.SetDwmInt(Handle, Native.DWMWA_BORDER_COLOR, Native.ColorRef(_palette.Border));
    }

    public void Update(ViewState state)
    {
        _state = state;
        if (!Visible) return;
        var size = PopupPainter.Measure(_state, _scale, DateTimeOffset.Now);
        if (size != Size) Place(size, animate: false);
        _anim.Enabled = _state.Fetching || _slideStart != default;
        Invalidate();
    }

    public void ShowAt(Rectangle anchor, float scale)
    {
        _anchor = anchor;
        _scale = scale;
        var pal = Palette.ForApps();
        if (pal != _palette) { _palette = pal; if (IsHandleCreated) ApplyChrome(); }

        var size = PopupPainter.Measure(_state, _scale, DateTimeOffset.Now);
        Place(size, animate: true);
        Show();
        Activate();
        Native.SetForegroundWindow(Handle);
        _outside.Start();
    }

    void Place(Size size, bool animate)
    {
        var work = Screen.FromRectangle(_anchor).WorkingArea;
        int gap = (int)(10 * _scale);
        bool above = _anchor.Top >= work.Bottom - 2 || _anchor.Top > work.Top + work.Height / 2;
        int x = Math.Clamp(_anchor.Right - size.Width, work.Left + gap, work.Right - size.Width - gap);
        int y = above ? work.Bottom - size.Height - gap : work.Top + gap;
        if (_anchor.Left >= work.Right - 2) { x = work.Right - size.Width - gap; y = Math.Clamp(_anchor.Bottom - size.Height, work.Top + gap, work.Bottom - size.Height - gap); }

        _slideTo = y;
        if (animate)
        {
            _slideFrom = y + (int)((above ? 14 : -14) * _scale);
            _slideStart = DateTime.UtcNow;
            _anim.Start();
            Bounds = new Rectangle(x, _slideFrom, size.Width, size.Height);
        }
        else
        {
            Bounds = new Rectangle(x, _slideStart == default ? y : Top, size.Width, size.Height);
        }
    }

    void Animate()
    {
        bool busy = false;
        if (_slideStart != default)
        {
            double t = Math.Min(1, (DateTime.UtcNow - _slideStart).TotalMilliseconds / 180);
            double e = 1 - Math.Pow(1 - t, 3); // ease-out cubic
            Top = (int)Math.Round(_slideFrom + (_slideTo - _slideFrom) * e);
            if (t >= 1) _slideStart = default; else busy = true;
        }
        if (_state.Fetching)
        {
            _spin = (_spin + 9) % 360;
            Invalidate(Rectangle.Ceiling(PopupPainter.RefreshButton(_scale)));
            busy = true;
        }
        else if (_spin != 0)
        {
            _spin = 0;
            Invalidate();
        }
        if (!busy) _anim.Stop();
    }

    public void HidePopup()
    {
        if (!Visible) return;
        _outside.Stop();
        _anim.Stop();
        _slideStart = default;
        Hide();
        LastHidden = DateTime.UtcNow;
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        HidePopup();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) HidePopup();
        else if (e.KeyCode == Keys.F5 || (e.Control && e.KeyCode == Keys.R)) RefreshRequested?.Invoke();
        base.OnKeyDown(e);
    }

    RectangleF SetupRect => PopupPainter.SetupButton(_state, DateTimeOffset.Now, _scale);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        bool refresh = PopupPainter.RefreshButton(_scale).Contains(e.Location);
        bool setup = SetupRect.Contains(e.Location);
        if (refresh != _refreshHover || setup != _setupHover)
        {
            _refreshHover = refresh;
            _setupHover = setup;
            Cursor = refresh || setup ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (_refreshHover || _setupHover) { _refreshHover = _setupHover = false; Cursor = Cursors.Default; Invalidate(); }
        base.OnMouseLeave(e);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            if (PopupPainter.RefreshButton(_scale).Contains(e.Location)) RefreshRequested?.Invoke();
            else if (SetupRect.Contains(e.Location)) SetupRequested?.Invoke();
        }
        base.OnMouseClick(e);
    }

    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void OnPaint(PaintEventArgs e) =>
        PopupPainter.Paint(e.Graphics, ClientSize, _scale, _palette, _state, DateTimeOffset.Now, _refreshHover, _spin, _setupHover);

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _anim.Dispose(); _outside.Dispose(); }
        base.Dispose(disposing);
    }
}
