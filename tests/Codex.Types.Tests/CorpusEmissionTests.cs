using Codex.Emit;
using Codex.Emit.CSharp;
using Xunit;

namespace Codex.Types.Tests;


public class CorpusEmissionTests
{
    static readonly string s_samplesDir = FindSamplesDir();
    static readonly string s_outputDir = FindOutputDir();

    static readonly ICodeEmitter[] s_emitters =
    [
        new CSharpEmitter(),
#if LEGACY_EMITTERS
        new Emit.JavaScript.JavaScriptEmitter(),
        new Emit.Python.PythonEmitter(),
        new Emit.Rust.RustEmitter(),
        new Emit.Cpp.CppEmitter(),
        new Emit.Go.GoEmitter(),
        new Emit.Java.JavaEmitter(),
        new Emit.Ada.AdaEmitter(),
        new Emit.Babbage.BabbageEmitter(),
        new Emit.Fortran.FortranEmitter(),
        new Emit.Cobol.CobolEmitter(),
#endif
    ];

    static string FindSamplesDir()
    {
        string dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            string candidate = Path.Combine(dir, "samples");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = Path.GetDirectoryName(dir)!;
        }
        throw new DirectoryNotFoundException("Cannot find samples/ directory");
    }

    static string FindOutputDir()
    {
        string dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            string candidate = Path.Combine(dir, "samples");
            if (Directory.Exists(candidate))
            {
                return Path.Combine(dir, "generated-output");
            }

            dir = Path.GetDirectoryName(dir)!;
        }
        throw new DirectoryNotFoundException("Cannot find repo root");
    }

    static string LanguageDirName(ICodeEmitter emitter) => emitter.TargetName switch
    {
        "C#" => "csharp",
        "C++" => "cpp",
        "Babbage Analytical Engine" => "babbage",
        string name => name.ToLowerInvariant(),
    };

    // Samples that are intentional error repros — they live in samples/ as
    // references for diagnostic tests and the parity audit, not as programs
    // that should compile cleanly.
    static readonly HashSet<string> s_negativeSamples = new(StringComparer.Ordinal)
    {
        "let-effectful-bug.codex", // CDX2033 repro (see commit 1b49158)
        "parser-resync.codex",     // parser error-recovery repro
    };

    public static IEnumerable<object[]> AllSamples()
    {
        foreach (string file in Directory.GetFiles(FindSamplesDir(), "*.codex").OrderBy(f => f))
        {
            string name = Path.GetFileName(file);
            if (s_negativeSamples.Contains(name))
            {
                continue;
            }

            yield return [name];
        }
    }

    public static IEnumerable<object[]> AllSamplesAndBackends()
    {
        string[] samples = Directory.GetFiles(FindSamplesDir(), "*.codex").OrderBy(f => f).ToArray();
        foreach (string file in samples)
        {
            string name = Path.GetFileName(file);
            if (s_negativeSamples.Contains(name))
            {
                continue;
            }

            foreach (ICodeEmitter emitter in s_emitters)
            {
                yield return [name, emitter.TargetName];
            }
        }
    }

    static bool RequiresModuleLoader(string source) =>
        source.Contains("cites ", StringComparison.Ordinal);

    [Theory]
    [MemberData(nameof(AllSamplesAndBackends))]
    public void Sample_compiles_to_backend(string sampleFile, string targetName)
    {
        string filePath = Path.Combine(s_samplesDir, sampleFile);
        string source = File.ReadAllText(filePath);
        if (RequiresModuleLoader(source))
        {
            return; // Multi-file samples need a module loader; skip in single-file tests
        }

        string chapterName = Path.GetFileNameWithoutExtension(sampleFile).Replace("-", "_");

        ICodeEmitter emitter = s_emitters.First(e => e.TargetName == targetName);
        string? output = Helpers.CompileToTarget(source, chapterName, emitter);

        Assert.True(output is not null,
            $"Failed to compile {sampleFile} to {targetName}");
        Assert.True(output!.Length > 0,
            $"Empty output for {sampleFile} to {targetName}");
    }

    [Fact(Skip = "On-demand only — Sample_compiles_to_backend covers the same compilation checks")]
    public void Emit_full_corpus_to_generated_output()
    {
        string[] sampleFiles = Directory.GetFiles(s_samplesDir, "*.codex").OrderBy(f => f).ToArray();
        List<string> failures = [];

        foreach (ICodeEmitter emitter in s_emitters)
        {
            string langDir = Path.Combine(s_outputDir, LanguageDirName(emitter));
            Directory.CreateDirectory(langDir);

            foreach (string filePath in sampleFiles)
            {
                string sampleName = Path.GetFileNameWithoutExtension(filePath);
                string chapterName = sampleName.Replace("-", "_");
                string source = File.ReadAllText(filePath);

                if (RequiresModuleLoader(source))
                {
                    continue; // Multi-file samples need a module loader; skip in single-file tests
                }

                string? output = Helpers.CompileToTarget(source, chapterName, emitter);
                if (output is null)
                {
                    failures.Add($"{sampleName} → {emitter.TargetName}");
                    continue;
                }

                string outputFileName = sampleName + emitter.FileExtension;
                string outputPath = Path.Combine(langDir, outputFileName);
                File.WriteAllText(outputPath, output);
            }
        }

        Assert.True(failures.Count == 0,
            $"Failed to compile:\n  {string.Join("\n  ", failures)}");
    }
}
