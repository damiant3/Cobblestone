[CmdletBinding()]
param(
    [string]$TestFile = (Join-Path $PSScriptRoot '..\codex\test\apps\spark-audio.codex'),
    [string]$SparkDir = 'D:\Projects\Spark\Spark'
)

# The oracle for codex/test/apps/spark-audio: the WPF Spark's own
# MusicConfig.cs, SfxConfig.cs, MusicTrack.cs and SfxTrack.cs, unmodified,
# reading the test chapter's literals, and the presenters' static rules
# (ExtractTitle, SanitizeFileName, DetectCategory) copied out of
# MusicPresenter.cs and SfxPresenter.cs by name. InjectTag is an instance
# method on the view model's prompt; its body is copied with m_vm.Prompt read
# as a local, the one edit this oracle makes.
#
#   pwsh build/spark-audio-oracle.ps1 > codex/test/apps/spark-audio.expected

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$work = Join-Path $env:TEMP 'spark-audio-oracle'
Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $work | Out-Null
foreach ($f in 'MusicConfig.cs', 'SfxConfig.cs', 'MusicTrack.cs', 'SfxTrack.cs') {
    $src = Join-Path $SparkDir $f
    if (-not (Test-Path $src)) { Write-Host "MISSING: $src (the original Spark source)"; exit 2 }
    Copy-Item -Force $src $work
}
$method = {
    param($file, $sig)
    $t = [IO.File]::ReadAllText((Join-Path $SparkDir "Presenters\$file"))
    $i = $t.IndexOf($sig)
    if ($i -lt 0) { throw "no $sig in $file" }
    $open = $t.IndexOf("`n", $i)
    $depth = 0; $j = $i; $seenBrace = $false
    while ($j -lt $t.Length) {
        $c = $t[$j]
        if ($c -eq '{') { $depth++; $seenBrace = $true }
        elseif ($c -eq '}') { $depth--; if ($seenBrace -and $depth -eq 0) { break } }
        elseif ($c -eq ';' -and -not $seenBrace -and $t.Substring($i, $j - $i).Contains('=>')) { break }
        $j++
    }
    $t.Substring($i, $j - $i + 1)
}
$musicTitle = (& $method 'MusicPresenter.cs' 'static string ExtractTitle(').Replace('ExtractTitle(', 'MusicTitle(')
$sfxTitle = (& $method 'SfxPresenter.cs' 'static string ExtractTitle(').Replace('ExtractTitle(', 'SfxTitle(')
$sanitize = & $method 'MusicPresenter.cs' 'static string SanitizeFileName('
$category = & $method 'SfxPresenter.cs' 'static string DetectCategory('
$inject = (& $method 'MusicPresenter.cs' 'void InjectTag(').Replace('void InjectTag(string? tag)', 'static string InjectTag(string prompt, string? tag)').Replace('m_vm.Prompt', 'prompt')
$inject = $inject.Replace('if (string.IsNullOrWhiteSpace(tag)) return;', 'if (string.IsNullOrWhiteSpace(tag)) return prompt;')
$inject = $inject.Substring(0, $inject.LastIndexOf('}')) + "    return prompt;`n    }"

$text = [IO.File]::ReadAllText($TestFile)
$lit = {
    param($name)
    $m = [regex]::Match($text, '(?m)^  ' + [regex]::Escape($name) + ' = "((?:[^"\\]|\\.)*)"')
    if (-not $m.Success) { throw "no $name literal" }
    [regex]::Replace($m.Groups[1].Value, '\\(.)', { param($x) if ($x.Groups[1].Value -eq 'n') { "`n" } else { $x.Groups[1].Value } })
}
$list = {
    param($name)
    $m = [regex]::Match($text, '(?m)^  ' + [regex]::Escape($name) + ' = \[(.*)\]\s*$')
    if (-not $m.Success) { throw "no $name list" }
    @([regex]::Matches($m.Groups[1].Value, '"((?:[^"\\]|\\.)*)"') | ForEach-Object { [regex]::Replace($_.Groups[1].Value, '\\(.)', { param($x) if ($x.Groups[1].Value -eq 'n') { "`n" } else { $x.Groups[1].Value } }) })
}
$data = Join-Path $work 'data'
New-Item -ItemType Directory -Force $data | Out-Null
$u8 = [Text.UTF8Encoding]::new($false)
foreach ($n in 'sat-music-catalog', 'sat-sfx-catalog', 'sat-made-catalog') { [IO.File]::WriteAllText((Join-Path $data "$n.json"), (& $lit $n), $u8) }
[IO.File]::WriteAllText((Join-Path $data 'prompts.txt'), ((& $list 'sat-prompts') -join "`u{1}"), $u8)
[IO.File]::WriteAllText((Join-Path $data 'inject.txt'), ((& $list 'sat-inject') -join "`u{1}"), $u8)
$music = & $lit 'sat-music-config'
$sfx = & $lit 'sat-sfx-config'

Set-Content (Join-Path $work 'o.csproj') '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0-windows</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><RootNamespace>Spark</RootNamespace></PropertyGroup></Project>'
Set-Content (Join-Path $work 'Program.cs') (@'
using System.Globalization;
using System.Text;
using System.Text.Json;
namespace Spark;
static class Rules {
MUSICTITLE
SFXTITLE
SANITIZE
CATEGORY
INJECT
    public static string MT(string p) => MusicTitle(p);
    public static string ST(string p) => SfxTitle(p);
    public static string SN(string p) => SanitizeFileName(p);
    public static string DC(string p) => DetectCategory(p);
    public static string IT(string p, string t) => InjectTag(p, t);
}
static class Program {
    static string B(bool b) => b ? "true" : "false";
    static string Utc(DateTime d) => d.ToString("yyyy-MM-ddTHH:mm:ss.FFFFFFFK", CultureInfo.InvariantCulture);
    static int Main(string[] a) {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string dir = a[0];
        var o = new StringBuilder();
        foreach (string name in new[] { "music", "sfx", "made" }) {
            string json = File.ReadAllText(Path.Combine(dir, $"sat-{name}-catalog.json"));
            var music = JsonSerializer.Deserialize<List<MusicTrack>>(json)!.Where(t => !t.Deleted).ToList();
            var sfx = JsonSerializer.Deserialize<List<SfxTrack>>(json)!.Where(t => !t.Deleted).ToList();
            int next = 1; foreach (var t in music) if (t.Id >= next) next = t.Id + 1;
            o.Append($"C {name} {music.Count} next {next}\n");
            for (int i = 0; i < music.Count; i++) {
                var m = music[i]; var s = sfx[i];
                o.Append($"R {m.Id}|{m.Title}|{m.Prompt}|{s.Category}|{m.FilePath}|{m.Duration}|{m.Temperature}|{m.CfgCoefficient}|{Utc(m.CreatedUtc)}|{m.VibeTag}|{m.Bpm}|{s.ParentId}|{m.Rating}|{B(m.Saved)}|{B(m.Deleted)}\n");
            }
        }
        o.Append("F " + string.Join("|", MusicConfig.InstrumentFamilies) + "\n");
        foreach (string f in MusicConfig.InstrumentFamilies) o.Append($"I {f} " + string.Join("|", MusicConfig.InstrumentsIn(f)) + "\n");
        o.Append("composers " + string.Join("|", MusicConfig.Composers) + "\n");
        o.Append("artists " + string.Join("|", MusicConfig.Artists) + "\n");
        o.Append("genres " + string.Join("|", MusicConfig.Genres) + "\n");
        o.Append("keys " + string.Join("|", MusicConfig.Keys) + "\n");
        o.Append("scales " + string.Join("|", MusicConfig.Scales) + "\n");
        o.Append("moods " + string.Join("|", MusicConfig.MoodPresets) + "\n");
        foreach (var t in MusicConfig.TempoMarkings) o.Append($"T {t.Label}|{t.BpmHint}\n");
        o.Append($"D {SfxConfig.MinDuration} {SfxConfig.MaxDuration}\n");
        o.Append("K " + string.Join("|", SfxConfig.Categories) + "\n");
        foreach (string c in SfxConfig.Categories) { var d = SfxConfig.DefaultsFor(c); o.Append(d is null ? $"CD {c} none\n" : $"CD {c} {d.Duration} {d.Temperature} {d.Cfg}\n"); }
        foreach (var p in SfxConfig.Presets) o.Append($"P {p.Label}|{p.Category}|{p.Prompt}\n");
        foreach (string p in File.ReadAllText(Path.Combine(dir, "prompts.txt")).Split('\u0001')) {
            o.Append($"M {Rules.MT(p)}|track_{7:D3}_{Rules.SN(Rules.MT(p))}.wav\n");
            o.Append($"S {Rules.ST(p)}|sfx_{12:D3}_{Rules.SN(Rules.ST(p))}.wav|{Rules.DC(p)}\n");
        }
        string[] inj = File.ReadAllText(Path.Combine(dir, "inject.txt")).Split('\u0001');
        for (int i = 0; i + 1 < inj.Length; i += 2) o.Append($"J [{Rules.IT(inj[i], inj[i + 1])}]\n");
        Console.OutputEncoding = new UTF8Encoding(false);
        Console.Out.Write(o.ToString());
        return 0;
    }
}
'@).Replace('MUSICTITLE', $musicTitle).Replace('SFXTITLE', $sfxTitle).Replace('SANITIZE', $sanitize).Replace('CATEGORY', $category).Replace('INJECT', $inject)
& dotnet build $work -c Release -v q -o (Join-Path $work 'bin') | Out-Null
if ($LASTEXITCODE -ne 0) { & dotnet build $work -c Release -v q -o (Join-Path $work 'bin'); Write-Host 'oracle build failed'; exit 2 }
[IO.File]::WriteAllText((Join-Path $work 'bin\music_config.json'), $music, $u8)
[IO.File]::WriteAllText((Join-Path $work 'bin\sfx_config.json'), $sfx, $u8)
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
# CCE has no Dingbats block (codex/foreword/core/core-backlog.md, "CCE has no
# Dingbats block"): U+2728, the only character of it in these files, reaches
# Codex text as '?'. Any other character the port loses stays a failure.
$out = & (Join-Path $work 'bin\o.exe') $data | Out-String
[Console]::Out.Write($out.Replace([string][char]0x2728, '?'))
