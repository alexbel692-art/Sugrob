using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Sugrob;

/// <summary>Рисует мир в координатах физических пикселей (масштаб в DIP задаёт вызывающий).</summary>
internal static class Renderer
{
    static readonly SolidColorBrush[] Solid = new SolidColorBrush[11];
    static readonly RadialGradientBrush[] Soft = new RadialGradientBrush[11];
    static readonly LinearGradientBrush PileFill;
    static readonly RadialGradientBrush BallFill;
    static readonly Brush ShadowBrush, EdgeBrush, BallEdge;
    static readonly Brush Track, Wheel, Yellow, YellowDark, Glass, BeaconOn, BeaconOff, Blade, BladeDark, Arm;
    static readonly Brush Coal, Carrot, Scarf, ScarfDark, Twig, HatBrush, HintBg, HintFg, White;
    static readonly Typeface HintFace = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    static Renderer()
    {
        for (int i = 0; i <= 10; i++)
        {
            byte a = (byte)(i * 25.5);
            Solid[i] = Freeze(new SolidColorBrush(Color.FromArgb(a, 255, 255, 255)));
            var g = new RadialGradientBrush();
            g.GradientStops.Add(new GradientStop(Color.FromArgb(a, 255, 255, 255), 0));
            g.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(a * 0.8), 255, 255, 255), 0.5));
            g.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
            Soft[i] = Freeze(g);
        }

        PileFill = new LinearGradientBrush(Color.FromRgb(255, 255, 255), Color.FromRgb(222, 233, 246), 90);
        PileFill.Freeze();
        BallFill = new RadialGradientBrush { GradientOrigin = new Point(0.35, 0.3), Center = new Point(0.4, 0.35), RadiusX = 0.75, RadiusY = 0.75 };
        BallFill.GradientStops.Add(new GradientStop(Colors.White, 0));
        BallFill.GradientStops.Add(new GradientStop(Color.FromRgb(246, 249, 253), 0.45));
        BallFill.GradientStops.Add(new GradientStop(Color.FromRgb(204, 218, 235), 1));
        BallFill.Freeze();

        ShadowBrush = Rgb(10, 20, 45, 60);
        EdgeBrush = Rgb(160, 185, 215, 150);
        BallEdge = Rgb(150, 170, 195, 160);
        Track = Rgb(43, 47, 58);
        Wheel = Rgb(138, 147, 166);
        Yellow = Rgb(255, 197, 49);
        YellowDark = Rgb(201, 143, 0);
        Glass = Rgb(110, 198, 255);
        BeaconOn = Rgb(255, 122, 26);
        BeaconOff = Rgb(150, 70, 20);
        Blade = Rgb(196, 205, 218);
        BladeDark = Rgb(110, 122, 140);
        Arm = Rgb(90, 96, 110);
        Coal = Rgb(28, 28, 32);
        Carrot = Rgb(255, 138, 31);
        Scarf = Rgb(222, 38, 48);
        ScarfDark = Rgb(160, 20, 30);
        Twig = Rgb(107, 68, 35);
        HatBrush = Rgb(24, 24, 30);
        HintBg = Rgb(18, 28, 48, 205);
        HintFg = Rgb(255, 255, 255);
        White = Rgb(255, 255, 255);
    }

    static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }
    static Brush Rgb(byte r, byte g, byte b, byte a = 255) => Freeze(new SolidColorBrush(Color.FromArgb(a, r, g, b)));
    static Pen P(Brush b, double t) => new(b, t) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };

    static void DrawFlake(DrawingContext dc, Flake f)
    {
        int i = Math.Clamp((int)Math.Round(f.Alpha * 10), 0, 10);
        if (f.R > 2.3 * f.Sc) dc.DrawEllipse(Soft[i], null, new Point(f.X, f.Y), f.R * 1.35, f.R * 1.35);
        else dc.DrawEllipse(Solid[i], null, new Point(f.X, f.Y), f.R, f.R);
    }

    public static void Draw(DrawingContext dc, World w, int monIndex, double pixelsPerDip)
    {
        var mon = w.Monitors[monIndex];
        var b = mon.Bounds;

        // дальние снежинки — за сугробами
        foreach (var f in w.Flakes)
            if (f.Mon == monIndex && f.Depth < 0.45) DrawFlake(dc, f);

        foreach (var s in w.Surfaces)
        {
            if (!w.IsEnabled(s)) continue;
            if (s.R.Right < b.Left || s.R.Left > b.Right || s.R.Top < b.Top - 60 || s.R.Top > b.Bottom + 60) continue;
            DrawPile(dc, s);
        }

        var ps = w.PlowSurface;
        if (ps != null && ps.R.Top >= b.Top - 60 && ps.R.Top <= b.Bottom + 60) DrawPlow(dc, w, ps);

        foreach (var m in w.Snowmen)
        {
            if (!w.TryGetSurface(m.Key, out var s) || !w.SnowmanVisible(m, s)) continue;
            double x = s.R.Left + m.OffX;
            if (x < b.Left - 60 || x > b.Right + 60) continue;
            DrawSnowman(dc, w, m, x, s.R.Top + m.Scale);
        }

        foreach (var d in w.Debris)
            if (mon.Contains(d.X, d.Y)) DrawFlake(dc, d);

        foreach (var f in w.Flakes)
            if (f.Mon == monIndex && f.Depth >= 0.45) DrawFlake(dc, f);

        if (w.SculptMode && mon.Contains(w.CursorX, w.CursorY))
        {
            double sc = mon.Scale;
            if (w.CursorOnSnow)
                dc.DrawEllipse(null, P(White, 1.6 * sc), new Point(w.CursorX, w.CursorY), 10 * sc, 10 * sc);
            DrawHint(dc, mon, pixelsPerDip,
                "☃  Режим лепки:  клик по сугробу — снежный ком (3 кома = снеговик)  ·  правый клик по снеговику — разрушить  ·  Esc — выход");
        }
    }

    static void DrawPile(DrawingContext dc, Surface s)
    {
        int n = s.Cols;
        double sc = s.Scale, top = s.R.Top, baseY = top + 1.2 * sc, min = 0.35 * sc;
        var shadow = P(ShadowBrush, 1.4 * sc);
        var edge = P(EdgeBrush, 0.9 * sc);
        int i = 0;
        var pts = new List<Point>(n + 2);
        while (i < n)
        {
            while (i < n && !(s.Vis[i] && s.H[i] > min)) i++;
            if (i >= n) break;
            int start = i;
            while (i < n && s.Vis[i] && s.H[i] > min) i++;
            int end = i - 1;

            double x0 = s.R.Left + start * s.Cell;
            double x1 = Math.Min(s.R.Right, s.R.Left + (end + 1) * s.Cell);
            pts.Clear();
            pts.Add(new Point(x0, top - s.H[start] * 0.45));
            for (int c = start; c <= end; c++) pts.Add(new Point(s.ColX(c), top - s.H[c]));
            pts.Add(new Point(x1, top - s.H[end] * 0.45));

            var fill = new StreamGeometry();
            using (var ctx = fill.Open())
            {
                ctx.BeginFigure(new Point(x0, baseY), true, true);
                ctx.PolyLineTo(pts, false, true);
                ctx.LineTo(new Point(x1, baseY), false, true);
            }
            fill.Freeze();

            var line = new StreamGeometry();
            using (var ctx = line.Open())
            {
                ctx.BeginFigure(pts[0], false, false);
                ctx.PolyLineTo(pts, true, true);
            }
            line.Freeze();

            dc.DrawLine(shadow, new Point(x0 + sc, baseY + 0.7 * sc), new Point(x1 - sc, baseY + 0.7 * sc));
            dc.DrawGeometry(PileFill, null, fill);
            dc.DrawGeometry(null, edge, line);
        }
    }

    static void DrawPlow(DrawingContext dc, World w, Surface s)
    {
        double u = w.Plow.Scale, X = w.Plow.X;
        double bse = s.R.Top + 0.5 * u + Math.Sin(w.Time * 22) * 0.35 * u;

        // снежный клин перед отвалом
        double hW = w.WedgeHeight(s);
        if (hW > 1)
        {
            double lW = hW * 2.6;
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(X + 1.5 * u, bse + 0.8 * u), true, true);
                ctx.LineTo(new Point(X + 1.5 * u, bse - hW), false, true);
                ctx.BezierTo(new Point(X - lW * 0.25, bse - hW * 1.05), new Point(X - lW * 0.7, bse - hW * 0.3),
                             new Point(X - lW, bse + 0.8 * u), false, true);
            }
            g.Freeze();
            dc.DrawGeometry(PileFill, P(EdgeBrush, 0.9 * u), g);
        }

        var yPen = P(YellowDark, 0.9 * u);
        dc.DrawRoundedRectangle(Track, null, new Rect(X + 4 * u, bse - 7 * u, 30 * u, 7 * u), 3.5 * u, 3.5 * u);
        for (int k = 0; k < 4; k++)
            dc.DrawEllipse(Wheel, null, new Point(X + 8 * u + k * 7.3 * u, bse - 3.5 * u), 2.3 * u, 2.3 * u);
        dc.DrawRoundedRectangle(Yellow, yPen, new Rect(X + 7 * u, bse - 14 * u, 26 * u, 7.5 * u), 1.5 * u, 1.5 * u);
        dc.DrawRoundedRectangle(Yellow, yPen, new Rect(X + 17 * u, bse - 24 * u, 13 * u, 10.5 * u), 1.5 * u, 1.5 * u);
        dc.DrawRoundedRectangle(Glass, null, new Rect(X + 19 * u, bse - 22 * u, 9 * u, 6 * u), u, u);
        bool blink = Math.Sin(w.Time * 9) > 0;
        dc.DrawRoundedRectangle(blink ? BeaconOn : BeaconOff, null, new Rect(X + 21.5 * u, bse - 27 * u, 4 * u, 3 * u), u, u);
        dc.DrawRoundedRectangle(White, null, new Rect(X + 16.5 * u, bse - 25.4 * u, 14 * u, 1.8 * u), 0.9 * u, 0.9 * u);
        dc.DrawLine(P(Arm, 1.8 * u), new Point(X + 2.5 * u, bse - 7.5 * u), new Point(X + 8 * u, bse - 10 * u));

        var blade = new StreamGeometry();
        using (var ctx = blade.Open())
        {
            ctx.BeginFigure(new Point(X - 0.5 * u, bse), true, true);
            ctx.LineTo(new Point(X + 3 * u, bse), true, true);
            ctx.LineTo(new Point(X + 4.5 * u, bse - 13 * u), true, true);
            ctx.LineTo(new Point(X + 0.5 * u, bse - 14.5 * u), true, true);
        }
        blade.Freeze();
        dc.DrawGeometry(Blade, P(BladeDark, 0.8 * u), blade);
    }

    static double Ease(double t) => 1 - Math.Pow(1 - Math.Clamp(t, 0, 1), 3);

    static void DrawSnowman(DrawingContext dc, World w, Snowman m, double x, double bse)
    {
        double u = m.Scale;
        double r0 = World.R0 * u * Ease(m.Grow[0]);
        double r1 = m.Balls > 1 ? World.R1 * u * Ease(m.Grow[1]) : 0;
        double r2 = m.Balls > 2 ? World.R2 * u * Ease(m.Grow[2]) : 0;
        double c0 = bse - r0 * 0.92;
        double c1 = c0 - r0 * 0.8 - r1 * 0.82;
        double c2 = c1 - r1 * 0.8 - r2 * 0.85;

        bool wob = m.Wobble > 0;
        if (wob) dc.PushTransform(new RotateTransform(Math.Sin(w.Time * 25) * m.Wobble * 7, x, bse));

        double a = m.Decor;
        if (a > 0)
        {
            dc.PushOpacity(a);
            var tw = P(Twig, 1.1 * u);
            dc.DrawLine(tw, new Point(x - r1 * 0.7, c1 - r1 * 0.1), new Point(x - r1 - 7 * u, c1 - 5 * u));
            dc.DrawLine(tw, new Point(x - r1 - 4 * u, c1 - 3.2 * u), new Point(x - r1 - 5 * u, c1 - 6.5 * u));
            dc.DrawLine(tw, new Point(x + r1 * 0.7, c1 - r1 * 0.1), new Point(x + r1 + 7 * u, c1 - 6 * u));
            dc.DrawLine(tw, new Point(x + r1 + 4 * u, c1 - 3.8 * u), new Point(x + r1 + 6.5 * u, c1 - 2.5 * u));
            dc.Pop();
        }

        var edge = P(BallEdge, 0.6 * u);
        dc.DrawEllipse(BallFill, edge, new Point(x, c0), r0, r0);
        if (r1 > 0) dc.DrawEllipse(BallFill, edge, new Point(x, c1), r1, r1);
        if (r2 > 0) dc.DrawEllipse(BallFill, edge, new Point(x, c2), r2, r2);

        if (a > 0)
        {
            dc.PushOpacity(a);
            // пуговицы
            dc.DrawEllipse(Coal, null, new Point(x, c1 - r1 * 0.2), 0.9 * u, 0.9 * u);
            dc.DrawEllipse(Coal, null, new Point(x, c1 + r1 * 0.3), 0.9 * u, 0.9 * u);
            dc.DrawEllipse(Coal, null, new Point(x, c0 - r0 * 0.25), 1.0 * u, 1.0 * u);
            // шарф
            double sy = c1 - r1 * 0.78;
            dc.DrawRoundedRectangle(Scarf, null, new Rect(x - r2 * 1.05, sy - 1.3 * u, r2 * 2.1, 2.6 * u), 1.2 * u, 1.2 * u);
            var tail = new StreamGeometry();
            using (var ctx = tail.Open())
            {
                ctx.BeginFigure(new Point(x + r2 * 0.3, sy), true, true);
                ctx.LineTo(new Point(x + r2 * 0.3 + 2.4 * u, sy), false, true);
                ctx.LineTo(new Point(x + r2 * 0.9 + 2.6 * u, sy + 6.5 * u), false, true);
                ctx.LineTo(new Point(x + r2 * 0.75, sy + 6.5 * u), false, true);
            }
            tail.Freeze();
            dc.DrawGeometry(Scarf, null, tail);
            dc.DrawLine(P(ScarfDark, 0.5 * u), new Point(x + r2 * 0.55, sy + 4.5 * u), new Point(x + r2 * 0.55 + 2.8 * u, sy + 4.5 * u));
            // лицо
            dc.DrawEllipse(Coal, null, new Point(x - 1.9 * u, c2 - 1.3 * u), 0.85 * u, 0.85 * u);
            dc.DrawEllipse(Coal, null, new Point(x + 1.9 * u, c2 - 1.3 * u), 0.85 * u, 0.85 * u);
            var nose = new StreamGeometry();
            using (var ctx = nose.Open())
            {
                ctx.BeginFigure(new Point(x + 0.3 * u, c2 + 0.1 * u), true, true);
                ctx.LineTo(new Point(x + 6 * u, c2 + 1.1 * u), false, true);
                ctx.LineTo(new Point(x + 0.3 * u, c2 + 1.9 * u), false, true);
            }
            nose.Freeze();
            dc.DrawGeometry(Carrot, null, nose);
            for (int k = -1; k <= 1; k++)
                dc.DrawEllipse(Coal, null, new Point(x + k * 1.5 * u, c2 + 3.1 * u - Math.Abs(k) * 0.5 * u), 0.45 * u, 0.45 * u);
            // шляпа
            double hy = c2 - r2 * 0.8;
            dc.DrawRoundedRectangle(HatBrush, null, new Rect(x - 5 * u, hy - 1.2 * u, 10 * u, 1.6 * u), 0.6 * u, 0.6 * u);
            dc.DrawRectangle(HatBrush, null, new Rect(x - 3.4 * u, hy - 7 * u, 6.8 * u, 6 * u));
            dc.DrawRectangle(Scarf, null, new Rect(x - 3.4 * u, hy - 2.6 * u, 6.8 * u, 1.2 * u));
            dc.Pop();
        }

        if (wob) dc.Pop();
    }

    static void DrawHint(DrawingContext dc, MonitorInfo mon, double ppd, string text)
    {
        double sc = mon.Scale;
        var ft = new FormattedText(text, CultureInfo.GetCultureInfo("ru-RU"), FlowDirection.LeftToRight, HintFace, 13 * sc, HintFg, ppd);
        ft.MaxTextWidth = Math.Max(100, mon.W - 80 * sc);
        double w = ft.Width + 28 * sc, h = ft.Height + 14 * sc;
        double x = mon.Left + (mon.W - w) / 2, y = mon.Top + 22 * sc;
        dc.DrawRoundedRectangle(HintBg, null, new Rect(x, y, w, h), 10 * sc, 10 * sc);
        dc.DrawText(ft, new Point(x + 14 * sc, y + 7 * sc));
    }
}
