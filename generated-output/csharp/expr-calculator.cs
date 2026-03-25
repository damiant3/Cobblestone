using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

Codex_expr_calculator.main();

public abstract record Expr;

public sealed record Lit(long Field0) : Expr;
public sealed record Add(Expr Field0, Expr Field1) : Expr;
public sealed record Sub(Expr Field0, Expr Field1) : Expr;
public sealed record Mul(Expr Field0, Expr Field1) : Expr;
public sealed record Div(Expr Field0, Expr Field1) : Expr;

public sealed record ParseResult(Expr expr, long pos);

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
                if ((input[(int)pos].ToString().Length > 0 && char.IsWhiteSpace(input[(int)pos].ToString()[0])))
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
                if ((input[(int)pos].ToString().Length > 0 && char.IsDigit(input[(int)pos].ToString()[0])))
                {
                    var d = long.Parse(input[(int)pos].ToString());
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
        return ((pos >= len) ? 0L : ((input[(int)pos].ToString().Length > 0 && char.IsDigit(input[(int)pos].ToString()[0])) ? (1L + digit_count(input, (pos + 1L), len)) : 0L));
    }

    public static ParseResult parse_atom(string input, long start)
    {
        return ((Func<long, ParseResult>)((pos) => ((Func<long, ParseResult>)((len) => ((pos >= len) ? new ParseResult(new Lit(0L), pos) : ((input[(int)pos].ToString() == "(") ? ((Func<ParseResult, ParseResult>)((inner) => ((Func<long, ParseResult>)((after) => ((after < len) ? new ParseResult(inner.expr, (after + 1L)) : new ParseResult(inner.expr, after))))(skip_ws(input, inner.pos))))(parse_additive(input, (pos + 1L))) : ((Func<long, ParseResult>)((digits) => ((digits > 0L) ? ((Func<long, ParseResult>)((value) => new ParseResult(new Lit(value), (pos + digits))))(collect_digits(input, pos, len, 0L)) : new ParseResult(new Lit(0L), pos))))(digit_count(input, pos, len))))))(((long)input.Length))))(skip_ws(input, start));
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
                if ((input[(int)pos].ToString() == "*"))
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
                    if ((input[(int)pos].ToString() == "/"))
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
                if ((input[(int)pos].ToString() == "+"))
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
                    if ((input[(int)pos].ToString() == "-"))
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
        return ((Func<Expr, string>)((_scrutinee1_) => (_scrutinee1_ is Lit _mLit1_ ? ((Func<long, string>)((n) => Convert.ToString(n)))((long)_mLit1_.Field0) : (_scrutinee1_ is Add _mAdd1_ ? ((Func<Expr, string>)((b) => ((Func<Expr, string>)((a) => string.Concat("(", string.Concat(format(a), string.Concat(" + ", string.Concat(format(b), ")"))))))((Expr)_mAdd1_.Field0)))((Expr)_mAdd1_.Field1) : (_scrutinee1_ is Sub _mSub1_ ? ((Func<Expr, string>)((b) => ((Func<Expr, string>)((a) => string.Concat("(", string.Concat(format(a), string.Concat(" - ", string.Concat(format(b), ")"))))))((Expr)_mSub1_.Field0)))((Expr)_mSub1_.Field1) : (_scrutinee1_ is Mul _mMul1_ ? ((Func<Expr, string>)((b) => ((Func<Expr, string>)((a) => string.Concat("(", string.Concat(format(a), string.Concat(" * ", string.Concat(format(b), ")"))))))((Expr)_mMul1_.Field0)))((Expr)_mMul1_.Field1) : (_scrutinee1_ is Div _mDiv1_ ? ((Func<Expr, string>)((b) => ((Func<Expr, string>)((a) => string.Concat("(", string.Concat(format(a), string.Concat(" / ", string.Concat(format(b), ")"))))))((Expr)_mDiv1_.Field0)))((Expr)_mDiv1_.Field1) : throw new InvalidOperationException("Non-exhaustive match"))))))))(e);
    }

    public static string test_expr(string input, long expected)
    {
        return ((Func<Expr, string>)((tree) => ((Func<long, string>)((result) => ((Func<string, string>)((status) => string.Concat(status, string.Concat(": ", string.Concat(input, string.Concat(" = ", string.Concat(Convert.ToString(result), string.Concat(" (expected ", string.Concat(Convert.ToString(expected), string.Concat(")  tree: ", format(tree)))))))))))(((result == expected) ? "PASS" : "FAIL"))))(eval(tree))))(parse(input));
    }

    public static object main()
    {
        ((Func<object>)(() => {
                Console.WriteLine("=== Expression Calculator ===");
                Console.WriteLine("");
                Console.WriteLine(test_expr("42", 42L));
                Console.WriteLine(test_expr("2 + 3", 5L));
                Console.WriteLine(test_expr("10 - 4", 6L));
                Console.WriteLine(test_expr("3 * 7", 21L));
                Console.WriteLine(test_expr("100 / 5", 20L));
                Console.WriteLine(test_expr("2 + 3 * 4", 14L));
                Console.WriteLine(test_expr("10 - 2 * 3", 4L));
                Console.WriteLine(test_expr("(2 + 3) * 4", 20L));
                Console.WriteLine(test_expr("1 + 2 + 3 + 4", 10L));
                Console.WriteLine(test_expr("2 * 3 + 4 * 5", 26L));
                Console.WriteLine("");
                Console.WriteLine("All PASS = compiler correctly compiles a recursive descent parser.");
                Console.WriteLine("QED: not a quine.");
                return null;
            }))();
        return null;
    }

}
