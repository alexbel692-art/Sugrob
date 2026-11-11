using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Sugrob;

/// <summary>Глобальные горячие клавиши через скрытое окно.</summary>
internal sealed class Hotkeys : IDisposable
{
    readonly HwndSource src;
    readonly Dictionary<int, Action> map = new();

    public Hotkeys()
    {
        var p = new HwndSourceParameters("SugrobHotkeys") { Width = 0, Height = 0, WindowStyle = 0 };
        src = new HwndSource(p);
        src.AddHook(WndProc);
    }

    public bool Register(int id, uint mods, uint vk, Action action)
    {
        Unregister(id);
        map[id] = action;
        return Native.RegisterHotKey(src.Handle, id, mods | Native.MOD_NOREPEAT, vk);
    }

    public void Unregister(int id)
    {
        if (map.Remove(id)) Native.UnregisterHotKey(src.Handle, id);
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && map.TryGetValue(wParam.ToInt32(), out var a))
        {
            handled = true;
            a();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (var id in new List<int>(map.Keys)) Unregister(id);
        src.Dispose();
    }
}

/// <summary>
/// Низкоуровневый хук мыши — только в режиме лепки. Клик по снегу перехватываем,
/// остальные клики идут в окна как обычно.
/// </summary>
internal sealed class MouseHook : IDisposable
{
    readonly Native.LowLevelMouseProc proc;   // держим ссылку, чтобы GC не собрал делегат
    readonly Func<int, int, bool, bool> onDown;
    IntPtr hook;
    bool swallowUpL, swallowUpR;

    public MouseHook(Func<int, int, bool, bool> onDown)
    {
        this.onDown = onDown;
        proc = Callback;
        hook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, proc, Native.GetModuleHandle(null), 0);
    }

    IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            int msg = wParam.ToInt32();
            if (msg == Native.WM_LBUTTONDOWN || msg == Native.WM_RBUTTONDOWN)
            {
                var d = Marshal.PtrToStructure<Native.MSLLHOOKSTRUCT>(lParam);
                bool right = msg == Native.WM_RBUTTONDOWN;
                bool mine = false;
                try { mine = onDown(d.pt.X, d.pt.Y, right); } catch (Exception ex) { Log.Error(ex); }
                if (mine)
                {
                    if (right) swallowUpR = true; else swallowUpL = true;
                    return new IntPtr(1);
                }
            }
            else if (msg == Native.WM_LBUTTONUP && swallowUpL) { swallowUpL = false; return new IntPtr(1); }
            else if (msg == Native.WM_RBUTTONUP && swallowUpR) { swallowUpR = false; return new IntPtr(1); }
        }
        return Native.CallNextHookEx(hook, code, wParam, lParam);
    }

    public void Dispose()
    {
        if (hook != IntPtr.Zero) { Native.UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
    }
}
