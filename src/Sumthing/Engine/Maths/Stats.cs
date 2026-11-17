namespace Sumthing.Maths;

/// <summary>1-Var Stats, like the TI-84 (quartiles use the same method: medians of the lower and upper halves).</summary>
public sealed class OneVarResult
{
    public int N;
    public double Mean, Sum, SumSq, Sx, SigmaX, Min, Q1, Median, Q3, Max;
}

/// <summary>2-Var Stats.</summary>
public sealed class TwoVarResult
{
    public int N;
    public double MeanX, SumX, SumX2, Sx, SigmaX, MeanY, SumY, SumY2, Sy, SigmaY, SumXY, R;
}

public enum RegressionModel { Linear, Quadratic, Cubic, Quartic, Exponential, Logarithmic, Power }

public sealed class RegressionResult
{
    public RegressionModel Model;
    /// <summary>Coefficients a, b, c, d, e in the order the TI shows them.</summary>
    public double[] Coefficients = Array.Empty<double>();
    /// <summary>r (Linear, Exponential, Logarithmic, Power); NaN for polynomials.</summary>
    public double R = double.NaN;
    public double R2;
    /// <summary>The fitted equation as text Sumthing can graph, e.g. "2.5x+1.2".</summary>
    public string Equation = "";
    /// <summary>The equation in the TI's form, e.g. "y = ax + b".</summary>
    public string Form = "";
    public Func<double, double> Predict = _ => double.NaN;
}

public static class Stats
{
    public static double Median(double[] v)
    {
        if (v.Length == 0) return double.NaN;
        var s = v.OrderBy(x => x).ToArray();
        int n = s.Length;
        return n % 2 == 1 ? s[n / 2] : (s[n / 2 - 1] + s[n / 2]) / 2;
    }

    public static double SampleVariance(double[] v)
    {
        if (v.Length < 2) return double.NaN;
        double m = v.Average();
        return v.Sum(x => (x - m) * (x - m)) / (v.Length - 1);
    }

    public static OneVarResult OneVar(double[] v)
    {
        if (v.Length == 0) throw new MathException("The list is empty");
        var s = v.OrderBy(x => x).ToArray();
        int n = s.Length;
        double mean = s.Average();
        double ss = s.Sum(x => (x - mean) * (x - mean));
        var r = new OneVarResult
        {
            N = n,
            Mean = mean,
            Sum = s.Sum(),
            SumSq = s.Sum(x => x * x),
            Sx = n > 1 ? System.Math.Sqrt(ss / (n - 1)) : double.NaN,
            SigmaX = System.Math.Sqrt(ss / n),
            Min = s[0],
            Max = s[^1],
            Median = Median(s),
        };
        if (n >= 2)
        {
            // TI method: lower half and upper half exclude the median when n is odd
            var lower = s.Take(n / 2).ToArray();
            var upper = s.Skip((n + 1) / 2).ToArray();
            r.Q1 = Median(lower);
            r.Q3 = Median(upper);
        }
        else { r.Q1 = r.Q3 = s[0]; }
        return r;
    }

    public static TwoVarResult TwoVar(double[] x, double[] y)
    {
        Check(x, y, 2);
        int n = x.Length;
        double mx = x.Average(), my = y.Average();
        double sxx = x.Sum(v => (v - mx) * (v - mx)), syy = y.Sum(v => (v - my) * (v - my));
        double sxy = 0;
        for (int i = 0; i < n; i++) sxy += (x[i] - mx) * (y[i] - my);
        return new TwoVarResult
        {
            N = n,
            MeanX = mx, SumX = x.Sum(), SumX2 = x.Sum(v => v * v), Sx = System.Math.Sqrt(sxx / (n - 1)), SigmaX = System.Math.Sqrt(sxx / n),
            MeanY = my, SumY = y.Sum(), SumY2 = y.Sum(v => v * v), Sy = System.Math.Sqrt(syy / (n - 1)), SigmaY = System.Math.Sqrt(syy / n),
            SumXY = x.Zip(y, (a, b) => a * b).Sum(),
            R = sxx == 0 || syy == 0 ? double.NaN : sxy / System.Math.Sqrt(sxx * syy),
        };
    }

    private static void Check(double[] x, double[] y, int min)
    {
        if (x.Length != y.Length) throw new MathException($"The two lists have different lengths ({x.Length} and {y.Length})");
        if (x.Length < min) throw new MathException($"Needs at least {min} points");
    }

    // ---------------------------------------------------------------- regressions

    public static RegressionResult Fit(RegressionModel model, double[] x, double[] y)
    {
        switch (model)
        {
            case RegressionModel.Linear:
            {
                Check(x, y, 2);
                var (a, b, r) = LinearFit(x, y);
                return new RegressionResult
                {
                    Model = model, Coefficients = new[] { a, b }, R = r, R2 = r * r, Form = "y = ax + b",
                    Equation = $"{F(a)}x{Signed(b)}", Predict = t => a * t + b,
                };
            }
            case RegressionModel.Quadratic:
            case RegressionModel.Cubic:
            case RegressionModel.Quartic:
            {
                int deg = model == RegressionModel.Quadratic ? 2 : model == RegressionModel.Cubic ? 3 : 4;
                Check(x, y, deg + 1);
                var c = PolyFit(x, y, deg); // highest power first
                double R2 = RSquared(x, y, t => Horner(c, t));
                string eq = "";
                for (int k = 0; k <= deg; k++)
                {
                    int p = deg - k;
                    string term = p == 0 ? "" : p == 1 ? "x" : $"x^{p}";
                    eq += k == 0 ? $"{F(c[k])}{term}" : $"{Signed(c[k])}{term}";
                }
                string form = deg switch { 2 => "y = ax² + bx + c", 3 => "y = ax³ + bx² + cx + d", _ => "y = ax⁴ + bx³ + cx² + dx + e" };
                return new RegressionResult { Model = model, Coefficients = c, R2 = R2, Form = form, Equation = eq, Predict = t => Horner(c, t) };
            }
            case RegressionModel.Exponential:
            {
                Check(x, y, 2);
                if (y.Any(v => v <= 0)) throw new MathException("Exponential fits need every y above 0");
                var (m, k, r) = LinearFit(x, y.Select(v => System.Math.Log(v)).ToArray());
                double a = System.Math.Exp(k), b = System.Math.Exp(m);
                return new RegressionResult
                {
                    Model = model, Coefficients = new[] { a, b }, R = r, R2 = r * r, Form = "y = a·bˣ",
                    Equation = $"{F(a)}*{F(b)}^x", Predict = t => a * System.Math.Pow(b, t),
                };
            }
            case RegressionModel.Logarithmic:
            {
                Check(x, y, 2);
                if (x.Any(v => v <= 0)) throw new MathException("Logarithmic fits need every x above 0");
                var (b, a, r) = LinearFit(x.Select(v => System.Math.Log(v)).ToArray(), y);
                return new RegressionResult
                {
                    Model = model, Coefficients = new[] { a, b }, R = r, R2 = r * r, Form = "y = a + b·ln(x)",
                    Equation = $"{F(a)}{Signed(b)}ln(x)", Predict = t => a + b * System.Math.Log(t),
                };
            }
            case RegressionModel.Power:
            {
                Check(x, y, 2);
                if (x.Any(v => v <= 0) || y.Any(v => v <= 0)) throw new MathException("Power fits need every x and y above 0");
                var (b, k, r) = LinearFit(x.Select(v => System.Math.Log(v)).ToArray(), y.Select(v => System.Math.Log(v)).ToArray());
                double a = System.Math.Exp(k);
                return new RegressionResult
                {
                    Model = model, Coefficients = new[] { a, b }, R = r, R2 = r * r, Form = "y = a·xᵇ",
                    Equation = $"{F(a)}*x^{F(b)}", Predict = t => a * System.Math.Pow(t, b),
                };
            }
        }
        throw new MathException("Unknown model");
    }

    /// <summary>Least squares y = slope·x + intercept, with r.</summary>
    private static (double Slope, double Intercept, double R) LinearFit(double[] x, double[] y)
    {
        int n = x.Length;
        double mx = x.Average(), my = y.Average();
        double sxx = 0, sxy = 0, syy = 0;
        for (int i = 0; i < n; i++)
        {
            sxx += (x[i] - mx) * (x[i] - mx);
            sxy += (x[i] - mx) * (y[i] - my);
            syy += (y[i] - my) * (y[i] - my);
        }
        if (sxx == 0) throw new MathException("Every x is the same, so there's no line to fit");
        double slope = sxy / sxx;
        double r = syy == 0 ? 1 : sxy / System.Math.Sqrt(sxx * syy);
        return (slope, my - slope * mx, r);
    }

    /// <summary>Polynomial least squares (normal equations, Gaussian elimination with pivoting). Highest power first.</summary>
    private static double[] PolyFit(double[] x, double[] y, int deg)
    {
        int m = deg + 1;
        var A = new double[m, m + 1];
        for (int r = 0; r < m; r++)
        {
            for (int c = 0; c < m; c++)
            {
                double s = 0;
                for (int i = 0; i < x.Length; i++) s += System.Math.Pow(x[i], r + c);
                A[r, c] = s;
            }
            double t = 0;
            for (int i = 0; i < x.Length; i++) t += y[i] * System.Math.Pow(x[i], r);
            A[r, m] = t;
        }
        for (int col = 0; col < m; col++)
        {
            int piv = col;
            for (int r = col + 1; r < m; r++) if (System.Math.Abs(A[r, col]) > System.Math.Abs(A[piv, col])) piv = r;
            if (System.Math.Abs(A[piv, col]) < 1e-300) throw new MathException("These points can't be fitted with that model");
            for (int c = 0; c <= m; c++) (A[col, c], A[piv, c]) = (A[piv, c], A[col, c]);
            for (int r = 0; r < m; r++)
            {
                if (r == col) continue;
                double f = A[r, col] / A[col, col];
                for (int c = col; c <= m; c++) A[r, c] -= f * A[col, c];
            }
        }
        var low = new double[m];
        for (int i = 0; i < m; i++) low[i] = Clean(A[i, m] / A[i, i]);
        return Enumerable.Reverse(low).ToArray();
    }

    private static double Clean(double v) => System.Math.Abs(v) < 1e-10 ? 0 : System.Math.Abs(v - System.Math.Round(v)) < 1e-9 ? System.Math.Round(v) : v;

    private static double Horner(double[] c, double x)
    {
        double r = 0;
        foreach (var k in c) r = r * x + k;
        return r;
    }

    private static double RSquared(double[] x, double[] y, Func<double, double> f)
    {
        double my = y.Average(), ssTot = 0, ssRes = 0;
        for (int i = 0; i < x.Length; i++)
        {
            ssTot += (y[i] - my) * (y[i] - my);
            ssRes += (y[i] - f(x[i])) * (y[i] - f(x[i]));
        }
        return ssTot == 0 ? 1 : 1 - ssRes / ssTot;
    }

    private static string F(double v) => System.Math.Round(v, 10).ToString("0.##########", System.Globalization.CultureInfo.InvariantCulture);
    private static string Signed(double v) => v < 0 ? "-" + F(-v) : "+" + F(v);

    // ---------------------------------------------------------------- distributions

    public static double NormalPdf(double x, double mu, double sd) =>
        System.Math.Exp(-0.5 * ((x - mu) / sd) * ((x - mu) / sd)) / (sd * System.Math.Sqrt(2 * System.Math.PI));

    /// <summary>Standard normal CDF.</summary>
    public static double NormalCdf(double z)
    {
        if (double.IsPositiveInfinity(z) || z > 40) return 1;
        if (double.IsNegativeInfinity(z) || z < -40) return 0;
        return 0.5 * Erfc(-z / System.Math.Sqrt(2));
    }

    // Complementary error function to ~15 digits: a positive series below 3, a continued fraction above.
    private static double Erfc(double x)
    {
        if (x < 0) return 2 - Erfc(-x);
        if (x < 3)
        {
            // erf(x) = 2/√π · e^(−x²) · Σ 2ⁿ x^(2n+1) / (1·3·5···(2n+1))
            double term = x, sum = x;
            for (int n = 1; n < 200; n++)
            {
                term *= 2 * x * x / (2 * n + 1);
                sum += term;
                if (term < 1e-17 * sum) break;
            }
            return 1 - 2 / System.Math.Sqrt(System.Math.PI) * System.Math.Exp(-x * x) * sum;
        }
        double k = x;
        for (int n = 60; n >= 1; n--) k = x + (n / 2.0) / k;
        return System.Math.Exp(-x * x) / (System.Math.Sqrt(System.Math.PI) * k);
    }

    /// <summary>Inverse of the standard normal CDF (Acklam's algorithm, refined by one Newton step).</summary>
    public static double InverseNormal(double p)
    {
        if (p <= 0 || p >= 1) return double.NaN;
        double[] a = { -3.969683028665376e+01, 2.209460984245205e+02, -2.759285104469687e+02, 1.383577518672690e+02, -3.066479806614716e+01, 2.506628277459239e+00 };
        double[] b = { -5.447609879822406e+01, 1.615858368580409e+02, -1.556989798598866e+02, 6.680131188771972e+01, -1.328068155288572e+01 };
        double[] c = { -7.784894002430293e-03, -3.223964580411365e-01, -2.400758277161838e+00, -2.549732539343734e+00, 4.374664141464968e+00, 2.938163982698783e+00 };
        double[] d = { 7.784695709041462e-03, 3.224671290700398e-01, 2.445134137142996e+00, 3.754408661907416e+00 };
        double q, x;
        if (p < 0.02425)
        {
            q = System.Math.Sqrt(-2 * System.Math.Log(p));
            x = (((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) / ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1);
        }
        else if (p > 1 - 0.02425)
        {
            q = System.Math.Sqrt(-2 * System.Math.Log(1 - p));
            x = -(((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) / ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1);
        }
        else
        {
            q = p - 0.5;
            double r = q * q;
            x = (((((a[0] * r + a[1]) * r + a[2]) * r + a[3]) * r + a[4]) * r + a[5]) * q / (((((b[0] * r + b[1]) * r + b[2]) * r + b[3]) * r + b[4]) * r + 1);
        }
        double e = NormalCdf(x) - p;
        double u = e * System.Math.Sqrt(2 * System.Math.PI) * System.Math.Exp(x * x / 2);
        return x - u / (1 + x * u / 2);
    }
}
