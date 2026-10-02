[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ScenarioFile,
    [string]$SparkDir = 'D:\Projects\Spark\Spark'
)

# The oracle for codex/test/apps/spark-preferences: the WPF Spark's own
# PreferenceTracker.cs and ImageRecord.cs, unmodified, driven by a scenario
# ("+ q | preset | text" rates positively with strength q/4, "-" negatively,
# "? text" adjusts a prompt) and printing what the Codex port prints.
#
#   pwsh build/spark-preference-oracle.ps1 -ScenarioFile <file> > codex/test/apps/spark-preferences.expected

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$work = Join-Path $env:TEMP 'spark-preference-oracle'
New-Item -ItemType Directory -Force $work | Out-Null
foreach ($f in 'PreferenceTracker.cs', 'ImageRecord.cs') {
    $src = Get-ChildItem $SparkDir -Recurse -Filter $f | Select-Object -First 1
    if (-not $src) { Write-Host "MISSING: $f under $SparkDir (the original Spark source)"; exit 2 }
    Copy-Item -Force $src.FullName $work
}
Set-Content (Join-Path $work 'o.csproj') '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><RootNamespace>Spark</RootNamespace></PropertyGroup></Project>'
Set-Content (Join-Path $work 'Program.cs') @'
using System.Globalization;
namespace Spark;
static class Program {
    static int Main(string[] a) {
        var dir = Path.Combine(Path.GetTempPath(), "spark-pref-oracle-" + Environment.ProcessId);
        var t = new PreferenceTracker(dir);
        var o = new System.Text.StringBuilder();
        foreach (var line in File.ReadAllText(a[0]).Split('\n')) {
            if (line.StartsWith("? ")) { var (p, e) = t.AdjustPrompt(line[2..]); o.Append("A " + p + " || " + e + "\n"); continue; }
            var parts = line[2..].Split(" | ");
            var r = new ImageRecord { PromptText = parts[2], Style = "", RefinePreset = parts[1] };
            double s = int.Parse(parts[0]) / 4.0;
            if (line[0] == '+') t.RecordPositive(r, s); else t.RecordNegative(r, s);
        }
        o.Append("S " + t.SuggestedPreset() + "\n");
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        Console.Out.Write(o.ToString());
        Directory.Delete(dir, true);
        return 0;
    }
}
'@
& dotnet build $work -c Release -v q -o (Join-Path $work 'bin') | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host 'oracle build failed'; exit 2 }
$lf = Join-Path $work 'scenario.txt'
[IO.File]::WriteAllText($lf, [IO.File]::ReadAllText($ScenarioFile).Replace("`r", '').TrimEnd(), [Text.UTF8Encoding]::new($false))
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
& (Join-Path $work 'bin\o.exe') $lf