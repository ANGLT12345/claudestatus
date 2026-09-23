using Microsoft.Win32;

namespace ClaudeUsageBar;

/// <summary>Taskbar strip sizes, largest first. Auto picks the largest that fits.</summary>
enum StripSize { Auto, Full, Compact, Mini, Micro }

static class Settings
{
    const string Key = @"Software\ClaudeUsageBar";

    /// <summary>Set once the first-run autostart default has been applied, so a user's "off" choice sticks.</summary>
    public static bool AutostartInitialised
    {
        get
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(Key);
                return k?.GetValue("AutostartInitialised") is int v && v == 1;
            }
            catch { return false; }
        }
        set
        {
            try
            {
                using var k = Registry.CurrentUser.CreateSubKey(Key);
                k.SetValue("AutostartInitialised", value ? 1 : 0, RegistryValueKind.DWord);
            }
            catch { }
        }
    }

    public static StripSize Size
    {
        get
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(Key);
                return Enum.TryParse<StripSize>(k?.GetValue("Size") as string, out var v) ? v : StripSize.Auto;
            }
            catch { return StripSize.Auto; }
        }
        set
        {
            try
            {
                using var k = Registry.CurrentUser.CreateSubKey(Key);
                k.SetValue("Size", value.ToString());
            }
            catch { }
        }
    }
}
