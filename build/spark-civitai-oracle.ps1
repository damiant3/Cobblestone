[CmdletBinding()]
param(
    [string]$TestFile = (Join-Path $PSScriptRoot '..\codex\test\apps\spark-civitai.codex'),
    [string]$SparkRoot = 'D:\Projects\Spark'
)

# The oracle for codex/test/apps/spark-civitai: the WPF Spark's own
# CivitAiClient.cs, HttpServiceClient.cs and ServiceMarkers.cs, unmodified,
# pointed at a local listener that answers with the test chapter's literals.
# It prints the path each query requests at both sorts, each search answer's
# rows and each by-hash answer's trigger words, in the Codex port's format.
#
#   pwsh build/spark-civitai-oracle.ps1 > codex/test/apps/spark-civitai.expected

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$work = Join-Path $env:TEMP 'spark-civitai-oracle'
New-Item -ItemType Directory -Force $work | Out-Null
foreach ($rel in 'Spark\Services\CivitAiClient.cs', 'Common.Core\Net\HttpServiceClient.cs', 'Common.Core\Net\ServiceMarkers.cs') {
    $src = Join-Path $SparkRoot $rel
    if (-not (Test-Path $src)) { Write-Host "MISSING: $src (the original Spark source)"; exit 2 }
    Copy-Item -Force $src $work
}
$text = [IO.File]::ReadAllText($TestFile)
$lit = {
    param($name)
    $m = [regex]::Match($text, '(?m)^  ' + [regex]::Escape($name) + ' = "((?:[^"\\]|\\.)*)"')
    if (-not $m.Success) { return $null }
    [regex]::Replace($m.Groups[1].Value, '\\(.)', { param($x) if ($x.Groups[1].Value -eq 'n') { "`n" } else { $x.Groups[1].Value } })
}
$data = Join-Path $work 'data'
New-Item -ItemType Directory -Force $data | Out-Null
Get-ChildItem $data | Remove-Item -Force
foreach ($kind in 'query', 'search', 'hash') {
    $i = if ($kind -eq 'query') { 0 } else { 1 }
    while ($null -ne ($v = & $lit "civ-$kind-$i")) {
        [IO.File]::WriteAllText((Join-Path $data "$kind-$i.txt"), $v, [Text.UTF8Encoding]::new($false))
        $i++
    }
}
Set-Content (Join-Path $work 'o.csproj') '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><RootNamespace>Spark</RootNamespace></PropertyGroup></Project>'
Set-Content (Join-Path $work 'Program.cs') @'
using System.Globalization;
using System.Net;
using System.Text;
using Common.Core.Net;
using Spark.Services;
namespace Spark;
static class Program {
    static string last = "";
    static int Main(string[] a) {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string dir = a[0];
        int port = int.Parse(a[1]);
        var l = new HttpListener();
        l.Prefixes.Add($"http://localhost:{port}/");
        l.Start();
        _ = Task.Run(async () => {
            while (true) {
                var c = await l.GetContextAsync();
                last = c.Request.RawUrl!.TrimStart('/');
                string? body = null;
                string q = c.Request.QueryString["query"] ?? "";
                string p = c.Request.Url!.AbsolutePath;
                if (p == "/api/v1/models" && q.StartsWith("fixture")) body = File.ReadAllText(Path.Combine(dir, "search-" + q.Substring(7) + ".txt"));
                else if (p.StartsWith("/api/v1/model-versions/by-hash/h")) body = File.ReadAllText(Path.Combine(dir, "hash-" + p.Substring(32) + ".txt"));
                byte[] b = Encoding.UTF8.GetBytes(body ?? "not found");
                c.Response.StatusCode = body is null ? 404 : 200;
                c.Response.ContentType = "application/json";
                c.Response.OutputStream.Write(b);
                c.Response.Close();
            }
        });
        var cli = new CivitAiClient(new ServiceUri<CivitAiApi>($"http://localhost:{port}"));
        var o = new StringBuilder();
        for (int i = 0; File.Exists(Path.Combine(dir, $"query-{i}.txt")); i++) {
            string q = File.ReadAllText(Path.Combine(dir, $"query-{i}.txt"));
            foreach (bool s in new[] { false, true }) { cli.SearchLorasAsync(q, s).GetAwaiter().GetResult(); o.Append("U " + last + "\n"); }
        }
        cli.GetTriggerWordsByHashAsync("0A1B2C3D4E").GetAwaiter().GetResult();
        o.Append("U " + last + "\n");
        for (int i = 1; File.Exists(Path.Combine(dir, $"search-{i}.txt")); i++) {
            var rs = cli.SearchLorasAsync("fixture" + i).GetAwaiter().GetResult();
            o.Append("S " + i + " " + rs.Count + "\n");
            foreach (var r in rs)
                o.Append("R " + r.Name + "|" + r.BaseModel + "|" + r.Downloads + "|" + (long)Math.Round(r.Rating * 10) + "|" + r.Tags + "|" + r.TriggerWords + "|" + r.ThumbnailUrl + "|" + r.DownloadUrl + "|" + r.FileName + "|" + (r.IsSdxlCompatible ? "1" : "0") + "|" + r.ModelPageUrl + "\n");
        }
        for (int i = 1; File.Exists(Path.Combine(dir, $"hash-{i}.txt")); i++)
            o.Append("H " + i + " " + cli.GetTriggerWordsByHashAsync("h" + i).GetAwaiter().GetResult() + "\n");
        Console.OutputEncoding = new UTF8Encoding(false);
        Console.Out.Write(o.ToString());
        l.Stop();
        return 0;
    }
}
'@
& dotnet build $work -c Release -v q -o (Join-Path $work 'bin') | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host 'oracle build failed'; exit 2 }
$tl = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0); $tl.Start(); $port = $tl.LocalEndpoint.Port; $tl.Stop()
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
& (Join-Path $work 'bin\o.exe') $data $port
