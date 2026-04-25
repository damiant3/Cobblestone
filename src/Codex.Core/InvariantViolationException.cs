namespace Codex.Core;

// Compiler bug, not user error. Thrown by InvariantVerifier when a phase's
// output breaks a precondition the next phase depends on. Surfaces with a
// structured header so it's obvious the fault is internal.
public sealed class InvariantViolationException : Exception
{
    public string Phase { get; }
    public string Invariant { get; }
    public string Detail { get; }
    public SourceSpan? Location { get; }

    public InvariantViolationException(string phase, string invariant, string detail, SourceSpan? location = null)
        : base(FormatMessage(phase, invariant, detail, location))
    {
        Phase = phase;
        Invariant = invariant;
        Detail = detail;
        Location = location;
    }

    static string FormatMessage(string phase, string invariant, string detail, SourceSpan? location)
    {
        string where = location is SourceSpan s ? $"\n  at:        {s}" : "";
        return $"COMPILER INVARIANT VIOLATED\n  phase:     {phase}\n  invariant: {invariant}\n  detail:    {detail}{where}";
    }
}
