using System.Globalization;
using System.Text;
using Sumthing.Engine;

namespace Sumthing.Maths;

/// <summary>Everything an expression can look up while it is worked out.</summary>
public sealed class EvalContext
{
    public double X, Y, T, Theta, N;
    public bool Degrees;
    /// <summary>Stored variables (A, B, ... from STO→) and graph sliders.</summary>
    public Dictionary<string, double> Vars { get; } = new(StringComparer.Ordinal);
    /// <summary>Lists L1–L6 (and any named list).</summary>
    public Dictionary<string, double[]> Lists { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>User functions: f1(x), g(x), Y1(x) ... (graph rows). Return null when there is no such function.</summary>
    public Func<string, double, double?> UserFunction { get; set; }
    /// <summary>The last answer (Ans).</summary>
    public double Ans { get; set; }

    internal int Depth;
    internal readonly Dictionary<string, double> Locals = new(StringComparer.Ordinal);
    private readonly Random _rng = new();
    internal double Random() => _rng.NextDouble();
    internal int RandomInt(int a, int b) => _rng.Next(System.Math.Min(a, b), System.Math.Max(a, b) + 1);

    public EvalContext Clone()
    {
        var c = new EvalContext { Degrees = Degrees, UserFunction = UserFunction, Ans = Ans };
        foreach (var kv in Vars) c.Vars[kv.Key] = kv.Value;
        foreach (var kv in Lists) c.Lists[kv.Key] = kv.Value;
        return c;
    }
}

public sealed class MathException : Exception
{
    public MathException(string message) : base(message) { }
}

// ============================================================ syntax tree

public abstract class Node
{
    public abstract double Eval(EvalContext c);
    /// <summary>Adds every variable name this expression reads.</summary>
    public virtual void CollectNames(HashSet<string> names) { }
}

public sealed class NumNode : Node
{
    public readonly double Value;
    public NumNode(double v) { Value = v; }
    public override double Eval(EvalContext c) => Value;
}

public enum VarKind { X, Y, T, Theta, N, Ans, Pi, E, Named }

public sealed class VarNode : Node
{
    public readonly string Name;
    public readonly VarKind Kind;

    public VarNode(string name)
    {
        Name = name;
        Kind = name switch
        {
            "x" or "X" => VarKind.X,
            "y" or "Y" => VarKind.Y,
            "t" or "T" => VarKind.T,
            "θ" or "theta" => VarKind.Theta,
            "n" => VarKind.N,
            "Ans" or "ans" => VarKind.Ans,
            "π" or "pi" => VarKind.Pi,
            "e" => VarKind.E,
            _ => VarKind.Named,
        };
    }

    public override double Eval(EvalContext c)
    {
        if (c.Locals.Count > 0 && c.Locals.TryGetValue(Name, out var local)) return local;
        switch (Kind)
        {
            case VarKind.X: return c.X;
            case VarKind.Y: return c.Y;
            case VarKind.T: return c.T;
            case VarKind.Theta: return c.Theta;
            case VarKind.N: return c.N;
            case VarKind.Ans: return c.Ans;
            case VarKind.Pi: return System.Math.PI;
            case VarKind.E: return System.Math.E;
        }
        if (c.Vars.TryGetValue(Name, out var v)) return v;
        throw new MathException($"{Name} has no value yet. Store one with {Name.ToUpperInvariant()}→ or 5→{Name}.");
    }

    public override void CollectNames(HashSet<string> names) => names.Add(Name);
}

public sealed class UnaryNode : Node
{
    public readonly char Op; // '-', '!', '%', '°'
    public readonly Node A;
    public UnaryNode(char op, Node a) { Op = op; A = a; }

    public override double Eval(EvalContext c)
    {
        double a = A.Eval(c);
        switch (Op)
        {
            case '-': return -a;
            case '!': return Functions.Factorial(a);
            case '%': return a / 100.0;
            case '°': return c.Degrees ? a : a * System.Math.PI / 180.0;
            case 'r': return c.Degrees ? a * 180.0 / System.Math.PI : a; // radian mark (ʳ)
        }
        return double.NaN;
    }

    public override void CollectNames(HashSet<string> names) => A.CollectNames(names);
}

public sealed class BinaryNode : Node
{
    public readonly string Op;
    public readonly Node A, B;
    public BinaryNode(string op, Node a, Node b) { Op = op; A = a; B = b; }

    public override double Eval(EvalContext c)
    {
        double a = A.Eval(c), b = B.Eval(c);
        switch (Op)
        {
            case "+": return a + b;
            case "-": return a - b;
            case "*": return a * b;
            case "/": return a / b;
            case "^": return Functions.Pow(a, b);
            case "nCr": return Functions.NCr(a, b);
            case "nPr": return Functions.NPr(a, b);
            case "mod": return Functions.Mod(a, b);
            case "<": return a < b ? 1 : 0;
            case ">": return a > b ? 1 : 0;
            case "<=": return a <= b ? 1 : 0;
            case ">=": return a >= b ? 1 : 0;
            case "=": return System.Math.Abs(a - b) <= 1e-12 * System.Math.Max(1, System.Math.Max(System.Math.Abs(a), System.Math.Abs(b))) ? 1 : 0;
            case "!=": return a != b ? 1 : 0;
            case "and": return a != 0 && b != 0 ? 1 : 0;
            case "or": return a != 0 || b != 0 ? 1 : 0;
        }
        return double.NaN;
    }

    public override void CollectNames(HashSet<string> names) { A.CollectNames(names); B.CollectNames(names); }
}

public sealed class CallNode : Node
{
    public readonly string Name;
    public readonly Node[] Args;
    public CallNode(string name, Node[] args) { Name = name; Args = args; }

    public override double Eval(EvalContext c) => Functions.Call(this, c);

    public override void CollectNames(HashSet<string> names)
    {
        // nDeriv(expr, x, value): the bound variable isn't a free name
        string bound = Functions.BindsVariable(Name) && Args.Length > 1 && Args[1] is VarNode v ? v.Name : null;
        var inner = new HashSet<string>(StringComparer.Ordinal);
        foreach (var a in Args) a.CollectNames(inner);
        if (bound != null) inner.Remove(bound);
        names.UnionWith(inner);
    }
}

/// <summary>A literal list {1, 2, 3}.</summary>
public sealed class ListNode : Node
{
    public readonly Node[] Items;
    public ListNode(Node[] items) { Items = items; }
    public override double Eval(EvalContext c) => throw new MathException("A list can't be used as a single number here.");
    public double[] EvalAll(EvalContext c) => Items.Select(i => i.Eval(c)).ToArray();
    public override void CollectNames(HashSet<string> names) { foreach (var i in Items) i.CollectNames(names); }
}

// ============================================================ parser

/// <summary>
/// Parses TI-84 / TI-Nspire style maths:
/// 2x+3, 3(x+1), 2πr, sin(30°), x², √(2), 5!, 7 nCr 2, |x−3|, sin⁻¹(0.5), log(8,2), e^(2x), 1.5E3,
/// nDeriv(x³,x,2), fnInt(x²,x,0,3), mean(L1), {1,2,3}, x<2 (gives 1 or 0), f1(3).
/// </summary>
public static class Parser
{
    private enum K { Num, Ident, Op, LParen, RParen, LBrace, RBrace, Comma, Bar, End }

    private readonly record struct Tok(K Kind, string Text, double Value = 0);

    /// <summary>Names that are functions (followed by "(" or a value).</summary>
    public static readonly HashSet<string> FunctionNames = new(StringComparer.Ordinal)
    {
        "sin", "cos", "tan", "asin", "acos", "atan", "sinh", "cosh", "tanh", "asinh", "acosh", "atanh",
        "sec", "csc", "cot", "ln", "log", "log2", "exp", "sqrt", "cbrt", "root", "abs", "floor", "ceil", "round",
        "int", "iPart", "fPart", "sign", "min", "max", "gcd", "lcm", "mod", "rem", "nCr", "nPr", "factorial",
        "rand", "randInt", "randNorm", "nDeriv", "fnInt", "sum", "prod", "mean", "median", "stdDev", "variance",
        "dim", "seq", "atan2", "arg", "deg", "rad", "piecewise", "normalpdf", "normalcdf", "invNorm", "binompdf", "binomcdf",
        "arcsin", "arccos", "arctan",
    };

    private static readonly HashSet<string> Constants = new(StringComparer.Ordinal) { "pi", "π", "e", "Ans", "ans", "theta", "θ" };

    /// <param name="knownNames">Multi-letter names that must not be split into single letters (stored variables, functions f1, g…).</param>
    /// <param name="userFunctions">Names of user functions such as g or area (called like g(2)).</param>
    public static Node Parse(string text, ICollection<string> knownNames = null, ICollection<string> userFunctions = null)
    {
        var known = new HashSet<string>(StringComparer.Ordinal);
        if (knownNames != null) known.UnionWith(knownNames);
        if (userFunctions != null) known.UnionWith(userFunctions);
        var toks = Tokenize(text ?? "", known);
        int i = 0;
        _userFns = userFunctions;
        _barDepth = 0;
        try
        {
            var node = ParseOr(toks, ref i);
            if (toks[i].Kind != K.End)
                throw new MathException(toks[i].Kind == K.RParen ? "There's an extra )" : $"Didn't understand \"{toks[i].Text}\"");
            return node;
        }
        finally { _userFns = null; }
    }

    [ThreadStatic] private static ICollection<string> _userFns;

    // ---------------------------------------------------------------- tokens

    private static List<Tok> Tokenize(string s, ICollection<string> known)
    {
        var list = new List<Tok>();
        int i = 0;
        while (i < s.Length)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }

            // numbers: 12, 3.5, .5, 1.5E3 (E or ᴇ for powers of ten)
            if (char.IsDigit(c) || (c == '.' && i + 1 < s.Length && char.IsDigit(s[i + 1])))
            {
                int st = i;
                while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' || (s[i] == ',' && IsGroup(s, i)))) i++;
                string num = s[st..i].Replace(",", "");
                if (i < s.Length && (s[i] == 'E' || s[i] == 'ᴇ'))
                {
                    int j = i + 1;
                    if (j < s.Length && (s[j] == '-' || s[j] == '+' || s[j] == '−')) j++;
                    if (j < s.Length && char.IsDigit(s[j]))
                    {
                        while (j < s.Length && char.IsDigit(s[j])) j++;
                        num += "E" + s[(i + 1)..j].Replace('−', '-');
                        i = j;
                    }
                }
                if (num.Count(ch => ch == '.') > 1) throw new MathException($"\"{num}\" has two decimal points");
                if (!double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    throw new MathException($"\"{num}\" isn't a number");
                list.Add(new Tok(K.Num, num, v));
                continue;
            }

            if (char.IsLetter(c) && c != 'π' && c != 'θ' || c == '_')
            {
                int st = i;
                while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_') && s[i] != 'π' && s[i] != 'θ') i++;
                string word = s[st..i];
                // sin⁻¹ / sin^-1 → asin
                if (FunctionNames.Contains("a" + word) || word is "sin" or "cos" or "tan" or "sinh" or "cosh" or "tanh")
                {
                    if (i + 1 < s.Length && s[i] == '⁻' && s[i + 1] == '¹') { word = "a" + word; i += 2; }
                    else if (i + 2 < s.Length && s[i] == '^' && (s[i + 1] == '-' || s[i + 1] == '−') && s[i + 2] == '1'
                             && i + 3 < s.Length && s[i + 3] == '(') { word = "a" + word; i += 3; }
                }
                foreach (var t in SplitWord(word, known)) list.Add(t);
                continue;
            }

            switch (c)
            {
                case 'π': list.Add(new Tok(K.Ident, "π")); break;
                case 'θ': list.Add(new Tok(K.Ident, "θ")); break;
                case '+': list.Add(new Tok(K.Op, "+")); break;
                case '-': case '−': case '–': list.Add(new Tok(K.Op, "-")); break;
                case '*': case '×': case '·': case '⋅': list.Add(new Tok(K.Op, "*")); break;
                case '/': case '÷': list.Add(new Tok(K.Op, "/")); break;
                case '^': list.Add(new Tok(K.Op, "^")); break;
                case '²': list.Add(new Tok(K.Op, "²")); break;
                case '³': list.Add(new Tok(K.Op, "³")); break;
                case '!':
                    if (i + 1 < s.Length && s[i + 1] == '=') { list.Add(new Tok(K.Op, "!=")); i++; }
                    else list.Add(new Tok(K.Op, "!"));
                    break;
                case '%': list.Add(new Tok(K.Op, "%")); break;
                case '°': list.Add(new Tok(K.Op, "°")); break;
                case 'ʳ': list.Add(new Tok(K.Op, "ʳ")); break;
                case '√': list.Add(new Tok(K.Ident, "sqrt")); break;
                case '∛': list.Add(new Tok(K.Ident, "cbrt")); break;
                case '(': case '[': list.Add(new Tok(K.LParen, "(")); break;
                case ')': case ']': list.Add(new Tok(K.RParen, ")")); break;
                case '{': list.Add(new Tok(K.LBrace, "{")); break;
                case '}': list.Add(new Tok(K.RBrace, "}")); break;
                case ',': list.Add(new Tok(K.Comma, ",")); break;
                case '|': list.Add(new Tok(K.Bar, "|")); break;
                case '<':
                    if (i + 1 < s.Length && s[i + 1] == '=') { list.Add(new Tok(K.Op, "<=")); i++; }
                    else list.Add(new Tok(K.Op, "<"));
                    break;
                case '>':
                    if (i + 1 < s.Length && s[i + 1] == '=') { list.Add(new Tok(K.Op, ">=")); i++; }
                    else list.Add(new Tok(K.Op, ">"));
                    break;
                case '≤': list.Add(new Tok(K.Op, "<=")); break;
                case '≥': list.Add(new Tok(K.Op, ">=")); break;
                case '≠': list.Add(new Tok(K.Op, "!=")); break;
                case '=':
                    if (i + 1 < s.Length && s[i + 1] == '=') i++;
                    list.Add(new Tok(K.Op, "="));
                    break;
                case '⁻':
                    if (i + 1 < s.Length && s[i + 1] == '¹') { list.Add(new Tok(K.Op, "⁻¹")); i++; break; }
                    throw new MathException("Didn't understand \"⁻\"");
                default:
                    throw new MathException($"Didn't understand \"{c}\"");
            }
            i++;
        }
        list.Add(new Tok(K.End, ""));
        return list;
    }

    private static bool IsGroup(string s, int i)
    {
        // a thousands separator: exactly three digits after the comma, then not a digit
        for (int k = 1; k <= 3; k++)
            if (i + k >= s.Length || !char.IsDigit(s[i + k])) return false;
        return i + 4 >= s.Length || !char.IsDigit(s[i + 4]);
    }

    /// <summary>"ax" → a·x and "xsin" → x·sin (like Desmos and the TI-Nspire graph entry), keeping known names whole.</summary>
    private static IEnumerable<Tok> SplitWord(string word, ICollection<string> known)
    {
        if (FunctionNames.Contains(word) || Constants.Contains(word) || (known != null && known.Contains(word))
            || IsUserFunctionName(word) || IsListName(word) || word is "and" or "or" or "not")
        {
            yield return new Tok(K.Ident, word);
            yield break;
        }
        int i = 0;
        while (i < word.Length)
        {
            if (char.IsDigit(word[i]))
            {
                int st = i;
                while (i < word.Length && char.IsDigit(word[i])) i++;
                yield return new Tok(K.Num, word[st..i], double.Parse(word[st..i], CultureInfo.InvariantCulture));
                continue;
            }
            // the longest function, known name or constant that starts here
            string match = null;
            foreach (var f in FunctionNames)
                if (f.Length >= 2 && string.CompareOrdinal(word, i, f, 0, f.Length) == 0 && (match == null || f.Length > match.Length))
                    match = f;
            if (known != null)
                foreach (var k in known)
                    if (k.Length > 1 && string.CompareOrdinal(word, i, k, 0, k.Length) == 0 && (match == null || k.Length > match.Length))
                        match = k;
            foreach (var k in Constants)
                if (k.Length > 1 && string.CompareOrdinal(word, i, k, 0, k.Length) == 0 && (match == null || k.Length > match.Length))
                    match = k;
            if (match != null) { yield return new Tok(K.Ident, match); i += match.Length; continue; }
            yield return new Tok(K.Ident, word[i].ToString());
            i++;
        }
    }

    public static bool IsUserFunctionName(string w) =>
        w.Length >= 2 && (w[0] == 'f' || w[0] == 'Y') && w[1..].All(char.IsDigit) && w.Length <= 3;

    public static bool IsListName(string w) =>
        w.Length == 2 && (w[0] == 'L' || w[0] == 'l') && w[1] >= '1' && w[1] <= '6';

    // ---------------------------------------------------------------- grammar

    private static Node ParseOr(List<Tok> t, ref int i)
    {
        var left = ParseAnd(t, ref i);
        while (t[i].Kind == K.Ident && t[i].Text == "or") { i++; left = new BinaryNode("or", left, ParseAnd(t, ref i)); }
        return left;
    }

    private static Node ParseAnd(List<Tok> t, ref int i)
    {
        var left = ParseCompare(t, ref i);
        while (t[i].Kind == K.Ident && t[i].Text == "and") { i++; left = new BinaryNode("and", left, ParseCompare(t, ref i)); }
        return left;
    }

    private static Node ParseCompare(List<Tok> t, ref int i)
    {
        var left = ParseSum(t, ref i);
        while (t[i].Kind == K.Op && t[i].Text is "<" or ">" or "<=" or ">=" or "=" or "!=")
        {
            string op = t[i++].Text;
            left = new BinaryNode(op, left, ParseSum(t, ref i));
        }
        return left;
    }

    private static Node ParseSum(List<Tok> t, ref int i)
    {
        var left = ParseTerm(t, ref i);
        while (t[i].Kind == K.Op && (t[i].Text == "+" || t[i].Text == "-"))
        {
            string op = t[i++].Text;
            left = new BinaryNode(op, left, ParseTerm(t, ref i));
        }
        return left;
    }

    private static bool StartsValue(Tok k) =>
        k.Kind is K.Num or K.LParen or K.LBrace
        || (k.Kind == K.Ident && k.Text is not ("and" or "or" or "nCr" or "nPr" or "mod"))
        || (k.Kind == K.Op && false);

    private static Node ParseTerm(List<Tok> t, ref int i)
    {
        var left = ParseUnary(t, ref i);
        while (true)
        {
            var k = t[i];
            if (k.Kind == K.Op && (k.Text == "*" || k.Text == "/"))
            {
                i++;
                left = new BinaryNode(k.Text, left, ParseUnary(t, ref i));
            }
            else if (k.Kind == K.Ident && k.Text is "nCr" or "nPr" or "mod" && !(i + 1 < t.Count && t[i + 1].Kind == K.LParen && LooksLikeCall(t, i)))
            {
                i++;
                left = new BinaryNode(k.Text, left, ParseUnary(t, ref i));
            }
            else if (StartsValue(k))
            {
                // implicit multiplication: 2x, 3(x+1), 2sin(x), (x+1)(x-1)
                left = new BinaryNode("*", left, ParsePower(t, ref i));
            }
            else if (k.Kind == K.Bar && _barDepth == 0)
            {
                left = new BinaryNode("*", left, ParsePower(t, ref i));
            }
            else break;
        }
        return left;
    }

    // "nCr(5,2)" is a call; "5 nCr 2" is infix
    private static bool LooksLikeCall(List<Tok> t, int i)
    {
        int depth = 0;
        for (int j = i + 1; j < t.Count; j++)
        {
            if (t[j].Kind == K.LParen) depth++;
            else if (t[j].Kind == K.RParen) { depth--; if (depth == 0) return false; }
            else if (t[j].Kind == K.Comma && depth == 1) return true;
        }
        return false;
    }

    [ThreadStatic] private static int _barDepth;

    private static Node ParseUnary(List<Tok> t, ref int i)
    {
        if (t[i].Kind == K.Op && t[i].Text == "-") { i++; return new UnaryNode('-', ParseUnary(t, ref i)); }
        if (t[i].Kind == K.Op && t[i].Text == "+") { i++; return ParseUnary(t, ref i); }
        if (t[i].Kind == K.Ident && t[i].Text == "not") { i++; var a = ParseUnary(t, ref i); return new BinaryNode("=", a, new NumNode(0)); }
        return ParsePower(t, ref i);
    }

    private static Node ParsePower(List<Tok> t, ref int i)
    {
        var b = ParsePostfix(t, ref i);
        if (t[i].Kind == K.Op && t[i].Text == "^")
        {
            i++;
            var e = ParseUnary(t, ref i); // right-associative: 2^3^2 = 2^9
            return new BinaryNode("^", b, e);
        }
        return b;
    }

    private static Node ParsePostfix(List<Tok> t, ref int i)
    {
        var v = ParsePrimary(t, ref i);
        while (t[i].Kind == K.Op)
        {
            switch (t[i].Text)
            {
                case "!": i++; v = new UnaryNode('!', v); continue;
                case "%": i++; v = new UnaryNode('%', v); continue;
                case "°": i++; v = new UnaryNode('°', v); continue;
                case "ʳ": i++; v = new UnaryNode('r', v); continue;
                case "²": i++; v = new BinaryNode("^", v, new NumNode(2)); continue;
                case "³": i++; v = new BinaryNode("^", v, new NumNode(3)); continue;
                case "⁻¹": i++; v = new BinaryNode("/", new NumNode(1), v); continue;
            }
            break;
        }
        return v;
    }

    private static Node ParsePrimary(List<Tok> t, ref int i)
    {
        var k = t[i];
        switch (k.Kind)
        {
            case K.Num:
                i++;
                return new NumNode(k.Value);
            case K.LParen:
            {
                i++;
                int saved = _barDepth;
                _barDepth = 0;
                Node e;
                try { e = ParseOr(t, ref i); }
                finally { _barDepth = saved; }
                if (t[i].Kind == K.RParen) i++;
                else if (t[i].Kind != K.End) throw new MathException("Missing )");
                return e;
            }
            case K.LBrace:
            {
                i++;
                var items = new List<Node>();
                if (t[i].Kind != K.RBrace)
                {
                    items.Add(ParseOr(t, ref i));
                    while (t[i].Kind == K.Comma) { i++; items.Add(ParseOr(t, ref i)); }
                }
                if (t[i].Kind == K.RBrace) i++;
                else if (t[i].Kind != K.End) throw new MathException("Missing }");
                return new ListNode(items.ToArray());
            }
            case K.Bar:
            {
                i++;
                _barDepth++;
                Node e;
                try { e = ParseSum(t, ref i); }
                finally { _barDepth--; }
                if (t[i].Kind == K.Bar) i++;
                else if (t[i].Kind != K.End) throw new MathException("Missing closing |");
                return new CallNode("abs", new[] { e });
            }
            case K.Ident:
            {
                i++;
                string name = k.Text;
                bool isFunc = FunctionNames.Contains(name) || IsUserFunctionName(name);
                if (t[i].Kind == K.LParen && (isFunc || (!Constants.Contains(name) && !IsListName(name) && IsCallable(name))))
                {
                    i++;
                    var args = new List<Node>();
                    if (t[i].Kind != K.RParen && t[i].Kind != K.End)
                    {
                        args.Add(ParseOr(t, ref i));
                        while (t[i].Kind == K.Comma) { i++; args.Add(ParseOr(t, ref i)); }
                    }
                    if (t[i].Kind == K.RParen) i++;
                    else if (t[i].Kind != K.End) throw new MathException("Missing )");
                    return new CallNode(name, args.ToArray());
                }
                if (isFunc && name is not ("rand"))
                {
                    if (t[i].Kind == K.End) throw new MathException($"{name} needs a value, like {name}(2)");
                    // "sin x", "√2", "ln 5": applies to the next value (power included: √x² = √(x²))
                    var arg = ParsePower(t, ref i);
                    return new CallNode(name, new[] { arg });
                }
                if (name == "rand") return new CallNode("rand", Array.Empty<Node>());
                return new VarNode(name);
            }
            case K.End:
                throw new MathException("The expression isn't finished");
            default:
                throw new MathException($"Didn't understand \"{k.Text}\"");
        }
    }

    // single letters followed by ( are multiplication (a(x+1)); f, g, h are functions when defined by the caller
    private static bool IsCallable(string name) => _userFns != null && _userFns.Contains(name);

    /// <summary>Formats a double the way a TI-84 does (up to 10 significant digits, then scientific).</summary>
    public static string Format(double v, int sig = 12)
    {
        if (double.IsNaN(v)) return "undefined";
        if (double.IsPositiveInfinity(v)) return "∞";
        if (double.IsNegativeInfinity(v)) return "−∞";
        string s = Num.Format(v, sig);
        return s.Replace("-", "−");
    }
}

// ============================================================ functions

public static class Functions
{
    public static bool BindsVariable(string name) => name is "nDeriv" or "fnInt" or "sum" or "prod" or "seq";

    public static double Factorial(double n)
    {
        if (n < 0 || n != System.Math.Floor(n))
        {
            if (n < 0 && n == System.Math.Floor(n)) return double.NaN;
            return Gamma(n + 1);
        }
        if (n > 170) return double.PositiveInfinity;
        double r = 1;
        for (int i = 2; i <= (int)n; i++) r *= i;
        return r;
    }

    // Lanczos approximation
    public static double Gamma(double x)
    {
        if (x < 0.5) return System.Math.PI / (System.Math.Sin(System.Math.PI * x) * Gamma(1 - x));
        x -= 1;
        double[] g = { 0.99999999999980993, 676.5203681218851, -1259.1392167224028, 771.32342877765313,
            -176.61502916214059, 12.507343278686905, -0.13857109526572012, 9.9843695780195716e-6, 1.5056327351493116e-7 };
        double a = g[0];
        double tt = x + 7.5;
        for (int i = 1; i < 9; i++) a += g[i] / (x + i);
        return System.Math.Sqrt(2 * System.Math.PI) * System.Math.Pow(tt, x + 0.5) * System.Math.Exp(-tt) * a;
    }

    public static double Pow(double a, double b)
    {
        // real odd roots of negatives: (-8)^(1/3) = -2, like the TI in real mode
        if (a < 0 && b != System.Math.Floor(b))
        {
            for (int q = 3; q <= 15; q += 2)
            {
                double p = b * q;
                if (System.Math.Abs(p - System.Math.Round(p)) < 1e-9)
                {
                    double r = System.Math.Pow(-a, b);
                    return ((long)System.Math.Round(p)) % 2 == 0 ? r : -r;
                }
            }
            return double.NaN;
        }
        return System.Math.Pow(a, b);
    }

    public static double NCr(double n, double r)
    {
        if (n < 0 || r < 0 || n != System.Math.Floor(n) || r != System.Math.Floor(r)) return double.NaN;
        if (r > n) return 0;
        r = System.Math.Min(r, n - r);
        double res = 1;
        for (int i = 1; i <= (int)r; i++) res = res * (n - r + i) / i;
        return System.Math.Round(res);
    }

    public static double NPr(double n, double r)
    {
        if (n < 0 || r < 0 || n != System.Math.Floor(n) || r != System.Math.Floor(r)) return double.NaN;
        if (r > n) return 0;
        double res = 1;
        for (int i = 0; i < (int)r; i++) res *= n - i;
        return res;
    }

    public static double Mod(double a, double b) => b == 0 ? a : a - b * System.Math.Floor(a / b);

    private static double ToRad(EvalContext c, double a) => c.Degrees ? a * System.Math.PI / 180 : a;
    private static double FromRad(EvalContext c, double a) => c.Degrees ? a * 180 / System.Math.PI : a;

    private static double Gcd(double a, double b)
    {
        a = System.Math.Abs(System.Math.Round(a)); b = System.Math.Abs(System.Math.Round(b));
        while (b > 0) { var t = a % b; a = b; b = t; }
        return a;
    }

    /// <summary>A list argument: a literal {..}, a list name (L1) or a seq(...).</summary>
    private static double[] ListArg(Node n, EvalContext c)
    {
        if (n is ListNode ln) return ln.EvalAll(c);
        if (n is VarNode v && c.Lists.TryGetValue(v.Name, out var l)) return l;
        if (n is CallNode cn && cn.Name == "seq") return Seq(cn, c);
        return null;
    }

    private static double[] Seq(CallNode cn, EvalContext c)
    {
        // seq(expr, var, start, end[, step])
        if (cn.Args.Length < 4 || cn.Args[1] is not VarNode v) throw new MathException("seq(expression, variable, start, end, step)");
        double a = cn.Args[2].Eval(c), b = cn.Args[3].Eval(c), step = cn.Args.Length > 4 ? cn.Args[4].Eval(c) : 1;
        if (step == 0 || (b - a) / step > 100000) throw new MathException("seq: too many items");
        var r = new List<double>();
        for (double k = a; step > 0 ? k <= b + 1e-12 : k >= b - 1e-12; k += step) r.Add(WithVar(c, v.Name, k, () => cn.Args[0].Eval(c)));
        return r.ToArray();
    }

    internal static double WithVar(EvalContext c, string name, double value, Func<double> body)
    {
        bool had = c.Locals.TryGetValue(name, out var old);
        c.Locals[name] = value;
        try { return body(); }
        finally { if (had) c.Locals[name] = old; else c.Locals.Remove(name); }
    }

    private static void Need(CallNode n, int min, int max = -1)
    {
        if (max < 0) max = min;
        if (n.Args.Length < min || n.Args.Length > max)
            throw new MathException(min == max ? $"{n.Name} needs {min} value{(min == 1 ? "" : "s")}" : $"{n.Name} needs {min} to {max} values");
    }

    public static double Call(CallNode n, EvalContext c)
    {
        if (++c.Depth > 200) { c.Depth--; throw new MathException("A function calls itself too many times"); }
        try { return CallInner(n, c); }
        finally { c.Depth--; }
    }

    private static double A(CallNode n, EvalContext c, int i) => n.Args[i].Eval(c);

    private static double CallInner(CallNode n, EvalContext c)
    {
        string f = n.Name;

        // user functions (graph rows): f1(3), Y1(3), g(2)
        if (Parser.IsUserFunctionName(f) || !Parser.FunctionNames.Contains(f))
        {
            Need(n, 1);
            double arg = A(n, c, 0);
            var r = c.UserFunction?.Invoke(f, arg);
            if (r == null) throw new MathException($"{f} isn't defined. Define it on the Graphing page.");
            return r.Value;
        }

        switch (f)
        {
            case "sin": Need(n, 1); return Clean(System.Math.Sin(ToRad(c, A(n, c, 0))));
            case "cos": Need(n, 1); return Clean(System.Math.Cos(ToRad(c, A(n, c, 0))));
            case "tan":
            {
                Need(n, 1);
                double x = ToRad(c, A(n, c, 0));
                if (System.Math.Abs(System.Math.Cos(x)) < 1e-15) return double.NaN;
                return Clean(System.Math.Tan(x));
            }
            case "sec": Need(n, 1); return 1 / System.Math.Cos(ToRad(c, A(n, c, 0)));
            case "csc": Need(n, 1); return 1 / System.Math.Sin(ToRad(c, A(n, c, 0)));
            case "cot": Need(n, 1); return 1 / System.Math.Tan(ToRad(c, A(n, c, 0)));
            case "asin": case "arcsin": Need(n, 1); return FromRad(c, System.Math.Asin(A(n, c, 0)));
            case "acos": case "arccos": Need(n, 1); return FromRad(c, System.Math.Acos(A(n, c, 0)));
            case "atan": case "arctan": Need(n, 1); return FromRad(c, System.Math.Atan(A(n, c, 0)));
            case "atan2": Need(n, 2); return FromRad(c, System.Math.Atan2(A(n, c, 0), A(n, c, 1)));
            case "sinh": Need(n, 1); return System.Math.Sinh(A(n, c, 0));
            case "cosh": Need(n, 1); return System.Math.Cosh(A(n, c, 0));
            case "tanh": Need(n, 1); return System.Math.Tanh(A(n, c, 0));
            case "asinh": Need(n, 1); return System.Math.Asinh(A(n, c, 0));
            case "acosh": Need(n, 1); return System.Math.Acosh(A(n, c, 0));
            case "atanh": Need(n, 1); return System.Math.Atanh(A(n, c, 0));
            case "deg": Need(n, 1); return A(n, c, 0) * 180 / System.Math.PI;
            case "rad": Need(n, 1); return A(n, c, 0) * System.Math.PI / 180;
            case "ln": Need(n, 1); return System.Math.Log(A(n, c, 0));
            case "log":
                Need(n, 1, 2);
                return n.Args.Length == 2 ? System.Math.Log(A(n, c, 0)) / System.Math.Log(A(n, c, 1)) : System.Math.Log10(A(n, c, 0));
            case "log2": Need(n, 1); return System.Math.Log2(A(n, c, 0));
            case "exp": Need(n, 1); return System.Math.Exp(A(n, c, 0));
            case "sqrt": Need(n, 1); return System.Math.Sqrt(A(n, c, 0));
            case "cbrt": Need(n, 1); return System.Math.Cbrt(A(n, c, 0));
            case "root": Need(n, 2); return Pow(A(n, c, 0), 1 / A(n, c, 1));
            case "abs":
            {
                Need(n, 1);
                return System.Math.Abs(A(n, c, 0));
            }
            case "floor": case "int": Need(n, 1); return System.Math.Floor(A(n, c, 0));
            case "ceil": Need(n, 1); return System.Math.Ceiling(A(n, c, 0));
            case "iPart": Need(n, 1); return System.Math.Truncate(A(n, c, 0));
            case "fPart": { Need(n, 1); double x = A(n, c, 0); return x - System.Math.Truncate(x); }
            case "round":
            {
                Need(n, 1, 2);
                int d = n.Args.Length == 2 ? (int)System.Math.Clamp(A(n, c, 1), 0, 15) : 0;
                return System.Math.Round(A(n, c, 0), d, MidpointRounding.AwayFromZero);
            }
            case "sign": Need(n, 1); return System.Math.Sign(A(n, c, 0));
            case "gcd": Need(n, 2); return Gcd(A(n, c, 0), A(n, c, 1));
            case "lcm": { Need(n, 2); double a = A(n, c, 0), b = A(n, c, 1); double g = Gcd(a, b); return g == 0 ? 0 : System.Math.Abs(System.Math.Round(a) * System.Math.Round(b)) / g; }
            case "mod": Need(n, 2); return Mod(A(n, c, 0), A(n, c, 1));
            case "rem": { Need(n, 2); double a = A(n, c, 0), b = A(n, c, 1); return b == 0 ? a : a - b * System.Math.Truncate(a / b); }
            case "nCr": Need(n, 2); return NCr(A(n, c, 0), A(n, c, 1));
            case "nPr": Need(n, 2); return NPr(A(n, c, 0), A(n, c, 1));
            case "factorial": Need(n, 1); return Factorial(A(n, c, 0));
            case "rand": return c.Random();
            case "randInt": Need(n, 2); return c.RandomInt((int)A(n, c, 0), (int)A(n, c, 1));
            case "randNorm":
            {
                Need(n, 2);
                double u1 = 1 - c.Random(), u2 = c.Random();
                return A(n, c, 0) + A(n, c, 1) * System.Math.Sqrt(-2 * System.Math.Log(u1)) * System.Math.Cos(2 * System.Math.PI * u2);
            }
            case "arg": Need(n, 2); return FromRad(c, System.Math.Atan2(A(n, c, 1), A(n, c, 0)));
            case "min": case "max":
            {
                if (n.Args.Length == 1)
                {
                    var l = ListArg(n.Args[0], c) ?? throw new MathException($"{f} needs a list or two numbers");
                    if (l.Length == 0) throw new MathException("The list is empty");
                    return f == "min" ? l.Min() : l.Max();
                }
                if (n.Args.Length < 2) throw new MathException($"{f} needs two numbers or a list");
                var vals = n.Args.Select(a => a.Eval(c));
                return f == "min" ? vals.Min() : vals.Max();
            }
            case "mean": case "median": case "stdDev": case "variance": case "dim":
            {
                Need(n, 1);
                var l = ListArg(n.Args[0], c) ?? throw new MathException($"{f} needs a list, like {f}(L1) or {f}({{1,2,3}})");
                return f switch
                {
                    "dim" => l.Length,
                    "mean" => l.Length == 0 ? double.NaN : l.Average(),
                    "median" => Stats.Median(l),
                    "stdDev" => System.Math.Sqrt(Stats.SampleVariance(l)),
                    _ => Stats.SampleVariance(l),
                };
            }
            case "sum": case "prod":
            {
                if (n.Args.Length == 1)
                {
                    var l = ListArg(n.Args[0], c) ?? throw new MathException($"{f} needs a list");
                    return f == "sum" ? l.Sum() : l.Aggregate(1.0, (p, x) => p * x);
                }
                // sum(expr, var, from, to)
                Need(n, 4);
                if (n.Args[1] is not VarNode v) throw new MathException($"{f}(expression, variable, from, to)");
                double a = A(n, c, 2), b = A(n, c, 3);
                if (b - a > 1_000_000) throw new MathException("Too many terms");
                double acc = f == "sum" ? 0 : 1;
                for (double k = System.Math.Ceiling(a - 1e-9); k <= b + 1e-9; k++)
                {
                    double term = WithVar(c, v.Name, k, () => n.Args[0].Eval(c));
                    acc = f == "sum" ? acc + term : acc * term;
                }
                return acc;
            }
            case "seq": throw new MathException("seq gives a list; use it inside sum, mean, max…");
            case "nDeriv":
            {
                // nDeriv(expression, variable, value[, h])
                Need(n, 3, 4);
                if (n.Args[1] is not VarNode v) throw new MathException("nDeriv(expression, variable, value)");
                double x0 = A(n, c, 2);
                return Analysis.Derivative(x => WithVar(c, v.Name, x, () => n.Args[0].Eval(c)), x0);
            }
            case "fnInt":
            {
                // fnInt(expression, variable, lower, upper)
                Need(n, 4);
                if (n.Args[1] is not VarNode v) throw new MathException("fnInt(expression, variable, lower, upper)");
                double a = A(n, c, 2), b = A(n, c, 3);
                return Analysis.Integrate(x => WithVar(c, v.Name, x, () => n.Args[0].Eval(c)), a, b);
            }
            case "piecewise":
            {
                // piecewise(value1, condition1, value2, condition2, ..., [otherwise])
                for (int i = 0; i + 1 < n.Args.Length; i += 2)
                    if (A(n, c, i + 1) != 0) return A(n, c, i);
                return n.Args.Length % 2 == 1 ? A(n, c, n.Args.Length - 1) : double.NaN;
            }
            case "normalpdf":
            {
                Need(n, 1, 3);
                double x = A(n, c, 0), mu = n.Args.Length > 1 ? A(n, c, 1) : 0, sd = n.Args.Length > 2 ? A(n, c, 2) : 1;
                return Stats.NormalPdf(x, mu, sd);
            }
            case "normalcdf":
            {
                Need(n, 2, 4);
                double lo = A(n, c, 0), hi = A(n, c, 1), mu = n.Args.Length > 2 ? A(n, c, 2) : 0, sd = n.Args.Length > 3 ? A(n, c, 3) : 1;
                return Stats.NormalCdf((hi - mu) / sd) - Stats.NormalCdf((lo - mu) / sd);
            }
            case "invNorm":
            {
                Need(n, 1, 3);
                double p = A(n, c, 0), mu = n.Args.Length > 1 ? A(n, c, 1) : 0, sd = n.Args.Length > 2 ? A(n, c, 2) : 1;
                return mu + sd * Stats.InverseNormal(p);
            }
            case "binompdf":
            {
                Need(n, 3);
                double trials = A(n, c, 0), p = A(n, c, 1), k = A(n, c, 2);
                return NCr(trials, k) * System.Math.Pow(p, k) * System.Math.Pow(1 - p, trials - k);
            }
            case "binomcdf":
            {
                Need(n, 3);
                double trials = A(n, c, 0), p = A(n, c, 1), k = A(n, c, 2), s = 0;
                for (int i = 0; i <= (int)k; i++) s += NCr(trials, i) * System.Math.Pow(p, i) * System.Math.Pow(1 - p, trials - i);
                return s;
            }
        }
        throw new MathException($"Unknown function {f}");
    }

    /// <summary>sin(π) = 1.2e-16 → 0, like a calculator.</summary>
    private static double Clean(double v) => System.Math.Abs(v) < 1e-14 ? 0 : v;
}
