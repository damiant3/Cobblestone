using Codex.Ast;
using Codex.Core;
using Codex.IR;
using Codex.Semantics;
using Codex.Syntax;
using Codex.Types;

namespace Codex.Cli;

// Per-phase dump sink. Constructed from --dump=<phases> and --dump-dir=<path>;
// each DumpXxx call is a no-op unless that phase was enabled. Files are written
// as soon as the phase completes so a later failure still leaves partial dumps.
sealed class PhaseDumpSink
{
    public const string PhaseParsed = "parsed";
    public const string PhaseDesugared = "desugared";
    public const string PhaseResolved = "resolved";
    public const string PhaseTyped = "typed";
    public const string PhaseIR = "ir";
    public const string PhaseCodegen = "codegen";

    static readonly string[] s_knownPhases =
        [PhaseParsed, PhaseDesugared, PhaseResolved, PhaseTyped, PhaseIR, PhaseCodegen];

    readonly HashSet<string> m_phases;
    readonly string m_outputDir;

    PhaseDumpSink(HashSet<string> phases, string outputDir)
    {
        m_phases = phases;
        m_outputDir = outputDir;
    }

    public static PhaseDumpSink? TryCreate(string? phaseList, string? outputDir, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(phaseList))
        {
            return null;
        }

        HashSet<string> phases = new(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in phaseList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string p = raw.ToLowerInvariant();
            if (p == "all")
            {
                foreach (string k in s_knownPhases)
                {
                    phases.Add(k);
                }
                continue;
            }
            if (Array.IndexOf(s_knownPhases, p) < 0)
            {
                error = $"unknown --dump phase '{raw}'. Known: {string.Join(", ", s_knownPhases)}, all";
                return null;
            }
            phases.Add(p);
        }

        string dir = string.IsNullOrWhiteSpace(outputDir)
            ? Path.Combine(Directory.GetCurrentDirectory(), "dumps")
            : Path.GetFullPath(outputDir);
        Directory.CreateDirectory(dir);
        return new PhaseDumpSink(phases, dir);
    }

    bool Enabled(string phase) => m_phases.Contains(phase);

    public void DumpParsed(string baseName, IReadOnlyList<(string FilePath, DocumentNode Document)> files)
    {
        if (!Enabled(PhaseParsed))
        {
            return;
        }

        using StreamWriter sw = OpenDump(baseName, PhaseParsed, ".txt");
        foreach ((string filePath, DocumentNode doc) in files)
        {
            sw.WriteLine($"=== file: {Path.GetFileName(filePath)} ===");
            ParsedDumpFormatter.Write(sw, doc);
            sw.WriteLine();
        }
    }

    public void DumpDesugared(string baseName, Chapter chapter)
    {
        if (!Enabled(PhaseDesugared))
        {
            return;
        }

        using StreamWriter sw = OpenDump(baseName, PhaseDesugared, ".txt");
        DesugaredDumpFormatter.Write(sw, chapter);
    }

    public void DumpResolved(string baseName, ResolvedChapter resolved)
    {
        if (!Enabled(PhaseResolved))
        {
            return;
        }

        using StreamWriter sw = OpenDump(baseName, PhaseResolved, ".txt");
        ResolvedDumpFormatter.Write(sw, resolved);
    }

    public void DumpTyped(
        string baseName,
        ResolvedChapter resolved,
        Map<string, CodexType> topLevelTypes,
        IReadOnlyDictionary<Expr, CodexType> exprTypes)
    {
        if (!Enabled(PhaseTyped))
        {
            return;
        }

        using StreamWriter sw = OpenDump(baseName, PhaseTyped, ".txt");
        TypedDumpFormatter.Write(sw, resolved, topLevelTypes, exprTypes);
    }

    public void DumpIR(string baseName, IRChapter ir)
    {
        if (!Enabled(PhaseIR))
        {
            return;
        }

        string output = new Codex.Emit.Codex.CodexEmitter().Emit(ir);
        File.WriteAllText(Path.Combine(m_outputDir, $"{baseName}.{PhaseIR}.codex"), output);
    }

    public void DumpCodegen(string baseName, string fileExtension, string content)
    {
        if (!Enabled(PhaseCodegen))
        {
            return;
        }

        string ext = NormalizeExt(fileExtension);
        File.WriteAllText(Path.Combine(m_outputDir, $"{baseName}.{PhaseCodegen}{ext}"), content);
    }

    public void DumpCodegen(string baseName, string fileExtension, byte[] content)
    {
        if (!Enabled(PhaseCodegen))
        {
            return;
        }

        string ext = NormalizeExt(fileExtension);
        File.WriteAllBytes(Path.Combine(m_outputDir, $"{baseName}.{PhaseCodegen}{ext}"), content);
    }

    StreamWriter OpenDump(string baseName, string phase, string extension)
    {
        string path = Path.Combine(m_outputDir, $"{baseName}.{phase}{extension}");
        return new StreamWriter(path);
    }

    static string NormalizeExt(string ext) =>
        string.IsNullOrEmpty(ext) ? ""
        : ext.StartsWith('.') ? ext
        : "." + ext;
}
