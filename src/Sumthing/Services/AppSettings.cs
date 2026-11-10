using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sumthing.Engine;

namespace Sumthing.Services;

public enum ThemeMode { System, Light, Dark, Classic }
public enum CalcMode { Standard, Sums, Converter, Tools, Settings }

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

    // ---- Window ----
    public double WinLeft { get; set; } = double.NaN;
    public double WinTop { get; set; } = double.NaN;
    public double WinWidth { get; set; } = 360;
    public double WinHeight { get; set; } = 600;
    public double ClassicWidth { get; set; } = 264;
    public double ClassicHeight { get; set; } = 380;

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
        if (s.Mode == CalcMode.Settings) s.Mode = CalcMode.Standard;
        if (s.History.Count > 100) s.History.RemoveRange(100, s.History.Count - 100);
        if (s.Tape.Count > 2000) s.Tape.RemoveRange(0, s.Tape.Count - 2000);
        if (double.IsNaN(s.WinWidth) || s.WinWidth < 320) s.WinWidth = 360;
        if (double.IsNaN(s.WinHeight) || s.WinHeight < 480) s.WinHeight = 600;
        if (double.IsNaN(s.ClassicWidth) || s.ClassicWidth < 240) s.ClassicWidth = 264;
        if (double.IsNaN(s.ClassicHeight) || s.ClassicHeight < 330) s.ClassicHeight = 380;
        s.ToolTab = Math.Clamp(s.ToolTab, 0, 3);
    }
}
