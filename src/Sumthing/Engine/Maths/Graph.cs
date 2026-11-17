using System.Text.RegularExpressions;

namespace Sumthing.Maths;

public enum PlotKind { Empty, Function, Inequality, Parametric, Polar, Implicit, ImplicitInequality, Points, Error }
public enum LineStyle { Solid, Thick, Dashed, Dotted }

/// <summary>One line in the function list, as the user typed it.</summary>
public sealed class GraphRow
{
    public string Text { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public int ColorIndex { get; set; }
    public LineStyle Style { get; set; } = LineStyle.Solid;
}

/// <summary>A slider for a letter that has no value (Nspire / Desmos style): y = a·x² + b.</summary>
public sealed class SliderInfo
{
    public double Value { get; set; } = 1;
    public double Min { get; set; } = -10;
    public double Max { get; set; } = 10;
    public double Step { get; set; } = 0.1;
}

/// <summary>A row after it has been read: what kind of plot it is and the maths behind it.</summary>
public sealed class CompiledRow
{
    public int Index;
    public GraphRow Row;
    public PlotKind Kind;
    /// <summary>f(x) for functions and inequalities; r(θ) for polar; F(x,y) = left − right for relations.</summary>
    public Node Expr;
    /// <summary>Parametric x(t), y(t), or a single point's coordinates.</summary>
    public Node XExpr, YExpr;
    public string Relation = "=";
    public string Alias;
    public string Error;
    public double[] PointsX = Array.Empty<double>(), PointsY = Array.Empty<double>();
    public HashSet<string> FreeNames = new(StringComparer.Ordinal);

    public string Name => $"f{Index + 1}";
    public bool IsFunction => Kind is PlotKind.Function or PlotKind.Inequality;
    public bool IsStrict => Relation is "<" or ">";
    public bool ShadeAbove => Relation is ">" or ">=";
    public bool IsVisible => Row.Enabled && Kind != PlotKind.Empty && Kind != PlotKind.Error;
}

/// <summary>The TI-84 window: Xmin, Xmax, Xscl, Ymin, Ymax, Yscl.</summary>
public sealed class GraphWindow
{
    public double XMin { get; set; } = -10, XMax { get; set; } = 10, XScl { get; set; } = 1;
    public double YMin { get; set; } = -10, YMax { get; set; } = 10, YScl { get; set; } = 1;
    public double TMin { get; set; } = 0, TMax { get; set; } = 2 * System.Math.PI;
    public double ThetaMin { get; set; } = 0, ThetaMax { get; set; } = 2 * System.Math.PI;

    public GraphWindow Copy() => (GraphWindow)MemberwiseClone();
}

/// <summary>
/// Everything on the Graphing page that isn't drawing: the rows, sliders, window and the maths to read and analyse them.
/// </summary>
public sealed class GraphModel
{
    public List<GraphRow> Rows { get; } = new();
    public List<CompiledRow> Compiled { get; private set; } = new();
    public Dictionary<string, SliderInfo> Sliders { get; } = new(StringComparer.Ordinal);
    public GraphWindow Window { get; set; } = new();
    public EvalContext Context { get; }
    /// <summary>Bumped whenever something that changes the picture changes.</summary>
    public int Version { get; private set; }

    public GraphModel(EvalContext shared)
    {
        Context = shared;
        Context.UserFunction = CallUserFunction;
    }

    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "x", "X", "y", "Y", "t", "T", "θ", "theta", "n", "Ans", "ans", "π", "pi", "e",
    };

    // ---------------------------------------------------------------- reading the rows

    public void Recompile()
    {
        var aliases = new Dictionary<string, int>(StringComparer.Ordinal);
        // first pass: names like g(x)=...
        for (int i = 0; i < Rows.Count; i++)
        {
            var m = AliasRegex.Match(Rows[i].Text ?? "");
            if (m.Success && m.Groups[1].Value is not ("y" or "r" or "x")) aliases[m.Groups[1].Value] = i;
        }
        var fnNames = new HashSet<string>(aliases.Keys, StringComparer.Ordinal);
        for (int i = 0; i < Rows.Count; i++) fnNames.Add($"f{i + 1}");

        var list = new List<CompiledRow>();
        for (int i = 0; i < Rows.Count; i++) list.Add(Compile(Rows[i], i, fnNames));
        Compiled = list;

        // sliders: letters with no value
        var needed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in list)
            foreach (var n in c.FreeNames)
                if (!Reserved.Contains(n) && !fnNames.Contains(n) && !Context.Lists.ContainsKey(n) && !IsStored(n))
                    needed.Add(n);
        foreach (var n in needed)
            if (!Sliders.ContainsKey(n)) Sliders[n] = new SliderInfo();
        foreach (var n in Sliders.Keys.ToList())
            if (!needed.Contains(n)) Sliders.Remove(n);
        PushSliders();

        // a row whose maths can't be worked out at all gets an error now, not a blank graph
        foreach (var c in list)
            if (c.Kind is PlotKind.Function or PlotKind.Inequality or PlotKind.Polar or PlotKind.Implicit or PlotKind.ImplicitInequality)
                try { Probe(c); }
                catch (MathException ex) { c.Error = ex.Message; c.Kind = PlotKind.Error; }
        Version++;
    }

    /// <summary>Variables stored on the Scientific page (not sliders).</summary>
    public HashSet<string> StoredNames { get; } = new(StringComparer.Ordinal);
    private bool IsStored(string n) => StoredNames.Contains(n);

    public void PushSliders()
    {
        foreach (var kv in Sliders) Context.Vars[kv.Key] = kv.Value.Value;
        Version++;
    }

    public void Touch() => Version++;

    private void Probe(CompiledRow c)
    {
        double sx = Context.X, sy = Context.Y, st = Context.Theta;
        try
        {
            foreach (var x in new[] { 0.5, 1.7, -2.3 })
            {
                Context.X = x; Context.Y = 0.7; Context.Theta = x;
                c.Expr.Eval(Context);
            }
        }
        finally { Context.X = sx; Context.Y = sy; Context.Theta = st; }
    }

    private static readonly Regex AliasRegex = new(@"^\s*([A-Za-z][A-Za-z0-9]*)\s*\(\s*x\s*\)\s*=(.*)$");
    private static readonly Regex PolarRegex = new(@"^\s*r\s*(\(\s*(θ|theta)\s*\))?\s*=(.*)$");

    private CompiledRow Compile(GraphRow row, int index, HashSet<string> fnNames)
    {
        var c = new CompiledRow { Index = index, Row = row };
        string text = (row.Text ?? "").Trim();
        if (text.Length == 0) { c.Kind = PlotKind.Empty; return c; }
        var known = new HashSet<string>(Context.Vars.Keys, StringComparer.Ordinal);
        foreach (var l in Context.Lists.Keys) known.Add(l);
        try
        {
            // g(x) = ...
            var am = AliasRegex.Match(text);
            if (am.Success && am.Groups[1].Value is not ("y" or "r" or "x"))
            {
                c.Alias = am.Groups[1].Value;
                text = am.Groups[2].Value.Trim();
                c.Expr = Parser.Parse(text, known, fnNames);
                c.Kind = PlotKind.Function;
                c.Expr.CollectNames(c.FreeNames);
                if (c.FreeNames.Contains("y") || c.FreeNames.Contains("Y")) throw new MathException("A function of x can't use y");
                return c;
            }

            // r = 2sin(3θ)
            var pm = PolarRegex.Match(text);
            if (pm.Success)
            {
                c.Expr = Parser.Parse(pm.Groups[3].Value, known, fnNames);
                c.Kind = PlotKind.Polar;
                c.Expr.CollectNames(c.FreeNames);
                return c;
            }

            // (cos(t), sin(t)) parametric, (2, 3) a point, (L1, L2) a scatter plot, (1,2),(3,4) several points
            if (text.StartsWith('('))
            {
                var groups = SplitPointGroups(text);
                if (groups != null)
                {
                    CompilePoints(c, groups, known, fnNames);
                    return c;
                }
            }

            var (lhs, rel, rhs) = SplitRelation(text);
            if (rel == null)
            {
                c.Expr = Parser.Parse(text, known, fnNames);
                c.Expr.CollectNames(c.FreeNames);
                if (c.FreeNames.Contains("y") || c.FreeNames.Contains("Y"))
                    throw new MathException("Add = or < to make it an equation, like x²+y²=25");
                c.Kind = PlotKind.Function;
                return c;
            }

            string L = lhs.Trim(), R = rhs.Trim();
            if (L is "y" or "Y")
            {
                var e = Parser.Parse(R, known, fnNames);
                var names = new HashSet<string>(StringComparer.Ordinal);
                e.CollectNames(names);
                if (!names.Contains("y") && !names.Contains("Y"))
                {
                    c.Expr = e;
                    c.FreeNames = names;
                    c.Relation = rel;
                    c.Kind = rel == "=" ? PlotKind.Function : PlotKind.Inequality;
                    return c;
                }
            }
            if (R is "y" or "Y")
            {
                var e = Parser.Parse(L, known, fnNames);
                var names = new HashSet<string>(StringComparer.Ordinal);
                e.CollectNames(names);
                if (!names.Contains("y") && !names.Contains("Y"))
                {
                    c.Expr = e;
                    c.FreeNames = names;
                    c.Relation = Flip(rel);
                    c.Kind = rel == "=" ? PlotKind.Function : PlotKind.Inequality;
                    return c;
                }
            }
            // anything else is a relation in x and y: x²+y²=25, x=3, y²<x
            var left = Parser.Parse(L, known, fnNames);
            var right = Parser.Parse(R, known, fnNames);
            c.Expr = new BinaryNode("-", left, right);
            c.Expr.CollectNames(c.FreeNames);
            c.Relation = rel;
            c.Kind = rel == "=" ? PlotKind.Implicit : PlotKind.ImplicitInequality;
            return c;
        }
        catch (MathException ex)
        {
            c.Kind = PlotKind.Error;
            c.Error = ex.Message;
            return c;
        }
    }

    private static string Flip(string rel) => rel switch { "<" => ">", ">" => "<", "<=" => ">=", ">=" => "<=", _ => rel };

    /// <summary>Splits "lhs ≤ rhs" at the top-level relation sign.</summary>
    public static (string Lhs, string Rel, string Rhs) SplitRelation(string s)
    {
        int depth = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char ch = s[i];
            if (ch is '(' or '[' or '{') depth++;
            else if (ch is ')' or ']' or '}') depth--;
            if (depth != 0) continue;
            string rel = null;
            int len = 1;
            if (ch == '<' || ch == '>')
            {
                if (i + 1 < s.Length && s[i + 1] == '=') { rel = ch + "="; len = 2; }
                else rel = ch.ToString();
            }
            else if (ch == '≤') rel = "<=";
            else if (ch == '≥') rel = ">=";
            else if (ch == '=')
            {
                if (i > 0 && (s[i - 1] == '!' || s[i - 1] == '<' || s[i - 1] == '>')) continue;
                rel = "=";
                if (i + 1 < s.Length && s[i + 1] == '=') len = 2;
            }
            if (rel != null) return (s[..i], rel, s[(i + len)..]);
        }
        return (s, null, null);
    }

    /// <summary>"(a, b)" or "(a, b), (c, d)" → the inside of each pair; null if the text isn't that shape.</summary>
    private static List<(string A, string B)> SplitPointGroups(string s)
    {
        var groups = new List<(string, string)>();
        int i = 0;
        while (i < s.Length)
        {
            while (i < s.Length && (char.IsWhiteSpace(s[i]) || s[i] == ',')) i++;
            if (i >= s.Length) break;
            if (s[i] != '(') return null;
            int depth = 0, start = i, comma = -1;
            for (; i < s.Length; i++)
            {
                if (s[i] is '(' or '{' or '[') depth++;
                else if (s[i] is ')' or '}' or ']') { depth--; if (depth == 0) break; }
                else if (s[i] == ',' && depth == 1)
                {
                    if (comma >= 0) return null;
                    comma = i;
                }
            }
            if (i >= s.Length || comma < 0) return null;
            groups.Add((s[(start + 1)..comma], s[(comma + 1)..i]));
            i++;
        }
        return groups.Count > 0 ? groups : null;
    }

    private void CompilePoints(CompiledRow c, List<(string A, string B)> groups, HashSet<string> known, HashSet<string> fnNames)
    {
        if (groups.Count == 1)
        {
            var xa = Parser.Parse(groups[0].A, known, fnNames);
            var ya = Parser.Parse(groups[0].B, known, fnNames);
            var names = new HashSet<string>(StringComparer.Ordinal);
            xa.CollectNames(names);
            ya.CollectNames(names);
            c.FreeNames = names;
            // (L1, L2): a scatter plot of two lists
            if (xa is VarNode lx && ya is VarNode ly && Context.Lists.TryGetValue(lx.Name, out var xs) && Context.Lists.TryGetValue(ly.Name, out var ys))
            {
                int n = System.Math.Min(xs.Length, ys.Length);
                c.PointsX = xs.Take(n).ToArray();
                c.PointsY = ys.Take(n).ToArray();
                c.Kind = PlotKind.Points;
                c.FreeNames.Remove(lx.Name);
                c.FreeNames.Remove(ly.Name);
                return;
            }
            if (names.Contains("t") || names.Contains("T"))
            {
                c.XExpr = xa;
                c.YExpr = ya;
                c.Kind = PlotKind.Parametric;
                return;
            }
            if (names.Contains("x") || names.Contains("y")) throw new MathException("A point can't use x or y; for a curve use t, like (cos(t), sin(t))");
            c.XExpr = xa;
            c.YExpr = ya;
            c.Kind = PlotKind.Points;
            return;
        }
        var px = new List<Node>();
        var py = new List<Node>();
        foreach (var (a, b) in groups)
        {
            var xa = Parser.Parse(a, known, fnNames);
            var ya = Parser.Parse(b, known, fnNames);
            xa.CollectNames(c.FreeNames);
            ya.CollectNames(c.FreeNames);
            px.Add(xa);
            py.Add(ya);
        }
        if (c.FreeNames.Overlaps(new[] { "x", "y", "t" })) throw new MathException("Points can't use x, y or t");
        c.XExpr = new ListNode(px.ToArray());
        c.YExpr = new ListNode(py.ToArray());
        c.Kind = PlotKind.Points;
    }

    // ---------------------------------------------------------------- evaluating

    private double? CallUserFunction(string name, double arg)
    {
        var row = Compiled.FirstOrDefault(c => c.IsFunction && (c.Name == name || c.Alias == name || $"Y{c.Index + 1}" == name));
        if (row == null || row.Expr == null) return null;
        double saved = Context.X;
        Context.X = arg;
        try { return row.Expr.Eval(Context); }
        finally { Context.X = saved; }
    }

    /// <summary>y = f(x) for a function row. Never throws: problems give NaN.</summary>
    public double EvalFunction(CompiledRow c, double x)
    {
        Context.X = x;
        try { return c.Expr.Eval(Context); }
        catch (MathException) { return double.NaN; }
    }

    public Func<double, double> AsFunction(CompiledRow c) => x => EvalFunction(c, x);

    /// <summary>F(x, y) for a relation (left − right).</summary>
    public double EvalRelation(CompiledRow c, double x, double y)
    {
        Context.X = x; Context.Y = y;
        try { return c.Expr.Eval(Context); }
        catch (MathException) { return double.NaN; }
    }

    /// <summary>r(θ) for a polar row; θ is in radians, passed in degrees when the angle mode is degrees.</summary>
    public double EvalPolar(CompiledRow c, double thetaRad)
    {
        Context.Theta = Context.Degrees ? thetaRad * 180 / System.Math.PI : thetaRad;
        try { return c.Expr.Eval(Context); }
        catch (MathException) { return double.NaN; }
    }

    public (double X, double Y) EvalParametric(CompiledRow c, double t)
    {
        Context.T = t;
        try { return (c.XExpr.Eval(Context), c.YExpr.Eval(Context)); }
        catch (MathException) { return (double.NaN, double.NaN); }
    }

    /// <summary>The points a Points row draws.</summary>
    public (double[] X, double[] Y) EvalPoints(CompiledRow c)
    {
        if (c.XExpr == null) return (c.PointsX, c.PointsY);
        try
        {
            if (c.XExpr is ListNode lx && c.YExpr is ListNode ly) return (lx.EvalAll(Context), ly.EvalAll(Context));
            return (new[] { c.XExpr.Eval(Context) }, new[] { c.YExpr.Eval(Context) });
        }
        catch (MathException) { return (Array.Empty<double>(), Array.Empty<double>()); }
    }

    // ---------------------------------------------------------------- window presets (TI-84 ZOOM menu)

    public void ZoomStandard() { Window.XMin = -10; Window.XMax = 10; Window.YMin = -10; Window.YMax = 10; Window.XScl = Window.YScl = 1; Touch(); }

    public void ZoomDecimal() { Window.XMin = -4.7; Window.XMax = 4.7; Window.YMin = -3.1; Window.YMax = 3.1; Window.XScl = Window.YScl = 1; Touch(); }

    public void ZoomInteger() { Window.XMin = -47; Window.XMax = 47; Window.YMin = -31; Window.YMax = 31; Window.XScl = Window.YScl = 10; Touch(); }

    public void ZoomTrig()
    {
        double xr = Context.Degrees ? 352.5 : 352.5 * System.Math.PI / 180;
        Window.XMin = -xr; Window.XMax = xr; Window.XScl = Context.Degrees ? 90 : System.Math.PI / 2;
        Window.YMin = -4; Window.YMax = 4; Window.YScl = 1;
        Touch();
    }

    /// <summary>Equal units on both axes for a canvas of the given size.</summary>
    public void ZoomSquare(double width, double height)
    {
        if (width <= 0 || height <= 0) return;
        double cx = (Window.XMin + Window.XMax) / 2, cy = (Window.YMin + Window.YMax) / 2;
        double ux = (Window.XMax - Window.XMin) / width, uy = (Window.YMax - Window.YMin) / height;
        double u = System.Math.Max(ux, uy);
        Window.XMin = cx - u * width / 2; Window.XMax = cx + u * width / 2;
        Window.YMin = cy - u * height / 2; Window.YMax = cy + u * height / 2;
        Touch();
    }

    public void Zoom(double factor, double cx, double cy)
    {
        Window.XMin = cx + (Window.XMin - cx) * factor; Window.XMax = cx + (Window.XMax - cx) * factor;
        Window.YMin = cy + (Window.YMin - cy) * factor; Window.YMax = cy + (Window.YMax - cy) * factor;
        Touch();
    }

    /// <summary>ZoomFit: keeps X, fits Y to the functions in view.</summary>
    public void ZoomFit()
    {
        double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
        foreach (var c in Compiled.Where(c => c.IsVisible))
        {
            if (c.IsFunction)
            {
                var ys = new List<double>();
                for (int i = 0; i <= 400; i++)
                {
                    double y = EvalFunction(c, Window.XMin + (Window.XMax - Window.XMin) * i / 400);
                    if (!double.IsNaN(y) && !double.IsInfinity(y)) ys.Add(y);
                }
                if (ys.Count == 0) continue;
                ys.Sort();
                // ignore the extreme 1% so asymptotes don't flatten everything
                lo = System.Math.Min(lo, ys[(int)(ys.Count * 0.01)]);
                hi = System.Math.Max(hi, ys[(int)(ys.Count * 0.99)]);
            }
            else if (c.Kind == PlotKind.Points)
            {
                var (xs, ys) = EvalPoints(c);
                foreach (var y in ys) { lo = System.Math.Min(lo, y); hi = System.Math.Max(hi, y); }
            }
        }
        if (double.IsInfinity(lo) || double.IsInfinity(hi)) return;
        if (hi - lo < 1e-9) { lo -= 1; hi += 1; }
        double pad = (hi - lo) * 0.08;
        Window.YMin = lo - pad; Window.YMax = hi + pad;
        Touch();
    }

    /// <summary>ZoomStat: fits the window to every scatter plot.</summary>
    public void ZoomData()
    {
        var xs = new List<double>();
        var ys = new List<double>();
        foreach (var c in Compiled.Where(c => c.IsVisible && c.Kind == PlotKind.Points))
        {
            var (px, py) = EvalPoints(c);
            xs.AddRange(px.Where(v => !double.IsNaN(v)));
            ys.AddRange(py.Where(v => !double.IsNaN(v)));
        }
        if (xs.Count == 0) return;
        double x0 = xs.Min(), x1 = xs.Max(), y0 = ys.Min(), y1 = ys.Max();
        if (x1 - x0 < 1e-9) { x0 -= 1; x1 += 1; }
        if (y1 - y0 < 1e-9) { y0 -= 1; y1 += 1; }
        double px2 = (x1 - x0) * 0.1, py2 = (y1 - y0) * 0.1;
        Window.XMin = x0 - px2; Window.XMax = x1 + px2; Window.YMin = y0 - py2; Window.YMax = y1 + py2;
        Touch();
    }

    // ---------------------------------------------------------------- table (TI TABLE)

    public List<double[]> Table(double start, double step, int count, IList<CompiledRow> rows)
    {
        var result = new List<double[]>();
        for (int i = 0; i < count; i++)
        {
            double x = Analysis.Snap(start + i * step);
            var line = new double[rows.Count + 1];
            line[0] = x;
            for (int k = 0; k < rows.Count; k++) line[k + 1] = EvalFunction(rows[k], x);
            result.Add(line);
        }
        return result;
    }

    // ---------------------------------------------------------------- points of interest

    /// <summary>Zeros, turning points, y-intercepts and intersections of the visible functions, within the window.</summary>
    public List<(FoundPoint Point, int Row)> PointsOfInterest()
    {
        var list = new List<(FoundPoint, int)>();
        var fns = Compiled.Where(c => c.IsVisible && c.Kind == PlotKind.Function).ToList();
        double a = Window.XMin, b = Window.XMax;
        foreach (var c in fns)
        {
            var f = AsFunction(c);
            foreach (var z in Analysis.Zeros(f, a, b, 600)) list.Add((new FoundPoint(z, 0, "Zero"), c.Index));
            foreach (var e in Analysis.Extrema(f, a, b, 600)) list.Add((e, c.Index));
            if (a < 0 && b > 0)
            {
                double y0 = f(0);
                if (!double.IsNaN(y0) && !double.IsInfinity(y0) && list.All(p => !(p.Item1.X == 0 && p.Item2 == c.Index)))
                    list.Add((new FoundPoint(0, Analysis.Snap(y0), "y-intercept"), c.Index));
            }
        }
        for (int i = 0; i < fns.Count; i++)
            for (int j = i + 1; j < fns.Count; j++)
                foreach (var p in Analysis.Intersections(AsFunction(fns[i]), AsFunction(fns[j]), a, b))
                    list.Add((p, fns[i].Index));
        return list.Where(p => p.Item1.Y >= Window.YMin - 1e-9 && p.Item1.Y <= Window.YMax + 1e-9).ToList();
    }
}
