# Compile all Zig benchmark files with zig at -O Debug and -O ReleaseFast,
# producing assembly listings (-femit-asm) and executables for each.
#
# Output: bench/build-output/<OutName>/<name>/{Debug,ReleaseFast}/{name}.exe, {name}.s
# -SrcDir/-OutName let the same build run over the zig the ZIG PLUG emitted
# from bench/codex (bench/transpile-zig.ps1 writes it): the two columns are
# then built and counted by one script, so a difference between them is the
# source, not the method.
[CmdletBinding()]
param(
    [string]$Zig = 'D:\zig-0.16.0\zig.exe',
    [string]$SrcDir = '',
    [string]$OutName = 'zig'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$BenchDir = $PSScriptRoot
if (-not $SrcDir) { $SrcDir = Join-Path $BenchDir 'zig' }
$OutRoot  = Join-Path $BenchDir 'build-output' $OutName

if (-not (Test-Path $Zig)) {
    Write-Error "zig.exe not found at $Zig"
    exit 1
}
$zigVersion = (& $Zig version 2>&1 | Out-String).Trim()
Write-Host "zig $zigVersion"

$sources = Get-ChildItem -Path $SrcDir -Filter '*.zig'
if ($sources.Count -eq 0) { Write-Host 'No .zig files found'; exit 0 }

$cacheDir = Join-Path $OutRoot '.zig-cache'
New-Item -ItemType Directory -Force -Path $cacheDir | Out-Null

foreach ($src in $sources) {
    $name = $src.BaseName
    foreach ($opt in @('Debug', 'ReleaseFast')) {
        $outDir = Join-Path $OutRoot $name $opt
        New-Item -ItemType Directory -Force -Path $outDir | Out-Null
        $exe = Join-Path $outDir "$name.exe"

        Write-Host "  [$opt] $name"
        Push-Location $outDir
        try {
            # -femit-asm writes <name>.s beside the exe in the working directory.
            $result = & $Zig build-exe $src.FullName -O $opt -femit-asm --cache-dir $cacheDir 2>&1
            $exitCode = $LASTEXITCODE
        } finally {
            Pop-Location
        }
        $result | Out-File -FilePath (Join-Path $outDir 'build.log') -Encoding UTF8
        if ($exitCode -ne 0) {
            Write-Host "    FAIL (exit $exitCode)"
            $result | ForEach-Object { Write-Host "    $_" }
        } else {
            $output = (& $exe 2>&1 | Out-String).Trim()
            Write-Host "    output: $output"
            $output | Out-File -FilePath (Join-Path $outDir 'result.txt') -Encoding UTF8
        }
        Remove-Item -Force (Join-Path $outDir '*.pdb') -ErrorAction SilentlyContinue
        Remove-Item -Force (Join-Path $outDir '*.obj') -ErrorAction SilentlyContinue
    }
}

$zigVersion | Out-File -FilePath (Join-Path $OutRoot 'zig-version.txt') -Encoding UTF8
Write-Host "`nDone. Assembly listings in $OutRoot"
