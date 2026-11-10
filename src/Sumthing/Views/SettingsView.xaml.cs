using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Navigation;
using Sumthing.Services;

namespace Sumthing.Views;

public partial class SettingsView : UserControl
{
    private static AppSettings S => SettingsService.Current;
    private bool _loading;

    private static readonly (string Keys, string What)[] Shortcuts =
    {
        ("0–9  .  + - * /", "Type numbers and operators"),
        ("Enter or =", "Equals"),
        ("Backspace", "Delete the last digit"),
        ("Esc", "Clear (C)"),
        ("Delete", "Clear entry (CE)"),
        ("%", "Percent"),
        ("F9", "Positive / negative"),
        ("R   Q   @", "1/x,  x²,  square root"),
        ("Ctrl+M  Ctrl+R  Ctrl+L", "Memory store, recall, clear"),
        ("Ctrl+P  Ctrl+Q", "Add to / subtract from memory"),
        ("Ctrl+C  Ctrl+V", "Copy the result, paste a number or a sum"),
        ("Ctrl+1 … Ctrl+4", "Standard, Type-a-sum, Converter, Everyday tools"),
        ("Ctrl+T", "Show or hide the tape"),
        ("Ctrl+H", "History"),
        ("Ctrl+,", "Settings"),
    };

    public SettingsView()
    {
        InitializeComponent();
        VersionText.Text = $"Version {AppInfo.Version}";
        ChangelogText.Text = ReadChangelog();
        for (int i = 0; i < Shortcuts.Length; i++)
        {
            ShortcutGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var k = new TextBlock { Text = Shortcuts[i].Keys, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12.5, Margin = new Thickness(0, 3, 16, 3) };
            var w = new TextBlock { Text = Shortcuts[i].What, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 3) };
            w.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
            Grid.SetRow(k, i); Grid.SetRow(w, i); Grid.SetColumn(w, 1);
            ShortcutGrid.Children.Add(k);
            ShortcutGrid.Children.Add(w);
        }
        foreach (var a in ThemeManager.Accents)
        {
            var b = new Button
            {
                Style = (Style)FindResource("SwatchButton"),
                Width = 30, Height = 30, Margin = new Thickness(0, 0, 10, 6),
                Background = new SolidColorBrush(a.Light),
                ToolTip = a.Name, Tag = a.Name,
            };
            System.Windows.Automation.AutomationProperties.SetName(b, a.Name + " accent");
            b.Click += Accent_Click;
            Swatches.Children.Add(b);
        }
        ThemeManager.ThemeChanged += Load;
    }

    public void OnShown() => Load();

    private void Load()
    {
        _loading = true;
        ThemeSystem.IsChecked = S.Theme == ThemeMode.System;
        ThemeLight.IsChecked = S.Theme == ThemeMode.Light;
        ThemeDark.IsChecked = S.Theme == ThemeMode.Dark;
        ThemeClassic.IsChecked = S.Theme == ThemeMode.Classic;
        AccentPanel.Visibility = S.Theme == ThemeMode.Classic ? Visibility.Collapsed : Visibility.Visible;
        foreach (var child in Swatches.Children)
            if (child is Button b)
            {
                bool on = b.Tag as string == S.Accent;
                b.BorderThickness = new Thickness(on ? 3 : 1);
                b.BorderBrush = on ? (Brush)FindResource("Text") : (Brush)FindResource("ControlBorder");
            }
        Separators.IsChecked = S.ThousandsSeparators;
        KeepSession.IsChecked = S.KeepBetweenSessions;
        OnTop.IsChecked = Window.GetWindow(this)?.Topmost ?? S.AlwaysOnTop;
        _loading = false;
    }

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not FrameworkElement fe || !Enum.TryParse<ThemeMode>(fe.Tag as string, out var t)) return;
        S.Theme = t;
        SettingsService.Commit();
    }

    private void Accent_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string name)
        {
            S.Accent = name;
            SettingsService.Commit();
        }
    }

    private void Option_Click(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        S.ThousandsSeparators = Separators.IsChecked == true;
        S.KeepBetweenSessions = KeepSession.IsChecked == true;
        S.AlwaysOnTop = OnTop.IsChecked == true;
        if (Window.GetWindow(this) is Window w) w.Topmost = S.AlwaysOnTop;
        SettingsService.Commit();
        if (Window.GetWindow(this) is MainWindow mw) mw.Engine.Refresh(); // redraw with/without separators
    }

    public void ScrollToAbout() => Dispatcher.InvokeAsync(() => AboutHeader.BringIntoView(), System.Windows.Threading.DispatcherPriority.Loaded);
    public void ScrollToShortcuts() => Dispatcher.InvokeAsync(() => ShortcutsHeader.BringIntoView(), System.Windows.Threading.DispatcherPriority.Loaded);

    private void Link_Navigate(object sender, RequestNavigateEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); } catch { }
        e.Handled = true;
    }

    private static string ReadChangelog()
    {
        try
        {
            using var s = typeof(SettingsView).Assembly.GetManifestResourceStream("CHANGELOG.md");
            if (s == null) return "";
            using var r = new StreamReader(s);
            var lines = r.ReadToEnd().Replace("\r\n", "\n").Split('\n')
                .Where(l => !l.StartsWith("# "))
                .Select(l => l.StartsWith("## ") ? "\n" + l[3..] : l.StartsWith("- ") ? "•  " + l[2..] : l);
            return string.Join("\n", lines).Trim();
        }
        catch { return ""; }
    }
}
