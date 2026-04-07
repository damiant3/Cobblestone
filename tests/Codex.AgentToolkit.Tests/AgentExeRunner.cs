using System.Diagnostics;
using System.Runtime.InteropServices;

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

    string BuiltToolPath(string toolName)
    {
        string stem = Path.GetFileNameWithoutExtension(toolName);
        string sourcePath = Path.Combine(m_solutionRoot, "tools", "codex-agent", $"{stem}.cs");
        if (!File.Exists(sourcePath))
            return ExePath(toolName);

        string buildDir = Path.Combine(TestDir, "tool-build", stem);
        Directory.CreateDirectory(buildDir);

        string projectPath = Path.Combine(buildDir, $"{stem}.csproj");
        string sourceInclude = sourcePath.Replace("&", "&amp;").Replace("\"", "&quot;");
        string projectText =
$"""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <ImportDirectoryBuildProps>false</ImportDirectoryBuildProps>
    <ImportDirectoryBuildTargets>false</ImportDirectoryBuildTargets>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <AssemblyName>{stem}</AssemblyName>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <UseAppHost>true</UseAppHost>
    <Nullable>disable</Nullable>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="{sourceInclude}" Link="{stem}.cs" />
  </ItemGroup>
</Project>
""";

        if (!File.Exists(projectPath) || File.ReadAllText(projectPath) != projectText)
            File.WriteAllText(projectPath, projectText);

        string outputDir = Path.Combine(buildDir, "bin", "Debug", "net8.0");
        string outputDll = Path.Combine(outputDir, $"{stem}.dll");
        DateTime sourceWrite = File.GetLastWriteTimeUtc(sourcePath);
        bool needsBuild = !File.Exists(outputDll) || File.GetLastWriteTimeUtc(outputDll) < sourceWrite;
        if (needsBuild)
        {
            ProcessStartInfo buildPsi = new("dotnet", $"build \"{projectPath}\" -nologo -clp:ErrorsOnly")
            {
                WorkingDirectory = buildDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using Process build = Process.Start(buildPsi)!;
            string stdout = build.StandardOutput.ReadToEnd();
            string stderr = build.StandardError.ReadToEnd();
            build.WaitForExit(30_000);
            if (build.ExitCode != 0)
                throw new InvalidOperationException(
                    $"Failed to build {toolName} from current source.{Environment.NewLine}{stdout}{stderr}");
        }

        return Path.Combine(outputDir, toolName);
    }

    public (int ExitCode, string StdOut, string StdErr) Run(string exe, params string[] args)
        => RunFrom(m_solutionRoot, exe, args);

    public (int ExitCode, string StdOut, string StdErr) RunFrom(
        string workingDirectory,
        string exe,
        params string[] args)
    {
        string exePath = BuiltToolPath(exe);

        // Prefer the native apphost .exe if it exists and we're on Windows,
        // fall back to dotnet + .dll on Linux/macOS (apphost is a Windows PE binary)
        string dllPath = Path.ChangeExtension(exePath, ".dll");
        ProcessStartInfo psi;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            && File.Exists(exePath) && File.Exists(dllPath))
        {
            // Native apphost — run directly (Windows only)
            psi = new(exePath, string.Join(' ', args.Select(a => $"\"{a}\"")));
        }
        else if (File.Exists(dllPath))
        {
            // Cross-platform: run managed DLL via dotnet
            psi = new("dotnet", $"\"{dllPath}\" {string.Join(' ', args.Select(a => $"\"{a}\""))}");
        }
        else
        {
            // Legacy: framework-dependent managed .exe, invoke via dotnet
            psi = new("dotnet", $"\"{exePath}\" {string.Join(' ', args.Select(a => $"\"{a}\""))}");
        }

        psi.WorkingDirectory = workingDirectory;
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
