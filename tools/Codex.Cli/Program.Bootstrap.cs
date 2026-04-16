using System.Diagnostics;
using System.Text;

namespace Codex.Cli;

public static partial class Program
{
    static int RunBootstrap(string[] args)
    {
        string codexDir = args.Length > 0 ? args[0]
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Codex.Codex"));

        if (!Directory.Exists(codexDir))
        {
            Console.Error.WriteLine($"Codex.Codex directory not found: {codexDir}");
            Console.Error.WriteLine("Usage: codex bootstrap [path/to/Codex.Codex]");
            return 1;
        }

        string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string bootstrapDir = Path.Combine(repoRoot, "tools", "Codex.Bootstrap");
        string codexLibPath = Path.Combine(bootstrapDir, "CodexLib.g.cs");

        Stopwatch total = Stopwatch.StartNew();
        Console.WriteLine("╔══════════════════════════════════════════════╗");
        Console.WriteLine("║  Codex Bootstrap — Self-Hosting Verification  ║");
        Console.WriteLine("╚══════════════════════════════════════════════╝");
        Console.WriteLine();

        Console.Write("Prep: cleaning intermediates...");
        CleanIntermediates(codexDir, repoRoot);
        Console.WriteLine(" done");

        Console.Write("Stage 0: Compiling .codex source (bootstrap compiler)...");
        Stopwatch sw = Stopwatch.StartNew();

        // Quire semantics: root + one level of subdirectory only.
        List<string> bootFiles = [];
        bootFiles.AddRange(Directory.GetFiles(codexDir, "*.codex", SearchOption.TopDirectoryOnly));
        foreach (string subDir in Directory.GetDirectories(codexDir))
        {
            bootFiles.AddRange(Directory.GetFiles(subDir, "*.codex", SearchOption.TopDirectoryOnly));
        }

        string[] files = bootFiles
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToArray();

        string outputDir = Path.Combine(repoRoot, "build-output", "bootstrap");
        if (!Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        string chapterName = "Codex.Codex";
        IRCompilationResult? irResult = CompileMultipleToIR(files, chapterName, codexRoot: codexDir);
        if (irResult is null)
        {
            Console.WriteLine(" FAILED");
            Console.Error.WriteLine("Stage 0 compilation failed.");
            return 1;
        }

        Emit.CSharp.CSharpEmitter emitter = new();
        string stage0Output = emitter.Emit(irResult.Chapter);
        string stage0Path = Path.Combine(outputDir, chapterName + ".cs");
        File.WriteAllText(stage0Path, stage0Output);
        sw.Stop();
        Console.WriteLine($" {stage0Output.Length:N0} chars ({sw.ElapsedMilliseconds}ms)");
        Console.WriteLine($"  {files.Length} files, {irResult.Chapter.Definitions.Length} defs → {stage0Path}");

        Console.Write("Prep: building Bootstrap...");
        int prepBuild = RunDotnetBuildFull(Path.Combine(bootstrapDir, "Codex.Bootstrap.csproj"));
        if (prepBuild != 0)
        {
            Console.WriteLine(" FAILED");
            return 1;
        }
        Console.WriteLine(" done");

        // ── Stage 1: Self-hosted compiler compiles itself ──
        Console.Write("Stage 1: Self-compile (self-hosted compiler compiles .codex)...");
        sw.Restart();

        string stage1Path = Path.Combine(repoRoot, "build-output", "bootstrap", "stage1-output.cs");
        int stage1Exit = RunBootstrapStage(bootstrapDir, codexDir, stage1Path);
        if (stage1Exit != 0)
        {
            Console.WriteLine(" FAILED");
            Console.Error.WriteLine($"Stage 1 failed with exit code {stage1Exit}.");
            return 1;
        }

        string stage1Output = File.ReadAllText(stage1Path);
        sw.Stop();
        Console.WriteLine($" {stage1Output.Length:N0} chars ({sw.ElapsedMilliseconds}ms)");

        // ── Stage 2: Swap in Stage 1 output, compile again ──
        Console.Write("Stage 2: Fixed-point test (stage 1 output compiles .codex)...");
        sw.Restart();

        string codexLibBackup = codexLibPath + ".bak";
        File.Copy(codexLibPath, codexLibBackup, true);
        try
        {
            // Copy stage1 output and strip entry point
            string stage1Content = File.ReadAllText(stage1Path);
            stage1Content = stage1Content.Replace("Codex_Codex_Codex.main();\n", "")
                                         .Replace("Codex_Codex_Codex.main();\r\n", "");
            File.WriteAllText(codexLibPath, stage1Content);

            // Rebuild bootstrap with new CodexLib
            int buildExit = RunDotnetBuild(Path.Combine(bootstrapDir, "Codex.Bootstrap.csproj"));
            if (buildExit != 0)
            {
                Console.WriteLine(" BUILD FAILED");
                return 1;
            }

            string stage3Path = Path.Combine(repoRoot, "build-output", "bootstrap", "stage3-output.cs");
            int stage3Exit = RunBootstrapStage(bootstrapDir, codexDir, stage3Path);
            if (stage3Exit != 0)
            {
                Console.WriteLine(" FAILED");
                Console.Error.WriteLine($"Stage 2 failed with exit code {stage3Exit}.");
                return 1;
            }

            string stage3Output = File.ReadAllText(stage3Path);
            sw.Stop();
            Console.WriteLine($" {stage3Output.Length:N0} chars ({sw.ElapsedMilliseconds}ms)");

            // ── Verify C# fixed point ──
            Console.WriteLine();
            if (stage1Output != stage3Output)
            {
                total.Stop();
                Console.WriteLine($"❌ BOOTSTRAP 1 DIVERGENCE (.NET, C# output): Stage 1 ({stage1Output.Length:N0} chars) ≠ Stage 3 ({stage3Output.Length:N0} chars)");
                Console.WriteLine($"   Total time: {total.ElapsedMilliseconds:N0}ms");
                return 1;
            }
            Console.WriteLine($"✅ BOOTSTRAP 1 (.NET, C# output): Stage 1 = Stage 3 ({stage1Output.Length:N0} chars identical)");
            Console.WriteLine($"   NOTE: This is NOT pingpong. Pingpong is bare-metal (bootstrap 2).");

            // ── Codex text emission check ──
            // Uses stage 0 CodexLib to emit Codex text, then stage 1 CodexLib to emit again.
            // Fixed point: both outputs must be identical.
            Console.WriteLine();
            Console.Write("Bootstrap 1.1 stage 1: emit Codex text (stage 0 compiler)...");
            sw.Restart();
            string codexText1Path = Path.Combine(outputDir, "stage1-codex.codex");
            int ct1Exit = RunBootstrapCodexEmit(bootstrapDir, codexDir, codexText1Path);
            if (ct1Exit != 0)
            {
                Console.WriteLine(" FAILED");
                Console.Error.WriteLine("Codex text stage 1 failed.");
                return 1;
            }
            string codexText1 = File.ReadAllText(codexText1Path);
            sw.Stop();
            Console.WriteLine($" {codexText1.Length:N0} chars ({sw.ElapsedMilliseconds}ms)");

            Console.Write("Bootstrap 1.1 stage 2: emit Codex text (stage 1 compiler)...");
            sw.Restart();
            // Stage 1 CodexLib is already in place from the C# fixed-point test above
            string codexText2Path = Path.Combine(outputDir, "stage2-codex.codex");
            int ct2Exit = RunBootstrapCodexEmit(bootstrapDir, codexDir, codexText2Path);
            if (ct2Exit != 0)
            {
                Console.WriteLine(" FAILED");
                Console.Error.WriteLine("Codex text stage 2 failed.");
                return 1;
            }
            string codexText2 = File.ReadAllText(codexText2Path);
            sw.Stop();
            Console.WriteLine($" {codexText2.Length:N0} chars ({sw.ElapsedMilliseconds}ms)");

            Console.WriteLine();
            if (codexText1 == codexText2)
            {
                total.Stop();
                Console.WriteLine($"✅ BOOTSTRAP 1.1 (.NET, Codex-text output): Stage 1 = Stage 2 ({codexText1.Length:N0} chars identical)");
                Console.WriteLine($"   NOTE: Still not pingpong. Pingpong is bare-metal (bootstrap 2).");
                Console.WriteLine($"   Total time: {total.ElapsedMilliseconds:N0}ms");
                return 0;
            }
            else
            {
                total.Stop();
                Console.WriteLine($"❌ BOOTSTRAP 1.1 DIVERGENCE (.NET, Codex-text output): Stage 1 ({codexText1.Length:N0} chars) ≠ Stage 2 ({codexText2.Length:N0} chars)");
                Console.WriteLine($"   Total time: {total.ElapsedMilliseconds:N0}ms");
                return 1;
            }
        }
        finally
        {
            // Restore original CodexLib.g.cs
            if (File.Exists(codexLibBackup))
            {
                File.Copy(codexLibBackup, codexLibPath, true);
                File.Delete(codexLibBackup);
            }
            // Rebuild with original
            RunDotnetBuild(Path.Combine(bootstrapDir, "Codex.Bootstrap.csproj"));
        }
    }

    static int RunBootstrapCodexEmit(string bootstrapDir, string codexDir, string outputPath)
    {
        string csproj = Path.Combine(bootstrapDir, "Codex.Bootstrap.csproj");
        ProcessStartInfo psi = new("dotnet", $"run --project \"{csproj}\" --no-build -- --codex-emit \"{codexDir}\" \"{outputPath}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using Process? proc = Process.Start(psi);
        if (proc is null)
        {
            return 1;
        }

        proc.StandardOutput.ReadToEnd();
        proc.StandardError.ReadToEnd();
        proc.WaitForExit(120_000);
        return proc.ExitCode;
    }

    static int RunBootstrapStage(string bootstrapDir, string codexDir, string outputPath)
    {
        string csproj = Path.Combine(bootstrapDir, "Codex.Bootstrap.csproj");
        ProcessStartInfo psi = new("dotnet", $"run --project \"{csproj}\" --no-build -- \"{codexDir}\" \"{outputPath}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using Process? proc = Process.Start(psi);
        if (proc is null)
        {
            return 1;
        }

        proc.StandardOutput.ReadToEnd();
        proc.StandardError.ReadToEnd();
        proc.WaitForExit(60_000);
        return proc.ExitCode;
    }

    static int RunDotnetBuild(string csproj)
    {
        ProcessStartInfo psi = new("dotnet", $"build \"{csproj}\" --no-restore -p:SkipCodexRegenerate=true")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(csproj) ?? "."
        };

        using Process? proc = Process.Start(psi);
        if (proc is null)
        {
            return 1;
        }

        proc.StandardOutput.ReadToEnd();
        string stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(30_000);
        if (proc.ExitCode != 0 && stderr.Length > 0)
        {
            Console.Error.WriteLine(stderr);
        }

        return proc.ExitCode;
    }

    static int RunDotnetBuildFull(string csproj)
    {
        ProcessStartInfo psi = new("dotnet", $"build \"{csproj}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(csproj) ?? "."
        };

        using Process? proc = Process.Start(psi);
        if (proc is null)
        {
            return 1;
        }

        proc.StandardOutput.ReadToEnd();
        string stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(60_000);
        if (proc.ExitCode != 0 && stderr.Length > 0)
        {
            Console.Error.WriteLine(stderr);
        }

        return proc.ExitCode;
    }

    static void CleanIntermediates(string codexDir, string repoRoot)
    {
        string outDir = Path.Combine(repoRoot, "build-output", "bootstrap");
        if (Directory.Exists(outDir))
        {
            foreach (string file in Directory.GetFiles(outDir))
            {
                File.Delete(file);
            }
        }
        else
        {
            Directory.CreateDirectory(outDir);
        }
    }
}
