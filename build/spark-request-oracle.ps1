[CmdletBinding()]
param(
    [string]$TestFile = (Join-Path $PSScriptRoot '..\codex\test\apps\spark-request.codex'),
    [string]$SparkRoot = 'D:\Projects\Spark'
)

# The oracle for codex/test/apps/spark-request: the WPF Spark's own
# ImageGenerator.cs, PromptParser.cs, ArtDirections.cs and CreativeEngine.cs,
# with Common.Core's HttpServiceClient, all unmodified. The scenario's JSON
# configs are written beside the program, where the original reads them.
# Each case runs GenerateAsync against a local listener that records the
# txt2img payload and answers with a one-pixel PNG. A case naming a
# direction first applies it as DirectedRegen does (MainViewModel.cs:364-374,
# which is WPF and is repeated here line for line). The scenario is the test
# chapter's srt-scenario literal.
#
#   pwsh build/spark-request-oracle.ps1 > codex/test/apps/spark-request.expected

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$work = Join-Path $env:TEMP 'spark-request-oracle'
if (Test-Path $work) { Remove-Item -Recurse -Force $work }
New-Item -ItemType Directory -Force $work | Out-Null
foreach ($f in 'Spark\ImageGenerator.cs', 'Spark\PromptParser.cs', 'Spark\ArtDirections.cs', 'Spark\CreativeEngine.cs', 'Common.Core\Net\HttpServiceClient.cs', 'Common.Core\Net\ServiceMarkers.cs') {
    $src = Join-Path $SparkRoot $f
    if (-not (Test-Path -PathType Leaf $src)) { Write-Host "MISSING: $src (the original Spark source)"; exit 2 }
    Copy-Item -Force $src $work
}
Set-Content (Join-Path $work 'o.csproj') '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><RootNamespace>Spark</RootNamespace></PropertyGroup></Project>'
Set-Content (Join-Path $work 'Program.cs') @'
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text.Json.Nodes;
using Common.Core.Net;
namespace Spark;
static class Program {
    static string E(string s) => s.Replace("\n", "\\n");
    static string Opt(double? d) => d.HasValue ? d.Value.ToString() : "-";
    static string Opt(int? d) => d.HasValue ? d.Value.ToString() : "-";
    static int Main(string[] a) {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var sections = new Dictionary<string, List<string>>();
        List<string>? cur = null;
        foreach (var line in File.ReadAllText(a[0]).Split('\n')) {
            if (line.StartsWith("@")) { cur = new List<string>(); sections[line[1..]] = cur; }
            else cur?.Add(line);
        }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "refine_presets.json"), string.Join("\n", sections["presets"]));
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "art_directions.json"), string.Join("\n", sections["directions"]));
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "creative_pools.json"), string.Join("\n", sections["pools"]));
        var o = new System.Text.StringBuilder();
        o.Append("N " + string.Join(",", RefinePresets.Names) + "\n");
        foreach (var (cat, d) in ArtDirections.All())
            o.Append("G " + cat + " | " + d.Label + " | " + d.PromptAdd + " | " + d.NegativeAdd + " | " + Opt(d.CfgNudge) + " | " + Opt(d.StepsNudge) + "\n");
        object pools = typeof(CreativeEngine).GetProperty("Pools", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        foreach (var name in new[] { "ColorThemes", "Compositions", "Inspirations", "Moods", "LightingSetups", "SamplerHints" })
            o.Append("O " + name + " | " + string.Join(" | ", (string[])pools.GetType().GetProperty(name)!.GetValue(pools)!) + "\n");

        int port = 17000 + Environment.ProcessId % 1000;
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();
        string? lastBody = null;
        string png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
        var serve = Task.Run(() => {
            while (listener.IsListening) {
                HttpListenerContext ctx;
                try { ctx = listener.GetContext(); } catch { return; }
                using (var r = new StreamReader(ctx.Request.InputStream)) lastBody = r.ReadToEnd();
                byte[] resp = System.Text.Encoding.UTF8.GetBytes("{\"images\":[\"" + png + "\"],\"info\":\"{\\\"seed\\\":1}\"}");
                ctx.Response.ContentType = "application/json";
                ctx.Response.OutputStream.Write(resp);
                ctx.Response.Close();
            }
        });
        var gen = new ImageGenerator(new ServiceUri<StableDiffusionApi>($"http://localhost:{port}/"));
        string root = Path.Combine(Path.GetTempPath(), "spark-request-oracle-" + Environment.ProcessId);
        foreach (var line in sections["cases"]) {
            if (line.StartsWith("C ")) {
                var f = line[2..].Split(" | ");
                var prompt = new ArtPrompt { Number = int.Parse(f[0]), Title = f[1], FullText = f[2].Replace("~", "\n") };
                var s = new ImageGeneratorSettings {
                    Width = int.Parse(f[9]), Height = int.Parse(f[10]), Steps = int.Parse(f[11]),
                    CfgScale = double.Parse(f[12], CultureInfo.InvariantCulture), Sampler = f[13], Scheduler = f[14], Seed = long.Parse(f[15]) };
                string? augment = f[6] == "-" ? null : f[6];
                if (f[8] != "-") {
                    ArtDirections.Direction? dir = null;
                    foreach ((_, ArtDirections.Direction d) in ArtDirections.All())
                        if (d.Label == f[8]) { dir = d; break; }
                    ImageGeneratorSettings settings = s;
                    if (dir!.CfgNudge.HasValue)
                        settings = settings with { CfgScale = Math.Clamp(settings.CfgScale + dir.CfgNudge.Value, 3, 15) };
                    if (dir.StepsNudge.HasValue)
                        settings = settings with { Steps = Math.Clamp(settings.Steps + dir.StepsNudge.Value, 10, 50) };
                    if (dir.NegativeAdd.Length > 0)
                        settings = settings with { NegativePrompt = settings.NegativePrompt + ", " + dir.NegativeAdd };
                    settings = settings with { Seed = -1 };
                    s = settings;
                    augment = dir.PromptAdd + ((augment ?? "").Length > 0 ? ", " + augment : "");
                }
                string outDir = Path.Combine(root, "case" + f[0]);
                var res = gen.GenerateAsync(prompt, s, outDir, runIndex: int.Parse(f[7]), refinePreset: f[3],
                    promptOverride: f[4] == "-" ? null : f[4].Replace("~", "\n"), loraTag: f[5] == "-" ? null : f[5],
                    promptAugment: augment).GetAwaiter().GetResult();
                var p = JsonNode.Parse(lastBody!)!;
                o.Append("F " + Path.GetRelativePath(outDir, res.FilePath!).Replace('\\', '/') + "\n");
                o.Append("P " + E(p["prompt"]!.GetValue<string>()) + "\n");
                o.Append("M " + E(p["negative_prompt"]!.GetValue<string>()) + "\n");
                o.Append("S " + p["width"]!.ToJsonString() + " " + p["height"]!.ToJsonString() + " " + p["steps"]!.ToJsonString() + " " + p["cfg_scale"]!.ToJsonString() + " " + p["sampler_name"]!.GetValue<string>() + " " + p["scheduler"]!.GetValue<string>() + " " + p["seed"]!.ToJsonString() + "\n");
            } else if (line.StartsWith("L ")) {
                var f = line[2..].Split(" | ");
                var ap = new ArtPrompt { Lora = f[0], LoraWeight = double.Parse(f[1], CultureInfo.InvariantCulture) };
                o.Append("L " + ap.LoraTag + "\n");
            }
        }
        listener.Stop();
        if (Directory.Exists(root)) Directory.Delete(root, true);
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        Console.Out.Write(o.ToString());
        return 0;
    }
}
'@
& dotnet build $work -c Release -v q -o (Join-Path $work 'bin') | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host 'oracle build failed'; & dotnet build $work -c Release -v q -o (Join-Path $work 'bin') | Select-String 'error' | Select-Object -First 5; exit 2 }
$lf = Join-Path $work 'scenario.txt'
$m = [regex]::Match([IO.File]::ReadAllText($TestFile), '(?m)^  srt-scenario = "((?:[^"\\]|\\.)*)"')
if (-not $m.Success) { Write-Host "no srt-scenario literal in $TestFile"; exit 2 }
$s = [regex]::Replace($m.Groups[1].Value, '\\(.)', { param($x) if ($x.Groups[1].Value -eq 'n') { "`n" } else { $x.Groups[1].Value } })
[IO.File]::WriteAllText($lf, $s, [Text.UTF8Encoding]::new($false))
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
& (Join-Path $work 'bin\o.exe') $lf
