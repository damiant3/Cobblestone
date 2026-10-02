[CmdletBinding()]
param(
    [string]$TestFile = (Join-Path $PSScriptRoot '..\codex\test\apps\spark-documents.codex'),
    [string]$SparkDir = 'D:\Projects\Spark\Spark'
)

# The oracle for codex/test/apps/spark-documents: the WPF Spark's own
# DocumentStore.cs, unmodified, ingesting the scenario's documents ("# name"
# and the lines under it) from a fresh folder and asked each "? prompt",
# printing what the Codex port prints. The scenario is the test chapter's
# sdt-scenario literal.
#
#   pwsh build/spark-document-oracle.ps1 > codex/test/apps/spark-documents.expected

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$work = Join-Path $env:TEMP 'spark-document-oracle'
New-Item -ItemType Directory -Force $work | Out-Null
$src = Get-ChildItem $SparkDir -Recurse -Filter 'DocumentStore.cs' | Select-Object -First 1
if (-not $src) { Write-Host "MISSING: DocumentStore.cs under $SparkDir (the original Spark source)"; exit 2 }
Copy-Item -Force $src.FullName $work
Set-Content (Join-Path $work 'o.csproj') '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><RootNamespace>Spark</RootNamespace></PropertyGroup></Project>'
Set-Content (Join-Path $work 'Program.cs') @'
namespace Spark;
static class Program {
    static string Esc(string s) => s.Replace("\n", "\\n");
    static int Main(string[] a) {
        var dir = Path.Combine(Path.GetTempPath(), "spark-doc-oracle-" + Environment.ProcessId);
        Directory.CreateDirectory(dir);
        var queries = new List<string>();
        string? name = null; var body = new List<string>();
        void Flush() { if (name != null) File.WriteAllText(Path.Combine(dir, name), string.Join("\n", body)); name = null; body.Clear(); }
        foreach (var line in File.ReadAllText(a[0]).Split('\n')) {
            if (line.StartsWith("# ")) { Flush(); name = line[2..]; }
            else if (line.StartsWith("? ")) { Flush(); queries.Add(line[2..]); }
            else body.Add(line);
        }
        Flush();
        var store = new DocumentStore(dir);
        store.Ingest([]);
        var o = new System.Text.StringBuilder();
        foreach (var e in store.Entries)
            o.Append("D " + e.FileName + " | " + e.Role + " | " + e.Keywords.Length + " | " + string.Join(",", e.Keywords.Take(12)) + "\n");
        foreach (var q in queries) {
            o.Append("Q " + q + "\n");
            foreach (var (source, chunk, _) in store.FindRelevant(q, 5)) o.Append("R " + source + " | " + Esc(chunk) + "\n");
            o.Append("C " + Esc(store.BuildContext(q)) + "\n");
        }
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
$m = [regex]::Match([IO.File]::ReadAllText($TestFile), '(?m)^  sdt-scenario = "([^"\\]*(?:\\n[^"\\]*)*)"')
if (-not $m.Success) { Write-Host "no sdt-scenario literal in $TestFile"; exit 2 }
[IO.File]::WriteAllText($lf, $m.Groups[1].Value.Replace('\n', "`n"), [Text.UTF8Encoding]::new($false))
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
& (Join-Path $work 'bin\o.exe') $lf
