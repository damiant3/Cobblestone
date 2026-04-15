namespace Codex.Cli;

public static partial class Program
{
    static int RunAdd(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: codex add <package> [--version <ver>] [--path <dir>]");
            return 1;
        }

        string directory = Directory.GetCurrentDirectory();
        CodexProject? project = LoadProjectFile(directory);
        if (project is null)
        {
            Console.Error.WriteLine("No codex.project.json found in current directory.");
            Console.Error.WriteLine("Run 'codex init' to create one.");
            return 1;
        }

        string packageName = args[0];
        string version = "*";
        string path = "";

        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] is "--version" or "-v" && i + 1 < args.Length)
            {
                version = args[++i];
            }
            else if (args[i] is "--path" or "-p" && i + 1 < args.Length)
            {
                path = args[++i];
            }
        }

        // Check if already added
        foreach (PackageRef existing in project.Packages)
        {
            if (string.Equals(existing.Name, packageName, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"Updating {packageName} {existing.Version} → {version}");
                existing.Version = version;
                existing.Path = path;
                SaveProjectFile(directory, project);
                UpdateLockFile(directory, project);
                Console.WriteLine($"✓ Updated {packageName}");
                return 0;
            }
        }

        // Add new package
        PackageRef newRef = new() { Name = packageName, Version = version, Path = path };
        project.Packages = [.. project.Packages, newRef];
        SaveProjectFile(directory, project);
        UpdateLockFile(directory, project);

        Console.WriteLine($"✓ Added {packageName} {version}");
        return 0;
    }

    static int RunRemove(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: codex remove <package>");
            return 1;
        }

        string directory = Directory.GetCurrentDirectory();
        CodexProject? project = LoadProjectFile(directory);
        if (project is null)
        {
            Console.Error.WriteLine("No codex.project.json found in current directory.");
            return 1;
        }

        string packageName = args[0];
        List<PackageRef> remaining = [];
        bool found = false;

        foreach (PackageRef pkg in project.Packages)
        {
            if (string.Equals(pkg.Name, packageName, StringComparison.OrdinalIgnoreCase))
            {
                found = true;
            }
            else
            {
                remaining.Add(pkg);
            }
        }

        if (!found)
        {
            Console.Error.WriteLine($"Package '{packageName}' not found in project.");
            return 1;
        }

        project.Packages = remaining.ToArray();
        SaveProjectFile(directory, project);
        UpdateLockFile(directory, project);

        Console.WriteLine($"✓ Removed {packageName}");
        return 0;
    }

    static int RunPack(string[] args)
    {
        string directory = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
        directory = Path.GetFullPath(directory);

        CodexProject? project = LoadProjectFile(directory);
        if (project is null)
        {
            Console.Error.WriteLine($"No codex.project.json found in {directory}");
            return 1;
        }

        if (string.IsNullOrEmpty(project.Name))
        {
            Console.Error.WriteLine("Project must have a name to be packed.");
            return 1;
        }

        Console.WriteLine($"Packing {project.Name} {project.Version}...");

        string[] sources = ResolveProjectSources(directory, project);
        Console.WriteLine($"  Sources: {sources.Length} file(s)");

        // Install to local cache
        string cachePath = PackageResolver.InstallToCache(directory, project);
        string hash = PackageResolver.ComputeContentHash(cachePath, project);

        Console.WriteLine($"  Cache:   {cachePath}");
        Console.WriteLine($"  Hash:    {hash[..16]}...");
        Console.WriteLine($"✓ Packed {project.Name} {project.Version}");

        return 0;
    }

    static int RunListPackages(string[] args)
    {
        string directory = Directory.GetCurrentDirectory();
        CodexProject? project = LoadProjectFile(directory);

        // List project packages
        if (project is not null)
        {
            Console.WriteLine($"Project: {project.Name} {project.Version}");
            if (project.Dependencies.Length > 0)
            {
                Console.WriteLine("  Dependencies:");
                foreach (string dep in project.Dependencies)
                {
                    Console.WriteLine($"    {dep}");
                }
            }
            if (project.Packages.Length > 0)
            {
                Console.WriteLine("  Packages:");
                foreach (PackageRef pkg in project.Packages)
                {
                    string loc = string.IsNullOrEmpty(pkg.Path) ? "" : $" (path: {pkg.Path})";
                    Console.WriteLine($"    {pkg.Name} {pkg.Version}{loc}");
                }
            }
            if (project.Dependencies.Length == 0 && project.Packages.Length == 0)
            {
                Console.WriteLine("  (no dependencies)");
            }
        }

        // List cached packages
        string cacheDir = PackageResolver.GetCacheDirectory();
        if (args.Length > 0 && args[0] is "--cache" or "-c")
        {
            Console.WriteLine();
            Console.WriteLine($"Cache: {cacheDir}");
            if (Directory.Exists(cacheDir))
            {
                string[] packages = Directory.GetDirectories(cacheDir);
                if (packages.Length == 0)
                {
                    Console.WriteLine("  (empty)");
                }
                else
                {
                    foreach (string pkgDir in packages)
                    {
                        string name = Path.GetFileName(pkgDir);
                        string[] versions = Directory.GetDirectories(pkgDir)
                            .Select(Path.GetFileName)
                            .OrderByDescending(v => v, StringComparer.Ordinal)
                            .ToArray()!;
                        Console.WriteLine($"  {name}: {string.Join(", ", versions)}");
                    }
                }
            }
            else
            {
                Console.WriteLine("  (not created)");
            }
        }

        return 0;
    }

    static void UpdateLockFile(string directory, CodexProject project)
    {
        Codex.Core.DiagnosticBag diagnostics = new();
        PackageResolver resolver = new(directory, diagnostics);
        List<PackageLockEntry> entries = [];

        foreach (PackageRef pkg in project.Packages)
        {
            string resolvedPath = "";
            string contentHash = "";
            string resolvedVersion = pkg.Version;

            if (!string.IsNullOrEmpty(pkg.Path))
            {
                string fullPath = Path.GetFullPath(Path.Combine(directory, pkg.Path));
                CodexProject? depProject = LoadProjectFile(fullPath);
                if (depProject is not null)
                {
                    resolvedPath = fullPath;
                    resolvedVersion = depProject.Version;
                    contentHash = PackageResolver.ComputeContentHash(fullPath, depProject);
                }
            }
            else
            {
                string cacheDir = PackageResolver.GetCacheDirectory();
                string candidatePath = Path.Combine(cacheDir, pkg.Name, pkg.Version);
                if (Directory.Exists(candidatePath))
                {
                    resolvedPath = candidatePath;
                    CodexProject? cachedProject = LoadProjectFile(candidatePath);
                    if (cachedProject is not null)
                    {
                        contentHash = PackageResolver.ComputeContentHash(candidatePath, cachedProject);
                    }
                }
            }

            entries.Add(new PackageLockEntry
            {
                Name = pkg.Name,
                Version = resolvedVersion,
                ContentHash = contentHash,
                ResolvedPath = resolvedPath
            });
        }

        PackageLock lockFile = new()
        {
            ProjectName = project.Name,
            ProjectVersion = project.Version,
            Packages = entries.ToArray()
        };

        SaveLockFile(directory, lockFile);
    }
}
