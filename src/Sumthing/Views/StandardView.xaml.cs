using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Sumthing.Engine;
using Sumthing.Services;

namespace Sumthing.Views;

public partial class StandardView : UserControl
{
    private CalcEngine _engine;

    public StandardView()
    {
        InitializeComponent();
    }

    public CalcEngine Engine => _engine;

    // read by the self-test
    public Panel ModernPadPanel => ModernPad;
    public Panel ClassicPadPanel => ClassicPad;
    public string MainDisplay => ThemeManager.IsClassic ? ClassicMain.Text : MainText.Text;
    public string ExpressionDisplay => ThemeManager.IsClassic ? ClassicExpr.Text : ExprText.Text;

    public void Attach(CalcEngine engine)
    {
        _engine = engine;
        _engine.Changed += Refresh;
        Refresh();
    }

    public void ApplyLook()
    {
        bool classic = ThemeManager.IsClassic;
        ModernLayout.Visibility = classic ? Visibility.Collapsed : Visibility.Visible;
        ClassicLayout.Visibility = classic ? Visibility.Visible : Visibility.Collapsed;
        Refresh();
    }

    public void Refresh()
    {
        if (_engine == null) return;
        string main = _engine.Display;
        MainText.Text = main;
        ClassicMain.Text = main;
        // errors read better a bit smaller
        MainText.FontSize = _engine.HasError ? 30 : 48;
        ExprText.Text = _engine.Expression;
        ClassicExpr.Text = _engine.Expression;

        bool hasMem = _engine.Memory != null;
        ModernMC.IsEnabled = ModernMR.IsEnabled = hasMem;
        ClassicMC.IsEnabled = ClassicMR.IsEnabled = hasMem;
        ClassicMem.Visibility = hasMem ? Visibility.Visible : Visibility.Hidden;
        MemoryLine.Text = hasMem ? "Memory: " + Num.Format(_engine.Memory.Value) : "";
        MemoryLine.Visibility = hasMem ? Visibility.Visible : Visibility.Collapsed;

        if (HistoryLayer.Visibility == Visibility.Visible) FillHistory();
    }

    // ================================================================ keys

    private void Key_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string key) _engine?.Press(key);
    }

    /// <summary>Finds the on-screen key for a key name, so keyboard input flashes it.</summary>
    private CalcKey FindKey(string tag)
    {
        Panel pad = ThemeManager.IsClassic ? ClassicPad : ModernPad;
        foreach (var child in pad.Children)
            if (child is CalcKey k && k.Tag as string == tag) return k;
        if (!ThemeManager.IsClassic && ModernPad.Parent is Grid g)
            foreach (var row in g.Children)
                if (row is UniformGrid ug)
                    foreach (var child in ug.Children)
                        if (child is CalcKey k && k.Tag as string == tag) return k;
        return null;
    }

    public void PressKey(string tag)
    {
        FindKey(tag)?.Flash();
        _engine.Press(tag);
    }

    /// <summary>Typed characters: digits, operators, % . = and the Windows calculator letters (R, Q, @).</summary>
    public bool HandleText(string text)
    {
        if (_engine == null) return false;
        string tag = text switch
        {
            "0" or "1" or "2" or "3" or "4" or "5" or "6" or "7" or "8" or "9" => text,
            "." or "," => ".",
            "+" => "+",
            "-" or "−" => "−",
            "*" or "x" or "X" or "×" => "×",
            "/" or "÷" => "÷",
            "=" => "=",
            "%" => "%",
            "@" => "√",
            "r" or "R" => "inv",
            "q" or "Q" => "sqr",
            _ => null,
        };
        if (tag == null) return false;
        PressKey(tag);
        return true;
    }

    public bool HandleKeyDown(Key key, ModifierKeys mods)
    {
        if (_engine == null) return false;
        if (mods == ModifierKeys.Control)
        {
            switch (key)
            {
                case Key.C: (Window.GetWindow(this) as MainWindow)?.CopyResult(); return true;
                case Key.V: (Window.GetWindow(this) as MainWindow)?.PasteValue(); return true;
                case Key.L: PressKey("MC"); return true;
                case Key.R: PressKey("MR"); return true;
                case Key.M: PressKey("MS"); return true;
                case Key.P: PressKey("M+"); return true;
                case Key.Q: PressKey("M−"); return true;
            }
            return false;
        }
        if (mods != ModifierKeys.None && mods != ModifierKeys.Shift) return false;
        switch (key)
        {
            case Key.Enter: PressKey("="); return true;
            case Key.Back: PressKey("back"); return true;
            case Key.Escape:
                if (IsHistoryOpen) { ShowHistory(false); return true; }
                PressKey("C"); return true;
            case Key.Delete: PressKey("CE"); return true;
            case Key.F9: PressKey("neg"); return true;
        }
        return false;
    }

    // ================================================================ history

    public bool IsHistoryOpen => HistoryLayer.Visibility == Visibility.Visible;

    public void ShowHistory(bool show)
    {
        HistoryLayer.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show) FillHistory();
    }

    private void FillHistory()
    {
        HistoryList.ItemsSource = null;
        HistoryList.ItemsSource = _engine.History.ToList();
        bool empty = _engine.History.Count == 0;
        HistoryEmpty.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        HistoryList.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        ClearHistoryButton.IsEnabled = !empty;
    }

    private void HistoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HistoryList.SelectedItem is HistoryItem h)
        {
            ShowHistory(false);
            _engine.SetValue(h.Value, h.Expression);
        }
    }

    private void HistoryDismiss_MouseDown(object sender, MouseButtonEventArgs e) => ShowHistory(false);
    private void HistoryClose_Click(object sender, RoutedEventArgs e) => ShowHistory(false);
    private void ClearHistory_Click(object sender, RoutedEventArgs e) { _engine.ClearHistory(); FillHistory(); }
}
