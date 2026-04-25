using Codex.Core;
using Codex.Semantics;

namespace Codex.Cli;

// Pretty-printer for ResolvedChapter: show resolved top-level/type/ctor name
// sets and cited chapters, then the underlying desugared chapter.
static class ResolvedDumpFormatter
{
    public static void Write(TextWriter w, ResolvedChapter resolved)
    {
        WriteNameSet(w, "top-level-names", resolved.TopLevelNames);
        WriteNameSet(w, "type-names", resolved.TypeNames);
        WriteNameSet(w, "constructor-names", resolved.ConstructorNames);

        if (resolved.CitedChapters.Count > 0)
        {
            DesugaredDumpFormatter.WriteLine(w, 0, "(cited-chapters");
            foreach (ResolvedChapter cited in resolved.CitedChapters)
            {
                DesugaredDumpFormatter.WriteLine(w, 1, $"(chapter {cited.Chapter.Name} quire={cited.Chapter.Quire ?? "<none>"})");
            }
            DesugaredDumpFormatter.WriteLine(w, 0, ")");
        }

        w.WriteLine();
        DesugaredDumpFormatter.Write(w, resolved.Chapter);
    }

    static void WriteNameSet(TextWriter w, string label, Set<string> names)
    {
        List<string> sorted = [];
        foreach (string n in names)
        {
            sorted.Add(n);
        }
        sorted.Sort(StringComparer.Ordinal);
        DesugaredDumpFormatter.WriteLine(w, 0, $"({label} count={sorted.Count} [{string.Join(",", sorted)}])");
    }
}
