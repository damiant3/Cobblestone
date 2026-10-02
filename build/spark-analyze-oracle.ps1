[CmdletBinding()]
param(
    [string]$Project = 'D:\Projects\Spark\Spark',
    [string]$SparkDir = 'D:\Projects\Spark\Spark',
    [Parameter(Mandatory = $true)][string]$Out
)

# The oracle for apps/spark/audio-analyze.mjs: the WPF Spark's own
# MusicAnalyzer.cs, unmodified, with NAudio from NuGet, run over every WAV in
# the project's Music and SoundFX folders. Writes one JSON object per file
# name: its sample rate, duration, peak and RMS in dB, estimated BPM, spectral
# centroid, vibe tag, and the waveform, envelope and spectrogram arrays.
#
#   pwsh build/spark-analyze-oracle.ps1 -Out analysis.json

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$work = Join-Path $env:TEMP 'spark-analyze-oracle'
New-Item -ItemType Directory -Force $work | Out-Null
$src = Join-Path $SparkDir 'Services\MusicAnalyzer.cs'
if (-not (Test-Path $src)) { Write-Host "MISSING: $src (the original Spark source)"; exit 2 }
Copy-Item -Force $src $work
Set-Content (Join-Path $work 'o.csproj') '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0-windows</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><RootNamespace>Spark</RootNamespace></PropertyGroup><ItemGroup><PackageReference Include="NAudio" Version="2.2.1" /></ItemGroup></Project>'
Set-Content (Join-Path $work 'Program.cs') @'
using System.Globalization;
using System.Text.Json;
using Spark.Services;
namespace Spark;
static class Program {
    static int Main(string[] a) {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var all = new SortedDictionary<string, object>(StringComparer.Ordinal);
        foreach (string dir in new[] { "Music", "SoundFX" }) {
            string d = Path.Combine(a[0], dir);
            if (!Directory.Exists(d)) continue;
            foreach (string f in Directory.GetFiles(d, "*.wav").OrderBy(x => x, StringComparer.Ordinal)) {
                MusicAnalysis m = MusicAnalyzer.Analyze(f);
                all[Path.GetFileName(f)] = new Dictionary<string, object> {
                    ["sampleRate"] = m.SampleRate, ["duration"] = m.DurationSeconds, ["peakDb"] = m.PeakDb, ["rmsDb"] = m.RmsDb,
                    ["bpm"] = m.EstimatedBpm, ["centroid"] = m.SpectralCentroid, ["vibe"] = m.VibeDescription(),
                    ["waveform"] = m.Waveform, ["envelope"] = m.EnvelopeDb, ["spectrogram"] = m.SpectrogramBands };
            }
        }
        File.WriteAllText(a[1], JsonSerializer.Serialize(all));
        return 0;
    }
}
'@
& dotnet build $work -c Release -v q -o (Join-Path $work 'bin') | Out-Null
if ($LASTEXITCODE -ne 0) { & dotnet build $work -c Release -v q -o (Join-Path $work 'bin'); Write-Host 'oracle build failed'; exit 2 }
& (Join-Path $work 'bin\o.exe') $Project $Out
