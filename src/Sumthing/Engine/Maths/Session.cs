using System.Text.RegularExpressions;

namespace Sumthing.Maths;

/// <summary>One line of the scientific "home screen": what was typed and the answer.</summary>
public sealed class HomeEntry
{
    public string Input { get; set; } = "";
    public string Output { get; set; } = "";
    /// <summary>The same answer as a fraction (3/4) when it is one; "" otherwise.</summary>
    public string Exact { get; set; } = "";
    public bool IsError { get; set; }
    public double? Value { get; set; }
}

public static class Frac
{
    /// <summary>Best fraction for v with a denominator up to maxDen, or null if v isn't (close to) one.</summary>
    public static (long Num, long Den)? Find(double v, long maxDen = 10000)
    {
        if (double.IsNaN(v) || double.IsInfinity(v) || System.Math.Abs(v) > 1e12) return null;
        long sign = v < 0 ? -1 : 1;
        double x = System.Math.Abs(v);
        long h0 = 0, h1 = 1, k0 = 1, k1 = 0;
        double r = x;
        for (int i = 0; i < 40; i++)
        {
            double a = System.Math.Floor(r);
            long h2 = (long)a * h1 + h0, k2 = (long)a * k1 + k0;
            if (k2 > maxDen) break;
            h0 = h1; h1 = h2; k0 = k1; k1 = k2;
            if (System.Math.Abs(x - (double)h1 / k1) <= 1e-12 * System.Math.Max(1, x)) return (sign * h1, k1);
            double frac = r - a;
            if (frac < 1e-15) break;
            r = 1 / frac;
        }
        if (k1 != 0 && System.Math.Abs(x - (double)h1 / k1) <= 1e-12 * System.Math.Max(1, x)) return (sign * h1, k1);
        return null;
    }

    public static string Text(double v)
    {
        var f = Find(v);
        if (f == null) return null;
        var (n, d) = f.Value;
        return d == 1 ? n.ToString() : $"{n}/{d}".Replace("-", "−");
    }
}

/// <summary>
/// The scientific home screen, like a TI-84's: Ans, storing (5→A, L1 from a list), ►Frac and ►Dec,
/// starting a line with an operator continues from Ans, and Enter on an empty line repeats the last one.
/// </summary>
public sealed class CalcSession
{
    public CalcSession(EvalContext context = null) { Context = context ?? new EvalContext(); }

    public EvalContext Context { get; }
    public List<HomeEntry> Entries { get; } = new();
    /// <summary>User function names known to the graph (g, area ...), so g(2) parses as a call.</summary>
    public Func<ICollection<string>> UserFunctionNames { get; set; } = () => Array.Empty<string>();

    public static readonly string[] StoreArrows = { "→", "->", "=>" };

    public HomeEntry Evaluate(string input)
    {
        input = (input ?? "").Trim();
        if (input.Length == 0)
        {
            if (Entries.Count == 0) return null;
            input = Entries[^1].Input;
        }
        var entry = new HomeEntry { Input = input };
        try
        {
            string body = input;
            bool toFrac = false, toDec = false;
            foreach (var suffix in new[] { "►Frac", ">Frac", "▸Frac" })
                if (body.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) { body = body[..^suffix.Length].Trim(); toFrac = true; }
            foreach (var suffix in new[] { "►Dec", ">Dec", "▸Dec" })
                if (body.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) { body = body[..^suffix.Length].Trim(); toDec = true; }

            // 5→A
            string storeTo = null;
            foreach (var arrow in StoreArrows)
            {
                int at = body.LastIndexOf(arrow, StringComparison.Ordinal);
                if (at > 0)
                {
                    storeTo = body[(at + arrow.Length)..].Trim();
                    body = body[..at].Trim();
                    break;
                }
            }
            if (storeTo != null && !Regex.IsMatch(storeTo, @"^[A-Za-zθ][A-Za-z0-9_]*$"))
                throw new MathException("Store into a name, like 5→A");
            if (storeTo is "x" or "y" or "t" or "θ" or "e" or "pi" or "Ans" or "ans")
                throw new MathException($"{storeTo} can't be used as a stored name");

            // starting with an operator continues from the last answer (like a TI)
            if (body.Length > 0 && "+*/^²³×÷!%".Contains(body[0]) && Entries.Count > 0) body = "Ans" + body;
            if (body.Length > 0 && (body[0] == '−') && Entries.Count > 0 && body.Length > 1) body = "Ans" + body;

            var known = new HashSet<string>(Context.Vars.Keys, StringComparer.Ordinal);
            if (storeTo != null) known.Add(storeTo);
            var node = Parser.Parse(body, known, UserFunctionNames());

            // lists: {1,2,3}→L1, seq(...)→L2
            if (node is ListNode ln || (node is CallNode cn && cn.Name == "seq"))
            {
                double[] list = node is ListNode l2 ? l2.EvalAll(Context) : EvalSeq((CallNode)node);
                if (storeTo != null) Context.Lists[storeTo] = list;
                entry.Output = "{" + string.Join(", ", list.Select(v => Parser.Format(v))) + "}";
            }
            else if (node is VarNode vn && Context.Lists.TryGetValue(vn.Name, out var shown))
            {
                entry.Output = "{" + string.Join(", ", shown.Select(v => Parser.Format(v))) + "}";
            }
            else
            {
                double v = node.Eval(Context);
                if (double.IsNaN(v)) throw new MathException("That's undefined (or not a real number)");
                if (storeTo != null)
                {
                    if (Parser.IsListName(storeTo)) throw new MathException("Store a list into L1–L6, like {1,2,3}→L1");
                    Context.Vars[storeTo] = v;
                }
                Context.Ans = v;
                entry.Value = v;
                string frac = Frac.Text(v);
                if (toFrac)
                {
                    entry.Output = frac ?? Parser.Format(v);
                }
                else
                {
                    entry.Output = Parser.Format(v);
                    if (!toDec && frac != null && frac.Contains('/') && System.Math.Abs(v) < 1e9) entry.Exact = frac;
                }
            }
        }
        catch (MathException ex)
        {
            entry.IsError = true;
            entry.Output = ex.Message;
        }
        catch (OverflowException)
        {
            entry.IsError = true;
            entry.Output = "Overflow";
        }
        Entries.Add(entry);
        if (Entries.Count > 500) Entries.RemoveAt(0);
        return entry;
    }

    private double[] EvalSeq(CallNode cn)
    {
        // reuse sum(...)'s machinery: evaluate seq via mean of a wrapper isn't possible, so walk it here
        if (cn.Args.Length < 4 || cn.Args[1] is not VarNode v) throw new MathException("seq(expression, variable, start, end, step)");
        double a = cn.Args[2].Eval(Context), b = cn.Args[3].Eval(Context), step = cn.Args.Length > 4 ? cn.Args[4].Eval(Context) : 1;
        if (step == 0 || System.Math.Abs((b - a) / step) > 10000) throw new MathException("seq: too many items");
        var r = new List<double>();
        for (double k = a; step > 0 ? k <= b + 1e-12 : k >= b - 1e-12; k += step)
            r.Add(Functions.WithVar(Context, v.Name, k, () => cn.Args[0].Eval(Context)));
        return r.ToArray();
    }
}
