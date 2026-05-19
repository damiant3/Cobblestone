using System.Collections.Immutable;
using Codex.Core;
using Codex.Syntax;
using Codex.Ast;
using Codex.Semantics;
using Codex.Types;

namespace Codex.Lsp;

internal sealed class AnalysisResult
{
    public required ImmutableArray<Diagnostic> Diagnostics { get; init; }
    public required Map<string, CodexType> Types { get; init; }
    public required IReadOnlyList<Definition> Definitions { get; init; }
    public required IReadOnlyList<TypeDef> TypeDefinitions { get; init; }
    public required IReadOnlyList<Token> Tokens { get; init; }
}

internal static class Analyzer
{
    public static AnalysisResult Analyze(string uri, string text)
    {
        SourceText source = new(uri, text);
        DiagnosticBag bag = new();

        DocumentNode document;
        IReadOnlyList<Token> tokens;
        if (ProseParser.IsProseDocument(text))
        {
            ProseParser proseParser = new(source, bag);
            document = proseParser.ParseDocument();
            tokens = [];
        }
        else
        {
            Lexer lexer = new(source, bag);
            tokens = lexer.TokenizeAll();
            Parser parser = new(tokens, bag);
            document = parser.ParseDocument();
        }

        Desugarer desugarer = new(bag);
        string chapterName = Path.GetFileNameWithoutExtension(uri);
        Chapter chapter = desugarer.Desugar(document, chapterName);

        if (bag.HasErrors)
        {
            return new AnalysisResult
            {
                Diagnostics = bag.ToImmutable(),
                Types = Map<string, CodexType>.s_empty,
                Definitions = chapter.Definitions,
                TypeDefinitions = chapter.TypeDefinitions,
                Tokens = tokens,
            };
        }

        NameResolver resolver = new(bag);
        ResolvedChapter resolved = resolver.Resolve(chapter);

        if (bag.HasErrors)
        {
            return new AnalysisResult
            {
                Diagnostics = bag.ToImmutable(),
                Types = Map<string, CodexType>.s_empty,
                Definitions = chapter.Definitions,
                TypeDefinitions = chapter.TypeDefinitions,
                Tokens = tokens,
            };
        }

        TypeChecker checker = new(bag);
        Map<string, CodexType> types = checker.CheckChapter(resolved.Chapter);

        if (!bag.HasErrors)
        {
            LinearityChecker linearityChecker = new(bag, types);
            linearityChecker.CheckChapter(resolved.Chapter);
        }

        return new AnalysisResult
        {
            Diagnostics = bag.ToImmutable(),
            Types = types,
            Definitions = resolved.Chapter.Definitions,
            TypeDefinitions = resolved.Chapter.TypeDefinitions,
            Tokens = tokens,
        };
    }
}
