using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Sugrob;

internal sealed class SettingsWindow : Window
{
    readonly Controller c;
    readonly StackPanel panel = new() { Margin = new Thickness(18, 14, 18, 14) };
    AppSettings S => c.S;

    public SettingsWindow(Controller c)
    {
        this.c = c;
        Title = "Сугроб — настройки";
        Width = 430;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.CanMinimize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;

        AddHeader("Снегопад");
        AddSlider("Интенсивность", 0, 100, S.Intensity, v => $"{v:0}%", v => S.Intensity = (int)Math.Round(v));
        AddSlider("Ветер", -100, 100, S.Wind, v => v == 0 ? "штиль" : v < 0 ? $"← {-v:0}" : $"{v:0} →", v => S.Wind = (int)Math.Round(v));
        AddSlider("Размер снежинок", 0.5, 2, S.FlakeSize, v => $"×{v:0.0}", v => S.FlakeSize = v);

        AddHeader("Сугробы");
        AddCheck("Снег копится на окнах", S.PileOnWindows, v => S.PileOnWindows = v);
        AddCheck("Снег копится на панели задач", S.PileOnTaskbar, v => S.PileOnTaskbar = v);
        AddSlider("Максимальная высота", 6, 40, S.MaxPile, v => $"{v:0} px", v => S.MaxPile = (int)Math.Round(v));
        AddSlider("Скорость накопления", 0.2, 3, S.Accumulation, v => $"×{v:0.0}", v => S.Accumulation = v);

        AddHeader("Снегоуборщик");
        AddCheck("Сам чистит панель задач", S.Plow, v => S.Plow = v);
        AddSlider("Раз в", 1, 60, S.PlowIntervalMin, v => $"{v:0} мин", v => S.PlowIntervalMin = (int)Math.Round(v));

        AddHeader("Прочее");
        AddCheck("Прятать снег в полноэкранных играх и видео", S.HideInFullscreen, v => S.HideInFullscreen = v);
        AddCheck("Плавная анимация 60 FPS (больше нагрузка)", S.Fps >= 60, v => S.Fps = v ? 60 : 30);
        AddCheck("Запускать вместе с Windows", Autostart.IsOn(), v => Autostart.Set(v), save: false);

        panel.Children.Add(new TextBlock
        {
            Text = "Ctrl+Alt+S — режим лепки: кликай по сугробу, три кома — снеговик. Правый клик по снеговику — разрушить. Esc — выход из режима.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 14, 0, 0)
        });

        var close = new Button { Content = "Готово", Width = 100, Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Right, IsDefault = true };
        close.Click += (_, _) => Close();
        panel.Children.Add(close);

        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    void AddHeader(string text) => panel.Children.Add(new TextBlock
    {
        Text = text,
        FontWeight = FontWeights.SemiBold,
        FontSize = 15,
        Margin = new Thickness(0, panel.Children.Count == 0 ? 0 : 14, 0, 6)
    });

    void AddSlider(string label, double min, double max, double value, Func<double, string> fmt, Action<double> set)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });

        var lbl = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        var val = new TextBlock { Text = fmt(value), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        var sl = new Slider { Minimum = min, Maximum = max, Value = value, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) };
        sl.ValueChanged += (_, e) =>
        {
            val.Text = fmt(e.NewValue);
            set(e.NewValue);
            c.ApplySettings();
        };
        Grid.SetColumn(lbl, 0);
        Grid.SetColumn(sl, 1);
        Grid.SetColumn(val, 2);
        grid.Children.Add(lbl);
        grid.Children.Add(sl);
        grid.Children.Add(val);
        panel.Children.Add(grid);
    }

    void AddCheck(string label, bool value, Action<bool> set, bool save = true)
    {
        var cb = new CheckBox { Content = label, IsChecked = value, Margin = new Thickness(0, 4, 0, 4) };
        cb.Checked += (_, _) => { set(true); if (save) c.ApplySettings(); };
        cb.Unchecked += (_, _) => { set(false); if (save) c.ApplySettings(); };
        panel.Children.Add(cb);
    }
}
