using Codex.Core;
using Codex.Types;

namespace Codex.IR;

// Chapter-level helpers shared across emit backends. Each backend previously
// hand-rolled the "find the entry point" lookup against a raw "opening"
// string literal; this centralizes the lookup so the entry-point contract
// lives in one place.
public static class IRChapterExtensions
{
    // The no-argument definition named Names.OpeningEntryPoint ("opening")
    // that a program's runtime is expected to invoke. Emitters that need to
    // wrap the entry point's body — e.g. print the result for pure-result
    // programs — call this to locate it. Returns null for type-check-only
    // chapters with no entry point.
    public static IRDefinition? FindEntryPoint(this IRChapter chapter)
    {
        foreach (IRDefinition def in chapter.Definitions)
        {
            if (def.Name == Names.OpeningEntryPoint && def.Parameters.Length == 0)
                return def;
        }
        return null;
    }

    // Set of every SumType constructor name declared in this chapter's type
    // definitions. Emit backends previously re-walked TypeDefinitions for this
    // on every call. Production callers now get a cached set populated by
    // Lowering.Lower(ResolvedChapter) from NameResolver's ConstructorNames;
    // hand-built IR in tests (which skips the resolver) falls through to the
    // on-demand walk so the extension remains safe to call unconditionally.
    public static Set<string> CollectConstructorNames(this IRChapter chapter)
    {
        if (chapter.ConstructorNames.Count > 0)
            return chapter.ConstructorNames;

        Set<string> names = Set<string>.s_empty;
        foreach (KeyValuePair<string, CodexType> kv in chapter.TypeDefinitions)
        {
            if (kv.Value is SumType sum)
            {
                foreach (SumConstructorType ctor in sum.Constructors)
                    names = names.Add(ctor.Name.Value);
            }
        }
        return names;
    }

    // Set of every top-level definition name in this chapter. Same cache-or-
    // derive contract as CollectConstructorNames — production callers get the
    // ResolvedChapter-sourced set, hand-built IR falls back.
    public static Set<string> CollectTopLevelNames(this IRChapter chapter)
    {
        if (chapter.TopLevelNames.Count > 0)
            return chapter.TopLevelNames;

        Set<string> names = Set<string>.s_empty;
        foreach (IRDefinition def in chapter.Definitions)
            names = names.Add(def.Name);
        return names;
    }
}
