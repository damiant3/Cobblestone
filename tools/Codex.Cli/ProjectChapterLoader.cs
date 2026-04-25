using Codex.Ast;
using Codex.Core;
using Codex.Semantics;
using Codex.Syntax;

namespace Codex.Cli;

sealed class ProjectChapterLoader : IChapterLoader
{
    readonly string m_projectDirectory;
    readonly DiagnosticBag m_diagnostics;
    readonly string[] m_sourceFiles;
    // Same cycle-detection pattern as FileChapterLoader (C/9): set for O(1)
    // reentry check, parallel list for a stable "A -> B -> A" chain in the
    // diagnostic. Shared with the spawned FileChapterLoader so a chapter
    // cited back from a transitively-loaded file still trips CDX9001.
    readonly HashSet<string> m_inFlight;
    readonly List<string> m_inFlightOrder;
    Map<string, ResolvedChapter> m_cache = Map<string, ResolvedChapter>.s_empty;

    ProjectChapterLoader(
        string projectDirectory,
        string[] sourceFiles,
        DiagnosticBag diagnostics,
        HashSet<string>? inFlight = null,
        List<string>? inFlightOrder = null)
    {
        m_projectDirectory = projectDirectory;
        m_sourceFiles = sourceFiles;
        m_diagnostics = diagnostics;
        m_inFlight = inFlight ?? new HashSet<string>();
        m_inFlightOrder = inFlightOrder ?? new List<string>();
    }

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
            string cycle = string.Join(" -> ", m_inFlightOrder.Append(key));
            m_diagnostics.Error(CdxCodes.ResourceExhausted,
                $"compiler resource exhausted in chapter-loader: circular import detected ({cycle})",
                SourceSpan.Single(0, 1, 1, key));
            return null;
        }
        m_inFlightOrder.Add(key);

        try
        {
            string? filePath = FindSourceFile(quire, chapterName);
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

            // Share the in-flight set/list with the spawned loader so cycles
            // spanning project + file loads trip CDX9001 at the first re-entry.
            FileChapterLoader transitiveLoader = new(
                m_projectDirectory, compileDiag, null,
                m_inFlight, m_inFlightOrder);
            NameResolver resolver = new(compileDiag, transitiveLoader);
            ResolvedChapter resolved = resolver.Resolve(chapter);
            if (compileDiag.HasErrors)
            {
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

    string? FindSourceFile(string quire, string chapterName)
    {
        foreach (string file in m_sourceFiles)
        {
            // Match by (containing directory basename == quire) AND chapter title.
            string? dir = Path.GetDirectoryName(file);
            string quireOfFile = dir is null ? "" : Path.GetFileName(dir);
            if (quireOfFile != quire) continue;

            string? firstLine = null;
            using (StreamReader r = new(file))
                firstLine = r.ReadLine();
            if (firstLine is null) continue;
            if (!firstLine.StartsWith("Chapter:", StringComparison.Ordinal)) continue;
            string title = firstLine["Chapter:".Length..].Trim();
            if (title == chapterName) return file;
        }
        return null;
    }

    public static ProjectChapterLoader? TryCreate(
        string dependencyPath,
        string relativeTo,
        DiagnosticBag diagnostics)
    {
        string fullPath = Path.GetFullPath(Path.Combine(relativeTo, dependencyPath));
        if (!Directory.Exists(fullPath))
            return null;

        string projectFile = Path.Combine(fullPath, "codex.project.json");
        if (!File.Exists(projectFile))
            return null;

        Program.CodexProject? project = Program.LoadProjectFile(fullPath);
        if (project is null)
            return null;

        string[] sources = Program.ResolveProjectSources(fullPath, project);
        if (sources.Length == 0)
            return null;

        return new ProjectChapterLoader(fullPath, sources, diagnostics);
    }
}
