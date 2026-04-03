using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
        if (args.Length > 0 && args[0] == "--mini" && args.Length > 1)
            return RunMini(args[1]);
        if (args.Length > 0 && args[0] == "--bench")
            return RunBench(args.Length > 1 ? args[1] : null);
        if (args.Length > 0 && args[0] == "--bench-check")
            return RunBenchCheck(args.Length > 1 ? args[1] : null);
        if (args.Length > 0 && args[0] == "--bench-save")
            return RunBenchSave(args.Length > 1 ? args[1] : null);
        if (args.Length > 0 && args[0] == "--dump-source")
            return RunDumpSource(args.Length > 1 ? args[1] : null);
        if (args.Length > 0 && args[0] == "--codex-emit")
            return RunCodexEmit(args.Length > 1 ? args[1] : null, args.Length > 2 ? args[2] : null);

        string codexDir = args.Length > 0 ? args[0] : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Codex.Codex"));
        string? outputOverride = args.Length > 1 ? args[1] : null;

        if (!Directory.Exists(codexDir))
        {
            Console.Error.WriteLine($"Codex.Codex directory not found: {codexDir}");
            return 1;
        }

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

        Console.WriteLine($"Total source after prose extraction: {combined.Length} chars");
        Console.WriteLine("Compiling with Codex.Codex (Stage 1)...");

        // Convert source from Unicode to CCE at the boundary
        string cceCombined = _Cce.FromUnicode(combined);


        try
        {
            var tokens = Codex_Codex_Codex.tokenize(cceCombined);
            Console.WriteLine($"  Tokens: {tokens.Count}");

            // Debug: dump first 80 tokens
            for (int ti = 0; ti < Math.Min(80, tokens.Count); ti++)
            {
                var tok = tokens[ti];
                string kindName = tok.kind.GetType().Name;
                string tokText = _Cce.ToUnicode(tok.text);
                if (tokText == "\n") tokText = "\\n";
                Console.WriteLine($"    [{ti}] {kindName,-20} \"{tokText}\"");
            }

            var st = Codex_Codex_Codex.make_parse_state(tokens);
            var doc = Codex_Codex_Codex.parse_document(st);
            Console.WriteLine($"  Parsed defs: {doc.defs.Count}");
            Console.WriteLine($"  Parsed type-defs: {doc.type_defs.Count}");


            var ast = Codex_Codex_Codex.desugar_document(doc, _Cce.FromUnicode("Codex_Codex"));
            Console.WriteLine($"  AST defs: {ast.defs.Count}");
            Console.WriteLine($"  AST type-defs: {ast.type_defs.Count}");

            var checkResult = Codex_Codex_Codex.check_module(ast);
            Console.WriteLine($"  Type bindings: {checkResult.types.Count}");
            Console.WriteLine($"  Unification errors: {checkResult.state.errors.Count}");

            // Dump ALL type bindings to a diagnostics file
            var diagLines = new List<string>();
            int errorTyCount = 0;
            int funcObjectCount = 0;
            for (int i = 0; i < checkResult.types.Count; i++)
            {
                var tb = checkResult.types[i];
                var resolved = Codex_Codex_Codex.deep_resolve(checkResult.state, tb.bound_type);
                string csType = Codex_Codex_Codex.cs_type(resolved);
                bool isErrorTy = resolved is ErrorTy;
                bool hasObject = csType.Contains("object");
                if (isErrorTy) errorTyCount++;
                if (hasObject) funcObjectCount++;
                string marker = isErrorTy ? " [ERRORTY]" : (hasObject ? " [HAS-OBJECT]" : "");
                diagLines.Add($"{i}: {_Cce.ToUnicode(tb.name)} : {_Cce.ToUnicode(csType)}{marker}");
            }
            string diagPath = Path.Combine(codexDir, "type-diag.txt");
            File.WriteAllLines(diagPath, diagLines);
            Console.WriteLine($"  ErrorTy bindings: {errorTyCount}");
            Console.WriteLine($"  Has-object bindings: {funcObjectCount}");
            Console.WriteLine($"  Type diagnostics written to: {diagPath}");

            string errPath = Path.Combine(codexDir, "unify-errors.txt");
            var errLines = new List<string>();
            for (int ei = 0; ei < checkResult.state.errors.Count; ei++)
            {
                Diagnostic diag = checkResult.state.errors[ei];
                errLines.Add($"{ei}: [{_Cce.ToUnicode(diag.code)}] {_Cce.ToUnicode(diag.message)}");
            }
            File.WriteAllLines(errPath, errLines);
            Console.WriteLine($"  Unification error log: {errPath}");

            // Show first 20 type bindings
            for (int i = 0; i < Math.Min(20, checkResult.types.Count); i++)
            {
                var tb = checkResult.types[i];
                var resolved = Codex_Codex_Codex.deep_resolve(checkResult.state, tb.bound_type);
                Console.WriteLine($"    {tb.name} : {Codex_Codex_Codex.cs_type(resolved)}");
            }

            var ir = Codex_Codex_Codex.lower_module(ast, checkResult.types, checkResult.state);
            Console.WriteLine($"  IR defs: {ir.defs.Count}");

            // Show first 10 IR def signatures
            for (int j = 0; j < Math.Min(10, ir.defs.Count); j++)
            {
                var d = ir.defs[j];
                var paramStr = string.Join(", ", d.@params.Select(p => $"{Codex_Codex_Codex.cs_type(p.type_val)} {p.name}"));
                Console.WriteLine($"    {d.name}({paramStr}) : {Codex_Codex_Codex.cs_type(d.type_val)}");
            }

            string cceOutput = Codex_Codex_Codex.csharp_emitter_emit_full_module(ir, ast.type_defs);
            // Convert emitted C# source from CCE back to Unicode for .NET compiler
            string output = _Cce.ToUnicode(cceOutput);
            string outputPath = outputOverride ?? Path.Combine(Path.GetFullPath(Path.Combine(codexDir, "..")), "build-output", "stage1-output.cs");
            File.WriteAllText(outputPath, output);
            Console.WriteLine($"Output written to: {outputPath}");
            Console.WriteLine($"Output size: {output.Length} chars");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Compilation failed: {ex.GetType().Name}: {ex.Message}");
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

    static int RunMini(string filePath)
    {
        if (!File.Exists(filePath))
        {
            Console.Error.WriteLine($"File not found: {filePath}");
            return 1;
        }

        string rawContent = File.ReadAllText(filePath);
        string source;
        if (IsProseDocument(rawContent))
        {
            source = ExtractCodeBlocks(rawContent);
            Console.WriteLine($"Mini compile (prose): {filePath} ({source.Length} chars from {rawContent.Length})");
        }
        else
        {
            source = rawContent;
            Console.WriteLine($"Mini compile: {filePath} ({source.Length} chars)");
        }

        try
        {
            string cceSource = _Cce.FromUnicode(source);
            List<Token> tokens = Codex_Codex_Codex.tokenize(cceSource);
            ParseState st = Codex_Codex_Codex.make_parse_state(tokens);
            Document doc = Codex_Codex_Codex.parse_document(st);
            AModule ast = Codex_Codex_Codex.desugar_document(doc, _Cce.FromUnicode("MiniTest"));

            Console.WriteLine($"  Defs: {ast.defs.Count}, TypeDefs: {ast.type_defs.Count}");

            ModuleResult checkResult = Codex_Codex_Codex.check_module(ast);
            Console.WriteLine($"  Type bindings: {checkResult.types.Count}");
            Console.WriteLine($"  Unification errors: {checkResult.state.errors.Count}");

            for (int i = 0; i < checkResult.types.Count; i++)
            {
                TypeBinding tb = checkResult.types[i];
                CodexType resolved = Codex_Codex_Codex.deep_resolve(checkResult.state, tb.bound_type);
                string csType = Codex_Codex_Codex.cs_type(resolved);
                bool isErr = resolved is ErrorTy;
                Console.WriteLine($"    {tb.name} : {csType}{(isErr ? " [ERRORTY]" : "")}");
            }

            for (int ei = 0; ei < checkResult.state.errors.Count; ei++)
            {
                Diagnostic diag = checkResult.state.errors[ei];
                Console.WriteLine($"  ERR {ei}: [{diag.code}] {diag.message}");
            }

            IRModule ir = Codex_Codex_Codex.lower_module(ast, checkResult.types, checkResult.state);
            string output = Codex_Codex_Codex.csharp_emitter_emit_full_module(ir, ast.type_defs);

            string outPath = Path.ChangeExtension(filePath, ".g.cs");
            File.WriteAllText(outPath, output);
            Console.WriteLine($"  Output: {outPath} ({output.Length} chars)");

            int p0Count = output.Split('\n').Count(l => l.Contains("_p0_"));
            int objCount = output.Split('\n').Count(l => l.Contains("object"));
            Console.WriteLine($"  _p0_ lines: {p0Count}, object lines: {objCount}");

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed: {ex.GetType().Name}: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    static int RunBench(string? codexDirOverride)
    {
        string codexDir = codexDirOverride ?? Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Codex.Codex"));

        if (!Directory.Exists(codexDir)) { Console.Error.WriteLine($"Not found: {codexDir}"); return 1; }

        // Load source once
        string[] files = Directory.GetFiles(codexDir, "*.codex", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.Ordinal).ToArray();
        List<string> codeBlocks = [];
        foreach (string f in files)
        {
            string content = File.ReadAllText(f);
            codeBlocks.Add(IsProseDocument(content) ? ExtractCodeBlocks(content) : content);
        }
        string source = _Cce.FromUnicode(string.Join("\n\n", codeBlocks));

        Console.WriteLine($"Benchmark: {source.Length} chars, {files.Length} files");
        Console.WriteLine("Protocol: 3 warmup + 10 measured, median reported");
        Console.WriteLine();

        int warmup = 3;
        int measured = 10;

        // Warmup
        for (int w = 0; w < warmup; w++)
        {
            RunPipeline(source, out _, out _, out _, out _, out _, out _, out _, out _);
            Console.Write($"  warmup {w + 1}/{warmup}\r");
        }
        Console.WriteLine($"  warmup done ({warmup} iterations)          ");

        // Measured runs
        double[] lexTimes = new double[measured];
        double[] parseTimes = new double[measured];
        double[] desugarTimes = new double[measured];
        double[] resolveTimes = new double[measured];
        double[] checkTimes = new double[measured];
        double[] lowerTimes = new double[measured];
        double[] emitTimes = new double[measured];
        double[] totalTimes = new double[measured];

        for (int r = 0; r < measured; r++)
        {
            RunPipeline(source,
                out lexTimes[r], out parseTimes[r], out desugarTimes[r],
                out resolveTimes[r], out checkTimes[r], out lowerTimes[r],
                out emitTimes[r], out totalTimes[r]);
            Console.Write($"  run {r + 1}/{measured}\r");
        }
        Console.WriteLine($"  measured done ({measured} iterations)       ");
        Console.WriteLine();

        Array.Sort(lexTimes); Array.Sort(parseTimes); Array.Sort(desugarTimes);
        Array.Sort(resolveTimes); Array.Sort(checkTimes); Array.Sort(lowerTimes);
        Array.Sort(emitTimes); Array.Sort(totalTimes);

        int mid = measured / 2;
        Console.WriteLine("Per-stage (median ms):");
        Console.WriteLine($"  lex        {lexTimes[mid]:F2}ms");
        Console.WriteLine($"  parse      {parseTimes[mid]:F2}ms");
        Console.WriteLine($"  desugar    {desugarTimes[mid]:F2}ms");
        Console.WriteLine($"  resolve    {resolveTimes[mid]:F2}ms");
        Console.WriteLine($"  typecheck  {checkTimes[mid]:F2}ms");
        Console.WriteLine($"  lower      {lowerTimes[mid]:F2}ms");
        Console.WriteLine($"  emit       {emitTimes[mid]:F2}ms");
        Console.WriteLine($"  ─────────────────────");
        Console.WriteLine($"  total      {totalTimes[mid]:F2}ms");
        Console.WriteLine();
        Console.WriteLine($"  min={totalTimes[0]:F2}ms  max={totalTimes[measured - 1]:F2}ms");
        return 0;
    }

    static void RunPipeline(string source,
        out double lexMs, out double parseMs, out double desugarMs,
        out double resolveMs, out double checkMs, out double lowerMs,
        out double emitMs, out double totalMs)
    {
        var total = System.Diagnostics.Stopwatch.StartNew();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var tokens = Codex_Codex_Codex.tokenize(source);
        sw.Stop(); lexMs = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        var pst = Codex_Codex_Codex.make_parse_state(tokens);
        var doc = Codex_Codex_Codex.parse_document(pst);
        sw.Stop(); parseMs = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        var ast = Codex_Codex_Codex.desugar_document(doc, _Cce.FromUnicode("Bench"));
        sw.Stop(); desugarMs = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        var resolved = Codex_Codex_Codex.resolve_module(ast);
        sw.Stop(); resolveMs = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        var checkResult = Codex_Codex_Codex.check_module(ast);
        sw.Stop(); checkMs = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        var ir = Codex_Codex_Codex.lower_module(ast, checkResult.types, checkResult.state);
        sw.Stop(); lowerMs = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        var output = Codex_Codex_Codex.csharp_emitter_emit_full_module(ir, ast.type_defs);
        sw.Stop(); emitMs = sw.Elapsed.TotalMilliseconds;

        total.Stop(); totalMs = total.Elapsed.TotalMilliseconds;
    }

    static int RunBenchCheck(string? codexDirOverride)
    {
        // Load baseline
        string baselinePath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "bench-baseline.json");
        if (!File.Exists(baselinePath))
        {
            // Try relative to project dir
            baselinePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", "..", "tools", "Codex.Bootstrap", "bench-baseline.json"));
        }
        if (!File.Exists(baselinePath))
        {
            Console.Error.WriteLine("bench-baseline.json not found");
            return 1;
        }

        string json = File.ReadAllText(baselinePath);
        // Minimal JSON parsing — extract medianMs and thresholdPercent
        double baselineMs = ExtractJsonDouble(json, "medianMs");
        double threshold = ExtractJsonDouble(json, "thresholdPercent");
        var baselineStages = new Dictionary<string, double>();
        foreach (string stage in new[] { "lex", "parse", "desugar", "resolve", "typecheck", "lower", "emit" })
            baselineStages[stage] = ExtractJsonDouble(json, stage);

        Console.WriteLine($"Baseline: {baselineMs:F2}ms (threshold: {threshold}%)");
        Console.WriteLine();

        // Run benchmark (same protocol as --bench)
        string codexDir = codexDirOverride ?? Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Codex.Codex"));
        if (!Directory.Exists(codexDir)) { Console.Error.WriteLine($"Not found: {codexDir}"); return 1; }

        string[] files = Directory.GetFiles(codexDir, "*.codex", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.Ordinal).ToArray();
        List<string> codeBlocks = [];
        foreach (string f in files)
        {
            string content = File.ReadAllText(f);
            codeBlocks.Add(IsProseDocument(content) ? ExtractCodeBlocks(content) : content);
        }
        string source = _Cce.FromUnicode(string.Join("\n\n", codeBlocks));

        int warmup = 3, measured = 10;
        for (int w = 0; w < warmup; w++)
        {
            RunPipeline(source, out _, out _, out _, out _, out _, out _, out _, out _);
            Console.Write($"  warmup {w + 1}/{warmup}\r");
        }
        Console.WriteLine($"  warmup done          ");

        double[] lexT = new double[measured], parseT = new double[measured], desugarT = new double[measured];
        double[] resolveT = new double[measured], checkT = new double[measured];
        double[] lowerT = new double[measured], emitT = new double[measured], totalT = new double[measured];

        for (int r = 0; r < measured; r++)
        {
            RunPipeline(source, out lexT[r], out parseT[r], out desugarT[r],
                out resolveT[r], out checkT[r], out lowerT[r], out emitT[r], out totalT[r]);
            Console.Write($"  run {r + 1}/{measured}\r");
        }
        Console.WriteLine($"  measured done         ");
        Console.WriteLine();

        Array.Sort(lexT); Array.Sort(parseT); Array.Sort(desugarT);
        Array.Sort(resolveT); Array.Sort(checkT); Array.Sort(lowerT);
        Array.Sort(emitT); Array.Sort(totalT);
        int mid = measured / 2;

        var current = new Dictionary<string, double>
        {
            ["lex"] = lexT[mid], ["parse"] = parseT[mid], ["desugar"] = desugarT[mid],
            ["resolve"] = resolveT[mid], ["typecheck"] = checkT[mid],
            ["lower"] = lowerT[mid], ["emit"] = emitT[mid]
        };

        Console.WriteLine("Stage        Baseline    Current     Delta");
        Console.WriteLine("───────────  ──────────  ──────────  ──────────");
        foreach (string stage in new[] { "lex", "parse", "desugar", "resolve", "typecheck", "lower", "emit" })
        {
            double b = baselineStages[stage], c = current[stage];
            double pct = b > 0 ? ((c - b) / b) * 100 : 0;
            string sign = pct >= 0 ? "+" : "";
            Console.WriteLine($"  {stage,-10}  {b,8:F2}ms  {c,8:F2}ms  {sign}{pct:F1}%");
        }
        Console.WriteLine("  ───────────────────────────────────────────");
        double totalPct = baselineMs > 0 ? ((totalT[mid] - baselineMs) / baselineMs) * 100 : 0;
        string totalSign = totalPct >= 0 ? "+" : "";
        Console.WriteLine($"  {"total",-10}  {baselineMs,8:F2}ms  {totalT[mid],8:F2}ms  {totalSign}{totalPct:F1}%");
        Console.WriteLine();

        if (totalPct > threshold)
        {
            Console.WriteLine($"REGRESSION: {totalPct:F1}% exceeds {threshold}% threshold");
            return 1;
        }
        Console.WriteLine($"OK: within {threshold}% threshold ({totalSign}{totalPct:F1}%)");
        return 0;
    }

    static int RunBenchSave(string? codexDirOverride)
    {
        // Run the same benchmark protocol, then overwrite bench-baseline.json
        string codexDir = codexDirOverride ?? Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Codex.Codex"));
        if (!Directory.Exists(codexDir)) { Console.Error.WriteLine($"Not found: {codexDir}"); return 1; }

        string[] files = Directory.GetFiles(codexDir, "*.codex", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.Ordinal).ToArray();
        List<string> codeBlocks = [];
        foreach (string f in files)
        {
            string content = File.ReadAllText(f);
            codeBlocks.Add(IsProseDocument(content) ? ExtractCodeBlocks(content) : content);
        }
        string source = _Cce.FromUnicode(string.Join("\n\n", codeBlocks));

        Console.WriteLine($"Benchmark: {source.Length} chars, {files.Length} files");
        Console.WriteLine("Protocol: 3 warmup + 10 measured, saving median as new baseline");
        Console.WriteLine();

        int warmup = 3, measured = 10;
        for (int w = 0; w < warmup; w++)
        {
            RunPipeline(source, out _, out _, out _, out _, out _, out _, out _, out _);
            Console.Write($"  warmup {w + 1}/{warmup}\r");
        }
        Console.WriteLine($"  warmup done          ");

        double[] lexT = new double[measured], parseT = new double[measured], desugarT = new double[measured];
        double[] resolveT = new double[measured], checkT = new double[measured];
        double[] lowerT = new double[measured], emitT = new double[measured], totalT = new double[measured];

        for (int r = 0; r < measured; r++)
        {
            RunPipeline(source, out lexT[r], out parseT[r], out desugarT[r],
                out resolveT[r], out checkT[r], out lowerT[r], out emitT[r], out totalT[r]);
            Console.Write($"  run {r + 1}/{measured}\r");
        }
        Console.WriteLine($"  measured done         ");
        Console.WriteLine();

        Array.Sort(lexT); Array.Sort(parseT); Array.Sort(desugarT);
        Array.Sort(resolveT); Array.Sort(checkT); Array.Sort(lowerT);
        Array.Sort(emitT); Array.Sort(totalT);
        int mid = measured / 2;

        // Get current git commit hash
        string commit = "unknown";
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("git", "rev-parse --short HEAD")
            { RedirectStandardOutput = true, UseShellExecute = false };
            var proc = System.Diagnostics.Process.Start(psi);
            if (proc is not null) { commit = proc.StandardOutput.ReadToEnd().Trim(); proc.WaitForExit(); }
        }
        catch { /* git not available */ }

        string date = DateTime.UtcNow.ToString("yyyy-MM-dd");
        string json = $$"""
            {
              "date": "{{date}}",
              "commit": "{{commit}}",
              "medianMs": {{totalT[mid].ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}},
              "stages": {
                "lex": {{lexT[mid].ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}},
                "parse": {{parseT[mid].ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}},
                "desugar": {{desugarT[mid].ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}},
                "resolve": {{resolveT[mid].ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}},
                "typecheck": {{checkT[mid].ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}},
                "lower": {{lowerT[mid].ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}},
                "emit": {{emitT[mid].ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}}
              },
              "thresholdPercent": 10
            }
            """;

        // Write baseline file next to the project source
        string baselinePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "bench-baseline.json"));
        File.WriteAllText(baselinePath, json + "\n");

        Console.WriteLine($"Saved baseline to: {baselinePath}");
        Console.WriteLine($"  total: {totalT[mid]:F2}ms  commit: {commit}  date: {date}");
        return 0;
    }

    static double ExtractJsonDouble(string json, string key)
    {
        // Simple: find "key": value
        int idx = json.IndexOf($"\"{key}\"");
        if (idx < 0) return 0;
        int colon = json.IndexOf(':', idx);
        if (colon < 0) return 0;
        int start = colon + 1;
        while (start < json.Length && (json[start] == ' ' || json[start] == '\t')) start++;
        int end = start;
        while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '.' || json[end] == '-')) end++;
        return double.TryParse(json[start..end], System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : 0;
    }

    static int RunDumpSource(string? outputPath)
    {
        string codexDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Codex.Codex"));
        string[] files = Directory.GetFiles(codexDir, "*.codex", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.Ordinal).ToArray();
        List<string> codeBlocks = [];
        foreach (string f in files)
        {
            string rel = Path.GetRelativePath(codexDir, f);
            string moduleName = Path.GetFileNameWithoutExtension(f);
            string slug = ToModuleSlug(moduleName);

            string content = File.ReadAllText(f);
            string code;
            if (IsProseDocument(content))
            {
                code = ExtractCodeBlocks(content);
            }
            else
            {
                code = content;
            }
            if (code.Length > 0)
                codeBlocks.Add($"module: {slug}\n\n{code}");
        }
        string combined = string.Join("\n\n", codeBlocks);
        string dest = outputPath ?? Path.Combine(Path.GetTempPath(), "codex-all-source.codex");
        File.WriteAllText(dest, combined);
        Console.WriteLine($"Wrote {combined.Length} chars ({files.Length} files) to {dest}");
        return 0;
    }

    static string ToModuleSlug(string fileName)
    {
        // "CSharpEmitter" -> "csharp-emitter", "X86_64" -> "x86-64"
        var sb = new System.Text.StringBuilder(fileName.Length + 4);
        for (int i = 0; i < fileName.Length; i++)
        {
            char c = fileName[i];
            if (c == '_')
            {
                if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
            }
            else if (char.IsUpper(c))
            {
                if (i > 0 && char.IsLower(fileName[i - 1]) && sb.Length > 0 && sb[^1] != '-')
                    sb.Append('-');
                sb.Append(char.ToLowerInvariant(c));
            }
            else if (char.IsLetterOrDigit(c) || c == '-')
            {
                sb.Append(c);
            }
        }
        while (sb.Length > 0 && sb[^1] == '-') sb.Length--;
        return sb.ToString();
    }

    static int RunCodexEmit(string? codexDirOverride, string? outputPath)
    {
        string codexDir = codexDirOverride ?? Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Codex.Codex"));
        if (!Directory.Exists(codexDir)) { Console.Error.WriteLine($"Not found: {codexDir}"); return 1; }

        string[] files = Directory.GetFiles(codexDir, "*.codex", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.Ordinal).ToArray();
        List<string> codeBlocks = [];
        foreach (string f in files)
        {
            string content = File.ReadAllText(f);
            if (IsProseDocument(content))
            {
                string code = ExtractCodeBlocks(content);
                if (code.Length > 0) codeBlocks.Add(code);
            }
            else
                codeBlocks.Add(content);
        }
        string combined = string.Join("\n\n", codeBlocks);
        string source = _Cce.FromUnicode(combined);
        Console.Error.WriteLine($"Source: {combined.Length} chars ({files.Length} files)");

        var tokens = Codex_Codex_Codex.tokenize(source);
        var st = Codex_Codex_Codex.make_parse_state(tokens);
        var doc = Codex_Codex_Codex.parse_document(st);
        var ast = Codex_Codex_Codex.desugar_document(doc, _Cce.FromUnicode("Codex_Codex"));
        Console.Error.WriteLine($"  Defs: {ast.defs.Count}, TypeDefs: {ast.type_defs.Count}");

        var checkResult = Codex_Codex_Codex.check_module(ast);
        Console.Error.WriteLine($"  Type bindings: {checkResult.types.Count}");
        Console.Error.WriteLine($"  Unification errors: {checkResult.state.errors.Count}");

        var ir = Codex_Codex_Codex.lower_module(ast, checkResult.types, checkResult.state);
        Console.Error.WriteLine($"  IR defs: {ir.defs.Count}");

        string cceOutput = Codex_Codex_Codex.codex_emitter_emit_full_module(ir, ast.type_defs);
        string output = _Cce.ToUnicode(cceOutput);

        string dest = outputPath ?? Path.Combine(Path.GetFullPath(Path.Combine(codexDir, "..")), "build-output", "stage1-codex.codex");
        File.WriteAllText(dest, output);
        Console.Error.WriteLine($"  Output: {dest} ({output.Length} chars, {output.Split('\n').Length} lines)");
        return 0;
    }
}
