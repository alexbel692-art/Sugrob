using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Sugrob;

/// <summary>Прозрачное окно на весь монитор поверх всех окон. Мышь проходит насквозь.</summary>
internal sealed class OverlayWindow : Window
{
    readonly World world;
    readonly int index;
    readonly SnowCanvas canvas;
    public MonitorInfo Mon { get; }
    public IntPtr Handle { get; private set; }

    public OverlayWindow(World world, MonitorInfo mon, int index)
    {
        this.world = world;
        this.index = index;
        Mon = mon;

        Title = "Sugrob overlay";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        Focusable = false;
        IsHitTestVisible = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = mon.Left / mon.Scale;
        Top = mon.Top / mon.Scale;
        Width = mon.W / mon.Scale;
        Height = mon.H / mon.Scale;

        canvas = new SnowCanvas(this);
        Content = canvas;

        SourceInitialized += (_, _) =>
        {
            Handle = new WindowInteropHelper(this).Handle;
            long ex = Native.GetExStyle(Handle);
            Native.SetExStyle(Handle, ex | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_LAYERED);
            ApplyBounds();
        };
    }

    public void ApplyBounds()
    {
        if (Handle == IntPtr.Zero) return;
        Native.SetWindowPos(Handle, Native.HWND_TOPMOST, Mon.Left, Mon.Top, Mon.W, Mon.H,
            Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW | Native.SWP_NOOWNERZORDER);
    }

    /// <summary>Панель задач и другие topmost-окна могут подняться выше — возвращаемся наверх.</summary>
    public void BringToTop()
    {
        if (Handle == IntPtr.Zero) return;
        Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Mon.Scale = newDpi.DpiScaleX;
        Dispatcher.BeginInvoke(new Action(ApplyBounds));
    }

    public void Redraw() => canvas.InvalidateVisual();

    sealed class SnowCanvas : FrameworkElement
    {
        readonly OverlayWindow owner;
        public SnowCanvas(OverlayWindow owner) { this.owner = owner; IsHitTestVisible = false; }

        protected override void OnRender(DrawingContext dc)
        {
            var w = owner.world;
            var m = owner.Mon;
            if (w.Paused || m.Suppressed || owner.index >= w.Monitors.Count) return;
            var dpi = VisualTreeHelper.GetDpi(this);
            double s = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : m.Scale;
            // мир в физических пикселях → DIP этого окна
            dc.PushTransform(new MatrixTransform(1 / s, 0, 0, 1 / s, -m.Left / s, -m.Top / s));
            try { Renderer.Draw(dc, w, owner.index, dpi.PixelsPerDip); }
            finally { dc.Pop(); }
        }
    }
}
