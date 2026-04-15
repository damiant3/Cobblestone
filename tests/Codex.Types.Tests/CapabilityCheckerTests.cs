using System.Collections.Immutable;
using Codex.Core;
using Codex.Types;
using Xunit;

namespace Codex.Types.Tests;

public class CapabilityCheckerTests
{
    [Fact]
    public void Pure_function_has_no_effects()
    {
        string source = """
            square : Integer -> Integer
            square (x) = x * x

            main : Integer
            main = square 5
            """;
        CapabilityReport? report = CheckCapabilities(source);
        Assert.NotNull(report);
        Assert.False(report.MainRequiresEffects);
        Assert.Empty(report.MainEffects);
    }

    [Fact]
    public void Console_effect_detected()
    {
        string source = """
            main : [Console] Nothing
            main = print-line "hello"
            """;
        CapabilityReport? report = CheckCapabilities(source);
        Assert.NotNull(report);
        Assert.True(report.MainRequiresEffects);
        Assert.Contains("Console", report.MainEffects);
    }

    [Fact]
    public void FileSystem_effect_detected()
    {
        string source = """
            main : [FileSystem] Text
            main = read-file "test.txt"
            """;
        CapabilityReport? report = CheckCapabilities(source);
        Assert.NotNull(report);
        Assert.Contains("FileSystem", report.MainEffects);
    }

    [Fact]
    public void Multiple_effects_detected()
    {
        string source = """
            main : [Console, FileSystem] Nothing
            main = do
              contents <- read-file "input.txt"
              print-line contents
            """;
        CapabilityReport? report = CheckCapabilities(source);
        Assert.NotNull(report);
        Assert.Contains("Console", report.MainEffects);
        Assert.Contains("FileSystem", report.MainEffects);
        Assert.Equal(2, report.MainEffects.Length);
    }

    [Fact]
    public void Effect_summary_includes_all_definitions()
    {
        string source = """
            greet : Text -> [Console] Nothing
            greet (name) = print-line ("Hello, " ++ name)

            main : [Console] Nothing
            main = greet "world"
            """;
        CapabilityReport? report = CheckCapabilities(source);
        Assert.NotNull(report);
        Assert.True(report.EffectSummary.ContainsKey("greet"));
        Assert.Contains("Console", report.EffectSummary["greet"]);
    }

    [Fact]
    public void Granted_capabilities_satisfied_no_error()
    {
        string source = """
            main : [Console] Nothing
            main = print-line "hello"
            """;
        DiagnosticBag diagnostics = CheckWithGrants(source, ["Console"]);
        Assert.False(diagnostics.HasErrors);
    }

    [Fact]
    public void Missing_capability_produces_error()
    {
        string source = """
            main : [Console, FileSystem] Nothing
            main = do
              contents <- read-file "input.txt"
              print-line contents
            """;
        DiagnosticBag diagnostics = CheckWithGrants(source, ["Console"]);
        Assert.True(diagnostics.HasErrors);
        bool found = false;
        foreach (Diagnostic diag in diagnostics.ToImmutable())
        {
            if (diag.Code == CdxCodes.CapabilityNotGranted && diag.Message.Contains("FileSystem"))
            {
                found = true;
            }
        }
        Assert.True(found, "Expected CDX4001 error for missing FileSystem capability");
    }

    [Fact]
    public void Empty_grants_rejects_all_effects()
    {
        string source = """
            main : [Console] Nothing
            main = print-line "hello"
            """;
        DiagnosticBag diagnostics = CheckWithGrants(source, []);
        Assert.True(diagnostics.HasErrors);
    }

    [Fact]
    public void Required_capabilities_as_set()
    {
        string source = """
            main : [Console, FileSystem] Nothing
            main = do
              contents <- read-file "input.txt"
              print-line contents
            """;
        CapabilityReport? report = CheckCapabilities(source);
        Assert.NotNull(report);
        Set<string> required = report.RequiredCapabilities;
        Assert.True(required.Contains("Console"));
        Assert.True(required.Contains("FileSystem"));
    }

    // ── Helpers ────────────────────────────────────────────────

    static CapabilityReport? CheckCapabilities(string source)
    {
        Codex.Core.SourceText src = new("test.codex", source);
        DiagnosticBag diagnostics = new();

        Codex.Syntax.Lexer lexer = new(src, diagnostics);
        IReadOnlyList<Codex.Syntax.Token> tokens = lexer.TokenizeAll();
        Codex.Syntax.Parser parser = new(tokens, diagnostics);
        Codex.Syntax.DocumentNode document = parser.ParseDocument();

        Codex.Ast.Desugarer desugarer = new(diagnostics);
        Codex.Ast.Chapter module = desugarer.Desugar(document, "test");
        if (diagnostics.HasErrors)
        {
            return null;
        }

        Codex.Semantics.NameResolver resolver = new(diagnostics);
        Codex.Semantics.ResolvedChapter resolved = resolver.Resolve(module);
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

        CapabilityChecker capChecker = new(diagnostics, types);
        return capChecker.CheckChapter(resolved.Chapter);
    }

    static DiagnosticBag CheckWithGrants(string source, string[] grants)
    {
        Codex.Core.SourceText src = new("test.codex", source);
        DiagnosticBag diagnostics = new();

        Codex.Syntax.Lexer lexer = new(src, diagnostics);
        IReadOnlyList<Codex.Syntax.Token> tokens = lexer.TokenizeAll();
        Codex.Syntax.Parser parser = new(tokens, diagnostics);
        Codex.Syntax.DocumentNode document = parser.ParseDocument();

        Codex.Ast.Desugarer desugarer = new(diagnostics);
        Codex.Ast.Chapter module = desugarer.Desugar(document, "test");

        Codex.Semantics.NameResolver resolver = new(diagnostics);
        Codex.Semantics.ResolvedChapter resolved = resolver.Resolve(module);

        TypeChecker checker = new(diagnostics);
        Map<string, CodexType> types = checker.CheckChapter(resolved.Chapter);

        Set<string> grantSet = Set<string>.s_empty;
        foreach (string g in grants)
        {
            grantSet = grantSet.Add(g);
        }

        CapabilityChecker capChecker = new(diagnostics, types);
        capChecker.CheckChapter(resolved.Chapter, grantSet);
        return diagnostics;
    }

    [Fact]
    public void Network_rejected_without_grant()
    {
        string source = """
            main : [Network] Text
            main = fetch "https://example.com"
            """;
        DiagnosticBag diagnostics = CheckWithGrants(source, ["Console"]);
        Assert.True(diagnostics.HasErrors);
        bool found = false;
        foreach (Diagnostic diag in diagnostics.ToImmutable())
        {
            if (diag.Code == CdxCodes.CapabilityNotGranted && diag.Message.Contains("Network"))
            {
                found = true;
            }
        }
        Assert.True(found, "Expected CDX4001 for missing Network capability");
    }

    [Fact]
    public void Network_accepted_with_grant()
    {
        string source = """
            main : [Console, Network] Nothing
            main = print-line "connected"
            """;
        DiagnosticBag diagnostics = CheckWithGrants(source, ["Console", "Network"]);
        Assert.False(diagnostics.HasErrors, string.Join("; ", diagnostics.ToImmutable()));
    }

    [Fact]
    public void Camera_rejected_without_grant()
    {
        string source = """
            main : [Camera] Text
            main = capture
            """;
        DiagnosticBag diagnostics = CheckWithGrants(source, ["Display"]);
        Assert.True(diagnostics.HasErrors);
        bool found = false;
        foreach (Diagnostic diag in diagnostics.ToImmutable())
        {
            if (diag.Code == CdxCodes.CapabilityNotGranted && diag.Message.Contains("Camera"))
            {
                found = true;
            }
        }
        Assert.True(found, "Expected CDX4001 for missing Camera capability");
    }

    [Fact]
    public void Microphone_rejected_without_grant()
    {
        string source = """
            main : [Microphone] Text
            main = listen 5000
            """;
        DiagnosticBag diagnostics = CheckWithGrants(source, []);
        Assert.True(diagnostics.HasErrors);
        bool found = false;
        foreach (Diagnostic diag in diagnostics.ToImmutable())
        {
            if (diag.Code == CdxCodes.CapabilityNotGranted && diag.Message.Contains("Microphone"))
            {
                found = true;
            }
        }
        Assert.True(found, "Expected CDX4001 for missing Microphone capability");
    }

    [Fact]
    public void Display_only_grants_no_network()
    {
        string source = """
            main : [Display, Network] Nothing
            main = do
              clear
              fetch "https://spy.example.com"
              draw-text "hello" 0 0
            """;
        DiagnosticBag diagnostics = CheckWithGrants(source, ["Display"]);
        Assert.True(diagnostics.HasErrors);
        bool found = false;
        foreach (Diagnostic diag in diagnostics.ToImmutable())
        {
            if (diag.Code == CdxCodes.CapabilityNotGranted && diag.Message.Contains("Network"))
            {
                found = true;
            }
        }
        Assert.True(found, "Expected CDX4001 for missing Network — the flashlight test");
    }

    [Fact]
    public void Phone_app_with_all_grants()
    {
        string source = """
            main : [Console, Display, Network] Nothing
            main = print-line "app running"
            """;
        DiagnosticBag diagnostics = CheckWithGrants(source, ["Console", "Display", "Network"]);
        Assert.False(diagnostics.HasErrors, string.Join("; ", diagnostics.ToImmutable()));
    }
}
