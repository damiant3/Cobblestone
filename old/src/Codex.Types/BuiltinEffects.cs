using Codex.Ast;
using Codex.Core;
using Codex.Syntax;

namespace Codex.Types;

sealed class BuiltinEffects
{
    static IReadOnlyList<EffectDef>? s_cached;

    public static IReadOnlyList<EffectDef> Load()
    {
        if (s_cached is not null)
            return s_cached;

        List<EffectDef> allEffects = [];
        DiagnosticBag diagnostics = new();
        Desugarer desugarer = new(diagnostics);

        foreach (string source in BuiltinChapters.EffectSources())
        {
            SourceText src = new("<builtin-effects>", source);
            Lexer lexer = new(src, diagnostics);
            IReadOnlyList<Token> tokens = lexer.TokenizeAll();
            Parser parser = new(tokens, diagnostics);
            DocumentNode document = parser.ParseDocument();
            Chapter chapter = desugarer.Desugar(document, "builtins");
            allEffects.AddRange(chapter.EffectDefs);
        }

        s_cached = allEffects;
        return s_cached;
    }
}
