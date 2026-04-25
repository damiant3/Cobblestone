using System.Text;
using System.Text.RegularExpressions;
using Codex.Core;

namespace Codex.Cli;

public static partial class Program
{
    static int RunDumpSource(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: codex dump-source <codex-dir> [output-path]");
            Console.Error.WriteLine("");
            Console.Error.WriteLine("  Concatenates every .codex file in <codex-dir> (root + one level of");
            Console.Error.WriteLine("  subdirectory), rewrites each Chapter: header to <Quire>--<Chapter>");
            Console.Error.WriteLine("  for files inside a quire subdir, prepends every Foreword chapter");
            Console.Error.WriteLine("  cited by the body, and writes the result to output-path. Default");
            Console.Error.WriteLine("  output is %TEMP%/codex-all-source.codex.");
            return 1;
        }

        string codexDir = Path.GetFullPath(args[0]);
        if (!Directory.Exists(codexDir))
        {
            Console.Error.WriteLine($"error: directory not found: {codexDir}");
            return 1;
        }

        string outputPath = args.Length > 1
            ? Path.GetFullPath(args[1])
            : Path.Combine(Path.GetTempPath(), "codex-all-source.codex");

        string combined = LoadCodexSourceConcatenated(codexDir);
        string? outDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);
        File.WriteAllText(outputPath, combined);
        Console.WriteLine($"Wrote {combined.Length} chars to {outputPath}");
        return 0;
    }

    // Walks codexDir + one level of subdirectory for *.codex, sorts ordinally,
    // rewrites each Chapter: header in a quire-subdirectory file to
    // <Quire>--<Chapter> so the parser can slug chapters by (quire, title) and
    // stay byte-identical with how `codex build <dir>` loads them. Files at the
    // root keep their original header. Foreword chapters cited by the body get
    // prepended (also with their headers rewritten to Foreword--<Chapter>).
    static string LoadCodexSourceConcatenated(string codexDir)
    {
        List<string> files = new();
        files.AddRange(Directory.GetFiles(codexDir, "*.codex", SearchOption.TopDirectoryOnly));
        foreach (string sub in Directory.GetDirectories(codexDir))
            files.AddRange(Directory.GetFiles(sub, "*.codex", SearchOption.TopDirectoryOnly));

        files.Sort(StringComparer.Ordinal);

        StringBuilder buf = new();
        foreach (string f in files)
        {
            string content = File.ReadAllText(f);
            if (content.Length == 0) continue;

            string quire = QuireOf(f, codexDir);
            if (quire.Length > 0)
            {
                content = Regex.Replace(content,
                    @"^(Chapter:\s*)(.+?)\s*$",
                    m => m.Groups[1].Value + quire + "--" + m.Groups[2].Value.Trim(),
                    RegexOptions.Multiline);
            }
            if (buf.Length > 0) buf.Append("\n\n");
            buf.Append(content);
        }

        string codexBody = buf.ToString();
        string forewordPrefix = LoadCitedForewordChapters(codexBody);
        return forewordPrefix.Length == 0 ? codexBody : forewordPrefix + "\n\n" + codexBody;
    }

    static string LoadCitedForewordChapters(string codexBody)
    {
        string? forewordDir = FindForewordDirectory();
        if (forewordDir is null) return "";

        Set<string> cited = Set<string>.s_empty;
        foreach (Match m in Regex.Matches(codexBody,
            @"^\s*cites\s+Foreword\s+chapter\s+([A-Za-z_][A-Za-z0-9_-]*)",
            RegexOptions.Multiline))
        {
            cited = cited.Add(m.Groups[1].Value);
        }
        if (cited.Count == 0) return "";

        List<string> ordered = new(cited);
        ordered.Sort(StringComparer.Ordinal);

        StringBuilder buf = new();
        foreach (string name in ordered)
        {
            string path = Path.Combine(forewordDir, name + ".codex");
            if (!File.Exists(path)) continue;

            string content = File.ReadAllText(path);
            if (content.Length == 0) continue;

            content = Regex.Replace(content,
                @"^(Chapter:\s*)(.+?)\s*$",
                m => m.Groups[1].Value + "Foreword--" + m.Groups[2].Value.Trim(),
                RegexOptions.Multiline);

            if (buf.Length > 0) buf.Append("\n\n");
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
            if (Directory.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }

        string cwdCandidate = Path.Combine(Directory.GetCurrentDirectory(), "foreword");
        if (Directory.Exists(cwdCandidate)) return cwdCandidate;
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
}
