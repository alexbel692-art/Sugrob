using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Sugrob;

internal sealed class Tray : IDisposable
{
    readonly NotifyIcon icon;
    readonly ToolStripMenuItem pauseItem, sculptItem;

    public Tray(Controller c)
    {
        var menu = new ContextMenuStrip();
        sculptItem = new ToolStripMenuItem("Слепить снеговика", null, (_, _) => c.ToggleSculpt()) { ShortcutKeyDisplayString = "Ctrl+Alt+S" };
        menu.Items.Add(sculptItem);
        menu.Items.Add("Вызвать снегоуборщик", null, (_, _) => c.CallPlow());
        menu.Items.Add("Стряхнуть весь снег", null, (_, _) => c.ShakeAll());
        menu.Items.Add(new ToolStripSeparator());
        pauseItem = new ToolStripMenuItem("Пауза", null, (_, _) => c.TogglePause());
        menu.Items.Add(pauseItem);
        menu.Items.Add("Настройки…", null, (_, _) => c.ShowSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => c.Exit());

        icon = new NotifyIcon
        {
            Icon = MakeIcon(),
            Text = "Сугроб — снег на рабочем столе",
            ContextMenuStrip = menu,
            Visible = true
        };
        icon.DoubleClick += (_, _) => c.ShowSettings();
    }

    public void SetPaused(bool on) => pauseItem.Checked = on;
    public void SetSculpt(bool on) => sculptItem.Checked = on;

    public void Balloon(string text) => icon.ShowBalloonTip(5000, "Сугроб", text, ToolTipIcon.Info);

    static Icon MakeIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var bg = new SolidBrush(Color.FromArgb(255, 37, 99, 235));
            g.FillEllipse(bg, 1, 1, 30, 30);
            using var pen = new Pen(Color.White, 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            for (int k = 0; k < 6; k++)
            {
                double a = k * Math.PI / 3;
                float ex = 16 + (float)Math.Cos(a) * 11, ey = 16 + (float)Math.Sin(a) * 11;
                g.DrawLine(pen, 16, 16, ex, ey);
                float mx = 16 + (float)Math.Cos(a) * 7, my = 16 + (float)Math.Sin(a) * 7;
                for (int sgn = -1; sgn <= 1; sgn += 2)
                {
                    double b = a + sgn * 0.75;
                    g.DrawLine(pen, mx, my, mx + (float)Math.Cos(b) * 4, my + (float)Math.Sin(b) * 4);
                }
            }
        }
        IntPtr h = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(h).Clone();
        Native.DestroyIcon(h);
        return icon;
    }

    public void Dispose()
    {
        icon.Visible = false;
        icon.Dispose();
    }
}
