using System.Diagnostics;

namespace Codex.AgentToolkit.Tests;

sealed class AgentExeRunner
{
    readonly string m_solutionRoot;
    readonly string m_testId = Guid.NewGuid().ToString("N")[..8];

    public AgentExeRunner()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "Codex.sln")))
            dir = Path.GetDirectoryName(dir);
        m_solutionRoot = dir ?? throw new InvalidOperationException("Could not find solution root");
    }

    string ExePath(string toolName) =>
        Path.Combine(m_solutionRoot, "tools", "codex-agent", toolName);

    public (int ExitCode, string StdOut, string StdErr) Run(string exe, params string[] args)
    {
        string exePath = ExePath(exe);

        // Prefer the native apphost .exe if it exists, fall back to dotnet + .dll
        string dllPath = Path.ChangeExtension(exePath, ".dll");
        ProcessStartInfo psi;
        if (File.Exists(exePath) && File.Exists(dllPath))
        {
            // Native apphost — run directly
            psi = new(exePath, string.Join(' ', args.Select(a => $"\"{a}\"")));
        }
        else
        {
            // Legacy: framework-dependent managed .exe, invoke via dotnet
            psi = new("dotnet", $"\"{exePath}\" {string.Join(' ', args.Select(a => $"\"{a}\""))}");
        }

        psi.WorkingDirectory = m_solutionRoot;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;

        using Process proc = Process.Start(psi)!;
        string stdout = proc.StandardOutput.ReadToEnd();
        string stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(30_000);
        return (proc.ExitCode, stdout, stderr);
    }

    public string SolutionRoot => m_solutionRoot;

    string TestDir => Path.Combine(m_solutionRoot, ".codex-agent-test", m_testId);

    public string CreateTempFile(string name, string content)
    {
        string path = Path.Combine(TestDir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public string RelativePath(string absolutePath) =>
        Path.GetRelativePath(m_solutionRoot, absolutePath).Replace('\\', '/');

    public void CleanupTestDir()
    {
        if (Directory.Exists(TestDir))
            Directory.Delete(TestDir, recursive: true);

        string parent = Path.Combine(m_solutionRoot, ".codex-agent-test");
        if (Directory.Exists(parent) && Directory.GetFileSystemEntries(parent).Length == 0)
            Directory.Delete(parent);
    }
}
