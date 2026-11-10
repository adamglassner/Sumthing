namespace Sumthing.Engine;

/// <summary>One line on the paper tape.</summary>
public sealed class TapeEntry
{
    public string Number { get; set; } = "";
    /// <summary>+ − × ÷ = % √ x² 1/x ±, or "" for a total line.</summary>
    public string Symbol { get; set; } = "";
    public bool IsTotal { get; set; }
    public bool IsDivider { get; set; }
    /// <summary>The user's own label for this line ("Rent", "Groceries"...).</summary>
    public string Note { get; set; } = "";
    public DateTime Time { get; set; } = DateTime.Now;
}

/// <summary>One finished calculation in History.</summary>
public sealed class HistoryItem
{
    public string Expression { get; set; } = "";
    public string Result { get; set; } = "";
    public decimal Value { get; set; }
}

/// <summary>
/// The Standard calculator. Works on decimals (28 digits), so 0.1 + 0.2 is exactly 0.3.
/// Behaves like the Windows calculator: immediate execution, repeated "=", percent rules,
/// "5 + =" uses 5 again, CE / C / backspace and one memory register.
/// </summary>
public sealed class CalcEngine
{
    public const int MaxDigits = 16;

    private string _entry = "0";
    private decimal _acc;
    private string _pending;           // + − × ÷ waiting for its right-hand number
    private bool _replace = true;      // the next digit starts a new number
    private bool _operandReady;        // a number was entered (or recalled) since the last operator
    private bool _afterEquals;
    private string _repeatOp;
    private decimal _repeatOperand;

    public string Error { get; private set; }
    public string Expression { get; private set; } = "";
    public decimal? Memory { get; private set; }
    public List<HistoryItem> History { get; } = new();

    public event Action Changed;
    public event Action<TapeEntry> Tape;

    public bool HasError => Error != null;
    public decimal Value => Num.TryParse(_entry, out var v) ? v : 0;

    /// <summary>What the big display shows.</summary>
    public string Display
    {
        get
        {
            if (Error != null) return Error;
            if (!Num.Grouping) return _entry;
            // keep what the user is typing (trailing "." or zeros) but add separators
            return Num.Group(_entry);
        }
    }

    // ------------------------------------------------------------------ input

    /// <summary>Handles a key name: 0-9 . + − × ÷ = % √ sqr inv neg back CE C MC MR MS M+ M−.</summary>
    public void Press(string key)
    {
        switch (key)
        {
            case "0": case "1": case "2": case "3": case "4":
            case "5": case "6": case "7": case "8": case "9": Digit(key); break;
            case ".": Dot(); break;
            case "+": case "−": case "×": case "÷": Operator(key); break;
            case "-": Operator("−"); break;
            case "*": Operator("×"); break;
            case "/": Operator("÷"); break;
            case "=": Calculate(); break;
            case "%": Percent(); break;
            case "√": Unary("sqrt"); break;
            case "sqr": Unary("sqr"); break;
            case "inv": Unary("inv"); break;
            case "neg": Negate(); break;
            case "back": Backspace(); break;
            case "CE": ClearEntry(); break;
            case "C": Clear(); break;
            case "MC": case "MR": case "MS": case "M+": case "M−": MemoryKey(key); break;
            case "M-": MemoryKey("M−"); break;
        }
    }

    public void Digit(string d)
    {
        if (Error != null) ResetAll(keepExpression: false);
        if (_afterEquals) { Expression = ""; _afterEquals = false; _repeatOp = null; }
        if (_replace)
        {
            _entry = d;
            _replace = false;
        }
        else
        {
            if (DigitCount(_entry) >= MaxDigits) return;
            _entry = _entry == "0" ? d : _entry == "-0" ? "-" + d : _entry + d;
        }
        _operandReady = true;
        Raise();
    }

    public void Dot()
    {
        if (Error != null) ResetAll(keepExpression: false);
        if (_afterEquals) { Expression = ""; _afterEquals = false; _repeatOp = null; }
        if (_replace)
        {
            _entry = "0.";
            _replace = false;
        }
        else if (!_entry.Contains('.'))
            _entry += ".";
        _operandReady = true;
        Raise();
    }

    public void Operator(string op)
    {
        if (Error != null) return;
        decimal cur = Value;
        if (_afterEquals)
        {
            _acc = cur;
            _afterEquals = false;
            Log(Num.Format(cur), op);
        }
        else if (_pending != null && _operandReady)
        {
            Log(Num.Format(cur), op);
            if (!TryApply(_acc, _pending, cur, out var r)) return;
            _acc = r;
        }
        else if (_pending != null)
        {
            ChangeLastSymbol(op);
        }
        else
        {
            _acc = cur;
            Log(Num.Format(cur), op);
        }
        _pending = op;
        _repeatOp = null;
        Expression = $"{Num.Format(_acc)} {op}";
        _entry = Num.Plain(_acc);
        _replace = true;
        _operandReady = false;
        Raise();
    }

    public void Calculate()
    {
        if (Error != null) return;
        decimal a, b;
        string op;
        if (_pending != null)
        {
            a = _acc;
            op = _pending;
            b = _operandReady ? Value : _acc;
        }
        else if (_afterEquals && _repeatOp != null)
        {
            a = Value;
            op = _repeatOp;
            b = _repeatOperand;
            Log(Num.Format(a), op);
        }
        else
        {
            Expression = $"{Num.Format(Value)} =";
            _afterEquals = true;
            _replace = true;
            Raise();
            return;
        }

        string expr = $"{Num.Format(a)} {op} {Num.Format(b)} =";
        Log(Num.Format(b), "=");
        if (!TryApply(a, op, b, out var r)) { Expression = expr; Raise(); return; }

        var result = Num.Format(r);
        LogTotal(result);
        History.Insert(0, new HistoryItem { Expression = expr, Result = result, Value = r });
        if (History.Count > 100) History.RemoveAt(History.Count - 1);

        Expression = expr;
        _entry = Num.Plain(r);
        _pending = null;
        _repeatOp = op;
        _repeatOperand = b;
        _replace = true;
        _operandReady = false;
        _afterEquals = true;
        Raise();
    }

    public void Percent()
    {
        if (Error != null) return;
        decimal cur = Value;
        decimal v;
        if (_pending == "+" || _pending == "−") v = _acc * cur / 100m;
        else v = cur / 100m;
        Log(Num.Format(cur), "%");
        _entry = Num.Plain(v);
        Expression = _pending != null ? $"{Num.Format(_acc)} {_pending} {Num.Format(v)}" : Num.Format(v);
        _replace = true;
        _operandReady = true;
        _afterEquals = false;
        Raise();
    }

    public void Unary(string kind)
    {
        if (Error != null) return;
        decimal cur = Value;
        decimal r;
        string label, sym;
        try
        {
            switch (kind)
            {
                case "sqrt":
                    if (cur < 0) { Fail("Invalid input"); return; }
                    r = Num.Sqrt(cur); label = $"√({Num.Format(cur)})"; sym = "√"; break;
                case "sqr":
                    r = cur * cur; label = $"sqr({Num.Format(cur)})"; sym = "x²"; break;
                case "inv":
                    if (cur == 0) { Fail("Can't divide by zero"); return; }
                    r = 1m / cur; label = $"1/({Num.Format(cur)})"; sym = "1/x"; break;
                default: return;
            }
        }
        catch (OverflowException) { Fail("Overflow"); return; }

        Log(Num.Format(cur), sym);
        Expression = _pending != null && !_afterEquals ? $"{Num.Format(_acc)} {_pending} {label}" : label;
        _entry = Num.Plain(r);
        _replace = true;
        _operandReady = true;
        if (_afterEquals) { _afterEquals = false; _repeatOp = null; }
        Raise();
    }

    public void Negate()
    {
        if (Error != null) return;
        if (!_replace)
        {
            if (_entry == "0") return;
            _entry = _entry.StartsWith('-') ? _entry[1..] : "-" + _entry;
        }
        else
        {
            decimal v = -Value;
            _entry = Num.Plain(v);
            _operandReady = true;
            if (_afterEquals) { Expression = $"negate({Num.Format(-v)})"; _afterEquals = false; _repeatOp = null; }
        }
        Raise();
    }

    public void Backspace()
    {
        if (Error != null) { ClearEntry(); return; }
        if (_replace)
        {
            if (_afterEquals) { Expression = ""; Raise(); }
            return;
        }
        string e = _entry[..^1];
        if (e.Length == 0 || e == "-") e = "0";
        _entry = e;
        Raise();
    }

    public void ClearEntry()
    {
        bool hadError = Error != null;
        Error = null;
        _entry = "0";
        _replace = true;
        if (hadError) ResetAll(keepExpression: false);
        else
        {
            _operandReady = true;
            if (_afterEquals) { Expression = ""; _afterEquals = false; _repeatOp = null; }
        }
        Raise();
    }

    public void Clear()
    {
        bool wasBusy = _pending != null || _afterEquals || _entry != "0";
        ResetAll(keepExpression: false);
        if (wasBusy) Tape?.Invoke(new TapeEntry { IsDivider = true, Symbol = "C" });
        Raise();
    }

    public void MemoryKey(string key)
    {
        decimal cur = Error != null ? 0 : Value;
        switch (key)
        {
            case "MC": Memory = null; break;
            case "MR":
                if (Memory is decimal m)
                {
                    Error = null;
                    _entry = Num.Plain(m);
                    _replace = true;
                    _operandReady = true;
                    if (_afterEquals) { Expression = ""; _afterEquals = false; _repeatOp = null; }
                }
                break;
            case "MS": Memory = cur; _replace = true; break;
            case "M+":
                try { Memory = (Memory ?? 0) + cur; } catch (OverflowException) { Fail("Overflow"); return; }
                _replace = true; break;
            case "M−":
                try { Memory = (Memory ?? 0) - cur; } catch (OverflowException) { Fail("Overflow"); return; }
                _replace = true; break;
        }
        Raise();
    }

    /// <summary>Puts a value on the display (paste, history, the other modes' "Send to calculator").</summary>
    public void SetValue(decimal v, string expression = null)
    {
        Error = null;
        _entry = Num.Plain(v);
        _replace = true;
        _operandReady = true;
        if (expression != null || _afterEquals)
        {
            Expression = expression ?? "";
            _afterEquals = false;
            _repeatOp = null;
            if (expression != null) _pending = null;
        }
        Raise();
    }

    public void RestoreMemory(decimal? m) { Memory = m; Raise(); }

    public void ClearHistory() { History.Clear(); Raise(); }

    /// <summary>Redraw (after a display setting such as thousands separators changes).</summary>
    public void Refresh() => Raise();

    // ------------------------------------------------------------------ helpers

    private bool TryApply(decimal a, string op, decimal b, out decimal r)
    {
        r = 0;
        try
        {
            switch (op)
            {
                case "+": r = a + b; break;
                case "−": r = a - b; break;
                case "×": r = a * b; break;
                case "÷":
                    if (b == 0) { Fail(a == 0 ? "Result is undefined" : "Can't divide by zero"); return false; }
                    r = a / b; break;
                default: r = b; break;
            }
            return true;
        }
        catch (OverflowException) { Fail("Overflow"); return false; }
    }

    private void Fail(string message)
    {
        Error = message;
        _pending = null;
        _replace = true;
        _operandReady = false;
        _afterEquals = false;
        _repeatOp = null;
        _entry = "0";
        Tape?.Invoke(new TapeEntry { Number = message, Symbol = "", IsTotal = true });
        Raise();
    }

    private void ResetAll(bool keepExpression)
    {
        Error = null;
        _entry = "0";
        _acc = 0;
        _pending = null;
        _replace = true;
        _operandReady = false;
        _afterEquals = false;
        _repeatOp = null;
        if (!keepExpression) Expression = "";
    }

    private TapeEntry _lastLogged;

    private void Log(string number, string symbol)
    {
        _lastLogged = new TapeEntry { Number = number, Symbol = symbol };
        Tape?.Invoke(_lastLogged);
    }

    private void ChangeLastSymbol(string op)
    {
        if (_lastLogged != null && !_lastLogged.IsTotal) _lastLogged.Symbol = op;
        Tape?.Invoke(null); // "refresh" signal
    }

    private void LogTotal(string number)
    {
        _lastLogged = new TapeEntry { Number = number, Symbol = "", IsTotal = true };
        Tape?.Invoke(_lastLogged);
    }

    private static int DigitCount(string s)
    {
        int n = 0;
        foreach (char c in s) if (char.IsDigit(c)) n++;
        return n;
    }

    private void Raise() => Changed?.Invoke();
}
