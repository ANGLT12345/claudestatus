using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ClaudeUsageBar;

/// <summary>
/// A layered child window parented into the Windows taskbar (Shell_TrayWnd), sitting just left of the tray.
/// Per-pixel alpha means it blends with Mica/acrylic taskbars.
/// </summary>
sealed class StripWindow : NativeWindow, IDisposable
{
    public event Action? Clicked;
    public event Action? RightClicked;

    ViewState _state;
    IntPtr _taskbar;
    bool _hover, _pressed, _tracking;
    float _scale = 1;
    StripSize _variant = StripSize.Full;
    Rectangle _screenBounds;
    Size _size;

    public Rectangle ScreenBounds => _screenBounds;
    public bool TaskbarIsLight { get; private set; } = Palette.TaskbarIsLight();

    public StripWindow(ViewState state)
    {
        _state = state;
        Attach();
    }

    public bool IsAttached => _taskbar != IntPtr.Zero && Native.IsWindow(_taskbar) && Handle != IntPtr.Zero;

    void Attach()
    {
        _taskbar = Native.FindWindow("Shell_TrayWnd", null);
        if (_taskbar == IntPtr.Zero) return;

        if (Handle != IntPtr.Zero) DestroyHandle();
        var cp = new CreateParams
        {
            Caption = "ClaudeUsageBar",
            Style = Native.WS_CHILD | Native.WS_VISIBLE | Native.WS_CLIPSIBLINGS,
            ExStyle = Native.WS_EX_LAYERED | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW,
            Parent = _taskbar,
            Width = 1, Height = 1,
        };
        try { CreateHandle(cp); } catch { _taskbar = IntPtr.Zero; return; }
        Reposition(force: true);
    }

    /// <summary>Called on a timer: follows taskbar resizes, DPI changes and Explorer restarts.</summary>
    public void Reposition(bool force = false)
    {
        if (!IsAttached) { Attach(); if (!IsAttached) return; force = true; }

        Native.GetWindowRect(_taskbar, out var tb);
        var tray = Native.FindWindowEx(_taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
        Native.RECT tr = default;
        bool haveTray = tray != IntPtr.Zero && Native.GetWindowRect(tray, out tr) && tr.Width > 0;

        float scale = Native.GetDpiForWindow(_taskbar) / 96f;
        if (scale <= 0) scale = 1;
        bool horizontal = tb.Width >= tb.Height;

        int w, h, x, y;
        StripSize variant;
        if (horizontal)
        {
            h = tb.Height;
            int trayLeft = haveTray ? tr.Left : tb.Right - (int)(300 * scale);
            ScanLayoutSoon(trayLeft);
            (x, variant) = Place(tb, trayLeft, scale);
            w = StripPainter.Width(variant, scale);
            x -= tb.Left;
            y = 0;
        }
        else
        {
            // Vertical taskbars are narrow: use the smallest size that fits the width, above the tray.
            variant = PreferredSizes().FirstOrDefault(v => StripPainter.Width(v, scale) <= tb.Width, StripSize.Micro);
            w = StripPainter.Width(variant, scale);
            h = (int)(48 * scale);
            x = Math.Max(0, (tb.Width - w) / 2);
            int bottom = haveTray ? tr.Top - tb.Top : tb.Height - (int)(300 * scale);
            y = bottom - h - (int)(4 * scale);
        }

        var screen = new Rectangle(tb.Left + x, tb.Top + y, w, h);
        bool changed = force || screen != _screenBounds || Math.Abs(scale - _scale) > 0.001f || variant != _variant;
        _screenBounds = screen;
        _scale = scale;
        _variant = variant;
        _size = new Size(w, h);

        // Keep above the XAML taskbar content, which is a sibling.
        Native.SetWindowPos(Handle, Native.HWND_TOP, x, y, w, h, Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        if (changed) Render();
    }

    TaskbarLayout? _layout;
    DateTime _lastScan;
    bool _scanning;

    /// <summary>Refreshes the real button layout in the background every few seconds (UIA is slow-ish and cross-process).</summary>
    void ScanLayoutSoon(int trayLeft)
    {
        if (_scanning || (DateTime.UtcNow - _lastScan).TotalSeconds < 3) return;
        _scanning = true;
        var taskbar = _taskbar;
        var ctx = SynchronizationContext.Current;
        Task.Run(() => TaskbarLayout.Scan(taskbar, trayLeft)).ContinueWith(t =>
        {
            void Apply()
            {
                _scanning = false;
                _lastScan = DateTime.UtcNow;
                var fresh = t.Result;
                if (fresh != _layout) { _layout = fresh; Reposition(force: true); }
            }
            if (ctx is null) Apply(); else ctx.Post(_ => Apply(), null);
        });
    }

    /// <summary>The user's fixed size, or every size largest-first when on Auto.</summary>
    static IEnumerable<StripSize> PreferredSizes() =>
        Settings.Size == StripSize.Auto ? StripPainter.BySize : new[] { Settings.Size };

    /// <summary>
    /// Picks where the strip goes and how big it is: the largest size that fits in a free gap. On a centred
    /// taskbar the gap left of the apps comes first because it doesn't move as apps open and close.
    /// If even the smallest size doesn't fit anywhere, it goes in the biggest gap.
    /// </summary>
    (int x, StripSize size) Place(Native.RECT tb, int trayLeft, float scale)
    {
        int margin = (int)(6 * scale);
        if (_layout is not { } l)
        {
            // Layout unknown (UIA unavailable): sit left of the tray.
            var v = PreferredSizes().First();
            return (trayLeft - StripPainter.Width(v, scale) - margin, v);
        }

        var gaps = l.Gaps(tb.Left, trayLeft).Where(g => g.Width > 0).ToList();
        foreach (var size in PreferredSizes())
        {
            int w = StripPainter.Width(size, scale);
            foreach (var gap in gaps)
                if (gap.Width >= w + margin * 2)
                    return (gap.AnchorRight ? gap.End - w - margin : gap.Start + margin, size);
        }

        var smallest = PreferredSizes().Last();
        int sw = StripPainter.Width(smallest, scale);
        if (gaps.Count == 0) return (trayLeft - sw, smallest);
        var best = gaps.MaxBy(g => g.Width);
        return (best.Start + Math.Max(0, (best.Width - sw) / 2), smallest);
    }

    /// <summary>Re-run placement now, e.g. after the size setting changed.</summary>
    public void Relayout() => Reposition(force: true);

    public void Update(ViewState state) { _state = state; Render(); }

    public void RefreshTheme()
    {
        TaskbarIsLight = Palette.TaskbarIsLight();
        Render();
    }

    public void Render()
    {
        if (!IsAttached || _size.Width <= 0 || _size.Height <= 0) return;

        using var bmp = new Bitmap(_size.Width, _size.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
            StripPainter.Paint(g, _size, _scale, _variant, TaskbarIsLight, _hover, _pressed, _state, DateTimeOffset.Now);

        IntPtr screenDc = Native.GetDC(IntPtr.Zero);
        IntPtr memDc = Native.CreateCompatibleDC(screenDc);
        IntPtr hBmp = bmp.GetHbitmap(Color.FromArgb(0));
        IntPtr old = Native.SelectObject(memDc, hBmp);
        try
        {
            var size = new Native.SIZE { Cx = _size.Width, Cy = _size.Height };
            var src = new Native.POINT();
            var blend = new Native.BLENDFUNCTION { BlendOp = Native.AC_SRC_OVER, SourceConstantAlpha = 255, AlphaFormat = Native.AC_SRC_ALPHA };
            Native.UpdateLayeredWindow(Handle, screenDc, IntPtr.Zero, ref size, memDc, ref src, 0, ref blend, Native.ULW_ALPHA);
        }
        finally
        {
            Native.SelectObject(memDc, old);
            Native.DeleteObject(hBmp);
            Native.DeleteDC(memDc);
            Native.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case Native.WM_MOUSEACTIVATE:
                m.Result = (IntPtr)Native.MA_NOACTIVATE;
                return;
            case Native.WM_MOUSEMOVE:
                if (!_tracking)
                {
                    var tme = new Native.TRACKMOUSEEVENT { cbSize = Marshal.SizeOf<Native.TRACKMOUSEEVENT>(), dwFlags = Native.TME_LEAVE, hwndTrack = Handle };
                    _tracking = Native.TrackMouseEvent(ref tme);
                }
                if (!_hover) { _hover = true; Render(); }
                break;
            case Native.WM_MOUSELEAVE:
                _tracking = false;
                if (_hover || _pressed) { _hover = false; _pressed = false; Render(); }
                break;
            case 0x0201: // WM_LBUTTONDOWN
                _pressed = true; Render();
                break;
            case Native.WM_LBUTTONUP:
                if (_pressed) { _pressed = false; Render(); Clicked?.Invoke(); }
                break;
            case Native.WM_RBUTTONUP:
                RightClicked?.Invoke();
                break;
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (Handle != IntPtr.Zero) DestroyHandle();
    }
}
