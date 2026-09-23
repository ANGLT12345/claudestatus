using System.Windows.Automation;
using Microsoft.Win32;

namespace ClaudeUsageBar;

/// <summary>
/// Where things actually are on the Windows 11 taskbar. The legacy child windows (ReBarWindow32 etc.)
/// report stale bounds on the XAML taskbar, so we ask UI Automation for the real button rectangles.
/// </summary>
sealed record TaskbarLayout(int? WidgetsLeft, int? WidgetsRight, int AppsLeft, int AppsRight, bool Centred)
{
    static readonly Condition Buttons = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button);

    /// <summary>Scans the taskbar. Call off the UI thread; returns null if UIA isn't available.</summary>
    public static TaskbarLayout? Scan(IntPtr taskbar, int trayLeft)
    {
        try
        {
            var root = AutomationElement.FromHandle(taskbar);
            int? wl = null, wr = null;
            int left = int.MaxValue, right = int.MinValue;
            foreach (AutomationElement b in root.FindAll(TreeScope.Descendants, Buttons))
            {
                var r = b.Current.BoundingRectangle;
                if (r.IsEmpty || r.Width <= 0 || r.Left >= trayLeft) continue;
                if (b.Current.AutomationId == "WidgetsButton") { wl = (int)r.Left; wr = (int)r.Right; continue; }
                left = Math.Min(left, (int)r.Left);
                right = Math.Max(right, (int)r.Right);
            }
            return left == int.MaxValue ? null : new TaskbarLayout(wl, wr, left, right, IsCentred());
        }
        catch { return null; }
    }

    static bool IsCentred()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
            return k?.GetValue("TaskbarAl") is not int v || v != 0; // Windows 11 default is centred
        }
        catch { return true; }
    }

    /// <summary>The free horizontal stretches of taskbar we could sit in, in screen coordinates.</summary>
    public IEnumerable<Gap> Gaps(int taskbarLeft, int trayLeft)
    {
        if (Centred)
        {
            // Widgets sits at the far left on a centred taskbar.
            int start = WidgetsRight is { } w && w <= AppsLeft ? w : taskbarLeft;
            yield return new Gap(start, AppsLeft, AnchorRight: false);
        }
        // On a left-aligned taskbar Widgets moves next to the tray.
        int end = WidgetsLeft is { } wl && wl >= AppsRight && wl < trayLeft ? wl : trayLeft;
        yield return new Gap(AppsRight, end, AnchorRight: true);
    }
}

/// <summary>A free stretch of taskbar. AnchorRight: hug the right end (next to the tray) rather than the left.</summary>
readonly record struct Gap(int Start, int End, bool AnchorRight)
{
    public int Width => End - Start;
}
