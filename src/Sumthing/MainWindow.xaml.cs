using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Sumthing.Engine;
using Sumthing.Services;
using System.Windows.Controls.Primitives;

namespace Sumthing;

public partial class MainWindow : Window
{
    private static AppSettings S => SettingsService.Current;

    public CalcEngine Engine { get; } = new();
    public CalcMode Mode { get; private set; } = CalcMode.Standard;
    private CalcMode _lastCalcMode = CalcMode.Standard;
    private bool _wasClassic;

    public MainWindow()
    {
        InitializeComponent();
        ThemeManager.Attach(this);

        // restore what was kept from last time
        if (S.KeepBetweenSessions)
        {
            Engine.History.AddRange(S.History);
            Engine.RestoreMemory(S.Memory);
            TapePage.Load(S.Tape);
        }
        Engine.Tape += e => TapePage.Add(e);
        StandardPage.Attach(Engine);
        SumsPage.SendToCalculator += SendToCalculator;

        _wasClassic = ThemeManager.IsClassic;
        RestoreWindow();
        ApplyLook();
        ThemeManager.ThemeChanged += OnThemeChanged;

        PreviewKeyDown += OnPreviewKeyDown;
        PreviewTextInput += OnPreviewTextInput;
        Closing += OnClosing;
        Loaded += async (_, _) =>
        {
            SetMode(S.Mode == CalcMode.Settings ? CalcMode.Standard : S.Mode, resize: false);
            SetTape(S.ShowTape, resize: false);
            Topmost = S.AlwaysOnTop;
            UpdateChrome();
            if (App.IsSelfTest) await RunSelfTestAsync();
        };
    }

    // ================================================================ look

    private void OnThemeChanged()
    {
        bool classic = ThemeManager.IsClassic;
        if (classic != _wasClassic && WindowState == WindowState.Normal)
        {
            // each look keeps its own window size (the classic calculator is small)
            if (_wasClassic) { S.ClassicWidth = Width - TapeExtra(); S.ClassicHeight = Height; }
            else { S.WinWidth = Width - TapeExtra(); S.WinHeight = Height; }
            Width = (classic ? S.ClassicWidth : S.WinWidth) + TapeExtra();
            Height = classic ? S.ClassicHeight : S.WinHeight;
            EnsureModeSize();
        }
        _wasClassic = classic;
        ApplyLook();
    }

    private void ApplyLook()
    {
        bool classic = ThemeManager.IsClassic;
        ClassicMenu.Visibility = classic ? Visibility.Visible : Visibility.Collapsed;
        Header.Visibility = classic ? Visibility.Collapsed : Visibility.Visible;
        MinWidth = classic ? 240 : 320;
        MinHeight = classic ? 330 : 480;
        StandardPage.ApplyLook();
        TapePage.ApplyLook();
        UpdateChrome();
    }

    private void UpdateChrome()
    {
        ModeTitle.Text = Mode switch
        {
            CalcMode.Sums => "Type-a-sum",
            CalcMode.Converter => "Converter",
            CalcMode.Tools => "Everyday tools",
            CalcMode.Settings => "Settings",
            _ => "Standard",
        };
        HistoryButton.Visibility = Mode == CalcMode.Standard ? Visibility.Visible : Visibility.Collapsed;
        PinToggle.IsChecked = Topmost;
        TapeToggle.IsChecked = TapePage.Visibility == Visibility.Visible;

        MenuStandard.IsChecked = Mode == CalcMode.Standard;
        MenuSums.IsChecked = Mode == CalcMode.Sums;
        MenuConverter.IsChecked = Mode == CalcMode.Converter;
        MenuTools.IsChecked = Mode == CalcMode.Tools;
        MenuTape.IsChecked = TapePage.Visibility == Visibility.Visible;
        MenuHistory.IsEnabled = Mode == CalcMode.Standard;
        MenuOnTop.IsChecked = Topmost;
        MenuThemeSystem.IsChecked = S.Theme == ThemeMode.System;
        MenuThemeLight.IsChecked = S.Theme == ThemeMode.Light;
        MenuThemeDark.IsChecked = S.Theme == ThemeMode.Dark;
        MenuThemeClassic.IsChecked = S.Theme == ThemeMode.Classic;

        NavStandard.IsChecked = Mode == CalcMode.Standard;
        NavSums.IsChecked = Mode == CalcMode.Sums;
        NavConverter.IsChecked = Mode == CalcMode.Converter;
        NavTools.IsChecked = Mode == CalcMode.Tools;
        NavSettings.IsChecked = Mode == CalcMode.Settings;
        Title = Mode == CalcMode.Standard ? "Sumthing" : $"Sumthing - {ModeTitle.Text}";
    }

    // ================================================================ modes

    public void SetMode(CalcMode mode, bool resize = true)
    {
        if (mode != CalcMode.Settings) _lastCalcMode = mode;
        Mode = mode;
        StandardPage.Visibility = mode == CalcMode.Standard ? Visibility.Visible : Visibility.Collapsed;
        SumsPage.Visibility = mode == CalcMode.Sums ? Visibility.Visible : Visibility.Collapsed;
        ConverterPage.Visibility = mode == CalcMode.Converter ? Visibility.Visible : Visibility.Collapsed;
        ToolsPage.Visibility = mode == CalcMode.Tools ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = mode == CalcMode.Settings ? Visibility.Visible : Visibility.Collapsed;
        if (mode != CalcMode.Standard) StandardPage.ShowHistory(false);
        if (mode == CalcMode.Converter) ConverterPage.OnShown();
        if (mode == CalcMode.Settings) SettingsPage.OnShown();
        if (mode == CalcMode.Sums) SumsPage.OnShown();
        if (mode != CalcMode.Settings) S.Mode = mode;
        NavLayer.Visibility = Visibility.Collapsed;
        if (resize) EnsureModeSize();
        UpdateChrome();
    }

    /// <summary>The other modes need more room than the little classic calculator.</summary>
    private void EnsureModeSize()
    {
        if (WindowState != WindowState.Normal || Mode == CalcMode.Standard) return;
        double w = 380 + TapeExtra(), h = 560;
        if (Width < w) Width = w;
        if (Height < h) Height = h;
    }

    private double TapeExtra() => TapePage.Visibility == Visibility.Visible ? TapePage.Width : 0;

    private void SetTape(bool show, bool resize = true)
    {
        bool shown = TapePage.Visibility == Visibility.Visible;
        if (show == shown) { UpdateChrome(); return; }
        TapePage.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (resize && WindowState == WindowState.Normal)
            Width += show ? TapePage.Width : -TapePage.Width;
        else if (!resize && show && WindowState == WindowState.Normal)
            Width += TapePage.Width;
        S.ShowTape = show;
        UpdateChrome();
    }

    public void ToggleTape() => SetTape(TapePage.Visibility != Visibility.Visible);

    private void SendToCalculator(decimal value, string label)
    {
        SetMode(CalcMode.Standard);
        Engine.SetValue(value, label);
    }

    // ================================================================ events

    private void Nav_Click(object sender, RoutedEventArgs e) =>
        NavLayer.Visibility = NavLayer.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

    private void NavDismiss_MouseDown(object sender, MouseButtonEventArgs e) => NavLayer.Visibility = Visibility.Collapsed;

    private void NavItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && Enum.TryParse<CalcMode>(fe.Tag as string, out var m)) SetMode(m);
    }

    private void MenuMode_Click(object sender, RoutedEventArgs e) => NavItem_Click(sender, e);

    private void MenuTheme_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && Enum.TryParse<ThemeMode>(fe.Tag as string, out var t))
        {
            S.Theme = t;
            SettingsService.Commit();
        }
    }

    private void TapeToggle_Click(object sender, RoutedEventArgs e) => ToggleTape();

    private void History_Click(object sender, RoutedEventArgs e)
    {
        if (Mode != CalcMode.Standard) SetMode(CalcMode.Standard);
        StandardPage.ShowHistory(!StandardPage.IsHistoryOpen);
    }

    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        S.AlwaysOnTop = Topmost;
        SettingsService.Save();
        UpdateChrome();
    }

    private void Copy_Click(object sender, RoutedEventArgs e) => CopyResult();
    private void Paste_Click(object sender, RoutedEventArgs e) => PasteValue();
    private void CopyTape_Click(object sender, RoutedEventArgs e) => TapePage.CopyAll();
    private void ClearTape_Click(object sender, RoutedEventArgs e) => TapePage.Clear();
    private void ClearHistory_Click(object sender, RoutedEventArgs e) => Engine.ClearHistory();

    private void About_Click(object sender, RoutedEventArgs e)
    {
        SetMode(CalcMode.Settings);
        SettingsPage.ScrollToAbout();
    }

    private void Shortcuts_Click(object sender, RoutedEventArgs e)
    {
        SetMode(CalcMode.Settings);
        SettingsPage.ScrollToShortcuts();
    }

    public void CopyResult()
    {
        try { Clipboard.SetText(Engine.HasError ? "" : Num.Plain(Engine.Value)); } catch { }
    }

    public void PasteValue()
    {
        string text;
        try { text = Clipboard.GetText(); } catch { return; }
        if (string.IsNullOrWhiteSpace(text)) return;
        if (Num.TryParse(text, out var v)) { SetMode(CalcMode.Standard); Engine.SetValue(v); return; }
        // a whole sum: work it out like Type-a-sum does
        var r = SumSheet.Evaluate(new[] { text.Replace('\n', ' ').Replace('\r', ' ') });
        if (r.Count == 1 && r[0].Value is decimal d)
        {
            SetMode(CalcMode.Standard);
            Engine.SetValue(d, text.Trim() + " =");
        }
    }

    // ================================================================ keyboard

    private static bool InTextBox => Keyboard.FocusedElement is TextBoxBase or ComboBox or DatePicker
                                     || (Keyboard.FocusedElement is DependencyObject o && FindParent<DatePicker>(o) != null);

    private static T FindParent<T>(DependencyObject o) where T : DependencyObject
    {
        while (o != null)
        {
            if (o is T t) return t;
            o = System.Windows.Media.VisualTreeHelper.GetParent(o) ?? LogicalTreeHelper.GetParent(o);
        }
        return null;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        if (mods == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.D1: case Key.NumPad1: SetMode(CalcMode.Standard); e.Handled = true; return;
                case Key.D2: case Key.NumPad2: SetMode(CalcMode.Sums); e.Handled = true; return;
                case Key.D3: case Key.NumPad3: SetMode(CalcMode.Converter); e.Handled = true; return;
                case Key.D4: case Key.NumPad4: SetMode(CalcMode.Tools); e.Handled = true; return;
                case Key.T: ToggleTape(); e.Handled = true; return;
                case Key.H: History_Click(this, null); e.Handled = true; return;
                case Key.OemComma: SetMode(CalcMode.Settings); e.Handled = true; return;
            }
        }
        if (e.Key == Key.Escape && NavLayer.Visibility == Visibility.Visible) { NavLayer.Visibility = Visibility.Collapsed; e.Handled = true; return; }
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
        bool classic = ThemeManager.IsClassic;
        Width = classic ? S.ClassicWidth : S.WinWidth;
        Height = classic ? S.ClassicHeight : S.WinHeight;
        if (!double.IsNaN(S.WinLeft) && !double.IsNaN(S.WinTop))
        {
            var area = SystemParameters.VirtualScreenWidth;
            if (S.WinLeft > SystemParameters.VirtualScreenLeft - 50 && S.WinLeft < SystemParameters.VirtualScreenLeft + area - 80 &&
                S.WinTop > SystemParameters.VirtualScreenTop - 20 && S.WinTop < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 80)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = S.WinLeft;
                Top = S.WinTop;
            }
        }
    }

    private void OnClosing(object sender, CancelEventArgs e)
    {
        if (App.IsSelfTest) return;
        SaveState();
    }

    public void SaveState()
    {
        if (WindowState == WindowState.Normal)
        {
            S.WinLeft = Left;
            S.WinTop = Top;
            double w = Width - TapeExtra();
            // the other modes stretch the window; remember the calculator's own size only from Standard
            if (Mode == CalcMode.Standard)
            {
                if (ThemeManager.IsClassic) { S.ClassicWidth = w; S.ClassicHeight = Height; }
                else { S.WinWidth = w; S.WinHeight = Height; }
            }
        }
        S.SumSheet = SumsPage.SheetText;
        if (S.KeepBetweenSessions)
        {
            S.History = Engine.History.ToList();
            S.Memory = Engine.Memory;
            S.Tape = TapePage.Entries.ToList();
        }
        else
        {
            S.History = new();
            S.Memory = null;
            S.Tape = new();
        }
        SettingsService.Save();
    }
}
