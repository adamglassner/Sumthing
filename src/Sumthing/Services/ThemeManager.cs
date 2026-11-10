using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace Sumthing.Services;

/// <summary>
/// Builds the palette in code and swaps it into Application.Resources (every control uses DynamicResource,
/// so switching is instant). Three looks: modern Light, modern Dark, and Classic (the Windows 7 / 8 calculator).
/// </summary>
public static class ThemeManager
{
    public static bool IsDark { get; private set; }
    public static bool IsClassic { get; private set; }

    public static readonly (string Name, Color Light, Color Dark)[] Accents =
    {
        ("Ember",    C("#C2410C"), C("#FF9B61")),
        ("Ocean",    C("#0B6FB8"), C("#5EB3F0")),
        ("Teal",     C("#0E7C7E"), C("#4CC7C9")),
        ("Forest",   C("#2E7D32"), C("#7CCB80")),
        ("Violet",   C("#6D4AD6"), C("#B3A1FA")),
        ("Rose",     C("#B5245E"), C("#F07AA6")),
        ("Graphite", C("#4A5568"), C("#A9B4C6")),
    };

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
        IsClassic = s.Theme == ThemeMode.Classic;
        IsDark = s.Theme switch
        {
            ThemeMode.Dark => true,
            ThemeMode.Light or ThemeMode.Classic => false,
            _ => SystemUsesDark(),
        };

        var d = new ResourceDictionary();
        void B(string key, Color c) { var b = new SolidColorBrush(c); b.Freeze(); d[key] = b; d[key + "Color"] = c; }
        void G(string key, string top, string bottom)
        {
            var g = new LinearGradientBrush(C(top), C(bottom), 90);
            g.Freeze();
            d[key] = g;
            d[key + "Color"] = C(bottom);
        }

        Color acc;
        if (IsClassic)
        {
            acc = C("#3C7FB1");
            // ---- Windows 7 / 8 calculator ----
            G("WindowBg", "#DFE9F5", "#D3DEEC");
            B("ChromeBg", C("#F0F4FA"));
            B("RibbonBg", C("#F5F8FC"));
            B("RibbonBorder", C("#A9B8CC"));
            B("Text", C("#1E2B3C"));
            B("TextSecondary", C("#4D5C70"));
            B("TextDisabled", C("#9AA5B4"));
            B("ControlBg", C("#00FFFFFF"));
            B("ControlHoverBg", C("#33FFFFFF"));
            B("ControlPressedBg", C("#22000000"));
            B("ControlBorder", C("#8E9DB2"));
            B("InputBg", C("#FFFFFF"));
            B("InputBorder", C("#8E9DB2"));
            G("ButtonBg", "#FBFCFE", "#DDE6F1");
            G("ButtonHoverBg", "#FFF7E3", "#FCD98F");
            B("Divider", C("#B8C5D6"));
            B("MenuBg", C("#F5F7FB"));
            B("MenuHover", C("#D5E6F7"));
            B("ToolTipBg", C("#FFFFFF"));
            B("ScrollThumb", C("#A2AEBE"));
            B("ScrollThumbHover", C("#7F8DA1"));
            B("DialogBg", C("#EAF0F8"));
            B("CardBg", C("#F7FAFD"));
            B("DangerText", C("#B3261E"));

            // keypad
            G("KeyNumBg", "#FBFCFE", "#DDE6F1");
            G("KeyNumHover", "#FFF7E3", "#FCD98F");
            G("KeyNumPressed", "#FCE1A6", "#F5B652");
            d["KeyFnBg"] = d["KeyNumBg"]; d["KeyFnHover"] = d["KeyNumHover"]; d["KeyFnPressed"] = d["KeyNumPressed"];
            d["KeyOpBg"] = d["KeyNumBg"]; d["KeyOpHover"] = d["KeyNumHover"]; d["KeyOpPressed"] = d["KeyNumPressed"];
            d["KeyEqBg"] = d["KeyNumBg"]; d["KeyEqHover"] = d["KeyNumHover"]; d["KeyEqPressed"] = d["KeyNumPressed"];
            B("KeyBorder", C("#8E9DB2"));
            B("KeyText", C("#1E2B3C"));
            B("KeyEqText", C("#1E2B3C"));
            d["KeyRadius"] = new CornerRadius(3);
            d["KeyGap"] = new Thickness(3);
            d["KeyFontSize"] = 13.0;
            d["KeyNumFontSize"] = 13.0;
            d["KeyNumWeight"] = FontWeights.Normal;
            d["KeyMemHeight"] = 27.0;

            // display: the white-to-blue "LCD" box
            G("DisplayBg", "#FFFFFF", "#DCE7F3");
            B("DisplayBorder", C("#8E9DB2"));
            B("DisplayText", C("#000000"));
            B("DisplaySubText", C("#4D5C70"));
            d["DisplayFont"] = new FontFamily("Consolas, Lucida Console");
            B("PanelBg", C("#EEF3F9"));
            B("TapeBg", C("#FFFEF6"));
            B("TapeText", C("#26303D"));
            B("TapeLine", C("#C9C2A8"));
        }
        else if (!IsDark)
        {
            var accent = Pick(s.Accent);
            acc = accent.Light;
            B("WindowBg", C("#F3F3F3"));
            B("ChromeBg", C("#F3F3F3"));
            B("RibbonBg", C("#FBFBFB"));
            B("RibbonBorder", C("#E3E3E3"));
            B("Text", C("#1B1B1B"));
            B("TextSecondary", C("#5C5C5C"));
            B("TextDisabled", C("#A0A0A0"));
            B("ControlBg", C("#00FFFFFF"));
            B("ControlHoverBg", C("#0F000000"));
            B("ControlPressedBg", C("#1A000000"));
            B("ControlBorder", C("#D6D6D6"));
            B("InputBg", C("#FFFFFF"));
            B("InputBorder", C("#C8C8C8"));
            B("ButtonBg", C("#FFFFFF"));
            B("ButtonHoverBg", C("#F6F6F6"));
            B("Divider", C("#E0E0E0"));
            B("MenuBg", C("#FCFCFC"));
            B("MenuHover", C("#EDEDED"));
            B("ToolTipBg", C("#FAFAFA"));
            B("ScrollThumb", C("#8A8A8A"));
            B("ScrollThumbHover", C("#5E5E5E"));
            B("DialogBg", C("#F9F9F9"));
            B("CardBg", C("#FFFFFF"));
            B("DangerText", C("#C42B1C"));

            B("KeyNumBg", C("#FFFFFF"));
            B("KeyNumHover", C("#F6F6F6"));
            B("KeyNumPressed", C("#EBEBEB"));
            B("KeyFnBg", C("#F9F9F9"));
            B("KeyFnHover", C("#F0F0F0"));
            B("KeyFnPressed", C("#E6E6E6"));
            d["KeyOpBg"] = d["KeyFnBg"]; d["KeyOpHover"] = d["KeyFnHover"]; d["KeyOpPressed"] = d["KeyFnPressed"];
            B("KeyBorder", C("#E5E5E5"));
            B("KeyText", C("#1B1B1B"));
            B("DisplayText", C("#1B1B1B"));
            B("DisplaySubText", C("#5C5C5C"));
            B("PanelBg", C("#EEEEEE"));
            B("TapeBg", C("#FFFDF5"));
            B("TapeText", C("#2A2A2A"));
            B("TapeLine", C("#D8D1BC"));
        }
        else
        {
            var accent = Pick(s.Accent);
            acc = accent.Dark;
            B("WindowBg", C("#202020"));
            B("ChromeBg", C("#202020"));
            B("RibbonBg", C("#2B2B2B"));
            B("RibbonBorder", C("#3A3A3A"));
            B("Text", C("#F2F2F2"));
            B("TextSecondary", C("#BDBDBD"));
            B("TextDisabled", C("#6E6E6E"));
            B("ControlBg", C("#00FFFFFF"));
            B("ControlHoverBg", C("#18FFFFFF"));
            B("ControlPressedBg", C("#26FFFFFF"));
            B("ControlBorder", C("#4A4A4A"));
            B("InputBg", C("#1C1C1C"));
            B("InputBorder", C("#555555"));
            B("ButtonBg", C("#373737"));
            B("ButtonHoverBg", C("#424242"));
            B("Divider", C("#3A3A3A"));
            B("MenuBg", C("#2C2C2C"));
            B("MenuHover", C("#3D3D3D"));
            B("ToolTipBg", C("#2C2C2C"));
            B("ScrollThumb", C("#8A8A8A"));
            B("ScrollThumbHover", C("#BBBBBB"));
            B("DialogBg", C("#262626"));
            B("CardBg", C("#2E2E2E"));
            B("DangerText", C("#FF99A4"));

            B("KeyNumBg", C("#3B3B3B"));
            B("KeyNumHover", C("#454545"));
            B("KeyNumPressed", C("#333333"));
            B("KeyFnBg", C("#2F2F2F"));
            B("KeyFnHover", C("#393939"));
            B("KeyFnPressed", C("#2A2A2A"));
            d["KeyOpBg"] = d["KeyFnBg"]; d["KeyOpHover"] = d["KeyFnHover"]; d["KeyOpPressed"] = d["KeyFnPressed"];
            B("KeyBorder", C("#454545"));
            B("KeyText", C("#FFFFFF"));
            B("DisplayText", C("#FFFFFF"));
            B("DisplaySubText", C("#C8C8C8"));
            B("PanelBg", C("#2B2B2B"));
            B("TapeBg", C("#262521"));
            B("TapeText", C("#ECE7DA"));
            B("TapeLine", C("#4A463C"));
        }

        if (!IsClassic)
        {
            B("KeyEqBg", acc);
            B("KeyEqHover", Blend(acc, IsDark ? Colors.Black : Colors.White, 0.12));
            B("KeyEqPressed", Blend(acc, IsDark ? Colors.Black : Colors.White, 0.24));
            B("KeyEqText", IsDark ? C("#111111") : Colors.White);
            d["KeyRadius"] = new CornerRadius(6);
            d["KeyGap"] = new Thickness(1.5);
            d["KeyFontSize"] = 15.0;
            d["KeyNumFontSize"] = 20.0;
            d["KeyNumWeight"] = FontWeights.SemiBold;
            d["KeyMemHeight"] = 34.0;
            d["DisplayFont"] = new FontFamily("Segoe UI Variable Display, Segoe UI");
            B("DisplayBg", Colors.Transparent);
            B("DisplayBorder", Colors.Transparent);
        }

        if (IsClassic)
        {
            d["KeyMemBg"] = d["KeyNumBg"]; d["KeyMemHover"] = d["KeyNumHover"]; d["KeyMemPressed"] = d["KeyNumPressed"];
            d["KeyMemBorder"] = d["KeyBorder"];
        }
        else
        {
            B("KeyMemBg", Colors.Transparent);
            B("KeyMemHover", IsDark ? C("#18FFFFFF") : C("#0F000000"));
            B("KeyMemPressed", IsDark ? C("#26FFFFFF") : C("#1A000000"));
            B("KeyMemBorder", Colors.Transparent);
        }

        B("Accent", acc);
        B("AccentHover", Blend(acc, IsDark ? Colors.White : Colors.Black, 0.12));
        B("AccentText", IsDark ? C("#111111") : Colors.White);
        B("AccentSoft", Color.FromArgb((byte)(IsDark ? 0x40 : 0x26), acc.R, acc.G, acc.B));
        B("AccentSoftHover", Color.FromArgb((byte)(IsDark ? 0x55 : 0x38), acc.R, acc.G, acc.B));
        B("Selection", Color.FromArgb(0x66, acc.R, acc.G, acc.B));
        B("FocusRing", acc);

        var app = Application.Current;
        if (_current != null) app.Resources.MergedDictionaries.Remove(_current);
        app.Resources.MergedDictionaries.Insert(0, d);
        _current = d;
        // Fonts live in Application.Resources itself so they win over Controls.xaml's defaults.
        app.Resources["UiFont"] = new FontFamily(IsClassic ? "Segoe UI" : "Segoe UI Variable Text, Segoe UI");
        app.Resources["UiFontSize"] = IsClassic ? 12.5 : 14.0;

        foreach (Window w in app.Windows) ApplyTitleBar(w);
        ThemeChanged?.Invoke();
    }

    private static (string Name, Color Light, Color Dark) Pick(string name)
    {
        var a = Accents.FirstOrDefault(x => x.Name == name);
        return a.Name == null ? Accents[0] : a;
    }

    // ---------- native dark title bar ----------
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static void ApplyTitleBar(Window w)
    {
        var hwnd = new WindowInteropHelper(w).Handle;
        if (hwnd == IntPtr.Zero) return;
        int on = IsDark ? 1 : 0;
        if (DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)) != 0)
            DwmSetWindowAttribute(hwnd, 19, ref on, sizeof(int));
        // Windows 11: classic look gets square corners, modern looks get the round ones
        int corner = IsClassic ? 1 : 0; // DWMWCP_DONOTROUND : DWMWCP_DEFAULT
        DwmSetWindowAttribute(hwnd, 33, ref corner, sizeof(int));
        if (w.IsVisible && Environment.OSVersion.Version.Build < 22000 && w.WindowState == WindowState.Normal)
        {
            w.Width += 1; w.Width -= 1;
        }
    }

    public static void Attach(Window w) => w.SourceInitialized += (_, _) => ApplyTitleBar(w);

    public static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    public static Color Blend(Color a, Color b, double t) => Color.FromArgb(a.A,
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
}
