using System.Windows;
using System.Windows.Controls;
using Sumthing.Engine;
using Sumthing.Services;

namespace Sumthing.Views;

/// <summary>Everyday tools: tip &amp; split, percent change, discount &amp; tax, date difference.</summary>
public partial class ToolsView : UserControl
{
    public event Action<decimal, string> SendToCalculator;

    private bool _ready;

    public ToolsView()
    {
        InitializeComponent();
        DateFrom.SelectedDate = DateTime.Today;
        DateTo.SelectedDate = new DateTime(DateTime.Today.Year, 12, 25) < DateTime.Today
            ? new DateTime(DateTime.Today.Year + 1, 12, 25)
            : new DateTime(DateTime.Today.Year, 12, 25);
        _ready = true;
        ShowTab(SettingsService.Current.ToolTab);
    }

    public int Tab { get; private set; }

    public void ShowTab(int tab)
    {
        Tab = Math.Clamp(tab, 0, 3);
        SettingsService.Current.ToolTab = Tab;
        TipPanel.Visibility = Tab == 0 ? Visibility.Visible : Visibility.Collapsed;
        PercentPanel.Visibility = Tab == 1 ? Visibility.Visible : Visibility.Collapsed;
        DiscountPanel.Visibility = Tab == 2 ? Visibility.Visible : Visibility.Collapsed;
        DatesPanel.Visibility = Tab == 3 ? Visibility.Visible : Visibility.Collapsed;
        TabTip.IsChecked = Tab == 0;
        TabPercent.IsChecked = Tab == 1;
        TabDiscount.IsChecked = Tab == 2;
        TabDates.IsChecked = Tab == 3;
        Recalculate();
    }

    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && int.TryParse(fe.Tag as string, out int t)) ShowTab(t);
    }

    private void Input_Changed(object sender, RoutedEventArgs e) => Recalculate();
    private void Date_Changed(object sender, SelectionChangedEventArgs e) => Recalculate();

    private void TipPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string p) TipPercent.Text = p;
    }

    private void TipPercent_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_ready) return;
        // keep the preset buttons in step with what's typed
        if (TipPanel.FindName("Tip20") is not null)
            foreach (var rb in FindPresets())
                rb.IsChecked = rb.Tag as string == TipPercent.Text.Trim();
        Recalculate();
    }

    private IEnumerable<RadioButton> FindPresets()
    {
        foreach (var child in ((Panel)Tip20.Parent).Children)
            if (child is RadioButton rb) yield return rb;
    }

    private void PeopleMinus_Click(object sender, RoutedEventArgs e)
    {
        int n = int.TryParse(TipPeople.Text, out var p) ? p : 1;
        TipPeople.Text = Math.Max(1, n - 1).ToString();
    }

    private void PeoplePlus_Click(object sender, RoutedEventArgs e)
    {
        int n = int.TryParse(TipPeople.Text, out var p) ? p : 1;
        TipPeople.Text = Math.Min(999, n + 1).ToString();
    }

    // ================================================================ maths

    private static bool Read(TextBox box, out decimal v)
    {
        if (Num.TryParse(box.Text, out v)) return true;
        var r = SumSheet.Evaluate(new[] { box.Text ?? "" });
        if (r.Count == 1 && r[0].Value is decimal d) { v = d; return true; }
        return false;
    }

    public static string Money(decimal v)
    {
        string s = Math.Round(v, 2, MidpointRounding.AwayFromZero).ToString("0.00", Num.Inv);
        return (Num.Grouping ? Num.Group(s) : s).Replace("-", "−");
    }

    private static string Pct(decimal v) => Num.Format(Math.Round(v, 2, MidpointRounding.AwayFromZero)) + "%";

    public void Recalculate()
    {
        if (!_ready) return;
        try
        {
            switch (Tab)
            {
                case 0: CalcTip(); break;
                case 1: CalcPercent(); break;
                case 2: CalcDiscount(); break;
                case 3: CalcDates(); break;
            }
        }
        catch (OverflowException) { }
    }

    /// <summary>Tip &amp; split. Public results are read by the self-test.</summary>
    public (decimal Tip, decimal Total, decimal Each) TipResult { get; private set; }

    private void CalcTip()
    {
        if (!Read(TipBill, out var bill) || !Read(TipPercent, out var pct) || !int.TryParse(TipPeople.Text.Trim(), out int people) || people < 1 || bill < 0)
        {
            TipEach.Text = "—"; TipAmount.Text = TipTotal.Text = ""; TipRoundNote.Text = "";
            return;
        }
        decimal tip = Math.Round(bill * pct / 100m, 2, MidpointRounding.AwayFromZero);
        decimal total = bill + tip;
        decimal each = total / people;
        string note = "";
        if (TipRound.IsChecked == true)
        {
            decimal up = Math.Ceiling(each);
            if (up != Math.Round(each, 2))
            {
                decimal extra = up * people - total;
                note = $"Rounding up adds {Money(extra)} to the tip ({Money(tip + extra)} in all).";
            }
            each = up;
        }
        else each = Math.Round(each, 2, MidpointRounding.AwayFromZero);
        TipResult = (tip, total, each);
        TipEach.Text = Money(each);
        TipAmount.Text = Money(tip);
        TipTotal.Text = Money(total);
        TipRoundNote.Text = people > 1 ? (note.Length > 0 ? note : $"{people} people, {Money(each)} each.") : note;
    }

    public decimal? PercentResult { get; private set; }

    private void CalcPercent()
    {
        PercentResult = null;
        if (Read(PctFrom, out var from) && Read(PctTo, out var to))
        {
            decimal diff = to - from;
            PctDiff.Text = (diff > 0 ? "+" : "") + Num.Format(diff).Replace("-", "−");
            if (from == 0)
            {
                PctChange.Text = "—";
                PctDirection.Text = "Change (can't work out a percent from zero)";
                PctRatio.Text = "";
            }
            else
            {
                decimal change = diff / Math.Abs(from) * 100m;
                PercentResult = change;
                PctChange.Text = (change > 0 ? "+" : "") + Pct(change).Replace("-", "−");
                PctDirection.Text = change > 0 ? "Increase" : change < 0 ? "Decrease" : "No change";
                PctRatio.Text = Pct(to / from * 100m);
            }
        }
        else
        {
            PctChange.Text = "—"; PctDiff.Text = PctRatio.Text = ""; PctDirection.Text = "Change";
        }

        if (Read(PartValue, out var part) && Read(WholeValue, out var whole) && whole != 0)
            PartResult.Text = $"{Num.Format(part)} is {Pct(part / whole * 100m)} of {Num.Format(whole)}";
        else
            PartResult.Text = "—";
    }

    public decimal? DiscountResult { get; private set; }

    private void CalcDiscount()
    {
        DiscountResult = null;
        if (!Read(DiscPrice, out var price) || !Read(DiscPercent, out var off) || !Read(DiscTax, out var tax))
        {
            DiscTotal.Text = "—"; DiscSave.Text = DiscAfter.Text = DiscTaxAmt.Text = "";
            return;
        }
        decimal save = Math.Round(price * off / 100m, 2, MidpointRounding.AwayFromZero);
        decimal after = price - save;
        decimal taxAmt = Math.Round(after * tax / 100m, 2, MidpointRounding.AwayFromZero);
        decimal total = after + taxAmt;
        DiscountResult = total;
        DiscTotal.Text = Money(total);
        DiscSave.Text = Money(save);
        DiscAfter.Text = Money(after);
        DiscTaxAmt.Text = Money(taxAmt);
    }

    public int? DaysResult { get; private set; }

    /// <summary>Sets both dates (used by the self-test).</summary>
    public void SetDates(DateTime from, DateTime to)
    {
        DateFrom.SelectedDate = from;
        DateTo.SelectedDate = to;
        Recalculate();
    }

    private void CalcDates()
    {
        DaysResult = null;
        if (DateFrom.SelectedDate is not DateTime a || DateTo.SelectedDate is not DateTime b)
        {
            DateSpan.Text = "Pick two dates";
            DateDays.Text = DateWeeks.Text = DateWorkdays.Text = "";
        }
        else
        {
            bool backwards = b < a;
            var (start, end) = backwards ? (b, a) : (a, b);
            bool inclusive = DateInclusive.IsChecked == true;
            var endForCount = inclusive ? end.AddDays(1) : end;

            // calendar difference: whole years, then months, then days
            int years = endForCount.Year - start.Year;
            if (start.AddYears(years) > endForCount) years--;
            var cursor = start.AddYears(years);
            int months = 0;
            while (cursor.AddMonths(months + 1) <= endForCount) months++;
            cursor = cursor.AddMonths(months);
            int days = (endForCount - cursor).Days;

            int total = (endForCount - start).Days;
            DaysResult = backwards ? -total : total;
            var parts = new List<string>();
            if (years > 0) parts.Add(Plural(years, "year"));
            if (months > 0) parts.Add(Plural(months, "month"));
            if (days > 0 || parts.Count == 0) parts.Add(Plural(days, "day"));
            DateSpan.Text = total == 0 ? "Same day" : string.Join(", ", parts) + (backwards ? " earlier" : "");
            DateDays.Text = Num.Format((decimal)total);
            DateWeeks.Text = total >= 7 ? $"{total / 7} weeks{(total % 7 > 0 ? $", {Plural(total % 7, "day")}" : "")}" : Plural(total, "day");
            int work = 0;
            for (var d = start; d < endForCount; d = d.AddDays(1))
                if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) work++;
            DateWorkdays.Text = Num.Format((decimal)work);
        }

        if (DateFrom.SelectedDate is DateTime from && int.TryParse(AddDays.Text.Trim().Replace("−", "-").Replace(",", ""), out int add)
            && Math.Abs(add) <= 3_000_000)
        {
            try { AddResult.Text = from.AddDays(add).ToString("dddd, MMMM d, yyyy"); }
            catch (ArgumentOutOfRangeException) { AddResult.Text = "—"; }
        }
        else AddResult.Text = "—";
    }

    private static string Plural(int n, string word) => $"{n:N0} {word}{(n == 1 ? "" : "s")}";
}
