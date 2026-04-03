namespace Codex.Cli;

using System.Text;
using System.Text.RegularExpressions;

public static partial class Program
{
    record SemDef(string Name, string Module, string Sig, string Body, int LineNo, bool IsType);

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

        var (stage0Modules, moduleSlugs) = ParseStage0(stage0Text);
        List<SemDef> stage1Defs = ParseStage1(stage1Text);

        HashSet<string> collidingModules = BuildCollisionSet(stage0Modules);
        string[] slugsSorted = moduleSlugs.OrderByDescending(s => s.Length).ToArray();

        AssignModules(stage1Defs, stage0Modules, collidingModules, slugsSorted);

        var (matched, dropped, extra) = MatchDefs(stage0Modules, stage1Defs);

        int bodyMatches = 0;
        int bodyMismatches = 0;
        int sigMatches = 0;
        int sigMismatches = 0;
        var bodyMismatchList = new List<(SemDef s0, SemDef s1, string diff)>();
        var sigMismatchList = new List<(SemDef s0, SemDef s1)>();

        foreach (var (s0, s1) in matched)
        {
            // Sig comparison (with type-var alpha-normalization)
            string normSig0 = AlphaNormalizeTypeVars(s0.Sig);
            string normSig1 = AlphaNormalizeTypeVars(s1.Sig);
            if (normSig0 == normSig1)
                sigMatches++;
            else
            {
                sigMismatches++;
                sigMismatchList.Add((s0, s1));
            }

            // Body comparison
            string norm0 = NormalizeBody(s0.Body, collidingModules, slugsSorted, demangle: false);
            string norm1 = NormalizeBody(s1.Body, collidingModules, slugsSorted, demangle: true);
            if (norm0 == norm1)
                bodyMatches++;
            else
            {
                bodyMismatches++;
                bodyMismatchList.Add((s0, s1, FirstDiff(norm0, norm1)));
            }
        }

        if (showDef != null)
        {
            ShowDefDetail(showDef, matched, collidingModules, slugsSorted);
            return 0;
        }

        PrintReport(stage0Modules, stage1Defs, moduleSlugs, matched, dropped, extra,
                     bodyMatches, bodyMismatches, bodyMismatchList,
                     sigMatches, sigMismatches, sigMismatchList, collidingModules);

        bool pass = dropped.Count == 0 && bodyMismatches == 0;
        Console.WriteLine();
        Console.WriteLine(pass ? "Verdict: PASS" : "Verdict: FAIL");
        return pass ? 0 : 1;
    }

    static (Dictionary<string, List<SemDef>> modules, List<string> slugs) ParseStage0(string text)
    {
        var modules = new Dictionary<string, List<SemDef>>();
        var slugs = new List<string>();
        string[] lines = text.Split('\n');
        string currentModule = "";
        int i = 0;

        while (i < lines.Length)
        {
            string line = lines[i];

            if (line.StartsWith("module: ", StringComparison.Ordinal))
            {
                currentModule = line["module: ".Length..].Trim();
                if (!modules.ContainsKey(currentModule))
                {
                    modules[currentModule] = [];
                    slugs.Add(currentModule);
                }
                i++;
                continue;
            }

            if (line.StartsWith("end module ", StringComparison.Ordinal))
            {
                i++;
                continue;
            }

            if (line.StartsWith("import ", StringComparison.Ordinal))
            {
                i++;
                continue;
            }

            if (line.Length > 0 && char.IsLetter(line[0]))
            {
                var (def, nextI) = ParseOneDef(lines, i, currentModule);
                if (def != null)
                    modules[currentModule].Add(def);
                i = nextI;
            }
            else
            {
                i++;
            }
        }

        return (modules, slugs);
    }

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

    static (SemDef? def, int nextI) ParseOneDef(string[] lines, int start, string module)
    {
        string firstLine = lines[start];

        bool isTypeDef = char.IsUpper(firstLine[0]);

        string name;
        string sig;
        int bodyStart;

        if (isTypeDef)
        {
            int eqPos = firstLine.IndexOf(" =", StringComparison.Ordinal);
            if (eqPos < 0)
                return (null, start + 1);

            name = firstLine[..eqPos].Trim();
            sig = "";
            bodyStart = start;
        }
        else
        {
            int colonPos = firstLine.IndexOf(" : ", StringComparison.Ordinal);
            if (colonPos < 0)
                return (null, start + 1);

            name = firstLine[..colonPos];
            sig = firstLine[(colonPos + 3)..];
            bodyStart = start + 1;
        }

        // For function defs, body starts at the definition line (skip sig line).
        // For type defs, body starts at the first line (includes "TypeName =").
        var bodyLines = new StringBuilder();
        int j;
        if (isTypeDef)
        {
            bodyLines.AppendLine(lines[start]);
            j = start + 1;
        }
        else
        {
            // Skip the sig line — it's already stored in `sig`.
            // The next line should be the def line: `name (params) =`
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
                bool looksLikeModuleEnd = ln.StartsWith("end module ", StringComparison.Ordinal);
                bool looksLikeModuleStart = ln.StartsWith("module: ", StringComparison.Ordinal);

                if (looksLikeModuleEnd || looksLikeModuleStart)
                    break;

                // For function defs, the definition line comes right after the sig
                if (!isTypeDef && j == start + 1 && ln.StartsWith(name, StringComparison.Ordinal))
                {
                    bodyLines.AppendLine(ln);
                    j++;
                    continue;
                }

                // A new sig or type def line → boundary
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

        return (new SemDef(name, module, sig, bodyLines.ToString().TrimEnd(), start + 1, isTypeDef), j);
    }

    static HashSet<string> BuildCollisionSet(Dictionary<string, List<SemDef>> modules)
    {
        // Find names defined in 2+ modules (per-name collision detection).
        // This matches the bare-metal compiler's current mangling behavior.
        // When the bare-metal compiler switches to whole-module mangling,
        // this should switch to returning colliding module slugs instead.
        var nameToModules = new Dictionary<string, HashSet<string>>();
        foreach (var (mod, defs) in modules)
        {
            foreach (SemDef d in defs)
            {
                if (!nameToModules.TryGetValue(d.Name, out var mods))
                {
                    mods = [];
                    nameToModules[d.Name] = mods;
                }
                mods.Add(mod);
            }
        }

        return nameToModules.Where(kv => kv.Value.Count > 1).Select(kv => kv.Key).ToHashSet();
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

    static void AssignModules(List<SemDef> stage1Defs, Dictionary<string, List<SemDef>> stage0Modules,
                              HashSet<string> collidingModules, string[] slugsSorted)
    {
        // Non-mangled names: look up in stage0 modules by name.
        // Only non-colliding names appear unmangled in stage1.
        var nameToModule = new Dictionary<string, string>();
        foreach (var (mod, defs) in stage0Modules)
        {
            foreach (SemDef d in defs)
            {
                if (!collidingModules.Contains(d.Name))
                    nameToModule.TryAdd(d.Name, mod);
            }
        }

        for (int i = 0; i < stage1Defs.Count; i++)
        {
            SemDef d = stage1Defs[i];
            var demangled = DemangleName(d.Name, slugsSorted);
            if (demangled is var (slug, baseName))
            {
                stage1Defs[i] = d with { Name = baseName, Module = slug };
            }
            else if (nameToModule.TryGetValue(d.Name, out string? mod))
            {
                stage1Defs[i] = d with { Module = mod };
            }
            else
            {
                stage1Defs[i] = d with { Module = "?" };
            }
        }
    }

    static (List<(SemDef s0, SemDef s1)> matched, List<SemDef> dropped, List<SemDef> extra)
        MatchDefs(Dictionary<string, List<SemDef>> stage0Modules, List<SemDef> stage1Defs)
    {
        var s1ByKey = new Dictionary<string, SemDef>();
        foreach (SemDef d in stage1Defs)
        {
            string key = d.Module + "|" + d.Name;
            s1ByKey.TryAdd(key, d);
        }

        var matched = new List<(SemDef, SemDef)>();
        var dropped = new List<SemDef>();
        var s0Keys = new HashSet<string>();

        foreach (var (mod, defs) in stage0Modules)
        {
            foreach (SemDef s0 in defs)
            {
                string key = mod + "|" + s0.Name;
                s0Keys.Add(key);
                if (s1ByKey.TryGetValue(key, out SemDef? s1))
                    matched.Add((s0, s1));
                else
                    dropped.Add(s0);
            }
        }

        var extra = stage1Defs.Where(d => !s0Keys.Contains(d.Module + "|" + d.Name)).ToList();

        return (matched, dropped, extra);
    }

    static string AlphaNormalizeTypeVars(string sig)
    {
        // Normalize type variables: a, b, c, a2, a3, b4 → t0, t1, t2, ...
        // Type vars in Codex are single lowercase letters or letter+digits
        var varMap = new Dictionary<string, string>();
        int nextVar = 0;

        return Regex.Replace(sig, @"\b([a-z])(\d*)\b", m =>
        {
            string full = m.Value;
            // Skip known type names and keywords
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
        // Known Codex types and keywords that look like type vars
        return name is "in" or "if" or "of" or "do" or "is";
    }

    static string NormalizeBody(string body, HashSet<string> collidingModules, string[] slugsSorted,
                                bool demangle)
    {
        string text = body;

        if (demangle)
        {
            foreach (string slug in slugsSorted)
            {
                string prefix = slug + "_";
                text = Regex.Replace(text, @"(?<![a-zA-Z0-9_-])" + Regex.Escape(prefix) + @"([a-z])", "$1");
            }
        }

        text = Regex.Replace(text, @"\s+", " ").Trim();

        // Normalize redundant parens: strip parens around field access `(expr.field)` → `expr.field`
        text = Regex.Replace(text, @"\(([a-zA-Z][a-zA-Z0-9-]*\.[a-zA-Z][a-zA-Z0-9.-]*)\)", "$1");

        // Strip redundant outer parens around `(if ... then ... else ...)` expressions
        text = StripRedundantIfParens(text);

        return text;
    }

    static string StripRedundantIfParens(string text)
    {
        // Find `(if ` and strip the balanced outer parens
        var sb = new StringBuilder(text.Length);
        int i = 0;
        while (i < text.Length)
        {
            if (i + 4 < text.Length && text[i] == '(' && text.Substring(i + 1, 3) == "if ")
            {
                // Track paren depth to find the matching close
                int depth = 1;
                int j = i + 1;
                while (j < text.Length && depth > 0)
                {
                    if (text[j] == '(') depth++;
                    else if (text[j] == ')') depth--;
                    if (depth > 0) j++;
                }
                if (depth == 0)
                {
                    // Strip outer parens: append content between ( and )
                    sb.Append(text, i + 1, j - i - 1);
                    i = j + 1;
                }
                else
                {
                    sb.Append(text[i]);
                    i++;
                }
            }
            else
            {
                sb.Append(text[i]);
                i++;
            }
        }
        return sb.ToString();
    }

    static void ShowDefDetail(string name, List<(SemDef s0, SemDef s1)> matched,
                              HashSet<string> collidingModules, string[] slugsSorted)
    {
        var hits = matched.Where(m => m.s0.Name == name).ToList();
        if (hits.Count == 0)
        {
            Console.Error.WriteLine($"No matched def named '{name}'");
            return;
        }
        foreach (var (s0, s1) in hits)
        {
            Console.WriteLine($"=== {s0.Module}: {s0.Name} ===");
            Console.WriteLine();
            Console.WriteLine("--- Stage0 Sig ---");
            Console.WriteLine(s0.Sig);
            Console.WriteLine("--- Stage1 Sig ---");
            Console.WriteLine(s1.Sig);
            Console.WriteLine();

            string norm0 = NormalizeBody(s0.Body, collidingModules, slugsSorted, demangle: false);
            string norm1 = NormalizeBody(s1.Body, collidingModules, slugsSorted, demangle: true);

            Console.WriteLine("--- Stage0 Body (normalized) ---");
            Console.WriteLine(norm0);
            Console.WriteLine();
            Console.WriteLine("--- Stage1 Body (normalized) ---");
            Console.WriteLine(norm1);
            Console.WriteLine();

            if (norm0 == norm1)
                Console.WriteLine("MATCH");
            else
                Console.WriteLine($"DIFF: {FirstDiff(norm0, norm1)}");
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
        return "identical after normalization (unexpected)";
    }

    static void PrintReport(Dictionary<string, List<SemDef>> stage0Modules,
                            List<SemDef> stage1Defs, List<string> moduleSlugs,
                            List<(SemDef s0, SemDef s1)> matched,
                            List<SemDef> dropped, List<SemDef> extra,
                            int bodyMatches, int bodyMismatches,
                            List<(SemDef s0, SemDef s1, string diff)> bodyMismatchList,
                            int sigMatches, int sigMismatches,
                            List<(SemDef s0, SemDef s1)> sigMismatchList,
                            HashSet<string> collidingModules)
    {
        int totalS0 = stage0Modules.Values.Sum(d => d.Count);

        Console.WriteLine("=== Bootstrap2 Semantic Equivalence ===");
        Console.WriteLine();
        Console.WriteLine($"Stage0: {totalS0} defs ({moduleSlugs.Count} modules)");
        Console.WriteLine($"Stage1: {stage1Defs.Count} defs");
        Console.WriteLine($"Collisions: {collidingModules.Count} names defined in 2+ modules");
        Console.WriteLine($"Sigs: {sigMatches} match, {sigMismatches} differ");
        Console.WriteLine($"Bodies: {bodyMatches} match, {bodyMismatches} differ");
        Console.WriteLine();

        Console.WriteLine("Per-Module Summary:");
        Console.WriteLine($"  {"Module",-32} {"S0",4} {"S1",4} {"Match",5} {"Drop",5} {"Extra",5} {"Body%",6}");
        Console.WriteLine("  " + new string('-', 67));

        int totalMatch = 0, totalDrop = 0, totalExtra = 0;

        foreach (string slug in moduleSlugs)
        {
            int s0Count = stage0Modules.TryGetValue(slug, out var s0Defs) ? s0Defs.Count : 0;
            int s1Count = stage1Defs.Count(d => d.Module == slug);
            int matchCount = matched.Count(m => m.s0.Module == slug);
            int dropCount = dropped.Count(d => d.Module == slug);
            int extraCount = extra.Count(d => d.Module == slug);
            int bodyOk = matchCount > 0
                ? matchCount - bodyMismatchList.Count(m => m.s0.Module == slug)
                : 0;
            string bodyPct = matchCount > 0 ? $"{100.0 * bodyOk / matchCount:F0}%" : "--";

            Console.WriteLine($"  {slug,-32} {s0Count,4} {s1Count,4} {matchCount,5} {dropCount,5} {extraCount,5} {bodyPct,6}");

            totalMatch += matchCount;
            totalDrop += dropCount;
            totalExtra += extraCount;
        }

        int unknownExtra = extra.Count(d => d.Module == "?");
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
                Console.WriteLine($"  {d.Module}: {d.Name} (line {d.LineNo})");
        }

        if (extra.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"Extra ({extra.Count}):");
            foreach (SemDef d in extra.Take(30))
            {
                string leaked = Regex.IsMatch(d.Sig, @"^a\d+$") ? " [leaked]" : "";
                Console.WriteLine($"  {d.Module}: {d.Name} : {d.Sig}{leaked}");
            }
            if (extra.Count > 30)
                Console.WriteLine($"  ... and {extra.Count - 30} more");
        }

        if (sigMismatchList.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"Sig Mismatches ({sigMismatchList.Count}):");
            foreach (var (s0, s1) in sigMismatchList.Take(20))
                Console.WriteLine($"  {s0.Module}: {s0.Name}");
            if (sigMismatchList.Count > 20)
                Console.WriteLine($"  ... and {sigMismatchList.Count - 20} more");
        }

        if (bodyMismatchList.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"Body Mismatches ({bodyMismatchList.Count}):");
            foreach (var (s0, s1, diff) in bodyMismatchList.Take(20))
                Console.WriteLine($"  {s0.Module}: {s0.Name} — {diff}");
            if (bodyMismatchList.Count > 20)
                Console.WriteLine($"  ... and {bodyMismatchList.Count - 20} more");
        }
    }
}
