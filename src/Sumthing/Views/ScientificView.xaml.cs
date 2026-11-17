using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Sumthing.Maths;
using Sumthing.Services;

namespace Sumthing.Views;

/// <summary>The scientific "home screen", like a TI-84: type a line, press Enter, the answer appears on the right.</summary>
public partial class ScientificView : UserControl
{
    private static CalcSession Session => MathState.Session;
    private int _recall = -1;

    // label, what it types, style, tooltip
    private static readonly (string Label, string Insert, string Style, string Tip)[] Keys =
    {
        ("sin", "sin(", "SciFn", "Sine"), ("cos", "cos(", "SciFn", "Cosine"), ("tan", "tan(", "SciFn", "Tangent"),
        ("sin⁻¹", "sin⁻¹(", "SciFn", "Inverse sine"), ("cos⁻¹", "cos⁻¹(", "SciFn", "Inverse cosine"), ("tan⁻¹", "tan⁻¹(", "SciFn", "Inverse tangent"),
        ("x²", "²", "SciFn", "Square"), ("xⁿ", "^", "SciFn", "Power"), ("√", "√(", "SciFn", "Square root"),
        ("ⁿ√", "root(", "SciFn", "nth root: root(x, n)"), ("x⁻¹", "⁻¹", "SciFn", "Reciprocal"), ("!", "!", "SciFn", "Factorial"),
        ("ln", "ln(", "SciFn", "Natural log"), ("log", "log(", "SciFn", "Log base 10 (log(x, b) for base b)"), ("eˣ", "e^(", "SciFn", "e to the power"),
        ("10ˣ", "10^(", "SciFn", "10 to the power"), ("π", "π", "SciFn", "Pi"), ("e", "e", "SciFn", "e = 2.718…"),
        ("(", "(", "SciFn", null), (")", ")", "SciFn", null), (",", ",", "SciFn", "Separates values: log(8, 2)"),
        ("|x|", "abs(", "SciFn", "Absolute value"), ("nCr", " nCr ", "SciFn", "Combinations: 7 nCr 3"), ("nPr", " nPr ", "SciFn", "Permutations: 7 nPr 3"),
        ("7", "7", "SciNum", null), ("8", "8", "SciNum", null), ("9", "9", "SciNum", null),
        ("÷", "÷", "SciOp", "Divide"), ("Ans", "Ans", "SciFn", "The last answer"), ("►Frac", "►Frac", "SciFn", "Show the answer as a fraction"),
        ("4", "4", "SciNum", null), ("5", "5", "SciNum", null), ("6", "6", "SciNum", null),
        ("×", "×", "SciOp", "Multiply"), ("STO→", "→", "SciFn", "Store: 5→A"), ("°", "°", "SciFn", "Degrees mark: sin(30°) works in radian mode too"),
        ("1", "1", "SciNum", null), ("2", "2", "SciNum", null), ("3", "3", "SciNum", null),
        ("−", "-", "SciOp", "Subtract"), ("⌫", "<back>", "SciFn", "Delete"), ("CLEAR", "<clear>", "SciFn", "Clear the line (press again to clear the screen)"),
        ("0", "0", "SciNum", null), (".", ".", "SciNum", null), ("(−)", "−", "SciNum", "Negative"),
        ("+", "+", "SciOp", "Add"), ("x", "x", "SciFn", "x, for nDeriv, fnInt and sum"), ("ENTER", "<enter>", "SciEq", "Work it out (Enter)"),
    };

    public ScientificView()
    {
        InitializeComponent();
        foreach (var (label, insert, style, tip) in Keys)
        {
            var k = new CalcKey { Content = label, Tag = insert, Style = (Style)FindResource(style) };
            if (tip != null) k.ToolTip = tip;
            System.Windows.Automation.AutomationProperties.SetName(k, tip ?? label);
            k.Click += Key_Click;
            Pad.Children.Add(k);
        }
        MathState.Changed += () => { UpdateMode(); ShowVars(); };
        Refresh();
        UpdateMode();
        ShowVars();
    }

    public void OnShown() => Dispatcher.InvokeAsync(() => { Entry.Focus(); Entry.CaretIndex = Entry.Text.Length; }, DispatcherPriority.Input);

    public void Refresh()
    {
        Screen.ItemsSource = null;
        Screen.ItemsSource = Session.Entries.ToList();
        EmptyHint.Visibility = Session.Entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (Session.Entries.Count > 0) Screen.ScrollIntoView(Screen.Items[^1]);
    }

    /// <summary>Works out a line (also used by the self-test).</summary>
    public HomeEntry Run(string text)
    {
        var e = Session.Evaluate(text);
        Entry.Clear();
        _recall = -1;
        Refresh();
        if (e != null && (text.Contains('→') || text.Contains("->")))
        {
            MathState.RaiseChanged();
        }
        return e;
    }

    private void Enter_Click(object sender, RoutedEventArgs e) { Run(Entry.Text); Entry.Focus(); }

    private void Key_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string insert) return;
        switch (insert)
        {
            case "<enter>": Run(Entry.Text); break;
            case "<back>":
                if (Entry.SelectionLength > 0) Entry.SelectedText = "";
                else if (Entry.CaretIndex > 0)
                {
                    int at = Entry.CaretIndex;
                    Entry.Text = Entry.Text.Remove(at - 1, 1);
                    Entry.CaretIndex = at - 1;
                }
                break;
            case "<clear>":
                if (Entry.Text.Length == 0) { Session.Entries.Clear(); Refresh(); }
                else Entry.Clear();
                break;
            default: InsertText(insert); break;
        }
        Entry.Focus();
    }

    public void InsertText(string text)
    {
        // an operator on an empty line carries on from Ans, like a TI
        int at = Entry.CaretIndex;
        if (Entry.SelectionLength > 0) { Entry.SelectedText = text; Entry.CaretIndex = Entry.SelectionStart + text.Length; Entry.SelectionLength = 0; return; }
        Entry.Text = Entry.Text.Insert(at, text);
        Entry.CaretIndex = at + text.Length;
    }

    private void Entry_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter: Run(Entry.Text); e.Handled = true; break;
            case Key.Escape: Entry.Clear(); e.Handled = true; break;
            case Key.Up:
            {
                var inputs = Session.Entries.Select(x => x.Input).ToList();
                if (inputs.Count == 0) break;
                _recall = _recall < 0 ? inputs.Count - 1 : System.Math.Max(0, _recall - 1);
                Entry.Text = inputs[_recall];
                Entry.CaretIndex = Entry.Text.Length;
                e.Handled = true;
                break;
            }
            case Key.Down:
            {
                var inputs = Session.Entries.Select(x => x.Input).ToList();
                if (_recall < 0) break;
                _recall++;
                if (_recall >= inputs.Count) { _recall = -1; Entry.Clear(); }
                else { Entry.Text = inputs[_recall]; Entry.CaretIndex = Entry.Text.Length; }
                e.Handled = true;
                break;
            }
        }
    }

    private void Screen_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Screen.SelectedItem is not HomeEntry h) return;
        // clicking a line puts its answer (or its expression, for errors) into the entry line
        InsertText(h.Value is double ? h.Output.Replace(",", "").Replace("−", "-") : h.Input);
        Screen.SelectedItem = null;
        Entry.Focus();
    }

    private void ClearScreen_Click(object sender, RoutedEventArgs e) { Session.Entries.Clear(); Refresh(); Entry.Focus(); }

    private void Deg_Click(object sender, RoutedEventArgs e) => MathState.Degrees = DegToggle.IsChecked == true;

    private void UpdateMode()
    {
        DegToggle.IsChecked = MathState.Degrees;
        DegText.Text = MathState.Degrees ? "DEG" : "RAD";
    }

    // ---------------------------------------------------------------- menus

    private void Menu(Button anchor, params (string Label, string Insert)[] items)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        foreach (var (label, insert) in items)
        {
            if (label == "-") { menu.Items.Add(new Separator()); continue; }
            var mi = new MenuItem { Header = label };
            mi.Click += (_, _) => { InsertText(insert); Entry.Focus(); };
            menu.Items.Add(mi);
        }
        menu.IsOpen = true;
    }

    private void MathMenu_Click(object sender, RoutedEventArgs e) => Menu(MathMenuButton,
        ("abs(x)   absolute value", "abs("), ("round(x, digits)", "round("), ("int(x)   floor", "int("), ("ceil(x)", "ceil("),
        ("iPart(x)   whole part", "iPart("), ("fPart(x)   fraction part", "fPart("), ("-", ""),
        ("gcd(a, b)", "gcd("), ("lcm(a, b)", "lcm("), ("mod(a, b)   remainder", "mod("), ("-", ""),
        ("∛(x)   cube root", "∛("), ("root(x, n)   nth root", "root("), ("log(x, b)   any base", "log("), ("-", ""),
        ("sinh   cosh   tanh", "sinh("), ("min(a, b)", "min("), ("max(a, b)", "max("), ("►Frac   as a fraction", "►Frac"), ("►Dec   as a decimal", "►Dec"));

    private void StatMenu_Click(object sender, RoutedEventArgs e) => Menu(StatMenuButton,
        ("{1, 2, 3}→L1   store a list", "{}→L1"), ("mean(L1)", "mean("), ("median(L1)", "median("), ("stdDev(L1)   sample", "stdDev("),
        ("sum(L1)", "sum("), ("min(L1)   max(L1)", "min("), ("dim(L1)   how many", "dim("), ("-", ""),
        ("rand   0 to 1", "rand"), ("randInt(a, b)", "randInt("), ("randNorm(μ, σ)", "randNorm("), ("-", ""),
        ("normalpdf(x, μ, σ)", "normalpdf("), ("normalcdf(low, high, μ, σ)", "normalcdf("), ("invNorm(area, μ, σ)", "invNorm("),
        ("binompdf(n, p, k)", "binompdf("), ("binomcdf(n, p, k)", "binomcdf("));

    private void CalcMenu_Click(object sender, RoutedEventArgs e) => Menu(CalcMenuButton,
        ("nDeriv(f, x, value)   slope at a point", "nDeriv("), ("fnInt(f, x, low, high)   area", "fnInt("),
        ("sum(f, n, from, to)   Σ", "sum("), ("prod(f, n, from, to)   Π", "prod("), ("seq(f, n, from, to)   a list", "seq("), ("-", ""),
        ("f1(x)   a function from Graphing", "f1("), ("piecewise(value, condition, …)", "piecewise("));

    // ---------------------------------------------------------------- variables

    private void ShowVars()
    {
        var vars = MathState.Context.Vars
            .Where(kv => !MathState.Graph.Sliders.ContainsKey(kv.Key))
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new KeyValuePair<string, string>(kv.Key, Parser.Format(kv.Value, 8)))
            .ToList();
        foreach (var l in MathState.Context.Lists.Where(l => l.Value.Length > 0).OrderBy(l => l.Key))
            vars.Add(new KeyValuePair<string, string>(l.Key, $"{{{l.Value.Length} values}}"));
        VarList.ItemsSource = vars;
        NoVars.Visibility = vars.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Var_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string k) { InsertText(k); Entry.Focus(); }
    }

    private void ClearVars_Click(object sender, RoutedEventArgs e)
    {
        foreach (var k in MathState.Context.Vars.Keys.Where(k => !MathState.Graph.Sliders.ContainsKey(k)).ToList()) MathState.Context.Vars.Remove(k);
        MathState.RaiseChanged();
    }
}
