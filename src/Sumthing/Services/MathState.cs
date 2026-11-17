using Sumthing.Maths;

namespace Sumthing.Services;

/// <summary>
/// The maths shared by Scientific, Graphing and Statistics, like one TI calculator's memory:
/// stored variables (A, B, …), lists L1–L6, user functions (f1, g …), Ans and the angle mode.
/// </summary>
public static class MathState
{
    public static EvalContext Context { get; } = new();
    public static CalcSession Session { get; } = new(Context);
    public static GraphModel Graph { get; } = new(Context);

    /// <summary>Lists, variables or the angle mode changed.</summary>
    public static event Action Changed;

    public static void RaiseChanged()
    {
        Graph.StoredNames.Clear();
        foreach (var k in Context.Vars.Keys)
            if (!Graph.Sliders.ContainsKey(k)) Graph.StoredNames.Add(k);
        Changed?.Invoke();
    }

    public static bool Degrees
    {
        get => Context.Degrees;
        set { Context.Degrees = value; SettingsService.Current.AngleDegrees = value; Graph.Touch(); Changed?.Invoke(); }
    }

    public static ICollection<string> UserFunctionNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in Graph.Compiled)
        {
            names.Add(c.Name);
            if (c.Alias != null) names.Add(c.Alias);
        }
        return names;
    }

    public static void Load(AppSettings s)
    {
        Context.Degrees = s.AngleDegrees;
        Session.UserFunctionNames = UserFunctionNames;
        foreach (var kv in s.StoredVariables) Context.Vars[kv.Key] = kv.Value;
        foreach (var kv in s.Lists) Context.Lists[kv.Key] = kv.Value ?? Array.Empty<double>();
        for (int i = 1; i <= 6; i++) if (!Context.Lists.ContainsKey($"L{i}")) Context.Lists[$"L{i}"] = Array.Empty<double>();
        Context.Ans = s.Ans;
        Session.Entries.AddRange(s.HomeHistory);

        var rows = s.GraphRows.Count > 0 ? s.GraphRows : AppSettings.DefaultGraphRows();
        foreach (var r in rows) Graph.Rows.Add(r);
        if (s.GraphWindow != null) Graph.Window = s.GraphWindow;
        foreach (var kv in s.Sliders) Graph.Sliders[kv.Key] = kv.Value;
        RaiseChanged();
        Graph.Recompile();
    }

    public static void Save(AppSettings s)
    {
        s.AngleDegrees = Context.Degrees;
        s.StoredVariables = Context.Vars.Where(kv => !Graph.Sliders.ContainsKey(kv.Key) && !double.IsNaN(kv.Value) && !double.IsInfinity(kv.Value))
                                        .ToDictionary(kv => kv.Key, kv => kv.Value);
        s.Lists = Context.Lists.ToDictionary(kv => kv.Key, kv => kv.Value);
        s.Ans = double.IsNaN(Context.Ans) || double.IsInfinity(Context.Ans) ? 0 : Context.Ans;
        s.HomeHistory = Session.Entries.TakeLast(100).ToList();
        s.GraphRows = Graph.Rows.ToList();
        s.GraphWindow = Graph.Window;
        s.Sliders = Graph.Sliders.ToDictionary(kv => kv.Key, kv => kv.Value);
    }
}
