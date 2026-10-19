using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace DevelopmentTools.CodeCheck
{
    /// <summary>變數或函式不存在／沒有資料時拋出，結果會標成「資料不足」並寫明缺什麼。</summary>
    public class MissingDataException : Exception
    {
        public string Name { get; }
        public MissingDataException(string name, string why = null)
            : base(why == null ? $"缺少資料：{name}" : $"缺少資料：{name}（{why}）")
        {
            Name = name;
        }
    }

    public class ExprSyntaxException : Exception
    {
        public ExprSyntaxException(string msg) : base(msg) { }
    }

    /// <summary>規則運算式的變數與函式來源（專案、樓層、房間、元素……逐層往上找）。</summary>
    public interface IVarSource
    {
        bool TryGet(string name, out object value);
        bool TryCall(string name, object[] args, out object value);
    }

    /// <summary>
    /// 規則運算式：數字、'字串'、true/false、變數、+ - * /、比較、&amp;&amp; || !、括號、函式呼叫。
    /// 內建函式：if(c,a,b)、min、max、ceil、floor、abs、round、in(x, a, b, ...)。
    /// 其他函式（has、deviceCount…）由 IVarSource 提供。
    /// 運算式解析後快取，同一條規則重複套用不用重新解析。
    /// </summary>
    public static class Expr
    {
        private static readonly Dictionary<string, Node> Cache = new Dictionary<string, Node>();

        public static object Eval(string text, IVarSource vars)
        {
            if (string.IsNullOrWhiteSpace(text)) return true;
            Node n;
            lock (Cache)
            {
                if (!Cache.TryGetValue(text, out n))
                {
                    n = new Parser(text).ParseAll();
                    Cache[text] = n;
                }
            }
            return n.Eval(vars);
        }

        public static double Num(object v)
        {
            if (v is double d) return d;
            if (v is bool b) return b ? 1 : 0;
            if (v is int i) return i;
            if (v is long l) return l;
            if (v is float f) return f;
            if (v is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double p)) return p;
            if (v == null) throw new MissingDataException("數值", "值為空");
            throw new ExprSyntaxException($"無法把「{v}」當成數字");
        }

        public static bool Bool(object v)
        {
            if (v is bool b) return b;
            if (v is double d) return Math.Abs(d) > 1e-12;
            if (v is int i) return i != 0;
            if (v is string s) return !string.IsNullOrEmpty(s);
            return v != null;
        }

        public static bool Eq(object a, object b)
        {
            if (a is string || b is string) return string.Equals(Convert.ToString(a, CultureInfo.InvariantCulture), Convert.ToString(b, CultureInfo.InvariantCulture), StringComparison.Ordinal);
            if (a is bool || b is bool) return Bool(a) == Bool(b);
            return Math.Abs(Num(a) - Num(b)) < 1e-9;
        }

        /// <summary>列出運算式用到的識別字（給資料完整度檢查用）。</summary>
        public static IEnumerable<string> Identifiers(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) yield break;
            foreach (var t in new Lexer(text).All())
                if (t.Kind == TokKind.Ident) yield return t.Text;
        }

        // ───────────────────────── 語法樹 ─────────────────────────

        private abstract class Node
        {
            public abstract object Eval(IVarSource v);
        }

        private sealed class Lit : Node
        {
            private readonly object _v;
            public Lit(object v) { _v = v; }
            public override object Eval(IVarSource v) => _v;
        }

        private sealed class Var : Node
        {
            private readonly string _n;
            public Var(string n) { _n = n; }
            public override object Eval(IVarSource v)
            {
                if (_n == "true") return true;
                if (_n == "false") return false;
                if (v != null && v.TryGet(_n, out object o)) return o;
                throw new MissingDataException(_n);
            }
        }

        private sealed class Unary : Node
        {
            private readonly string _op; private readonly Node _a;
            public Unary(string op, Node a) { _op = op; _a = a; }
            public override object Eval(IVarSource v)
            {
                object a = _a.Eval(v);
                return _op == "!" ? (object)!Bool(a) : -Num(a);
            }
        }

        private sealed class Binary : Node
        {
            private readonly string _op; private readonly Node _a, _b;
            public Binary(string op, Node a, Node b) { _op = op; _a = a; _b = b; }
            public override object Eval(IVarSource v)
            {
                switch (_op)
                {
                    case "&&": return Bool(_a.Eval(v)) && Bool(_b.Eval(v));
                    case "||": return Bool(_a.Eval(v)) || Bool(_b.Eval(v));
                }
                object a = _a.Eval(v), b = _b.Eval(v);
                switch (_op)
                {
                    case "+":
                        if (a is string || b is string) return Convert.ToString(a, CultureInfo.InvariantCulture) + Convert.ToString(b, CultureInfo.InvariantCulture);
                        return Num(a) + Num(b);
                    case "-": return Num(a) - Num(b);
                    case "*": return Num(a) * Num(b);
                    case "/":
                        double d = Num(b);
                        if (Math.Abs(d) < 1e-12) throw new MissingDataException("除數", "除以零");
                        return Num(a) / d;
                    case "==": return Eq(a, b);
                    case "!=": return !Eq(a, b);
                    case ">": return Num(a) > Num(b);
                    case "<": return Num(a) < Num(b);
                    case ">=": return Num(a) >= Num(b) - 1e-9;
                    case "<=": return Num(a) <= Num(b) + 1e-9;
                }
                throw new ExprSyntaxException("未知運算子 " + _op);
            }
        }

        private sealed class Call : Node
        {
            private readonly string _n; private readonly List<Node> _args;
            public Call(string n, List<Node> args) { _n = n; _args = args; }
            public override object Eval(IVarSource v)
            {
                switch (_n)
                {
                    case "if":
                        if (_args.Count != 3) throw new ExprSyntaxException("if 需要 3 個參數");
                        return Bool(_args[0].Eval(v)) ? _args[1].Eval(v) : _args[2].Eval(v);
                    case "min": return _args.Select(a => Num(a.Eval(v))).Min();
                    case "max": return _args.Select(a => Num(a.Eval(v))).Max();
                    case "ceil": return Math.Ceiling(Num(_args[0].Eval(v)) - 1e-9);
                    case "floor": return Math.Floor(Num(_args[0].Eval(v)) + 1e-9);
                    case "abs": return Math.Abs(Num(_args[0].Eval(v)));
                    case "round": return Math.Round(Num(_args[0].Eval(v)), _args.Count > 1 ? (int)Num(_args[1].Eval(v)) : 0);
                    case "in":
                        object x = _args[0].Eval(v);
                        for (int i = 1; i < _args.Count; i++) if (Eq(x, _args[i].Eval(v))) return true;
                        return false;
                }
                object[] vals = _args.Select(a => a.Eval(v)).ToArray();
                if (v != null && v.TryCall(_n, vals, out object r)) return r;
                throw new MissingDataException(_n + "()");
            }
        }

        // ───────────────────────── 詞法 ─────────────────────────

        private enum TokKind { Num, Str, Ident, Op, LParen, RParen, Comma, End }

        private struct Tok
        {
            public TokKind Kind; public string Text; public double Value;
        }

        private sealed class Lexer
        {
            private readonly string _s; private int _i;
            public Lexer(string s) { _s = s; }

            public IEnumerable<Tok> All()
            {
                Tok t;
                do { t = Next(); yield return t; } while (t.Kind != TokKind.End);
            }

            public Tok Next()
            {
                while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++;
                if (_i >= _s.Length) return new Tok { Kind = TokKind.End };
                char c = _s[_i];
                if (char.IsDigit(c) || (c == '.' && _i + 1 < _s.Length && char.IsDigit(_s[_i + 1])))
                {
                    int st = _i;
                    while (_i < _s.Length && (char.IsDigit(_s[_i]) || _s[_i] == '.')) _i++;
                    string num = _s.Substring(st, _i - st);
                    return new Tok { Kind = TokKind.Num, Text = num, Value = double.Parse(num, CultureInfo.InvariantCulture) };
                }
                if (c == '\'' || c == '"')
                {
                    char q = c; int st = ++_i; var sb = new StringBuilder();
                    while (_i < _s.Length && _s[_i] != q) sb.Append(_s[_i++]);
                    if (_i >= _s.Length) throw new ExprSyntaxException("字串沒有結尾：" + _s);
                    _i++;
                    return new Tok { Kind = TokKind.Str, Text = sb.ToString() };
                }
                if (char.IsLetter(c) || c == '_')
                {
                    int st = _i;
                    while (_i < _s.Length && (char.IsLetterOrDigit(_s[_i]) || _s[_i] == '_' || _s[_i] == '.')) _i++;
                    return new Tok { Kind = TokKind.Ident, Text = _s.Substring(st, _i - st) };
                }
                _i++;
                switch (c)
                {
                    case '(': return new Tok { Kind = TokKind.LParen, Text = "(" };
                    case ')': return new Tok { Kind = TokKind.RParen, Text = ")" };
                    case ',': return new Tok { Kind = TokKind.Comma, Text = "," };
                    case '+': case '-': case '*': case '/':
                        return new Tok { Kind = TokKind.Op, Text = c.ToString() };
                    case '&': if (_i < _s.Length && _s[_i] == '&') { _i++; return new Tok { Kind = TokKind.Op, Text = "&&" }; } break;
                    case '|': if (_i < _s.Length && _s[_i] == '|') { _i++; return new Tok { Kind = TokKind.Op, Text = "||" }; } break;
                    case '=': if (_i < _s.Length && _s[_i] == '=') { _i++; return new Tok { Kind = TokKind.Op, Text = "==" }; } break;
                    case '!': if (_i < _s.Length && _s[_i] == '=') { _i++; return new Tok { Kind = TokKind.Op, Text = "!=" }; } return new Tok { Kind = TokKind.Op, Text = "!" };
                    case '>': if (_i < _s.Length && _s[_i] == '=') { _i++; return new Tok { Kind = TokKind.Op, Text = ">=" }; } return new Tok { Kind = TokKind.Op, Text = ">" };
                    case '<': if (_i < _s.Length && _s[_i] == '=') { _i++; return new Tok { Kind = TokKind.Op, Text = "<=" }; } return new Tok { Kind = TokKind.Op, Text = "<" };
                }
                throw new ExprSyntaxException($"無法辨識的字元「{c}」：{_s}");
            }
        }

        // ───────────────────────── 語法分析（優先序：|| && 比較 加減 乘除 一元） ─────────────────────────

        private sealed class Parser
        {
            private readonly Lexer _lx; private Tok _t; private readonly string _src;
            public Parser(string s) { _src = s; _lx = new Lexer(s); _t = _lx.Next(); }

            private void Eat() { _t = _lx.Next(); }
            private bool IsOp(params string[] ops) => _t.Kind == TokKind.Op && ops.Contains(_t.Text);

            public Node ParseAll()
            {
                Node n = Or();
                if (_t.Kind != TokKind.End) throw new ExprSyntaxException($"運算式多出「{_t.Text}」：{_src}");
                return n;
            }

            private Node Or()
            {
                Node a = And();
                while (IsOp("||")) { Eat(); a = new Binary("||", a, And()); }
                return a;
            }

            private Node And()
            {
                Node a = Cmp();
                while (IsOp("&&")) { Eat(); a = new Binary("&&", a, Cmp()); }
                return a;
            }

            private Node Cmp()
            {
                Node a = Add();
                while (IsOp("==", "!=", ">", "<", ">=", "<="))
                {
                    string op = _t.Text; Eat(); a = new Binary(op, a, Add());
                }
                return a;
            }

            private Node Add()
            {
                Node a = Mul();
                while (IsOp("+", "-")) { string op = _t.Text; Eat(); a = new Binary(op, a, Mul()); }
                return a;
            }

            private Node Mul()
            {
                Node a = Un();
                while (IsOp("*", "/")) { string op = _t.Text; Eat(); a = new Binary(op, a, Un()); }
                return a;
            }

            private Node Un()
            {
                if (IsOp("!")) { Eat(); return new Unary("!", Un()); }
                if (IsOp("-")) { Eat(); return new Unary("-", Un()); }
                return Prim();
            }

            private Node Prim()
            {
                Tok t = _t;
                switch (t.Kind)
                {
                    case TokKind.Num: Eat(); return new Lit(t.Value);
                    case TokKind.Str: Eat(); return new Lit(t.Text);
                    case TokKind.LParen:
                        Eat(); Node n = Or();
                        if (_t.Kind != TokKind.RParen) throw new ExprSyntaxException("缺少右括號：" + _src);
                        Eat(); return n;
                    case TokKind.Ident:
                        Eat();
                        if (_t.Kind == TokKind.LParen)
                        {
                            Eat();
                            var args = new List<Node>();
                            if (_t.Kind != TokKind.RParen)
                            {
                                args.Add(Or());
                                while (_t.Kind == TokKind.Comma) { Eat(); args.Add(Or()); }
                            }
                            if (_t.Kind != TokKind.RParen) throw new ExprSyntaxException("函式缺少右括號：" + _src);
                            Eat();
                            return new Call(t.Text, args);
                        }
                        return new Var(t.Text);
                }
                throw new ExprSyntaxException($"運算式不完整：{_src}");
            }
        }
    }

    /// <summary>以字典提供變數、可串接上層來源（元素 → 樓層 → 專案）。</summary>
    public class VarBag : IVarSource
    {
        public readonly Dictionary<string, object> Values = new Dictionary<string, object>(StringComparer.Ordinal);
        public readonly Dictionary<string, Func<object[], object>> Functions = new Dictionary<string, Func<object[], object>>(StringComparer.Ordinal);
        public IVarSource Parent { get; set; }

        public VarBag(IVarSource parent = null) { Parent = parent; }

        public object this[string name]
        {
            set => Values[name] = value is int i ? (double)i : value;
        }

        public bool TryGet(string name, out object value)
        {
            if (Values.TryGetValue(name, out value))
            {
                if (value == null) throw new MissingDataException(name, "模型中沒有可量測的資料");
                return true;
            }
            return Parent != null && Parent.TryGet(name, out value);
        }

        public bool TryCall(string name, object[] args, out object value)
        {
            if (Functions.TryGetValue(name, out var f)) { value = f(args); return true; }
            value = null;
            return Parent != null && Parent.TryCall(name, args, out value);
        }
    }
}
