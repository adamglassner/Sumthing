using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Sumthing.Engine;
using Sumthing.Maths;
using Sumthing.Services;
using Sumthing.Views;

namespace Sumthing;

/// <summary>
/// Automated end-to-end test, run by the build with:  Sumthing.exe --self-test out.txt [screenshot-folder]
/// Checks the calculator engine, Type-a-sum, the converter, the everyday tools and the tape, then drives the
/// real window in all three looks (Light, Dark, Classic) and saves a screenshot of every mode.
/// Settings go to a temporary folder, never the user's own.
/// </summary>
public partial class MainWindow
{
    private readonly List<string> _testLog = new();
    private int _testFails;
    private string _shotDir;

    private sealed class TestFailure : Exception
    {
        public TestFailure(string message) : base(message) { }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new TestFailure(message);
    }

    private static void AreEqual(string expected, string actual, string what)
    {
        if (expected != actual) throw new TestFailure($"{what}: expected \"{expected}\", got \"{actual}\"");
    }

    private async Task Test(string name, Func<Task> body)
    {
        var sw = Stopwatch.StartNew();
        App.TestName = name;
        string line;
        try
        {
            await body();
            line = $"PASS {name} ({sw.ElapsedMilliseconds} ms)";
        }
        catch (Exception ex)
        {
            _testFails++;
            var msg = ex is TestFailure ? ex.Message : ex.ToString();
            line = $"FAIL {name}: {msg.Replace("\r", " ").Replace("\n", " ")}";
        }
        _testLog.Add(line);
        try { File.AppendAllText(App.SelfTestOutput, line + "\r\n"); } catch { }
    }

    private Task Test(string name, Action body) => Test(name, () => { body(); return Task.CompletedTask; });

    private async Task Idle()
    {
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        await Task.Delay(120);
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }

    private static string Run(string keys)
    {
        var e = new CalcEngine();
        foreach (var k in keys.Split(' ', StringSplitOptions.RemoveEmptyEntries)) e.Press(k);
        return e.Display;
    }

    private async Task RunSelfTestAsync()
    {
        _shotDir = App.SelfTestShots;
        if (_shotDir != null) Directory.CreateDirectory(_shotDir);
        try { File.WriteAllText(App.SelfTestOutput, $"Sumthing {AppInfo.Version} self-test\r\n"); } catch { }
        Num.Grouping = true;

        // ---------------------------------------------------------------- engine
        await Test("Engine: basic sums", () =>
        {
            AreEqual("42", Run("1 2 + 3 0 ="), "12 + 30 =");
            AreEqual("5", Run("2 + 3 ×"), "immediate execution");
            AreEqual("10", Run("5 + ="), "5 + = uses 5 again");
            AreEqual("8", Run("2 + 3 = ="), "repeated equals");
            AreEqual("0.3", Run("0 . 1 + 0 . 2 ="), "0.1 + 0.2 (decimal, not floating point)");
            AreEqual("1", Run("1 ÷ 3 × 3 ="), "1 ÷ 3 × 3");
            AreEqual("-5", Run("2 − 7 ="), "negative result");
            AreEqual("1,234,567", Run("1 2 3 4 5 6 7"), "thousands separators while typing");
            AreEqual("12", Run("1 2 3 back"), "backspace");
            AreEqual("0", Run("8 × 2 C"), "clear");
            AreEqual("24", Run("8 × 2 CE 3 −"), "clear entry keeps the sum");
        });
        await Test("Engine: percent, roots, powers", () =>
        {
            AreEqual("55", Run("5 0 + 1 0 % ="), "50 + 10%");
            AreEqual("40", Run("5 0 − 2 0 % ="), "50 − 20%");
            AreEqual("10", Run("5 0 × 2 0 % ="), "50 × 20%");
            AreEqual("3", Run("9 √"), "√9");
            AreEqual("1.414213562373095", Run("2 √"), "√2 to 16 digits");
            AreEqual("144", Run("1 2 sqr"), "12²");
            AreEqual("0.25", Run("4 inv"), "1/4");
            AreEqual("-5", Run("5 neg"), "negate");
        });
        await Test("Engine: errors", () =>
        {
            AreEqual("Can't divide by zero", Run("1 0 ÷ 0 ="), "10 ÷ 0");
            AreEqual("Invalid input", Run("4 neg √"), "√-4");
            AreEqual("7", Run("1 ÷ 0 = 7"), "typing after an error starts again");
            var big = Run("9 9 9 9 9 9 9 9 9 9 9 9 9 9 9 9 × 9 9 9 9 9 9 9 9 9 9 9 9 = ");
            Assert(big.Contains("e+"), $"huge numbers switch to scientific notation (got {big})");
        });
        await Test("Engine: memory and history", () =>
        {
            var e = new CalcEngine();
            foreach (var k in "7 MS C 3 M+ C MR".Split(' ')) e.Press(k);
            AreEqual("10", e.Display, "MS, M+, MR");
            foreach (var k in "MC".Split(' ')) e.Press(k);
            Assert(e.Memory == null, "MC clears memory");
            foreach (var k in "6 × 7 =".Split(' ')) e.Press(k);
            Assert(e.History.Count == 1 && e.History[0].Result == "42" && e.History[0].Expression == "6 × 7 =", "history entry");
        });
        await Test("Engine: tape", () =>
        {
            var e = new CalcEngine();
            var tape = new List<TapeEntry>();
            e.Tape += t => { if (t != null) tape.Add(t); };
            foreach (var k in "1 2 + 3 0 × 2 =".Split(' ')) e.Press(k);
            AreEqual("84", e.Display, "12 + 30 × 2 (immediate)");
            AreEqual("12 + | 30 × | 2 = | 84 ", string.Join(" | ", tape.Select(t => $"{t.Number} {t.Symbol}")), "tape lines");
            Assert(tape[^1].IsTotal, "last line is a total");
        });

        // ---------------------------------------------------------------- Type-a-sum
        await Test("Type-a-sum", () =>
        {
            var lines = new[]
            {
                "# Budget", "Rent = 1,250", "Groceries: 85 + 42.50 + 61", "car payment = 300", "total",
                "", "12% of 340 + 8", "100 + 10%", "80 - 25%", "20% off 80", "8% on 50", "rent * 12",
                "car payment / 2", "2^10", "(4 + 6) / 4", "sqrt 81", "5k + 250", "ans * 2", "5 apples + 3 apples",
                "just words", "1 / 0", "// comment only", "2(3 + 4)",
            };
            var r = SumSheet.Evaluate(lines);
            string[] expect =
            {
                "", "1,250", "188.5", "300", "1,738.5",
                "", "48.8", "110", "60", "64", "54", "15,000",
                "150", "1,024", "2.5", "9", "5,250", "10,500", "8",
                "", "", "", "14",
            };
            for (int i = 0; i < lines.Length; i++)
                AreEqual(expect[i], r[i].Text, $"line {i + 1} \"{lines[i]}\"");
            Assert(r[20].Error != null, "1 / 0 is an error");
            Assert(r[0].IsHeading, "# makes a heading");
        });

        // ---------------------------------------------------------------- converter maths
        await Test("Units", () =>
        {
            UnitCategory C(string n) => Units.Categories.First(c => c.Name == n);
            Unit U(string cat, string sym) => C(cat).Units.First(u => u.Symbol == sym);
            AreEqual("1.609344", Num.Format(Units.Convert(C("Length"), U("Length", "mi"), U("Length", "km"), 1)), "1 mi in km");
            AreEqual("212", Num.Format(Units.Convert(C("Temperature"), U("Temperature", "°C"), U("Temperature", "°F"), 100)), "100 °C in °F");
            AreEqual("-40", Num.Format(Units.Convert(C("Temperature"), U("Temperature", "°F"), U("Temperature", "°C"), -40)), "-40 °F in °C");
            AreEqual("3.785411784", Num.Format(Units.Convert(C("Volume"), U("Volume", "gal"), U("Volume", "L"), 1)), "1 gal in L");
            AreEqual("1,073.741824", Num.Format(Units.Convert(C("Data"), U("Data", "GiB"), U("Data", "MB"), 1)), "1 GiB in MB");
            AreEqual("7.84", Num.Format(Math.Round(Units.Convert(C("Fuel economy"), U("Fuel economy", "mpg"), U("Fuel economy", "L/100 km"), 30), 2)), "30 mpg in L/100 km");
            AreEqual("2.20462262", Num.Format(Units.Convert(C("Weight"), U("Weight", "kg"), U("Weight", "lb"), 1), 9), "1 kg in lb");
        });

        CurrencyService.UseRates(new Dictionary<string, decimal> { ["EUR"] = 1m, ["USD"] = 1.10m, ["GBP"] = 0.85m, ["JPY"] = 165m }, "2026-11-06");
        await Test("Currency maths", () =>
        {
            AreEqual("100", Num.Format(CurrencyService.Convert(110m, "USD", "EUR")), "110 USD in EUR");
            AreEqual("85", Num.Format(CurrencyService.Convert(110m, "USD", "GBP")), "110 USD in GBP");
        });

        // ---------------------------------------------------------------- maths engine (answers checked against a TI-84)
        var cx = new EvalContext();
        string E(string expr, double x = 0)
        {
            cx.X = x;
            return Parser.Format(Parser.Parse(expr, cx.Vars.Keys).Eval(cx), 10);
        }
        await Test("Maths: order of operations and implicit multiplication", () =>
        {
            AreEqual("14", E("2+3*4"), "2+3*4");
            AreEqual("512", E("2^3^2"), "2^3^2 (right to left)");
            AreEqual("−4", E("-2^2"), "-2^2 = -(2^2)");
            AreEqual("6", E("2x", 3), "2x at x=3");
            AreEqual("15", E("3(4+1)"), "3(4+1)");
            AreEqual("20", E("(2+3)(1+3)"), "(2+3)(1+3)");
            AreEqual("4", E("√(16)"), "√(16)");
            AreEqual("4", E("√16"), "√16");
            AreEqual("120", E("5!"), "5!");
            AreEqual("1,500", E("1.5E3"), "1.5E3");
            AreEqual("6", E("|−5|+1"), "|−5|+1");
            AreEqual("8", E("2|x−5|", 1), "2|x−5| at x=1");
            AreEqual("1.570796327", E("xsin(x)", System.Math.PI / 2), "xsin(x) = x·sin(x)");
            AreEqual("9", E("x²", 3), "x²");
            AreEqual("−2", E("(-8)^(1/3)"), "real cube root of −8");
        });
        await Test("Maths: functions", () =>
        {
            AreEqual("35", E("7 nCr 3"), "7 nCr 3");
            AreEqual("35", E("nCr(7,3)"), "nCr(7,3)");
            AreEqual("210", E("7 nPr 3"), "7 nPr 3");
            AreEqual("0.5", E("sin(30°)"), "sin(30°) in radians mode");
            AreEqual("3", E("log(1000)"), "log(1000)");
            AreEqual("3", E("log(8,2)"), "log(8,2)");
            AreEqual("2", E("ln(e^2)"), "ln(e²)");
            AreEqual("2", E("mod(17,5)"), "mod(17,5)");
            AreEqual("2.57", E("round(2.567,2)"), "round(2.567,2)");
            AreEqual("6", E("gcd(12,18)"), "gcd(12,18)");
            AreEqual("2.5", E("mean({1,2,3,4})"), "mean({1,2,3,4})");
            AreEqual("12", E("nDeriv(x^3,x,2)"), "nDeriv(x³,x,2)");
            AreEqual("9", E("fnInt(x^2,x,0,3)"), "fnInt(x²,x,0,3)");
            AreEqual("385", E("sum(n^2,n,1,10)"), "Σ n² for n = 1 to 10");
            AreEqual("0.6826894921", E("normalcdf(-1,1)"), "normalcdf(-1,1)");
            AreEqual("1.959963985", E("invNorm(0.975)"), "invNorm(0.975)");
            AreEqual("0.1171875", E("binompdf(10,0.5,3)"), "binompdf(10,.5,3)");
            cx.Degrees = true;
            AreEqual("0.5", E("sin(30)"), "sin(30) in degrees");
            AreEqual("30", E("sin⁻¹(0.5)"), "sin⁻¹(0.5) in degrees");
            cx.Degrees = false;
        });
        await Test("Maths: home screen (Ans, STO→, ►Frac, lists)", () =>
        {
            var s = new CalcSession();
            AreEqual("5", s.Evaluate("5→A").Output, "5→A");
            AreEqual("26", s.Evaluate("A²+1").Output, "A²+1");
            AreEqual("30", s.Evaluate("+4").Output, "+4 carries on from Ans");
            AreEqual("3/4", s.Evaluate("0.75►Frac").Output, "0.75►Frac");
            AreEqual("1/3", s.Evaluate("1/3").Exact, "1/3 shows its fraction");
            AreEqual("{3, 5, 8}", s.Evaluate("{3,5,8}→L1").Output, "store a list");
            AreEqual("5.333333333", Parser.Format(s.Evaluate("mean(L1)").Value!.Value, 10), "mean(L1)");
            Assert(s.Evaluate("1/0+").IsError, "an unfinished line is an error, not a crash");
            Assert(s.Evaluate("Q+1").IsError, "an unknown letter says it has no value");
        });
        await Test("Maths: analysis (CALC menu)", () =>
        {
            Func<double, double> f = x => x * x - 2 * x - 3;
            AreEqual("−1, 3", string.Join(", ", Analysis.Zeros(f, -10, 10).Select(z => Parser.Format(z))), "zeros of x²−2x−3");
            var ex = Analysis.Extrema(f, -10, 10);
            Assert(ex.Count == 1 && ex[0].Kind == "Minimum" && ex[0].X == 1 && ex[0].Y == -4, $"minimum of x²−2x−3 at (1, −4), got {string.Join(";", ex)}");
            var meet = Analysis.Intersections(x => x * x, x => x + 2, -10, 10);
            AreEqual("(−1, 1) (2, 4)", string.Join(" ", meet.Select(p => $"({Parser.Format(p.X)}, {Parser.Format(p.Y)})")), "x² meets x+2");
            AreEqual("0", string.Join(", ", Analysis.Zeros(x => x * x, -5, 5).Select(z => Parser.Format(z))), "touching zero of x²");
            Assert(Analysis.Zeros(x => System.Math.Tan(x), -1.4, 1.4).Count == 1, "tan has one zero in (−1.4, 1.4)");
            Assert(Analysis.Zeros(x => 1 / x, -5, 5).Count == 0, "1/x has no zero (the jump at 0 is an asymptote)");
            AreEqual("9", Parser.Format(Analysis.Integrate(x => x * x, 0, 3)), "∫x² from 0 to 3");
            AreEqual("2", Parser.Format(Analysis.Integrate(System.Math.Sin, 0, System.Math.PI)), "∫sin from 0 to π");
            AreEqual("12", Parser.Format(Analysis.Derivative(x => x * x * x, 2)), "d/dx x³ at 2");
        });
        await Test("Maths: statistics and regressions", () =>
        {
            var one = Stats.OneVar(new double[] { 2, 4, 4, 4, 5, 5, 7, 9 });
            Assert(one.Mean == 5 && one.SigmaX == 2 && one.Median == 4.5 && one.Q1 == 4 && one.Q3 == 6, "1-Var Stats: mean 5, σx 2, Med 4.5, Q1 4, Q3 6");
            AreEqual("2.138089935", Parser.Format(one.Sx, 10), "Sx");
            var xs = new double[] { 1, 2, 3, 4, 5 };
            var lin = Stats.Fit(RegressionModel.Linear, xs, xs.Select(x => 2 * x + 1).ToArray());
            Assert(Near(lin.Coefficients[0], 2) && Near(lin.Coefficients[1], 1) && Near(lin.R, 1), "LinReg y = 2x + 1, r = 1");
            var quad = Stats.Fit(RegressionModel.Quadratic, xs, xs.Select(x => x * x + 1).ToArray());
            Assert(Near(quad.Coefficients[0], 1) && Near(quad.Coefficients[1], 0) && Near(quad.Coefficients[2], 1), "QuadReg x² + 1");
            var exp = Stats.Fit(RegressionModel.Exponential, xs, xs.Select(x => 3 * System.Math.Pow(2, x)).ToArray());
            Assert(Near(exp.Coefficients[0], 3) && Near(exp.Coefficients[1], 2), "ExpReg 3·2ˣ");
            var pwr = Stats.Fit(RegressionModel.Power, xs, xs.Select(x => 2 * x * x * x).ToArray());
            Assert(Near(pwr.Coefficients[0], 2) && Near(pwr.Coefficients[1], 3), "PwrReg 2x³");
            var two = Stats.TwoVar(xs, xs.Select(x => 10 - x).ToArray());
            Assert(Near(two.R, -1), "2-Var r = −1");
        });
        await Test("Graph: reading rows", () =>
        {
            var g = new GraphModel(new EvalContext());
            foreach (var t in new[] { "x²−2x−3", "y<2x+1", "x²+y²=25", "r=2sin(3θ)", "(cos(t), sin(t))", "(1,2),(3,4)", "g(x)=x+1", "f1(2)+g(1)", "ax²+b", "y=2x+", "x≥y²" })
                g.Rows.Add(new GraphRow { Text = t });
            g.Recompile();
            var kinds = g.Compiled.Select(c => c.Kind).ToArray();
            var want = new[] { PlotKind.Function, PlotKind.Inequality, PlotKind.Implicit, PlotKind.Polar, PlotKind.Parametric, PlotKind.Points,
                               PlotKind.Function, PlotKind.Function, PlotKind.Function, PlotKind.Error, PlotKind.ImplicitInequality };
            for (int i = 0; i < want.Length; i++) Assert(kinds[i] == want[i], $"row {i + 1} \"{g.Rows[i].Text}\" should be {want[i]}, got {kinds[i]} {g.Compiled[i].Error}");
            AreEqual("−1", Parser.Format(g.EvalFunction(g.Compiled[7], 0)), "f1(2)+g(1) uses other rows");
            Assert(g.Sliders.ContainsKey("a") && g.Sliders.ContainsKey("b") && g.Sliders.Count == 2, "a and b become sliders");
            g.Sliders["a"].Value = 2; g.Sliders["b"].Value = 3; g.PushSliders();
            AreEqual("11", Parser.Format(g.EvalFunction(g.Compiled[8], 2)), "ax²+b with a=2, b=3 at x=2");
            g.Context.Lists["L1"] = new double[] { 2, 4, 6 };
            g.Context.Lists["L2"] = new double[] { 10, 20, 30 };
            g.Rows.Add(new GraphRow { Text = "(L1/2, L2*10)" });
            g.Recompile();
            var pts = g.EvalPoints(g.Compiled[^1]);
            AreEqual("1,2,3 / 100,200,300", string.Join(",", pts.X) + " / " + string.Join(",", pts.Y), "list arithmetic in a scatter plot");
            Assert(!g.Sliders.ContainsKey("L1"), "lists aren't sliders");
        });

        // ---------------------------------------------------------------- the window
        S.Theme = ThemeMode.Light;
        S.KeypadStyle = KeypadStyle.Modern;
        SettingsService.Commit();
        SetMode(CalcMode.Standard);
        await Idle();

        await Test("Standard: clicking keys", async () =>
        {
            Engine.Press("C");
            StandardPage.TapePanel.Clear();
            foreach (var k in new[] { "7", "×", "6", "=" }) Click(FindKey(StandardPage.ModernPadPanel, k));
            await Idle();
            AreEqual("42", StandardPage.MainDisplay, "display after 7 × 6 =");
            AreEqual("7 × 6 =", StandardPage.ExpressionDisplay, "expression line");
            Assert(StandardPage.TapePanel.Entries.Count >= 3 && StandardPage.TapePanel.Entries[^1].Number == "42", "the tape got the sum");
        });
        await Test("Standard: typing", async () =>
        {
            Engine.Press("C");
            foreach (var ch in "125/5=") StandardPage.HandleText(ch.ToString());
            await Idle();
            AreEqual("25", StandardPage.MainDisplay, "typed 125/5=");
        });
        await Test("Standard: tape save", () =>
        {
            var path = Path.Combine(Path.GetTempPath(), "sumthing-tape-test.txt");
            Assert(StandardPage.TapePanel.SaveTo(path), "saved");
            Assert(File.ReadAllText(path).Contains("42"), "saved tape has the sum");
        });

        await Test("Scientific page", async () =>
        {
            SetMode(CalcMode.Scientific);
            MathState.Session.Entries.Clear();
            ScientificPage.Run("5→A");
            ScientificPage.Run("2A+sin(π/6)");
            ScientificPage.Run("7 nCr 3");
            ScientificPage.Run("√(2)");
            ScientificPage.Run("{3,5,8}→L1");
            var last = ScientificPage.Run("mean(L1)►Frac");
            await Idle();
            AreEqual("16/3", last.Output, "mean(L1)►Frac");
            AreEqual("10.5", MathState.Session.Entries[1].Output, "2A+sin(π/6)");
        });

        await Test("Graphing page: analyze", async () =>
        {
            SetMode(CalcMode.Graphing);
            GraphPage.SetRows("x²−2x−3", "x+1", "");
            GraphPage.ZoomPreset("Standard");
            await Idle();
            GraphPage.SelectAnalyzeRows(0, 1);
            GraphPage.Zeros_Click(null, null);
            AreEqual("x = −1,  x = 3", GraphPage.Results[0].Value, "zeros");
            GraphPage.Minimum_Click(null, null);
            AreEqual("(1, −4)", GraphPage.Results[0].Value, "minimum");
            GraphPage.Intersect_Click(null, null);
            AreEqual("(−1, 0),  (4, 5)", GraphPage.Results[0].Value, "x²−2x−3 meets x+1");
            GraphPage.SetAnalysisInputs("2", "0", "3");
            GraphPage.Value_Click(null, null);
            AreEqual("−3", GraphPage.Results[0].Value, "f1(2)");
            GraphPage.Integral_Click(null, null);
            AreEqual("−9", GraphPage.Results[0].Value, "∫ from 0 to 3");
            GraphPage.Derivative_Click(null, null);
            Assert(GraphPage.Results[0].Value.StartsWith("2 "), "dy/dx at 2 is 2: " + GraphPage.Results[0].Value);
            GraphPage.ShowSideTab(1);
            await Idle();
            Assert(GraphPage.TableData.Count > 3 && GraphPage.TableData[0][0] == "−3" && GraphPage.TableData[0][1] == "12", "table starts at x = −3 with f1 = 12");
            GraphPage.ShowSideTab(0);
            GraphPage.SetTrace(true);
            GraphPage.GraphCanvas.TraceValue = 2;
            Assert(GraphPage.GraphCanvas.TraceText().Contains("y = −3"), "trace at x = 2: " + GraphPage.GraphCanvas.TraceText());
            GraphPage.SetTrace(false);
            GraphPage.GraphCanvas.RefreshPointsOfInterest();
            Assert(GraphPage.GraphCanvas.PointsOfInterest.Count >= 5, "points of interest: zeros, minimum, intercepts, intersections");
        });

        await Test("Statistics page", async () =>
        {
            SetMode(CalcMode.Statistics);
            StatsPage.SetList(1, 1, 2, 3, 4, 5, 6, 7, 8);
            StatsPage.SetList(2, 52, 55, 61, 64, 70, 73, 79, 84);
            StatsPage.Choose(2, 0);
            StatsPage.Calculate_Click(null, null);
            await Idle();
            Assert(StatsPage.LastFit != null && System.Math.Abs(StatsPage.LastFit.Coefficients[0] - 4.595238095) < 1e-6, "LinReg slope 4.595238095");
            StatsPage.Choose(0);
            StatsPage.Calculate_Click(null, null);
            Assert(StatsPage.Results.Any(r => r.Name.StartsWith("x̄") && r.Value == "4.5"), "1-Var mean of L1 is 4.5");
        });

        await Test("Tools: tip & split", async () =>
        {
            SetMode(CalcMode.Tools);
            ToolsPage.ShowTab(0);
            await Idle();
            Assert(ToolsPage.TipResult.Each == 38.88m, $"64.80 + 20% for 2 = 38.88 each (got {ToolsPage.TipResult.Each})");
        });
        await Test("Converter page", async () =>
        {
            SetMode(CalcMode.Converter);
            ConverterPage.SelectCategory("Length");
            ConverterPage.SetUnits(7, 3);
            ConverterPage.SetAmount("1");
            await Idle();
            Assert(ConverterPage.ResultText.StartsWith("1.609344"), "1 mi = 1.609344 km, got " + ConverterPage.ResultText);
        });

        await Test("Type-a-sum page", async () =>
        {
            SetMode(CalcMode.Sums);
            SumsPage.SetSheet(AppSettings.DefaultSheet.TrimEnd('\n'));
            await Idle();
            Assert(SumsPage.Results.Count(r => r.Value != null) >= 7, "answers shown");
        });

        // ---------------------------------------------------------------- screenshots of every page and look
        GraphPage.SetRows("x²−2x−3", "2sin(x)", "y<0.5x−4", "x²+y²=36", "r=2+2cos(θ)", "(L1/2, L2/20)", "ax+b");
        GraphPage.ZoomPreset("Standard");
        MathState.Session.Entries.Clear();
        foreach (var line in new[] { "5→A", "2A+sin(π/6)", "7 nCr 3", "√(2)", "1/3+1/4", "{3,5,8}→L1", "mean(L1)►Frac", "fnInt(x²,x,0,3)", "normalcdf(−1,1)" })
            ScientificPage.Run(line);
        StatsPage.SetList(1, 1, 2, 3, 4, 5, 6, 7, 8);
        StatsPage.SetList(2, 52, 55, 61, 64, 70, 73, 79, 84);
        var pages = new[] { CalcMode.Standard, CalcMode.Scientific, CalcMode.Graphing, CalcMode.Statistics, CalcMode.Sums, CalcMode.Converter, CalcMode.Tools, CalcMode.Settings };
        foreach (var (theme, keys, tag) in new[] { (ThemeMode.Light, KeypadStyle.Modern, "light"), (ThemeMode.Dark, KeypadStyle.Modern, "dark"), (ThemeMode.Light, KeypadStyle.Classic, "classic") })
        {
            await Test($"Look: {tag}", async () =>
            {
                S.Theme = theme;
                S.KeypadStyle = keys;
                SettingsService.Commit();
                await Idle();
                Assert(ThemeManager.IsClassic == (keys == KeypadStyle.Classic), "classic keys flag");
                int n = 1;
                foreach (var m in pages)
                {
                    if (tag == "classic" && m != CalcMode.Standard && m != CalcMode.Scientific) continue;
                    SetMode(m, animate: false);
                    if (m == CalcMode.Standard)
                    {
                        Engine.Press("C");
                        if (keys == KeypadStyle.Classic)
                        {
                            foreach (var k in new[] { "1", "2", "+", "8", "=" }) Click(FindKey(StandardPage.ClassicPadPanel, k));
                            await Idle();
                            AreEqual("20", StandardPage.MainDisplay, "classic keypad 12 + 8 =");
                        }
                        else foreach (var k in "1250×12=") StandardPage.HandleText(k.ToString());
                    }
                    if (m == CalcMode.Statistics) { StatsPage.Choose(2, 0); StatsPage.Calculate_Click(null, null); }
                    if (m == CalcMode.Graphing) GraphPage.GraphCanvas.RefreshPointsOfInterest();
                    if (m == CalcMode.Converter) { ConverterPage.SelectCategory("Currency"); ConverterPage.SetCurrencies("USD", "EUR"); ConverterPage.SetAmount("250"); }
                    await Idle();
                    if (m == CalcMode.Graphing) { await Task.Delay(300); await Idle(); }
                    Shot($"{tag}-{n++}-{m.ToString().ToLower()}");
                }
                // compact calculator
                SetCompact(true);
                await Idle();
                Shot($"{tag}-9-compact");
                SetCompact(false);
                await Idle();
            });
        }

        await Test("Sidebar collapses", async () =>
        {
            S.Theme = ThemeMode.Dark; S.KeypadStyle = KeypadStyle.Modern; SettingsService.Commit();
            SetSidebar(true);
            SetMode(CalcMode.Graphing, animate: false);
            await Idle();
            Assert(SidebarColumn.Width.Value < 100, "collapsed");
            Shot("dark-10-sidebar-collapsed");
            SetSidebar(false);
        });

        await Test("Settings are saved", () =>
        {
            SaveState();
            SettingsService.Load();
            Assert(SettingsService.Current.Theme == ThemeMode.Dark, "theme saved");
            Assert(SettingsService.Current.GraphRows.Count == 7, "graph rows saved");
            Assert(SettingsService.Current.StoredVariables.ContainsKey("A"), "stored values saved");
        });

        int fails = _testFails;
        try { File.AppendAllText(App.SelfTestOutput, $"\r\n{_testLog.Count - fails} passed, {fails} failed\r\n"); } catch { }
        Application.Current.Shutdown(fails == 0 ? 0 : 1);
    }

    private static bool Near(double a, double b) => System.Math.Abs(a - b) < 1e-6 * System.Math.Max(1, System.Math.Abs(b));

    // ---------------------------------------------------------------- helpers

    private static CalcKey FindKey(Panel pad, string tag)
    {
        foreach (var child in pad.Children)
            if (child is CalcKey k && k.Tag as string == tag) return k;
        throw new TestFailure($"no key \"{tag}\"");
    }

    private static void Click(Button b)
    {
        var peer = new ButtonAutomationPeer(b);
        ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
    }

    private void Shot(string name)
    {
        if (_shotDir == null || Content is not FrameworkElement element || element.ActualWidth < 1) return;
        var dpi = VisualTreeHelper.GetDpi(element);
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(element.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        var bg = new DrawingVisual();
        using (var dc = bg.RenderOpen())
            dc.DrawRectangle(Background ?? Brushes.White, null, new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        bmp.Render(bg);
        bmp.Render(element);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(Path.Combine(_shotDir, name + ".png"));
        enc.Save(fs);
    }
}
