using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

Codex_expr_calculator.main();

public sealed record ParseResult(Expr expr, long pos);

public abstract record Expr;

public sealed record Lit(long Field0) : Expr;
public sealed record Add(Expr Field0, Expr Field1) : Expr;
public sealed record Sub(Expr Field0, Expr Field1) : Expr;
public sealed record Mul(Expr Field0, Expr Field1) : Expr;
public sealed record Div(Expr Field0, Expr Field1) : Expr;

static class _Cce {
    static readonly int[] _toUni = {
        0, 10, 13, 9, 32, 160, 8201, 8239,
        48, 49, 50, 51, 52, 53, 54, 55, 56, 57,
        101, 116, 97, 111, 105, 110, 115, 104, 114, 100,
        108, 99, 117, 109, 119, 102, 103, 121, 112, 98,
        118, 107, 106, 120, 113, 122,
        69, 84, 65, 79, 73, 78, 83, 72, 82, 68,
        76, 67, 85, 77, 87, 70, 71, 89, 80, 66,
        86, 75, 74, 88, 81, 90,
        46, 44, 33, 63, 58, 59, 39, 34, 45, 40,
        41, 47, 64, 35, 43, 61, 42, 38, 95, 92,
        233, 232, 234, 235, 225, 224, 226, 228, 243, 242,
        244, 246, 250, 249, 251, 252, 241, 231, 223, 237,
        236, 238,
        1072, 1086, 1077, 1080, 1085, 1090, 1089, 1088,
        1074, 1083, 1082, 1084, 1076, 1087, 1091, 1075
    };
    static readonly Dictionary<int, int> _fromUni = new();
    static _Cce() { for (int i = 0; i < 128; i++) _fromUni[_toUni[i]] = i; }
    public static string FromUnicode(string s) {
        var cs = new char[s.Length];
        for (int i = 0; i < s.Length; i++) {
            int u = s[i];
            cs[i] = _fromUni.TryGetValue(u, out int c) ? (char)c : (char)0;
        }
        return new string(cs);
    }
    public static string ToUnicode(string s) {
        var cs = new char[s.Length];
        for (int i = 0; i < s.Length; i++) {
            int b = s[i];
            cs[i] = (b >= 0 && b < 128) ? (char)_toUni[b] : '\uFFFD';
        }
        return new string(cs);
    }
    public static long UniToCce(long u) {
        return _fromUni.TryGetValue((int)u, out int c) ? c : 0;
    }
    public static long CceToUni(long b) {
        return (b >= 0 && b < 128) ? _toUni[(int)b] : 65533;
    }
}

public static class Codex_expr_calculator
{
    public static long skip_ws(string input, long pos)
    {
        while (true)
        {
            if ((pos >= ((long)input.Length)))
            {
                return pos;
            }
            else
            {
                if ((((long)input[(int)pos]) <= 7L))
                {
                    var _tco_0 = input;
                    var _tco_1 = (pos + 1L);
                    input = _tco_0;
                    pos = _tco_1;
                    continue;
                }
                else
                {
                    return pos;
                }
            }
        }
    }

    public static long collect_digits(string input, long pos, long len, long acc)
    {
        while (true)
        {
            if ((pos >= len))
            {
                return acc;
            }
            else
            {
                if ((((long)input[(int)pos]) >= 8L && ((long)input[(int)pos]) <= 17L))
                {
                    var d = (((long)input[(int)pos]) - 48L);
                    var _tco_0 = input;
                    var _tco_1 = (pos + 1L);
                    var _tco_2 = len;
                    var _tco_3 = ((acc * 10L) + d);
                    input = _tco_0;
                    pos = _tco_1;
                    len = _tco_2;
                    acc = _tco_3;
                    continue;
                }
                else
                {
                    return acc;
                }
            }
        }
    }

    public static long digit_count(string input, long pos, long len)
    {
        return ((pos >= len) ? 0L : ((((long)input[(int)pos]) >= 8L && ((long)input[(int)pos]) <= 17L) ? (1L + digit_count(input, (pos + 1L), len)) : 0L));
    }

    public static ParseResult parse_atom(string input, long start)
    {
        return ((Func<long, ParseResult>)((pos) => ((Func<long, ParseResult>)((len) => ((pos >= len) ? new ParseResult(new Lit(0L), pos) : ((((char)((long)input[(int)pos])).ToString() == "O") ? ((Func<ParseResult, ParseResult>)((inner) => ((Func<long, ParseResult>)((after) => ((after < len) ? new ParseResult(inner.expr, (after + 1L)) : new ParseResult(inner.expr, after))))(skip_ws(input, inner.pos))))(parse_additive(input, (pos + 1L))) : ((Func<long, ParseResult>)((digits) => ((digits > 0L) ? ((Func<long, ParseResult>)((value) => new ParseResult(new Lit(value), (pos + digits))))(collect_digits(input, pos, len, 0L)) : new ParseResult(new Lit(0L), pos))))(digit_count(input, pos, len))))))(((long)input.Length))))(skip_ws(input, start));
    }

    public static ParseResult parse_multiplicative(string input, long start)
    {
        return ((Func<ParseResult, ParseResult>)((left) => continue_multiplicative(input, left)))(parse_atom(input, start));
    }

    public static ParseResult continue_multiplicative(string input, ParseResult current)
    {
        while (true)
        {
            var pos = skip_ws(input, current.pos);
            var len = ((long)input.Length);
            if ((pos >= len))
            {
                return current;
            }
            else
            {
                if ((((char)((long)input[(int)pos])).ToString() == "V"))
                {
                    var right = parse_atom(input, (pos + 1L));
                    var _tco_0 = input;
                    var _tco_1 = new ParseResult(new Mul(current.expr, right.expr), right.pos);
                    input = _tco_0;
                    current = _tco_1;
                    continue;
                }
                else
                {
                    if ((((char)((long)input[(int)pos])).ToString() == "Q"))
                    {
                        var right = parse_atom(input, (pos + 1L));
                        var _tco_0 = input;
                        var _tco_1 = new ParseResult(new Div(current.expr, right.expr), right.pos);
                        input = _tco_0;
                        current = _tco_1;
                        continue;
                    }
                    else
                    {
                        return current;
                    }
                }
            }
        }
    }

    public static ParseResult parse_additive(string input, long start)
    {
        return ((Func<ParseResult, ParseResult>)((left) => continue_additive(input, left)))(parse_multiplicative(input, start));
    }

    public static ParseResult continue_additive(string input, ParseResult current)
    {
        while (true)
        {
            var pos = skip_ws(input, current.pos);
            var len = ((long)input.Length);
            if ((pos >= len))
            {
                return current;
            }
            else
            {
                if ((((char)((long)input[(int)pos])).ToString() == "T"))
                {
                    var right = parse_multiplicative(input, (pos + 1L));
                    var _tco_0 = input;
                    var _tco_1 = new ParseResult(new Add(current.expr, right.expr), right.pos);
                    input = _tco_0;
                    current = _tco_1;
                    continue;
                }
                else
                {
                    if ((((char)((long)input[(int)pos])).ToString() == "N"))
                    {
                        var right = parse_multiplicative(input, (pos + 1L));
                        var _tco_0 = input;
                        var _tco_1 = new ParseResult(new Sub(current.expr, right.expr), right.pos);
                        input = _tco_0;
                        current = _tco_1;
                        continue;
                    }
                    else
                    {
                        return current;
                    }
                }
            }
        }
    }

    public static Expr parse(string input)
    {
        return parse_additive(input, 0L).expr;
    }

    public static long eval(Expr e)
    {
        return ((Func<Expr, long>)((_scrutinee0_) => (_scrutinee0_ is Lit _mLit0_ ? ((Func<long, long>)((n) => n))((long)_mLit0_.Field0) : (_scrutinee0_ is Add _mAdd0_ ? ((Func<Expr, long>)((b) => ((Func<Expr, long>)((a) => (eval(a) + eval(b))))((Expr)_mAdd0_.Field0)))((Expr)_mAdd0_.Field1) : (_scrutinee0_ is Sub _mSub0_ ? ((Func<Expr, long>)((b) => ((Func<Expr, long>)((a) => (eval(a) - eval(b))))((Expr)_mSub0_.Field0)))((Expr)_mSub0_.Field1) : (_scrutinee0_ is Mul _mMul0_ ? ((Func<Expr, long>)((b) => ((Func<Expr, long>)((a) => (eval(a) * eval(b))))((Expr)_mMul0_.Field0)))((Expr)_mMul0_.Field1) : (_scrutinee0_ is Div _mDiv0_ ? ((Func<Expr, long>)((b) => ((Func<Expr, long>)((a) => (eval(a) / eval(b))))((Expr)_mDiv0_.Field0)))((Expr)_mDiv0_.Field1) : throw new InvalidOperationException("Non-exhaustive match"))))))))(e);
    }

    public static string format(Expr e)
    {
        return ((Func<Expr, string>)((_scrutinee1_) => (_scrutinee1_ is Lit _mLit1_ ? ((Func<long, string>)((n) => _Cce.FromUnicode(Convert.ToString(n))))((long)_mLit1_.Field0) : (_scrutinee1_ is Add _mAdd1_ ? ((Func<Expr, string>)((b) => ((Func<Expr, string>)((a) => string.Concat("O", format(a), "\u0004T\u0004", format(b), "P")))((Expr)_mAdd1_.Field0)))((Expr)_mAdd1_.Field1) : (_scrutinee1_ is Sub _mSub1_ ? ((Func<Expr, string>)((b) => ((Func<Expr, string>)((a) => string.Concat("O", format(a), "\u0004N\u0004", format(b), "P")))((Expr)_mSub1_.Field0)))((Expr)_mSub1_.Field1) : (_scrutinee1_ is Mul _mMul1_ ? ((Func<Expr, string>)((b) => ((Func<Expr, string>)((a) => string.Concat("O", format(a), "\u0004V\u0004", format(b), "P")))((Expr)_mMul1_.Field0)))((Expr)_mMul1_.Field1) : (_scrutinee1_ is Div _mDiv1_ ? ((Func<Expr, string>)((b) => ((Func<Expr, string>)((a) => string.Concat("O", format(a), "\u0004Q\u0004", format(b), "P")))((Expr)_mDiv1_.Field0)))((Expr)_mDiv1_.Field1) : throw new InvalidOperationException("Non-exhaustive match"))))))))(e);
    }

    public static string test_expr(string input, long expected)
    {
        return ((Func<Expr, string>)((tree) => ((Func<long, string>)((result) => ((Func<string, string>)((status) => string.Concat(status, "J\u0004", input, "\u0004U\u0004", _Cce.FromUnicode(Convert.ToString(result)), "\u0004O\u0012)$\u0012\u001D\u0013\u0012\u001B\u0004", _Cce.FromUnicode(Convert.ToString(expected)), "P\u0004\u0004\u0013\u001A\u0012\u0012J\u0004", format(tree))))(((result == expected) ? ">.22" : ";.06"))))(eval(tree))))(parse(input));
    }

    public static object main()
    {
        ((Func<object>)(() => {
                Console.WriteLine(_Cce.ToUnicode("UUU\u0004,)$\u001A\u0012\u0018\u0018\u0016\u0015\u0017\u00047\u0014\u001C\u001D\u001E\u001C\u0014\u0013\u0015\u001A\u0004UUU"));
                Console.WriteLine(_Cce.ToUnicode(""));
                Console.WriteLine(_Cce.ToUnicode(test_expr("\u000C\u000A", 42L)));
                Console.WriteLine(_Cce.ToUnicode(test_expr("\u000A\u0004T\u0004\u000B", 5L)));
                Console.WriteLine(_Cce.ToUnicode(test_expr("\u0009\u0008\u0004N\u0004\u000C", 6L)));
                Console.WriteLine(_Cce.ToUnicode(test_expr("\u000B\u0004V\u0004\u000F", 21L)));
                Console.WriteLine(_Cce.ToUnicode(test_expr("\u0009\u0008\u0008\u0004Q\u0004\u000D", 20L)));
                Console.WriteLine(_Cce.ToUnicode(test_expr("\u000A\u0004T\u0004\u000B\u0004V\u0004\u000C", 14L)));
                Console.WriteLine(_Cce.ToUnicode(test_expr("\u0009\u0008\u0004N\u0004\u000A\u0004V\u0004\u000B", 4L)));
                Console.WriteLine(_Cce.ToUnicode(test_expr("O\u000A\u0004T\u0004\u000BP\u0004V\u0004\u000C", 20L)));
                Console.WriteLine(_Cce.ToUnicode(test_expr("\u0009\u0004T\u0004\u000A\u0004T\u0004\u000B\u0004T\u0004\u000C", 10L)));
                Console.WriteLine(_Cce.ToUnicode(test_expr("\u000A\u0004V\u0004\u000B\u0004T\u0004\u000C\u0004V\u0004\u000D", 26L)));
                Console.WriteLine(_Cce.ToUnicode(""));
                Console.WriteLine(_Cce.ToUnicode(".\u001C\u001C\u0004>.22\u0004U\u0004\u001D\u0015\u001F$\u0016\u001C\u0012\u001A\u0004\u001D\u0015\u001A\u001A\u0012\u001D\u0013\u001C#\u0004\u001D\u0015\u001F$\u0016\u001C\u0012\u0018\u0004\u0014\u0004\u001A\u0012\u001D\u001E\u001A\u0018\u0016&\u0012\u0004\u001B\u0012\u0018\u001D\u0012\u0017\u0013\u0004$\u0014\u001A\u0018\u0012\u001AF"));
                Console.WriteLine(_Cce.ToUnicode("D,5J\u0004\u0017\u0015\u0013\u0004\u0014\u0004*\u001E\u0016\u0017\u0012F"));
                return null;
            }))();
        return null;
    }

}
