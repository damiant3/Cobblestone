using Codex.Core;
using Codex.Semantics;

namespace Codex.Cli;

sealed class PackageResolver(string projectDirectory, DiagnosticBag diagnostics)
{
    readonly string m_projectDirectory = projectDirectory;
    readonly DiagnosticBag m_diagnostics = diagnostics;

    /// <summary>
    /// Resolves all package references for a project into module loaders.
    /// Returns loaders in dependency order (foreword first, then explicit packages).
    /// </summary>
    public List<IChapterLoader> ResolveAll(Program.CodexProject project)
    {
        List<IChapterLoader> loaders = [];

        // Always include prelude unless this IS the foreword
        if (!project.Foreword)
        {
            ForewordChapterLoader? foreword = ForewordChapterLoader.TryCreate(m_diagnostics);
            if (foreword is not null)
            {
                loaders.Add(foreword);
            }
        }

        // Resolve explicit path dependencies (existing behavior)
        foreach (string dep in project.Dependencies)
        {
            ProjectChapterLoader? depLoader = ProjectChapterLoader.TryCreate(
                dep, m_projectDirectory, m_diagnostics);
            if (depLoader is not null)
            {
                loaders.Add(depLoader);
            }
            else
            {
                Console.Error.WriteLine($"  warning: Cannot resolve dependency '{dep}'");
            }
        }

        // Resolve named packages
        foreach (Program.PackageRef pkg in project.Packages)
        {
            IChapterLoader? loader = ResolvePackage(pkg);
            if (loader is not null)
            {
                loaders.Add(loader);
            }
            else
            {
                Console.Error.WriteLine($"  warning: Cannot resolve package '{pkg.Name}' {pkg.Version}");
            }
        }

        return loaders;
    }

    /// <summary>
    /// Resolves a single package reference. Tries in order:
    /// 1. Explicit path (if specified)
    /// 2. Local cache (~/.codex/packages/)
    /// 3. Sibling directory (../PackageName)
    /// </summary>
    IChapterLoader? ResolvePackage(Program.PackageRef pkg)
    {
        // Explicit path takes priority
        if (!string.IsNullOrEmpty(pkg.Path))
        {
            return ProjectChapterLoader.TryCreate(pkg.Path, m_projectDirectory, m_diagnostics);
        }

        // Check local cache
        string? cachePath = FindInCache(pkg.Name, pkg.Version);
        if (cachePath is not null)
        {
            return ProjectChapterLoader.TryCreate(cachePath, "/", m_diagnostics);
        }

        // Check sibling directories (convention: ../PackageName)
        string siblingPath = Path.Combine(m_projectDirectory, "..", pkg.Name);
        if (Directory.Exists(siblingPath))
        {
            string fullSibling = Path.GetFullPath(siblingPath);
            Program.CodexProject? siblingProject = Program.LoadProjectFile(fullSibling);
            if (siblingProject is not null && VersionMatches(siblingProject.Version, pkg.Version))
            {
                return ProjectChapterLoader.TryCreate(
                    fullSibling, "/", m_diagnostics);
            }
        }

        return null;
    }

    /// <summary>
    /// Finds a package in the local cache directory.
    /// Cache layout: ~/.codex/packages/{name}/{version}/
    /// </summary>
    static string? FindInCache(string name, string versionConstraint)
    {
        string cacheRoot = GetCacheDirectory();
        string packageDir = Path.Combine(cacheRoot, name);
        if (!Directory.Exists(packageDir))
        {
            return null;
        }

        // Find best matching version
        string[] versions = Directory.GetDirectories(packageDir)
            .Select(d => Path.GetFileName(d))
            .Where(v => VersionMatches(v, versionConstraint))
            .OrderByDescending(v => v, StringComparer.Ordinal)
            .ToArray();

        if (versions.Length == 0)
        {
            return null;
        }

        string bestVersion = versions[0];
        string resolved = Path.Combine(packageDir, bestVersion);
        return File.Exists(Path.Combine(resolved, "codex.project.json"))
            ? resolved
            : null;
    }

    /// <summary>
    /// Installs a package from a source directory into the local cache.
    /// Returns the cache path.
    /// </summary>
    public static string InstallToCache(string sourceDirectory, Program.CodexProject project)
    {
        string cacheRoot = GetCacheDirectory();
        string targetDir = Path.Combine(cacheRoot, project.Name, project.Version);

        if (Directory.Exists(targetDir))
        {
            Directory.Delete(targetDir, recursive: true);
        }

        Directory.CreateDirectory(targetDir);

        // Copy project file
        string sourceProject = Path.Combine(sourceDirectory, "codex.project.json");
        if (File.Exists(sourceProject))
        {
            File.Copy(sourceProject, Path.Combine(targetDir, "codex.project.json"));
        }

        // Copy source files
        string[] sources = Program.ResolveProjectSources(sourceDirectory, project);
        foreach (string source in sources)
        {
            string relativePath = Path.GetRelativePath(sourceDirectory, source);
            string targetPath = Path.Combine(targetDir, relativePath);
            string? targetSubDir = Path.GetDirectoryName(targetPath);
            if (targetSubDir is not null && !Directory.Exists(targetSubDir))
            {
                Directory.CreateDirectory(targetSubDir);
            }

            File.Copy(source, targetPath, overwrite: true);
        }

        return targetDir;
    }

    /// <summary>
    /// Creates a content hash for a package directory (for lock file).
    /// Uses the same content-addressing scheme as the Codex repository.
    /// </summary>
    public static string ComputeContentHash(string directory, Program.CodexProject project)
    {
        string[] sources = Program.ResolveProjectSources(directory, project);
        using System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create();

        foreach (string source in sources)
        {
            byte[] content = File.ReadAllBytes(source);
            sha.TransformBlock(content, 0, content.Length, null, 0);
        }

        sha.TransformFinalBlock([], 0, 0);
        return Convert.ToHexString(sha.Hash!).ToLowerInvariant();
    }

    /// <summary>
    /// Simple version matching. Supports:
    /// - "*" matches anything
    /// - "0.1.0" exact match
    /// - "0.1.*" prefix match
    /// - ">=0.1.0" minimum version
    /// </summary>
    static bool VersionMatches(string actual, string constraint)
    {
        if (string.IsNullOrEmpty(constraint) || constraint == "*")
        {
            return true;
        }

        if (constraint.EndsWith(".*"))
        {
            string prefix = constraint[..^2];
            return actual.StartsWith(prefix, StringComparison.Ordinal);
        }

        if (constraint.StartsWith(">="))
        {
            string min = constraint[2..];
            return string.Compare(actual, min, StringComparison.Ordinal) >= 0;
        }

        return string.Equals(actual, constraint, StringComparison.Ordinal);
    }

    public static string GetCacheDirectory()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home))
        {
            home = Environment.GetEnvironmentVariable("HOME") ?? "/tmp";
        }

        return Path.Combine(home, ".codex", "packages");
    }
}
