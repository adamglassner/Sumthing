using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sumthing.Engine;
using Sumthing.Maths;

namespace Sumthing.Services;

/// <summary>App colours. "Classic" is only read from 1.0 settings (it becomes Light + the Classic keypad).</summary>
public enum ThemeMode { System, Light, Dark, Classic }
public enum KeypadStyle { Modern, Classic }
public enum CalcMode { Standard, Sums, Converter, Tools, Settings, Scientific, Graphing, Statistics }

public static class AppInfo
{
    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "1.0.0";
}

/// <summary>All preferences plus the things Sumthing keeps between sessions. Saved in %APPDATA%\Sumthing\settings.json.</summary>
public sealed class AppSettings
{
    // ---- Appearance ----
    public ThemeMode Theme { get; set; } = ThemeMode.System;
    public string Accent { get; set; } = "Ember";
    public KeypadStyle KeypadStyle { get; set; } = KeypadStyle.Modern;
    public bool SidebarCollapsed { get; set; }
    public bool CompactMode { get; set; }

    // ---- Behaviour ----
    public bool ThousandsSeparators { get; set; } = true;
    public bool KeepBetweenSessions { get; set; } = true;
    public bool AlwaysOnTop { get; set; } = false;
    public bool ShowTape { get; set; } = false;
    public CalcMode Mode { get; set; } = CalcMode.Standard;
    public int ToolTab { get; set; } = 0;

    // ---- Converter ----
    public string ConverterCategory { get; set; } = "Length";
    public string CurrencyFrom { get; set; } = "USD";
    public string CurrencyTo { get; set; } = "EUR";

    // ---- Kept between sessions ----
    public List<HistoryItem> History { get; set; } = new();
    public List<TapeEntry> Tape { get; set; } = new();
    public decimal? Memory { get; set; }
    public string SumSheet { get; set; } = DefaultSheet;
    public string RightPanel { get; set; } = "Tape";

    // ---- Scientific / Graphing / Statistics (shared like one TI's memory) ----
    public bool AngleDegrees { get; set; }
    public double Ans { get; set; }
    public Dictionary<string, double> StoredVariables { get; set; } = new();
    public Dictionary<string, double[]> Lists { get; set; } = new();
    public List<HomeEntry> HomeHistory { get; set; } = new();
    public List<GraphRow> GraphRows { get; set; } = new();
    public GraphWindow GraphWindow { get; set; }
    public Dictionary<string, SliderInfo> Sliders { get; set; } = new();

    public static List<GraphRow> DefaultGraphRows() => new()
    {
        new GraphRow { Text = "x²−2x−3", ColorIndex = 0 },
        new GraphRow { Text = "2sin(x)", ColorIndex = 1 },
        new GraphRow { Text = "", ColorIndex = 4 },
    };

    // ---- Window ----
    public double WinLeft { get; set; } = double.NaN;
    public double WinTop { get; set; } = double.NaN;
    public double WinWidth { get; set; } = 1280;
    public double WinHeight { get; set; } = 820;
    public bool WinMaximized { get; set; }
    public double CompactLeft { get; set; } = double.NaN;
    public double CompactTop { get; set; } = double.NaN;
    /// <summary>Settings from Sumthing 1.x had a small window; 2.0 starts large.</summary>
    public int SettingsVersion { get; set; }

    public const string DefaultSheet =
        "# Try typing a sum on each line\n" +
        "Rent = 1,250\n" +
        "Groceries: 85 + 42.50 + 61\n" +
        "Phone = 45\n" +
        "total\n" +
        "\n" +
        "# Percentages\n" +
        "12% of 340\n" +
        "80 - 25%\n" +
        "Rent * 12 + 3%\n";
}

public static class SettingsService
{
    /// <summary>%APPDATA%\Sumthing. The self-test points this at a temporary folder.</summary>
    public static string Folder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sumthing");

    public static string FilePath => Path.Combine(Folder, "settings.json");

    public static AppSettings Current { get; private set; } = new();

    public static event Action Changed;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public static void Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options);
                if (s != null) Current = s;
            }
        }
        catch
        {
            try { File.Copy(FilePath, FilePath + ".bad", true); } catch { }
            Current = new AppSettings();
        }
        Sanitize(Current);
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Current, Options));
            File.Move(tmp, FilePath, true);
        }
        catch { /* best effort */ }
    }

    /// <summary>Save and tell the window to re-apply settings.</summary>
    public static void Commit()
    {
        Sanitize(Current);
        Save();
        Changed?.Invoke();
    }

    private static void Sanitize(AppSettings s)
    {
        s.History ??= new();
        s.Tape ??= new();
        s.SumSheet ??= AppSettings.DefaultSheet;
        s.StoredVariables ??= new();
        s.Lists ??= new();
        s.HomeHistory ??= new();
        s.GraphRows ??= new();
        s.Sliders ??= new();
        // 1.x had "Classic" as a theme; in 2.0 it's a keypad style on top of Light
        if (s.Theme == ThemeMode.Classic) { s.Theme = ThemeMode.Light; s.KeypadStyle = KeypadStyle.Classic; }
        if (s.SettingsVersion < 2)
        {
            s.WinWidth = 1280; s.WinHeight = 820;
            s.SettingsVersion = 2;
        }
        if (s.Mode == CalcMode.Settings) s.Mode = CalcMode.Standard;
        if (s.History.Count > 100) s.History.RemoveRange(100, s.History.Count - 100);
        if (s.Tape.Count > 2000) s.Tape.RemoveRange(0, s.Tape.Count - 2000);
        if (double.IsNaN(s.WinWidth) || s.WinWidth < 900) s.WinWidth = 1280;
        if (double.IsNaN(s.WinHeight) || s.WinHeight < 600) s.WinHeight = 820;
        if (s.GraphWindow != null && (!(s.GraphWindow.XMax > s.GraphWindow.XMin) || !(s.GraphWindow.YMax > s.GraphWindow.YMin))) s.GraphWindow = null;
        s.ToolTab = Math.Clamp(s.ToolTab, 0, 3);
    }
}
