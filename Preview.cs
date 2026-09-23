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

        using (var icon = Icon(256)) icon.Save(Path.Combine(dir, "icon.png"), ImageFormat.Png);

        const float s = 1.5f;
        Popup(sample, Palette.Dark, s, Path.Combine(dir, "popup-dark.png"));
        Popup(sample, Palette.LightTheme, s, Path.Combine(dir, "popup-light.png"));
        Popup(limited, Palette.Dark, s, Path.Combine(dir, "popup-limited.png"));
        Popup(setup, Palette.Dark, s, Path.Combine(dir, "popup-setup.png"));
        Strips(sample, false, s, Path.Combine(dir, "strip-sizes-dark.png"));
        Strips(sample, true, s, Path.Combine(dir, "strip-sizes-light.png"));
        Strips(setup, false, s, Path.Combine(dir, "strip-setup.png"));
    }

    /// <summary>
    /// Draws the app icon: two usage gauges (like the Micro strip) on a dark rounded square.
    /// Run with --icon &lt;file.ico&gt; to regenerate app.ico.
    /// </summary>
    public static Bitmap Icon(int n, bool mac = false)
    {
        var bmp = new Bitmap(n, n, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        Gfx.Setup(g, clearType: false);
        g.Clear(Color.Transparent);

        // macOS icons sit inside a ~10% margin on the canvas, per Apple's icon grid.
        float inset = mac ? n * 0.1f : n <= 24 ? 0 : n * 0.04f;
        var tile = new RectangleF(inset, inset, n - inset * 2, n - inset * 2);
        using (var bg = new System.Drawing.Drawing2D.LinearGradientBrush(tile, Color.FromArgb(52, 52, 58), Color.FromArgb(22, 22, 26), 90f))
        using (var path = Gfx.Round(tile, tile.Width * 0.24f))
        {
            g.FillPath(bg, path);
            // Faint edge so the tile doesn't vanish on dark taskbars and backgrounds.
            using var edge = new Pen(Color.FromArgb(40, 255, 255, 255), Math.Max(1f, n / 64f));
            g.DrawPath(edge, path);
        }

        // Two gauges: session (green, lower) and weekly (amber, higher).
        float bw = tile.Width * 0.19f, gap = tile.Width * 0.13f, h = tile.Height * 0.58f;
        float x = tile.X + (tile.Width - bw * 2 - gap) / 2, top = tile.Y + (tile.Height - h) / 2;
        var track = Color.FromArgb(60, 255, 255, 255);
        foreach (var (fill, color) in new[] { (0.45f, Palette.Dark.Green), (0.78f, Palette.Dark.Amber) })
        {
            var r = new RectangleF(x, top, bw, h);
            Gfx.FillRound(g, track, r, bw / 2);
            float fh = h * fill;
            Gfx.FillRound(g, color, new RectangleF(x, r.Bottom - fh, bw, fh), bw / 2);
            x += bw + gap;
        }
        return bmp;
    }

    /// <summary>
    /// Writes a multi-resolution .ico. Sizes below 256 are classic 32-bit DIBs (readable everywhere);
    /// 256 is PNG-compressed, as Windows expects.
    /// </summary>
    public static void WriteIco(string path)
    {
        int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
        var images = sizes.Select(n =>
        {
            using var b = Icon(n);
            using var ms = new MemoryStream();
            if (n >= 256) b.Save(ms, ImageFormat.Png);
            else WriteDib(b, ms);
            return ms.ToArray();
        }).ToArray();

        using var w = new BinaryWriter(File.Create(path));
        w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
        int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)0); w.Write((byte)0);
            w.Write((short)1); w.Write((short)32);
            w.Write(images[i].Length); w.Write(offset);
            offset += images[i].Length;
        }
        foreach (var img in images) w.Write(img);
    }

    /// <summary>Writes the PNGs for a macOS .iconset folder (turned into .icns by iconutil at build time).</summary>
    public static void WriteIconset(string dir)
    {
        Directory.CreateDirectory(dir);
        foreach (var pt in new[] { 16, 32, 128, 256, 512 })
            foreach (var scale in new[] { 1, 2 })
            {
                using var b = Icon(pt * scale, mac: true);
                b.Save(Path.Combine(dir, scale == 1 ? $"icon_{pt}x{pt}.png" : $"icon_{pt}x{pt}@2x.png"), ImageFormat.Png);
            }
    }

    /// <summary>Icon-style DIB: BITMAPINFOHEADER with doubled height, bottom-up BGRA pixels, then an empty AND mask.</summary>
    static void WriteDib(Bitmap b, Stream s)
    {
        int n = b.Width;
        using var w = new BinaryWriter(s, System.Text.Encoding.UTF8, leaveOpen: true);
        int maskStride = ((n + 31) / 32) * 4;
        w.Write(40); w.Write(n); w.Write(n * 2);
        w.Write((short)1); w.Write((short)32);
        w.Write(0); w.Write(n * n * 4 + maskStride * n);
        w.Write(0); w.Write(0); w.Write(0); w.Write(0);
        for (int y = n - 1; y >= 0; y--)
            for (int x = 0; x < n; x++)
            {
                var c = b.GetPixel(x, y);
                w.Write(c.B); w.Write(c.G); w.Write(c.R); w.Write(c.A);
            }
        w.Write(new byte[maskStride * n]);
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
