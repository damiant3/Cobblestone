using System.Text.Json;
using System.Text.Json.Serialization;

namespace Codex.Cli;

public static partial class Program
{
    internal sealed class CodexProject
    {
        public string Name { get; set; } = "";
        public string Version { get; set; } = "0.1.0";
        public string Description { get; set; } = "";
        public string[] Authors { get; set; } = [];
        public string License { get; set; } = "";
        public string[] Sources { get; set; } = ["**/*.codex"];
        public string[] Exclude { get; set; } = [];
        public string[] Dependencies { get; set; } = [];
        public PackageRef[] Packages { get; set; } = [];
        public string Target { get; set; } = "cs";
        public string[] Targets { get; set; } = [];
        public string Output { get; set; } = "out/";
        public bool Foreword { get; set; }
    }

    internal sealed class PackageRef
    {
        public string Name { get; set; } = "";
        public string Version { get; set; } = "*";
        public string Path { get; set; } = "";
    }

    internal sealed class PackageLock
    {
        public string ProjectName { get; set; } = "";
        public string ProjectVersion { get; set; } = "";
        public PackageLockEntry[] Packages { get; set; } = [];
    }

    internal sealed class PackageLockEntry
    {
        public string Name { get; set; } = "";
        public string Version { get; set; } = "";
        public string ContentHash { get; set; } = "";
        public string ResolvedPath { get; set; } = "";
    }

    static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        PropertyNamingPolicy = JsonNamingPolicy.KebabCaseLower,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    internal static CodexProject? LoadProjectFile(string directory)
    {
        string projectPath = Path.Combine(directory, "codex.project.json");
        if (!File.Exists(projectPath))
        {
            return null;
        }

        string json = File.ReadAllText(projectPath);
        return JsonSerializer.Deserialize<CodexProject>(json, s_jsonOptions);
    }

    internal static void SaveProjectFile(string directory, CodexProject project)
    {
        string projectPath = Path.Combine(directory, "codex.project.json");
        string json = JsonSerializer.Serialize(project, s_jsonOptions);
        File.WriteAllText(projectPath, json + "\n");
    }

    internal static PackageLock? LoadLockFile(string directory)
    {
        string lockPath = Path.Combine(directory, "codex.lock.json");
        if (!File.Exists(lockPath))
        {
            return null;
        }

        string json = File.ReadAllText(lockPath);
        return JsonSerializer.Deserialize<PackageLock>(json, s_jsonOptions);
    }

    internal static void SaveLockFile(string directory, PackageLock lockFile)
    {
        string lockPath = Path.Combine(directory, "codex.lock.json");
        string json = JsonSerializer.Serialize(lockFile, s_jsonOptions);
        File.WriteAllText(lockPath, json + "\n");
    }

    internal static string[] ResolveProjectSources(string directory, CodexProject project)
    {
        List<string> files = [];
        foreach (string pattern in project.Sources)
        {
            if (pattern.Contains('*'))
            {
                string filePattern = Path.GetFileName(pattern);
                bool recursive = pattern.Contains("**");
                // Quire semantics: `**` means root + one level of subdirectory
                // (each top-level subdirectory is a quire). Nested subdirectories
                // below depth 1 are not scanned.
                foreach (string file in Directory.GetFiles(directory, filePattern, SearchOption.TopDirectoryOnly))
                {
                    if (!files.Contains(file))
                    {
                        files.Add(file);
                    }
                }

                if (recursive)
                {
                    foreach (string subDir in Directory.GetDirectories(directory))
                    {
                        foreach (string file in Directory.GetFiles(subDir, filePattern, SearchOption.TopDirectoryOnly))
                        {
                            if (!files.Contains(file))
                            {
                                files.Add(file);
                            }
                        }
                    }
                }
            }
            else
            {
                string fullPath = Path.Combine(directory, pattern);
                if (File.Exists(fullPath) && !files.Contains(fullPath))
                {
                    files.Add(fullPath);
                }
            }
        }

        // Apply exclude patterns
        foreach (string exclude in project.Exclude)
        {
            string excludePattern = Path.GetFileName(exclude);
            bool recursive = exclude.Contains("**");
            string? excludeSubDir = recursive ? null : Path.GetDirectoryName(exclude);
            HashSet<string> excluded = new(StringComparer.OrdinalIgnoreCase);
            if (!recursive)
            {
                string excludeDir = Path.Combine(directory, excludeSubDir ?? "");
                if (Directory.Exists(excludeDir))
                {
                    foreach (string f in Directory.GetFiles(excludeDir, excludePattern, SearchOption.TopDirectoryOnly))
                    {
                        excluded.Add(f);
                    }
                }
            }
            else
            {
                foreach (string f in Directory.GetFiles(directory, excludePattern, SearchOption.TopDirectoryOnly))
                {
                    excluded.Add(f);
                }

                foreach (string subDir in Directory.GetDirectories(directory))
                {
                    foreach (string f in Directory.GetFiles(subDir, excludePattern, SearchOption.TopDirectoryOnly))
                    {
                        excluded.Add(f);
                    }
                }
            }
            files.RemoveAll(f => excluded.Contains(f));
        }

        files.Sort(StringComparer.Ordinal);
        return files.ToArray();
    }

    /// <summary>
    /// The quire a file belongs to, relative to <paramref name="codexRoot"/>.
    /// Returns null for files sitting directly in the codex root; otherwise
    /// returns the top-level subdirectory basename.
    /// </summary>
    internal static string? QuireNameFor(string filePath, string codexRoot)
    {
        string fullFile = Path.GetFullPath(filePath);
        string fullRoot = Path.GetFullPath(codexRoot);
        string rel = Path.GetRelativePath(fullRoot, fullFile);
        int sep = rel.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
        return sep < 0 ? null : rel.Substring(0, sep);
    }
}
