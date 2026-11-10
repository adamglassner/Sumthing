using System.IO;
using System.Windows;
using System.Windows.Threading;
using Sumthing.Engine;
using Sumthing.Services;

namespace Sumthing;

public partial class App : Application
{
    /// <summary>Set by "--self-test &lt;output.txt&gt; [screenshot folder]" (used by the build to test Sumthing).</summary>
    public static string SelfTestOutput { get; private set; }
    public static string SelfTestShots { get; private set; }
    public static bool IsSelfTest => SelfTestOutput != null;
    internal static string TestName = "(starting)";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;

        int st = Array.FindIndex(e.Args, a => a.Equals("--self-test", StringComparison.OrdinalIgnoreCase));
        if (st >= 0)
        {
            SelfTestOutput = st + 1 < e.Args.Length ? Path.GetFullPath(e.Args[st + 1]) : Path.Combine(Path.GetTempPath(), "sumthing-selftest.txt");
            SelfTestShots = st + 2 < e.Args.Length ? Path.GetFullPath(e.Args[st + 2]) : null;
            // Never touch the real settings while testing.
            SettingsService.Folder = Path.Combine(Path.GetTempPath(), "SumthingSelfTest-" + Guid.NewGuid().ToString("N")[..8]);
        }

        SettingsService.Load();
        Num.Grouping = SettingsService.Current.ThousandsSeparators;
        ThemeManager.Apply(SettingsService.Current);
        SettingsService.Changed += () =>
        {
            Num.Grouping = SettingsService.Current.ThousandsSeparators;
            ThemeManager.Apply(SettingsService.Current);
        };
        CurrencyService.LoadCache();

        var win = new MainWindow();
        MainWindow = win;
        win.Show();
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (e.Exception is ResourceReferenceKeyNotFoundException rk)
        {
            if (IsSelfTest)
                try { File.AppendAllText(SelfTestOutput, $"FAIL missing resource '{rk.Key}' during \"{TestName}\"\r\n"); } catch { }
            e.Handled = true;
            return;
        }
        if (IsSelfTest)
        {
            try { File.AppendAllText(SelfTestOutput, $"FAIL unhandled exception during \"{TestName}\": {e.Exception}\r\n"); } catch { }
            e.Handled = true;
            Environment.Exit(99);
            return;
        }
        try
        {
            Directory.CreateDirectory(SettingsService.Folder);
            File.AppendAllText(Path.Combine(SettingsService.Folder, "error.log"), $"[{DateTime.Now:u}] {e.Exception}\r\n\r\n");
        }
        catch { }
        MessageBox.Show(MainWindow, "Sumthing hit an unexpected error:\n\n" + e.Exception.Message, "Sumthing",
            MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
