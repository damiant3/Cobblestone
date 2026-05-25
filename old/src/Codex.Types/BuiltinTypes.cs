using System.Collections.Immutable;
using Codex.Ast;
using Codex.Core;

namespace Codex.Types;

// Wraps each BuiltinChapter.TypeDefs list in a synthetic Chapter so a
// `cites Codex chapter X` can inject X's record/variant types into the
// citing chapter's scope via the same ResolvedChapter → TypeChecker.CiteChapter
// path used for foreword cites. Parallel to BuiltinEffects (which does the same
// for effect declarations).
public sealed class BuiltinTypes
{
    static IReadOnlyDictionary<string, Chapter>? s_cached;

    public static Chapter? ChapterFor(string builtinChapterName)
    {
        EnsureLoaded();
        return s_cached!.TryGetValue(builtinChapterName, out Chapter? ch) ? ch : null;
    }

    static void EnsureLoaded()
    {
        if (s_cached is not null) return;

        Dictionary<string, Chapter> built = new();
        SourceSpan span = SourceSpan.Single(0, 1, 1, "<builtin-types>");

        foreach ((string chapterName, ImmutableArray<TypeDef> typeDefs) in BuiltinChapters.TypeDefLists())
        {
            Chapter ch = new(
                QualifiedName.Simple(chapterName),
                Definitions: [],
                TypeDefinitions: typeDefs,
                Claims: [],
                Proofs: [],
                Span: span);
            built[chapterName] = ch;
        }

        s_cached = built;
    }
}
