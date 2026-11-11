using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Sugrob;

internal sealed class Controller
{
    readonly Application app;
    public AppSettings S { get; private set; }
    World world;
    readonly List<OverlayWindow> overlays = new();
    Tray tray;
    Hotkeys hotkeys;
    MouseHook hook;
    SettingsWindow settingsWin;
    readonly Stopwatch sw = new();
    readonly StringBuilder sb = new(256);
    double last, scanAcc, fsAcc, topAcc, sculptIdle;
    bool rendering, paused, rebuildPending;

    const int HK_SCULPT = 1, HK_ESC = 2;
    const uint VK_S = 0x53, VK_ESCAPE = 0x1B;

    public Controller(Application app) { this.app = app; }

    public void Start()
    {
        S = AppSettings.Load();
        world = new World(S);
        BuildOverlays();
        world.Scan();

        tray = new Tray(this);
        hotkeys = new Hotkeys();
        bool hk = hotkeys.Register(HK_SCULPT, Native.MOD_CONTROL | Native.MOD_ALT, VK_S, ToggleSculpt);

        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        sw.Start();
        SetRendering(true);

        if (S.FirstRun)
        {
            S.FirstRun = false;
            S.Save();
            tray.Balloon("Снег пошёл! Ctrl+Alt+S — слепить снеговика. Настройки — в значке у часов.");
        }
        else if (!hk) tray.Balloon("Ctrl+Alt+S занято другой программой — режим лепки включается из меню в трее.");
    }

    // ───────────── оверлеи по мониторам ─────────────

    void OnDisplayChanged(object sender, EventArgs e)
    {
        if (rebuildPending) return;
        rebuildPending = true;
        app.Dispatcher.BeginInvoke(new Action(() =>
        {
            rebuildPending = false;
            BuildOverlays();
            world.Scan();
        }));
    }

    void BuildOverlays()
    {
        foreach (var o in overlays) o.Close();
        overlays.Clear();
        world.OwnWindows.Clear();

        var mons = new List<MonitorInfo>();
        foreach (var scr in System.Windows.Forms.Screen.AllScreens)
        {
            var b = scr.Bounds;
            mons.Add(new MonitorInfo
            {
                Bounds = new Native.RECT { Left = b.Left, Top = b.Top, Right = b.Right, Bottom = b.Bottom },
                Scale = Native.MonitorScale(b.Left + b.Width / 2, b.Top + b.Height / 2)
            });
        }
        world.SetMonitors(mons);

        for (int i = 0; i < mons.Count; i++)
        {
            var o = new OverlayWindow(world, mons[i], i);
            o.Show();
            overlays.Add(o);
            world.OwnWindows.Add(o.Handle);
        }
    }

    // ───────────── кадр ─────────────

    void SetRendering(bool on)
    {
        if (on == rendering) return;
        rendering = on;
        if (on)
        {
            last = sw.Elapsed.TotalSeconds;
            CompositionTarget.Rendering += OnFrame;
        }
        else CompositionTarget.Rendering -= OnFrame;
    }

    void OnFrame(object sender, EventArgs e)
    {
        try
        {
            double now = sw.Elapsed.TotalSeconds;
            double dt = now - last;
            if (dt < 1.0 / Math.Clamp(S.Fps, 15, 120) - 0.003) return;
            last = now;
            dt = Math.Min(dt, 0.1);

            scanAcc += dt;
            if (scanAcc >= 0.15) { scanAcc = 0; world.Scan(); }
            fsAcc += dt;
            if (fsAcc >= 0.5) { fsAcc = 0; UpdateFullscreen(); }
            topAcc += dt;
            if (topAcc >= 1.0) { topAcc = 0; foreach (var o in overlays) o.BringToTop(); }

            if (world.SculptMode)
            {
                if (Native.GetCursorPos(out var p))
                {
                    world.CursorX = p.X;
                    world.CursorY = p.Y;
                    world.CursorOnSnow = world.HitAnything(p.X, p.Y);
                }
                sculptIdle += dt;
                if (sculptIdle > 120) SetSculpt(false);
            }

            world.Update(dt);
            foreach (var o in overlays) o.Redraw();
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }
    }

    void UpdateFullscreen()
    {
        bool hideAll = false;
        if (S.HideInFullscreen && Native.SHQueryUserNotificationState(out int st) == 0)
            hideAll = st == 3 /*D3D fullscreen*/ || st == 4 /*презентация*/;

        IntPtr fg = Native.GetForegroundWindow();
        Native.RECT fr = default;
        bool fgOk = S.HideInFullscreen && fg != IntPtr.Zero && !world.OwnWindows.Contains(fg)
                    && !World.IsDesktopClass(Native.ClassOf(fg, sb)) && Native.GetFrameRect(fg, out fr);

        foreach (var m in world.Monitors)
            m.Suppressed = S.HideInFullscreen && (hideAll ||
                (fgOk && fr.Left <= m.Left && fr.Top <= m.Top && fr.Right >= m.Right && fr.Bottom >= m.Bottom));
    }

    // ───────────── команды из трея ─────────────

    public void ToggleSculpt() => SetSculpt(!world.SculptMode);

    void SetSculpt(bool on)
    {
        if (on == world.SculptMode) return;
        world.SculptMode = on;
        tray.SetSculpt(on);
        if (on)
        {
            if (paused) TogglePause();
            sculptIdle = 0;
            hook = new MouseHook(OnHookDown);
            hotkeys.Register(HK_ESC, 0, VK_ESCAPE, () => SetSculpt(false));
        }
        else
        {
            hook?.Dispose();
            hook = null;
            hotkeys.Unregister(HK_ESC);
            world.CursorOnSnow = false;
        }
    }

    bool OnHookDown(int x, int y, bool right)
    {
        if (!world.SculptMode || paused) return false;
        bool mine = world.Sculpt(x, y, right);
        if (mine) sculptIdle = 0;
        return mine;
    }

    public void CallPlow()
    {
        if (paused) TogglePause();
        if (!world.StartPlow()) tray.Balloon("Не нашёл панель задач внизу экрана (или сугроб на ней выключен).");
    }

    public void ShakeAll() => world.ShakeAll();

    public void TogglePause()
    {
        paused = !paused;
        world.Paused = paused;
        tray.SetPaused(paused);
        if (paused && world.SculptMode) SetSculpt(false);
        SetRendering(!paused);
        foreach (var o in overlays) o.Redraw();
    }

    public void ShowSettings()
    {
        if (settingsWin == null)
        {
            settingsWin = new SettingsWindow(this);
            settingsWin.Closed += (_, _) => settingsWin = null;
            settingsWin.Show();
        }
        settingsWin.Activate();
    }

    public void ApplySettings()
    {
        S.Save();
        world.EnsureFlakeCount();
    }

    public void Exit()
    {
        try
        {
            SetSculpt(false);
            SetRendering(false);
            SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            hotkeys?.Dispose();
            tray?.Dispose();
            settingsWin?.Close();
            foreach (var o in overlays) o.Close();
        }
        catch (Exception ex) { Log.Error(ex); }
        app.Shutdown();
    }
}
