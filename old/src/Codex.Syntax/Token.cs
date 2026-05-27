using Codex.Core;

namespace Codex.Syntax;

public sealed record Token(
    TokenKind Kind,
    string Text,
    SourceSpan Span)
{
    public object? LiteralValue { get; init; }

    public override string ToString() => $"{Kind}({Text})";
}
