using Codex.Emit.CSharp;

namespace Codex.Cli;

public static partial class Program
{
    static int RunRun(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: codex run <file.codex>");
            return 1;
        }

        string filePath = args[0];
        CompilationResult? result = CompileFile(filePath);
        if (result is null)
        {
            return 1;
        }

        string tempDir = Path.Combine(Path.GetTempPath(), "codex_run_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);

        try
        {
            string csFile = Path.Combine(tempDir, "Program.cs");
            File.WriteAllText(csFile, result.CSharpSource);

            string csproj = Path.Combine(tempDir, "CodexOutput.csproj");
            File.WriteAllText(csproj, GenerateCsproj());

            System.Diagnostics.ProcessStartInfo buildInfo = new("dotnet", "build --nologo --verbosity quiet")
            {
                WorkingDirectory = tempDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            System.Diagnostics.Process? buildProc = System.Diagnostics.Process.Start(buildInfo);
            if (buildProc is null)
            {
                Console.Error.WriteLine("Failed to start dotnet build");
                return 1;
            }

            string buildStdout = buildProc.StandardOutput.ReadToEnd();
            string buildStderr = buildProc.StandardError.ReadToEnd();
            buildProc.WaitForExit();

            if (buildProc.ExitCode != 0)
            {
                Console.Error.WriteLine("C# compilation failed:");
                Console.Error.WriteLine(buildStdout);
                Console.Error.WriteLine(buildStderr);
                return 1;
            }

            System.Diagnostics.ProcessStartInfo runInfo = new("dotnet", "run --no-build --nologo")
            {
                WorkingDirectory = tempDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            System.Diagnostics.Process? runProc = System.Diagnostics.Process.Start(runInfo);
            if (runProc is null)
            {
                Console.Error.WriteLine("Failed to start dotnet run");
                return 1;
            }

            string output = runProc.StandardOutput.ReadToEnd();
            string errOutput = runProc.StandardError.ReadToEnd();
            runProc.WaitForExit();

            if (output.Length > 0)
            {
                Console.Write(output);
            }

            if (errOutput.Length > 0)
            {
                Console.Error.Write(errOutput);
            }

            return runProc.ExitCode;
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* best effort cleanup */ }
        }
    }

    static string GenerateCsproj()
    {
        return """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net8.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """;
    }
}
