using System;
using System.Threading;
using System.Windows;

namespace Sugrob;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(true, "Sugrob.SingleInstance.{6C1F0E52-9A2B-4C1E-9E1B-5A7D3B1C2F10}", out bool created);
        if (!created) return;   // уже запущен

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException += (_, e) => { Log.Error(e.Exception); e.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => { if (e.ExceptionObject is Exception ex) Log.Error(ex); };

        var controller = new Controller(app);
        app.Startup += (_, _) => controller.Start();
        app.Run();
        GC.KeepAlive(mutex);
    }
}
