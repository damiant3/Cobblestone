[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PromptFile,
    [string]$SparkDir = 'D:\Projects\Spark\Spark'
)

# The oracle for codex/test/apps/spark-prompt-file: the WPF Spark's own
# PromptParser.cs, unmodified, built into a temp console program that prints
# every prompt as the Codex port (apps/spark/SparkPromptFile.codex) does. The
# prompt file is read with carriage returns removed, because CCE carries none.
#
#   pwsh build/spark-prompt-oracle.ps1 -PromptFile <file> > codex/test/apps/spark-prompt-file.expected

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$parser = Join-Path $SparkDir 'PromptParser.cs'
if (-not (Test-Path -PathType Leaf $parser)) { Write-Host "MISSING: $parser (the original Spark source)"; exit 2 }
$work = Join-Path $env:TEMP 'spark-prompt-oracle'
New-Item -ItemType Directory -Force $work | Out-Null
Copy-Item -Force $parser (Join-Path $work 'PromptParser.cs')
Set-Content (Join-Path $work 'ppo.csproj') '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><RootNamespace>Spark</RootNamespace></PropertyGroup></Project>'
Set-Content (Join-Path $work 'Program.cs') @'
using System.Globalization;
namespace Spark;
static class Program {
    static string E(string s) => s.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n");
    static int Main(string[] a) {
        var ps = PromptParser.Parse(a[0]);
        var o = new System.Text.StringBuilder();
        o.Append("prompts " + ps.Count + "\n");
        foreach (var p in ps) {
            o.Append("P " + p.Number + " | " + E(p.Title) + " | " + E(p.Series) + " | " + (p.Lora ?? "-") + " | " + p.LoraWeight.ToString("F2", CultureInfo.InvariantCulture) + " | " + p.Filename + "\n");
            o.Append("S " + E(p.Scene) + "\n");
            o.Append("T " + E(p.Style) + "\n");
        }
        Console.Out.Write(o.ToString());
        return 0;
    }
}
'@
& dotnet build $work -c Release -v q -o (Join-Path $work 'bin') | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host 'oracle build failed'; exit 2 }
$lf = Join-Path $work 'input.txt'
[IO.File]::WriteAllText($lf, [IO.File]::ReadAllText($PromptFile).Replace("`r", ''), [Text.UTF8Encoding]::new($false))
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
& (Join-Path $work 'bin\ppo.exe') $lf
