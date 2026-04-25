using System.Text;
using System.Text.RegularExpressions;
using Codex.Core;

partial class Program
{
    /// <summary>
    /// Enumerates .codex files under a codex directory with quire semantics
    /// (root + one level of subdirectory) and concatenates them. Each file's
    /// `Chapter: X` header is rewritten to `Chapter: &lt;Quire&gt;--X` so the
    /// self-host parser — which sees only a token stream — can slug chapters
    /// by (quire, title) and stay byte-identical with the reference compiler.
    /// Files at the codex root (no quire) pass through unchanged.
    /// </summary>
    static string LoadCodexSourceConcatenated(string codexDir)
    {
        List<string> files = new();
        files.AddRange(Directory.GetFiles(codexDir, "*.codex", SearchOption.TopDirectoryOnly));
        foreach (string sub in Directory.GetDirectories(codexDir))
        {
            files.AddRange(Directory.GetFiles(sub, "*.codex", SearchOption.TopDirectoryOnly));
        }

        files.Sort(StringComparer.Ordinal);

        StringBuilder buf = new();
        foreach (string f in files)
        {
            string content = File.ReadAllText(f);
            if (content.Length == 0)
            {
                continue;
            }

            string quire = QuireOf(f, codexDir);
            if (quire.Length > 0)
            {
                content = Regex.Replace(content,
                    @"^(Chapter:\s*)(.+?)\s*$",
                    m => m.Groups[1].Value + quire + "--" + m.Groups[2].Value.Trim(),
                    RegexOptions.Multiline);
            }
            if (buf.Length > 0)
            {
                buf.Append("\n\n");
            }

            buf.Append(content);
        }

        string codexBody = buf.ToString();
        string forewordPrefix = LoadCitedForewordChapters(codexBody);
        return forewordPrefix.Length == 0 ? codexBody : forewordPrefix + "\n\n" + codexBody;
    }

    static string LoadCitedForewordChapters(string codexBody)
    {
        return LoadCitedForewordChapters(codexBody, out _);
    }

    static string LoadCitedForewordChapters(string codexBody, out List<string> missing)
    {
        missing = new();
        string? forewordDir = FindForewordDirectory();
        if (forewordDir is null)
        {
            return "";
        }

        Set<string> cited = Set<string>.s_empty;
        foreach (Match m in Regex.Matches(codexBody,
            @"^\s*cites\s+Foreword\s+chapter\s+([A-Za-z_][A-Za-z0-9_-]*)",
            RegexOptions.Multiline))
        {
            cited = cited.Add(m.Groups[1].Value);
        }

        if (cited.Count == 0)
        {
            return "";
        }

        List<string> ordered = new(cited);
        ordered.Sort(StringComparer.Ordinal);

        StringBuilder buf = new();
        foreach (string name in ordered)
        {
            string path = Path.Combine(forewordDir, name + ".codex");
            if (!File.Exists(path))
            {
                missing.Add(name);
                continue;
            }

            string content = File.ReadAllText(path);
            if (content.Length == 0)
            {
                continue;
            }

            content = Regex.Replace(content,
                @"^(Chapter:\s*)(.+?)\s*$",
                m => m.Groups[1].Value + "Foreword--" + m.Groups[2].Value.Trim(),
                RegexOptions.Multiline);

            if (buf.Length > 0)
            {
                buf.Append("\n\n");
            }

            buf.Append(content);
        }

        return buf.ToString();
    }

    static string? FindForewordDirectory()
    {
        string? dir = Path.GetDirectoryName(typeof(Program).Assembly.Location);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir, "foreword");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = Path.GetDirectoryName(dir);
        }

        string cwdCandidate = Path.Combine(Directory.GetCurrentDirectory(), "foreword");
        if (Directory.Exists(cwdCandidate))
        {
            return cwdCandidate;
        }

        return null;
    }

    static string QuireOf(string filePath, string codexRoot)
    {
        string full = Path.GetFullPath(filePath);
        string fullRoot = Path.GetFullPath(codexRoot);
        string rel = Path.GetRelativePath(fullRoot, full);
        int sep = rel.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
        return sep < 0 ? "" : rel.Substring(0, sep);
    }

    static int Main(string[] args)
    {
        int exitCode = 1;
        Thread thread = new(() => exitCode = Run(args), 256 * 1024 * 1024);
        thread.Start();
        thread.Join();
        return exitCode;
    }

    static int Run(string[] args)
    {
        if (args.Length > 0 && args[0] == "--mini" && args.Length > 1)
        {
            return RunMini(args[1]);
        }

        if (args.Length > 0 && args[0] == "--bench")
        {
            return RunBench(args.Length > 1 ? args[1] : null);
        }

        if (args.Length > 0 && args[0] == "--bench-check")
        {
            return RunBenchCheck(args.Length > 1 ? args[1] : null);
        }

        if (args.Length > 0 && args[0] == "--bench-save")
        {
            return RunBenchSave(args.Length > 1 ? args[1] : null);
        }

        if (args.Length > 0 && args[0] == "--dump-source")
        {
            return RunDumpSource(args.Length > 1 ? args[1] : null);
        }

        if (args.Length > 0 && args[0] == "--codex-emit")
        {
            return RunCodexEmit(args.Length > 1 ? args[1] : null, args.Length > 2 ? args[2] : null);
        }

        if (args.Length > 0 && args[0] == "--scan-test" && args.Length > 1)
        {
            return RunScanTest(args[1]);
        }

        if (args.Length > 0 && args[0] == "--binary")
        {
            if (!TryParseExitMode(args, out X86_64ExitMode binaryMode)) { return 1; }
            if (!TryParseWatchdogMode(args, out X86_64WatchdogMode binaryWd)) { return 1; }
            string? outputPath = args.Length > 1 && !args[1].StartsWith("--") ? args[1] : null;
            return RunBinaryEmit(outputPath, binaryMode, binaryWd);
        }

        if (args.Length >= 3 && args[0] == "--emit-sample")
        {
            return RunEmitSample(args[1], args[2]);
        }

        if (args.Length >= 3 && args[0] == "--binary-sample")
        {
            if (!TryParseExitMode(args, out X86_64ExitMode sampleMode)) { return 1; }
            if (!TryParseWatchdogMode(args, out X86_64WatchdogMode sampleWd)) { return 1; }
            return RunBinarySample(args[1], args[2], sampleMode, sampleWd);
        }

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

        string combined = LoadCodexSourceConcatenated(codexDir);

        Console.WriteLine($"Total source after prose extraction: {combined.Length} chars");
        Console.WriteLine("Compiling with Codex.Codex (Stage 1)...");

        // Convert source from Unicode to CCE at the boundary
        string cceCombined = _Cce.FromUnicode(combined);


        try
        {
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            string chapterCce = _Cce.FromUnicode("Codex_Codex");

            Console.WriteLine("  compile_text (CtCSharp)...");
            CompileTextResult result = Codex_Codex_Codex.compile_text(cceCombined, chapterCce, new CtCSharp());
            Console.WriteLine($"         {sw.ElapsedMilliseconds}ms");

            if (result.bag.diagnostics.Count > 0)
            {
                Console.WriteLine($"Diagnostics: {result.bag.diagnostics.Count}");
                foreach (Diagnostic diag in result.bag.diagnostics)
                {
                    Console.WriteLine($"  [{diag.code}] {_Cce.ToUnicode(diag.message)} @ ({diag.span.start.line}:{diag.span.start.column})");
                }
            }

            if (Codex_Codex_Codex.bag_has_errors(result.bag))
            {
                Console.Error.WriteLine("Compilation failed; no output emitted.");
                return 1;
            }

            string output = _Cce.ToUnicode(result.text);
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
            List<Token> tokens = Codex_Codex_Codex.tokenize(cceSource, 1L).tokens;
            ParseState st = Codex_Codex_Codex.make_parse_state(tokens);
            Document doc = Codex_Codex_Codex.parse_document(st);
            AChapter ast = Codex_Codex_Codex.desugar_document(doc, _Cce.FromUnicode("MiniTest"));

            Console.WriteLine($"  Parse errors: {doc.parse_bag.diagnostics.Count}");
            for (int pi = 0; pi < doc.parse_bag.diagnostics.Count; pi++)
            {
                Diagnostic d = doc.parse_bag.diagnostics[pi];
                Console.WriteLine($"    P{pi}: [{d.code}] {d.message} @ ({d.span.start.line}:{d.span.start.column})");
            }
            Console.WriteLine($"  Defs: {ast.defs.Count}, TypeDefs: {ast.type_defs.Count}");

            ChapterResult checkResult = Codex_Codex_Codex.check_chapter(ast);
            Console.WriteLine($"  Type bindings: {checkResult.types.Count}");
            Console.WriteLine($"  Unification errors: {checkResult.state.bag.diagnostics.Count}");

            for (int i = 0; i < checkResult.types.Count; i++)
            {
                TypeBinding tb = checkResult.types[i];
                CodexType resolved = Codex_Codex_Codex.deep_resolve(checkResult.state, tb.bound_type);
                string csType = _Cce.ToUnicode(Codex_Codex_Codex.cs_type(resolved));
                string name = _Cce.ToUnicode(tb.name);
                bool isErr = resolved is ErrorTy;
                Console.WriteLine($"    {name} : {csType}{(isErr ? " [ERRORTY]" : "")}");
            }

            for (int ei = 0; ei < checkResult.state.bag.diagnostics.Count; ei++)
            {
                Diagnostic diag = checkResult.state.bag.diagnostics[ei];
                string msg = _Cce.ToUnicode(diag.message);
                Console.WriteLine($"  ERR {ei}: [{diag.code}] {msg} @ ({diag.span.start.line}:{diag.span.start.column})");
            }

            ResolveResult resolveResult = Codex_Codex_Codex.resolve_chapter(ast);
            IRChapter ir = Codex_Codex_Codex.lower_chapter(ast, checkResult.types, checkResult.env, checkResult.state, resolveResult.ctor_names);
            string output = Codex_Codex_Codex.emit_csharp_text_chapter(ir, ast.type_defs);

            string outPath = Path.ChangeExtension(filePath, ".g.cs");
            string outputUnicode = _Cce.ToUnicode(output);
            File.WriteAllText(outPath, outputUnicode);
            Console.WriteLine($"  Output: {outPath} ({outputUnicode.Length} chars)");

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

        // Load source once (quire-aware concatenation)
        string source = _Cce.FromUnicode(LoadCodexSourceConcatenated(codexDir));

        Console.WriteLine($"Benchmark: {source.Length} chars");
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
        System.Diagnostics.Stopwatch total = System.Diagnostics.Stopwatch.StartNew();

        System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
        List<Token> tokens = Codex_Codex_Codex.tokenize(source, 1L).tokens;
        sw.Stop(); lexMs = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        ParseState pst = Codex_Codex_Codex.make_parse_state(tokens);
        Document doc = Codex_Codex_Codex.parse_document(pst);
        sw.Stop(); parseMs = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        AChapter ast = Codex_Codex_Codex.desugar_document(doc, _Cce.FromUnicode("Bench"));
        sw.Stop(); desugarMs = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        ResolveResult resolved = Codex_Codex_Codex.resolve_chapter(ast);
        sw.Stop(); resolveMs = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        ChapterResult checkResult = Codex_Codex_Codex.check_chapter(ast);
        sw.Stop(); checkMs = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        IRChapter ir = Codex_Codex_Codex.lower_chapter(ast, checkResult.types, checkResult.env, checkResult.state, resolved.ctor_names);
        sw.Stop(); lowerMs = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        string output = Codex_Codex_Codex.emit_csharp_text_chapter(ir, ast.type_defs);
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
        ValueMap<string, double> baselineStages = ValueMap<string, double>.s_empty;
        foreach (string stage in new[] { "lex", "parse", "desugar", "resolve", "typecheck", "lower", "emit" })
        {
            baselineStages = baselineStages.Set(stage, ExtractJsonDouble(json, stage));
        }

        Console.WriteLine($"Baseline: {baselineMs:F2}ms (threshold: {threshold}%)");
        Console.WriteLine();

        // Run benchmark (same protocol as --bench)
        string codexDir = codexDirOverride ?? Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Codex.Codex"));
        if (!Directory.Exists(codexDir)) { Console.Error.WriteLine($"Not found: {codexDir}"); return 1; }

        string source = _Cce.FromUnicode(LoadCodexSourceConcatenated(codexDir));

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

        ValueMap<string, double> current = ValueMap<string, double>.Of(
            ("lex", lexT[mid]),
            ("parse", parseT[mid]),
            ("desugar", desugarT[mid]),
            ("resolve", resolveT[mid]),
            ("typecheck", checkT[mid]),
            ("lower", lowerT[mid]),
            ("emit", emitT[mid]));

        Console.WriteLine("Stage        Baseline    Current     Delta");
        Console.WriteLine("───────────  ──────────  ──────────  ──────────");
        foreach (string stage in new[] { "lex", "parse", "desugar", "resolve", "typecheck", "lower", "emit" })
        {
            double b = baselineStages.Get(stage, 0.0), c = current.Get(stage, 0.0);
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

        string source = _Cce.FromUnicode(LoadCodexSourceConcatenated(codexDir));

        Console.WriteLine($"Benchmark: {source.Length} chars");
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
            System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo("git", "rev-parse --short HEAD")
            { RedirectStandardOutput = true, UseShellExecute = false };
            System.Diagnostics.Process? proc = System.Diagnostics.Process.Start(psi);
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
        if (idx < 0)
        {
            return 0;
        }

        int colon = json.IndexOf(':', idx);
        if (colon < 0)
        {
            return 0;
        }

        int start = colon + 1;
        while (start < json.Length && (json[start] == ' ' || json[start] == '\t'))
        {
            start++;
        }

        int end = start;
        while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '.' || json[end] == '-'))
        {
            end++;
        }

        return double.TryParse(json[start..end], System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : 0;
    }

    static int RunDumpSource(string? outputPath)
    {
        string codexDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Codex.Codex"));
        string combined = LoadCodexSourceConcatenated(codexDir);
        string dest = outputPath ?? Path.Combine(Path.GetTempPath(), "codex-all-source.codex");
        File.WriteAllText(dest, combined);
        Console.WriteLine($"Wrote {combined.Length} chars to {dest}");
        return 0;
    }

    static int RunCodexEmit(string? codexDirOverride, string? outputPath)
    {
        string codexDir = codexDirOverride ?? Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Codex.Codex"));
        if (!Directory.Exists(codexDir)) { Console.Error.WriteLine($"Not found: {codexDir}"); return 1; }

        string combined = LoadCodexSourceConcatenated(codexDir);
        string source = _Cce.FromUnicode(combined);
        Console.Error.WriteLine($"Source: {combined.Length} chars");

        CompileTextResult result = Codex_Codex_Codex.compile_text(source, _Cce.FromUnicode("Codex_Codex"), new CtCodexText());

        if (result.bag.diagnostics.Count > 0)
        {
            Console.Error.WriteLine($"  Diagnostics: {result.bag.diagnostics.Count}");
            foreach (Diagnostic diag in result.bag.diagnostics)
            {
                Console.Error.WriteLine($"    [{diag.code}] {_Cce.ToUnicode(diag.message)} @ ({diag.span.start.line}:{diag.span.start.column})");
            }
        }

        if (Codex_Codex_Codex.bag_has_errors(result.bag))
        {
            Console.Error.WriteLine("Compilation failed; no output emitted.");
            return 1;
        }

        string output = _Cce.ToUnicode(result.text);
        string dest = outputPath ?? Path.Combine(Path.GetFullPath(Path.Combine(codexDir, "..")), "build-output", "bootstrap", "stage1-codex.codex");
        File.WriteAllText(dest, output);
        Console.Error.WriteLine($"  Output: {dest} ({output.Length} chars, {output.Split('\n').Length} lines)");
        return 0;
    }

    static int RunScanTest(string filePath)
    {
        string source = File.ReadAllText(filePath);
        string cceSrc = _Cce.FromUnicode(source);
        List<Token> tokens = Codex_Codex_Codex.tokenize(cceSrc, 1L).tokens;

        // Test scan_document
        ParseState st = Codex_Codex_Codex.make_parse_state(tokens);
        ScanResult scan = Codex_Codex_Codex.scan_document(st);
        Console.WriteLine($"scan_document: type_defs={scan.type_defs.Count}, def_headers={scan.def_headers.Count}");

        // Test parse_document for comparison
        ParseState st2 = Codex_Codex_Codex.make_parse_state(tokens);
        Document doc = Codex_Codex_Codex.parse_document(st2);
        AChapter ast = Codex_Codex_Codex.desugar_document(doc, _Cce.FromUnicode("Test"));
        Console.WriteLine($"parse_document: type_defs={ast.type_defs.Count}, defs={ast.defs.Count}");

        // Show first few type def names from scan
        for (int i = 0; i < Math.Min(scan.type_defs.Count, 10); i++)
        {
            TypeDef td = scan.type_defs[i];
            Console.WriteLine($"  scan td[{i}]: {_Cce.ToUnicode(td.name.text)}");
        }

        return 0;
    }

    // Compile a single sample file through the self-host and emit C# to the
    // given output path. Exits 0 iff no diagnostics with error severity were
    // produced. On failure, prints each diagnostic to stderr in the same
    // format ref-sweep.sh scans (`error <code>: <message>`) so .failing
    // sidecars can match against this tool's output too.
    static int RunEmitSample(string srcPath, string outPath)
    {
        if (!File.Exists(srcPath))
        {
            Console.Error.WriteLine($"error: file not found: {srcPath}");
            return 1;
        }
        string source = File.ReadAllText(srcPath);
        string cceSource = _Cce.FromUnicode(source);
        string chapterCce = _Cce.FromUnicode(Path.GetFileNameWithoutExtension(srcPath));
        CompileTextResult result = Codex_Codex_Codex.compile_source(cceSource, chapterCce, new CtCSharp());
        int errCount = 0;
        long sevError = Codex_Codex_Codex.sev_error();
        long sevWarning = Codex_Codex_Codex.sev_warning();
        foreach (Diagnostic d in result.bag.diagnostics)
        {
            string msg = _Cce.ToUnicode(d.message);
            string severity = d.severity == sevError ? "error"
                            : d.severity == sevWarning ? "warning" : "info";
            Console.Error.WriteLine($"{severity} {d.code}: {msg} {srcPath} ({d.span.start.line}:{d.span.start.column})");
            if (d.severity == sevError) { errCount++; }
        }
        if (errCount > 0 || Codex_Codex_Codex.bag_has_errors(result.bag))
        {
            return 1;
        }
        string output = _Cce.ToUnicode(result.text);
        File.WriteAllText(outPath, output);
        return 0;
    }

    // Parse --exit-mode=<repl|qemu-exit|acpi-s5> from args (optional; defaults
    // to Repl). Writes error to stderr on unknown value and returns false.
    static bool TryParseExitMode(string[] args, out X86_64ExitMode mode)
    {
        mode = new ExitRepl();
        foreach (string a in args)
        {
            if (!a.StartsWith("--exit-mode=", StringComparison.Ordinal)) { continue; }
            string v = a.Substring("--exit-mode=".Length);
            switch (v)
            {
                case "repl": mode = new ExitRepl(); return true;
                case "qemu-exit": mode = new ExitQemuExit(); return true;
                case "acpi-s5": mode = new ExitAcpiS5(); return true;
                default:
                    Console.Error.WriteLine($"error: unknown --exit-mode value '{v}'; expected repl|qemu-exit|acpi-s5");
                    return false;
            }
        }
        return true;
    }

    // Parse --watchdog=<progress|pet> from args (optional; defaults to Progress).
    static bool TryParseWatchdogMode(string[] args, out X86_64WatchdogMode mode)
    {
        mode = new WatchdogProgress();
        foreach (string a in args)
        {
            if (!a.StartsWith("--watchdog=", StringComparison.Ordinal)) { continue; }
            string v = a.Substring("--watchdog=".Length);
            switch (v)
            {
                case "progress": mode = new WatchdogProgress(); return true;
                case "pet": mode = new WatchdogPet(); return true;
                default:
                    Console.Error.WriteLine($"error: unknown --watchdog value '{v}'; expected progress|pet");
                    return false;
            }
        }
        return true;
    }

    // Compile a single sample file through the self-host's x86-64 bare-metal
    // backend and write the resulting ELF. Mirrors RunEmitSample's diagnostic
    // format so ref-sweep.sh's .failing sidecars apply here too.
    static int RunBinarySample(string srcPath, string outPath, X86_64ExitMode exitMode, X86_64WatchdogMode watchdogMode)
    {
        if (!File.Exists(srcPath))
        {
            Console.Error.WriteLine($"error: file not found: {srcPath}");
            return 1;
        }
        string source = File.ReadAllText(srcPath);
        string forewordPrefix = LoadCitedForewordChapters(source, out List<string> missingForewords);
        if (missingForewords.Count > 0)
        {
            foreach (string name in missingForewords)
            {
                Console.Error.WriteLine(
                    $"error 3010: Cited foreword chapter '{name}' not found (expected foreword/{name}.codex) {srcPath} (1:1)");
            }
            return 1;
        }
        string fullSource = forewordPrefix.Length == 0 ? source : forewordPrefix + "\n\n" + source;
        string cceSource = _Cce.FromUnicode(fullSource);
        string chapterCce = _Cce.FromUnicode(Path.GetFileNameWithoutExtension(srcPath));
        EmitChapterResult result = Codex_Codex_Codex.compile_to_binary_with_options(cceSource, chapterCce, exitMode, watchdogMode);
        int errCount = 0;
        long sevError = Codex_Codex_Codex.sev_error();
        long sevWarning = Codex_Codex_Codex.sev_warning();
        foreach (Diagnostic d in result.bag.diagnostics)
        {
            string msg = _Cce.ToUnicode(d.message);
            string severity = d.severity == sevError ? "error"
                            : d.severity == sevWarning ? "warning" : "info";
            Console.Error.WriteLine($"{severity} {d.code}: {msg} {srcPath} ({d.span.start.line}:{d.span.start.column})");
            if (d.severity == sevError) { errCount++; }
        }
        if (errCount > 0 || Codex_Codex_Codex.bag_has_errors(result.bag))
        {
            return 1;
        }
        List<long> bytes = result.bytes;
        byte[] elfBytes = new byte[bytes.Count];
        for (int i = 0; i < bytes.Count; i++)
        {
            elfBytes[i] = (byte)bytes[i];
        }
        string? dir = Path.GetDirectoryName(Path.GetFullPath(outPath));
        if (!string.IsNullOrEmpty(dir)) { Directory.CreateDirectory(dir); }
        File.WriteAllBytes(outPath, elfBytes);
        return 0;
    }

    static int RunBinaryEmit(string? outputPath, X86_64ExitMode exitMode, X86_64WatchdogMode watchdogMode)
    {
        string codexDir = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Codex.Codex"));
        if (!Directory.Exists(codexDir)) { Console.Error.WriteLine($"Not found: {codexDir}"); return 1; }

        string combined = LoadCodexSourceConcatenated(codexDir);
        string source = _Cce.FromUnicode(combined);
        string chapterName = _Cce.FromUnicode("Codex_Codex");

        Console.WriteLine($"Binary emit: {combined.Length} chars");
        Console.WriteLine();

        System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            PerfCounters.Reset();
            Console.WriteLine("  compile_to_binary...");
            EmitChapterResult result = Codex_Codex_Codex.compile_to_binary_with_options(source, chapterName, exitMode, watchdogMode);
            Console.WriteLine($"  done: {sw.ElapsedMilliseconds}ms");
            PerfCounters.Report();

            List<Diagnostic> errors = result.bag.diagnostics;
            if (errors.Count > 0)
            {
                Console.WriteLine($"  {errors.Count} error(s):");
                for (int i = 0; i < Math.Min(errors.Count, 20); i++)
                {
                    Console.WriteLine($"    [{errors[i].code}] {_Cce.ToUnicode(errors[i].message)}");
                }
            }

            List<long> bytes = result.bytes;
            Console.WriteLine($"  ELF size: {bytes.Count} bytes");

            string dest = outputPath ?? Path.Combine(
                Path.GetFullPath(Path.Combine(codexDir, "..")),
                "build-output", "bare-metal", "selfhost.elf");
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

            byte[] elfBytes = new byte[bytes.Count];
            for (int i = 0; i < bytes.Count; i++)
            {
                elfBytes[i] = (byte)bytes[i];
            }

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
