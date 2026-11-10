using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Sumthing.Engine;
using Sumthing.Services;

namespace Sumthing.Views;

/// <summary>Type-a-sum: a notepad where every line is worked out as you type.</summary>
public partial class SumsView : UserControl
{
    public event Action<decimal, string> SendToCalculator;

    public sealed class AnswerRow
    {
        public string Text { get; init; } = "";
        public decimal? Value { get; init; }
        public string Tip { get; init; }
        public Brush Brush { get; init; }
        public FontWeight Weight { get; init; } = FontWeights.Normal;
        public Cursor Cursor => Value != null ? Cursors.Hand : Cursors.Arrow;
    }

    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private List<SheetResult> _results = new();
    private bool _loading;

    public SumsView()
    {
        InitializeComponent();
        _debounce.Tick += (_, _) => { _debounce.Stop(); Recalculate(); };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SettingsService.Current.SumSheet = Sheet.Text; SettingsService.Save(); };
        _loading = true;
        Sheet.Text = SettingsService.Current.SumSheet ?? "";
        _loading = false;
        ThemeManager.ThemeChanged += Recalculate;
        Recalculate();
    }

    public string SheetText => Sheet.Text;

    /// <summary>Replaces the sheet (used by the self-test).</summary>
    public void SetSheet(string text)
    {
        Sheet.Text = text;
        Recalculate();
    }

    public IReadOnlyList<SheetResult> Results => _results;

    public void OnShown()
    {
        Recalculate();
        Dispatcher.InvokeAsync(() => Sheet.Focus(), DispatcherPriority.Input);
    }

    private void Sheet_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        _debounce.Stop();
        _debounce.Start();
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void Recalculate()
    {
        var lines = Sheet.Text.Replace("\r\n", "\n").Split('\n');
        _results = SumSheet.Evaluate(lines);
        var normal = (Brush)FindResource("Text");
        var muted = (Brush)FindResource("TextSecondary");
        var accent = (Brush)FindResource("Accent");
        var danger = (Brush)FindResource("DangerText");
        var rows = new List<AnswerRow>();
        foreach (var r in _results)
        {
            if (r.Error != null)
                rows.Add(new AnswerRow { Text = "?", Tip = r.Error, Brush = danger });
            else if (r.Value is decimal v)
                rows.Add(new AnswerRow
                {
                    Text = r.Text,
                    Value = v,
                    Tip = r.Variable != null ? $"{r.Variable} = {r.Text} (click to copy)" : "Click to copy",
                    Brush = r.Variable != null ? muted : accent,
                    Weight = r.Variable != null ? FontWeights.Normal : FontWeights.SemiBold,
                });
            else
                rows.Add(new AnswerRow { Text = "", Brush = normal });
        }
        Answers.ItemsSource = rows;
        var last = _results.LastOrDefault(r => r.Value != null);
        LastAnswer.Text = last?.Text ?? "—";
        SendButton.IsEnabled = last != null;
        _lastValue = last?.Value;
    }

    private decimal? _lastValue;

    private void Answer_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is AnswerRow row && row.Value is decimal v)
            try { Clipboard.SetText(Num.Plain(v)); } catch { }
    }

    private void Send_Click(object sender, RoutedEventArgs e)
    {
        if (_lastValue is decimal v) SendToCalculator?.Invoke(v, "From Type-a-sum");
    }

    private void Help_Click(object sender, RoutedEventArgs e) =>
        HelpPanel.Visibility = HelpToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        Sheet.Clear();
        Recalculate();
        Sheet.Focus();
    }

    private void CopyAll_Click(object sender, RoutedEventArgs e)
    {
        var lines = Sheet.Text.Replace("\r\n", "\n").Split('\n');
        int width = Math.Min(48, lines.Max(l => l.Length));
        var sb = new StringBuilder();
        for (int i = 0; i < lines.Length; i++)
        {
            var ans = i < _results.Count ? _results[i] : null;
            if (ans?.Value != null) sb.AppendLine($"{lines[i].PadRight(width)}   = {ans.Text}");
            else sb.AppendLine(lines[i]);
        }
        try { Clipboard.SetText(sb.ToString()); } catch { }
    }
}
