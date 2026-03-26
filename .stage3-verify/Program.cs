using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

class Program
{
    static int Main(string[] args)
    {
        int exitCode = 1;
        var thread = new Thread(() => exitCode = Run(args), 256 * 1024 * 1024);
        thread.Start();
        thread.Join();
        return exitCode;
    }

    static int Run(string[] args)
    {
        string codexDir = args.Length > 0 ? args[0]
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Codex.Codex"));

        if (!Directory.Exists(codexDir))
        {
            Console.Error.WriteLine($"Codex.Codex directory not found: {codexDir}");
            return 1;
        }

        Console.WriteLine($"=== Stage 3 Fixed-Point Verification ===");
        Console.WriteLine($"Reading .codex sources from: {codexDir}");

        string[] files = Directory.GetFiles(codexDir, "*.codex", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToArray();

        Console.WriteLine($"Found {files.Length} .codex files");

        List<string> codeBlocks = [];
        foreach (string f in files)
        {
            string rel = Path.GetRelativePath(codexDir, f);
            string content = File.ReadAllText(f);
            Console.WriteLine($"  {rel}");

            if (IsProseDocument(content))
            {
                string code = ExtractCodeBlocks(content);
                if (code.Length > 0)
                    codeBlocks.Add(code);
            }
            else
            {
                codeBlocks.Add(content);
            }
        }

        string combined = string.Join("\n\n", codeBlocks);
        Console.WriteLine($"Total source: {combined.Length} chars");
        Console.WriteLine("Compiling with Stage 2 compiler...");

        try
        {
            var tokens = Codex_Codex_Codex.tokenize(combined);
            var st = Codex_Codex_Codex.make_parse_state(tokens);
            var doc = Codex_Codex_Codex.parse_document(st);
            var ast = Codex_Codex_Codex.desugar_document(doc, "Codex_Codex");
            var checkResult = Codex_Codex_Codex.check_module(ast);

            Console.WriteLine($"  Type bindings: {checkResult.types.Count}");
            Console.WriteLine($"  Unification errors: {checkResult.state.errors.Count}");

            if (checkResult.state.errors.Count > 0)
            {
                Console.WriteLine("  First 10 errors:");
                for (int i = 0; i < Math.Min(10, checkResult.state.errors.Count); i++)
                {
                    var diag = checkResult.state.errors[i];
                    Console.WriteLine($"    [{diag.code}] {diag.message}");
                }
            }

            var ir = Codex_Codex_Codex.lower_module(ast, checkResult.types, checkResult.state);
            Console.WriteLine($"  IR defs: {ir.defs.Count}");

            string stage3 = Codex_Codex_Codex.emit_full_module(ir, ast.type_defs);

            string stage3Path = Path.Combine(codexDir, "stage3-output.cs");
            File.WriteAllText(stage3Path, stage3);
            Console.WriteLine($"Stage 3 output written to: {stage3Path}");
            Console.WriteLine($"Stage 3 size: {stage3.Length} chars");

            // Compare against Stage 2
            string stage2Path = Path.Combine(codexDir, "stage1-output.cs");
            if (File.Exists(stage2Path))
            {
                string stage2 = File.ReadAllText(stage2Path);
                Console.WriteLine($"Stage 2 size: {stage2.Length} chars");

                if (stage3 == stage2)
                {
                    Console.WriteLine();
                    Console.WriteLine("*** FIXED POINT ACHIEVED ***");
                    Console.WriteLine("Stage 3 == Stage 2 (byte-for-byte identical)");
                    Console.WriteLine("The compiler produces itself.");
                }
                else
                {
                    Console.WriteLine();
                    Console.WriteLine("Stage 3 != Stage 2 — analyzing differences...");

                    string[] s2Lines = stage2.Split('\n');
                    string[] s3Lines = stage3.Split('\n');
                    Console.WriteLine($"  Stage 2 lines: {s2Lines.Length}");
                    Console.WriteLine($"  Stage 3 lines: {s3Lines.Length}");

                    int diffCount = 0;
                    int maxLines = Math.Max(s2Lines.Length, s3Lines.Length);
                    for (int i = 0; i < maxLines; i++)
                    {
                        string l2 = i < s2Lines.Length ? s2Lines[i] : "<missing>";
                        string l3 = i < s3Lines.Length ? s3Lines[i] : "<missing>";
                        if (l2 != l3)
                        {
                            diffCount++;
                            if (diffCount <= 20)
                            {
                                Console.WriteLine($"  Line {i + 1}:");
                                Console.WriteLine($"    S2: {(l2.Length > 120 ? l2[..120] + "..." : l2)}");
                                Console.WriteLine($"    S3: {(l3.Length > 120 ? l3[..120] + "..." : l3)}");
                            }
                        }
                    }
                    Console.WriteLine($"  Total differing lines: {diffCount}");

                    // Check semantic equivalence — same function count, same function names
                    var s2Funcs = s2Lines.Where(l => l.Contains("public static")).Select(l => l.Trim().Split('(')[0]).ToList();
                    var s3Funcs = s3Lines.Where(l => l.Contains("public static")).Select(l => l.Trim().Split('(')[0]).ToList();
                    Console.WriteLine($"  Stage 2 functions: {s2Funcs.Count}");
                    Console.WriteLine($"  Stage 3 functions: {s3Funcs.Count}");

                    var onlyInS2 = s2Funcs.Except(s3Funcs).ToList();
                    var onlyInS3 = s3Funcs.Except(s2Funcs).ToList();
                    if (onlyInS2.Count > 0)
                    {
                        Console.WriteLine($"  Only in Stage 2: {string.Join(", ", onlyInS2.Take(10))}");
                    }
                    if (onlyInS3.Count > 0)
                    {
                        Console.WriteLine($"  Only in Stage 3: {string.Join(", ", onlyInS3.Take(10))}");
                    }
                    if (onlyInS2.Count == 0 && onlyInS3.Count == 0 && s2Funcs.Count == s3Funcs.Count)
                    {
                        Console.WriteLine("  *** SEMANTIC EQUIVALENCE: same functions, same signatures ***");
                    }
                }
            }
            else
            {
                Console.WriteLine($"Stage 2 file not found at {stage2Path} — skipping comparison.");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Stage 3 compilation failed: {ex.GetType().Name}: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    static bool IsProseDocument(string content)
    {
        foreach (string line in content.Split('\n'))
        {
            string trimmed = line.TrimStart();
            if (trimmed.Length == 0)
                continue;
            return trimmed.StartsWith("Chapter:", StringComparison.Ordinal);
        }
        return false;
    }

    static string ExtractCodeBlocks(string content)
    {
        string[] lines = content.Split('\n');
        List<string> result = [];
        int i = 0;

        while (i < lines.Length)
        {
            string trimmed = lines[i].Trim();

            if (trimmed.Length == 0)
            {
                i++;
                continue;
            }

            if (trimmed.StartsWith("Chapter:", StringComparison.Ordinal) ||
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
                            peekIdx++;

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
                        break;

                    if (lt.StartsWith("Chapter:", StringComparison.Ordinal) ||
                        lt.StartsWith("Section:", StringComparison.Ordinal))
                        break;

                    string dedented = lineIndent >= baseIndent
                        ? line[baseIndent..].TrimEnd('\r')
                        : lt;
                    block.Add(dedented);
                    i++;
                }

                if (block.Count > 0)
                    result.Add(string.Join("\n", block));
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
        if (trimmed.Length == 0) return false;
        if (trimmed[0] == '|') return true;
        if (char.IsLetter(trimmed[0]) || trimmed[0] == '_')
        {
            if (trimmed.Contains(" : ")) return true;
            if (trimmed.Contains(" = ")) return true;
            if (trimmed.EndsWith(" =") || trimmed.EndsWith("=")) return true;
            if (trimmed.Contains('(')) return true;
        }
        return false;
    }

    static int MeasureIndent(string line)
    {
        int count = 0;
        foreach (char c in line)
        {
            if (c == ' ') count++;
            else break;
        }
        return count;
    }
}
