using System.Globalization;
using System.Text;

namespace Z10.Core.Sensors;

/// <summary>
/// Formel für eigene Sensoren, z. B. <c>max([auto:cpu/Temperature/*], [auto:gpu/Temperature/*])</c>
/// oder <c>([/lpc/nct6798d/fan/0] + [/lpc/nct6798d/fan/1]) / 2</c>.
///
/// Sensoren stehen in eckigen Klammern (Id oder Auto-Abfrage). Erlaubt sind Zahlen (Punkt als Dezimaltrenner),
/// + - * / ^, Klammern und die Funktionen min, max, avg, sum (ignorieren fehlende Werte),
/// abs, round(x[, Stellen]), clamp(x, min, max).
/// Fehlt ein Sensor in einer Rechnung, ist das Ergebnis leer ("--").
/// </summary>
public sealed class Formula
{
    private readonly Node _root;

    public string Text { get; }
    public IReadOnlyList<string> SensorRefs { get; }

    private Formula(string text, Node root, List<string> refs)
    {
        Text = text;
        _root = root;
        SensorRefs = refs;
    }

    public static Formula Parse(string text)
    {
        var parser = new Parser(text ?? "");
        var root = parser.ParseAll();
        return new Formula(text ?? "", root, parser.Refs);
    }

    public static bool TryParse(string text, out Formula? formula, out string? error)
    {
        try
        {
            formula = Parse(text);
            error = null;
            return true;
        }
        catch (FormatException ex)
        {
            formula = null;
            error = ex.Message;
            return false;
        }
    }

    public double? Evaluate(Func<string, double?> lookup)
    {
        var v = _root.Eval(lookup);
        return v is double d && (double.IsNaN(d) || double.IsInfinity(d)) ? null : v;
    }

    // ---------- Syntaxbaum ----------

    private abstract class Node
    {
        public abstract double? Eval(Func<string, double?> lookup);
    }

    private sealed class Num(double value) : Node
    {
        public override double? Eval(Func<string, double?> lookup) => value;
    }

    private sealed class Ref(string id) : Node
    {
        public override double? Eval(Func<string, double?> lookup) => lookup(id);
    }

    private sealed class Neg(Node inner) : Node
    {
        public override double? Eval(Func<string, double?> lookup) => -inner.Eval(lookup);
    }

    private sealed class Bin(char op, Node a, Node b) : Node
    {
        public override double? Eval(Func<string, double?> lookup)
        {
            if (a.Eval(lookup) is not double x || b.Eval(lookup) is not double y) return null;
            return op switch
            {
                '+' => x + y,
                '-' => x - y,
                '*' => x * y,
                '/' => y == 0 ? null : x / y,
                '^' => Math.Pow(x, y),
                _ => null,
            };
        }
    }

    private sealed class Call(string name, List<Node> args) : Node
    {
        public override double? Eval(Func<string, double?> lookup)
        {
            var vals = args.Select(a => a.Eval(lookup)).ToList();
            var known = vals.Where(v => v.HasValue).Select(v => v!.Value).ToList();
            switch (name)
            {
                case "min": return known.Count == 0 ? null : known.Min();
                case "max": return known.Count == 0 ? null : known.Max();
                case "avg": return known.Count == 0 ? null : known.Average();
                case "sum": return known.Count == 0 ? null : known.Sum();
            }
            if (vals.Any(v => v is null)) return null;
            return name switch
            {
                "abs" => Math.Abs(vals[0]!.Value),
                "round" => Math.Round(vals[0]!.Value, vals.Count > 1 ? (int)Math.Clamp(vals[1]!.Value, 0, 10) : 0, MidpointRounding.AwayFromZero),
                "clamp" => Math.Clamp(vals[0]!.Value, Math.Min(vals[1]!.Value, vals[2]!.Value), Math.Max(vals[1]!.Value, vals[2]!.Value)),
                _ => null,
            };
        }
    }

    // ---------- Parser (rekursiver Abstieg) ----------

    private sealed class Parser
    {
        private static readonly Dictionary<string, (int Min, int Max)> Functions = new()
        {
            ["min"] = (1, 64), ["max"] = (1, 64), ["avg"] = (1, 64), ["sum"] = (1, 64),
            ["abs"] = (1, 1), ["round"] = (1, 2), ["clamp"] = (3, 3),
        };

        private readonly string _s;
        private int _i;
        public List<string> Refs { get; } = new();

        public Parser(string s) => _s = s;

        public Node ParseAll()
        {
            if (string.IsNullOrWhiteSpace(_s)) throw Error("Formel ist leer");
            var n = Expr();
            Skip();
            if (_i < _s.Length) throw Error($"Unerwartetes Zeichen „{_s[_i]}“");
            return n;
        }

        private Node Expr()
        {
            var n = Term();
            while (true)
            {
                Skip();
                if (Peek('+') || Peek('-')) { char op = _s[_i++]; n = new Bin(op, n, Term()); }
                else return n;
            }
        }

        private Node Term()
        {
            var n = Power();
            while (true)
            {
                Skip();
                if (Peek('*') || Peek('/')) { char op = _s[_i++]; n = new Bin(op, n, Power()); }
                else return n;
            }
        }

        private Node Power()
        {
            var n = Unary();
            Skip();
            if (Peek('^')) { _i++; return new Bin('^', n, Power()); }   // rechtsassoziativ
            return n;
        }

        private Node Unary()
        {
            Skip();
            if (Peek('-')) { _i++; return new Neg(Unary()); }
            if (Peek('+')) { _i++; return Unary(); }
            return Primary();
        }

        private Node Primary()
        {
            Skip();
            if (_i >= _s.Length) throw Error("Formel endet unerwartet");
            char c = _s[_i];

            if (c == '(')
            {
                _i++;
                var n = Expr();
                Expect(')');
                return n;
            }

            if (c == '[')
            {
                int end = _s.IndexOf(']', _i + 1);
                if (end < 0) throw Error("Fehlende „]“ nach Sensor");
                var id = _s.Substring(_i + 1, end - _i - 1).Trim();
                if (id.Length == 0) throw Error("Leerer Sensor []");
                _i = end + 1;
                if (!Refs.Contains(id)) Refs.Add(id);
                return new Ref(id);
            }

            if (char.IsDigit(c) || c == '.')
            {
                int start = _i;
                while (_i < _s.Length && (char.IsDigit(_s[_i]) || _s[_i] == '.')) _i++;
                if (!double.TryParse(_s.AsSpan(start, _i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    throw Error($"Ungültige Zahl „{_s[start.._i]}“ (Dezimalpunkt verwenden)");
                return new Num(v);
            }

            if (char.IsLetter(c))
            {
                int start = _i;
                while (_i < _s.Length && char.IsLetter(_s[_i])) _i++;
                var name = _s[start.._i].ToLowerInvariant();
                if (!Functions.TryGetValue(name, out var arity))
                    throw Error($"Unbekannte Funktion „{name}“ (erlaubt: {string.Join(", ", Functions.Keys)}). Sensoren in [ ] schreiben.");
                Expect('(');
                var args = new List<Node> { Expr() };
                Skip();
                while (Peek(','))
                {
                    _i++;
                    args.Add(Expr());
                    Skip();
                }
                Expect(')');
                if (args.Count < arity.Min || args.Count > arity.Max)
                    throw Error($"{name} erwartet {(arity.Min == arity.Max ? arity.Min.ToString() : $"{arity.Min} bis {arity.Max}")} Argument(e)");
                return new Call(name, args);
            }

            throw Error($"Unerwartetes Zeichen „{c}“");
        }

        private void Skip()
        {
            while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++;
        }

        private bool Peek(char c) => _i < _s.Length && _s[_i] == c;

        private void Expect(char c)
        {
            Skip();
            if (!Peek(c)) throw Error($"„{c}“ erwartet");
            _i++;
        }

        private FormatException Error(string msg) => new($"{msg} (Position {_i + 1})");
    }
}
