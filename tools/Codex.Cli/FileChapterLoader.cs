using Codex.Ast;
using Codex.Core;
using Codex.Semantics;
using Codex.Syntax;

namespace Codex.Cli;

/// <summary>
/// Loads chapters from a codex directory. The quire parameter is resolved to
/// a subdirectory of <paramref name="baseDirectory"/> — unless it matches
/// <paramref name="virtualQuireName"/>, in which case the root directory
/// itself is treated as the quire body (used by stdlib-style codexes like
/// the foreword, whose chapter files live at project root but are presented
/// as a single named quire).
/// </summary>
public sealed class FileChapterLoader(
    string baseDirectory,
    DiagnosticBag diagnostics,
    string? virtualQuireName = null,
    HashSet<string>? inFlight = null,
    List<string>? inFlightOrder = null) : IChapterLoader
{
    readonly string m_baseDirectory = baseDirectory;
    readonly DiagnosticBag m_diagnostics = diagnostics;
    readonly string? m_virtualQuireName = virtualQuireName;
    // Shared across a load transitive-closure: if a chapter cites back (directly
    // or transitively) into one currently being loaded, re-entry trips CDX9001
    // rather than looping. The set is passed by reference through each fresh
    // transitive-loader ctor so all levels share one view of what's in flight.
    // The parallel list preserves insertion order for the diagnostic message —
    // HashSet enumeration order is not specified so the set alone would print
    // an unstable chain like "B -> A -> A" for a real A→B→A cycle.
    readonly HashSet<string> m_inFlight = inFlight ?? new HashSet<string>();
    readonly List<string> m_inFlightOrder = inFlightOrder ?? new List<string>();
    Map<string, ResolvedChapter> m_cache = Map<string, ResolvedChapter>.s_empty;

    public ResolvedChapter? Load(string quire, string chapterName)
    {
        string key = $"{quire}::{chapterName}";
        ResolvedChapter? cached = m_cache[key];
        if (cached is not null)
        {
            return cached;
        }

        if (!m_inFlight.Add(key))
        {
            // Already being loaded further up the call stack — circular cite.
            // Use the ordered parallel list for a stable "A -> B -> A" chain.
            string cycle = string.Join(" -> ", m_inFlightOrder.Append(key));
            m_diagnostics.Error(CdxCodes.ResourceExhausted,
                $"compiler resource exhausted in chapter-loader: circular import detected ({cycle})",
                SourceSpan.Single(0, 1, 1, key));
            return null;
        }
        m_inFlightOrder.Add(key);

        try
        {
            string quireDir = (m_virtualQuireName is not null && quire == m_virtualQuireName)
                ? m_baseDirectory
                : Path.Combine(m_baseDirectory, quire);
            if (!Directory.Exists(quireDir))
            {
                return null;
            }

            string? filePath = FindFileForChapter(quireDir, chapterName);
            if (filePath is null)
            {
                return null;
            }

            string source = File.ReadAllText(filePath);
            SourceText src = new(filePath, source);
            DiagnosticBag compileDiag = new();

            DocumentNode document = DocumentParser.Parse(src, compileDiag);
            if (compileDiag.HasErrors)
            {
                return null;
            }

            Desugarer desugarer = new(compileDiag);
            Chapter chapter = desugarer.Desugar(document, chapterName);
            if (compileDiag.HasErrors)
            {
                return null;
            }

            // Transitive imports resolve against the same codex root + quire layout
            // and share the in-flight set (+ its ordered parallel list) so a
            // cycle trips CDX9001 rather than recursing forever.
            FileChapterLoader transitiveLoader = new(
                m_baseDirectory, compileDiag, m_virtualQuireName,
                m_inFlight, m_inFlightOrder);
            NameResolver resolver = new(compileDiag, transitiveLoader);
            ResolvedChapter resolved = resolver.Resolve(chapter);
            if (compileDiag.HasErrors)
            {
                // Forward circular-import (CDX9001) diagnostics from the nested
                // load so the outer caller sees them.
                foreach (Diagnostic d in compileDiag.ToImmutable())
                {
                    if (d.Code == CdxCodes.ResourceExhausted)
                    {
                        m_diagnostics.Add(d);
                    }
                }
                return null;
            }

            m_cache = m_cache.Set(key, resolved);
            return resolved;
        }
        finally
        {
            m_inFlight.Remove(key);
            m_inFlightOrder.Remove(key);
        }
    }

    static string? FindFileForChapter(string quireDir, string chapterName)
    {
        foreach (string file in Directory.GetFiles(quireDir, "*.codex"))
        {
            string? firstLine = null;
            using (StreamReader r = new(file))
                firstLine = r.ReadLine();
            if (firstLine is null) continue;
            if (!firstLine.StartsWith("Chapter:", StringComparison.Ordinal)) continue;
            string title = firstLine["Chapter:".Length..].Trim();
            if (title == chapterName)
                return file;
        }
        return null;
    }
}
