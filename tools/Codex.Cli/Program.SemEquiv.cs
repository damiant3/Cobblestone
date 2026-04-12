namespace Codex.Cli;

using System.Text;
using System.Text.RegularExpressions;

public static partial class Program
{
    record SemDef(string Name, string Chapter, string Sig, string Body, int LineNo, bool IsType);

    record Stage0Stats(int Chapters, int Sections, int ProseLines, int Cites);

    static int RunSemEquiv(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: codex sem-equiv <stage0.codex> <stage1.codex> [--show <name>]");
            return 1;
        }

        string? showDef = null;
        for (int ai = 2; ai < args.Length; ai++)
        {
            if (args[ai] == "--show" && ai + 1 < args.Length)
                showDef = args[++ai];
        }

        string stage0Text = File.ReadAllText(args[0]).Replace("\r\n", "\n");
        string stage1Text = File.ReadAllText(args[1]).Replace("\r\n", "\n");

        var (stage0Chapters, chapterSlugs, stats) = ParseStage0(stage0Text);
        List<SemDef> stage1Defs = ParseStage1(stage1Text);

        HashSet<string> collidingNames = BuildCollisionSet(stage0Chapters);
        string[] slugsSorted = chapterSlugs.OrderByDescending(s => s.Length).ToArray();

        AssignChapters(stage1Defs, stage0Chapters, collidingNames, slugsSorted);

        var (matched, dropped, extra) = MatchDefs(stage0Chapters, stage1Defs);

        // Second pass: match colliding names by body comparison.
        // Stage1 emits colliding defs unmangled (no chapter prefix), so they land in "?".
        // Match each dropped stage0 def to an extra stage1 def with the same base name.
        ResolveCollidingDefs(matched, dropped, extra, slugsSorted);

        int bodyMatches = 0;
        int bodyMismatches = 0;
        int sigMatches = 0;
        int sigMismatches = 0;
        var bodyMismatchList = new List<(SemDef s0, SemDef s1, string diff)>();
        var sigMismatchList = new List<(SemDef s0, SemDef s1)>();

        foreach (var (s0, s1) in matched)
        {
            string normSig0 = AlphaNormalizeTypeVars(s0.Sig);
            string normSig1 = AlphaNormalizeTypeVars(s1.Sig);
            if (normSig0 == normSig1)
                sigMatches++;
            else
            {
                sigMismatches++;
                sigMismatchList.Add((s0, s1));
            }

            string body0 = CollapseWhitespace(DemangleNames(s0.Body, slugsSorted));
            string body1 = CollapseWhitespace(DemangleNames(s1.Body, slugsSorted));
            if (body0 == body1)
                bodyMatches++;
            else
            {
                bodyMismatches++;
                bodyMismatchList.Add((s0, s1, FirstDiff(body0, body1)));
            }
        }

        if (showDef != null)
        {
            ShowDefDetail(showDef, matched, slugsSorted);
            return 0;
        }

        PrintReport(stage0Chapters, stage1Defs, chapterSlugs, stats, matched, dropped, extra,
                     bodyMatches, bodyMismatches, bodyMismatchList,
                     sigMatches, sigMismatches, sigMismatchList, collidingNames);

        bool pass = dropped.Count == 0 && bodyMismatches == 0;
        Console.WriteLine();
        Console.WriteLine(pass ? "Verdict: PASS" : "Verdict: FAIL");
        return pass ? 0 : 1;
    }

    // ── Stage0: extract defs from prose structure, don't mutate ──────

    static (Dictionary<string, List<SemDef>> chapters, List<string> slugs, Stage0Stats stats)
        ParseStage0(string text)
    {
        var chapters = new Dictionary<string, List<SemDef>>();
        var slugs = new List<string>();
        string[] rawLines = text.Split('\n');
        string currentSlug = "";
        int statChapters = 0, statSections = 0, statProse = 0, statCites = 0;

        int i = 0;
        while (i < rawLines.Length)
        {
            string raw = rawLines[i];

            // Chapter header → set current chapter, track slug
            if (raw.StartsWith("Chapter: ", StringComparison.Ordinal))
            {
                string chapterName = raw[9..].Trim();
                currentSlug = chapterName.ToLowerInvariant().Replace(' ', '-');
                if (!slugs.Contains(currentSlug))
                    slugs.Add(currentSlug);
                if (!chapters.ContainsKey(currentSlug))
                    chapters[currentSlug] = new List<SemDef>();
                statChapters++;
                i++;
                continue;
            }

            // Section header → skip, count
            if (raw.StartsWith("Section: ", StringComparison.Ordinal))
            {
                statSections++;
                i++;
                continue;
            }

            // Page marker → skip
            if (raw.StartsWith("Page ", StringComparison.Ordinal))
            {
                i++;
                continue;
            }

            // Prose: exactly 1-space indent (not 2+)
            if (raw.Length >= 1 && raw[0] == ' ' && (raw.Length < 2 || raw[1] != ' '))
            {
                statProse++;
                i++;
                continue;
            }

            // 2-space indented content → extract def or skip cites/import
            if (raw.Length >= 2 && raw[0] == ' ' && raw[1] == ' ')
            {
                string dedented = raw[2..];

                if (dedented.StartsWith("cites ", StringComparison.Ordinal)
                    || dedented.StartsWith("import ", StringComparison.Ordinal))
                {
                    statCites++;
                    i++;
                    continue;
                }

                if (dedented.Length > 0 && char.IsLetter(dedented[0]))
                {
                    // Extract a def from the indented block
                    var (def, nextI) = ExtractStage0Def(rawLines, i, currentSlug);
                    if (def != null && chapters.ContainsKey(currentSlug))
                        chapters[currentSlug].Add(def);
                    i = nextI;
                    continue;
                }
            }

            // Column-0 def (flat codex emitter output, not prose-indented)
            if (raw.Length > 0 && char.IsLetter(raw[0]))
            {
                var (def, nextI) = ParseOneDef(rawLines, i, currentSlug);
                if (def != null && chapters.ContainsKey(currentSlug))
                    chapters[currentSlug].Add(def);
                i = nextI;
                continue;
            }

            // Blank lines, anything else → skip
            i++;
        }

        // Demangle chapter-prefixed names in stage0 defs (codex emitter output
        // uses prefixed names like csharp-emitter_emit-type-defs)
        string[] s0Slugs = slugs.OrderByDescending(s => s.Length).ToArray();
        foreach (var (ch, defs) in chapters)
        {
            for (int di = 0; di < defs.Count; di++)
            {
                var dm = DemangleName(defs[di].Name, s0Slugs);
                if (dm is var (slug, baseName) && slug == ch)
                    defs[di] = defs[di] with { Name = baseName };
            }
        }

        return (chapters, slugs, new Stage0Stats(statChapters, statSections, statProse, statCites));
    }

    static (SemDef? def, int nextI) ExtractStage0Def(string[] rawLines, int start, string chapter)
    {
        // Collect the indented block belonging to this def, strip 2-space prefix,
        // then parse with ParseOneDef. This is extraction from prose structure.
        var defLines = new List<string>();
        int i = start;

        while (i < rawLines.Length)
        {
            string raw = rawLines[i];

            // 2-space indented content → part of this def (strip prefix)
            if (raw.Length >= 2 && raw[0] == ' ' && raw[1] == ' ')
            {
                defLines.Add(raw[2..]);
                i++;
                continue;
            }

            // Blank line → might continue the def (check what follows)
            if (raw.Length == 0)
            {
                int peek = i + 1;
                while (peek < rawLines.Length && rawLines[peek].Length == 0)
                    peek++;

                if (peek >= rawLines.Length)
                    break;

                string next = rawLines[peek];
                // If next non-blank line is 2-space indented and looks like a continuation
                // (starts with space after dedent), it's still part of this def
                if (next.Length >= 3 && next[0] == ' ' && next[1] == ' ' && next[2] == ' ')
                {
                    defLines.Add("");
                    i++;
                    continue;
                }

                // Otherwise the blank line is a def boundary
                break;
            }

            // Anything else (chapter, section, prose, another def at same indent) → boundary
            break;
        }

        if (defLines.Count == 0)
            return (null, i);

        string[] lines = defLines.ToArray();
        var (def, _) = ParseOneDef(lines, 0, chapter);
        return (def, i);
    }

    // ── Stage1: parse flat format ───────────────────────────────────

    static List<SemDef> ParseStage1(string text)
    {
        var defs = new List<SemDef>();
        string[] lines = text.Split('\n');
        int i = 0;

        while (i < lines.Length)
        {
            string line = lines[i];

            if (Regex.IsMatch(line, @"^(HEAP|STACK):\d+"))
            {
                i++;
                continue;
            }

            if (line.Length > 0 && char.IsLetter(line[0]))
            {
                var (def, nextI) = ParseOneDef(lines, i, "");
                if (def != null)
                    defs.Add(def);
                i = nextI;
            }
            else
            {
                i++;
            }
        }

        return defs;
    }

    // ── Shared def parser (expects column-0 defs) ───────────────────

    static (SemDef? def, int nextI) ParseOneDef(string[] lines, int start, string chapter)
    {
        string firstLine = lines[start];

        bool isTypeDef = char.IsUpper(firstLine[0]);

        string name;
        string sig;

        if (isTypeDef)
        {
            int eqPos = firstLine.IndexOf(" =", StringComparison.Ordinal);
            if (eqPos < 0)
                return (null, start + 1);

            name = firstLine[..eqPos].Trim();
            sig = "";
        }
        else
        {
            int colonPos = firstLine.IndexOf(" : ", StringComparison.Ordinal);
            if (colonPos < 0)
                return (null, start + 1);

            name = firstLine[..colonPos];
            sig = firstLine[(colonPos + 3)..];
        }

        var bodyLines = new StringBuilder();
        int j;
        if (isTypeDef)
        {
            bodyLines.AppendLine(lines[start]);
            j = start + 1;
        }
        else
        {
            j = start + 1;
        }

        while (j < lines.Length)
        {
            string ln = lines[j];

            if (Regex.IsMatch(ln, @"^(HEAP|STACK):\d+"))
            {
                j++;
                continue;
            }

            if (ln.Length == 0)
            {
                int peek = j + 1;
                while (peek < lines.Length && lines[peek].Length == 0)
                    peek++;

                if (peek >= lines.Length)
                    break;

                if (lines[peek].Length > 0 && !char.IsWhiteSpace(lines[peek][0]))
                    break;

                bodyLines.AppendLine();
                j++;
                continue;
            }

            if (ln.Length > 0 && char.IsLetter(ln[0]))
            {
                if (!isTypeDef && j == start + 1 && ln.StartsWith(name, StringComparison.Ordinal))
                {
                    bodyLines.AppendLine(ln);
                    j++;
                    continue;
                }

                bool looksLikeSig = ln.Contains(" : ");
                bool looksLikeTypeDef = char.IsUpper(ln[0]) && ln.Contains(" =");

                if (isTypeDef && !looksLikeSig && !looksLikeTypeDef)
                {
                    bodyLines.AppendLine(ln);
                    j++;
                    continue;
                }

                break;
            }

            if (ln.Length > 0 && (char.IsWhiteSpace(ln[0]) || ln[0] == '|' || ln[0] == '}'))
            {
                bodyLines.AppendLine(ln);
                j++;
                continue;
            }

            break;
        }

        return (new SemDef(name, chapter, sig, bodyLines.ToString().TrimEnd(), start + 1, isTypeDef), j);
    }

    // ── Chapter resolution ──────────────────────────────────────────

    static HashSet<string> BuildCollisionSet(Dictionary<string, List<SemDef>> chapters)
    {
        var nameToChapters = new Dictionary<string, HashSet<string>>();
        foreach (var (ch, defs) in chapters)
        {
            foreach (SemDef d in defs)
            {
                if (!nameToChapters.TryGetValue(d.Name, out var chs))
                {
                    chs = [];
                    nameToChapters[d.Name] = chs;
                }
                chs.Add(ch);
            }
        }

        return nameToChapters.Where(kv => kv.Value.Count > 1).Select(kv => kv.Key).ToHashSet();
    }

    static (string slug, string baseName)? DemangleName(string name, string[] slugsSorted)
    {
        foreach (string slug in slugsSorted)
        {
            string prefix = slug + "_";
            if (name.StartsWith(prefix, StringComparison.Ordinal) && name.Length > prefix.Length)
                return (slug, name[prefix.Length..]);
        }
        return null;
    }

    static void AssignChapters(List<SemDef> stage1Defs, Dictionary<string, List<SemDef>> stage0Chapters,
                               HashSet<string> collidingNames, string[] slugsSorted)
    {
        var nameToChapter = new Dictionary<string, string>();
        foreach (var (ch, defs) in stage0Chapters)
        {
            foreach (SemDef d in defs)
            {
                if (!collidingNames.Contains(d.Name))
                    nameToChapter.TryAdd(d.Name, ch);
            }
        }

        for (int i = 0; i < stage1Defs.Count; i++)
        {
            SemDef d = stage1Defs[i];
            var demangled = DemangleName(d.Name, slugsSorted);
            if (demangled is var (slug, baseName))
            {
                stage1Defs[i] = d with { Name = baseName, Chapter = slug };
            }
            else if (nameToChapter.TryGetValue(d.Name, out string? ch))
            {
                stage1Defs[i] = d with { Chapter = ch };
            }
            else
            {
                stage1Defs[i] = d with { Chapter = "?" };
            }
        }
    }

    static (List<(SemDef s0, SemDef s1)> matched, List<SemDef> dropped, List<SemDef> extra)
        MatchDefs(Dictionary<string, List<SemDef>> stage0Chapters, List<SemDef> stage1Defs)
    {
        var s1ByKey = new Dictionary<string, SemDef>();
        foreach (SemDef d in stage1Defs)
        {
            string key = d.Chapter + "|" + d.Name;
            s1ByKey.TryAdd(key, d);
        }

        var matched = new List<(SemDef, SemDef)>();
        var dropped = new List<SemDef>();
        var s0Keys = new HashSet<string>();

        foreach (var (ch, defs) in stage0Chapters)
        {
            foreach (SemDef s0 in defs)
            {
                string key = ch + "|" + s0.Name;
                s0Keys.Add(key);
                if (s1ByKey.TryGetValue(key, out SemDef? s1))
                    matched.Add((s0, s1));
                else
                    dropped.Add(s0);
            }
        }

        var extra = stage1Defs.Where(d => !s0Keys.Contains(d.Chapter + "|" + d.Name)).ToList();

        return (matched, dropped, extra);
    }

    static void ResolveCollidingDefs(
        List<(SemDef s0, SemDef s1)> matched,
        List<SemDef> dropped, List<SemDef> extra,
        string[] slugsSorted)
    {
        // Build lookup: base name → list of unassigned stage1 defs
        var extraByName = new Dictionary<string, List<SemDef>>();
        foreach (SemDef d in extra)
        {
            if (d.Chapter != "?") continue;
            if (!extraByName.TryGetValue(d.Name, out var list))
            {
                list = [];
                extraByName[d.Name] = list;
            }
            list.Add(d);
        }

        var resolvedDropped = new List<SemDef>();
        var resolvedExtra = new HashSet<SemDef>();

        foreach (SemDef s0 in dropped)
        {
            if (!extraByName.TryGetValue(s0.Name, out var candidates))
                continue;

            // Normalize stage0 body for comparison
            string body0 = CollapseWhitespace(DemangleNames(s0.Body, slugsSorted));

            SemDef? bestMatch = null;
            foreach (SemDef s1 in candidates)
            {
                if (resolvedExtra.Contains(s1)) continue;
                string body1 = CollapseWhitespace(DemangleNames(s1.Body, slugsSorted));
                if (body0 == body1)
                {
                    bestMatch = s1;
                    break;
                }
            }

            // If no exact body match, try signature match
            if (bestMatch is null)
            {
                string sig0 = AlphaNormalizeTypeVars(s0.Sig);
                foreach (SemDef s1 in candidates)
                {
                    if (resolvedExtra.Contains(s1)) continue;
                    string sig1 = AlphaNormalizeTypeVars(s1.Sig);
                    if (sig0 == sig1)
                    {
                        bestMatch = s1;
                        break;
                    }
                }
            }

            if (bestMatch is not null)
            {
                matched.Add((s0, bestMatch with { Chapter = s0.Chapter }));
                resolvedDropped.Add(s0);
                resolvedExtra.Add(bestMatch);
            }
        }

        foreach (SemDef d in resolvedDropped)
            dropped.Remove(d);
        foreach (SemDef d in resolvedExtra)
            extra.Remove(d);
    }

    // ── Comparison-time transforms (applied during compare, not stored) ──

    static string DemangleNames(string body, string[] slugsSorted)
    {
        string text = body;
        foreach (string slug in slugsSorted)
        {
            string prefix = slug + "_";
            text = Regex.Replace(text, @"(?<![a-zA-Z0-9_-])" + Regex.Escape(prefix) + @"([a-z])", "$1");
        }
        return text;
    }

    static string CollapseWhitespace(string text)
    {
        // Tokenize: split into identifier-like runs and single non-identifier
        // chars, drop whitespace between them. Leading/trailing whitespace is
        // implicitly trimmed. Per the project's diagnostic semantics rule:
        // leading whitespace (structural indent) is what the parser cares
        // about; whitespace between tokens is style and ignored here.
        var tokens = Regex.Matches(text, @"[a-zA-Z0-9_][a-zA-Z0-9_\-]*|""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])*'|\S")
                          .Select(m => m.Value)
                          .ToList();

        // Strip redundant parens wrapping a single atomic expression:
        // "(x)" where x has no internal whitespace or parens of its own.
        // Applied iteratively until no more matches, so ((x)) -> (x) -> x.
        var joined = string.Join(" ", tokens);
        string prev;
        do
        {
            prev = joined;
            joined = Regex.Replace(joined, @"\(\s*([^()\s]+(?:\s+\.\s+[^()\s]+)*)\s*\)", "$1");
        } while (joined != prev);

        return joined.Trim();
    }

    static string AlphaNormalizeTypeVars(string sig)
    {
        var varMap = new Dictionary<string, string>();
        int nextVar = 0;

        return Regex.Replace(sig, @"\b([a-z])(\d*)\b", m =>
        {
            string full = m.Value;
            if (IsKnownTypeName(full))
                return full;
            if (!varMap.TryGetValue(full, out string? canonical))
            {
                canonical = $"t{nextVar++}";
                varMap[full] = canonical;
            }
            return canonical;
        });
    }

    static bool IsKnownTypeName(string name)
    {
        return name is "in" or "if" or "of" or "do" or "is";
    }

    // ── Output ──────────────────────────────────────────────────────

    static void ShowDefDetail(string name, List<(SemDef s0, SemDef s1)> matched,
                              string[] slugsSorted)
    {
        var hits = matched.Where(m => m.s0.Name == name).ToList();
        if (hits.Count == 0)
        {
            Console.Error.WriteLine($"No matched def named '{name}'");
            return;
        }
        foreach (var (s0, s1) in hits)
        {
            Console.WriteLine($"=== {s0.Chapter}: {s0.Name} ===");
            Console.WriteLine();
            Console.WriteLine("--- Stage0 Sig ---");
            Console.WriteLine(s0.Sig);
            Console.WriteLine("--- Stage1 Sig ---");
            Console.WriteLine(s1.Sig);
            Console.WriteLine();

            string body0 = CollapseWhitespace(DemangleNames(s0.Body, slugsSorted));
            string body1 = CollapseWhitespace(DemangleNames(s1.Body, slugsSorted));

            Console.WriteLine("--- Stage0 Body (whitespace-collapsed) ---");
            Console.WriteLine(body0);
            Console.WriteLine();
            Console.WriteLine("--- Stage1 Body (demangled, whitespace-collapsed) ---");
            Console.WriteLine(body1);
            Console.WriteLine();

            if (body0 == body1)
                Console.WriteLine("MATCH");
            else
                Console.WriteLine($"DIFF: {FirstDiff(body0, body1)}");
            Console.WriteLine();
        }
    }

    static string FirstDiff(string a, string b)
    {
        string[] tokA = a.Split(' ');
        string[] tokB = b.Split(' ');
        int len = Math.Min(tokA.Length, tokB.Length);
        for (int i = 0; i < len; i++)
        {
            if (tokA[i] != tokB[i])
            {
                int contextStart = Math.Max(0, i - 2);
                string ctxA = string.Join(' ', tokA.Skip(contextStart).Take(7));
                string ctxB = string.Join(' ', tokB.Skip(contextStart).Take(7));
                return $"token {i}: S0[...{ctxA}...] vs S1[...{ctxB}...]";
            }
        }
        if (tokA.Length != tokB.Length)
            return $"length differs: S0 has {tokA.Length} tokens, S1 has {tokB.Length}";
        return "identical (unexpected)";
    }

    static void PrintReport(Dictionary<string, List<SemDef>> stage0Chapters,
                            List<SemDef> stage1Defs, List<string> chapterSlugs,
                            Stage0Stats stats,
                            List<(SemDef s0, SemDef s1)> matched,
                            List<SemDef> dropped, List<SemDef> extra,
                            int bodyMatches, int bodyMismatches,
                            List<(SemDef s0, SemDef s1, string diff)> bodyMismatchList,
                            int sigMatches, int sigMismatches,
                            List<(SemDef s0, SemDef s1)> sigMismatchList,
                            HashSet<string> collidingNames)
    {
        int totalS0 = stage0Chapters.Values.Sum(d => d.Count);

        Console.WriteLine("=== Bootstrap2 Semantic Equivalence ===");
        Console.WriteLine();
        Console.WriteLine("Stage0 structure (recognized, not compared):");
        Console.WriteLine($"  {stats.Chapters} chapters, {stats.Sections} sections, {stats.ProseLines} prose lines, {stats.Cites} cites");
        Console.WriteLine();
        Console.WriteLine("Comparison method:");
        Console.WriteLine("  Applied: name demangling (stage1), whitespace collapse (both), type-var alpha-norm (sigs)");
        Console.WriteLine("  Not applied: brace escapes, parenthesization — reported as-is");
        Console.WriteLine();
        Console.WriteLine($"Stage0: {totalS0} defs ({chapterSlugs.Count} chapters)");
        Console.WriteLine($"Stage1: {stage1Defs.Count} defs");
        Console.WriteLine($"Collisions: {collidingNames.Count} names defined in 2+ chapters");
        Console.WriteLine($"Sigs: {sigMatches} match, {sigMismatches} differ");
        Console.WriteLine($"Bodies: {bodyMatches} match, {bodyMismatches} differ");
        Console.WriteLine();

        Console.WriteLine("Per-Chapter Summary:");
        Console.WriteLine($"  {"Chapter",-32} {"S0",4} {"S1",4} {"Match",5} {"Drop",5} {"Extra",5} {"Body%",6}");
        Console.WriteLine("  " + new string('-', 67));

        int totalMatch = 0, totalDrop = 0, totalExtra = 0;

        foreach (string slug in chapterSlugs)
        {
            int s0Count = stage0Chapters.TryGetValue(slug, out var s0Defs) ? s0Defs.Count : 0;
            int s1Count = stage1Defs.Count(d => d.Chapter == slug);
            int matchCount = matched.Count(m => m.s0.Chapter == slug);
            int dropCount = dropped.Count(d => d.Chapter == slug);
            int extraCount = extra.Count(d => d.Chapter == slug);
            int bodyOk = matchCount > 0
                ? matchCount - bodyMismatchList.Count(m => m.s0.Chapter == slug)
                : 0;
            string bodyPct = matchCount > 0 ? $"{100.0 * bodyOk / matchCount:F0}%" : "--";

            Console.WriteLine($"  {slug,-32} {s0Count,4} {s1Count,4} {matchCount,5} {dropCount,5} {extraCount,5} {bodyPct,6}");

            totalMatch += matchCount;
            totalDrop += dropCount;
            totalExtra += extraCount;
        }

        int unknownExtra = extra.Count(d => d.Chapter == "?");
        if (unknownExtra > 0)
            Console.WriteLine($"  {"(unassigned)",-32} {"",4} {unknownExtra,4} {"",5} {"",5} {unknownExtra,5}");

        string totalBodyPct = totalMatch > 0 ? $"{100.0 * bodyMatches / totalMatch:F0}%" : "--";
        Console.WriteLine("  " + new string('-', 67));
        Console.WriteLine($"  {"TOTAL",-32} {totalS0,4} {stage1Defs.Count,4} {totalMatch,5} {totalDrop,5} {totalExtra + unknownExtra,5} {totalBodyPct,6}");

        if (dropped.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"Dropped ({dropped.Count}):");
            foreach (SemDef d in dropped)
                Console.WriteLine($"  {d.Chapter}: {d.Name} (line {d.LineNo})");
        }

        if (extra.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"Extra ({extra.Count}):");
            foreach (SemDef d in extra.Take(30))
            {
                string leaked = Regex.IsMatch(d.Sig, @"^a\d+$") ? " [leaked]" : "";
                Console.WriteLine($"  {d.Chapter}: {d.Name} : {d.Sig}{leaked}");
            }
            if (extra.Count > 30)
                Console.WriteLine($"  ... and {extra.Count - 30} more");
        }

        if (sigMismatchList.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"Sig Mismatches ({sigMismatchList.Count}):");
            foreach (var (s0, s1) in sigMismatchList.Take(20))
                Console.WriteLine($"  {s0.Chapter}: {s0.Name}");
            if (sigMismatchList.Count > 20)
                Console.WriteLine($"  ... and {sigMismatchList.Count - 20} more");
        }

        if (bodyMismatchList.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"Body Mismatches ({bodyMismatchList.Count}):");
            foreach (var (s0, s1, diff) in bodyMismatchList)
                Console.WriteLine($"  {s0.Chapter}: {s0.Name} — {diff}");
        }
    }
}
