using System.Globalization;
using System.Text;

namespace Sumthing.Engine;

/// <summary>Number formatting and parsing shared by every part of Sumthing.</summary>
public static class Num
{
    public static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Show thousands separators (1,234,567). Set from Settings.</summary>
    public static bool Grouping { get; set; } = true;

    /// <summary>Formats a value for display, rounded to <paramref name="maxSig"/> significant digits.</summary>
    public static string Format(decimal v, int maxSig = 16)
    {
        if (v == 0) return "0";
        decimal a = Math.Abs(v);
        if (a >= 1e16m) return Sci((double)v, maxSig);
        double lg = Math.Log10((double)a);
        int dec;
        if (a >= 1) dec = maxSig - ((int)Math.Floor(lg) + 1);
        else dec = maxSig + (-(int)Math.Floor(lg) - 1);
        dec = Math.Clamp(dec, 0, 28);
        decimal r = Math.Round(v, dec, MidpointRounding.AwayFromZero);
        if (r == 0) return Sci((double)v, maxSig);
        if (a < 1e-12m) return Sci((double)v, maxSig);
        string s = r.ToString("0.############################", Inv);
        return Grouping ? Group(s) : s;
    }

    /// <summary>Formats a double (units, scientific functions) to <paramref name="maxSig"/> significant digits.</summary>
    public static string Format(double v, int maxSig = 12)
    {
        if (double.IsNaN(v) || double.IsInfinity(v)) return "—";
        if (v == 0) return "0";
        double a = Math.Abs(v);
        if (a >= 1e16 || a < 1e-12) return Sci(v, maxSig);
        try { return Format((decimal)v, maxSig); }
        catch (OverflowException) { return Sci(v, maxSig); }
    }

    private static string Sci(double v, int maxSig)
    {
        int digits = Math.Clamp(maxSig - 1, 1, 15);
        string s = v.ToString("0." + new string('#', digits) + "e+0", Inv);
        return s;
    }

    /// <summary>Adds thousands separators to the whole-number part of a plain number string ("-1234.5" → "-1,234.5").</summary>
    public static string Group(string s)
    {
        if (string.IsNullOrEmpty(s) || s.Contains('e') || s.Contains('E')) return s;
        bool neg = s.StartsWith('-');
        string body = neg ? s[1..] : s;
        int dot = body.IndexOf('.');
        string whole = dot >= 0 ? body[..dot] : body;
        string frac = dot >= 0 ? body[dot..] : "";
        if (whole.Length <= 3) return s;
        var sb = new StringBuilder();
        int first = whole.Length % 3;
        if (first > 0) sb.Append(whole, 0, first);
        for (int i = first; i < whole.Length; i += 3)
        {
            if (sb.Length > 0) sb.Append(',');
            sb.Append(whole, i, 3);
        }
        return (neg ? "-" : "") + sb + frac;
    }

    /// <summary>Lenient parse: ignores spaces, thousands separators and currency symbols; accepts 1.5e3.</summary>
    public static bool TryParse(string s, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;
        var sb = new StringBuilder();
        foreach (char c in s.Trim())
        {
            if (char.IsWhiteSpace(c) || c == ',' || c == '$' || c == '€' || c == '£' || c == '¥' || c == ' ') continue;
            if (c == '−') { sb.Append('-'); continue; }
            sb.Append(c);
        }
        string t = sb.ToString();
        if (t.Length == 0) return false;
        if (decimal.TryParse(t, NumberStyles.Float, Inv, out value)) return true;
        if (double.TryParse(t, NumberStyles.Float, Inv, out double d) && !double.IsNaN(d) && !double.IsInfinity(d))
        {
            try { value = (decimal)d; return true; } catch { }
        }
        return false;
    }

    /// <summary>Plain text for a value (no grouping), used as the editable entry and for copying.</summary>
    public static string Plain(decimal v, int maxSig = 16)
    {
        bool g = Grouping;
        Grouping = false;
        try { return Format(v, maxSig); }
        finally { Grouping = g; }
    }

    /// <summary>Square root of a decimal, accurate to decimal precision.</summary>
    public static decimal Sqrt(decimal x)
    {
        if (x < 0) throw new ArgumentException("negative");
        if (x == 0) return 0;
        decimal g = (decimal)Math.Sqrt((double)x);
        for (int i = 0; i < 6 && g != 0; i++)
        {
            decimal next = (g + x / g) / 2;
            if (next == g) break;
            g = next;
        }
        return g;
    }

    /// <summary>x^y: exact for whole-number powers, otherwise via double.</summary>
    public static decimal Pow(decimal x, decimal y)
    {
        if (y == Math.Truncate(y) && Math.Abs(y) <= 512)
        {
            int n = (int)Math.Abs(y);
            decimal r = 1, b = x;
            while (n > 0)
            {
                if ((n & 1) == 1) r *= b;
                n >>= 1;
                if (n > 0) b *= b;
            }
            if (y < 0)
            {
                if (r == 0) throw new DivideByZeroException();
                r = 1 / r;
            }
            return r;
        }
        double d = Math.Pow((double)x, (double)y);
        if (double.IsNaN(d)) throw new ArgumentException("Invalid input");
        if (double.IsInfinity(d)) throw new OverflowException();
        return (decimal)d;
    }
}
