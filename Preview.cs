using System.Drawing.Imaging;

namespace ClaudeUsageBar;

/// <summary>Renders the strip and popup with sample data to PNGs (run with --render &lt;dir&gt;). Used for the README.</summary>
static class Preview
{
    public static void Render(string dir)
    {
        Directory.CreateDirectory(dir);
        var now = DateTimeOffset.Now;
        var sample = new ViewState
        {
            Plan = "pro",
            Status = FetchStatus.Ok,
            Data = new UsageSnapshot
            {
                FiveHour = new(37, now.AddHours(3).AddMinutes(1)),
                SevenDay = new(74, now.AddDays(2).AddHours(4)),
                SevenDaySonnet = new(3, now.AddDays(2).AddHours(5)),
                Extra = new(true, 150, 5.59, 3.7),
                FetchedAt = now.AddMinutes(-2),
            },
        };
        var limited = new ViewState
        {
            Plan = "max",
            Status = FetchStatus.RateLimited,
            Message = "Rate limited by Anthropic",
            NextAttempt = now.AddMinutes(8),
            Data = new UsageSnapshot { FiveHour = new(93, now.AddMinutes(42)), SevenDay = new(58, now.AddDays(4)), FetchedAt = now.AddMinutes(-11) },
        };
        var setup = new ViewState { Plan = "pro", Status = FetchStatus.NoCredentials, CliInstalled = true };

        const float s = 1.5f;
        Popup(sample, Palette.Dark, s, Path.Combine(dir, "popup-dark.png"));
        Popup(sample, Palette.LightTheme, s, Path.Combine(dir, "popup-light.png"));
        Popup(limited, Palette.Dark, s, Path.Combine(dir, "popup-limited.png"));
        Popup(setup, Palette.Dark, s, Path.Combine(dir, "popup-setup.png"));
        Strips(sample, false, s, Path.Combine(dir, "strip-sizes-dark.png"));
        Strips(sample, true, s, Path.Combine(dir, "strip-sizes-light.png"));
        Strips(setup, false, s, Path.Combine(dir, "strip-setup.png"));
    }

    static void Popup(ViewState vs, Palette p, float s, string path)
    {
        var size = PopupPainter.Measure(vs, s, DateTimeOffset.Now);
        using var bmp = new Bitmap(size.Width, size.Height);
        using (var g = Graphics.FromImage(bmp))
            PopupPainter.Paint(g, size, s, p, vs, DateTimeOffset.Now, false, 0);
        bmp.Save(path, ImageFormat.Png);
    }

    /// <summary>All four strip sizes side by side on a taskbar-coloured backdrop.</summary>
    static void Strips(ViewState vs, bool light, float s, string path)
    {
        int h = (int)(48 * s), gap = (int)(24 * s);
        int total = StripPainter.BySize.Sum(v => StripPainter.Width(v, s) + gap) + gap;
        using var bmp = new Bitmap(total, h);
        using var g = Graphics.FromImage(bmp);
        g.Clear(light ? Color.FromArgb(238, 238, 242) : Color.FromArgb(32, 32, 36));
        int x = gap;
        foreach (var v in StripPainter.BySize)
        {
            int w = StripPainter.Width(v, s);
            using var strip = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var sg = Graphics.FromImage(strip))
                StripPainter.Paint(sg, new Size(w, h), s, v, light, false, false, vs, DateTimeOffset.Now);
            g.DrawImage(strip, x, 0);
            x += w + gap;
        }
        bmp.Save(path, ImageFormat.Png);
    }
}
