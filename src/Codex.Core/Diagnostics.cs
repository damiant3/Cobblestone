using System.Collections.Immutable;

namespace Codex.Core;

public enum DiagnosticSeverity
{
    Hint,
    Info,
    Warning,
    Error
}

public sealed record Diagnostic(
    DiagnosticSeverity Severity,
    int Code,
    string Message,
    SourceSpan Span,
    ImmutableArray<SourceSpan> RelatedSpans)
{
    public Diagnostic(DiagnosticSeverity severity, int code, string message, SourceSpan span)
        : this(severity, code, message, span, ImmutableArray<SourceSpan>.Empty)
    {
    }

    public bool IsError => Severity == DiagnosticSeverity.Error;

    // Canonical rendered form of the code, e.g. "CDX3002". Stored internally
    // as a plain int; the "CDX" prefix is automatic at display time.
    public string FormattedCode => CdxCodes.FormatCode(Code);

    public override string ToString() =>
        $"{Severity} {FormattedCode}: {Message} at {Span}";
}

public sealed class DiagnosticBag
{
    const int MaxErrors = 20;

    readonly List<Diagnostic> m_diagnostics = [];
    readonly object m_lock = new();
    int m_errorCount;

    public void Add(Diagnostic diagnostic)
    {
        lock (m_lock)
        {
            if (diagnostic.IsError)
            {
                m_errorCount++;
                if (m_errorCount > MaxErrors)
                {
                    return;
                }

                if (m_errorCount == MaxErrors)
                {
                    m_diagnostics.Add(new Diagnostic(
                        DiagnosticSeverity.Error, CdxCodes.TooManyErrors,
                        $"Too many errors ({MaxErrors}). Further errors suppressed.",
                        diagnostic.Span));
                    return;
                }
            }
            m_diagnostics.Add(diagnostic);
        }
    }

    public void Error(int code, string message, SourceSpan span)
    {
        Add(new Diagnostic(DiagnosticSeverity.Error, code, message, span));
    }

    public void Error(int code, string message, SourceSpan span, params SourceSpan[] relatedSpans)
    {
        Add(new Diagnostic(DiagnosticSeverity.Error, code, message, span,
            System.Collections.Immutable.ImmutableArray.Create(relatedSpans)));
    }

    public void Warning(int code, string message, SourceSpan span)
    {
        Add(new Diagnostic(DiagnosticSeverity.Warning, code, message, span));
    }

    public void Info(int code, string message, SourceSpan span)
    {
        Add(new Diagnostic(DiagnosticSeverity.Info, code, message, span));
    }

    public bool HasErrors
    {
        get
        {
            lock (m_lock) { return m_diagnostics.Any(d => d.IsError); }
        }
    }

    public int Count
    {
        get
        {
            lock (m_lock) { return m_diagnostics.Count; }
        }
    }

    public ImmutableArray<Diagnostic> ToImmutable()
    {
        lock (m_lock) { return m_diagnostics.ToImmutableArray(); }
    }
}
