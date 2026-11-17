using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Sumthing.Maths;
using Sumthing.Services;

namespace Sumthing.Views;

/// <summary>Statistics like a TI-84's STAT menu: list editor L1–L6, 1-Var and 2-Var Stats, regressions, scatter plots.</summary>
public partial class StatsView : UserControl
{
    public sealed class Row
    {
        public string Name { get; init; } = "";
        public string Value { get; init; } = "";
    }

    /// <summary>Plot rows on the graph: scatter "(L1, L2)" and maybe a fitted equation.</summary>
    public event Action<string[]> PlotRequested;

    private readonly TextBox[] _boxes = new TextBox[6];
    private readonly TextBlock[] _counts = new TextBlock[6];
    private readonly DispatcherTimer _sync = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private bool _loading;
    private RegressionResult _lastFit;
    private string _lastX, _lastY;

    public StatsView()
    {
        InitializeComponent();
        for (int i = 0; i < 6; i++)
        {
            var panel = new DockPanel { Margin = new Thickness(i == 0 ? 0 : 4, 0, i == 5 ? 0 : 4, 0) };
            var head = new DockPanel { Margin = new Thickness(2, 0, 2, 6) };
            var count = new TextBlock { FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center };
            count.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            DockPanel.SetDock(count, Dock.Right);
            head.Children.Add(count);
            head.Children.Add(new TextBlock { Text = $"L{i + 1}", FontWeight = FontWeights.SemiBold, FontSize = 14 });
            DockPanel.SetDock(head, Dock.Top);
            panel.Children.Add(head);
            var box = new TextBox
            {
                AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, VerticalContentAlignment = VerticalAlignment.Top,
                FontFamily = (FontFamily)FindResource("MonoFont"), FontSize = 13.5, Padding = new Thickness(8, 6, 8, 6),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, TextAlignment = TextAlignment.Right,
            };
            System.Windows.Automation.AutomationProperties.SetName(box, $"List L{i + 1}");
            box.TextChanged += (_, _) => { if (!_loading) { _sync.Stop(); _sync.Start(); } };
            panel.Children.Add(box);
            _boxes[i] = box;
            _counts[i] = count;
            ListGrid.Children.Add(panel);
        }
        _sync.Tick += (_, _) => { _sync.Stop(); SyncLists(); };
        var names = Enumerable.Range(1, 6).Select(i => $"L{i}").ToList();
        XList.ItemsSource = names; YList.ItemsSource = names.ToList();
        XList.SelectedIndex = 0; YList.SelectedIndex = 1;
        ModelBox.ItemsSource = new[]
        {
            "Linear   y = ax + b", "Quadratic   y = ax² + bx + c", "Cubic   y = ax³ + bx² + cx + d", "Quartic   y = ax⁴ + …",
            "Exponential   y = a·bˣ", "Logarithmic   y = a + b·ln x", "Power   y = a·xᵇ",
        };
        ModelBox.SelectedIndex = 0;
        LoadLists();
        MathState.Changed += () => { if (!_syncing) LoadLists(); };
    }

    private bool _syncing;

    public void LoadLists()
    {
        _loading = true;
        for (int i = 0; i < 6; i++)
        {
            var l = MathState.Context.Lists.TryGetValue($"L{i + 1}", out var v) ? v : Array.Empty<double>();
            if (!_boxes[i].IsKeyboardFocused)
                _boxes[i].Text = string.Join("\n", l.Select(x => x.ToString("0.##########", CultureInfo.InvariantCulture)));
            _counts[i].Text = l.Length == 0 ? "" : l.Length.ToString();
        }
        _loading = false;
    }

    private void SyncLists()
    {
        for (int i = 0; i < 6; i++)
        {
            var values = new List<double>();
            foreach (var part in _boxes[i].Text.Split(new[] { '\n', '\r', '\t', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string p = part.Trim().TrimEnd(',');
                if (p.Length == 0) continue;
                if (Engine.Num.TryParse(p, out var d)) values.Add((double)d);
                else
                    try { double v = Parser.Parse(p).Eval(MathState.Context); if (!double.IsNaN(v)) values.Add(v); }
                    catch (MathException) { }
            }
            MathState.Context.Lists[$"L{i + 1}"] = values.ToArray();
            _counts[i].Text = values.Count == 0 ? "" : values.Count.ToString();
        }
        _syncing = true;
        try { MathState.RaiseChanged(); }
        finally { _syncing = false; }
    }

    /// <summary>Sets a list (self-test, example data).</summary>
    public void SetList(int n, params double[] values)
    {
        MathState.Context.Lists[$"L{n}"] = values;
        LoadLists();
        MathState.RaiseChanged();
    }

    private void Example_Click(object sender, RoutedEventArgs e)
    {
        // hours studied vs test score
        SetList(1, 1, 2, 3, 4, 5, 6, 7, 8);
        SetList(2, 52, 55, 61, 64, 70, 73, 79, 84);
        ModeReg.IsChecked = true;
        Mode_Click(null, null);
    }

    private void ClearAll_Click(object sender, RoutedEventArgs e)
    {
        for (int i = 1; i <= 6; i++) MathState.Context.Lists[$"L{i}"] = Array.Empty<double>();
        LoadLists();
        MathState.RaiseChanged();
    }

    private void Mode_Click(object sender, RoutedEventArgs e)
    {
        bool two = Mode2.IsChecked == true || ModeReg.IsChecked == true;
        YPanel.Visibility = two ? Visibility.Visible : Visibility.Collapsed;
        ModelPanel.Visibility = ModeReg.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        XLabel.Text = two ? "X LIST" : "LIST";
        ResultRows.ItemsSource = null; ResultTitle.Text = ""; ResultEquation.Text = ""; ErrorText.Text = "";
        PlotButtons.Visibility = Visibility.Collapsed;
    }

    /// <summary>Chooses the calculation (0 = 1-Var, 1 = 2-Var, 2 = regression) and model (self-test).</summary>
    public void Choose(int mode, int model = 0)
    {
        Mode1.IsChecked = mode == 0; Mode2.IsChecked = mode == 1; ModeReg.IsChecked = mode == 2;
        Mode_Click(null, null);
        ModelBox.SelectedIndex = model;
    }

    public RegressionResult LastFit => _lastFit;
    public IReadOnlyList<Row> Results => (ResultRows.ItemsSource as IEnumerable<Row>)?.ToList() ?? new List<Row>();

    private static string F(double v) => double.IsNaN(v) ? "—" : Parser.Format(v, 10);

    public void Calculate_Click(object sender, RoutedEventArgs e)
    {
        _sync.Stop();
        SyncLists();
        ErrorText.Text = ""; ResultEquation.Text = ""; _lastFit = null;
        PlotButtons.Visibility = Visibility.Collapsed;
        string xn = (string)XList.SelectedItem ?? "L1", yn = (string)YList.SelectedItem ?? "L2";
        var x = MathState.Context.Lists.TryGetValue(xn, out var xs) ? xs : Array.Empty<double>();
        var y = MathState.Context.Lists.TryGetValue(yn, out var ys) ? ys : Array.Empty<double>();
        var rows = new List<Row>();
        try
        {
            if (Mode1.IsChecked == true)
            {
                var r = Stats.OneVar(x);
                ResultTitle.Text = $"1-VAR STATS  ({xn})";
                rows.AddRange(new[]
                {
                    new Row { Name = "x̄  (mean)", Value = F(r.Mean) }, new Row { Name = "Σx", Value = F(r.Sum) }, new Row { Name = "Σx²", Value = F(r.SumSq) },
                    new Row { Name = "Sx  (sample std. dev.)", Value = F(r.Sx) }, new Row { Name = "σx  (population std. dev.)", Value = F(r.SigmaX) },
                    new Row { Name = "n", Value = r.N.ToString() }, new Row { Name = "minX", Value = F(r.Min) }, new Row { Name = "Q₁", Value = F(r.Q1) },
                    new Row { Name = "Med", Value = F(r.Median) }, new Row { Name = "Q₃", Value = F(r.Q3) }, new Row { Name = "maxX", Value = F(r.Max) },
                });
            }
            else if (Mode2.IsChecked == true)
            {
                var r = Stats.TwoVar(x, y);
                ResultTitle.Text = $"2-VAR STATS  ({xn}, {yn})";
                rows.AddRange(new[]
                {
                    new Row { Name = "x̄", Value = F(r.MeanX) }, new Row { Name = "Σx", Value = F(r.SumX) }, new Row { Name = "Σx²", Value = F(r.SumX2) },
                    new Row { Name = "Sx", Value = F(r.Sx) }, new Row { Name = "σx", Value = F(r.SigmaX) },
                    new Row { Name = "ȳ", Value = F(r.MeanY) }, new Row { Name = "Σy", Value = F(r.SumY) }, new Row { Name = "Σy²", Value = F(r.SumY2) },
                    new Row { Name = "Sy", Value = F(r.Sy) }, new Row { Name = "σy", Value = F(r.SigmaY) },
                    new Row { Name = "Σxy", Value = F(r.SumXY) }, new Row { Name = "r", Value = F(r.R) }, new Row { Name = "n", Value = r.N.ToString() },
                });
                _lastX = xn; _lastY = yn;
                PlotText.Text = "Plot on Graphing";
                PlotButtons.Visibility = Visibility.Visible;
            }
            else
            {
                var model = (RegressionModel)System.Math.Max(0, ModelBox.SelectedIndex);
                var fit = Stats.Fit(model, x, y);
                _lastFit = fit; _lastX = xn; _lastY = yn;
                ResultTitle.Text = $"{model.ToString().ToUpperInvariant()} REGRESSION  ({xn}, {yn})";
                ResultEquation.Text = fit.Form;
                string[] letters = { "a", "b", "c", "d", "e" };
                for (int i = 0; i < fit.Coefficients.Length; i++) rows.Add(new Row { Name = letters[i], Value = F(fit.Coefficients[i]) });
                if (!double.IsNaN(fit.R)) rows.Add(new Row { Name = "r", Value = F(fit.R) });
                rows.Add(new Row { Name = "r²", Value = F(fit.R2) });
                PlotText.Text = "Plot data and curve on Graphing";
                PlotButtons.Visibility = Visibility.Visible;
            }
        }
        catch (MathException ex)
        {
            ResultTitle.Text = "";
            ErrorText.Text = ex.Message;
        }
        ResultRows.ItemsSource = rows;
    }

    private void Plot_Click(object sender, RoutedEventArgs e)
    {
        var rows = new List<string> { $"({_lastX}, {_lastY})" };
        if (_lastFit != null) rows.Add("y=" + _lastFit.Equation);
        PlotRequested?.Invoke(rows.ToArray());
    }
}
