using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace Sumthing.Services;

/// <summary>Colours the graph canvas draws with (it paints in code, so it reads these directly).</summary>
public sealed class GraphPalette
{
    public Color Background, MinorGrid, MajorGrid, Axis, Label, Trace, PointFill, Shade;
    public Color[] Curves = Array.Empty<Color>();
}

/// <summary>
/// Builds Sumthing's palette in code and swaps it into Application.Resources. Same keys as WillowWallet and
/// Guardrail (Brush.Bg, Brush.Sidebar, Brush.Accent, ...), so the shared control styles work unchanged.
/// Light / Dark / follow Windows, seven accent colours, and two keypad styles: Modern and Classic
/// (the Windows 7 calculator).
/// </summary>
public static class ThemeManager
{
    public static bool IsDark { get; private set; }
    public static bool IsClassic { get; private set; }
    public static GraphPalette Graph { get; private set; } = new();

    public static readonly (string Name, Color Light, Color Dark)[] Accents =
    {
        ("Ember",    C("#C2410C"), C("#FB8A4C")),
        ("Ocean",    C("#1F5FA8"), C("#5AA1EC")),
        ("Teal",     C("#0E7C7E"), C("#4CC7C9")),
        ("Forest",   C("#1F6B4F"), C("#4FBF8A")),
        ("Violet",   C("#6D4AD6"), C("#A996F5")),
        ("Rose",     C("#B5245E"), C("#F07AA6")),
        ("Graphite", C("#3F4A5A"), C("#A9B4C6")),
    };

    /// <summary>The TI-84 Plus CE colour order (blue, red, black, magenta, green, orange, brown, navy, light blue, yellow).</summary>
    private static readonly string[] CurveLight = { "#1F6FEB", "#D92D20", "#1C1917", "#C026D3", "#15803D", "#EA580C", "#8B5E34", "#1E3A8A", "#0EA5E9", "#CA8A04" };
    private static readonly string[] CurveDark  = { "#60A5FA", "#F87171", "#E7E5E4", "#E879F9", "#4ADE80", "#FB923C", "#D6A06A", "#818CF8", "#38BDF8", "#FACC15" };

    public static event Action ThemeChanged;

    private static ResourceDictionary _current;

    public static bool SystemUsesDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int i) return i == 0;
        }
        catch { }
        return false;
    }

    public static void Apply(AppSettings s)
    {
        IsClassic = s.KeypadStyle == KeypadStyle.Classic;
        IsDark = s.Theme switch
        {
            ThemeMode.Dark => true,
            ThemeMode.Light => false,
            _ => SystemUsesDark(),
        };

        var d = new ResourceDictionary();
        void B(string key, Color c) { var b = new SolidColorBrush(c); b.Freeze(); d[key] = b; d[key + ".Color"] = c; }
        void G(string key, string top, string bottom)
        {
            var g = new LinearGradientBrush(C(top), C(bottom), 90);
            g.Freeze();
            d[key] = g;
        }

        var accent = Accents.FirstOrDefault(a => a.Name == s.Accent);
        if (accent.Name == null) accent = Accents[0];
        Color acc = IsDark ? accent.Dark : accent.Light;

        // ---------------- app palette (WillowWallet / Guardrail keys) ----------------
        if (!IsDark)
        {
            B("Brush.Bg", C("#F4F2EF"));
            B("Brush.Surface", C("#FFFFFF"));
            B("Brush.SurfaceAlt", C("#F1EEEA"));
            B("Brush.Border", C("#E4E0DA"));
            B("Brush.Skeleton", C("#ECE8E3"));
            B("Brush.Track", C("#EEEAE5"));
            B("Brush.Text", C("#1C1917"));
            B("Brush.TextMuted", C("#5F5750"));
            B("Brush.HoverOverlay", C("#10000000"));
            B("Brush.Sidebar", C("#1C1917"));
            B("Brush.SidebarText", C("#FFFFFF"));
            B("Brush.SidebarMuted", C("#C4BBB3"));
            B("Brush.SidebarHeading", C("#8F857C"));
            B("Brush.SidebarHover", C("#29231F"));
            B("Brush.SidebarSelected", C("#332B26"));
            B("Brush.SidebarCard", C("#29231F"));
            B("Brush.Hero", C("#1C1917"));
            B("Brush.HeroText", C("#FFFFFF"));
            B("Brush.HeroMuted", C("#C4BBB3"));
            B("Brush.AccentText", Colors.White);
            B("Brush.AccentSoft", Blend(acc, Colors.White, 0.88));
            B("Brush.BarMuted", C("#D6D0C9"));
            B("Brush.Warn", C("#B86A12"));
            B("Brush.Danger", C("#B91C1C"));
            B("Brush.Positive", C("#15803D"));
            B("Brush.ToastBg", C("#1C1917"));
            B("Brush.ToastText", C("#FFFFFF"));
        }
        else
        {
            B("Brush.Bg", C("#12100E"));
            B("Brush.Surface", C("#1C1917"));
            B("Brush.SurfaceAlt", C("#26211D"));
            B("Brush.Border", C("#332C27"));
            B("Brush.Skeleton", C("#2A2420"));
            B("Brush.Track", C("#2A2420"));
            B("Brush.Text", C("#EDE7E1"));
            B("Brush.TextMuted", C("#A89F97"));
            B("Brush.HoverOverlay", C("#14FFFFFF"));
            B("Brush.Sidebar", C("#0B0908"));
            B("Brush.SidebarText", C("#FFFFFF"));
            B("Brush.SidebarMuted", C("#B5ABA2"));
            B("Brush.SidebarHeading", C("#7C726A"));
            B("Brush.SidebarHover", C("#1A1613"));
            B("Brush.SidebarSelected", C("#26201C"));
            B("Brush.SidebarCard", C("#17130F"));
            B("Brush.Hero", C("#26211D"));
            B("Brush.HeroText", C("#FFFFFF"));
            B("Brush.HeroMuted", C("#C4BBB3"));
            B("Brush.AccentText", C("#140B06"));
            B("Brush.AccentSoft", Blend(acc, C("#1C1917"), 0.78));
            B("Brush.BarMuted", C("#3F3732"));
            B("Brush.Warn", C("#E9A24A"));
            B("Brush.Danger", C("#F87171"));
            B("Brush.Positive", C("#4ADE80"));
            B("Brush.ToastBg", C("#EDE7E1"));
            B("Brush.ToastText", C("#12100E"));
        }
        B("Brush.Accent", acc);
        B("Brush.Mint", accent.Dark);          // selected sidebar icon (sidebar is always dark)
        B("Brush.Highlight", accent.Dark);     // second half of the wordmark

        // ---------------- keypad ----------------
        if (IsClassic)
        {
            // The Windows 7 / 8 calculator
            G("KeyNumBg", "#FBFCFE", "#DDE6F1");
            G("KeyNumHover", "#FFF7E3", "#FCD98F");
            G("KeyNumPressed", "#FCE1A6", "#F5B652");
            foreach (var k in new[] { "KeyFn", "KeyOp", "KeyEq", "KeyMem" })
            {
                d[k + "Bg"] = d["KeyNumBg"]; d[k + "Hover"] = d["KeyNumHover"]; d[k + "Pressed"] = d["KeyNumPressed"];
            }
            B("KeyBorder", C("#8E9DB2"));
            B("KeyMemBorder", C("#8E9DB2"));
            B("KeyText", C("#1E2B3C"));
            B("KeyOpText", C("#1E2B3C"));
            B("KeyEqText", C("#1E2B3C"));
            B("KeyMemText", C("#1E2B3C"));
            d["KeyRadius"] = new CornerRadius(3);
            d["KeyGap"] = new Thickness(3);
            d["KeyFontSize"] = 13.0;
            d["KeyNumFontSize"] = 14.0;
            d["KeyNumWeight"] = FontWeights.Normal;
            d["KeyMemHeight"] = 30.0;
            G("CalcBg", "#DFE9F5", "#D3DEEC");
            B("CalcBorder", C("#A9B8CC"));
            d["CalcRadius"] = new CornerRadius(4);
            G("DisplayBg", "#FFFFFF", "#DCE7F3");
            B("DisplayBorder", C("#8E9DB2"));
            B("DisplayText", C("#000000"));
            B("DisplaySubText", C("#4D5C70"));
            d["CalcDisplayFont"] = new FontFamily("Consolas, Lucida Console");
            d["DisplayRadius"] = new CornerRadius(2);
        }
        else
        {
            if (!IsDark)
            {
                B("KeyNumBg", C("#FFFFFF")); B("KeyNumHover", C("#F6F3F0")); B("KeyNumPressed", C("#ECE7E2"));
                B("KeyFnBg", C("#F4F1ED")); B("KeyFnHover", C("#EBE6E1")); B("KeyFnPressed", C("#E2DCD5"));
                B("KeyBorder", C("#E4E0DA"));
                B("KeyText", C("#1C1917"));
                B("CalcBg", C("#FFFFFF"));
                B("CalcBorder", C("#E4E0DA"));
            }
            else
            {
                B("KeyNumBg", C("#2A2420")); B("KeyNumHover", C("#342D28")); B("KeyNumPressed", C("#231E1A"));
                B("KeyFnBg", C("#221D1A")); B("KeyFnHover", C("#2C2622")); B("KeyFnPressed", C("#1C1815"));
                B("KeyBorder", C("#3A322D"));
                B("KeyText", C("#F2ECE6"));
                B("CalcBg", C("#1C1917"));
                B("CalcBorder", C("#332C27"));
            }
            var soft = (SolidColorBrush)d["Brush.AccentSoft"];
            B("KeyOpBg", soft.Color);
            B("KeyOpHover", Blend(soft.Color, acc, 0.12));
            B("KeyOpPressed", Blend(soft.Color, acc, 0.24));
            B("KeyOpText", acc);
            B("KeyEqBg", acc);
            B("KeyEqHover", Blend(acc, IsDark ? Colors.White : Colors.Black, 0.10));
            B("KeyEqPressed", Blend(acc, IsDark ? Colors.White : Colors.Black, 0.20));
            B("KeyEqText", IsDark ? C("#140B06") : Colors.White);
            B("KeyMemBg", Colors.Transparent);
            B("KeyMemHover", IsDark ? C("#14FFFFFF") : C("#10000000"));
            B("KeyMemPressed", IsDark ? C("#22FFFFFF") : C("#1C000000"));
            B("KeyMemBorder", Colors.Transparent);
            B("KeyMemText", IsDark ? C("#C9C0B8") : C("#5F5750"));
            d["KeyRadius"] = new CornerRadius(10);
            d["KeyGap"] = new Thickness(3);
            d["KeyFontSize"] = 15.0;
            d["KeyNumFontSize"] = 21.0;
            d["KeyNumWeight"] = FontWeights.SemiBold;
            d["KeyMemHeight"] = 36.0;
            d["CalcRadius"] = new CornerRadius(14);
            B("DisplayBg", Colors.Transparent);
            B("DisplayBorder", Colors.Transparent);
            B("DisplayText", IsDark ? C("#FFFFFF") : C("#1C1917"));
            B("DisplaySubText", IsDark ? C("#A89F97") : C("#5F5750"));
            d["CalcDisplayFont"] = new FontFamily("Segoe UI Variable Display, Segoe UI");
            d["DisplayRadius"] = new CornerRadius(0);
        }

        // ---------------- paper tape ----------------
        B("TapeBg", IsDark ? C("#221F1A") : C("#FFFDF5"));
        B("TapeText", IsDark ? C("#ECE7DA") : C("#2A2620"));
        B("TapeLine", IsDark ? C("#4A463C") : C("#D8D1BC"));

        // ---------------- graph ----------------
        Graph = IsDark
            ? new GraphPalette
            {
                Background = C("#1C1917"), MinorGrid = C("#26211D"), MajorGrid = C("#332C27"), Axis = C("#A89F97"),
                Label = C("#A89F97"), Trace = acc, PointFill = C("#1C1917"), Shade = acc,
                Curves = CurveDark.Select(C).ToArray(),
            }
            : new GraphPalette
            {
                Background = C("#FFFFFF"), MinorGrid = C("#F1EEEA"), MajorGrid = C("#E2DDD7"), Axis = C("#5F5750"),
                Label = C("#6F665E"), Trace = acc, PointFill = C("#FFFFFF"), Shade = acc,
                Curves = CurveLight.Select(C).ToArray(),
            };

        var app = Application.Current;
        if (_current != null) app.Resources.MergedDictionaries.Remove(_current);
        app.Resources.MergedDictionaries.Insert(0, d);
        _current = d;

        foreach (Window w in app.Windows) ApplyTitleBar(w);
        ThemeChanged?.Invoke();
    }

    public static Color CurveColor(int index) =>
        Graph.Curves.Length == 0 ? Colors.SteelBlue : Graph.Curves[((index % Graph.Curves.Length) + Graph.Curves.Length) % Graph.Curves.Length];

    // ---------- native title bar matches the theme ----------
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static void ApplyTitleBar(Window w)
    {
        var hwnd = new WindowInteropHelper(w).Handle;
        if (hwnd == IntPtr.Zero) return;
        int on = IsDark ? 1 : 0;
        try
        {
            if (DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, 19, ref on, sizeof(int));
        }
        catch { }
    }

    public static void Attach(Window w) => w.SourceInitialized += (_, _) => ApplyTitleBar(w);

    public static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    public static Color Blend(Color a, Color b, double t) => Color.FromArgb(255,
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
}
