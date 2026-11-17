using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using Sumthing.Maths;
using Sumthing.Services;

namespace Sumthing.Views;

/// <summary>The graphing calculator: TI-84 Y= / WINDOW / ZOOM / TRACE / TABLE / CALC, with TI-Nspire style
/// relations, sliders and points of interest.</summary>
public partial class GraphView : UserControl
{
    private static GraphModel Model => MathState.Graph;

    public sealed class RowVM : INotifyPropertyChanged
    {
        private readonly GraphView _owner;
        public GraphRow Row { get; }
        public int Index { get; set; }

        public RowVM(GraphView owner, GraphRow row, int index) { _owner = owner; Row = row; Index = index; }

        public string Label => $"f{Index + 1}";

        public string Text
        {
            get => Row.Text;
            set { if (Row.Text == value) return; Row.Text = value; _owner.RowsEdited(); }
        }

        public bool Enabled
        {
            get => Row.Enabled;
            set { Row.Enabled = value; OnChanged(); OnChanged(nameof(HiddenDot)); _owner.Redraw(); }
        }

        public Brush ColorBrush
        {
            get { var b = new SolidColorBrush(ThemeManager.CurveColor(Row.ColorIndex)); b.Freeze(); return b; }
        }

        public Visibility HiddenDot => Row.Enabled ? Visibility.Collapsed : Visibility.Visible;

        public string Hint { get; private set; } = "";
        public Brush HintBrush { get; private set; }
        public Visibility HintVisibility => string.IsNullOrEmpty(Hint) ? Visibility.Collapsed : Visibility.Visible;

        public void Update(CompiledRow c)
        {
            bool err = c?.Kind == PlotKind.Error;
            Hint = c == null ? "" : c.Kind switch
            {
                PlotKind.Error => c.Error,
                PlotKind.Inequality => "Inequality, shaded" + (c.IsStrict ? " (dashed edge: not included)" : ""),
                PlotKind.Parametric => "Parametric: t runs from Tmin to Tmax (Window tab)",
                PlotKind.Polar => "Polar: θ runs from θmin to θmax (Window tab)",
                PlotKind.Implicit => "Relation in x and y",
                PlotKind.ImplicitInequality => "Relation in x and y, shaded",
                PlotKind.Points => c.XExpr == null ? $"Scatter plot, {c.PointsX.Length} points" : "Points",
                PlotKind.Function when c.Alias != null => $"{c.Alias}(x): use it anywhere, like {c.Alias}(2)",
                _ => "",
            };
            HintBrush = (Brush)Application.Current.FindResource(err ? "Brush.Danger" : "Brush.TextMuted");
            OnChanged(nameof(Hint)); OnChanged(nameof(HintBrush)); OnChanged(nameof(HintVisibility));
        }

        public void RefreshColor() { OnChanged(nameof(ColorBrush)); OnChanged(nameof(Label)); }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public sealed class SliderVM : INotifyPropertyChanged
    {
        private readonly GraphView _owner;
        private readonly SliderInfo _info;
        private DispatcherTimer _timer;
        private int _dir = 1;
        public string Name { get; }

        public SliderVM(GraphView owner, string name, SliderInfo info) { _owner = owner; Name = name; _info = info; }

        public double Min { get => _info.Min; set { _info.Min = value; OnChanged(); } }
        public double Max { get => _info.Max; set { _info.Max = value; OnChanged(); } }
        public double Step => _info.Step;

        public double Value
        {
            get => _info.Value;
            set
            {
                double v = System.Math.Round(value / _info.Step) * _info.Step;
                v = System.Math.Round(v, 10);
                if (_info.Value == v) return;
                _info.Value = v;
                OnChanged(); OnChanged(nameof(ValueText));
                Model.PushSliders();
                _owner.Redraw();
            }
        }

        public string ValueText
        {
            get => Parser.Format(_info.Value, 8);
            set
            {
                try
                {
                    double v = Parser.Parse(value).Eval(MathState.Context);
                    if (double.IsNaN(v) || double.IsInfinity(v)) return;
                    if (v < Min) Min = System.Math.Floor(v);
                    if (v > Max) Max = System.Math.Ceiling(v);
                    _info.Value = v;
                    OnChanged(nameof(Value)); OnChanged(nameof(ValueText));
                    Model.PushSliders();
                    _owner.Redraw();
                }
                catch (MathException) { OnChanged(nameof(ValueText)); }
            }
        }

        public bool Playing
        {
            get => _timer?.IsEnabled == true;
            set
            {
                if (value)
                {
                    _timer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
                    _timer.Tick -= Tick;
                    _timer.Tick += Tick;
                    _timer.Start();
                }
                else _timer?.Stop();
                OnChanged();
            }
        }

        private void Tick(object s, EventArgs e)
        {
            double next = _info.Value + _dir * (Max - Min) / 120;
            if (next > Max) { next = Max; _dir = -1; }
            if (next < Min) { next = Min; _dir = 1; }
            _info.Value = System.Math.Round(next, 6);
            OnChanged(nameof(Value)); OnChanged(nameof(ValueText));
            Model.PushSliders();
            _owner.Redraw(quick: true);
        }

        public void Stop() => _timer?.Stop();

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public sealed class ResultVM
    {
        public string Title { get; init; } = "";
        public string Value { get; init; } = "";
    }

    private sealed class Choice
    {
        public int Index;
        public string Text = "";
        public override string ToString() => Text;
    }

    private readonly List<RowVM> _rows = new();
    private readonly List<SliderVM> _sliders = new();
    private readonly List<ResultVM> _results = new();
    private readonly DispatcherTimer _recompile = new() { Interval = TimeSpan.FromMilliseconds(220) };
    private TextBox _lastBox;
    private readonly Dictionary<string, TextBox> _windowBoxes = new();

    public GraphView()
    {
        InitializeComponent();
        Plot.Model = Model;
        _recompile.Tick += (_, _) => { _recompile.Stop(); Recompile(); };
        Plot.ViewChanged += () => { FillWindowBoxes(); if (TabTable.IsChecked == true) FillTable(); };
        Plot.TraceChanged += t => StatusLine.Text = t;
        Plot.PointerMoved += (x, y) => { if (!Plot.TraceOn) StatusLine.Text = $"x = {Parser.Format(x, 5)}    y = {Parser.Format(y, 5)}"; };
        MathState.Changed += () => { Recompile(); UpdateAngle(); };
        ThemeManager.ThemeChanged += () => { foreach (var r in _rows) { r.RefreshColor(); r.Update(Model.Compiled.ElementAtOrDefault(r.Index)); } Plot.Refresh(); };

        BuildPalette();
        BuildWindowBoxes();
        RebuildRows();
        Recompile();
        UpdateAngle();
        Loaded += (_, _) => Plot.Refresh();
        IsVisibleChanged += (_, _) => { if (!IsVisible) foreach (var s in _sliders) s.Stop(); };
    }

    // ---------------------------------------------------------------- rows

    private void RebuildRows()
    {
        _rows.Clear();
        for (int i = 0; i < Model.Rows.Count; i++) _rows.Add(new RowVM(this, Model.Rows[i], i));
        RowList.ItemsSource = null;
        RowList.ItemsSource = _rows;
    }

    internal void RowsEdited()
    {
        _recompile.Stop();
        _recompile.Start();
    }

    /// <summary>Re-reads every row (after typing stops) and refreshes everything that depends on it.</summary>
    public void Recompile()
    {
        Model.Recompile();
        foreach (var r in _rows) r.Update(Model.Compiled.ElementAtOrDefault(r.Index));
        RebuildSliders();
        FillChoices();
        if (TabTable.IsChecked == true) FillTable();
        Plot.Refresh();
        if (Plot.TraceOn) StatusLine.Text = Plot.TraceText();
    }

    internal void Redraw(bool quick = false)
    {
        Model.Touch();
        if (quick) Plot.InvalidateVisual(); else Plot.Refresh();
        if (TabTable.IsChecked == true) FillTable();
        if (Plot.TraceOn) StatusLine.Text = Plot.TraceText();
    }

    private void AddRow_Click(object sender, RoutedEventArgs e) => AddRow("");

    /// <summary>Adds a row (also used by Statistics to plot data and a fitted curve).</summary>
    public void AddRow(string text)
    {
        if (Model.Rows.Count >= 30) return;
        var used = Model.Rows.Select(r => r.ColorIndex).ToHashSet();
        int color = Enumerable.Range(0, 10).FirstOrDefault(i => !used.Contains(i));
        if (used.Count >= 10) color = Model.Rows.Count % 10;
        Model.Rows.Add(new GraphRow { Text = text, ColorIndex = color });
        RebuildRows();
        Recompile();
        Dispatcher.InvokeAsync(() =>
        {
            var container = RowList.ItemContainerGenerator.ContainerFromIndex(_rows.Count - 1) as FrameworkElement;
            var box = container == null ? null : FindChild<TextBox>(container);
            box?.Focus();
        }, DispatcherPriority.Loaded);
    }

    /// <summary>Replaces every row (self-test and "Plot on Graphing").</summary>
    public void SetRows(params string[] texts)
    {
        Model.Rows.Clear();
        for (int i = 0; i < texts.Length; i++) Model.Rows.Add(new GraphRow { Text = texts[i], ColorIndex = i % 10 });
        Plot.Marks.Clear(); Plot.Tangent = null; Plot.Integral = null;
        _results.Clear(); ShowResults();
        RebuildRows();
        Recompile();
    }

    private void DeleteRow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not RowVM vm) return;
        Model.Rows.Remove(vm.Row);
        if (Model.Rows.Count == 0) Model.Rows.Add(new GraphRow());
        Plot.Marks.Clear(); Plot.Tangent = null; Plot.Integral = null;
        RebuildRows();
        Recompile();
    }

    private void RowBox_GotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _lastBox = sender as TextBox;
        if (_lastBox?.Tag is RowVM vm && Model.Compiled.ElementAtOrDefault(vm.Index) is { } c && (c.IsFunction || c.Kind is PlotKind.Polar or PlotKind.Parametric))
        {
            Plot.TraceRow = vm.Index;
            if (Plot.TraceOn) { StatusLine.Text = Plot.TraceText(); Plot.InvalidateVisual(); }
        }
    }

    private void RowBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox box || box.Tag is not RowVM vm) return;
        _recompile.Stop();
        Recompile();
        // Enter on the last row adds a new one, like the Y= editor moving down
        if (vm.Index == _rows.Count - 1 && !string.IsNullOrWhiteSpace(vm.Text)) AddRow("");
        else if (vm.Index + 1 < _rows.Count)
        {
            var next = RowList.ItemContainerGenerator.ContainerFromIndex(vm.Index + 1) as FrameworkElement;
            if (next != null) FindChild<TextBox>(next)?.Focus();
        }
        e.Handled = true;
    }

    private static readonly string[] ColorNames = { "Blue", "Red", "Black", "Magenta", "Green", "Orange", "Brown", "Navy", "Light blue", "Yellow" };

    private void RowColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not RowVM vm) return;
        var menu = new ContextMenu { PlacementTarget = b, Placement = PlacementMode.Bottom };
        for (int i = 0; i < 10; i++)
        {
            int ci = i;
            var dot = new Ellipse { Width = 12, Height = 12, Fill = new SolidColorBrush(ThemeManager.CurveColor(i)), Margin = new Thickness(0, 0, 10, 0) };
            var header = new StackPanel { Orientation = Orientation.Horizontal };
            header.Children.Add(dot);
            header.Children.Add(new TextBlock { Text = ColorNames[i] + (vm.Row.ColorIndex == i ? "  ✓" : "") });
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => { vm.Row.ColorIndex = ci; vm.RefreshColor(); Redraw(); };
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        foreach (LineStyle st in Enum.GetValues(typeof(LineStyle)))
        {
            var style = st;
            var item = new MenuItem { Header = $"{st} line" + (vm.Row.Style == st ? "  ✓" : "") };
            item.Click += (_, _) => { vm.Row.Style = style; Redraw(); };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    // ---------------------------------------------------------------- palette

    private static readonly (string Label, string Insert, string Tip)[] PaletteKeys =
    {
        ("x²", "²", "Squared"), ("xⁿ", "^", "Power"), ("√", "√(", "Square root"), ("π", "π", "Pi"),
        ("θ", "θ", "Theta, for polar: r = 2sin(3θ)"), ("|x|", "abs(", "Absolute value"), ("≤", "≤", "Less than or equal (shades)"),
        ("≥", "≥", "Greater than or equal (shades)"), ("sin", "sin(", "Sine"), ("cos", "cos(", "Cosine"), ("tan", "tan(", "Tangent"),
        ("ln", "ln(", "Natural log"), ("log", "log(", "Log base 10"), ("eˣ", "e^(", "e to the power"),
        ("( , )", "(cos(t), sin(t))", "Parametric curve or a point"), ("r =", "r = ", "Polar curve"),
    };

    private void BuildPalette()
    {
        foreach (var (label, insert, tip) in PaletteKeys)
        {
            var b = new Button
            {
                Content = label, ToolTip = tip, Style = (Style)FindResource("GhostButton"),
                Padding = new Thickness(8, 0, 8, 0), Height = 30, MinHeight = 0, MinWidth = 36, Margin = new Thickness(0, 0, 5, 5),
                FontFamily = new FontFamily("Cambria Math, Segoe UI"), FontSize = 13, Focusable = false,
            };
            b.Click += (_, _) => Insert(insert);
            Palette.Children.Add(b);
        }
    }

    private void Insert(string text)
    {
        var box = _lastBox;
        if (box == null || !box.IsVisible)
        {
            var c = RowList.ItemContainerGenerator.ContainerFromIndex(0) as FrameworkElement;
            box = c == null ? null : FindChild<TextBox>(c);
        }
        if (box == null) return;
        int at = box.CaretIndex;
        if (text.StartsWith("(cos") || text.StartsWith("r ="))
        {
            if (box.Text.Trim().Length > 0) return;
            box.Text = text;
            box.CaretIndex = box.Text.Length;
        }
        else
        {
            box.Text = box.Text.Insert(at, text);
            box.CaretIndex = at + text.Length;
        }
        box.Focus();
    }

    // ---------------------------------------------------------------- sliders

    private void RebuildSliders()
    {
        var names = Model.Sliders.Keys.OrderBy(n => n, StringComparer.Ordinal).ToList();
        if (names.SequenceEqual(_sliders.Select(s => s.Name))) return;
        foreach (var s in _sliders) s.Stop();
        _sliders.Clear();
        foreach (var n in names) _sliders.Add(new SliderVM(this, n, Model.Sliders[n]));
        SliderList.ItemsSource = null;
        SliderList.ItemsSource = _sliders;
        SliderCard.Visibility = _sliders.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SliderBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox t) t.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
    }

    public IReadOnlyList<SliderVM> Sliders => _sliders;

    // ---------------------------------------------------------------- toolbar

    private void Zoom_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = ZoomButton, Placement = PlacementMode.Bottom };
        void Add(string text, Action a)
        {
            var mi = new MenuItem { Header = text };
            mi.Click += (_, _) => { a(); AfterZoom(); };
            menu.Items.Add(mi);
        }
        var win = Model.Window;
        Add("Zoom in", () => Model.Zoom(0.5, (win.XMin + win.XMax) / 2, (win.YMin + win.YMax) / 2));
        Add("Zoom out", () => Model.Zoom(2, (win.XMin + win.XMax) / 2, (win.YMin + win.YMax) / 2));
        menu.Items.Add(new Separator());
        Add("Standard  (−10 to 10)", Model.ZoomStandard);
        Add("Square  (equal units)", () => Model.ZoomSquare(Plot.ActualWidth, Plot.ActualHeight));
        Add("Decimal  (0.1 per step)", Model.ZoomDecimal);
        Add("Integer", Model.ZoomInteger);
        Add("Trig  (multiples of π/2)", Model.ZoomTrig);
        Add("Fit  (fit y to the functions)", Model.ZoomFit);
        Add("Data  (fit the scatter plots)", Model.ZoomData);
        menu.IsOpen = true;
    }

    /// <summary>Applies a zoom preset by name (self-test).</summary>
    public void ZoomPreset(string name)
    {
        switch (name)
        {
            case "Standard": Model.ZoomStandard(); break;
            case "Trig": Model.ZoomTrig(); break;
            case "Decimal": Model.ZoomDecimal(); break;
            case "Fit": Model.ZoomFit(); break;
            case "Data": Model.ZoomData(); break;
            case "Square": Model.ZoomSquare(Plot.ActualWidth, Plot.ActualHeight); break;
        }
        AfterZoom();
    }

    private void AfterZoom()
    {
        FillWindowBoxes();
        if (TabTable.IsChecked == true) FillTable();
        Plot.Refresh();
    }

    private void Trace_Click(object sender, RoutedEventArgs e) => SetTrace(TraceToggle.IsChecked == true);

    public void SetTrace(bool on)
    {
        TraceToggle.IsChecked = on;
        Plot.TraceOn = on;
        if (on)
        {
            var win = Model.Window;
            Plot.TraceValue = Analysis.Snap((win.XMin + win.XMax) / 2);
            StatusLine.Text = Plot.TraceText();
            Plot.Focus();
        }
        else StatusLine.Text = "Drag to move · scroll to zoom · Shift+drag to zoom to a box";
        Plot.InvalidateVisual();
    }

    private void Poi_Click(object sender, RoutedEventArgs e)
    {
        Plot.ShowPointsOfInterest = PoiToggle.IsChecked == true;
        Plot.RefreshPointsOfInterest();
    }

    private void Grid_Click(object sender, RoutedEventArgs e)
    {
        Plot.ShowGrid = GridToggle.IsChecked == true;
        Plot.InvalidateVisual();
    }

    private void Side_Click(object sender, RoutedEventArgs e)
    {
        bool show = SideToggle.IsChecked == true;
        SidePanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        SideColumn.Width = show ? new GridLength(300) : new GridLength(0);
    }

    private void CopyImage_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetImage(Plot.Snapshot()); (Window.GetWindow(this) as MainWindow)?.ShowToast("Graph copied as a picture"); } catch { }
    }

    private void SaveImage_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { Title = "Save graph", Filter = "PNG picture (*.png)|*.png", FileName = $"Sumthing graph {DateTime.Now:yyyy-MM-dd}.png" };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(Plot.Snapshot()));
            using var fs = File.Create(dlg.FileName);
            enc.Save(fs);
            (Window.GetWindow(this) as MainWindow)?.ShowToast("Graph saved");
        }
        catch (Exception ex) { (Window.GetWindow(this) as MainWindow)?.ShowToast("Couldn't save: " + ex.Message); }
    }

    // ---------------------------------------------------------------- side panel

    private void SideTab_Click(object sender, RoutedEventArgs e) => ShowSideTab(TabTable.IsChecked == true ? 1 : TabWindow.IsChecked == true ? 2 : 0);

    public void ShowSideTab(int tab)
    {
        TabAnalyze.IsChecked = tab == 0; TabTable.IsChecked = tab == 1; TabWindow.IsChecked = tab == 2;
        AnalyzePanel.Visibility = tab == 0 ? Visibility.Visible : Visibility.Collapsed;
        TablePanel.Visibility = tab == 1 ? Visibility.Visible : Visibility.Collapsed;
        WindowPanel.Visibility = tab == 2 ? Visibility.Visible : Visibility.Collapsed;
        if (tab == 1) FillTable();
        if (tab == 2) FillWindowBoxes();
    }

    private void FillChoices()
    {
        var choices = Model.Compiled.Where(c => c.IsFunction && c.Row.Enabled)
            .Select(c => new Choice { Index = c.Index, Text = $"{c.Alias ?? c.Name}:  {Short(c.Row.Text)}" }).ToList();
        int keep1 = (AnalyzeRow.SelectedItem as Choice)?.Index ?? -1, keep2 = (AnalyzeRow2.SelectedItem as Choice)?.Index ?? -1;
        AnalyzeRow.ItemsSource = choices;
        AnalyzeRow2.ItemsSource = choices.ToList();
        AnalyzeRow.SelectedItem = choices.FirstOrDefault(c => c.Index == keep1) ?? choices.FirstOrDefault();
        AnalyzeRow2.SelectedItem = choices.FirstOrDefault(c => c.Index == keep2 && c.Index != (AnalyzeRow.SelectedItem as Choice)?.Index)
                                   ?? choices.FirstOrDefault(c => c != AnalyzeRow.SelectedItem);
    }

    private static string Short(string s) => s.Length > 26 ? s[..25] + "…" : s;

    private CompiledRow Selected(ComboBox box)
    {
        if (box.SelectedItem is Choice ch) return Model.Compiled.ElementAtOrDefault(ch.Index);
        return null;
    }

    /// <summary>Picks the function to analyse by row number (self-test).</summary>
    public void SelectAnalyzeRows(int first, int second)
    {
        AnalyzeRow.SelectedItem = (AnalyzeRow.ItemsSource as IEnumerable<Choice>)?.FirstOrDefault(c => c.Index == first);
        AnalyzeRow2.SelectedItem = (AnalyzeRow2.ItemsSource as IEnumerable<Choice>)?.FirstOrDefault(c => c.Index == second);
    }

    public IReadOnlyList<ResultVM> Results => _results;
    public IReadOnlyList<GraphMark> Marks => Plot.Marks;

    private void AddResult(string title, string value) => _results.Insert(0, new ResultVM { Title = title, Value = value });

    private void ShowResults()
    {
        ResultList.ItemsSource = null;
        ResultList.ItemsSource = _results.Take(40).ToList();
        NoResults.Visibility = _results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Plot.InvalidateVisual();
    }

    private static string F(double v) => Parser.Format(Analysis.Snap(v), 10);

    private bool NeedRow(out CompiledRow c)
    {
        c = Selected(AnalyzeRow);
        if (c == null) { AddResult("Analyze", "Add a function of x first (like x² − 4)."); ShowResults(); return false; }
        return true;
    }

    private void Mark(double x, double y, string label, CompiledRow c) =>
        Plot.Marks.Add(new GraphMark { X = x, Y = y, Label = label, ColorIndex = c.Row.ColorIndex });

    public void Zeros_Click(object sender, RoutedEventArgs e)
    {
        if (!NeedRow(out var c)) return;
        var w = Model.Window;
        var zs = Analysis.Zeros(Model.AsFunction(c), w.XMin, w.XMax);
        string name = c.Alias ?? c.Name;
        if (zs.Count == 0) AddResult($"Zeros of {name}", $"None between x = {F(w.XMin)} and {F(w.XMax)}");
        else
        {
            AddResult($"Zeros of {name}", string.Join(",  ", zs.Select(z => $"x = {F(z)}")));
            foreach (var z in zs) Mark(z, 0, $"Zero  x = {F(z)}", c);
        }
        ShowResults();
    }

    private void YIntercept_Click(object sender, RoutedEventArgs e)
    {
        if (!NeedRow(out var c)) return;
        double y = Model.EvalFunction(c, 0);
        string name = c.Alias ?? c.Name;
        if (double.IsNaN(y) || double.IsInfinity(y)) AddResult($"y-intercept of {name}", "None (no value at x = 0)");
        else { AddResult($"y-intercept of {name}", $"(0, {F(y)})"); Mark(0, y, $"(0, {F(y)})", c); }
        ShowResults();
    }

    public void Minimum_Click(object sender, RoutedEventArgs e) => Extrema("Minimum");
    public void Maximum_Click(object sender, RoutedEventArgs e) => Extrema("Maximum");

    private void Extrema(string kind)
    {
        if (!NeedRow(out var c)) return;
        var w = Model.Window;
        var pts = Analysis.Extrema(Model.AsFunction(c), w.XMin, w.XMax).Where(p => p.Kind == kind).ToList();
        string name = c.Alias ?? c.Name;
        if (pts.Count == 0) AddResult($"{kind} of {name}", $"None between x = {F(w.XMin)} and {F(w.XMax)}");
        else
        {
            AddResult($"{kind} of {name}", string.Join(",  ", pts.Select(p => $"({F(p.X)}, {F(p.Y)})")));
            foreach (var p in pts) Mark(p.X, p.Y, $"{kind}  ({F(p.X)}, {F(p.Y)})", c);
        }
        ShowResults();
    }

    public void Intersect_Click(object sender, RoutedEventArgs e)
    {
        if (!NeedRow(out var c)) return;
        var d = Selected(AnalyzeRow2);
        if (d == null || d.Index == c.Index) { AddResult("Intersection", "Pick a second, different function."); ShowResults(); return; }
        var w = Model.Window;
        var pts = Analysis.Intersections(Model.AsFunction(c), Model.AsFunction(d), w.XMin, w.XMax);
        string title = $"{c.Alias ?? c.Name} and {d.Alias ?? d.Name} meet";
        if (pts.Count == 0) AddResult(title, "Nowhere in this window");
        else
        {
            AddResult(title, string.Join(",  ", pts.Select(p => $"({F(p.X)}, {F(p.Y)})")));
            foreach (var p in pts) Mark(p.X, p.Y, $"Intersection  ({F(p.X)}, {F(p.Y)})", c);
        }
        ShowResults();
    }

    private bool ReadNumber(TextBox box, out double v)
    {
        v = double.NaN;
        try { v = Parser.Parse(box.Text, MathState.Context.Vars.Keys).Eval(MathState.Context); }
        catch (MathException ex) { AddResult("Can't read that number", ex.Message); ShowResults(); return false; }
        if (double.IsNaN(v) || double.IsInfinity(v)) { AddResult("Can't read that number", box.Text); ShowResults(); return false; }
        return true;
    }

    public void Value_Click(object sender, RoutedEventArgs e)
    {
        if (!NeedRow(out var c) || !ReadNumber(AtX, out double x)) return;
        double y = Model.EvalFunction(c, x);
        string name = c.Alias ?? c.Name;
        if (double.IsNaN(y) || double.IsInfinity(y)) AddResult($"{name}({F(x)})", "No value there");
        else { AddResult($"{name}({F(x)})", F(y)); Mark(x, y, $"({F(x)}, {F(y)})", c); }
        ShowResults();
    }

    public void Derivative_Click(object sender, RoutedEventArgs e)
    {
        if (!NeedRow(out var c) || !ReadNumber(AtX, out double x)) return;
        var f = Model.AsFunction(c);
        double y = f(x), m = Analysis.Derivative(f, x);
        string name = c.Alias ?? c.Name;
        if (double.IsNaN(m)) { AddResult($"dy/dx of {name} at x = {F(x)}", "No slope there"); ShowResults(); return; }
        m = Analysis.Snap(m);
        double b = Analysis.Snap(y - m * x);
        AddResult($"dy/dx of {name} at x = {F(x)}", $"{F(m)}     tangent: y = {F(m)}x {(b < 0 ? "−" : "+")} {F(System.Math.Abs(b))}");
        Plot.Tangent = (c.Index, x);
        Mark(x, y, $"dy/dx = {F(m)}", c);
        ShowResults();
    }

    public void Integral_Click(object sender, RoutedEventArgs e)
    {
        if (!NeedRow(out var c) || !ReadNumber(IntA, out double a) || !ReadNumber(IntB, out double b)) return;
        double v = Analysis.Integrate(Model.AsFunction(c), a, b);
        string name = c.Alias ?? c.Name;
        AddResult($"∫ {name}(x) dx from {F(a)} to {F(b)}", double.IsNaN(v) ? "Doesn't exist over that interval" : F(v));
        Plot.Integral = (c.Index, a, b);
        ShowResults();
    }

    private void ClearMarks_Click(object sender, RoutedEventArgs e)
    {
        _results.Clear();
        Plot.Marks.Clear();
        Plot.Tangent = null;
        Plot.Integral = null;
        ShowResults();
    }

    /// <summary>Sets the analysis inputs (self-test).</summary>
    public void SetAnalysisInputs(string atX, string a, string b) { AtX.Text = atX; IntA.Text = a; IntB.Text = b; }

    // ---------------------------------------------------------------- table

    private void Table_Changed(object sender, TextChangedEventArgs e) { if (IsLoaded) FillTable(); }

    public List<string[]> TableData { get; private set; } = new();

    public void FillTable()
    {
        var rows = Model.Compiled.Where(c => c.IsFunction && c.Row.Enabled).Take(5).ToList();
        var header = new List<string> { "x" };
        header.AddRange(rows.Select(r => r.Alias ?? r.Name));
        TableHeader.ItemsSource = header;
        double start, step;
        try
        {
            start = Parser.Parse(TblStart.Text).Eval(MathState.Context);
            step = Parser.Parse(TblStep.Text).Eval(MathState.Context);
        }
        catch (MathException) { TableRows.ItemsSource = null; return; }
        if (double.IsNaN(start) || double.IsNaN(step) || step == 0) { TableRows.ItemsSource = null; return; }
        var data = Model.Table(start, step, 60, rows);
        TableData = data.Select(line => line.Select((v, i) => i == 0 ? Parser.Format(v, 7) : double.IsNaN(v) || double.IsInfinity(v) ? "—" : Parser.Format(Analysis.Snap(v), 7)).ToArray()).ToList();
        TableRows.ItemsSource = TableData;
    }

    // ---------------------------------------------------------------- window

    private static readonly (string Key, string Label)[] WindowFields =
    {
        ("XMin", "Xmin"), ("XMax", "Xmax"), ("XScl", "Xscl"), ("YMin", "Ymin"), ("YMax", "Ymax"), ("YScl", "Yscl"),
        ("TMin", "Tmin"), ("TMax", "Tmax"), ("ThetaMin", "θmin"), ("ThetaMax", "θmax"),
    };

    private void BuildWindowBoxes()
    {
        int row = 0;
        foreach (var (key, label) in WindowFields)
        {
            WindowGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var t = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Cambria Math, Segoe UI"), FontSize = 14 };
            var box = new TextBox { Margin = new Thickness(0, 3, 0, 3), Tag = key };
            System.Windows.Automation.AutomationProperties.SetName(box, label);
            box.LostKeyboardFocus += (_, _) => ApplyWindowBox(box);
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) ApplyWindowBox(box); };
            Grid.SetRow(t, row); Grid.SetRow(box, row); Grid.SetColumn(box, 1);
            WindowGrid.Children.Add(t); WindowGrid.Children.Add(box);
            _windowBoxes[key] = box;
            row++;
        }
        FillWindowBoxes();
    }

    private void FillWindowBoxes()
    {
        var w = Model.Window;
        foreach (var (key, _) in WindowFields)
        {
            var box = _windowBoxes[key];
            if (box.IsKeyboardFocused) continue;
            double v = (double)typeof(GraphWindow).GetProperty(key)!.GetValue(w)!;
            box.Text = Parser.Format(Analysis.Snap(v), 8).Replace(",", "");
        }
    }

    private void ApplyWindowBox(TextBox box)
    {
        string key = (string)box.Tag;
        var w = Model.Window;
        var prop = typeof(GraphWindow).GetProperty(key)!;
        try
        {
            double v = Parser.Parse(box.Text).Eval(MathState.Context);
            if (double.IsNaN(v) || double.IsInfinity(v)) throw new MathException("bad");
            var copy = w.Copy();
            prop.SetValue(copy, v);
            if (copy.XMax <= copy.XMin || copy.YMax <= copy.YMin || copy.TMax <= copy.TMin || copy.ThetaMax <= copy.ThetaMin || copy.XScl < 0 || copy.YScl < 0)
                throw new MathException("bad");
            prop.SetValue(w, v);
            Model.Touch();
            Plot.Refresh();
            if (TabTable.IsChecked == true) FillTable();
        }
        catch (MathException) { }
        box.Text = Parser.Format(Analysis.Snap((double)prop.GetValue(w)!), 8).Replace(",", "");
    }

    private void Angle_Click(object sender, RoutedEventArgs e) => MathState.Degrees = DegreesRadio.IsChecked == true;

    private void UpdateAngle()
    {
        RadiansRadio.IsChecked = !MathState.Degrees;
        DegreesRadio.IsChecked = MathState.Degrees;
        AngleText.Text = MathState.Degrees ? "Degrees" : "Radians";
    }

    // ---------------------------------------------------------------- helpers

    public GraphCanvas GraphCanvas => Plot;

    private static T FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t) return t;
            var found = FindChild<T>(child);
            if (found != null) return found;
        }
        return null;
    }
}
