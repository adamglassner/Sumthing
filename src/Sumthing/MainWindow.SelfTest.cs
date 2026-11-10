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

        // ---------------------------------------------------------------- the window, modern light
        S.Theme = ThemeMode.Light;
        SettingsService.Commit();
        SetMode(CalcMode.Standard);
        await Idle();

        await Test("Window: clicking keys", async () =>
        {
            Engine.Press("C");
            TapePage.Clear();
            foreach (var k in new[] { "7", "×", "6", "=" }) Click(FindKey(StandardPage.ModernPadPanel, k));
            await Idle();
            AreEqual("42", StandardPage.MainDisplay, "display after 7 × 6 =");
            AreEqual("7 × 6 =", StandardPage.ExpressionDisplay, "expression line");
            Assert(TapePage.Entries.Count >= 3 && TapePage.Entries[^1].Number == "42" && TapePage.Entries[^1].IsTotal, "the tape got the sum");
        });

        await Test("Window: typing", async () =>
        {
            Engine.Press("C");
            foreach (var ch in "125/5=") StandardPage.HandleText(ch.ToString());
            await Idle();
            AreEqual("25", StandardPage.MainDisplay, "typed 125/5=");
            StandardPage.HandleKeyDown(System.Windows.Input.Key.Escape, System.Windows.Input.ModifierKeys.None);
            AreEqual("0", StandardPage.MainDisplay, "Esc clears");
        });

        await Test("Window: tape save", () =>
        {
            var path = Path.Combine(Path.GetTempPath(), "sumthing-tape-test.txt");
            Assert(TapePage.SaveTo(path), "saved");
            var text = File.ReadAllText(path);
            Assert(text.Contains("42") && text.Contains("×"), "saved tape has the sum: " + text);
        });

        await Test("Tools: tip & split", async () =>
        {
            SetMode(CalcMode.Tools);
            ToolsPage.ShowTab(0);
            await Idle();
            var r = ToolsPage.TipResult;
            Assert(r.Tip == 12.96m && r.Total == 77.76m && r.Each == 38.88m, $"64.80 + 20% for 2 = 38.88 each (got {r.Each})");
        });
        await Test("Tools: percent change", async () =>
        {
            ToolsPage.ShowTab(1);
            await Idle();
            Assert(ToolsPage.PercentResult == 25m, $"80 → 100 is +25% (got {ToolsPage.PercentResult})");
        });
        await Test("Tools: discount & tax", async () =>
        {
            ToolsPage.ShowTab(2);
            await Idle();
            Assert(ToolsPage.DiscountResult == 103.34m, $"129.99 − 25% + 6% tax = 103.34 (got {ToolsPage.DiscountResult})");
        });
        await Test("Tools: date difference", async () =>
        {
            ToolsPage.ShowTab(3);
            ToolsPage.SetDates(new DateTime(2026, 1, 1), new DateTime(2026, 12, 25));
            await Idle();
            Assert(ToolsPage.DaysResult == 358, $"Jan 1 → Dec 25 2026 is 358 days (got {ToolsPage.DaysResult})");
        });

        await Test("Converter screen", async () =>
        {
            SetMode(CalcMode.Converter);
            ConverterPage.SelectCategory("Length");
            ConverterPage.SetUnits(7, 3);
            ConverterPage.SetAmount("1");
            await Idle();
            Assert(ConverterPage.ResultText.StartsWith("1.609344"), "1 mi = 1.609344 km, got " + ConverterPage.ResultText);
            ConverterPage.SelectCategory("Currency");
            ConverterPage.SetCurrencies("USD", "EUR");
            ConverterPage.SetAmount("110");
            await Idle();
            AreEqual("100.00 EUR", ConverterPage.ResultText, "110 USD in EUR");
            ConverterPage.SetAmount("12 * 10");
            await Idle();
            Assert(ConverterPage.ResultText.StartsWith("109.09"), "a little sum as the amount, got " + ConverterPage.ResultText);
        });

        await Test("Type-a-sum screen", async () =>
        {
            SetMode(CalcMode.Sums);
            SumsPage.SetSheet(AppSettings.DefaultSheet.TrimEnd('\n'));
            await Idle();
            Assert(SumsPage.Results.Count(r => r.Value != null) >= 7, "answers shown");
        });

        // ---------------------------------------------------------------- screenshots of every look
        var modes = new[] { CalcMode.Standard, CalcMode.Sums, CalcMode.Converter, CalcMode.Tools, CalcMode.Settings };
        foreach (var theme in new[] { ThemeMode.Light, ThemeMode.Dark, ThemeMode.Classic })
        {
            await Test($"Look: {theme}", async () =>
            {
                S.Theme = theme;
                SettingsService.Commit();
                SetMode(CalcMode.Standard);
                SetTape(false);
                await Idle();
                Assert(ThemeManager.IsClassic == (theme == ThemeMode.Classic), "classic flag");
                Assert((ClassicMenu.Visibility == Visibility.Visible) == (theme == ThemeMode.Classic), "menu bar only in Classic");
                if (theme == ThemeMode.Classic)
                {
                    Engine.Press("C");
                    foreach (var k in new[] { "1", "2", "+", "8", "=" }) Click(FindKey(StandardPage.ClassicPadPanel, k));
                    await Idle();
                    AreEqual("20", StandardPage.MainDisplay, "classic keypad 12 + 8 =");
                }
                else
                {
                    Engine.Press("C");
                    foreach (var k in "1250 × 12 =") StandardPage.HandleText(k.ToString());
                    await Idle();
                }
                Shot($"{theme.ToString().ToLower()}-1-standard");

                Engine.Press("M+");
                StandardPage.ShowHistory(true);
                await Idle();
                Shot($"{theme.ToString().ToLower()}-2-history");
                StandardPage.ShowHistory(false);

                SetTape(true);
                await Idle();
                Shot($"{theme.ToString().ToLower()}-3-tape");
                SetTape(false);

                int n = 4;
                foreach (var m in modes.Skip(1))
                {
                    SetMode(m);
                    if (m == CalcMode.Tools) ToolsPage.ShowTab(0);
                    if (m == CalcMode.Converter) { ConverterPage.SelectCategory("Currency"); ConverterPage.SetCurrencies("USD", "EUR"); ConverterPage.SetAmount("250"); }
                    await Idle();
                    Shot($"{theme.ToString().ToLower()}-{n++}-{m.ToString().ToLower()}");
                }
                if (theme != ThemeMode.Classic)
                {
                    SetMode(CalcMode.Standard);
                    NavLayer.Visibility = Visibility.Visible;
                    await Idle();
                    Shot($"{theme.ToString().ToLower()}-9-navigation");
                    NavLayer.Visibility = Visibility.Collapsed;
                }
                SetMode(CalcMode.Standard);
            });
        }

        await Test("Settings are saved", () =>
        {
            S.Theme = ThemeMode.Dark;
            SaveState();
            SettingsService.Load();
            Assert(SettingsService.Current.Theme == ThemeMode.Dark, "theme saved");
            Assert(SettingsService.Current.History.Count > 0, "history saved");
        });

        int fails = _testFails;
        try { File.AppendAllText(App.SelfTestOutput, $"\r\n{_testLog.Count - fails} passed, {fails} failed\r\n"); } catch { }
        Application.Current.Shutdown(fails == 0 ? 0 : 1);
    }

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
