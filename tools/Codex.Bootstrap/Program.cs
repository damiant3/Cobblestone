using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

partial class Program
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
        if (args.Length > 0 && args[0] == "--scan-test" && args.Length > 1)
            return RunScanTest(args[1]);
        if (args.Length > 0 && args[0] == "--binary")
            return RunBinaryEmit(args.Length > 1 ? args[1] : null);

        bool verbose = args.Contains("--verbose");
        string[] posArgs = args.Where(a => !a.StartsWith("--")).ToArray();
        string codexDir = posArgs.Length > 0 ? posArgs[0] : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Codex.Codex"));
        string? outputOverride = posArgs.Length > 1 ? posArgs[1] : null;

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

            if (content.Length > 0)
                codeBlocks.Add(content);
        }

        string combined = string.Join("\n\n", codeBlocks);

        Console.WriteLine($"Total source after prose extraction: {combined.Length} chars");
        Console.WriteLine("Compiling with Codex.Codex (Stage 1)...");

        // Convert source from Unicode to CCE at the boundary
        string cceCombined = _Cce.FromUnicode(combined);


        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string chapterCce = _Cce.FromUnicode("Codex_Codex");

            Console.WriteLine("  [1/11] tokenize...");
            var tokens = Codex_Codex_Codex.tokenize(cceCombined);
            Console.WriteLine($"         {sw.ElapsedMilliseconds}ms — {tokens.Count} tokens");

            Console.WriteLine("  [2/11] make_parse_state...");
            var ps = Codex_Codex_Codex.make_parse_state(tokens);
            Console.WriteLine($"         {sw.ElapsedMilliseconds}ms");

            Console.WriteLine("  [3/11] scan_document...");
            var scan = Codex_Codex_Codex.scan_document(ps);
            Console.WriteLine($"         {sw.ElapsedMilliseconds}ms");

            Console.WriteLine("  [4/11] build_all_assignments...");
            var assignments = Codex_Codex_Codex.build_all_assignments(scan.def_headers, 0L, new List<ChapterAssignment>());
            Console.WriteLine($"         {sw.ElapsedMilliseconds}ms");

            Console.WriteLine("  [5/11] find_colliding_names...");
            var colliding = Codex_Codex_Codex.find_colliding_names(assignments);
            Console.WriteLine($"         {sw.ElapsedMilliseconds}ms");

            Console.WriteLine("  [6/11] parse_document...");
            var doc = Codex_Codex_Codex.parse_document(Codex_Codex_Codex.make_parse_state(tokens));
            Console.WriteLine($"         {sw.ElapsedMilliseconds}ms");

            Console.WriteLine("  [7/11] desugar_document...");
            var ast = Codex_Codex_Codex.desugar_document(doc, chapterCce);
            Console.WriteLine($"         {sw.ElapsedMilliseconds}ms");

            Console.WriteLine("  [8/11] scope_achapter...");
            var scoped = Codex_Codex_Codex.scope_achapter(ast, colliding, assignments);
            Console.WriteLine($"         {sw.ElapsedMilliseconds}ms");

            Console.WriteLine("  [9/11] check_chapter...");
            var checkResult = Codex_Codex_Codex.check_chapter(scoped);
            Console.WriteLine($"         {sw.ElapsedMilliseconds}ms");

            Console.WriteLine("  [10/11] lower_chapter...");
            var ir = Codex_Codex_Codex.lower_chapter(scoped, checkResult.types, checkResult.state);
            Console.WriteLine($"         {sw.ElapsedMilliseconds}ms");

            Console.WriteLine("  [11/11] csharp_emitter_emit_full_chapter...");
            string cceOutput = Codex_Codex_Codex.csharp_emitter_emit_full_chapter(ir, scoped.type_defs);
            Console.WriteLine($"         {sw.ElapsedMilliseconds}ms");

            string output = _Cce.ToUnicode(cceOutput);
            string outputPath = outputOverride ?? Path.Combine(Path.GetFullPath(Path.Combine(codexDir, "..")), "build-output", "bootstrap", "stage1-output.cs");
            File.WriteAllText(outputPath, output);
            Console.WriteLine($"Output written to: {outputPath}");
            Console.WriteLine($"Output size: {output.Length} chars");
            Console.WriteLine($"Total: {sw.ElapsedMilliseconds}ms");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Compilation failed: {ex.GetType().Name}: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    static int RunMini(string filePath)
    {
        if (!File.Exists(filePath))
        {
            Console.Error.WriteLine($"File not found: {filePath}");
            return 1;
        }

        string source = File.ReadAllText(filePath);
        Console.WriteLine($"Mini compile: {filePath} ({source.Length} chars)");

        try
        {
            string cceSource = _Cce.FromUnicode(source);
            List<Token> tokens = Codex_Codex_Codex.tokenize(cceSource);
            ParseState st = Codex_Codex_Codex.make_parse_state(tokens);
            Document doc = Codex_Codex_Codex.parse_document(st);
            AChapter ast = Codex_Codex_Codex.desugar_document(doc, _Cce.FromUnicode("MiniTest"));

            Console.WriteLine($"  Defs: {ast.defs.Count}, TypeDefs: {ast.type_defs.Count}");

            ChapterResult checkResult = Codex_Codex_Codex.check_chapter(ast);
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

            IRChapter ir = Codex_Codex_Codex.lower_chapter(ast, checkResult.types, checkResult.state);
            string output = Codex_Codex_Codex.csharp_emitter_emit_full_chapter(ir, ast.type_defs);

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
            codeBlocks.Add(content);
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
        var resolved = Codex_Codex_Codex.resolve_chapter(ast);
        sw.Stop(); resolveMs = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        var checkResult = Codex_Codex_Codex.check_chapter(ast);
        sw.Stop(); checkMs = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        var ir = Codex_Codex_Codex.lower_chapter(ast, checkResult.types, checkResult.state);
        sw.Stop(); lowerMs = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        var output = Codex_Codex_Codex.csharp_emitter_emit_full_chapter(ir, ast.type_defs);
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
            codeBlocks.Add(content);
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
            codeBlocks.Add(content);
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
            string content = File.ReadAllText(f);
            if (content.Length > 0)
                codeBlocks.Add(content);
        }
        string combined = string.Join("\n\n", codeBlocks);
        string dest = outputPath ?? Path.Combine(Path.GetTempPath(), "codex-all-source.codex");
        File.WriteAllText(dest, combined);
        Console.WriteLine($"Wrote {combined.Length} chars ({files.Length} files) to {dest}");
        return 0;
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
            if (content.Length > 0)
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

        var checkResult = Codex_Codex_Codex.check_chapter(ast);
        Console.Error.WriteLine($"  Type bindings: {checkResult.types.Count}");
        Console.Error.WriteLine($"  Unification errors: {checkResult.state.errors.Count}");

        var ir = Codex_Codex_Codex.lower_chapter(ast, checkResult.types, checkResult.state);
        Console.Error.WriteLine($"  IR defs: {ir.defs.Count}");

        string cceOutput = Codex_Codex_Codex.codex_emitter_emit_full_chapter(ir, ast.type_defs);
        string output = _Cce.ToUnicode(cceOutput);

        string dest = outputPath ?? Path.Combine(Path.GetFullPath(Path.Combine(codexDir, "..")), "build-output", "bootstrap", "stage1-codex.codex");
        File.WriteAllText(dest, output);
        Console.Error.WriteLine($"  Output: {dest} ({output.Length} chars, {output.Split('\n').Length} lines)");
        return 0;
    }

    static int RunScanTest(string filePath)
    {
        string source = File.ReadAllText(filePath);
        string cceSrc = _Cce.FromUnicode(source);
        var tokens = Codex_Codex_Codex.tokenize(cceSrc);

        // Test scan_document
        var st = Codex_Codex_Codex.make_parse_state(tokens);
        var scan = Codex_Codex_Codex.scan_document(st);
        Console.WriteLine($"scan_document: type_defs={scan.type_defs.Count}, def_headers={scan.def_headers.Count}");

        // Test parse_document for comparison
        var st2 = Codex_Codex_Codex.make_parse_state(tokens);
        var doc = Codex_Codex_Codex.parse_document(st2);
        var ast = Codex_Codex_Codex.desugar_document(doc, _Cce.FromUnicode("Test"));
        Console.WriteLine($"parse_document: type_defs={ast.type_defs.Count}, defs={ast.defs.Count}");

        // Show first few type def names from scan
        for (int i = 0; i < Math.Min(scan.type_defs.Count, 10); i++)
        {
            var td = scan.type_defs[i];
            Console.WriteLine($"  scan td[{i}]: {_Cce.ToUnicode(td.name.text)}");
        }

        return 0;
    }

    static int RunBinaryEmit(string? outputPath)
    {
        string codexDir = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Codex.Codex"));
        if (!Directory.Exists(codexDir)) { Console.Error.WriteLine($"Not found: {codexDir}"); return 1; }

        string[] files = Directory.GetFiles(codexDir, "*.codex", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.Ordinal).ToArray();
        List<string> codeBlocks = [];
        foreach (string f in files)
        {
            string content = File.ReadAllText(f);
            if (content.Length > 0)
                codeBlocks.Add(content);
        }
        string combined = string.Join("\n\n", codeBlocks);
        string source = _Cce.FromUnicode(combined);
        string chapterName = _Cce.FromUnicode("Codex_Codex");

        Console.WriteLine($"Binary emit: {combined.Length} chars ({files.Length} files)");
        Console.WriteLine();

        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            Console.WriteLine("  compile_to_binary...");
            var result = Codex_Codex_Codex.compile_to_binary(source, chapterName);
            Console.WriteLine($"  done: {sw.ElapsedMilliseconds}ms");

            var errors = result.errors;
            if (errors.Count > 0)
            {
                Console.WriteLine($"  {errors.Count} error(s):");
                for (int i = 0; i < Math.Min(errors.Count, 20); i++)
                    Console.WriteLine($"    [{errors[i].code}] {_Cce.ToUnicode(errors[i].message)}");
            }

            var bytes = result.bytes;
            Console.WriteLine($"  ELF size: {bytes.Count} bytes");

            string dest = outputPath ?? Path.Combine(
                Path.GetFullPath(Path.Combine(codexDir, "..")),
                "build-output", "bare-metal", "selfhost.elf");
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

            byte[] elfBytes = new byte[bytes.Count];
            for (int i = 0; i < bytes.Count; i++)
                elfBytes[i] = (byte)bytes[i];
            File.WriteAllBytes(dest, elfBytes);

            Console.WriteLine($"  Output: {dest}");
            Console.WriteLine($"  Total: {sw.ElapsedMilliseconds}ms");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Binary compilation failed at {sw.ElapsedMilliseconds}ms: {ex.GetType().Name}: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }
}
