using System.Drawing.Drawing2D;
using System.Drawing.Text;
using Microsoft.Win32;

namespace ClaudeUsageBar;

sealed class Palette
{
    public required bool Light;
    public required Color BgTop, BgBottom, Border, Text, Sub, Muted, Track, Divider, Hover, Pressed;
    public required Color Green, Amber, Red;
    public static readonly Color Claude = Color.FromArgb(217, 119, 87);
    public static readonly Color ChatGpt = Color.FromArgb(16, 163, 127);
    public static readonly Color Gemini = Color.FromArgb(66, 133, 244);

    /// <summary>The provider's brand colour, for buttons, badges and step numbers.</summary>
    public static Color Accent(Provider p) => p switch
    {
        Provider.ChatGpt => ChatGpt,
        Provider.Gemini => Gemini,
        _ => Claude,
    };

    /// <summary>Accent readable as text on the popup background.</summary>
    public Color AccentText(Provider p) => p switch
    {
        Provider.ChatGpt => Light ? Color.FromArgb(13, 122, 95) : Color.FromArgb(86, 212, 168),
        Provider.Gemini => Light ? Color.FromArgb(26, 95, 200) : Color.FromArgb(138, 180, 248),
        _ => Light ? Color.FromArgb(180, 83, 50) : Color.FromArgb(240, 150, 118),
    };

    public Color Status(double pct) => pct >= 90 ? Red : pct >= 70 ? Amber : Green;

    public static readonly Palette Dark = new()
    {
        Light = false,
        BgTop = Color.FromArgb(30, 30, 33),
        BgBottom = Color.FromArgb(19, 19, 21),
        Border = Color.FromArgb(58, 58, 62),
        Text = Color.FromArgb(244, 244, 246),
        Sub = Color.FromArgb(152, 152, 160),
        Muted = Color.FromArgb(112, 112, 120),
        Track = Color.FromArgb(44, 44, 48),
        Divider = Color.FromArgb(42, 42, 46),
        Hover = Color.FromArgb(24, 255, 255, 255),
        Pressed = Color.FromArgb(14, 255, 255, 255),
        Green = Color.FromArgb(52, 208, 120),
        Amber = Color.FromArgb(245, 176, 62),
        Red = Color.FromArgb(242, 88, 88),
    };

    public static readonly Palette LightTheme = new()
    {
        Light = true,
        BgTop = Color.FromArgb(252, 252, 253),
        BgBottom = Color.FromArgb(243, 243, 246),
        Border = Color.FromArgb(214, 214, 220),
        Text = Color.FromArgb(28, 28, 30),
        Sub = Color.FromArgb(92, 92, 100),
        Muted = Color.FromArgb(138, 138, 146),
        Track = Color.FromArgb(226, 226, 232),
        Divider = Color.FromArgb(228, 228, 233),
        Hover = Color.FromArgb(16, 0, 0, 0),
        Pressed = Color.FromArgb(10, 0, 0, 0),
        Green = Color.FromArgb(22, 163, 74),
        Amber = Color.FromArgb(217, 119, 6),
        Red = Color.FromArgb(220, 38, 38),
    };

    public static Palette ForApps() => ReadLight("AppsUseLightTheme") ? LightTheme : Dark;
    public static bool TaskbarIsLight() => ReadLight("SystemUsesLightTheme");

    static bool ReadLight(string name)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue(name) is int v && v == 1;
        }
        catch { return false; }
    }
}

static class Gfx
{
    static readonly string UiFamily = Installed("Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI";
    static readonly string DisplayFamily = Installed("Segoe UI Variable Display") ? "Segoe UI Variable Display" : "Segoe UI";
    public static readonly string IconFamily = Installed("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";

    static bool Installed(string family)
    {
        using var fonts = new InstalledFontCollection();
        return fonts.Families.Any(f => f.Name.Equals(family, StringComparison.OrdinalIgnoreCase));
    }

    public static Font Ui(float px, FontStyle style = FontStyle.Regular) => new(UiFamily, px, style, GraphicsUnit.Pixel);
    public static Font Display(float px, FontStyle style = FontStyle.Bold) => new(DisplayFamily, px, style, GraphicsUnit.Pixel);
    public static Font Semibold(float px) => new(Installed("Segoe UI Semibold") ? "Segoe UI Semibold" : "Segoe UI", px, FontStyle.Regular, GraphicsUnit.Pixel);
    public static Font Icon(float px) => new(IconFamily, px, FontStyle.Regular, GraphicsUnit.Pixel);

    public static readonly StringFormat Typo = CreateTypo();
    static StringFormat CreateTypo()
    {
        var f = (StringFormat)StringFormat.GenericTypographic.Clone();
        f.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces;
        return f;
    }

    public static void Setup(Graphics g, bool clearType)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.TextRenderingHint = clearType ? TextRenderingHint.ClearTypeGridFit : TextRenderingHint.AntiAliasGridFit;
    }

    public static SizeF Measure(Graphics g, string s, Font f) => g.MeasureString(s, f, int.MaxValue, Typo);

    /// <summary>Draws text with its top-left at (x, y); align: 0 left, 1 right (x is the right edge), 0.5 center.</summary>
    public static void Text(Graphics g, string s, Font f, Color c, float x, float y, float align = 0)
    {
        if (align != 0) x -= Measure(g, s, f).Width * align;
        using var b = new SolidBrush(c);
        g.DrawString(s, f, b, x, y, Typo);
    }

    /// <summary>Draws text vertically centred on cy using the font's cap height, which looks centred to the eye.</summary>
    public static void TextMid(Graphics g, string s, Font f, Color c, float x, float cy, float align = 0)
    {
        var fam = f.FontFamily;
        float em = f.Size;
        float ascent = em * fam.GetCellAscent(f.Style) / fam.GetEmHeight(f.Style);
        float cap = em * 0.70f;
        Text(g, s, f, c, x, cy - ascent + cap / 2f, align);
    }

    public static GraphicsPath Round(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0.5f) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void FillRound(Graphics g, Color c, RectangleF r, float radius)
    {
        using var b = new SolidBrush(c);
        using var p = Round(r, radius);
        g.FillPath(b, p);
    }

    public static Color A(Color c, int alpha) => Color.FromArgb(Math.Clamp(alpha, 0, 255), c);
    public static Color Mix(Color a, Color b, float t) => Color.FromArgb(
        (int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    /// <summary>The Claude spark: rays of uneven length around a centre.</summary>
    public static void ClaudeMark(Graphics g, PointF c, float r, Color color)
    {
        float[] len = { 1.00f, 0.78f, 0.92f, 0.70f, 1.00f, 0.82f, 0.95f, 0.72f, 0.98f, 0.80f, 0.90f, 0.74f };
        using var pen = new Pen(color, r * 0.22f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        for (int i = 0; i < len.Length; i++)
        {
            double a = (i * 30 + 8) * Math.PI / 180;
            float inner = r * 0.16f, outer = r * len[i];
            g.DrawLine(pen,
                c.X + (float)Math.Cos(a) * inner, c.Y + (float)Math.Sin(a) * inner,
                c.X + (float)Math.Cos(a) * outer, c.Y + (float)Math.Sin(a) * outer);
        }
    }
}
