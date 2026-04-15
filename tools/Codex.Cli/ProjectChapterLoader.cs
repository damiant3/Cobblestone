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
    Map<string, ResolvedChapter> m_cache = Map<string, ResolvedChapter>.s_empty;

    ProjectChapterLoader(
        string projectDirectory,
        string[] sourceFiles,
        DiagnosticBag diagnostics)
    {
        m_projectDirectory = projectDirectory;
        m_sourceFiles = sourceFiles;
        m_diagnostics = diagnostics;
    }

    public ResolvedChapter? Load(string quire, string chapterName)
    {
        string key = $"{quire}::{chapterName}";
        ResolvedChapter? cached = m_cache[key];
        if (cached is not null)
            return cached;

        string? filePath = FindSourceFile(quire, chapterName);
        if (filePath is null)
            return null;

        string source = File.ReadAllText(filePath);
        SourceText src = new(filePath, source);
        DiagnosticBag compileDiag = new();

        DocumentNode document = DocumentParser.Parse(src, compileDiag);
        if (compileDiag.HasErrors)
            return null;

        Desugarer desugarer = new(compileDiag);
        Chapter chapter = desugarer.Desugar(document, chapterName);
        if (compileDiag.HasErrors)
            return null;

        FileChapterLoader transitiveLoader = new(m_projectDirectory, compileDiag);
        NameResolver resolver = new(compileDiag, transitiveLoader);
        ResolvedChapter resolved = resolver.Resolve(chapter);
        if (compileDiag.HasErrors)
            return null;

        m_cache = m_cache.Set(key, resolved);
        return resolved;
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
