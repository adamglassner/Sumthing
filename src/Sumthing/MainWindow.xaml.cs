using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Sumthing.Engine;
using Sumthing.Maths;
using Sumthing.Services;

namespace Sumthing;

/// <summary>A page in the sidebar.</summary>
public sealed class NavItem : INotifyPropertyChanged
{
    public CalcMode Mode { get; init; }
    public string Name { get; init; } = "";
    public string Glyph { get; init; } = "";
    public string Tagline { get; init; } = "";
    public bool IsComingSoon => false;

    private string _title = "";
    /// <summary>What the sidebar shows (empty when the sidebar is collapsed to icons).</summary>
    public string Title { get => _title; set { _title = value; OnChanged(); } }

    private bool _selected;
    public bool IsSelected { get => _selected; set { _selected = value; OnChanged(); } }

    public event PropertyChangedEventHandler PropertyChanged;
    private void OnChanged([CallerMemberName] string n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public partial class MainWindow : Window
{
    private static AppSettings S => SettingsService.Current;

    public CalcEngine Engine { get; } = new();
    public CalcMode Mode { get; private set; } = CalcMode.Standard;
    public bool IsCompact { get; private set; }

    private readonly List<NavItem> _nav = new()
    {
        new() { Mode = CalcMode.Standard, Name = "Standard", Glyph = "", Tagline = "Everyday sums with memory, history and a paper tape" },
        new() { Mode = CalcMode.Scientific, Name = "Scientific", Glyph = "", Tagline = "A TI-style home screen: functions, Ans, stored values, fractions" },
        new() { Mode = CalcMode.Graphing, Name = "Graphing", Glyph = "", Tagline = "Graph functions, inequalities, polar and parametric curves, like a TI-84 and TI-Nspire" },
        new() { Mode = CalcMode.Statistics, Name = "Statistics", Glyph = "", Tagline = "Lists, 1-Var and 2-Var stats, regressions and scatter plots" },
        new() { Mode = CalcMode.Sums, Name = "Type-a-sum", Glyph = "", Tagline = "Type sums in words, one per line, with running totals" },
        new() { Mode = CalcMode.Converter, Name = "Converter", Glyph = "", Tagline = "Units and currencies" },
        new() { Mode = CalcMode.Tools, Name = "Everyday tools", Glyph = "", Tagline = "Tip and split, percent change, discount and tax, dates" },
    };

    private static readonly Dictionary<CalcMode, string> Subtitles = new()
    {
        [CalcMode.Standard] = "Everyday sums with memory, history and a paper tape",
        [CalcMode.Scientific] = "Like a TI home screen: type a line, press Enter. Stored values and lists are shared with Graphing and Statistics.",
        [CalcMode.Graphing] = "Functions, inequalities, polar, parametric and relations, with trace, table, zoom and analysis, like a TI-84 and TI-Nspire",
        [CalcMode.Statistics] = "Lists L1 to L6, 1-Var and 2-Var stats and regressions, like a TI-84's STAT menu",
        [CalcMode.Sums] = "Type a sum on each line and the answers appear as you type",
        [CalcMode.Converter] = "Length, weight, temperature, volume, area, speed, time, data, fuel and currency",
        [CalcMode.Tools] = "Tip and bill splitter, percent change, discount and sales tax, date difference",
        [CalcMode.Settings] = "Appearance, calculator options, shortcuts and what's new",
    };

    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(2.6) };
    private double _fullWidth = 1280, _fullHeight = 820;
    private bool _fullMaximized;

    public MainWindow()
    {
        InitializeComponent();
        ThemeManager.Attach(this);

        if (S.KeepBetweenSessions)
        {
            Engine.History.AddRange(S.History);
            Engine.RestoreMemory(S.Memory);
            StandardPage.TapePanel.Load(S.Tape);
        }
        Engine.Tape += e => StandardPage.TapePanel.Add(e);
        Engine.Changed += UpdateAnsCard;
        StandardPage.Attach(Engine);
        StandardPage.ShowSideTab(S.RightPanel);
        SumsPage.SendToCalculator += SendToCalculator;
        StatsPage.PlotRequested += rows =>
        {
            GraphPage.SetRows(rows);
            SetMode(CalcMode.Graphing);
            GraphPage.ZoomPreset("Data");
        };

        NavCalc.ItemsSource = _nav.Take(5).ToList();
        NavTools.ItemsSource = _nav.Skip(5).ToList();
        foreach (var n in _nav) n.Title = n.Name;

        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); HideToast(); };
        RestoreWindow();
        ApplyLook();
        ThemeManager.ThemeChanged += ApplyLook;
        MathState.Changed += UpdateAnsCard;
        SettingsService.Changed += () => { if (IsCompact) Topmost = S.AlwaysOnTop; };

        PreviewKeyDown += OnPreviewKeyDown;
        PreviewTextInput += OnPreviewTextInput;
        Closing += OnClosing;
        Loaded += async (_, _) =>
        {
            SetSidebar(S.SidebarCollapsed);
            SetMode(S.Mode, animate: false);
            if (S.CompactMode) SetCompact(true);
            UpdateAnsCard();
            if (App.IsSelfTest) await RunSelfTestAsync();
        };
    }

    // ================================================================ look

    private void ApplyLook()
    {
        StandardPage.ApplyLook();
        ThemeGlyph.Text = ThemeManager.IsDark ? "" : "";
        ThemeLabel.Text = ThemeManager.IsDark ? "Light mode" : "Dark mode";
        ThemeButton.ToolTip = "Switch between light and dark";
        UpdateStatus();
    }

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        S.Theme = ThemeManager.IsDark ? ThemeMode.Light : ThemeMode.Dark;
        SettingsService.Commit();
    }

    private void UpdateAnsCard()
    {
        var entry = MathState.Session.Entries.LastOrDefault(x => x.Value != null);
        double? sci = entry?.Value;
        if (Mode == CalcMode.Standard || sci == null)
        {
            AnsText.Text = Engine.HasError ? "—" : Engine.Display;
            AnsDetail.Text = Engine.Memory is decimal m ? $"Memory {Num.Format(m)}" : "Standard calculator";
        }
        else
        {
            AnsText.Text = Parser.Format(MathState.Context.Ans);
            AnsDetail.Text = $"Ans on Scientific · {(MathState.Degrees ? "degrees" : "radians")}";
        }
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        string angle = MathState.Degrees ? "Degrees" : "Radians";
        StatusText.Text = Mode switch
        {
            CalcMode.Standard => Engine.Memory is decimal m ? $"Memory {Num.Format(m)} · Ctrl+Shift+M for the compact calculator" : "Type with your keyboard · Ctrl+Shift+M for the compact calculator",
            CalcMode.Scientific or CalcMode.Graphing => $"{angle} · {MathState.Context.Vars.Count(v => !MathState.Graph.Sliders.ContainsKey(v.Key))} stored values · {MathState.Context.Lists.Count(l => l.Value.Length > 0)} lists",
            CalcMode.Statistics => $"{MathState.Context.Lists.Count(l => l.Value.Length > 0)} lists with data",
            _ => "Ready",
        };
        StatusRight.Text = $"Sumthing {AppInfo.Version} · {(ThemeManager.IsClassic ? "Classic keys" : "Modern keys")}";
    }

    // ================================================================ navigation

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is NavItem n && n.Mode != Mode) SetMode(n.Mode);
    }

    public void SetMode(CalcMode mode, bool animate = true)
    {
        if (IsCompact && mode != CalcMode.Standard) SetCompact(false);
        Mode = mode;
        foreach (var n in _nav) n.IsSelected = n.Mode == mode;
        var pages = new Dictionary<CalcMode, FrameworkElement>
        {
            [CalcMode.Standard] = StandardPage, [CalcMode.Scientific] = ScientificPage, [CalcMode.Graphing] = GraphPage,
            [CalcMode.Statistics] = StatsPage, [CalcMode.Sums] = SumsPage, [CalcMode.Converter] = ConverterPage,
            [CalcMode.Tools] = ToolsPage, [CalcMode.Settings] = SettingsPage,
        };
        foreach (var kv in pages) kv.Value.Visibility = kv.Key == mode ? Visibility.Visible : Visibility.Collapsed;
        PageTitle.Text = mode == CalcMode.Settings ? "Settings" : _nav.First(n => n.Mode == mode).Name;
        PageSubtitle.Text = Subtitles[mode];
        HeaderCompact.Visibility = mode == CalcMode.Standard ? Visibility.Visible : Visibility.Collapsed;
        Title = mode == CalcMode.Standard ? "Sumthing" : $"Sumthing - {PageTitle.Text}";
        if (mode != CalcMode.Settings) S.Mode = mode;

        switch (mode)
        {
            case CalcMode.Scientific: ScientificPage.OnShown(); break;
            case CalcMode.Converter: ConverterPage.OnShown(); break;
            case CalcMode.Settings: SettingsPage.OnShown(); break;
            case CalcMode.Sums: SumsPage.OnShown(); break;
            case CalcMode.Graphing: GraphPage.Recompile(); break;
        }
        UpdateAnsCard();

        if (animate && !App.IsSelfTest)
        {
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            var slide = new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            PageHost.BeginAnimation(OpacityProperty, fade);
            PageShift.BeginAnimation(TranslateTransform.YProperty, slide);
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => SetMode(CalcMode.Settings);

    // ================================================================ sidebar collapse

    private void Collapse_Click(object sender, RoutedEventArgs e) => SetSidebar(SidebarColumn.Width.Value > 100);

    public void SetSidebar(bool collapsed)
    {
        S.SidebarCollapsed = collapsed;
        SidebarColumn.Width = new GridLength(collapsed ? 76 : 248);
        Sidebar.Padding = collapsed ? new Thickness(10, 20, 10, 14) : new Thickness(16, 20, 16, 14);
        var vis = collapsed ? Visibility.Collapsed : Visibility.Visible;
        Wordmark.Visibility = vis;
        ((FrameworkElement)Wordmark.Parent).Visibility = vis;
        Heading1.Visibility = vis;
        Heading2.Visibility = collapsed ? Visibility.Hidden : Visibility.Visible;
        Heading2.Height = collapsed ? 10 : double.NaN;
        AnsCard.Visibility = vis;
        CompactLabel.Visibility = vis;
        ThemeLabel.Visibility = vis;
        SettingsLabel.Visibility = vis;
        CollapseButton.HorizontalAlignment = collapsed ? HorizontalAlignment.Center : HorizontalAlignment.Right;
        DockPanel.SetDock(CollapseButton, collapsed ? Dock.Top : Dock.Right);
        CollapseGlyph.Text = collapsed ? "" : "";
        CollapseButton.ToolTip = collapsed ? "Expand the sidebar (Ctrl+B)" : "Collapse the sidebar (Ctrl+B)";
        foreach (var n in _nav) n.Title = collapsed ? "" : n.Name;
    }

    // ================================================================ compact mode

    private void Compact_Click(object sender, RoutedEventArgs e) => SetCompact(!IsCompact);

    /// <summary>
    /// Compact: a small calculator window (the Standard keypad only, optionally on top).
    /// Full: the sidebar app at its previous size (or maximized).
    /// </summary>
    public void SetCompact(bool compact)
    {
        if (compact == IsCompact) return;
        if (compact && Mode != CalcMode.Standard) SetMode(CalcMode.Standard, animate: false);
        IsCompact = compact;
        S.CompactMode = compact;

        if (compact)
        {
            _fullMaximized = WindowState == WindowState.Maximized;
            if (WindowState == WindowState.Normal) { _fullWidth = Width; _fullHeight = Height; }
            WindowState = WindowState.Normal;
        }

        Sidebar.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        SidebarColumn.Width = compact ? new GridLength(0) : new GridLength(S.SidebarCollapsed ? 76 : 248);
        PageHeader.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        CompactBar.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        StatusBar.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        PageHost.Margin = compact ? new Thickness(8, 0, 8, 8) : new Thickness(36, 0, 36, 24);
        StandardPage.SetCompact(compact);
        CompactLabel.Text = "Compact calculator";

        if (compact)
        {
            MinWidth = 280; MinHeight = 420;
            bool classic = ThemeManager.IsClassic;
            Width = classic ? 300 : 360;
            Height = classic ? 460 : 600;
            if (!double.IsNaN(S.CompactLeft) && OnScreen(S.CompactLeft, S.CompactTop)) { Left = S.CompactLeft; Top = S.CompactTop; }
            Topmost = S.AlwaysOnTop;
            PinToggle.IsChecked = Topmost;
        }
        else
        {
            if (WindowState == WindowState.Normal) { S.CompactLeft = Left; S.CompactTop = Top; }
            Topmost = false;
            MinWidth = 980; MinHeight = 640;
            Width = _fullWidth; Height = _fullHeight;
            if (Left + Width > SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth) Left = System.Math.Max(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - Width - 20);
            if (Top + Height > SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight) Top = System.Math.Max(SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - Height - 40);
            if (_fullMaximized) WindowState = WindowState.Maximized;
        }
    }

    private static bool OnScreen(double left, double top) =>
        left > SystemParameters.VirtualScreenLeft - 50 && left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 80 &&
        top > SystemParameters.VirtualScreenTop - 20 && top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 80;

    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        Topmost = PinToggle.IsChecked == true;
        S.AlwaysOnTop = Topmost;
        SettingsService.Save();
    }

    private void CompactHistory_Click(object sender, RoutedEventArgs e) => StandardPage.ShowHistory(!StandardPage.IsHistoryOpen);

    // ================================================================ shared helpers

    private void SendToCalculator(decimal value, string label)
    {
        SetMode(CalcMode.Standard);
        Engine.SetValue(value, label);
    }

    public void CopyResult()
    {
        try { Clipboard.SetText(Engine.HasError ? "" : Num.Plain(Engine.Value)); ShowToast("Copied " + Engine.Display); } catch { }
    }

    public void PasteValue()
    {
        string text;
        try { text = Clipboard.GetText(); } catch { return; }
        if (string.IsNullOrWhiteSpace(text)) return;
        if (Num.TryParse(text, out var v)) { Engine.SetValue(v); return; }
        var r = SumSheet.Evaluate(new[] { text.Replace('\n', ' ').Replace('\r', ' ') });
        if (r.Count == 1 && r[0].Value is decimal d) Engine.SetValue(d, text.Trim() + " =");
    }

    /// <summary>A short message in the corner, like WillowWallet's.</summary>
    public void ShowToast(string message)
    {
        ToastText.Text = message;
        Toast.Visibility = Visibility.Visible;
        Toast.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));
        ToastShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(200)));
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void HideToast()
    {
        var a = new DoubleAnimation(0, TimeSpan.FromMilliseconds(200));
        a.Completed += (_, _) => Toast.Visibility = Visibility.Collapsed;
        Toast.BeginAnimation(OpacityProperty, a);
    }

    // ================================================================ keyboard

    private static bool InTextBox => Keyboard.FocusedElement is TextBoxBase or ComboBox or DatePicker or Slider
                                     || (Keyboard.FocusedElement is DependencyObject o && FindParent<DatePicker>(o) != null);

    private static T FindParent<T>(DependencyObject o) where T : DependencyObject
    {
        while (o != null)
        {
            if (o is T t) return t;
            o = o is Visual ? VisualTreeHelper.GetParent(o) ?? LogicalTreeHelper.GetParent(o) : LogicalTreeHelper.GetParent(o);
        }
        return null;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        if (mods == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.M) { SetCompact(!IsCompact); e.Handled = true; return; }
        if (mods == ModifierKeys.Control)
        {
            int n = e.Key switch
            {
                Key.D1 or Key.NumPad1 => 1, Key.D2 or Key.NumPad2 => 2, Key.D3 or Key.NumPad3 => 3, Key.D4 or Key.NumPad4 => 4,
                Key.D5 or Key.NumPad5 => 5, Key.D6 or Key.NumPad6 => 6, Key.D7 or Key.NumPad7 => 7, _ => 0,
            };
            if (n > 0) { SetMode(_nav[n - 1].Mode); e.Handled = true; return; }
            switch (e.Key)
            {
                case Key.B: if (!IsCompact) SetSidebar(SidebarColumn.Width.Value > 100); e.Handled = true; return;
                case Key.OemComma: SetMode(CalcMode.Settings); e.Handled = true; return;
                case Key.T:
                    SetMode(CalcMode.Standard);
                    if (IsCompact) SetCompact(false);
                    StandardPage.ShowSideTab("Tape");
                    e.Handled = true; return;
                case Key.H:
                    SetMode(CalcMode.Standard);
                    if (IsCompact) StandardPage.ShowHistory(!StandardPage.IsHistoryOpen);
                    else StandardPage.ShowSideTab("History");
                    e.Handled = true; return;
            }
        }
        if (Mode != CalcMode.Standard || InTextBox) return;
        if (StandardPage.HandleKeyDown(e.Key == Key.System ? e.SystemKey : e.Key, mods)) e.Handled = true;
    }

    private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (Mode != CalcMode.Standard || InTextBox || string.IsNullOrEmpty(e.Text)) return;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        if (StandardPage.HandleText(e.Text)) e.Handled = true;
    }

    // ================================================================ window state

    private void RestoreWindow()
    {
        Width = _fullWidth = S.WinWidth;
        Height = _fullHeight = S.WinHeight;
        if (!double.IsNaN(S.WinLeft) && !double.IsNaN(S.WinTop) && OnScreen(S.WinLeft, S.WinTop))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = S.WinLeft;
            Top = S.WinTop;
        }
        if (S.WinMaximized && !S.CompactMode) WindowState = WindowState.Maximized;
    }

    private void OnClosing(object sender, CancelEventArgs e)
    {
        if (App.IsSelfTest) return;
        SaveState();
    }

    public void SaveState()
    {
        if (IsCompact)
        {
            if (WindowState == WindowState.Normal) { S.CompactLeft = Left; S.CompactTop = Top; }
            S.WinWidth = _fullWidth; S.WinHeight = _fullHeight; S.WinMaximized = _fullMaximized;
        }
        else
        {
            S.WinMaximized = WindowState == WindowState.Maximized;
            var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            if (!bounds.IsEmpty)
            {
                S.WinLeft = bounds.Left; S.WinTop = bounds.Top;
                S.WinWidth = bounds.Width; S.WinHeight = bounds.Height;
            }
        }
        S.SumSheet = SumsPage.SheetText;
        if (S.KeepBetweenSessions)
        {
            S.History = Engine.History.ToList();
            S.Memory = Engine.Memory;
            S.Tape = StandardPage.TapePanel.Entries.ToList();
            MathState.Save(S);
        }
        else
        {
            S.History = new(); S.Memory = null; S.Tape = new();
            var keepRows = MathState.Graph.Rows.ToList();
            MathState.Save(S);
            S.HomeHistory = new(); S.StoredVariables = new(); S.Lists = new();
            S.GraphRows = keepRows;
        }
        SettingsService.Save();
    }
}
