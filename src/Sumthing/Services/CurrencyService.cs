using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace Sumthing.Services;

/// <summary>
/// Exchange rates from the Frankfurter API (European Central Bank reference rates, free, no key).
/// Rates are cached in %APPDATA%\Sumthing\currency.json, so the converter works offline with the last rates it saw.
/// </summary>
public static class CurrencyService
{
    // Declared first: static fields initialise top to bottom and Names copies this.
    private static readonly Dictionary<string, string> DefaultNames = new()
    {
        ["AUD"] = "Australian Dollar", ["BGN"] = "Bulgarian Lev", ["BRL"] = "Brazilian Real", ["CAD"] = "Canadian Dollar",
        ["CHF"] = "Swiss Franc", ["CNY"] = "Chinese Renminbi Yuan", ["CZK"] = "Czech Koruna", ["DKK"] = "Danish Krone",
        ["EUR"] = "Euro", ["GBP"] = "British Pound", ["HKD"] = "Hong Kong Dollar", ["HUF"] = "Hungarian Forint",
        ["IDR"] = "Indonesian Rupiah", ["ILS"] = "Israeli New Sheqel", ["INR"] = "Indian Rupee", ["ISK"] = "Icelandic Króna",
        ["JPY"] = "Japanese Yen", ["KRW"] = "South Korean Won", ["MXN"] = "Mexican Peso", ["MYR"] = "Malaysian Ringgit",
        ["NOK"] = "Norwegian Krone", ["NZD"] = "New Zealand Dollar", ["PHP"] = "Philippine Peso", ["PLN"] = "Polish Złoty",
        ["RON"] = "Romanian Leu", ["SEK"] = "Swedish Krona", ["SGD"] = "Singapore Dollar", ["THB"] = "Thai Baht",
        ["TRY"] = "Turkish Lira", ["USD"] = "United States Dollar", ["ZAR"] = "South African Rand",
    };

    /// <summary>Units per 1 euro (EUR itself is 1).</summary>
    public static Dictionary<string, decimal> Rates { get; private set; } = new();
    public static Dictionary<string, string> Names { get; private set; } = new(DefaultNames);
    /// <summary>The date the central bank published these rates.</summary>
    public static string RatesDate { get; private set; } = "";
    public static DateTime FetchedUtc { get; private set; } = DateTime.MinValue;
    public static bool HasRates => Rates.Count > 1;

    public static event Action Updated;

    private static string CachePath => Path.Combine(SettingsService.Folder, "currency.json");

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Sumthing/" + AppInfo.Version + " (+https://github.com/adamglassner/sumthing)");
        return c;
    }

    private sealed class CacheFile
    {
        public string Date { get; set; } = "";
        public DateTime FetchedUtc { get; set; }
        public Dictionary<string, decimal> Rates { get; set; } = new();
        public Dictionary<string, string> Names { get; set; } = new();
    }

    public static void LoadCache()
    {
        try
        {
            if (!File.Exists(CachePath)) return;
            var c = JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(CachePath));
            if (c?.Rates == null || c.Rates.Count < 2) return;
            Rates = c.Rates;
            RatesDate = c.Date ?? "";
            FetchedUtc = c.FetchedUtc;
            if (c.Names != null) foreach (var kv in c.Names) Names[kv.Key] = kv.Value;
        }
        catch { }
    }

    /// <summary>True when the cached rates are older than a few hours (or missing).</summary>
    public static bool IsStale => !HasRates || DateTime.UtcNow - FetchedUtc > TimeSpan.FromHours(6);

    private static Task<bool> _running;

    /// <summary>Downloads the latest rates. Returns false (and keeps the old ones) when offline.</summary>
    public static Task<bool> RefreshAsync()
    {
        if (_running != null && !_running.IsCompleted) return _running;
        _running = DoRefreshAsync();
        return _running;
    }

    private static async Task<bool> DoRefreshAsync()
    {
        string[] latest = { "https://api.frankfurter.dev/v1/latest", "https://api.frankfurter.app/latest" };
        string[] names = { "https://api.frankfurter.dev/v1/currencies", "https://api.frankfurter.app/currencies" };
        bool ok = false;
        foreach (var url in latest)
        {
            try
            {
                var json = await Http.GetStringAsync(url).ConfigureAwait(true);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var rates = new Dictionary<string, decimal> { ["EUR"] = 1m };
                string baseCode = root.TryGetProperty("base", out var b) ? b.GetString() ?? "EUR" : "EUR";
                foreach (var p in root.GetProperty("rates").EnumerateObject())
                    rates[p.Name] = p.Value.GetDecimal();
                if (baseCode != "EUR" && rates.TryGetValue("EUR", out var eur) && eur != 0)
                {
                    // rebase to EUR = 1
                    var rebased = new Dictionary<string, decimal>();
                    foreach (var kv in rates) rebased[kv.Key] = kv.Value / eur;
                    rebased[baseCode] = 1m / eur;
                    rates = rebased;
                }
                Rates = rates;
                RatesDate = root.TryGetProperty("date", out var d) ? d.GetString() ?? "" : "";
                FetchedUtc = DateTime.UtcNow;
                ok = true;
                break;
            }
            catch { /* try the next address */ }
        }
        if (!ok) return false;

        foreach (var url in names)
        {
            try
            {
                var json = await Http.GetStringAsync(url).ConfigureAwait(true);
                var n = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (n != null) foreach (var kv in n) Names[kv.Key] = kv.Value;
                break;
            }
            catch { }
        }
        SaveCache();
        Updated?.Invoke();
        return true;
    }

    /// <summary>Used by the self-test so it never depends on the network.</summary>
    public static void UseRates(Dictionary<string, decimal> rates, string date)
    {
        Rates = rates;
        RatesDate = date;
        FetchedUtc = DateTime.UtcNow;
        Updated?.Invoke();
    }

    private static void SaveCache()
    {
        try
        {
            Directory.CreateDirectory(SettingsService.Folder);
            File.WriteAllText(CachePath, JsonSerializer.Serialize(new CacheFile { Date = RatesDate, FetchedUtc = FetchedUtc, Rates = Rates, Names = Names }));
        }
        catch { }
    }

    public static decimal Convert(decimal amount, string from, string to)
    {
        if (from == to) return amount;
        if (!Rates.TryGetValue(from, out var f) || !Rates.TryGetValue(to, out var t) || f == 0)
            throw new InvalidOperationException("No rate for that currency");
        return amount / f * t;
    }

    public static string NameOf(string code) => Names.TryGetValue(code, out var n) ? n : code;

    /// <summary>Codes ordered with the everyday ones first.</summary>
    public static List<string> Codes()
    {
        var popular = new[] { "USD", "EUR", "GBP", "CAD", "AUD", "JPY", "MXN", "CHF", "CNY", "INR" };
        var all = Rates.Keys.ToList();
        var list = popular.Where(all.Contains).ToList();
        list.AddRange(all.Where(c => !popular.Contains(c)).OrderBy(c => c));
        return list;
    }
}
