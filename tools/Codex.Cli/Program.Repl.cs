using Codex.Core;
using Codex.Syntax;
using Codex.Ast;
using Codex.Semantics;
using Codex.Types;
using Codex.IR;
using Codex.Emit.CSharp;

namespace Codex.Cli;

public static partial class Program
{
    static int RunRepl(string[] args)
    {
        Console.WriteLine("Codex REPL 0.1.0-bootstrap");
        Console.WriteLine("Type :help for commands, :quit to exit.");
        Console.WriteLine();

        ReplState state = new();

        while (true)
        {
            Console.Write("codex> ");
            string? line = Console.ReadLine();
            if (line is null)
            {
                break;
            }

            line = line.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith(':'))
            {
                if (HandleMetaCommand(line, state))
                {
                    continue;
                }
                else
                {
                    break;
                }
            }

            EvaluateLine(line, state);
        }

        state.Cleanup();
        return 0;
    }

    // Returns true to continue the loop, false to exit.
    static bool HandleMetaCommand(string line, ReplState state)
    {
        string[] parts = line.Split(' ', 2, StringSplitOptions.TrimEntries);
        string command = parts[0].ToLowerInvariant();

        switch (command)
        {
            case ":quit" or ":q":
                state.Cleanup();
                return false;

            case ":help" or ":h":
                PrintReplHelp();
                return true;

            case ":type" or ":t":
                if (parts.Length < 2)
                {
                    Console.Error.WriteLine("Usage: :type <expression>");
                }
                else
                {
                    ShowType(parts[1], state);
                }

                return true;

            case ":reset":
                state.Reset();
                Console.WriteLine("Session reset.");
                return true;

            case ":defs":
                if (state.Definitions.Count == 0 && state.TypeDefinitions.Count == 0)
                {
                    Console.WriteLine("(no definitions)");
                }
                else
                {
                    foreach (string def in state.TypeDefinitions)
                    {
                        Console.WriteLine(def);
                    }

                    foreach (string def in state.Definitions)
                    {
                        Console.WriteLine(def);
                    }
                }
                return true;

            default:
                Console.Error.WriteLine($"Unknown command: {command}");
                Console.Error.WriteLine("Type :help for available commands.");
                return true;
        }
    }

    static void PrintReplHelp()
    {
        Console.WriteLine("Commands:");
        Console.WriteLine("  :help, :h          Show this help");
        Console.WriteLine("  :quit, :q          Exit the REPL");
        Console.WriteLine("  :type, :t <expr>   Show the type of an expression");
        Console.WriteLine("  :reset             Clear all definitions");
        Console.WriteLine("  :defs              List current definitions");
        Console.WriteLine();
        Console.WriteLine("Enter a definition (name : Type / name (args) = body)");
        Console.WriteLine("or an expression to evaluate it.");
    }

    static void EvaluateLine(string line, ReplState state)
    {
        bool isDefinition = LooksLikeDefinition(line);

        string source;
        if (isDefinition)
        {
            // Temporarily add this definition and compile the whole session
            source = state.BuildSource(line, null);
        }
        else
        {
            // Wrap expression as: __repl_it = <expr>
            source = state.BuildSource(null, line);
        }

        // Parse and type-check
        SourceText sourceText = new("<repl>", source);
        DiagnosticBag diagnostics = new();

        DocumentNode document = ParseSourceFile(sourceText, source, diagnostics);
        Desugarer desugarer = new(diagnostics);
        Chapter chapter = desugarer.Desugar(document, "repl");

        if (diagnostics.HasErrors)
        {
            PrintReplDiagnostics(diagnostics);
            return;
        }

        NameResolver resolver = new(diagnostics);
        ResolvedChapter resolved = resolver.Resolve(chapter);

        if (diagnostics.HasErrors)
        {
            PrintReplDiagnostics(diagnostics);
            return;
        }

        TypeChecker checker = new(diagnostics);
        Map<string, CodexType> types = checker.CheckChapter(resolved.Chapter);

        if (diagnostics.HasErrors)
        {
            PrintReplDiagnostics(diagnostics);
            return;
        }

        if (isDefinition)
        {
            // Success — persist the definition
            state.AddDefinition(line);
            string defName = ExtractDefinitionName(line);
            CodexType? defType = types[defName];
            if (defType is not null)
            {
                Console.WriteLine($"{defName} : {defType}");
            }
            else
            {
                Console.WriteLine($"Defined {defName}");
            }

            return;
        }

        // For expressions, also lower and emit to get the result
        LinearityChecker linearityChecker = new(diagnostics, types);
        linearityChecker.CheckChapter(resolved.Chapter);

        if (diagnostics.HasErrors)
        {
            PrintReplDiagnostics(diagnostics);
            return;
        }

        Lowering lowering = new(types, checker.ConstructorMap, checker.TypeDefMap, diagnostics);
        IRChapter irModule = lowering.Lower(resolved.Chapter);

        if (diagnostics.HasErrors)
        {
            PrintReplDiagnostics(diagnostics);
            return;
        }

        CSharpEmitter emitter = new();
        string csharpSource = emitter.Emit(irModule);

        string? output = RunCSharpSource(csharpSource, state);
        if (output is not null)
        {
            output = output.TrimEnd();
            if (output.Length > 0)
            {
                Console.WriteLine(output);
            }
        }
    }

    static void ShowType(string exprText, ReplState state)
    {
        string source = state.BuildSource(null, exprText);

        SourceText sourceText = new("<repl>", source);
        DiagnosticBag diagnostics = new();

        DocumentNode document = ParseSourceFile(sourceText, source, diagnostics);
        Desugarer desugarer = new(diagnostics);
        Chapter chapter = desugarer.Desugar(document, "repl");

        if (diagnostics.HasErrors)
        {
            PrintReplDiagnostics(diagnostics);
            return;
        }

        NameResolver resolver = new(diagnostics);
        ResolvedChapter resolved = resolver.Resolve(chapter);

        if (diagnostics.HasErrors)
        {
            PrintReplDiagnostics(diagnostics);
            return;
        }

        TypeChecker checker = new(diagnostics);
        Map<string, CodexType> types = checker.CheckChapter(resolved.Chapter);

        if (diagnostics.HasErrors)
        {
            PrintReplDiagnostics(diagnostics);
            return;
        }

        CodexType? itType = types["__repl_it"];
        if (itType is not null)
        {
            Console.WriteLine($"{exprText} : {itType}");
        }
        else
        {
            Console.Error.WriteLine("Could not determine type.");
        }
    }

    static bool LooksLikeDefinition(string line)
    {
        // A definition has a name followed by : (type annotation) or = (body)
        // or name (params) = body
        // An expression does not start with these patterns
        int i = 0;
        // skip identifier (lowercase-hyphenated)
        while (i < line.Length && (char.IsLetterOrDigit(line[i]) || line[i] == '-' || line[i] == '_'))
        {
            i++;
        }

        if (i == 0)
        {
            return false;
        }
        // skip whitespace
        while (i < line.Length && line[i] == ' ')
        {
            i++;
        }

        if (i >= line.Length)
        {
            return false;
        }
        // definition if next is : or = or (
        return line[i] is ':' or '=' or '(';
    }

    static string ExtractDefinitionName(string line)
    {
        int i = 0;
        while (i < line.Length && (char.IsLetterOrDigit(line[i]) || line[i] == '-' || line[i] == '_'))
        {
            i++;
        }

        return line[..i];
    }

    static string? RunCSharpSource(string csharpSource, ReplState state)
    {
        state.EnsureTempDir();

        string csFile = Path.Combine(state.TempDir!, "Program.cs");
        File.WriteAllText(csFile, csharpSource);

        string csproj = Path.Combine(state.TempDir!, "CodexOutput.csproj");
        if (!File.Exists(csproj))
        {
            File.WriteAllText(csproj, GenerateCsproj());
        }

        System.Diagnostics.ProcessStartInfo buildInfo =
            new("dotnet", "build --nologo --verbosity quiet")
            {
                WorkingDirectory = state.TempDir!,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
        System.Diagnostics.Process? buildProc =
            System.Diagnostics.Process.Start(buildInfo);
        if (buildProc is null)
        {
            Console.Error.WriteLine("Failed to start dotnet build");
            return null;
        }

        string buildStderr = buildProc.StandardError.ReadToEnd();
        buildProc.WaitForExit();

        if (buildProc.ExitCode != 0)
        {
            Console.Error.WriteLine("Compilation error (internal):");
            Console.Error.WriteLine(buildStderr);
            return null;
        }

        System.Diagnostics.ProcessStartInfo runInfo =
            new("dotnet", "run --no-build --nologo")
            {
                WorkingDirectory = state.TempDir!,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
        System.Diagnostics.Process? runProc =
            System.Diagnostics.Process.Start(runInfo);
        if (runProc is null)
        {
            Console.Error.WriteLine("Failed to start dotnet run");
            return null;
        }

        string output = runProc.StandardOutput.ReadToEnd();
        string errOutput = runProc.StandardError.ReadToEnd();
        runProc.WaitForExit();

        if (errOutput.Length > 0)
        {
            Console.Error.Write(errOutput);
        }

        return output;
    }

    static void PrintReplDiagnostics(DiagnosticBag diagnostics)
    {
        foreach (Diagnostic diag in diagnostics.ToImmutable())
        {
            if (diag.Severity == DiagnosticSeverity.Error)
            {
                Console.Error.WriteLine($"  {diag.Code}: {diag.Message}");
            }
        }
    }
}

sealed class ReplState
{
    readonly List<string> m_definitions = [];
    readonly List<string> m_typeDefinitions = [];
    string? m_tempDir;

    public IReadOnlyList<string> Definitions => m_definitions;
    public IReadOnlyList<string> TypeDefinitions => m_typeDefinitions;
    public string? TempDir => m_tempDir;

    public void AddDefinition(string line)
    {
        // Type definitions start with uppercase
        if (line.Length > 0 && char.IsUpper(line[0]))
        {
            m_typeDefinitions.Add(line);
        }
        else
        {
            m_definitions.Add(line);
        }
    }

    public string BuildSource(string? newDefinition, string? expression)
    {
        System.Text.StringBuilder sb = new();

        foreach (string td in m_typeDefinitions)
        {
            sb.AppendLine(td);
            sb.AppendLine();
        }

        foreach (string def in m_definitions)
        {
            sb.AppendLine(def);
            sb.AppendLine();
        }

        if (newDefinition is not null)
        {
            sb.AppendLine(newDefinition);
            sb.AppendLine();
        }

        if (expression is not null)
        {
            // Wrap as main so the emitter will print it
            sb.AppendLine($"main = {expression}");
        }

        return sb.ToString();
    }

    public void Reset()
    {
        m_definitions.Clear();
        m_typeDefinitions.Clear();
    }

    public void EnsureTempDir()
    {
        if (m_tempDir is not null)
        {
            return;
        }

        m_tempDir = Path.Combine(
            Path.GetTempPath(),
            "codex_repl_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(m_tempDir);
    }

    public void Cleanup()
    {
        if (m_tempDir is not null)
        {
            try { Directory.Delete(m_tempDir, true); } catch { }
            m_tempDir = null;
        }
    }
}
