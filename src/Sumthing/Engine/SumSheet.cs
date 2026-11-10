using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Sumthing.Engine;

/// <summary>The answer for one line of a Type-a-sum sheet.</summary>
public sealed class SheetResult
{
    public decimal? Value { get; init; }
    /// <summary>Formatted answer, or "" when the line has no answer (blank, heading, words only).</summary>
    public string Text { get; init; } = "";
    public string Error { get; init; }
    public bool IsHeading { get; init; }
    public string Variable { get; init; }
}

/// <summary>
/// Type-a-sum: every line is a sum, answered as you type.
/// <list type="bullet">
/// <item>Numbers: 1,200  3.5  $40  5k (5,000)</item>
/// <item>Operators: + - * / × ÷ ^ ( ), and the words plus, minus, times, divided by</item>
/// <item>Percent: 12% of 340 · 340 + 15% · 340 - 20% · 20% off 80 · 8% on 50</item>
/// <item>Variables: rent = 1,200, then "rent * 12" on a later line (names can have spaces)</item>
/// <item>ans / prev = the line above; total / sum = everything since the last blank line or heading</item>
/// <item>Functions: sqrt abs round floor ceil sin cos tan (degrees) log ln; constants pi, e</item>
/// <item>Labels and notes: "Groceries: 45 + 12", "# Heading", anything after // is a comment, other words are ignored</item>
/// </list>
/// </summary>
public static class SumSheet
{
    public static List<SheetResult> Evaluate(IReadOnlyList<string> lines)
    {
        var vars = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var results = new List<SheetResult>(lines.Count);
        decimal? prev = null;
        decimal runningTotal = 0;
        bool anyInGroup = false;

        foreach (var raw in lines)
        {
            string line = raw ?? "";
            string trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                results.Add(new SheetResult());
                runningTotal = 0; anyInGroup = false;
                continue;
            }
            if (trimmed.StartsWith('#'))
            {
                results.Add(new SheetResult { IsHeading = true });
                runningTotal = 0; anyInGroup = false;
                continue;
            }
            int comment = line.IndexOf("//", StringComparison.Ordinal);
            if (comment >= 0) line = line[..comment];

            // "Label: 45 + 12" — drop a leading label that has no digits.
            int colon = line.IndexOf(':');
            if (colon > 0 && !line[..colon].Any(char.IsDigit) && line[..colon].Any(char.IsLetter))
                line = line[(colon + 1)..];

            // "name = expression"
            string assignTo = null;
            var m = Regex.Match(line, @"^\s*([A-Za-z_][A-Za-z0-9_ ]*?)\s*=\s*(.+)$");
            if (m.Success && !IsReserved(m.Groups[1].Value))
            {
                assignTo = NormalizeName(m.Groups[1].Value);
                line = m.Groups[2].Value;
            }

            if (string.IsNullOrWhiteSpace(line) || !HasValueToken(line, vars))
            {
                results.Add(new SheetResult());
                continue;
            }

            try
            {
                var p = new Parser(line, vars, prev, anyInGroup ? runningTotal : 0m);
                decimal v = p.Parse();
                if (assignTo != null) vars[assignTo] = v;
                prev = v;
                runningTotal += v;
                anyInGroup = true;
                results.Add(new SheetResult { Value = v, Text = Num.Format(v), Variable = assignTo });
            }
            catch (OverflowException)
            {
                results.Add(new SheetResult { Error = "Too big" });
            }
            catch (DivideByZeroException)
            {
                results.Add(new SheetResult { Error = "Can't divide by zero" });
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException)
            {
                results.Add(new SheetResult { Error = ex.Message });
            }
        }
        return results;
    }

    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "ans", "prev", "previous", "total", "sum", "subtotal", "pi", "e", "of", "off", "on",
        "plus", "minus", "times", "divided", "by", "over", "sqrt", "abs", "round", "floor", "ceil",
        "sin", "cos", "tan", "log", "ln",
    };

    private static bool IsReserved(string name) => Reserved.Contains(NormalizeName(name));

    public static string NormalizeName(string name) =>
        Regex.Replace(name.Trim(), @"\s+", " ").ToLowerInvariant();

    /// <summary>True if the line has a number, a known variable or a value word; lines of plain words get no answer.</summary>
    private static bool HasValueToken(string line, Dictionary<string, decimal> vars)
    {
        if (line.Any(char.IsDigit)) return true;
        foreach (Match w in Regex.Matches(line, @"[A-Za-z_][A-Za-z0-9_]*"))
        {
            var s = w.Value.ToLowerInvariant();
            if (s is "ans" or "prev" or "previous" or "total" or "sum" or "subtotal" or "pi") return true;
        }
        string norm = " " + NormalizeName(Regex.Replace(line, @"[^A-Za-z0-9_ ]", " ")) + " ";
        foreach (var k in vars.Keys)
            if (norm.Contains(" " + k + " ", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    // ================================================================ parser

    private enum T { Num, Word, Op, LParen, RParen, Percent, End }

    private readonly record struct Tok(T Kind, string Text, decimal Value = 0);

    private readonly record struct Val(decimal V, bool IsPercent = false)
    {
        /// <summary>The value to use in arithmetic: 15% is 0.15.</summary>
        public decimal Plain => IsPercent ? V / 100m : V;
    }

    private sealed class Parser
    {
        private readonly List<Tok> _toks;
        private int _i;
        private readonly Dictionary<string, decimal> _vars;
        private readonly decimal? _prev;
        private readonly decimal _total;

        public Parser(string text, Dictionary<string, decimal> vars, decimal? prev, decimal total)
        {
            _vars = vars;
            _prev = prev;
            _total = total;
            _toks = Tokenize(text);
        }

        public decimal Parse()
        {
            SkipNoise();
            if (Peek.Kind == T.End) throw new FormatException("Nothing to work out");
            var v = Expr();
            SkipNoise();
            if (Peek.Kind != T.End) throw new FormatException($"Didn't understand \"{Peek.Text}\"");
            return v.Plain;
        }

        private Tok Peek => _toks[_i];
        private Tok Next() => _toks[_i++];

        private Val Expr()
        {
            var left = Term();
            while (true)
            {
                SkipNoise();
                var t = Peek;
                string op = null;
                if (t.Kind == T.Op && (t.Text == "+" || t.Text == "-")) op = t.Text;
                else if (t.Kind == T.Word && t.Text == "plus") op = "+";
                else if (t.Kind == T.Word && t.Text == "minus") op = "-";
                if (op == null) break;
                Next();
                var right = Term();
                decimal l = left.Plain;
                decimal r;
                if (right.IsPercent && !left.IsPercent)
                    r = op == "+" ? l + l * right.V / 100m : l - l * right.V / 100m;
                else
                    r = op == "+" ? l + right.Plain : l - right.Plain;
                left = new Val(r);
            }
            return left;
        }

        private Val Term()
        {
            var left = Unary();
            while (true)
            {
                SkipNoise();
                var t = Peek;
                string op = null;
                if (t.Kind == T.Op && (t.Text == "*" || t.Text == "/")) op = t.Text;
                else if (t.Kind == T.Word && (t.Text == "times" || t.Text == "x")) op = "*";
                else if (t.Kind == T.Word && (t.Text == "divided" || t.Text == "over")) op = "/";
                else if (t.Kind is T.Num or T.LParen) op = "*"; // implicit: 2(3+4)
                if (op == null) break;
                if (t.Kind is not (T.Num or T.LParen)) Next();
                if (op == "/" && Peek.Kind == T.Word && Peek.Text == "by") Next();
                var right = Unary();
                decimal l = left.Plain, r = right.Plain;
                if (op == "*") left = new Val(l * r);
                else
                {
                    if (r == 0) throw new DivideByZeroException();
                    left = new Val(l / r);
                }
            }
            return left;
        }

        private Val Unary()
        {
            SkipNoise();
            if (Peek.Kind == T.Op && Peek.Text == "-") { Next(); var v = Unary(); return new Val(-v.V, v.IsPercent); }
            if (Peek.Kind == T.Op && Peek.Text == "+") { Next(); return Unary(); }
            return Power();
        }

        private Val Power()
        {
            var b = Postfix();
            SkipNoise();
            if (Peek.Kind == T.Op && Peek.Text == "^")
            {
                Next();
                var ex = Unary();
                return new Val(Num.Pow(b.Plain, ex.Plain));
            }
            return b;
        }

        private Val Postfix()
        {
            var v = Primary();
            SkipNoise();
            if (Peek.Kind == T.Percent)
            {
                Next();
                v = new Val(v.V, true);
                SkipNoise();
                if (Peek.Kind == T.Word && Peek.Text is "of" or "off" or "on")
                {
                    string w = Next().Text;
                    var target = Unary().Plain;
                    decimal p = v.V / 100m;
                    return w switch
                    {
                        "of" => new Val(target * p),
                        "off" => new Val(target - target * p),
                        _ => new Val(target + target * p),
                    };
                }
            }
            return v;
        }

        private Val Primary()
        {
            SkipNoise();
            var t = Peek;
            switch (t.Kind)
            {
                case T.Num:
                    Next();
                    return new Val(t.Value);
                case T.LParen:
                {
                    Next();
                    var v = Expr();
                    SkipNoise();
                    if (Peek.Kind == T.RParen) Next();
                    else if (Peek.Kind != T.End) throw new FormatException("Missing )");
                    return new Val(v.Plain);
                }
                case T.Word:
                    return WordValue();
                case T.End:
                    throw new FormatException("The sum isn't finished");
                default:
                    throw new FormatException($"Didn't understand \"{t.Text}\"");
            }
        }

        private Val WordValue()
        {
            // Longest run of words that names a variable ("car payment").
            for (int len = Math.Min(6, CountWords()); len >= 1; len--)
            {
                var name = string.Join(" ", _toks.Skip(_i).Take(len).Select(x => x.Text));
                if (_vars.TryGetValue(name, out var val)) { _i += len; return new Val(val); }
            }
            var t = Next();
            switch (t.Text)
            {
                case "ans": case "prev": case "previous":
                    if (_prev is decimal p) return new Val(p);
                    throw new InvalidOperationException("There's no line above yet");
                case "total": case "sum": case "subtotal":
                    return new Val(_total);
                case "pi": return new Val(3.1415926535897932384626433833m);
                case "e": return new Val(2.7182818284590452353602874714m);
                case "sqrt": return Fn(x => { if (x < 0) throw new ArgumentException("Can't take the square root of a negative number"); return Num.Sqrt(x); });
                case "abs": return Fn(Math.Abs);
                case "round": return Fn(x => Math.Round(x, MidpointRounding.AwayFromZero));
                case "floor": return Fn(Math.Floor);
                case "ceil": return Fn(Math.Ceiling);
                case "sin": return FnD(x => Math.Sin(x * Math.PI / 180));
                case "cos": return FnD(x => Math.Cos(x * Math.PI / 180));
                case "tan": return FnD(x => Math.Tan(x * Math.PI / 180));
                case "log": return FnD(x => { if (x <= 0) throw new ArgumentException("log needs a number above 0"); return Math.Log10(x); });
                case "ln": return FnD(x => { if (x <= 0) throw new ArgumentException("ln needs a number above 0"); return Math.Log(x); });
            }
            throw new FormatException($"Didn't understand \"{t.Text}\"");
        }

        private int CountWords()
        {
            int n = 0;
            while (_i + n < _toks.Count && _toks[_i + n].Kind == T.Word) n++;
            return n;
        }

        private Val Fn(Func<decimal, decimal> f) => new(f(Postfix().Plain));

        private Val FnD(Func<double, double> f)
        {
            double r = f((double)Postfix().Plain);
            if (double.IsNaN(r) || double.IsInfinity(r)) throw new ArgumentException("No answer");
            r = Math.Round(r, 14);
            return new Val((decimal)r);
        }

        private static readonly HashSet<string> Known = new()
        {
            "of", "off", "on", "plus", "minus", "times", "x", "divided", "by", "over",
            "ans", "prev", "previous", "total", "sum", "subtotal", "pi", "e",
            "sqrt", "abs", "round", "floor", "ceil", "sin", "cos", "tan", "log", "ln",
        };

        /// <summary>Skips words that aren't operators, functions or variables ("5 apples + 3 apples").</summary>
        private void SkipNoise()
        {
            while (Peek.Kind == T.Word && !Known.Contains(Peek.Text) && !StartsVariable())
                _i++;
        }

        private bool StartsVariable()
        {
            for (int len = Math.Min(6, CountWords()); len >= 1; len--)
            {
                var name = string.Join(" ", _toks.Skip(_i).Take(len).Select(x => x.Text));
                if (_vars.ContainsKey(name)) return true;
            }
            return false;
        }

        private static List<Tok> Tokenize(string s)
        {
            var list = new List<Tok>();
            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (char.IsWhiteSpace(c) || c is '$' or '€' or '£' or '¥' or ' ') { i++; continue; }
                if (char.IsDigit(c) || (c == '.' && i + 1 < s.Length && char.IsDigit(s[i + 1])))
                {
                    var sb = new StringBuilder();
                    while (i < s.Length)
                    {
                        char d = s[i];
                        if (char.IsDigit(d) || d == '.') { sb.Append(d); i++; }
                        // a thousands separator: comma followed by exactly three digits
                        else if (d == ',' && IsGroup(s, i)) { i++; }
                        else break;
                    }
                    if (!decimal.TryParse(sb.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                        throw new FormatException($"\"{sb}\" isn't a number");
                    // 5k = 5,000
                    if (i < s.Length && (s[i] == 'k' || s[i] == 'K') && (i + 1 >= s.Length || !char.IsLetter(s[i + 1])))
                    { v *= 1000; i++; }
                    list.Add(new Tok(T.Num, sb.ToString(), v));
                    continue;
                }
                if (char.IsLetter(c) || c == '_')
                {
                    int st = i;
                    while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_')) i++;
                    list.Add(new Tok(T.Word, s[st..i].ToLowerInvariant()));
                    continue;
                }
                switch (c)
                {
                    case '+': list.Add(new Tok(T.Op, "+")); break;
                    case '-': case '−': case '–': list.Add(new Tok(T.Op, "-")); break;
                    case '*': case '×': case '·': list.Add(new Tok(T.Op, "*")); break;
                    case '/': case '÷': list.Add(new Tok(T.Op, "/")); break;
                    case '^': list.Add(new Tok(T.Op, "^")); break;
                    case '(': list.Add(new Tok(T.LParen, "(")); break;
                    case ')': list.Add(new Tok(T.RParen, ")")); break;
                    case '%': list.Add(new Tok(T.Percent, "%")); break;
                    case ',': case ';': case '?': case '!': case '"': case '\'': break; // punctuation in notes
                    default: throw new FormatException($"Didn't understand \"{c}\"");
                }
                i++;
            }
            list.Add(new Tok(T.End, ""));
            return list;
        }

        /// <summary>True when s[i] is ',' followed by exactly three digits (then a non-digit or the end).</summary>
        private static bool IsGroup(string s, int i)
        {
            for (int k = 1; k <= 3; k++)
                if (i + k >= s.Length || !char.IsDigit(s[i + k])) return false;
            return i + 4 >= s.Length || !char.IsDigit(s[i + 4]);
        }
    }
}
