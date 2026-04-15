using System.Diagnostics;

namespace Codex.AgentToolkit.Tests;

sealed class AgentExeRunner
{
    readonly string m_solutionRoot;

    public AgentExeRunner()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "Codex.sln")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        m_solutionRoot = dir ?? throw new InvalidOperationException("Could not find solution root");
    }

    public (int ExitCode, string StdOut, string StdErr) Run(string exe, params string[] args)
        => RunFrom(m_solutionRoot, exe, args);

    public (int ExitCode, string StdOut, string StdErr) RunFrom(
        string workingDirectory,
        string exe,
        params string[] args)
    {
        // Use the IL-compiled .dll directly — the real artifact, built from
        // .codex source via the Codex compiler's IL backend. No CS transpilation.
        string stem = Path.GetFileNameWithoutExtension(exe);
        string dllPath = Path.Combine(m_solutionRoot, "tools", "codex-agent", $"{stem}.dll");

        if (!File.Exists(dllPath))
        {
            throw new InvalidOperationException($"IL-compiled tool not found: {dllPath}");
        }

        ProcessStartInfo psi = new("dotnet",
            $"\"{dllPath}\" {string.Join(' ', args.Select(a => $"\"{a}\""))}")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using Process proc = Process.Start(psi)!;
        string stdout = proc.StandardOutput.ReadToEnd();
        string stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(30_000);
        return (proc.ExitCode, stdout, stderr);
    }

    public string SolutionRoot => m_solutionRoot;

    public string CreateTempFile(string name, string content)
    {
        string dir = Path.Combine(m_solutionRoot, ".codex-agent-test", Guid.NewGuid().ToString("N")[..8]);
        string path = Path.Combine(dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public string RelativePath(string absolutePath) =>
        Path.GetRelativePath(m_solutionRoot, absolutePath).Replace('\\', '/');

    public void CleanupTestDir()
    {
        string parent = Path.Combine(m_solutionRoot, ".codex-agent-test");
        if (Directory.Exists(parent))
        {
            foreach (string dir in Directory.GetDirectories(parent))
            {
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
            if (Directory.GetFileSystemEntries(parent).Length == 0)
            {
                Directory.Delete(parent);
            }
        }
    }
}
