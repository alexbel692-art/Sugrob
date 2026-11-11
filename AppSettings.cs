using System;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace Sugrob;

internal sealed class AppSettings
{
    public int Intensity { get; set; } = 45;          // 0..100
    public int Wind { get; set; } = 8;                // -100..100
    public double FlakeSize { get; set; } = 1.0;      // 0.5..2
    public int MaxPile { get; set; } = 18;            // DIP
    public double Accumulation { get; set; } = 1.0;   // 0.2..3
    public bool PileOnWindows { get; set; } = true;
    public bool PileOnTaskbar { get; set; } = true;
    public bool Plow { get; set; } = true;
    public int PlowIntervalMin { get; set; } = 8;
    public bool HideInFullscreen { get; set; } = true;
    public int Fps { get; set; } = 30;
    public bool FirstRun { get; set; } = true;

    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sugrob");
    static string FilePath => Path.Combine(Dir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch { /* битый файл — берём настройки по умолчанию */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}

internal static class Autostart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Name = "Sugrob";

    public static bool IsOn()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, false);
            return k?.GetValue(Name) != null;
        }
        catch { return false; }
    }

    public static void Set(bool on)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, true) ?? Registry.CurrentUser.CreateSubKey(RunKey);
            if (on) k.SetValue(Name, "\"" + Environment.ProcessPath + "\"");
            else k.DeleteValue(Name, false);
        }
        catch { }
    }
}

internal static class Log
{
    public static void Error(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(AppSettings.Dir);
            File.AppendAllText(Path.Combine(AppSettings.Dir, "error.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\r\n\r\n");
        }
        catch { }
    }
}
