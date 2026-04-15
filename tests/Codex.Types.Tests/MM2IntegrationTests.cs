using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Codex.Ast;
using Codex.Core;
using Codex.IR;
using Codex.Semantics;
using Codex.Syntax;
using Xunit;
using Xunit.Abstractions;

namespace Codex.Types.Tests;

/// <summary>
/// MM2 integration tests: compiling and running programs on bare metal x86-64
/// under QEMU with serial I/O. These tests validate the path toward running
/// the self-hosted compiler on bare metal.
/// </summary>
public class MM2IntegrationTests
{
    private readonly ITestOutputHelper m_output;

    public MM2IntegrationTests(ITestOutputHelper output)
    {
        m_output = output;
    }

    // ── Serial Input Round-trip ──────────────────────────────────

    [Fact]
    public void BareMetal_read_serial_and_print_boots_under_qemu()
    {
        // A program that reads from serial (via read-file) and prints it back.
        // This validates the CCE I/O boundary: Unicode in → CCE internal → Unicode out.
        if (!IsLinux()) { m_output.WriteLine("SKIP: not Linux"); return; }
        if (!HasQemuSystem()) { m_output.WriteLine("SKIP: no qemu-system-x86_64"); return; }

        string source = """
            main : [Console, FileSystem] Nothing
            main = do
              content <- read-file "test.codex"
              print-line content
            """;

        string serialInput = "Hello from serial!";
        string? output = CompileAndBootWithSerialInput(source, "bm_serial_echo", serialInput);
        Assert.NotNull(output);
        m_output.WriteLine($"Serial echo output: [{output}]");
        Assert.Equal("Hello from serial!", output.Trim());
    }

    // ── New Builtins on Bare Metal ───────────────────────────────

    [Fact]
    public void BareMetal_text_compare_works_under_qemu()
    {
        if (!IsLinux()) { m_output.WriteLine("SKIP: not Linux"); return; }
        if (!HasQemuSystem()) { m_output.WriteLine("SKIP: no qemu-system-x86_64"); return; }

        string source = """
            main : Integer
            main =
              let a = text-compare "apple" "banana"
              in let b = text-compare "hello" "hello"
              in let c = text-compare "zebra" "alpha"
              in a + b + c
            """;

        // "apple" < "banana" → -1, "hello" == "hello" → 0, "zebra" > "alpha" → 1
        // Sum = -1 + 0 + 1 = 0
        string? output = CompileAndBootBareMetal(source, "bm_text_compare");
        Assert.NotNull(output);
        m_output.WriteLine($"text-compare output: [{output}]");
        Assert.Equal("0", output.Trim());
    }

    [Fact]
    public void BareMetal_list_snoc_works_under_qemu()
    {
        if (!IsLinux()) { m_output.WriteLine("SKIP: not Linux"); return; }
        if (!HasQemuSystem()) { m_output.WriteLine("SKIP: no qemu-system-x86_64"); return; }

        string source = """
            main : Integer
            main =
              let xs = list-snoc (list-snoc (list-snoc [] 10) 20) 30
              in list-at xs 0 + list-at xs 1 + list-at xs 2
            """;

        // 10 + 20 + 30 = 60
        string? output = CompileAndBootBareMetal(source, "bm_list_snoc");
        Assert.NotNull(output);
        m_output.WriteLine($"list-snoc output: [{output}]");
        Assert.Equal("60", output.Trim());
    }

    [Fact]
    public void BareMetal_list_insert_at_works_under_qemu()
    {
        if (!IsLinux()) { m_output.WriteLine("SKIP: not Linux"); return; }
        if (!HasQemuSystem()) { m_output.WriteLine("SKIP: no qemu-system-x86_64"); return; }

        // Insert 99 at index 1 into [10, 30] → [10, 99, 30]
        string source = """
            main : Integer
            main =
              let xs = list-snoc (list-snoc [] 10) 30
              in let ys = list-insert-at xs 1 99
              in list-at ys 0 + list-at ys 1 + list-at ys 2
            """;

        // 10 + 99 + 30 = 139
        string? output = CompileAndBootBareMetal(source, "bm_list_insert_at");
        Assert.NotNull(output);
        m_output.WriteLine($"list-insert-at output: [{output}]");
        Assert.Equal("139", output.Trim());
    }

    // NOTE: list-contains has an x86-64 runtime helper but is NOT registered
    // as a frontend builtin (CDX3002: Undefined name). Cam needs to add it
    // to the builtin table. Test deferred until then.

    [Fact]
    public void BareMetal_text_concat_list_works_under_qemu()
    {
        if (!IsLinux()) { m_output.WriteLine("SKIP: not Linux"); return; }
        if (!HasQemuSystem()) { m_output.WriteLine("SKIP: no qemu-system-x86_64"); return; }

        string source = """
            main : [Console] Nothing
            main = print-line (text-concat-list ["Hello", " ", "World"])
            """;

        string? output = CompileAndBootBareMetal(source, "bm_text_concat_list");
        Assert.NotNull(output);
        m_output.WriteLine($"text-concat-list output: [{output}]");
        Assert.Equal("Hello World", output.Trim());
    }

    // ── MM2 Compiler Kernel ─────────────────────────────────────

    [Fact]
    public void MM2_compiler_kernel_compiles_to_bare_metal()
    {
        // Phase 3 prerequisite: can we compile the self-hosted compiler into a
        // bare metal ELF at all? This tests compilation only, not execution.
        if (!IsLinux()) { m_output.WriteLine("SKIP: not Linux"); return; }

        string? codexDir = FindCodexDir();
        if (codexDir is null) { m_output.WriteLine("SKIP: Codex.Codex dir not found"); return; }

        string[] files = Directory.GetFiles(codexDir, "*.codex", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToArray();
        m_output.WriteLine($"Compiler source: {files.Length} files");

        DiagnosticBag diag = new DiagnosticBag();
        Desugarer desugarer = new Codex.Ast.Desugarer(diag);
        List<Chapter> chapters = [];

        // Parse and desugar each file as its own chapter
        foreach (string filePath in files)
        {
            string content = File.ReadAllText(filePath);
            string chapterName = Path.GetFileNameWithoutExtension(filePath);
            SourceText src;
            DocumentNode document;

            if (ProseParser.IsProseDocument(content))
            {
                src = new SourceText(filePath, content);
                document = new ProseParser(src, diag).ParseDocument();
            }
            else
            {
                src = new SourceText(filePath, content);
                IReadOnlyList<Token> tokens = new Lexer(src, diag).TokenizeAll();
                document = new Parser(tokens, diag).ParseDocument();
            }

            if (document.Chapters.Count > 0)
            {
                chapterName = document.Chapters[0].Title;
            }

            chapters.Add(desugarer.Desugar(document, chapterName));
        }

        int parseErrors = CountErrors(diag);
        m_output.WriteLine($"Parse+Desugar: {chapters.Count} chapters, {parseErrors} errors");
        if (diag.HasErrors) { DumpErrors(diag, 10); Assert.Fail("Parse/Desugar failed"); return; }

        // Scope chapters (handles duplicate names across files)
        ChapterScoper scoper = new ChapterScoper(diag);
        Chapter combined = scoper.Scope(chapters, "CompilerKernel");
        m_output.WriteLine($"Scope: {combined.Definitions.Count} defs, {CountErrors(diag)} errors");
        if (diag.HasErrors) { DumpErrors(diag, 10); Assert.Fail("Chapter scoping failed"); return; }

        // Resolve
        ResolvedChapter resolved = new Codex.Semantics.NameResolver(diag).Resolve(combined);
        int resolveErrors = CountErrors(diag);
        m_output.WriteLine($"Resolve: {resolveErrors} total errors");
        if (diag.HasErrors) { DumpErrors(diag, 10); Assert.Fail("Name resolution failed"); return; }

        // TypeCheck
        TypeChecker checker = new Codex.Types.TypeChecker(diag);
        Map<string, CodexType> types = checker.CheckChapter(resolved.Chapter);
        int typeErrors = CountErrors(diag);
        m_output.WriteLine($"TypeCheck: {typeErrors} total errors");
        if (diag.HasErrors) { DumpErrors(diag, 10); Assert.Fail("Type checking failed"); return; }

        // Linearity
        new Codex.Types.LinearityChecker(diag, types).CheckChapter(resolved.Chapter);
        if (diag.HasErrors) { DumpErrors(diag, 10); Assert.Fail("Linearity check failed"); return; }

        // Lower
        IRChapter irModule = new Lowering(types, checker.ConstructorMap, checker.TypeDefMap, diag).Lower(resolved.Chapter);
        m_output.WriteLine($"Lower: {irModule.Definitions.Length} IR defs, {CountErrors(diag)} total errors");
        if (diag.HasErrors) { DumpErrors(diag, 10); Assert.Fail("Lowering failed"); return; }

        // Emit bare metal
        Emit.X86_64.X86_64Emitter emitter = new Codex.Emit.X86_64.X86_64Emitter(Codex.Emit.X86_64.X86_64Target.BareMetal);
        byte[] bytes = emitter.EmitAssembly(irModule, "CompilerKernel");
        m_output.WriteLine($"SUCCESS: Compiler kernel = {bytes.Length} bytes ({bytes.Length / 1024} KB)");
        Assert.True(bytes.Length > 0);
    }

    int CountErrors(DiagnosticBag bag)
        => bag.ToImmutable().Count(d => d.IsError);

    void DumpErrors(DiagnosticBag bag, int max)
    {
        foreach (Diagnostic? d in bag.ToImmutable().Where(d => d.IsError).Take(max))
        {
            m_output.WriteLine($"  {d.Code}: {d.Message}");
        }
    }

    // ── Helpers ──────────────────────────────────────────────────

    static string? CompileAndBootBareMetal(string source, string chapterName)
    {
        byte[]? bytes = Helpers.CompileToX86_64BareMetal(source, chapterName);
        if (bytes is null)
        {
            return null;
        }

        return BootAndCapture(bytes, chapterName, null);
    }

    static string? CompileAndBootWithSerialInput(string source, string chapterName, string serialInput)
    {
        byte[]? bytes = Helpers.CompileToX86_64BareMetal(source, chapterName);
        if (bytes is null)
        {
            return null;
        }

        return BootAndCapture(bytes, chapterName, serialInput);
    }

    static string? BootAndCapture(byte[] elfBytes, string chapterName, string? serialInput)
    {
        string tempDir = Path.Combine(Path.GetTempPath(),
            $"codex_mm2_{chapterName}_{Guid.NewGuid().ToString("N")[..8]}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string elfPath = Path.Combine(tempDir, chapterName + ".elf");
            File.WriteAllBytes(elfPath, elfBytes);

            // Boot under qemu-system-x86_64 with serial on stdio.
            // If serialInput is provided, pipe it to stdin (→ COM1).
            // Timeout prevents hang on kernel halt.
            ProcessStartInfo psi = new("timeout",
                $"10 qemu-system-x86_64 -kernel {elfPath} -nographic -no-reboot")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = serialInput is not null,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = tempDir
            };

            using Process? proc = Process.Start(psi);
            if (proc is null)
            {
                return null;
            }

            if (serialInput is not null)
            {
                // Send input over serial after a brief delay for kernel boot.
                // Append null terminator so __bare_metal_read_serial knows when to stop.
                Task.Run(async () =>
                {
                    await Task.Delay(1000); // wait for kernel to boot and start reading
                    byte[] inputBytes = Encoding.ASCII.GetBytes(serialInput + "\0");
                    await proc.StandardInput.BaseStream.WriteAsync(inputBytes);
                    await proc.StandardInput.BaseStream.FlushAsync();
                    proc.StandardInput.Close();
                });
            }

            string stdout = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(15_000);

            // Extract output after QEMU boot message
            int marker = stdout.IndexOf("Booting from ROM..");
            if (marker >= 0)
            {
                string afterBoot = stdout[(marker + "Booting from ROM..".Length)..].Trim();
                int newline = afterBoot.IndexOf('\n');
                return newline >= 0 ? afterBoot[..newline].Trim() : afterBoot;
            }

            return stdout;
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    static string LoadCompilerSource(string codexDir)
    {
        string[] files = Directory.GetFiles(codexDir, "*.codex", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToArray();

        List<string> codeBlocks = [];
        foreach (string f in files)
        {
            string content = File.ReadAllText(f);
            if (IsProseDocument(content))
            {
                string code = ExtractCodeBlocks(content);
                if (code.Length > 0)
                {
                    codeBlocks.Add(code);
                }
            }
            else
            {
                codeBlocks.Add(content);
            }
        }
        return string.Join("\n\n", codeBlocks);
    }

    static bool IsProseDocument(string content)
    {
        string trimmed = content.TrimStart();
        return trimmed.StartsWith("Chapter:") || trimmed.StartsWith("Section:");
    }

    // Matches tools/Codex.Bootstrap/Program.cs ExtractCodeBlocks exactly.
    static string ExtractCodeBlocks(string content)
    {
        string[] lines = content.Split('\n');
        List<string> result = [];
        int i = 0;

        while (i < lines.Length)
        {
            string trimmed = lines[i].Trim();

            if (trimmed.Length == 0 ||
                trimmed.StartsWith("Chapter:", StringComparison.Ordinal) ||
                trimmed.StartsWith("Section:", StringComparison.Ordinal))
            {
                i++;
                continue;
            }

            int indent = MeasureIndent(lines[i]);
            if (indent >= 2 && LooksLikeNotation(trimmed))
            {
                int baseIndent = indent;
                List<string> block = [];

                while (i < lines.Length)
                {
                    string line = lines[i];
                    string lt = line.Trim();

                    if (lt.Length == 0)
                    {
                        int peekIdx = i + 1;
                        while (peekIdx < lines.Length && lines[peekIdx].Trim().Length == 0)
                        {
                            peekIdx++;
                        }

                        if (peekIdx < lines.Length && MeasureIndent(lines[peekIdx]) >= baseIndent)
                        {
                            block.Add("");
                            i++;
                            continue;
                        }
                        break;
                    }

                    int lineIndent = MeasureIndent(line);
                    if (lineIndent < baseIndent)
                    {
                        break;
                    }

                    if (lt.StartsWith("Chapter:", StringComparison.Ordinal) ||
                        lt.StartsWith("Section:", StringComparison.Ordinal))
                    {
                        break;
                    }

                    string dedented = lineIndent >= baseIndent
                        ? line[baseIndent..].TrimEnd('\r')
                        : lt;
                    block.Add(dedented);
                    i++;
                }

                if (block.Count > 0)
                {
                    result.Add(string.Join("\n", block));
                }
            }
            else
            {
                i++;
            }
        }

        return string.Join("\n\n", result);
    }

    static bool LooksLikeNotation(string trimmed)
    {
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (trimmed[0] == '|')
        {
            return true;
        }

        if (char.IsLetter(trimmed[0]) || trimmed[0] == '_')
        {
            if (trimmed.Contains(" : "))
            {
                return true;
            }

            if (trimmed.Contains(" = "))
            {
                return true;
            }

            if (trimmed.EndsWith(" =") || trimmed.EndsWith("="))
            {
                return true;
            }

            if (trimmed.Contains('('))
            {
                return true;
            }
        }
        return false;
    }

    static int MeasureIndent(string line)
    {
        int count = 0;
        foreach (char c in line)
        {
            if (c == ' ')
            {
                count++;
            }
            else
            {
                break;
            }
        }
        return count;
    }

    static string? FindCodexDir()
    {
        string dir = AppContext.BaseDirectory;
        for (int i = 0; i < 10; i++)
        {
            string candidate = Path.Combine(dir, "Codex.Codex");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = Path.GetDirectoryName(dir)!;
            if (dir is null)
            {
                break;
            }
        }
        return null;
    }

    static bool IsLinux() => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
    static bool HasQemuSystem() => HasTool("qemu-system-x86_64");

    static bool HasTool(string name)
    {
        try
        {
            using Process? p = Process.Start(new ProcessStartInfo(name, "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            p?.WaitForExit(3000);
            return p?.ExitCode == 0;
        }
        catch { return false; }
    }
}
