using Microsoft.Win32;

namespace ClaudeUsageBar;

sealed class AppController : ApplicationContext
{
    static readonly TimeSpan Poll = TimeSpan.FromMinutes(5);
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run", RunName = "ClaudeUsageBar";

    readonly ViewState _vs = new() { PollInterval = Poll };
    UsageClient _client = UsageClient.For(Settings.Provider);
    readonly StripWindow _strip;
    readonly PopupForm _popup;
    readonly ContextMenuStrip _menu;
    readonly ToolStripMenuItem _startupItem;
    readonly System.Windows.Forms.Timer _tick = new() { Interval = 1000 };

    DateTimeOffset _nextPoll, _lastAttempt;
    int _failures;
    int _lastMinute = -1;

    public AppController()
    {
        LoadProvider();

        _popup = new PopupForm(_vs);
        _popup.RefreshRequested += () => _ = Fetch(manual: true);
        _popup.SetupRequested += OpenSetup;

        InitAutostart();
        _startupItem = new ToolStripMenuItem("Start with Windows", null, (_, _) => ToggleStartup()) { Checked = StartupEnabled() };
        _menu = BuildMenu();

        _strip = new StripWindow(_vs);
        _strip.Clicked += TogglePopup;
        _strip.RightClicked += ShowMenu;

        SystemEvents.UserPreferenceChanged += OnPrefsChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;

        _tick.Tick += (_, _) => Tick();
        _tick.Start();
        Tick();
    }

    /// <summary>Resets the view to the current provider, starting from its last cached numbers.</summary>
    void LoadProvider()
    {
        var (cached, plan) = _client.LoadCache();
        _vs.Provider = _client.Provider;
        _vs.Data = cached;
        _vs.Plan = plan;
        _vs.Status = FetchStatus.None;
        _vs.Message = null;
        _vs.NextAttempt = null;
        _vs.Fetching = false;
        _vs.CliInstalled = true;
        _failures = 0;
        var now = DateTimeOffset.Now;
        _nextPoll = cached is not null && now - cached.FetchedAt < Poll ? cached.FetchedAt + Poll : now;
    }

    void SwitchProvider(Provider p)
    {
        if (p == _client.Provider) return;
        Settings.Provider = p;
        _client = UsageClient.For(p);
        LoadProvider();
        Push();
        Tick();
    }

    void Tick()
    {
        var now = DateTimeOffset.Now;
        _strip.Reposition();
        if (now >= _nextPoll && !_vs.Fetching) _ = Fetch(manual: false);

        // Countdowns only change once a minute on the strip; the popup shows "x min ago" so refresh it every tick.
        if (now.Minute != _lastMinute) { _lastMinute = now.Minute; _strip.Render(); }
        if (_popup.Visible) _popup.Update(_vs);
    }

    async Task Fetch(bool manual)
    {
        var now = DateTimeOffset.Now;
        if (_vs.Fetching) return;
        if (manual && now - _lastAttempt < TimeSpan.FromSeconds(15)) return;

        _lastAttempt = now;
        _vs.Fetching = true;
        Push();

        var client = _client;
        var r = await client.FetchAsync();
        // The provider was switched while this was in flight: its answer is for the other one.
        if (client != _client) return;
        now = DateTimeOffset.Now;
        _vs.Fetching = false;
        _vs.Status = r.Status;
        _vs.Message = r.Message;
        if (r.Plan is not null) _vs.Plan = r.Plan;

        switch (r.Status)
        {
            case FetchStatus.Ok:
                _vs.Data = r.Data;
                _failures = 0;
                _nextPoll = now + Poll;
                break;
            case FetchStatus.RateLimited:
                _failures++;
                var wait = r.RetryAfter ?? TimeSpan.FromMinutes(5 * Math.Pow(2, Math.Min(_failures - 1, 2)));
                _nextPoll = now + TimeSpan.FromSeconds(Math.Clamp(wait.TotalSeconds + 5, 60, 1800));
                break;
            case FetchStatus.NoCredentials:
            case FetchStatus.Unauthorized:
                // Only re-reads the local credentials file until it changes, so checking often is cheap.
                _vs.CliInstalled = _client.FindCli() is not null;
                _nextPoll = now + TimeSpan.FromSeconds(10);
                break;
            default:
                _failures++;
                _nextPoll = now + TimeSpan.FromMinutes(Math.Min(15, Math.Pow(2, Math.Min(_failures, 4))));
                break;
        }
        _vs.NextAttempt = _nextPoll;
        Push();
    }

    void Push()
    {
        _strip.Update(_vs);
        _popup.Update(_vs);
    }

    void TogglePopup()
    {
        if (_popup.Visible) { _popup.HidePopup(); return; }
        // The click that dismissed the popup (via deactivation) shouldn't immediately reopen it.
        if ((DateTime.UtcNow - _popup.LastHidden).TotalMilliseconds < 250) return;
        _popup.ShowAt(_strip.ScreenBounds, DpiScale());
        if (_vs.Data is null || DateTimeOffset.Now - _vs.Data.FetchedAt > Poll) _ = Fetch(manual: false);
    }

    float DpiScale()
    {
        var tb = Native.FindWindow("Shell_TrayWnd", null);
        var dpi = tb != IntPtr.Zero ? Native.GetDpiForWindow(tb) : 96;
        return dpi / 96f;
    }

    ContextMenuStrip BuildMenu()
    {
        var m = new ContextMenuStrip
        {
            ShowImageMargin = false,
            ShowCheckMargin = true,
            Font = new Font("Segoe UI", 9.5f),
            Padding = new Padding(4),
            DropShadowEnabled = false,
        };
        m.Items.Add(new ToolStripMenuItem("Show details", null, (_, _) => TogglePopup()) { Font = new Font("Segoe UI Semibold", 9.5f) });
        m.Items.Add(new ToolStripMenuItem("Refresh now", null, (_, _) => _ = Fetch(manual: true)));
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add(BuildProviderMenu());
        m.Items.Add(BuildSizeMenu());
        m.Items.Add(_startupItem);
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add(new ToolStripMenuItem("Quit", null, (_, _) => Quit()));
        foreach (ToolStripItem i in m.Items) if (i is ToolStripMenuItem mi) mi.Padding = new Padding(4, 5, 12, 5);
        m.HandleCreated += (_, _) => Native.SetDwmInt(m.Handle, Native.DWMWA_WINDOW_CORNER_PREFERENCE, Native.DWMWCP_ROUND);
        return m;
    }

    ToolStripMenuItem BuildProviderMenu() => BuildChoiceMenu("Show usage for",
        Enum.GetValues<Provider>().Select(p => (p, UsageClient.Name(p))).ToArray(),
        () => _client.Provider, SwitchProvider);

    ToolStripMenuItem BuildSizeMenu() => BuildChoiceMenu("Size",
        new[]
        {
            (StripSize.Auto, "Auto (fit available space)"),
            (StripSize.Full, "Full"),
            (StripSize.Compact, "Compact"),
            (StripSize.Mini, "Mini"),
            (StripSize.Micro, "Micro"),
        },
        () => Settings.Size, size => { Settings.Size = size; _strip.Relayout(); });

    /// <summary>A submenu of options with a check next to the current one, styled like the main menu.</summary>
    ToolStripMenuItem BuildChoiceMenu<T>(string title, (T value, string label)[] options, Func<T> current, Action<T> choose)
    {
        var menu = new ToolStripMenuItem(title);
        foreach (var (value, label) in options)
        {
            var item = new ToolStripMenuItem(label) { Padding = new Padding(4, 5, 12, 5) };
            item.Click += (_, _) => choose(value);
            menu.DropDownItems.Add(item);
        }
        menu.DropDownOpening += (_, _) =>
        {
            var now = current();
            for (int i = 0; i < options.Length; i++)
                ((ToolStripMenuItem)menu.DropDownItems[i]).Checked = EqualityComparer<T>.Default.Equals(options[i].value, now);
            if (menu.DropDown is ToolStripDropDownMenu dd) { dd.ShowImageMargin = false; dd.ShowCheckMargin = true; }
            menu.DropDown.Renderer = _menu.Renderer;
            menu.DropDown.BackColor = _menu.BackColor;
            if (menu.DropDown.IsHandleCreated)
                Native.SetDwmInt(menu.DropDown.Handle, Native.DWMWA_WINDOW_CORNER_PREFERENCE, Native.DWMWCP_ROUND);
        };
        menu.DropDown.HandleCreated += (_, _) => Native.SetDwmInt(menu.DropDown.Handle, Native.DWMWA_WINDOW_CORNER_PREFERENCE, Native.DWMWCP_ROUND);
        return menu;
    }

    /// <summary>
    /// Opens a terminal to sign in (Claude Code for /login, `codex login` for ChatGPT, `gemini` for Gemini),
    /// or the CLI's install guide if it's missing.
    /// </summary>
    void OpenSetup()
    {
        var cli = _client.FindCli();
        _vs.CliInstalled = cli is not null;
        _popup.HidePopup();
        bool chatGpt = _client.Provider == Provider.ChatGpt;
        if (cli is null)
        {
            Launch(_client.Provider switch
            {
                Provider.ChatGpt => "https://developers.openai.com/codex/cli",
                Provider.Gemini => "https://github.com/google-gemini/gemini-cli",
                _ => "https://docs.claude.com/en/docs/claude-code/setup",
            }, null);
            return;
        }
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        // cmd /k strips the outer quotes of its command line, so the whole command is wrapped once more.
        var command = chatGpt ? $"\"\"{cli}\" login\"" : $"\"{cli}\"";
        if (!Launch("wt.exe", $"-d \"{home}\" cmd /k {command}", home))
            Launch("cmd.exe", $"/k {command}", home);
    }

    static bool Launch(string file, string? args, string? dir = null)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file, args ?? "")
            {
                UseShellExecute = true,
                WorkingDirectory = dir ?? "",
            });
            return true;
        }
        catch { return false; }
    }

    void ShowMenu()
    {
        var pal = Palette.ForApps();
        _menu.Renderer = new MenuRenderer(pal);
        _menu.BackColor = pal.BgTop;
        _menu.ForeColor = pal.Text;
        _startupItem.Checked = StartupEnabled();
        if (_menu.IsHandleCreated)
            Native.SetDwmInt(_menu.Handle, Native.DWMWA_BORDER_COLOR, Native.ColorRef(pal.Border));
        _popup.HidePopup();
        _menu.Show(Cursor.Position, ToolStripDropDownDirection.AboveLeft);
        Native.SetDwmInt(_menu.Handle, Native.DWMWA_BORDER_COLOR, Native.ColorRef(pal.Border));
    }

    static bool StartupEnabled()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue(RunName) is string;
    }

    static string RunCommand => $"\"{Environment.ProcessPath}\"";

    /// <summary>
    /// Starts with Windows by default: turned on the first time the app runs (and only then, so turning it off
    /// sticks). If it's on but the exe has moved since, the entry is pointed at the new location.
    /// Dev builds under bin\ are left alone so they don't hijack the installed copy's autostart.
    /// </summary>
    static void InitAutostart()
    {
        if (Environment.ProcessPath is not { } exe || exe.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey);
            if (!Settings.AutostartInitialised)
            {
                k.SetValue(RunName, RunCommand);
                Settings.AutostartInitialised = true;
            }
            else if (k.GetValue(RunName) is string current && current != RunCommand)
            {
                k.SetValue(RunName, RunCommand);
            }
        }
        catch { }
    }

    void ToggleStartup()
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey);
        if (StartupEnabled()) k.DeleteValue(RunName, false);
        else k.SetValue(RunName, RunCommand);
        Settings.AutostartInitialised = true;
        _startupItem.Checked = StartupEnabled();
    }

    void OnPrefsChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)
        {
            _strip.RefreshTheme();
            _popup.HidePopup();
        }
    }

    void OnDisplayChanged(object? sender, EventArgs e) => _strip.Reposition(force: true);

    void Quit()
    {
        _tick.Stop();
        SystemEvents.UserPreferenceChanged -= OnPrefsChanged;
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        _strip.Dispose();
        _popup.Dispose();
        _menu.Dispose();
        ExitThread();
    }
}

sealed class MenuRenderer : ToolStripProfessionalRenderer
{
    readonly Palette _p;
    public MenuRenderer(Palette p) : base(new Colors(p)) { _p = p; RoundedEdges = false; }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled) return;
        Gfx.Setup(e.Graphics, true);
        var r = new RectangleF(3, 1, e.Item.Width - 6, e.Item.Height - 2);
        Gfx.FillRound(e.Graphics, _p.Hover, r, 4);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = _p.Text;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        using var f = Gfx.Icon(12);
        var r = e.ImageRectangle;
        TextRenderer.DrawText(e.Graphics, "", f, r, _p.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        int y = e.Item.Height / 2;
        using var pen = new Pen(_p.Divider);
        e.Graphics.DrawLine(pen, 8, y, e.Item.Width - 8, y);
    }

    sealed class Colors : ProfessionalColorTable
    {
        readonly Palette _p;
        public Colors(Palette p) { _p = p; UseSystemColors = false; }
        public override Color ToolStripDropDownBackground => _p.BgTop;
        public override Color ImageMarginGradientBegin => _p.BgTop;
        public override Color ImageMarginGradientMiddle => _p.BgTop;
        public override Color ImageMarginGradientEnd => _p.BgTop;
        public override Color MenuBorder => _p.Border;
        public override Color MenuItemBorder => Color.Transparent;
        public override Color MenuItemSelected => _p.Hover;
        public override Color SeparatorDark => _p.Divider;
        public override Color SeparatorLight => _p.Divider;
    }
}
