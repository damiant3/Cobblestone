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

    public ResolvedChapter? Load(string chapterName)
    {
        ResolvedChapter? cached = m_cache[chapterName];
        if (cached is not null)
            return cached;

        string? filePath = FindSourceFile(chapterName);
        if (filePath is null)
            return null;

        string source = File.ReadAllText(filePath);
        SourceText src = new(filePath, source);
        DiagnosticBag compileDiag = new();

        DocumentNode document;
        if (ProseParser.IsProseDocument(source))
        {
            ProseParser proseParser = new(src, compileDiag);
            document = proseParser.ParseDocument();
        }
        else
        {
            Lexer lexer = new(src, compileDiag);
            IReadOnlyList<Token> tokens = lexer.TokenizeAll();
            Parser parser = new(tokens, compileDiag);
            document = parser.ParseDocument();
        }

        if (compileDiag.HasErrors)
            return null;

        Desugarer desugarer = new(compileDiag);
        Chapter chapter = desugarer.Desugar(document, chapterName);
        if (compileDiag.HasErrors)
            return null;

        FileChapterLoader transitiveLoader = new(
            Path.GetDirectoryName(filePath) ?? m_projectDirectory, compileDiag);
        NameResolver resolver = new(compileDiag, transitiveLoader);
        ResolvedChapter resolved = resolver.Resolve(chapter);
        if (compileDiag.HasErrors)
            return null;

        m_cache = m_cache.Set(chapterName, resolved);
        return resolved;
    }

    string? FindSourceFile(string chapterName)
    {
        string target = chapterName + ".codex";
        string targetLower = chapterName.ToLowerInvariant() + ".codex";

        foreach (string file in m_sourceFiles)
        {
            string fileName = Path.GetFileName(file);
            if (string.Equals(fileName, target, StringComparison.OrdinalIgnoreCase)
                || string.Equals(fileName, targetLower, StringComparison.OrdinalIgnoreCase))
            {
                return file;
            }
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
