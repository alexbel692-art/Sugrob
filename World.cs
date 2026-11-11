using System;
using System.Collections.Generic;
using System.Text;

namespace Sugrob;

// Все координаты в мире — физические пиксели виртуального экрана (процесс Per-Monitor-V2).

internal sealed class MonitorInfo
{
    public Native.RECT Bounds;
    public double Scale = 1.0;      // DPI / 96
    public bool Suppressed;         // поверх монитора полноэкранное приложение
    public int Left => Bounds.Left;
    public int Top => Bounds.Top;
    public int Right => Bounds.Right;
    public int Bottom => Bounds.Bottom;
    public int W => Bounds.Width;
    public int H => Bounds.Height;
    public bool Contains(double x, double y) => x >= Left && x < Right && y >= Top && y < Bottom;
}

internal sealed class Flake
{
    public double X, Y, PrevY, VX, VY, R, Depth, Phase, Alpha, Sc, Life;
    public int Mon;
}

/// <summary>Верхняя кромка окна или панели задач, на которой копится снег.</summary>
internal sealed class Surface
{
    public IntPtr Key;
    public Native.RECT R;
    public bool IsTaskbar;
    public double Scale = 1;
    public int Cell = 3;
    public double[] H = Array.Empty<double>();   // высота сугроба по столбцам
    public bool[] Vis = Array.Empty<bool>();     // столбец не перекрыт другим окном
    public double SpillL, SpillR;

    public int Cols => H.Length;
    public double ColX(int c) => R.Left + (c + 0.5) * Cell;
    public int ColAt(double x) => (int)Math.Floor((x - R.Left) / Cell);

    public void Alloc()
    {
        int n = Math.Max(1, (int)Math.Ceiling(R.Width / (double)Cell));
        H = new double[n];
        Vis = new bool[n];
    }

    public void Resample()
    {
        var old = H;
        int n = Math.Max(1, (int)Math.Ceiling(R.Width / (double)Cell));
        H = new double[n];
        Vis = new bool[n];
        if (old.Length == 0) return;
        for (int i = 0; i < n; i++) H[i] = old[Math.Min(old.Length - 1, (int)((long)i * old.Length / n))];
    }

    public double Total()
    {
        double t = 0;
        foreach (var h in H) t += h;
        return t;
    }
}

internal sealed class Snowman
{
    public IntPtr Key;
    public double OffX;          // от левого края поверхности
    public int Balls;            // 1..3
    public readonly double[] Grow = new double[3];
    public double Decor;         // 0..1 — появление лица, шарфа, шляпы
    public double Wobble;
    public double Scale = 1;
}

internal sealed class Plow
{
    public bool Active, Dumped;
    public IntPtr Key;
    public double X;             // передний край отвала
    public double Pushed;        // сколько снега набрано (сумма высот столбцов)
    public int NextCol;
    public double Scale = 1;
}

internal sealed class World
{
    readonly Random rnd = new();
    public AppSettings S;
    public readonly List<MonitorInfo> Monitors = new();
    public readonly List<Flake> Flakes = new();
    public readonly List<Flake> Debris = new();
    public List<Surface> Surfaces = new();          // в z-порядке, верхние первыми
    readonly Dictionary<IntPtr, Surface> byKey = new();
    public readonly List<Snowman> Snowmen = new();
    public readonly Plow Plow = new();
    public readonly HashSet<IntPtr> OwnWindows = new();
    readonly StringBuilder sb = new(256);

    public double Time;
    double plowTimer;
    public bool Paused;
    public bool SculptMode;
    public int CursorX, CursorY;
    public bool CursorOnSnow;

    public const double R0 = 10, R1 = 7.4, R2 = 5.4;   // радиусы комов снеговика, DIP

    public World(AppSettings s) { S = s; }

    double R01() => rnd.NextDouble();

    // ───────────────────────────── мониторы и снежинки ─────────────────────────────

    public void SetMonitors(List<MonitorInfo> mons)
    {
        Monitors.Clear();
        Monitors.AddRange(mons);
        Flakes.Clear();
        Debris.Clear();
        EnsureFlakeCount();
    }

    int TargetCount(MonitorInfo m)
    {
        double area = (double)m.W * m.H / (m.Scale * m.Scale) / (1920.0 * 1080.0);
        return (int)Math.Clamp(S.Intensity / 100.0 * 460 * area, 0, 1600);
    }

    public void EnsureFlakeCount()
    {
        var counts = new int[Monitors.Count];
        for (int i = Flakes.Count - 1; i >= 0; i--)
        {
            if (Flakes[i].Mon >= Monitors.Count) { Flakes.RemoveAt(i); continue; }
            counts[Flakes[i].Mon]++;
        }
        for (int m = 0; m < Monitors.Count; m++)
        {
            int target = TargetCount(Monitors[m]);
            for (int k = counts[m]; k < target; k++)
            {
                var f = new Flake();
                Spawn(f, m, true);
                Flakes.Add(f);
            }
            int remove = counts[m] - target;
            for (int i = Flakes.Count - 1; i >= 0 && remove > 0; i--)
                if (Flakes[i].Mon == m) { Flakes.RemoveAt(i); remove--; }
        }
    }

    void Spawn(Flake f, int m, bool anywhere)
    {
        var mon = Monitors[m];
        double sc = mon.Scale;
        f.Mon = m;
        f.Sc = sc;
        f.Depth = R01();
        f.R = (0.9 + R01() * 1.6) * (0.55 + f.Depth * 0.9) * S.FlakeSize * sc;
        f.X = mon.Left + R01() * mon.W;
        f.Y = anywhere ? mon.Top + R01() * mon.H : mon.Top - f.R - R01() * 30 * sc;
        f.PrevY = f.Y;
        f.VY = (28 + 62 * f.Depth) * sc * (0.8 + 0.4 * R01());
        f.Phase = R01() * Math.PI * 2;
        f.Alpha = 0.35 + 0.6 * f.Depth;
    }

    void AddDebris(double x, double y, double vx, double vy, double r, double sc)
    {
        if (Debris.Count > 1500) return;
        Debris.Add(new Flake { X = x, Y = y, PrevY = y, VX = vx, VY = vy, R = r, Sc = sc, Alpha = 0.95, Depth = 1, Life = 8 });
    }

    double VirtualBottom()
    {
        int b = 0;
        foreach (var m in Monitors) b = Math.Max(b, m.Bottom);
        return b;
    }

    // ───────────────────────────── шаг симуляции ─────────────────────────────

    public void Update(double dt)
    {
        Time += dt;
        double wind = S.Wind / 100.0 * 90.0;
        bool snowing = S.Intensity > 0;

        for (int i = 0; i < Flakes.Count; i++)
        {
            var f = Flakes[i];
            if (f.Mon >= Monitors.Count) continue;
            var mon = Monitors[f.Mon];
            double sc = mon.Scale;
            f.PrevY = f.Y;
            f.Y += f.VY * dt;
            f.X += (wind * sc * (0.4 + f.Depth) + Math.Sin(Time * (0.7 + f.Depth * 0.8) + f.Phase) * 14 * sc * (0.4 + f.Depth)) * dt;
            if (f.X < mon.Left - 10) f.X += mon.W + 20;
            else if (f.X > mon.Right + 10) f.X -= mon.W + 20;

            if (f.Depth >= 0.45 && TryLand(f.X, f.PrevY, f.Y, f.R, 1.0)) Spawn(f, f.Mon, false);
            else if (f.Y - f.R > mon.Bottom) Spawn(f, f.Mon, false);
        }

        double vb = VirtualBottom();
        for (int i = Debris.Count - 1; i >= 0; i--)
        {
            var d = Debris[i];
            d.VY = Math.Min(d.VY + 650 * d.Sc * dt, 520 * d.Sc);
            d.VX *= 1 - Math.Min(1, 1.2 * dt);
            d.PrevY = d.Y;
            d.X += d.VX * dt;
            d.Y += d.VY * dt;
            d.Life -= dt;
            if (d.VY > 0 && TryLand(d.X, d.PrevY, d.Y, d.R, 0.7)) Debris.RemoveAt(i);
            else if (d.Life <= 0 || d.Y > vb + 50) Debris.RemoveAt(i);
        }

        foreach (var s in Surfaces) Relax(s, dt, snowing);
        UpdatePlow(dt);
        UpdateSnowmen(dt);
    }

    public bool IsEnabled(Surface s) => s.IsTaskbar ? S.PileOnTaskbar : S.PileOnWindows;
    double MaxPx(Surface s) => S.MaxPile * s.Scale;

    bool TryLand(double x, double y0, double y1, double r, double mul)
    {
        foreach (var s in Surfaces)
        {
            if (!IsEnabled(s) || x < s.R.Left || x >= s.R.Right) continue;
            int c = s.ColAt(x);
            if (c < 0 || c >= s.Cols || !s.Vis[c]) continue;
            double line = s.R.Top - s.H[c];
            if (y0 < line && y1 >= line)
            {
                Deposit(s, c, r, mul);
                return true;
            }
        }
        return false;
    }

    void Deposit(Surface s, int c, double r, double mul)
    {
        double maxH = MaxPx(s);
        double amt = 3.2 * s.Scale * S.Accumulation * mul * Math.Clamp(r / (1.6 * s.Scale), 0.5, 2.2);
        AddH(s, c, amt * 0.5, maxH);
        AddH(s, c - 1, amt * 0.25, maxH);
        AddH(s, c + 1, amt * 0.25, maxH);
    }

    static void AddH(Surface s, int i, double a, double maxH)
    {
        if (i < 0 || i >= s.Cols) return;
        s.H[i] = Math.Min(maxH, s.H[i] + a);
    }

    /// <summary>Осыпание: снег не держит крутой склон и падает с краёв окна.</summary>
    void Relax(Surface s, double dt, bool snowing)
    {
        int n = s.Cols;
        if (n < 2) return;
        if (Plow.Active && Plow.Key == s.Key) return;   // не размазываем снег за отвалом
        var h = s.H;
        double slope = s.Cell * 0.85;

        for (int i = 0; i < n - 1; i++) Settle(h, i, i + 1, slope);
        for (int i = n - 2; i >= 0; i--) Settle(h, i, i + 1, slope);
        double maxH = MaxPx(s);
        for (int i = 0; i < n; i++) if (h[i] > maxH) h[i] = maxH;   // если в настройках уменьшили высоту

        double edge = slope * 1.3;
        if (h[0] > edge) { s.SpillL += h[0] - edge; h[0] = edge; }
        if (h[n - 1] > edge) { s.SpillR += h[n - 1] - edge; h[n - 1] = edge; }
        double sc = s.Scale;
        if (s.SpillL > 5 * sc)
        {
            if (s.Vis[0]) AddDebris(s.R.Left - 1.5 * sc, s.R.Top - edge, -(10 + R01() * 25) * sc, 0, (1.1 + R01()) * sc, sc);
            s.SpillL = 0;
        }
        if (s.SpillR > 5 * sc)
        {
            if (s.Vis[n - 1]) AddDebris(s.R.Right + 1.5 * sc, s.R.Top - edge, (10 + R01() * 25) * sc, 0, (1.1 + R01()) * sc, sc);
            s.SpillR = 0;
        }

        if (!snowing)
        {
            double melt = 0.25 * sc * dt;
            for (int i = 0; i < n; i++) h[i] = Math.Max(0, h[i] - melt);
        }
    }

    static void Settle(double[] h, int a, int b, double slope)
    {
        double d = h[a] - h[b];
        if (d > slope) { double t = (d - slope) * 0.5; h[a] -= t; h[b] += t; }
        else if (d < -slope) { double t = (-d - slope) * 0.5; h[a] += t; h[b] -= t; }
    }

    // ───────────────────────────── окна ─────────────────────────────

    struct WinItem
    {
        public IntPtr H;
        public Native.RECT R;
        public bool Taskbar, Title, Tool;
    }

    MonitorInfo MonitorAt(double x, double y)
    {
        foreach (var m in Monitors) if (m.Contains(x, y)) return m;
        return null;
    }

    bool CoversMonitor(Native.RECT r)
    {
        foreach (var m in Monitors)
            if (r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom) return true;
        return false;
    }

    public static bool IsDesktopClass(string cls) => cls == "Progman" || cls == "WorkerW";

    /// <summary>Перечитать окна: где верхние кромки, что чем перекрыто, кто сдвинулся.</summary>
    public void Scan()
    {
        if (Monitors.Count == 0) return;
        var items = new List<WinItem>(64);
        Native.EnumWindows((h, _) =>
        {
            if (OwnWindows.Contains(h)) return true;
            if (!Native.IsWindowVisible(h) || Native.IsIconic(h) || Native.IsCloaked(h)) return true;
            string cls = Native.ClassOf(h, sb);
            if (IsDesktopClass(cls)) return true;
            long ex = Native.GetExStyle(h);
            if ((ex & Native.WS_EX_TRANSPARENT) != 0) return true;   // прозрачные для мыши оверлеи
            if (!Native.GetFrameRect(h, out var r) || r.Width <= 0 || r.Height <= 0) return true;
            bool taskbar = cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd";
            bool title = Native.GetWindowTextLength(h) > 0;
            if (!taskbar && !title && CoversMonitor(r)) return true;     // невидимые служебные полноэкранные окна
            items.Add(new WinItem { H = h, R = r, Taskbar = taskbar, Title = title, Tool = (ex & Native.WS_EX_TOOLWINDOW) != 0 });
            return true;
        }, IntPtr.Zero);

        var present = new HashSet<IntPtr>();
        foreach (var it in items) present.Add(it.H);

        var next = new List<Surface>();
        var segs = new List<(int a, int b)>();
        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            var r = it.R;
            var mon = MonitorAt((r.Left + r.Right) / 2.0, r.Top + 1);
            if (mon == null) continue;

            bool holds = it.Taskbar
                ? r.Width > r.Height && r.Top > mon.Top + mon.H / 2 && r.Top < mon.Bottom - 4
                : it.Title && !it.Tool && r.Width >= 120 && r.Height >= 60 && r.Top > mon.Top + 6;
            if (!holds) continue;

            // Видимые участки кромки: вычитаем окна, лежащие выше по z-порядку.
            segs.Clear();
            segs.Add((r.Left, r.Right));
            int y = r.Top - 2;
            for (int j = 0; j < i && segs.Count > 0; j++)
            {
                var o = items[j].R;
                if (o.Top <= y && o.Bottom > y) Subtract(segs, o.Left, o.Right);
            }

            if (!byKey.TryGetValue(it.H, out var s))
            {
                s = new Surface { Key = it.H, IsTaskbar = it.Taskbar, R = r };
                SetScale(s, mon);
                s.Alloc();
                byKey[it.H] = s;
            }
            else if (s.R.Left != r.Left || s.R.Top != r.Top)
            {
                Shake(s);                       // окно сдвинули — снег осыпается
                s.R = r;
                SetScale(s, mon);
                s.Alloc();
            }
            else if (s.R.Right != r.Right)
            {
                s.R = r;
                s.Resample();
            }
            else s.R = r;

            for (int c = 0; c < s.Cols; c++)
            {
                double x = s.ColX(c);
                bool v = false;
                foreach (var sg in segs) if (x >= sg.a && x < sg.b) { v = true; break; }
                s.Vis[c] = v;
            }
            next.Add(s);
        }

        var keep = new HashSet<IntPtr>();
        foreach (var s in next) keep.Add(s.Key);
        var gone = new List<IntPtr>();
        foreach (var kv in byKey)
        {
            if (keep.Contains(kv.Key)) continue;
            // окно развернули на весь экран — снег падает; закрыли/свернули — просто исчезает
            if (present.Contains(kv.Key)) Shake(kv.Value);
            else RemoveSnowmenOn(kv.Key);
            gone.Add(kv.Key);
        }
        foreach (var k in gone) byKey.Remove(k);
        Surfaces = next;
        if (Plow.Active && !byKey.ContainsKey(Plow.Key)) Plow.Active = false;
    }

    static void SetScale(Surface s, MonitorInfo mon)
    {
        s.Scale = mon.Scale;
        s.Cell = Math.Max(2, (int)Math.Round(3 * mon.Scale));
    }

    static void Subtract(List<(int a, int b)> segs, int a, int b)
    {
        for (int i = segs.Count - 1; i >= 0; i--)
        {
            var (s, e) = segs[i];
            if (b <= s || a >= e) continue;
            segs.RemoveAt(i);
            if (a > s) segs.Add((s, a));
            if (b < e) segs.Add((b, e));
        }
    }

    /// <summary>Стряхнуть снег с поверхности (окно сдвинули).</summary>
    void Shake(Surface s)
    {
        double sc = s.Scale;
        double total = 0;
        for (int c = 0; c < s.Cols; c++) if (s.Vis[c] && s.H[c] > 1.2 * sc) total += s.H[c] / (4 * sc);
        double p = total > 0 ? Math.Min(1, 380 / total) : 0;
        for (int c = 0; c < s.Cols; c++)
        {
            if (!s.Vis[c] || s.H[c] < 1.2 * sc) continue;
            int k = (int)Math.Ceiling(s.H[c] / (4 * sc));
            for (int t = 0; t < k; t++)
            {
                if (R01() > p) continue;
                AddDebris(s.ColX(c) + (R01() - 0.5) * s.Cell, s.R.Top - R01() * s.H[c],
                          (R01() - 0.5) * 60 * sc, -R01() * 90 * sc, (1 + R01() * 1.5) * sc, sc);
            }
        }
        Array.Clear(s.H);
        for (int i = Snowmen.Count - 1; i >= 0; i--)
            if (Snowmen[i].Key == s.Key) Crumble(Snowmen[i], s);
    }

    public void ShakeAll()
    {
        foreach (var s in Surfaces) Shake(s);
        Plow.Active = false;
    }

    void RemoveSnowmenOn(IntPtr key) => Snowmen.RemoveAll(m => m.Key == key);

    // ───────────────────────────── снегоуборщик ─────────────────────────────

    public bool StartPlow()
    {
        if (Plow.Active) return true;
        Surface tb = null;
        foreach (var s in Surfaces)
            if (s.IsTaskbar && (tb == null || s.Total() > tb.Total())) tb = s;
        if (tb == null) return false;
        Plow.Active = true;
        Plow.Dumped = false;
        Plow.Key = tb.Key;
        Plow.Scale = tb.Scale;
        Plow.X = tb.R.Right + 2 * tb.Scale;
        Plow.NextCol = tb.Cols - 1;
        Plow.Pushed = 0;
        return true;
    }

    public Surface PlowSurface => Plow.Active && byKey.TryGetValue(Plow.Key, out var s) ? s : null;

    public double WedgeHeight(Surface s)
    {
        double area = Plow.Pushed * s.Cell;
        return Math.Min(Math.Sqrt(area / 1.3), 26 * s.Scale);
    }

    void UpdatePlow(double dt)
    {
        plowTimer += dt;
        if (!Plow.Active)
        {
            if (S.Plow && plowTimer >= S.PlowIntervalMin * 60.0)
            {
                plowTimer = 0;
                foreach (var s in Surfaces)
                    if (s.IsTaskbar && s.Total() / Math.Max(1, s.Cols) >= 0.3 * MaxPx(s)) { StartPlow(); break; }
            }
            return;
        }
        var tb = PlowSurface;
        if (tb == null) { Plow.Active = false; return; }
        double sc = Plow.Scale;
        Plow.X -= 95 * sc * dt;
        while (Plow.NextCol >= 0 && Plow.NextCol < tb.Cols && tb.ColX(Plow.NextCol) >= Plow.X)
        {
            Plow.Pushed += tb.H[Plow.NextCol];
            tb.H[Plow.NextCol] = 0;
            Plow.NextCol--;
        }
        if (!Plow.Dumped && Plow.X <= tb.R.Left)
        {
            // сбрасываем набранный снег за край
            double wh = WedgeHeight(tb);
            int n = (int)Math.Min(160, Plow.Pushed / (1.5 * sc));
            for (int i = 0; i < n; i++)
                AddDebris(tb.R.Left - R01() * wh * 2.6, tb.R.Top - R01() * wh,
                          -(20 + R01() * 70) * sc, -R01() * 50 * sc, (1.1 + R01() * 1.6) * sc, sc);
            Plow.Pushed = 0;
            Plow.Dumped = true;
        }
        if (Plow.X + 44 * sc < tb.R.Left) { Plow.Active = false; plowTimer = 0; }
    }

    // ───────────────────────────── снеговики ─────────────────────────────

    void UpdateSnowmen(double dt)
    {
        for (int i = Snowmen.Count - 1; i >= 0; i--)
        {
            var m = Snowmen[i];
            if (!byKey.ContainsKey(m.Key)) { Snowmen.RemoveAt(i); continue; }
            for (int b = 0; b < m.Balls; b++) m.Grow[b] = Math.Min(1, m.Grow[b] + dt * 3.5);
            if (m.Balls == 3 && m.Grow[2] >= 1) m.Decor = Math.Min(1, m.Decor + dt * 2);
            m.Wobble = Math.Max(0, m.Wobble - dt * 1.5);
        }
    }

    public bool TryGetSurface(IntPtr key, out Surface s) => byKey.TryGetValue(key, out s);

    public static double SnowmanHeight(Snowman m)
    {
        double u = m.Scale;
        double h = 2 * R0 * u;
        if (m.Balls > 1) h += 1.6 * R1 * u;
        if (m.Balls > 2) h += 1.7 * R2 * u + 7 * u;   // голова + шляпа
        return h;
    }

    public bool SnowmanVisible(Snowman m, Surface s)
    {
        int c = s.ColAt(s.R.Left + m.OffX);
        return c >= 0 && c < s.Cols && s.Vis[c];
    }

    Snowman HitSnowman(double x, double y)
    {
        for (int i = Snowmen.Count - 1; i >= 0; i--)
        {
            var m = Snowmen[i];
            if (!byKey.TryGetValue(m.Key, out var s) || !SnowmanVisible(m, s)) continue;
            double cx = s.R.Left + m.OffX, baseY = s.R.Top + m.Scale;
            if (Math.Abs(x - cx) <= (R0 + 3) * m.Scale && y <= baseY + 3 * m.Scale && y >= baseY - SnowmanHeight(m))
                return m;
        }
        return null;
    }

    Surface HitPile(double x, double y, out int col)
    {
        col = -1;
        foreach (var s in Surfaces)
        {
            if (!IsEnabled(s) || x < s.R.Left || x >= s.R.Right) continue;
            int c = s.ColAt(x);
            if (c < 0 || c >= s.Cols || !s.Vis[c]) continue;
            double sc = s.Scale;
            if (s.H[c] > 0.8 * sc && y >= s.R.Top - s.H[c] - 6 * sc && y <= s.R.Top + 5 * sc)
            {
                col = c;
                return s;
            }
        }
        return null;
    }

    public bool HitAnything(double x, double y) => HitSnowman(x, y) != null || HitPile(x, y, out _) != null;

    /// <summary>Клик в режиме лепки. true — клик наш, дальше в окна не пропускаем.</summary>
    public bool Sculpt(double x, double y, bool right)
    {
        var m = HitSnowman(x, y);
        if (m != null)
        {
            byKey.TryGetValue(m.Key, out var ms);
            if (right) Crumble(m, ms);
            else if (m.Balls < 3) AddBall(m, ms);
            else m.Wobble = 1;
            return true;
        }
        if (right) return false;

        var s = HitPile(x, y, out int col);
        if (s == null) return false;
        double sc = s.Scale;

        // рядом недолепленный снеговик — докатываем ком к нему
        foreach (var other in Snowmen)
            if (other.Key == s.Key && other.Balls < 3 && Math.Abs(s.R.Left + other.OffX - x) < R0 * sc * 2.4)
            {
                AddBall(other, s);
                return true;
            }

        if (Snowmen.Count >= 40) return true;
        double r0 = R0 * sc;
        int c0 = s.ColAt(x - r0), c1 = s.ColAt(x + r0);
        double sum = 0; int cnt = 0;
        for (int c = Math.Max(0, c0); c <= Math.Min(s.Cols - 1, c1); c++) { sum += s.H[c]; cnt++; }
        if (cnt == 0 || sum / cnt < 2.0 * sc)
        {
            // снега мало — только пух
            for (int i = 0; i < 8; i++) AddDebris(x, s.R.Top - s.H[col], (R01() - 0.5) * 80 * sc, -(40 + R01() * 60) * sc, (0.8 + R01()) * sc, sc);
            return true;
        }
        TakeSnow(s, x, r0 * 1.5, 0.3);
        if (s.R.Width < 2 * r0) return true;
        Snowmen.Add(new Snowman { Key = s.Key, OffX = Math.Clamp(x - s.R.Left, r0, s.R.Width - r0), Balls = 1, Scale = sc });
        return true;
    }

    void AddBall(Snowman m, Surface s)
    {
        if (m.Balls >= 3) return;
        m.Grow[m.Balls] = 0;
        m.Balls++;
        if (s != null) TakeSnow(s, s.R.Left + m.OffX, R0 * s.Scale * 2.5, 0.6);
    }

    static void TakeSnow(Surface s, double x, double radius, double keep)
    {
        int c0 = Math.Max(0, s.ColAt(x - radius)), c1 = Math.Min(s.Cols - 1, s.ColAt(x + radius));
        for (int c = c0; c <= c1; c++) s.H[c] *= keep;
    }

    void Crumble(Snowman m, Surface s)
    {
        Snowmen.Remove(m);
        if (s == null) return;
        double u = m.Scale, cx = s.R.Left + m.OffX, baseY = s.R.Top;
        double h = SnowmanHeight(m);
        for (int i = 0; i < 70; i++)
            AddDebris(cx + (R01() - 0.5) * 2 * R0 * u, baseY - R01() * h,
                      (R01() - 0.5) * 120 * u, -R01() * 120 * u, (1.1 + R01() * 1.8) * u, u);
    }
}
