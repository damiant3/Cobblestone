[CmdletBinding()]
param(
    [string]$TestFile = (Join-Path $PSScriptRoot '..\codex\test\apps\spark-pref-json.codex'),
    [string]$SparkDir = 'D:\Projects\Spark\Spark'
)

# The oracle for codex/test/apps/spark-pref-json: the WPF Spark's own
# PreferenceTracker.cs and ImageRecord.cs, unmodified, loading the test
# chapter's spj-file literal as Concept/preferences.json, printing its state
# twice in the Codex port's format (the port prints it before and after a
# round trip through its own writer).
#
#   pwsh build/spark-pref-json-oracle.ps1 > codex/test/apps/spark-pref-json.expected

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$work = Join-Path $env:TEMP 'spark-pref-json-oracle'
New-Item -ItemType Directory -Force $work | Out-Null
foreach ($f in 'PreferenceTracker.cs', 'ImageRecord.cs') {
    $src = Get-ChildItem $SparkDir -Recurse -Filter $f | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } | Select-Object -First 1
    if (-not $src) { Write-Host "MISSING: $f under $SparkDir (the original Spark source)"; exit 2 }
    Copy-Item -Force $src.FullName $work
}
Set-Content (Join-Path $work 'o.csproj') '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><RootNamespace>Spark</RootNamespace></PropertyGroup></Project>'
Set-Content (Join-Path $work 'Program.cs') @"
using System.Globalization;
using System.Reflection;
namespace Spark;
static class Program {
    static int Main(string[] a) {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var t = new PreferenceTracker(a[0]);
        var st = (PreferenceState)typeof(PreferenceTracker).GetField("m_state", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(t)!;
        var o = new System.Text.StringBuilder();
        foreach (var kv in st.Weights) o.Append("W " + kv.Key + " " + kv.Value.ToString() + "\n");
        o.Append("T " + st.TotalPositive + " " + st.TotalNegative + "\n");
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        Console.Out.Write(o.ToString() + o.ToString());
        return 0;
    }
}
"@
& dotnet build $work -c Release -v q -o (Join-Path $work 'bin') | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host 'oracle build failed'; exit 2 }
$m = [regex]::Match([IO.File]::ReadAllText($TestFile), '(?m)^  spj-file = "((?:[^"\\]|\\.)*)"')
if (-not $m.Success) { Write-Host "no spj-file literal in $TestFile"; exit 2 }
$json = [regex]::Replace($m.Groups[1].Value, '\\(.)', { param($x) if ($x.Groups[1].Value -eq 'n') { "`n" } else { $x.Groups[1].Value } })
$dir = Join-Path $work 'concept'
New-Item -ItemType Directory -Force $dir | Out-Null
[IO.File]::WriteAllText((Join-Path $dir 'preferences.json'), $json, [Text.UTF8Encoding]::new($false))
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
& (Join-Path $work 'bin\o.exe') $dir