using Codex.Ast;
using Codex.Core;
using Codex.Semantics;
using Codex.Syntax;
using Codex.Types;
using Xunit;

namespace Codex.Types.Tests;

public class ForewordTests
{
    static string FindForewordDir()
    {
        string dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            string candidate = Path.Combine(dir, "foreword");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = Path.GetDirectoryName(dir)!;
        }
        throw new DirectoryNotFoundException("Cannot find foreword/ directory");
    }

    static DiagnosticBag CompileForewordFile(string fileName)
    {
        string path = Path.Combine(FindForewordDir(), fileName);
        string source = File.ReadAllText(path);
        string chapterName = Path.GetFileNameWithoutExtension(fileName);

        SourceText src = new(path, source);
        DiagnosticBag diagnostics = new();

        DocumentNode document = DocumentParser.Parse(src, diagnostics);
        if (diagnostics.HasErrors)
        {
            return diagnostics;
        }

        Desugarer desugarer = new(diagnostics);
        Chapter chapter = desugarer.Desugar(document, chapterName);
        if (diagnostics.HasErrors)
        {
            return diagnostics;
        }

        NameResolver resolver = new(diagnostics);
        ResolvedChapter resolved = resolver.Resolve(chapter);
        if (diagnostics.HasErrors)
        {
            return diagnostics;
        }

        TypeChecker checker = new(diagnostics);
        checker.CheckChapter(resolved.Chapter);
        return diagnostics;
    }

    [Fact]
    public void Maybe_compiles()
    {
        DiagnosticBag diag = CompileForewordFile("Maybe.codex");
        Assert.False(diag.HasErrors, string.Join("; ", diag.ToImmutable()));
    }

    [Fact]
    public void Result_compiles()
    {
        DiagnosticBag diag = CompileForewordFile("Result.codex");
        Assert.False(diag.HasErrors, string.Join("; ", diag.ToImmutable()));
    }

    [Fact]
    public void Either_compiles()
    {
        DiagnosticBag diag = CompileForewordFile("Either.codex");
        Assert.False(diag.HasErrors, string.Join("; ", diag.ToImmutable()));
    }

    [Fact]
    public void Pair_compiles()
    {
        DiagnosticBag diag = CompileForewordFile("Pair.codex");
        Assert.False(diag.HasErrors, string.Join("; ", diag.ToImmutable()));
    }

    [Fact]
    public void CCE_compiles()
    {
        DiagnosticBag diag = CompileForewordFile("CCE.codex");
        Assert.False(diag.HasErrors, string.Join("; ", diag.ToImmutable()));
    }

    [Fact(Skip = "Hamt uses self-hosted-only syntax (record, lambdas, list literals)")]
    public void Hamt_compiles()
    {
        DiagnosticBag diag = CompileForewordFileWithLoader("Hamt.codex");
        Assert.False(diag.HasErrors, string.Join("; ", diag.ToImmutable()));
    }

    [Fact]
    public void List_compiles()
    {
        DiagnosticBag diag = CompileForewordFile("List.codex");
        Assert.False(diag.HasErrors, string.Join("; ", diag.ToImmutable()));
    }

    [Fact]
    public void Console_effect_compiles()
    {
        DiagnosticBag diag = CompileForewordFile("Console.codex");
        Assert.False(diag.HasErrors, string.Join("; ", diag.ToImmutable()));
    }

    [Fact]
    public void FileSystem_effect_compiles()
    {
        DiagnosticBag diag = CompileForewordFile("FileSystem.codex");
        Assert.False(diag.HasErrors, string.Join("; ", diag.ToImmutable()));
    }

    [Fact]
    public void Time_effect_compiles()
    {
        DiagnosticBag diag = CompileForewordFile("Time.codex");
        Assert.False(diag.HasErrors, string.Join("; ", diag.ToImmutable()));
    }

    [Fact]
    public void Random_effect_compiles()
    {
        DiagnosticBag diag = CompileForewordFile("Random.codex");
        Assert.False(diag.HasErrors, string.Join("; ", diag.ToImmutable()));
    }

    [Fact]
    public void State_effect_compiles()
    {
        DiagnosticBag diag = CompileForewordFile("State.codex");
        Assert.False(diag.HasErrors, string.Join("; ", diag.ToImmutable()));
    }

    [Fact]
    public void Network_effect_compiles()
    {
        DiagnosticBag diag = CompileForewordFile("Network.codex");
        Assert.False(diag.HasErrors, string.Join("; ", diag.ToImmutable()));
    }

    [Fact]
    public void Display_effect_compiles()
    {
        DiagnosticBag diag = CompileForewordFile("Display.codex");
        Assert.False(diag.HasErrors, string.Join("; ", diag.ToImmutable()));
    }

    [Fact]
    public void Camera_effect_compiles()
    {
        DiagnosticBag diag = CompileForewordFile("Camera.codex");
        Assert.False(diag.HasErrors, string.Join("; ", diag.ToImmutable()));
    }

    [Fact]
    public void Microphone_effect_compiles()
    {
        DiagnosticBag diag = CompileForewordFile("Microphone.codex");
        Assert.False(diag.HasErrors, string.Join("; ", diag.ToImmutable()));
    }

    [Fact]
    public void Location_effect_compiles()
    {
        DiagnosticBag diag = CompileForewordFile("Location.codex");
        Assert.False(diag.HasErrors, string.Join("; ", diag.ToImmutable()));
    }

    [Fact]
    public void Sensors_effect_compiles()
    {
        DiagnosticBag diag = CompileForewordFile("Sensors.codex");
        Assert.False(diag.HasErrors, string.Join("; ", diag.ToImmutable()));
    }

    [Fact]
    public void Identity_effect_compiles()
    {
        DiagnosticBag diag = CompileForewordFile("Identity.codex");
        Assert.False(diag.HasErrors, string.Join("; ", diag.ToImmutable()));
    }

    [Fact]
    public void Maybe_used_in_program()
    {
        string source = """
            cites Foreword chapter Maybe

            safe-div : Integer -> Integer -> Maybe Integer
            safe-div (a) (b) = if b == 0 then None else Just (a / b)

            main : Integer
            main = from-maybe (safe-div 10 2) 0
            """;
        DiagnosticBag diag = CompileWithPrelude(source);
        Assert.False(diag.HasErrors, string.Join("; ", diag.ToImmutable()));
    }

    [Fact]
    public void Result_used_in_program()
    {
        string source = """
            cites Foreword chapter Result

            parse-nat : Text -> Result Integer Text
            parse-nat (t) = Ok (text-to-integer t)

            main : Integer
            main = from-ok (parse-nat "42") 0
            """;
        DiagnosticBag diag = CompileWithPrelude(source);
        Assert.False(diag.HasErrors, string.Join("; ", diag.ToImmutable()));
    }

    [Fact]
    public void Either_used_in_program()
    {
        string source = """
            cites Foreword chapter Either

            to-zero : Text -> Integer
            to-zero (s) = 0

            identity : Integer -> Integer
            identity (n) = n

            classify : Integer -> Either Text Integer
            classify (n) = if n > 0 then Right n else Left "negative"

            main : Integer
            main = either to-zero identity (classify 5)
            """;
        DiagnosticBag diag = CompileWithPrelude(source);
        Assert.False(diag.HasErrors, string.Join("; ", diag.ToImmutable()));
    }

    [Fact]
    public void Pair_used_in_program()
    {
        string source = """
            cites Foreword chapter Pair

            main : Integer
            main = pair-fst (make-pair 1 2)
            """;
        DiagnosticBag diag = CompileWithPrelude(source);
        Assert.False(diag.HasErrors, string.Join("; ", diag.ToImmutable()));
    }

    [Fact]
    public void CCE_used_in_program()
    {
        string source = """
            cites Foreword chapter CCE

            main : Boolean
            main = is-digit 10
            """;
        DiagnosticBag diag = CompileWithPrelude(source);
        Assert.False(diag.HasErrors, string.Join("; ", diag.ToImmutable()));
    }

    [Fact(Skip = "Hamt uses self-hosted-only syntax (record, lambdas, list literals)")]
    public void Hamt_used_in_program()
    {
        string source = """
            cites Foreword chapter Maybe
            cites Foreword chapter Hamt

            main : Integer
            main = hamt-size (hamt-set hamt-empty "key" 42)
            """;
        DiagnosticBag diag = CompileWithPrelude(source);
        Assert.False(diag.HasErrors, string.Join("; ", diag.ToImmutable()));
    }

    [Fact]
    public void List_used_in_program()
    {
        string source = """
            cites Foreword chapter List

            main : Integer
            main = list-length (cons 1 (cons 2 (cons 3 nil)))
            """;
        DiagnosticBag diag = CompileWithPrelude(source);
        Assert.False(diag.HasErrors, string.Join("; ", diag.ToImmutable()));
    }

    [Fact]
    public void Console_effect_used_in_program()
    {
        string source = """
            main : [Console] Nothing
            main = act
              print-line "hello"
              name <- read-line
              print-line name
            end
            """;
        DiagnosticBag diag = Helpers.TypeCheckWithDiagnostics(source);
        Assert.False(diag.HasErrors, string.Join("; ", diag.ToImmutable()));
    }

    [Fact]
    public void FileSystem_effect_used_in_program()
    {
        string source = """
            main : [FileSystem] Text
            main = read-file "test.txt"
            """;
        DiagnosticBag diag = Helpers.TypeCheckWithDiagnostics(source);
        Assert.False(diag.HasErrors, string.Join("; ", diag.ToImmutable()));
    }

    [Fact]
    public void Builtin_effects_available_without_cites()
    {
        string source = """
            greet : Text -> [Console] Nothing
            greet (name) = print-line ("Hello, " ++ name ++ "!")

            main : [Console] Nothing
            main = greet "world"
            """;
        DiagnosticBag diag = Helpers.TypeCheckWithDiagnostics(source);
        Assert.False(diag.HasErrors, string.Join("; ", diag.ToImmutable()));
    }

    static DiagnosticBag CompileForewordFileWithLoader(string fileName)
    {
        string forewordDir = FindForewordDir();
        string path = Path.Combine(forewordDir, fileName);
        string source = File.ReadAllText(path);
        string chapterName = Path.GetFileNameWithoutExtension(fileName);

        SourceText src = new(path, source);
        DiagnosticBag diagnostics = new();

        DocumentNode document = DocumentParser.Parse(src, diagnostics);
        if (diagnostics.HasErrors)
        {
            return diagnostics;
        }

        Desugarer desugarer = new(diagnostics);
        Chapter chapter = desugarer.Desugar(document, chapterName);
        if (diagnostics.HasErrors)
        {
            return diagnostics;
        }

        ForewordTestLoader forewordLoader = new(forewordDir, diagnostics);
        NameResolver resolver = new(diagnostics, forewordLoader);
        ResolvedChapter resolved = resolver.Resolve(chapter);
        if (diagnostics.HasErrors)
        {
            return diagnostics;
        }

        TypeChecker checker = new(diagnostics);

        foreach (ResolvedChapter imported in resolved.CitedChapters)
        {
            checker.CheckChapter(imported.Chapter);
        }

        checker.CheckChapter(resolved.Chapter);
        return diagnostics;
    }

    static DiagnosticBag CompileWithPrelude(string source)
    {
        string forewordDir = FindForewordDir();
        SourceText src = new("test.codex", source);
        DiagnosticBag diagnostics = new();

        DocumentNode document = DocumentParser.Parse(src, diagnostics);
        if (diagnostics.HasErrors)
        {
            return diagnostics;
        }

        Desugarer desugarer = new(diagnostics);
        Chapter chapter = desugarer.Desugar(document, "test");
        if (diagnostics.HasErrors)
        {
            return diagnostics;
        }

        ForewordTestLoader forewordLoader = new(forewordDir, diagnostics);
        NameResolver resolver = new(diagnostics, forewordLoader);
        ResolvedChapter resolved = resolver.Resolve(chapter);
        if (diagnostics.HasErrors)
        {
            return diagnostics;
        }

        TypeChecker checker = new(diagnostics);

        foreach (ResolvedChapter imported in resolved.CitedChapters)
        {
            checker.CheckChapter(imported.Chapter);
        }

        checker.CheckChapter(resolved.Chapter);
        return diagnostics;
    }
}

sealed class ForewordTestLoader(string forewordDir, DiagnosticBag diagnostics) : IChapterLoader
{
    const string QuireName = "Foreword";
    readonly string m_forewordDir = forewordDir;
    readonly DiagnosticBag m_diagnostics = diagnostics;
    Map<string, ResolvedChapter> m_cache = Map<string, ResolvedChapter>.s_empty;

    public ResolvedChapter? Load(string quire, string chapterName)
    {
        if (quire != QuireName)
        {
            return null;
        }

        ResolvedChapter? cached = m_cache[chapterName];
        if (cached is not null)
        {
            return cached;
        }

        // Scan forward files for a matching Chapter: header.
        string? filePath = null;
        foreach (string candidate in Directory.GetFiles(m_forewordDir, "*.codex"))
        {
            string? firstLine;
            using (StreamReader r = new(candidate))
            {
                firstLine = r.ReadLine();
            }

            if (firstLine is null)
            {
                continue;
            }

            if (!firstLine.StartsWith("Chapter:", StringComparison.Ordinal))
            {
                continue;
            }

            if (firstLine["Chapter:".Length..].Trim() == chapterName)
            {
                filePath = candidate;
                break;
            }
        }
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

        NameResolver resolver = new(compileDiag, this);
        ResolvedChapter resolved = resolver.Resolve(chapter);
        if (compileDiag.HasErrors)
        {
            return null;
        }

        m_cache = m_cache.Set(chapterName, resolved);
        return resolved;
    }
}
