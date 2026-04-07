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
        if (!File.Exists(projectPath)) return null;

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
        if (!File.Exists(lockPath)) return null;

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
                string searchDir = directory;
                SearchOption searchOpt = pattern.Contains("**")
                    ? SearchOption.AllDirectories
                    : SearchOption.TopDirectoryOnly;
                string filePattern = Path.GetFileName(pattern);
                foreach (string file in Directory.GetFiles(searchDir, filePattern, searchOpt))
                {
                    if (!files.Contains(file))
                        files.Add(file);
                }
            }
            else
            {
                string fullPath = Path.Combine(directory, pattern);
                if (File.Exists(fullPath) && !files.Contains(fullPath))
                    files.Add(fullPath);
            }
        }

        // Apply exclude patterns
        foreach (string exclude in project.Exclude)
        {
            string excludePattern = Path.GetFileName(exclude);
            bool recursive = exclude.Contains("**");
            SearchOption excludeOpt = recursive
                ? SearchOption.AllDirectories
                : SearchOption.TopDirectoryOnly;
            string excludeDir = recursive ? directory
                : Path.Combine(directory, Path.GetDirectoryName(exclude) ?? "");
            if (Directory.Exists(excludeDir))
            {
                HashSet<string> excluded = new(
                    Directory.GetFiles(excludeDir, excludePattern, excludeOpt),
                    StringComparer.OrdinalIgnoreCase);
                files.RemoveAll(f => excluded.Contains(f));
            }
        }

        files.Sort(StringComparer.Ordinal);
        return files.ToArray();
    }
}
