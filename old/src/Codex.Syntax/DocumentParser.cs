using Codex.Core;

namespace Codex.Syntax;

/// <summary>
/// Parses a .codex source file, auto-detecting whether it uses the prose
/// (Chapter:/Section:) format or the flat code-only format. Use this as the
/// single entry point for whole-file parsing — bypassing it and going
/// straight to Lexer+Parser will misread any file that starts with
/// `Chapter:`.
/// </summary>
public static class DocumentParser
{
    public static DocumentNode Parse(SourceText source, DiagnosticBag diagnostics)
    {
        if (ProseParser.IsProseDocument(source.Content))
        {
            ProseParser proseParser = new(source, diagnostics);
            return proseParser.ParseDocument();
        }
        Lexer lexer = new(source, diagnostics);
        IReadOnlyList<Token> tokens = lexer.TokenizeAll();
        Parser parser = new(tokens, diagnostics);
        return parser.ParseDocument();
    }
}
