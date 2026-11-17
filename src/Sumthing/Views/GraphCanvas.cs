using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Sumthing.Maths;
using Sumthing.Services;

namespace Sumthing.Views;

/// <summary>A labelled point the user asked for (Analyze results, Value, ...).</summary>
public sealed class GraphMark
{
    public double X, Y;
    public string Label = "";
    public int ColorIndex;
}

/// <summary>
/// Draws the graph and handles the mouse: drag to pan, wheel to zoom, Shift+drag for a zoom box (ZBox),
/// trace along a curve, hover/click points of interest. Arrow keys trace (or pan), + and − zoom.
/// </summary>
public sealed class GraphCanvas : FrameworkElement
{
    public GraphModel Model { get; set; }

    public bool TraceOn { get; set; }
    public int TraceRow { get; set; } = -1;
    public double TraceValue { get; set; }  // x, t or θ depending on the row
    public bool ShowGrid { get; set; } = true;
    public bool ShowAxes { get; set; } = true;
    public bool ShowPointsOfInterest { get; set; } = true;
    public List<GraphMark> Marks { get; } = new();
    /// <summary>Tangent line from dy/dx: row, x.</summary>
    public (int Row, double X)? Tangent { get; set; }
    /// <summary>Shaded area from ∫f(x)dx: row, from, to.</summary>
    public (int Row, double A, double B)? Integral { get; set; }

    public event Action ViewChanged;
    public event Action<string> TraceChanged;
    public event Action<double, double> PointerMoved;

    private List<(FoundPoint Point, int Row)> _poi = new();
    private readonly HashSet<(double, double)> _pinned = new();
    private (FoundPoint Point, int Row)? _hover;
    private readonly DispatcherTimer _poiTimer = new() { Interval = TimeSpan.FromMilliseconds(160) };
    private int _poiVersion = -1;
    private string _poiWindow = "";

    private Point _dragStart, _dragLast;
    private bool _dragging, _boxing, _moved;
    private Rect _box;

    private static readonly Typeface Face = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface FaceBold = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    public GraphCanvas()
    {
        Focusable = true;
        FocusVisualStyle = null;
        ClipToBounds = true;
        Cursor = Cursors.Cross;
        _poiTimer.Tick += (_, _) => { _poiTimer.Stop(); RefreshPointsOfInterest(); };
        ThemeManager.ThemeChanged += InvalidateVisual;
    }

    /// <summary>Redraw now and recompute points of interest shortly.</summary>
    public void Refresh()
    {
        InvalidateVisual();
        _poiTimer.Stop();
        _poiTimer.Start();
    }

    public void RefreshPointsOfInterest()
    {
        if (Model == null) return;
        _poi = ShowPointsOfInterest ? Model.PointsOfInterest() : new();
        _poiVersion = Model.Version;
        _poiWindow = WindowKey();
        InvalidateVisual();
    }

    public IReadOnlyList<(FoundPoint Point, int Row)> PointsOfInterest => _poi;

    private string WindowKey() => Model == null ? "" : $"{Model.Window.XMin},{Model.Window.XMax},{Model.Window.YMin},{Model.Window.YMax}";

    // ---------------------------------------------------------------- coordinates

    private double W => System.Math.Max(1, ActualWidth);
    private double H => System.Math.Max(1, ActualHeight);
    private double PX(double x) => (x - Model.Window.XMin) / (Model.Window.XMax - Model.Window.XMin) * W;
    private double PY(double y) => H - (y - Model.Window.YMin) / (Model.Window.YMax - Model.Window.YMin) * H;
    public double WX(double px) => Model.Window.XMin + px / W * (Model.Window.XMax - Model.Window.XMin);
    public double WY(double py) => Model.Window.YMin + (H - py) / H * (Model.Window.YMax - Model.Window.YMin);

    private static bool Ok(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

    // ---------------------------------------------------------------- drawing

    private readonly Dictionary<(Color, double, LineStyle), Pen> _pens = new();

    private Pen PenFor(Color c, double width, LineStyle style = LineStyle.Solid)
    {
        if (_pens.TryGetValue((c, width, style), out var p)) return p;
        var b = new SolidColorBrush(c);
        b.Freeze();
        p = new Pen(b, width) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (style == LineStyle.Dashed) p.DashStyle = new DashStyle(new double[] { 4, 3 }, 0);
        if (style == LineStyle.Dotted) p.DashStyle = new DashStyle(new double[] { 0.1, 2.6 }, 0);
        p.Freeze();
        _pens[(c, width, style)] = p;
        return p;
    }

    private static Brush Fill(Color c, byte alpha = 255)
    {
        var b = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
        b.Freeze();
        return b;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var pal = ThemeManager.Graph;
        dc.DrawRectangle(Fill(pal.Background), null, new Rect(0, 0, W, H));
        if (Model == null) return;
        var win = Model.Window;
        if (!(win.XMax > win.XMin) || !(win.YMax > win.YMin)) return;

        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var (xStep, yStep) = (NiceStep(win.XMax - win.XMin, W / 95), NiceStep(win.YMax - win.YMin, H / 70));

        if (ShowGrid) DrawGrid(dc, pal, xStep, yStep);

        // shading first so curves sit on top
        foreach (var c in Model.Compiled.Where(c => c.IsVisible))
        {
            var col = ThemeManager.CurveColor(c.Row.ColorIndex);
            if (c.Kind == PlotKind.Inequality) ShadeFunction(dc, c, col);
            else if (c.Kind == PlotKind.ImplicitInequality) DrawImplicit(dc, c, col, shade: true);
        }
        if (Integral is { } ig) ShadeIntegral(dc, ig.Row, ig.A, ig.B);

        if (ShowAxes) DrawAxes(dc, pal, xStep, yStep, dpi);

        foreach (var c in Model.Compiled.Where(c => c.IsVisible))
        {
            var col = ThemeManager.CurveColor(c.Row.ColorIndex);
            double width = c.Row.Style == LineStyle.Thick ? 3.6 : 2.2;
            var pen = PenFor(col, width, c.IsStrict ? LineStyle.Dashed : c.Row.Style == LineStyle.Thick ? LineStyle.Solid : c.Row.Style);
            switch (c.Kind)
            {
                case PlotKind.Function:
                case PlotKind.Inequality:
                    DrawFunction(dc, c, pen);
                    break;
                case PlotKind.Polar:
                    DrawCurve(dc, pen, Model.Window.ThetaMin, Model.Window.ThetaMax, th =>
                    {
                        double r = Model.EvalPolar(c, th);
                        return (r * System.Math.Cos(th), r * System.Math.Sin(th));
                    });
                    break;
                case PlotKind.Parametric:
                    DrawCurve(dc, pen, Model.Window.TMin, Model.Window.TMax, t => Model.EvalParametric(c, t));
                    break;
                case PlotKind.Implicit:
                case PlotKind.ImplicitInequality:
                    DrawImplicit(dc, c, col, shade: false, pen);
                    break;
                case PlotKind.Points:
                    var (xs, ys) = Model.EvalPoints(c);
                    var fill = Fill(col);
                    for (int i = 0; i < System.Math.Min(xs.Length, ys.Length); i++)
                        if (Ok(xs[i]) && Ok(ys[i]))
                            dc.DrawEllipse(fill, PenFor(pal.Background, 1.5), new Point(PX(xs[i]), PY(ys[i])), 4.5, 4.5);
                    break;
            }
        }

        if (Tangent is { } tg) DrawTangent(dc, tg.Row, tg.X, dpi);

        // points of interest (hollow), pinned labels
        if (ShowPointsOfInterest)
            foreach (var (p, row) in _poi)
            {
                var col = ThemeManager.CurveColor(Model.Compiled.ElementAtOrDefault(row)?.Row.ColorIndex ?? 0);
                var pt = new Point(PX(p.X), PY(p.Y));
                bool hot = _hover is { } h && h.Point.Equals(p) && h.Row == row;
                dc.DrawEllipse(Fill(hot ? col : pal.PointFill), PenFor(col, 2), pt, hot ? 5.5 : 4.2, hot ? 5.5 : 4.2);
                if (hot || _pinned.Contains((p.X, p.Y)))
                    Label(dc, pt, $"{p.Kind}  ({Fmt(p.X)}, {Fmt(p.Y)})", col, dpi);
            }

        foreach (var m in Marks)
        {
            var col = ThemeManager.CurveColor(m.ColorIndex);
            var pt = new Point(PX(m.X), PY(m.Y));
            dc.DrawEllipse(Fill(col), PenFor(pal.Background, 2), pt, 5.5, 5.5);
            Label(dc, pt, m.Label, col, dpi);
        }

        if (TraceOn) DrawTrace(dc, pal, dpi);

        if (_boxing)
        {
            dc.DrawRectangle(Fill(pal.Trace, 0x22), PenFor(pal.Trace, 1.2, LineStyle.Dashed), _box);
        }
    }

    /// <summary>A "nice" grid step (1, 2 or 5 × 10ⁿ) about every targetPx pixels.</summary>
    public static double NiceStep(double range, double count)
    {
        if (count < 1) count = 1;
        double raw = range / count;
        double mag = System.Math.Pow(10, System.Math.Floor(System.Math.Log10(raw)));
        double n = raw / mag;
        double step = n < 1.5 ? 1 : n < 3.5 ? 2 : n < 7.5 ? 5 : 10;
        return step * mag;
    }

    private void DrawGrid(DrawingContext dc, GraphPalette pal, double xs, double ys)
    {
        var win = Model.Window;
        var minor = PenFor(pal.MinorGrid, 1);
        var major = PenFor(pal.MajorGrid, 1);
        double mx = xs / 5, my = ys / 5;
        if (PX(win.XMin + mx) - PX(win.XMin) > 7)
            for (double x = System.Math.Ceiling(win.XMin / mx) * mx; x <= win.XMax; x += mx)
                dc.DrawLine(minor, new Point(Snap(PX(x)), 0), new Point(Snap(PX(x)), H));
        if (PY(win.YMin) - PY(win.YMin + my) > 7)
            for (double y = System.Math.Ceiling(win.YMin / my) * my; y <= win.YMax; y += my)
                dc.DrawLine(minor, new Point(0, Snap(PY(y))), new Point(W, Snap(PY(y))));
        for (double x = System.Math.Ceiling(win.XMin / xs) * xs; x <= win.XMax; x += xs)
            dc.DrawLine(major, new Point(Snap(PX(x)), 0), new Point(Snap(PX(x)), H));
        for (double y = System.Math.Ceiling(win.YMin / ys) * ys; y <= win.YMax; y += ys)
            dc.DrawLine(major, new Point(0, Snap(PY(y))), new Point(W, Snap(PY(y))));
    }

    private static double Snap(double v) => System.Math.Round(v) + 0.5;

    private void DrawAxes(DrawingContext dc, GraphPalette pal, double xs, double ys, double dpi)
    {
        var win = Model.Window;
        var axis = PenFor(pal.Axis, 1.4);
        double ax = PY(0), ay = PX(0);
        bool xAxisIn = ax >= 0 && ax <= H, yAxisIn = ay >= 0 && ay <= W;
        if (xAxisIn) dc.DrawLine(axis, new Point(0, Snap(ax)), new Point(W, Snap(ax)));
        if (yAxisIn) dc.DrawLine(axis, new Point(Snap(ay), 0), new Point(Snap(ay), H));

        // TI-style tick marks every Xscl / Yscl
        var tick = PenFor(pal.Axis, 1.2);
        if (win.XScl > 0 && PX(win.XMin + win.XScl) - PX(win.XMin) > 5 && xAxisIn)
            for (double x = System.Math.Ceiling(win.XMin / win.XScl) * win.XScl; x <= win.XMax; x += win.XScl)
                dc.DrawLine(tick, new Point(Snap(PX(x)), ax - 3), new Point(Snap(PX(x)), ax + 3));
        if (win.YScl > 0 && PY(win.YMin) - PY(win.YMin + win.YScl) > 5 && yAxisIn)
            for (double y = System.Math.Ceiling(win.YMin / win.YScl) * win.YScl; y <= win.YMax; y += win.YScl)
                dc.DrawLine(tick, new Point(ay - 3, Snap(PY(y))), new Point(ay + 3, Snap(PY(y))));

        // numbers
        var brush = Fill(pal.Label);
        double labelY = System.Math.Clamp(xAxisIn ? ax + 4 : H - 18, 2, H - 18);
        for (double x = System.Math.Ceiling(win.XMin / xs) * xs; x <= win.XMax; x += xs)
        {
            if (System.Math.Abs(x) < xs * 1e-6) continue;
            var ft = Text(TickText(x, xs), 11, brush, dpi);
            double px = PX(x) - ft.Width / 2;
            if (px < 2 || px + ft.Width > W - 2) continue;
            dc.DrawText(ft, new Point(px, labelY));
        }
        double labelX = yAxisIn ? ay - 6 : 34;
        for (double y = System.Math.Ceiling(win.YMin / ys) * ys; y <= win.YMax; y += ys)
        {
            if (System.Math.Abs(y) < ys * 1e-6) continue;
            var ft = Text(TickText(y, ys), 11, brush, dpi);
            double px = System.Math.Clamp(labelX - ft.Width, 2, W - ft.Width - 2);
            double py = PY(y) - ft.Height / 2;
            if (py < 2 || py + ft.Height > H - 2) continue;
            dc.DrawText(ft, new Point(px, py));
        }
        if (xAxisIn && yAxisIn)
        {
            var o = Text("0", 11, brush, dpi);
            dc.DrawText(o, new Point(ay - o.Width - 5, ax + 3));
        }
    }

    private static string TickText(double v, double step)
    {
        // multiples of π read as π (ZTrig)
        double k = v / System.Math.PI;
        if (System.Math.Abs(step / (System.Math.PI / 2) - System.Math.Round(step / (System.Math.PI / 2))) < 1e-9 && System.Math.Abs(k * 2 - System.Math.Round(k * 2)) < 1e-9)
        {
            double h = System.Math.Round(k * 2);
            if (h % 2 == 0) { double w = h / 2; return w == 1 ? "π" : w == -1 ? "−π" : $"{w}π".Replace("-", "−"); }
            return h == 1 ? "π/2" : h == -1 ? "−π/2" : $"{h}π/2".Replace("-", "−");
        }
        double a = System.Math.Abs(v);
        string s = a >= 1e5 || (a < 1e-3 && a > 0) ? v.ToString("0.##E+0", CultureInfo.InvariantCulture) : System.Math.Round(v, 10).ToString("0.##########", CultureInfo.InvariantCulture);
        return s.Replace("-", "−");
    }

    private FormattedText Text(string s, double size, Brush b, double dpi, bool bold = false) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, bold ? FaceBold : Face, size, b, dpi);

    private static string Fmt(double v) => Parser.Format(Analysis.Snap(v), 8);

    /// <summary>Samples y = f(x) once per half pixel and breaks the line at gaps and asymptotes.</summary>
    private List<List<Point>> FunctionSegments(CompiledRow c, double x0, double x1, int samples)
    {
        var segs = new List<List<Point>>();
        List<Point> cur = null;
        double yRange = Model.Window.YMax - Model.Window.YMin;
        double prevY = double.NaN, prevX = double.NaN;
        for (int i = 0; i <= samples; i++)
        {
            double x = x0 + (x1 - x0) * i / samples;
            double y = Model.EvalFunction(c, x);
            if (!Ok(y)) { cur = null; prevY = double.NaN; continue; }
            if (Ok(prevY) && System.Math.Abs(y - prevY) > yRange * 0.6)
            {
                // a big jump: is it a real steep curve or an asymptote? Look between.
                double ym = Model.EvalFunction(c, (x + prevX) / 2);
                bool between = Ok(ym) && ym >= System.Math.Min(y, prevY) - yRange * 0.01 && ym <= System.Math.Max(y, prevY) + yRange * 0.01;
                if (!between) cur = null;
            }
            if (cur == null) { cur = new List<Point>(); segs.Add(cur); }
            cur.Add(new Point(PX(x), System.Math.Clamp(PY(y), -4 * H, 5 * H)));
            prevY = y; prevX = x;
        }
        return segs;
    }

    private void DrawFunction(DrawingContext dc, CompiledRow c, Pen pen)
    {
        int samples = (int)(W * 1.5);
        foreach (var seg in FunctionSegments(c, Model.Window.XMin, Model.Window.XMax, samples))
            DrawPolyline(dc, pen, seg);
    }

    private static void DrawPolyline(DrawingContext dc, Pen pen, List<Point> pts)
    {
        if (pts.Count == 0) return;
        if (pts.Count == 1) { dc.DrawEllipse(pen.Brush, null, pts[0], pen.Thickness / 2, pen.Thickness / 2); return; }
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(pts[0], false, false);
            ctx.PolyLineTo(pts.Skip(1).ToList(), true, true);
        }
        g.Freeze();
        dc.DrawGeometry(null, pen, g);
    }

    private void ShadeFunction(DrawingContext dc, CompiledRow c, Color col)
    {
        var fill = Fill(col, 0x2E);
        double edge = c.ShadeAbove ? -2 : H + 2;
        foreach (var seg in FunctionSegments(c, Model.Window.XMin, Model.Window.XMax, (int)(W / 2)))
        {
            if (seg.Count < 2) continue;
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(seg[0].X, edge), true, true);
                ctx.PolyLineTo(seg, false, false);
                ctx.LineTo(new Point(seg[^1].X, edge), false, false);
            }
            g.Freeze();
            dc.DrawGeometry(fill, null, g);
        }
    }

    private void ShadeIntegral(DrawingContext dc, int row, double a, double b)
    {
        var c = Model.Compiled.ElementAtOrDefault(row);
        if (c == null || !c.IsFunction) return;
        var col = ThemeManager.CurveColor(c.Row.ColorIndex);
        double lo = System.Math.Max(System.Math.Min(a, b), Model.Window.XMin), hi = System.Math.Min(System.Math.Max(a, b), Model.Window.XMax);
        if (hi <= lo) return;
        double zero = System.Math.Clamp(PY(0), -2, H + 2);
        foreach (var seg in FunctionSegments(c, lo, hi, System.Math.Max(40, (int)(PX(hi) - PX(lo)))))
        {
            if (seg.Count < 2) continue;
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(seg[0].X, zero), true, true);
                ctx.PolyLineTo(seg, false, false);
                ctx.LineTo(new Point(seg[^1].X, zero), false, false);
            }
            g.Freeze();
            dc.DrawGeometry(Fill(col, 0x55), null, g);
        }
    }

    /// <summary>Parametric and polar curves: 2,000 steps, broken at gaps and wild jumps.</summary>
    private void DrawCurve(DrawingContext dc, Pen pen, double from, double to, Func<double, (double X, double Y)> f)
    {
        const int steps = 2000;
        var seg = new List<Point>();
        Point? last = null;
        double diag = System.Math.Sqrt(W * W + H * H);
        for (int i = 0; i <= steps; i++)
        {
            double t = from + (to - from) * i / steps;
            var (x, y) = f(t);
            if (!Ok(x) || !Ok(y)) { DrawPolyline(dc, pen, seg); seg = new(); last = null; continue; }
            var p = new Point(System.Math.Clamp(PX(x), -4 * W, 5 * W), System.Math.Clamp(PY(y), -4 * H, 5 * H));
            if (last is { } l && (p - l).Length > diag * 0.5) { DrawPolyline(dc, pen, seg); seg = new(); }
            seg.Add(p);
            last = p;
        }
        DrawPolyline(dc, pen, seg);
    }

    /// <summary>Relations in x and y (x²+y²=25): marching squares on a 4-pixel grid; inequalities are shaded.</summary>
    private void DrawImplicit(DrawingContext dc, CompiledRow c, Color col, bool shade, Pen pen = null)
    {
        const double cs = 4;
        int nx = (int)System.Math.Ceiling(W / cs) + 1, ny = (int)System.Math.Ceiling(H / cs) + 1;
        var v = new double[nx, ny];
        for (int i = 0; i < nx; i++)
        {
            double x = WX(i * cs);
            for (int j = 0; j < ny; j++) v[i, j] = Model.EvalRelation(c, x, WY(j * cs));
        }

        if (shade)
        {
            var fill = Fill(col, 0x2E);
            bool want = c.Relation is ">" or ">=";
            for (int j = 0; j < ny - 1; j++)
            {
                int run = -1;
                for (int i = 0; i <= nx - 1; i++)
                {
                    double m = (v[System.Math.Min(i, nx - 1), j] + v[System.Math.Min(i, nx - 1), j + 1]) / 2;
                    bool on = i < nx - 1 && Ok(m) && (want ? m > 0 : m < 0);
                    if (on && run < 0) run = i;
                    if (!on && run >= 0)
                    {
                        dc.DrawRectangle(fill, null, new Rect(run * cs - cs / 2, j * cs, (i - run) * cs, cs));
                        run = -1;
                    }
                }
            }
            return;
        }

        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            for (int i = 0; i < nx - 1; i++)
                for (int j = 0; j < ny - 1; j++)
                {
                    double a = v[i, j], b = v[i + 1, j], d = v[i, j + 1], e = v[i + 1, j + 1];
                    if (!Ok(a) || !Ok(b) || !Ok(d) || !Ok(e)) continue;
                    var pts = new List<Point>(4);
                    double x0 = i * cs, y0 = j * cs;
                    if ((a > 0) != (b > 0)) pts.Add(new Point(x0 + cs * a / (a - b), y0));
                    if ((b > 0) != (e > 0)) pts.Add(new Point(x0 + cs, y0 + cs * b / (b - e)));
                    if ((d > 0) != (e > 0)) pts.Add(new Point(x0 + cs * d / (d - e), y0 + cs));
                    if ((a > 0) != (d > 0)) pts.Add(new Point(x0, y0 + cs * a / (a - d)));
                    if (pts.Count < 2) continue;
                    // skip sign changes through a pole (tan, 1/x): the middle would be huge, not near zero
                    double mid = Model.EvalRelation(c, WX(x0 + cs / 2), WY(y0 + cs / 2));
                    double big = System.Math.Max(System.Math.Max(System.Math.Abs(a), System.Math.Abs(b)), System.Math.Max(System.Math.Abs(d), System.Math.Abs(e)));
                    if (Ok(mid) && System.Math.Abs(mid) > big * 4) continue;
                    ctx.BeginFigure(pts[0], false, false);
                    ctx.LineTo(pts[1], true, false);
                    if (pts.Count == 4) { ctx.BeginFigure(pts[2], false, false); ctx.LineTo(pts[3], true, false); }
                }
        }
        g.Freeze();
        dc.DrawGeometry(null, pen, g);
    }

    private void DrawTangent(DrawingContext dc, int row, double x0, double dpi)
    {
        var c = Model.Compiled.ElementAtOrDefault(row);
        if (c == null || !c.IsFunction) return;
        double y0 = Model.EvalFunction(c, x0);
        double m = Analysis.Derivative(Model.AsFunction(c), x0);
        if (!Ok(y0) || !Ok(m)) return;
        var pen = PenFor(ThemeManager.Graph.Axis, 1.6, LineStyle.Dashed);
        double xa = Model.Window.XMin, xb = Model.Window.XMax;
        dc.DrawLine(pen, new Point(PX(xa), System.Math.Clamp(PY(y0 + m * (xa - x0)), -4 * H, 5 * H)),
                         new Point(PX(xb), System.Math.Clamp(PY(y0 + m * (xb - x0)), -4 * H, 5 * H)));
    }

    private void Label(DrawingContext dc, Point at, string text, Color col, double dpi)
    {
        var pal = ThemeManager.Graph;
        var ft = Text(text, 11.5, Fill(pal.Axis.R + pal.Axis.G + pal.Axis.B > 380 ? Colors.White : Color.FromRgb(0x1C, 0x19, 0x17)), dpi, true);
        double x = at.X + 10, y = at.Y - ft.Height - 10;
        if (x + ft.Width + 12 > W) x = at.X - ft.Width - 22;
        if (y < 2) y = at.Y + 10;
        var box = new Rect(x, y, ft.Width + 12, ft.Height + 6);
        dc.DrawRoundedRectangle(Fill(pal.Background, 0xF2), PenFor(col, 1.2), box, 6, 6);
        dc.DrawText(ft, new Point(x + 6, y + 3));
    }

    // ---------------------------------------------------------------- trace

    private IEnumerable<CompiledRow> Traceable() =>
        Model.Compiled.Where(c => c.IsVisible && (c.IsFunction || c.Kind is PlotKind.Polar or PlotKind.Parametric));

    private CompiledRow TraceTarget()
    {
        var rows = Traceable().ToList();
        if (rows.Count == 0) return null;
        return rows.FirstOrDefault(r => r.Index == TraceRow) ?? rows[0];
    }

    /// <summary>The traced point, or null.</summary>
    public (double X, double Y, CompiledRow Row)? TracePoint()
    {
        if (Model == null) return null;
        var c = TraceTarget();
        if (c == null) return null;
        TraceRow = c.Index;
        double x, y;
        if (c.IsFunction) { x = TraceValue; y = Model.EvalFunction(c, x); }
        else if (c.Kind == PlotKind.Polar) { double r = Model.EvalPolar(c, TraceValue); x = r * System.Math.Cos(TraceValue); y = r * System.Math.Sin(TraceValue); }
        else (x, y) = Model.EvalParametric(c, TraceValue);
        return (x, y, c);
    }

    public string TraceText()
    {
        var tp = TracePoint();
        if (tp == null) return "Nothing to trace. Add a function first.";
        var (x, y, c) = tp.Value;
        string name = c.Alias ?? c.Name;
        string p = c.Kind == PlotKind.Polar ? $"θ={Fmt(TraceValue)}  " : c.Kind == PlotKind.Parametric ? $"t={Fmt(TraceValue)}  " : "";
        return Ok(y) ? $"{name}:  {p}x = {Fmt(x)}   y = {Fmt(y)}" : $"{name}:  x = {Fmt(x)}   (no value here)";
    }

    private void DrawTrace(DrawingContext dc, GraphPalette pal, double dpi)
    {
        var tp = TracePoint();
        if (tp == null) return;
        var (x, y, c) = tp.Value;
        if (!Ok(x) || !Ok(y)) return;
        var col = ThemeManager.CurveColor(c.Row.ColorIndex);
        var pt = new Point(PX(x), PY(y));
        var guide = PenFor(pal.Axis, 1, LineStyle.Dashed);
        dc.DrawLine(guide, new Point(pt.X, 0), new Point(pt.X, H));
        dc.DrawLine(guide, new Point(0, pt.Y), new Point(W, pt.Y));
        dc.DrawEllipse(Fill(col), PenFor(pal.Background, 2.5), pt, 6.5, 6.5);
        Label(dc, pt, $"({Fmt(x)}, {Fmt(y)})", col, dpi);
    }

    public void MoveTrace(int direction)
    {
        var c = TraceTarget();
        if (c == null) return;
        if (c.IsFunction) TraceValue = Analysis.Snap(TraceValue + direction * (Model.Window.XMax - Model.Window.XMin) / 100);
        else
        {
            double from = c.Kind == PlotKind.Polar ? Model.Window.ThetaMin : Model.Window.TMin;
            double to = c.Kind == PlotKind.Polar ? Model.Window.ThetaMax : Model.Window.TMax;
            TraceValue = System.Math.Clamp(TraceValue + direction * (to - from) / 96, from, to);
        }
        AfterTrace();
    }

    public void NextTraceRow(int direction)
    {
        var rows = Traceable().ToList();
        if (rows.Count == 0) return;
        int i = rows.FindIndex(r => r.Index == TraceRow);
        var next = rows[((i < 0 ? 0 : i + direction) % rows.Count + rows.Count) % rows.Count];
        TraceRow = next.Index;
        if (!next.IsFunction) TraceValue = next.Kind == PlotKind.Polar ? Model.Window.ThetaMin : Model.Window.TMin;
        AfterTrace();
    }

    private void AfterTrace()
    {
        // keep the traced point in view, like the TI does
        var tp = TracePoint();
        if (tp is { } p && Ok(p.X) && Ok(p.Y) && p.Row.IsFunction)
        {
            var win = Model.Window;
            double w = win.XMax - win.XMin, h = win.YMax - win.YMin;
            if (p.X < win.XMin || p.X > win.XMax) { double cx = p.X; win.XMin = cx - w / 2; win.XMax = cx + w / 2; ViewChanged?.Invoke(); }
            if (p.Y < win.YMin || p.Y > win.YMax) { double cy = p.Y; win.YMin = cy - h / 2; win.YMax = cy + h / 2; ViewChanged?.Invoke(); }
        }
        TraceChanged?.Invoke(TraceText());
        InvalidateVisual();
    }

    // ---------------------------------------------------------------- mouse and keys

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        _dragStart = _dragLast = e.GetPosition(this);
        _dragging = true;
        _moved = false;
        _boxing = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        _box = new Rect(_dragStart, _dragStart);
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (Model == null) return;
        var p = e.GetPosition(this);
        PointerMoved?.Invoke(WX(p.X), WY(p.Y));
        if (_dragging)
        {
            if ((p - _dragStart).Length > 3) _moved = true;
            if (_boxing) { _box = new Rect(_dragStart, p); InvalidateVisual(); return; }
            if (!_moved) return;
            var win = Model.Window;
            double dx = (p.X - _dragLast.X) / W * (win.XMax - win.XMin);
            double dy = (p.Y - _dragLast.Y) / H * (win.YMax - win.YMin);
            win.XMin -= dx; win.XMax -= dx; win.YMin += dy; win.YMax += dy;
            _dragLast = p;
            Model.Touch();
            ViewChanged?.Invoke();
            Refresh();
            return;
        }
        if (TraceOn)
        {
            var c = TraceTarget();
            if (c != null && c.IsFunction)
            {
                TraceValue = WX(p.X);
                TraceChanged?.Invoke(TraceText());
                InvalidateVisual();
            }
        }
        var near = NearestPoi(p);
        if (!Equals(near, _hover)) { _hover = near; InvalidateVisual(); }
        Cursor = near != null ? Cursors.Hand : _dragging ? Cursors.SizeAll : Cursors.Cross;
    }

    private (FoundPoint Point, int Row)? NearestPoi(Point p)
    {
        if (!ShowPointsOfInterest) return null;
        (FoundPoint, int)? best = null;
        double bestD = 9;
        foreach (var poi in _poi)
        {
            double d = (new Point(PX(poi.Point.X), PY(poi.Point.Y)) - p).Length;
            if (d < bestD) { bestD = d; best = poi; }
        }
        return best;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();
        var p = e.GetPosition(this);
        if (_boxing)
        {
            _boxing = false;
            if (_box.Width > 8 && _box.Height > 8)
            {
                var win = Model.Window;
                double x0 = WX(_box.Left), x1 = WX(_box.Right), y0 = WY(_box.Bottom), y1 = WY(_box.Top);
                win.XMin = x0; win.XMax = x1; win.YMin = y0; win.YMax = y1;
                Model.Touch();
                ViewChanged?.Invoke();
            }
            Refresh();
            return;
        }
        if (!_moved)
        {
            var near = NearestPoi(p);
            if (near is { } n)
            {
                var key = (n.Point.X, n.Point.Y);
                if (!_pinned.Remove(key)) _pinned.Add(key);
                InvalidateVisual();
            }
            else if (TraceOn)
            {
                TraceValue = WX(p.X);
                AfterTrace();
            }
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (Model == null) return;
        var p = e.GetPosition(this);
        double f = e.Delta > 0 ? 1 / 1.2 : 1.2;
        Model.Zoom(f, WX(p.X), WY(p.Y));
        ViewChanged?.Invoke();
        Refresh();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (Model == null) return;
        var win = Model.Window;
        double w = win.XMax - win.XMin, h = win.YMax - win.YMin;
        switch (e.Key)
        {
            case Key.Left: if (TraceOn) MoveTrace(-1); else { win.XMin -= w / 10; win.XMax -= w / 10; Changed(); } break;
            case Key.Right: if (TraceOn) MoveTrace(1); else { win.XMin += w / 10; win.XMax += w / 10; Changed(); } break;
            case Key.Up: if (TraceOn) NextTraceRow(-1); else { win.YMin += h / 10; win.YMax += h / 10; Changed(); } break;
            case Key.Down: if (TraceOn) NextTraceRow(1); else { win.YMin -= h / 10; win.YMax -= h / 10; Changed(); } break;
            case Key.OemPlus: case Key.Add: Model.Zoom(1 / 1.5, (win.XMin + win.XMax) / 2, (win.YMin + win.YMax) / 2); Changed(); break;
            case Key.OemMinus: case Key.Subtract: Model.Zoom(1.5, (win.XMin + win.XMax) / 2, (win.YMin + win.YMax) / 2); Changed(); break;
            default: return;
        }
        e.Handled = true;
    }

    private void Changed()
    {
        Model.Touch();
        ViewChanged?.Invoke();
        Refresh();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        Refresh();
    }

    /// <summary>The graph as a picture (Copy / Save image).</summary>
    public System.Windows.Media.Imaging.BitmapSource Snapshot()
    {
        double dpi = 96 * VisualTreeHelper.GetDpi(this).DpiScaleX;
        var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)(W * dpi / 96), (int)(H * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
        bmp.Render(this);
        bmp.Freeze();
        return bmp;
    }
}
