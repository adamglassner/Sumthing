using System.Windows;
using System.Windows.Controls;
using Sumthing.Engine;
using Sumthing.Services;

namespace Sumthing.Views;

/// <summary>Unit and currency converter.</summary>
public partial class ConverterView : UserControl
{
    private UnitCategory _cat;
    private bool _busy;

    public sealed class Row
    {
        public string Name { get; init; }
        public string Value { get; init; }
    }

    public ConverterView()
    {
        InitializeComponent();
        CategoryBox.ItemsSource = Units.Categories;
        var saved = Units.Categories.FirstOrDefault(c => c.Name == SettingsService.Current.ConverterCategory) ?? Units.Categories[1];
        CategoryBox.SelectedItem = saved;
        CurrencyService.Updated += () => Dispatcher.Invoke(() => { if (_cat?.IsCurrency == true) FillUnits(keep: true); });
    }

    public void OnShown()
    {
        if (_cat?.IsCurrency == true && CurrencyService.IsStale && !App.IsSelfTest) _ = RefreshRatesAsync();
    }

    /// <summary>Picks a category by name (used by the self-test).</summary>
    public void SelectCategory(string name) =>
        CategoryBox.SelectedItem = Units.Categories.First(c => c.Name == name);

    public void SetUnits(int from, int to)
    {
        FromUnit.SelectedIndex = from;
        ToUnit.SelectedIndex = to;
        Convert();
    }

    public void SetCurrencies(string from, string to)
    {
        FromUnit.SelectedIndex = CurrencyCodes.IndexOf(from);
        ToUnit.SelectedIndex = CurrencyCodes.IndexOf(to);
        Convert();
    }

    public void SetAmount(string text) => FromValue.Text = text;
    public string ResultText => ToValue.Text;

    private List<string> CurrencyCodes = new();

    private void Category_Changed(object sender, SelectionChangedEventArgs e)
    {
        _cat = CategoryBox.SelectedItem as UnitCategory;
        if (_cat == null) return;
        SettingsService.Current.ConverterCategory = _cat.Name;
        CurrencyBar.Visibility = _cat.IsCurrency ? Visibility.Visible : Visibility.Collapsed;
        FillUnits(keep: false);
        if (_cat.IsCurrency && IsVisible) OnShown();
    }

    private void FillUnits(bool keep)
    {
        _busy = true;
        try
        {
            if (_cat.IsCurrency)
            {
                string from = keep && FromUnit.SelectedIndex >= 0 && FromUnit.SelectedIndex < CurrencyCodes.Count ? CurrencyCodes[FromUnit.SelectedIndex] : SettingsService.Current.CurrencyFrom;
                string to = keep && ToUnit.SelectedIndex >= 0 && ToUnit.SelectedIndex < CurrencyCodes.Count ? CurrencyCodes[ToUnit.SelectedIndex] : SettingsService.Current.CurrencyTo;
                CurrencyCodes = CurrencyService.Codes();
                var names = CurrencyCodes.Select(c => $"{c} - {CurrencyService.NameOf(c)}").ToList();
                FromUnit.ItemsSource = names;
                ToUnit.ItemsSource = names;
                FromUnit.SelectedIndex = Math.Max(0, CurrencyCodes.IndexOf(from));
                ToUnit.SelectedIndex = Math.Max(0, CurrencyCodes.IndexOf(to));
                if (CurrencyCodes.Count > 1 && FromUnit.SelectedIndex == ToUnit.SelectedIndex) ToUnit.SelectedIndex = FromUnit.SelectedIndex == 0 ? 1 : 0;
                UpdateRatesStatus();
            }
            else
            {
                FromUnit.ItemsSource = _cat.Units;
                ToUnit.ItemsSource = _cat.Units;
                FromUnit.SelectedIndex = _cat.DefaultFrom;
                ToUnit.SelectedIndex = _cat.DefaultTo;
            }
        }
        finally { _busy = false; }
        Convert();
    }

    private void Unit_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_busy) return;
        if (_cat?.IsCurrency == true)
        {
            if (FromUnit.SelectedIndex >= 0 && FromUnit.SelectedIndex < CurrencyCodes.Count) SettingsService.Current.CurrencyFrom = CurrencyCodes[FromUnit.SelectedIndex];
            if (ToUnit.SelectedIndex >= 0 && ToUnit.SelectedIndex < CurrencyCodes.Count) SettingsService.Current.CurrencyTo = CurrencyCodes[ToUnit.SelectedIndex];
        }
        Convert();
    }

    private void Value_Changed(object sender, TextChangedEventArgs e) => Convert();

    private void Swap_Click(object sender, RoutedEventArgs e)
    {
        _busy = true;
        int f = FromUnit.SelectedIndex;
        FromUnit.SelectedIndex = ToUnit.SelectedIndex;
        ToUnit.SelectedIndex = f;
        _busy = false;
        Unit_Changed(null, null);
    }

    private void CopyResult_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(ToValue.Text.Replace(",", "")); } catch { }
    }

    private void Convert()
    {
        if (_cat == null || FromUnit == null || ToValue == null) return;
        var rows = new List<Row>();
        if (!TryReadAmount(out decimal amount))
        {
            ToValue.Text = "—";
            RateLine.Text = "Type a number to convert.";
            AllList.ItemsSource = rows;
            return;
        }

        if (_cat.IsCurrency)
        {
            if (!CurrencyService.HasRates || FromUnit.SelectedIndex < 0 || ToUnit.SelectedIndex < 0)
            {
                ToValue.Text = "—";
                RateLine.Text = "No exchange rates yet. Connect to the internet and choose Update rates.";
                AllList.ItemsSource = rows;
                return;
            }
            string from = CurrencyCodes[FromUnit.SelectedIndex], to = CurrencyCodes[ToUnit.SelectedIndex];
            decimal r = CurrencyService.Convert(amount, from, to);
            ToValue.Text = Money(r) + " " + to;
            RateLine.Text = $"1 {from} = {Num.Format(CurrencyService.Convert(1, from, to), 6)} {to}";
            foreach (var code in CurrencyCodes)
                if (code != from)
                    rows.Add(new Row { Name = $"{code}  {CurrencyService.NameOf(code)}", Value = Money(CurrencyService.Convert(amount, from, code)) });
        }
        else
        {
            if (FromUnit.SelectedItem is not Unit uf || ToUnit.SelectedItem is not Unit ut) return;
            double v = (double)amount;
            double r = Units.Convert(_cat, uf, ut, v);
            ToValue.Text = Num.Format(r, 10) + " " + ut.Symbol;
            double one = Units.Convert(_cat, uf, ut, 1);
            RateLine.Text = _cat.IsTemperature || uf.Factor < 0 || ut.Factor < 0 ? "" : $"1 {uf.Symbol} = {Num.Format(one, 10)} {ut.Symbol}";
            foreach (var u in _cat.Units)
                if (u != uf)
                    rows.Add(new Row { Name = u.Name, Value = Num.Format(Units.Convert(_cat, uf, u, v), 10) + " " + u.Symbol });
        }
        AllList.ItemsSource = rows;
    }

    private bool TryReadAmount(out decimal amount)
    {
        string t = FromValue.Text?.Trim() ?? "";
        if (Num.TryParse(t, out amount)) return true;
        // let people type a little sum: 12 * 3.5
        var r = SumSheet.Evaluate(new[] { t });
        if (r.Count == 1 && r[0].Value is decimal d) { amount = d; return true; }
        return false;
    }

    private static string Money(decimal v)
    {
        decimal a = Math.Abs(v);
        int dp = a >= 1 ? 2 : a >= 0.01m ? 4 : 6;
        string s = Math.Round(v, dp, MidpointRounding.AwayFromZero).ToString("0." + new string('0', dp == 2 ? 2 : 0) + new string('#', dp == 2 ? 0 : dp), Num.Inv);
        return Num.Grouping ? Num.Group(s) : s;
    }

    private void UpdateRatesStatus()
    {
        if (!CurrencyService.HasRates) RatesStatus.Text = "No rates downloaded yet.";
        else
        {
            string date = DateTime.TryParse(CurrencyService.RatesDate, out var d) ? d.ToString("MMMM d, yyyy") : CurrencyService.RatesDate;
            RatesStatus.Text = $"European Central Bank rates for {date}. Rates are updated once each working day.";
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshRatesAsync();

    private async Task RefreshRatesAsync()
    {
        RefreshButton.IsEnabled = false;
        RatesStatus.Text = "Getting the latest rates…";
        bool ok = await CurrencyService.RefreshAsync();
        RefreshButton.IsEnabled = true;
        UpdateRatesStatus();
        if (!ok) RatesStatus.Text = (CurrencyService.HasRates ? RatesStatus.Text + " " : "") + "Couldn't reach the rates service; check your internet connection.";
        else if (_cat?.IsCurrency == true) FillUnits(keep: true);
    }
}
