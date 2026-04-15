using Codex.Ast;
using Codex.Core;
using Codex.Emit.CSharp;
using Codex.Emit.Codex;
using Codex.Emit.IL;
using Codex.Emit.Wasm;
using Codex.IR;
using Codex.Semantics;
using Codex.Syntax;

namespace Codex.Types.Tests;

public static class Helpers
{
    /// <summary>
    /// Emitted IL references Codex.Core.CceTable for CCE ↔ Unicode conversion
    /// at I/O boundaries. Copy the assembly next to the test output so
    /// `dotnet &lt;dll&gt;` can resolve it via the default probing path.
    /// </summary>
    public static void CopyIlRuntimeDeps(string tempDir)
    {
        string codexCoreDll = typeof(CceTable).Assembly.Location;
        if (!string.IsNullOrEmpty(codexCoreDll))
        {
            File.Copy(codexCoreDll, Path.Combine(tempDir, "Codex.Core.dll"), overwrite: true);
        }
    }

    public static DiagnosticBag CheckWithProofs(string source, string chapterName = "test")
    {
        SourceText src = new("test.codex", source);
        DiagnosticBag diagnostics = new();

        Lexer lexer = new(src, diagnostics);
        IReadOnlyList<Token> tokens = lexer.TokenizeAll();
        Parser parser = new(tokens, diagnostics);
        DocumentNode document = parser.ParseDocument();

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
        Map<string, CodexType> types = checker.CheckChapter(resolved.Chapter);
        if (diagnostics.HasErrors)
        {
            return diagnostics;
        }

        Codex.Proofs.ProofChecker proofChecker = new(diagnostics);
        proofChecker.CheckChapter(resolved.Chapter, types);
        return diagnostics;
    }

    public static DiagnosticBag CheckWithLinearity(string source, string chapterName = "test")
    {
        SourceText src = new("test.codex", source);
        DiagnosticBag diagnostics = new();

        Lexer lexer = new(src, diagnostics);
        IReadOnlyList<Token> tokens = lexer.TokenizeAll();
        Parser parser = new(tokens, diagnostics);
        DocumentNode document = parser.ParseDocument();

        Desugarer desugarer = new(diagnostics);
        Chapter chapter = desugarer.Desugar(document, chapterName);

        NameResolver resolver = new(diagnostics);
        ResolvedChapter resolved = resolver.Resolve(chapter);

        TypeChecker checker = new(diagnostics);
        Map<string, CodexType> types = checker.CheckChapter(resolved.Chapter);

        LinearityChecker linearityChecker = new(diagnostics, types);
        linearityChecker.CheckChapter(resolved.Chapter);

        return diagnostics;
    }

    public static DiagnosticBag TypeCheckWithDiagnostics(string source, string chapterName = "test")
    {
        SourceText src = new("test.codex", source);
        DiagnosticBag diagnostics = new();

        Lexer lexer = new(src, diagnostics);
        IReadOnlyList<Token> tokens = lexer.TokenizeAll();
        Parser parser = new(tokens, diagnostics);
        DocumentNode document = parser.ParseDocument();

        Desugarer desugarer = new(diagnostics);
        Chapter chapter = desugarer.Desugar(document, chapterName);

        NameResolver resolver = new(diagnostics);
        ResolvedChapter resolved = resolver.Resolve(chapter);

        TypeChecker checker = new(diagnostics);
        checker.CheckChapter(resolved.Chapter);
        return diagnostics;
    }


    public static string? CompileToCodex(string source, string chapterName = "test")
    {
        return CompileToTarget(source, chapterName, new CodexEmitter());
    }

    public static string? CompileToCS(string source, string chapterName = "test")
    {
        return CompileToTarget(source, chapterName, new CSharpEmitter());
    }

#if LEGACY_EMITTERS
    public static string? CompileToJS(string source, string chapterName = "test")
    {
        return CompileToTarget(source, chapterName, new Codex.Emit.JavaScript.JavaScriptEmitter());
    }

    public static string? CompileToPython(string source, string chapterName = "test")
    {
        return CompileToTarget(source, chapterName, new Codex.Emit.Python.PythonEmitter());
    }

    public static string? CompileToRust(string source, string chapterName = "test")
    {
        return CompileToTarget(source, chapterName, new Codex.Emit.Rust.RustEmitter());
    }

    public static string? CompileToCpp(string source, string chapterName = "test")
    {
        return CompileToTarget(source, chapterName, new Codex.Emit.Cpp.CppEmitter());
    }

    public static string? CompileToGo(string source, string chapterName = "test")
    {
        return CompileToTarget(source, chapterName, new Codex.Emit.Go.GoEmitter());
    }

    public static string? CompileToJava(string source, string chapterName = "test")
    {
        return CompileToTarget(source, chapterName, new Codex.Emit.Java.JavaEmitter());
    }

    public static string? CompileToAda(string source, string chapterName = "test")
    {
        return CompileToTarget(source, chapterName, new Codex.Emit.Ada.AdaEmitter());
    }

    public static string? CompileToBabbage(string source, string chapterName = "test")
    {
        return CompileToTarget(source, chapterName, new Codex.Emit.Babbage.BabbageEmitter());
    }

    public static string? CompileToFortran(string source, string chapterName = "test")
    {
        return CompileToTarget(source, chapterName, new Codex.Emit.Fortran.FortranEmitter());
    }

    public static string? CompileToCobol(string source, string chapterName = "test")
    {
        return CompileToTarget(source, chapterName, new Codex.Emit.Cobol.CobolEmitter());
    }
#else
    public static string? CompileToJS(string source, string chapterName = "test") => null;
    public static string? CompileToPython(string source, string chapterName = "test") => null;
    public static string? CompileToRust(string source, string chapterName = "test") => null;
    public static string? CompileToCpp(string source, string chapterName = "test") => null;
    public static string? CompileToGo(string source, string chapterName = "test") => null;
    public static string? CompileToJava(string source, string chapterName = "test") => null;
    public static string? CompileToAda(string source, string chapterName = "test") => null;
    public static string? CompileToBabbage(string source, string chapterName = "test") => null;
    public static string? CompileToFortran(string source, string chapterName = "test") => null;
    public static string? CompileToCobol(string source, string chapterName = "test") => null;
#endif

    public static IRChapter? CompileToIR(string source, string chapterName = "test")
    {
        SourceText src = new("test.codex", source);
        DiagnosticBag diagnostics = new();

        DocumentNode document;
        if (ProseParser.IsProseDocument(source))
        {
            ProseParser proseParser = new(src, diagnostics);
            document = proseParser.ParseDocument();
        }
        else
        {
            Lexer lexer = new(src, diagnostics);
            IReadOnlyList<Token> tokens = lexer.TokenizeAll();
            Parser parser = new(tokens, diagnostics);
            document = parser.ParseDocument();
        }

        Desugarer desugarer = new(diagnostics);
        Chapter chapter = desugarer.Desugar(document, chapterName);
        if (diagnostics.HasErrors)
        {
            return null;
        }

        NameResolver resolver = new(diagnostics);
        ResolvedChapter resolved = resolver.Resolve(chapter);
        if (diagnostics.HasErrors)
        {
            return null;
        }

        TypeChecker checker = new(diagnostics);
        Map<string, CodexType> types = checker.CheckChapter(resolved.Chapter);
        if (diagnostics.HasErrors)
        {
            return null;
        }

        LinearityChecker linearityChecker = new(diagnostics, types);
        linearityChecker.CheckChapter(resolved.Chapter);
        if (diagnostics.HasErrors)
        {
            return null;
        }

        Lowering lowering = new(types, checker.ConstructorMap, checker.TypeDefMap, diagnostics);
        IRChapter irModule = lowering.Lower(resolved.Chapter);
        if (diagnostics.HasErrors)
        {
            return null;
        }

        return irModule;
    }

    public static byte[]? CompileToIL(string source, string chapterName = "test")
    {
        IRChapter? irModule = CompileToIR(source, chapterName);
        if (irModule is null)
        {
            return null;
        }

        ILEmitter emitter = new();
        return emitter.EmitAssembly(irModule, chapterName);
    }

    public static byte[]? CompileToRiscV(string source, string chapterName = "test")
    {
        return CompileToRiscVTarget(source, chapterName, Codex.Emit.RiscV.RiscVTarget.LinuxUser);
    }

    public static byte[]? CompileToRiscVBareMetal(string source, string chapterName = "test")
    {
        return CompileToRiscVTarget(source, chapterName, Codex.Emit.RiscV.RiscVTarget.BareMetal);
    }

    static byte[]? CompileToRiscVTarget(string source, string chapterName, Codex.Emit.RiscV.RiscVTarget target)
    {
        IRChapter? irModule = CompileToIR(source, chapterName);
        if (irModule is null)
        {
            return null;
        }

        Codex.Emit.RiscV.RiscVEmitter riscvEmitter = new(target);
        return riscvEmitter.EmitAssembly(irModule, chapterName);
    }

    public static byte[]? CompileToWasm(string source, string chapterName = "test")
    {
        IRChapter? irModule = CompileToIR(source, chapterName);
        if (irModule is null)
        {
            return null;
        }

        WasmEmitter emitter = new();
        return emitter.EmitAssembly(irModule, chapterName);
    }

    public static string? CompileToTarget(string source, string chapterName, Codex.Emit.ICodeEmitter emitter)
    {
        IRChapter? irModule = CompileToIR(source, chapterName);
        if (irModule is null)
        {
            return null;
        }

        return emitter.Emit(irModule);
    }


    public static Map<string, CodexType>? TypeCheck(
        string source, string chapterName = "test")
    {
        SourceText src = new("test.codex", source);
        DiagnosticBag diagnostics = new();

        DocumentNode document;
        if (ProseParser.IsProseDocument(source))
        {
            ProseParser proseParser = new(src, diagnostics);
            document = proseParser.ParseDocument();
        }
        else
        {
            Lexer lexer = new(src, diagnostics);
            IReadOnlyList<Token> tokens = lexer.TokenizeAll();
            Parser parser = new(tokens, diagnostics);
            document = parser.ParseDocument();
        }

        Desugarer desugarer = new(diagnostics);
        Chapter chapter = desugarer.Desugar(document, chapterName);
        if (diagnostics.HasErrors)
        {
            return null;
        }

        NameResolver resolver = new(diagnostics);
        ResolvedChapter resolved = resolver.Resolve(chapter);
        if (diagnostics.HasErrors)
        {
            return null;
        }

        TypeChecker checker = new(diagnostics);
        Map<string, CodexType> types = checker.CheckChapter(resolved.Chapter);
        return diagnostics.HasErrors ? null : types;
    }

    public static byte[]? CompileToX86_64(string source, string chapterName = "test")
    {
        IRChapter? irModule = CompileToIR(source, chapterName);
        if (irModule is null)
        {
            return null;
        }

        Codex.Emit.X86_64.X86_64Emitter emitter = new();
        return emitter.EmitAssembly(irModule, chapterName);
    }

    public static byte[]? CompileToX86_64BareMetal(string source, string chapterName = "test")
    {
        IRChapter? irModule = CompileToIR(source, chapterName);
        if (irModule is null)
        {
            return null;
        }

        Codex.Emit.X86_64.X86_64Emitter emitter = new(Codex.Emit.X86_64.X86_64Target.BareMetal);
        return emitter.EmitAssembly(irModule, chapterName);
    }

    public static byte[]? CompileToArm64(string source, string chapterName = "test")
    {
        IRChapter? irModule = CompileToIR(source, chapterName);
        if (irModule is null)
        {
            return null;
        }

        Codex.Emit.Arm64.Arm64Emitter emitter = new();
        return emitter.EmitAssembly(irModule, chapterName);
    }
}
