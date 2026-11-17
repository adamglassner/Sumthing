namespace Sumthing.Maths;

/// <summary>A point the graph found: a zero, a turning point, an intersection.</summary>
public readonly record struct FoundPoint(double X, double Y, string Kind);

/// <summary>
/// Numerical analysis like the TI-84's CALC menu: value, zero, minimum, maximum, intersect, dy/dx and ∫f(x)dx.
/// Works on any function of x; looks across an interval so it finds every answer in view, not just one.
/// </summary>
public static class Analysis
{
    private static bool Ok(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

    /// <summary>dy/dx by a fourth-order central difference.</summary>
    public static double Derivative(Func<double, double> f, double x)
    {
        double h = 1e-3 * System.Math.Max(1, System.Math.Abs(x));
        double a = f(x - 2 * h), b = f(x - h), c = f(x + h), d = f(x + 2 * h);
        if (!Ok(a) || !Ok(d))
        {
            if (!Ok(b) || !Ok(c)) return double.NaN;
            return (c - b) / (2 * h);
        }
        double r = (a - 8 * b + 8 * c - d) / (12 * h);
        return System.Math.Abs(r) < 1e-11 ? 0 : r;
    }

    public static double SecondDerivative(Func<double, double> f, double x)
    {
        double h = 1e-3 * System.Math.Max(1, System.Math.Abs(x));
        return (f(x + h) - 2 * f(x) + f(x - h)) / (h * h);
    }

    /// <summary>∫ f from a to b, adaptive Gauss–Kronrod (15 points). Handles a > b.</summary>
    public static double Integrate(Func<double, double> f, double a, double b)
    {
        if (a == b) return 0;
        if (!Ok(a) || !Ok(b)) return double.NaN;
        if (a > b) return -Integrate(f, b, a);
        double total = 0;
        int segments = 16;
        double w = (b - a) / segments;
        for (int i = 0; i < segments; i++)
            total += Adaptive(f, a + i * w, a + (i + 1) * w, 1e-11, 0);
        return System.Math.Abs(total) < 1e-13 ? 0 : total;
    }

    private static readonly double[] XGK = { 0.991455371120812639, 0.949107912342758525, 0.864864423359769073, 0.741531185599394440, 0.586087235467691130, 0.405845151377397167, 0.207784955007898468, 0.0 };
    private static readonly double[] WGK = { 0.022935322010529225, 0.063092092629978553, 0.104790010322250184, 0.140653259715525919, 0.169004726639267903, 0.190350578064785410, 0.204432940075298892, 0.209482141084727828 };
    private static readonly double[] WG = { 0.129484966168869693, 0.279705391489276668, 0.381830050505118945, 0.417959183673469388 };

    private static double Adaptive(Func<double, double> f, double a, double b, double tol, int depth)
    {
        double c = (a + b) / 2, h = (b - a) / 2;
        double fc = f(c);
        double kron = WGK[7] * fc, gauss = WG[3] * fc;
        for (int j = 0; j < 7; j++)
        {
            double dx = h * XGK[j];
            double f1 = f(c - dx), f2 = f(c + dx);
            kron += WGK[j] * (f1 + f2);
            if (j % 2 == 1) gauss += WG[j / 2] * (f1 + f2);
        }
        kron *= h; gauss *= h;
        if (!Ok(kron)) return double.NaN;
        if (depth > 40 || System.Math.Abs(kron - gauss) <= System.Math.Max(tol, 1e-14 * System.Math.Abs(kron)))
            return kron;
        return Adaptive(f, a, c, tol / 2, depth + 1) + Adaptive(f, c, b, tol / 2, depth + 1);
    }

    /// <summary>Every zero of f in [a, b]: sign changes, plus touching zeros like x² at 0.</summary>
    public static List<double> Zeros(Func<double, double> f, double a, double b, int samples = 800)
    {
        var found = new List<double>();
        double step = (b - a) / samples;
        double x0 = a, y0 = f(a);
        double scale = 0;
        var ys = new double[samples + 1];
        ys[0] = y0;
        for (int i = 1; i <= samples; i++) { ys[i] = f(a + i * step); if (Ok(ys[i])) scale = System.Math.Max(scale, System.Math.Abs(ys[i])); }
        if (scale == 0) scale = 1;

        for (int i = 1; i <= samples; i++)
        {
            double x1 = a + i * step, y1 = ys[i];
            if (Ok(y0) && Ok(y1))
            {
                if (y0 == 0) Add(found, x0, step);
                else if (System.Math.Sign(y0) != System.Math.Sign(y1) && y1 != 0)
                {
                    double r = Bisect(f, x0, x1, y0);
                    // a sign change through an asymptote (tan, 1/x) isn't a zero
                    if (Ok(r) && System.Math.Abs(f(r)) < 1e-6 * System.Math.Max(1, System.Math.Max(System.Math.Abs(y0), System.Math.Abs(y1)))) Add(found, r, step);
                }
            }
            // touching zero: |f| has a local minimum near 0 without a sign change
            if (i >= 2 && Ok(ys[i - 2]) && Ok(y0) && Ok(y1)
                && System.Math.Abs(y0) <= System.Math.Abs(ys[i - 2]) && System.Math.Abs(y0) <= System.Math.Abs(y1)
                && System.Math.Sign(ys[i - 2]) == System.Math.Sign(y1) && System.Math.Sign(y0) == System.Math.Sign(y1))
            {
                double m = GoldenMin(x => System.Math.Abs(f(x)), x0 - step, x1);
                double fm = f(m);
                if (Ok(fm) && System.Math.Abs(fm) < 1e-9 * System.Math.Max(1, scale)) Add(found, m, step);
            }
            x0 = x1; y0 = y1;
        }
        if (Ok(y0) && y0 == 0) Add(found, b, step);
        found.Sort();
        return found;
    }

    private static void Add(List<double> list, double x, double step)
    {
        double clean = Snap(x);
        if (list.All(v => System.Math.Abs(v - clean) > step * 0.5)) list.Add(clean);
    }

    /// <summary>Tidies 1.9999999999 → 2 so answers read like a calculator's.</summary>
    public static double Snap(double x)
    {
        double r = System.Math.Round(x, 9);
        return System.Math.Abs(r - x) < 1e-9 ? (r == 0 ? 0 : r) : x;
    }

    private static double Bisect(Func<double, double> f, double a, double b, double fa)
    {
        for (int k = 0; k < 200; k++)
        {
            double m = (a + b) / 2;
            double fm = f(m);
            if (!Ok(fm)) return double.NaN;
            if (fm == 0 || (b - a) / 2 < 1e-15 * System.Math.Max(1, System.Math.Abs(m))) return m;
            if (System.Math.Sign(fm) == System.Math.Sign(fa)) { a = m; fa = fm; }
            else b = m;
        }
        return (a + b) / 2;
    }

    private static double GoldenMin(Func<double, double> f, double a, double b)
    {
        const double g = 0.6180339887498949;
        double c = b - g * (b - a), d = a + g * (b - a);
        double fc = f(c), fd = f(d);
        for (int k = 0; k < 200 && System.Math.Abs(b - a) > 1e-13 * System.Math.Max(1, System.Math.Abs(c)); k++)
        {
            if (fc < fd || double.IsNaN(fd)) { b = d; d = c; fd = fc; c = b - g * (b - a); fc = f(c); }
            else { a = c; c = d; fc = fd; d = a + g * (b - a); fd = f(d); }
        }
        return (a + b) / 2;
    }

    /// <summary>Local minima and maxima of f in (a, b).</summary>
    public static List<FoundPoint> Extrema(Func<double, double> f, double a, double b, int samples = 800)
    {
        var found = new List<FoundPoint>();
        double step = (b - a) / samples;
        var ys = new double[samples + 1];
        for (int i = 0; i <= samples; i++) ys[i] = f(a + i * step);
        double range = 0;
        foreach (var y in ys) if (Ok(y)) range = System.Math.Max(range, System.Math.Abs(y));
        for (int i = 1; i < samples; i++)
        {
            double l = ys[i - 1], m = ys[i], r = ys[i + 1];
            if (!Ok(l) || !Ok(m) || !Ok(r)) continue;
            bool isMax = m >= l && m >= r && (m > l || m > r);
            bool isMin = m <= l && m <= r && (m < l || m < r);
            if (!isMax && !isMin) continue;
            double lo = a + (i - 1) * step, hi = a + (i + 1) * step;
            double x = isMin ? GoldenMin(f, lo, hi) : GoldenMin(t => -f(t), lo, hi);
            double y = f(x);
            if (!Ok(y)) continue;
            // a cusp at an asymptote isn't a turning point
            if (range > 0 && System.Math.Abs(y) > range * 1.5) continue;
            x = Snap(x);
            y = Snap(f(x));
            if (found.Any(p => System.Math.Abs(p.X - x) < step)) continue;
            found.Add(new FoundPoint(x, y, isMin ? "Minimum" : "Maximum"));
        }
        return found;
    }

    /// <summary>Where f and g cross in [a, b].</summary>
    public static List<FoundPoint> Intersections(Func<double, double> f, Func<double, double> g, double a, double b)
    {
        var xs = Zeros(x => f(x) - g(x), a, b);
        return xs.Select(x => new FoundPoint(x, Snap(f(x)), "Intersection")).Where(p => Ok(p.Y)).ToList();
    }
}
