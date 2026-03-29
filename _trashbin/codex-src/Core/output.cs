using System;
using System.Collections.Generic;
using System.Linq;

public sealed record SourceSpan(SourcePosition start, SourcePosition end, string file);

public sealed record Name(string value);

public abstract record DiagnosticSeverity;

public sealed record Error : DiagnosticSeverity;
public sealed record Warning : DiagnosticSeverity;
public sealed record Info : DiagnosticSeverity;

public sealed record SourcePosition(long line, long column, long offset);

public sealed record Diagnostic(string code, string message, DiagnosticSeverity severity);

public static class Codex_Core
{
    public static List<b> map_list(Func<a, b> f, List<a> xs)
    {
        return map_list_loop(f, xs, 0L, ((long)xs.Count), new List<b>());
    }

    public static List<b> map_list_loop(Func<a, b> f, List<a> xs, long i, long len, List<b> acc)
    {
        return ((i == len) ? acc : map_list_loop(f, xs, (i + 1L), len, Enumerable.Concat(acc, new List<b>() { f(xs[(int)i]) }).ToList()));
    }

    public static b fold_list(Func<b, Func<a, b>> f, b z, List<a> xs)
    {
        return fold_list_loop(f, z, xs, 0L, ((long)xs.Count));
    }

    public static b fold_list_loop(Func<b, Func<a, b>> f, b z, List<a> xs, long i, long len)
    {
        return ((i == len) ? z : fold_list_loop(f, f(z)(xs[(int)i]), xs, (i + 1L), len));
    }

    public static Diagnostic make_error(string code, string msg)
    {
        return new Diagnostic(code, msg, new Error());
    }

    public static Diagnostic make_warning(string code, string msg)
    {
        return new Diagnostic(code, msg, new Warning());
    }

    public static string severity_label(DiagnosticSeverity s)
    {
        return ((Func<DiagnosticSeverity, string>)((_scrutinee_) => (_scrutinee_ is Error _mError_ ? "error" : (_scrutinee_ is Warning _mWarning_ ? "warning" : (_scrutinee_ is Info _mInfo_ ? "info" : throw new InvalidOperationException("Non-exhaustive match"))))))(s);
    }

    public static string diagnostic_display(Diagnostic d)
    {
        return string.Concat(severity_label(d.severity), string.Concat(" ", string.Concat(d.code, string.Concat(": ", d.message))));
    }

    public static Name make_name(string s)
    {
        return new Name(s);
    }

    public static string name_value(Name n)
    {
        return n.value;
    }

    public static SourcePosition make_position(long line, long col, long offset)
    {
        return new SourcePosition(line, col, offset);
    }

    public static SourceSpan make_span(SourcePosition s, SourcePosition e, string f)
    {
        return new SourceSpan(s, e, f);
    }

    public static long span_length(SourceSpan span)
    {
        return (span.end.offset - span.start.offset);
    }

}
