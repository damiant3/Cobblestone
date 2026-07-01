# Compile all Codex benchmark files to CDX, producing .cdx and .map files.
# Then run each in codex-vm to verify correctness.
#
# Output: bench/build-output/codex/<name>/<name>.cdx, .map, result.txt
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$BenchDir  = $PSScriptRoot
$SrcDir    = Join-Path $BenchDir 'codex'
$OutRoot   = Join-Path $BenchDir 'build-output' 'codex'
$Repo      = Split-Path $BenchDir
$Compile   = Join-Path $Repo 'build' 'compile.ps1'
$VmBin     = Join-Path $Repo 'tools' 'codex-vm.exe'

if (-not (Test-Path $Compile)) { Write-Error "compile.ps1 not found at $Compile"; exit 1 }
if (-not (Test-Path $VmBin))   { Write-Error "codex-vm.exe not found at $VmBin"; exit 1 }

$Stage0 = Join-Path $Repo 'build-output' 'bare-metal' 'Codex.cdx'
if (-not (Test-Path $Stage0)) {
    $seed = Join-Path $Repo 'seed' 'Codex.cdx'
    if (-not (Test-Path $seed)) { Write-Error "No Codex.cdx found in build-output or seed"; exit 1 }
    New-Item -ItemType Directory -Force -Path (Split-Path $Stage0) | Out-Null
    Copy-Item $seed $Stage0
    Write-Host "  Copied seed/Codex.cdx -> build-output/bare-metal/Codex.cdx"
}

$sources = Get-ChildItem -Path $SrcDir -Filter '*.codex'
if ($sources.Count -eq 0) { Write-Host 'No .codex files found'; exit 0 }

$failed = 0
foreach ($src in $sources) {
    $name = $src.BaseName
    $outDir = Join-Path $OutRoot $name
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null

    $cdx = Join-Path $outDir "$name.cdx"
    $log = Join-Path $outDir 'build.log'

    Write-Host "  [compile] $name"
    & pwsh -NoProfile -File $Compile -Src $src.FullName -Out $cdx -Log $log
    if ($LASTEXITCODE -ne 0) {
        Write-Host "    FAIL (exit $LASTEXITCODE)"
        if (Test-Path $log) { Get-Content $log | ForEach-Object { Write-Host "    $_" } }
        $failed++
        continue
    }

    $mapFile = Join-Path $outDir "$name.map"
    if (Test-Path $mapFile) {
        $funcCount = (Get-Content $mapFile | Where-Object { $_ -match '^0x' }).Count
        Write-Host "    map: $funcCount functions"
    }

    # Run for correctness
    $outputFile = [System.IO.Path]::GetTempFileName()
    $vmArgs = @('-kernel', $cdx, '-output', $outputFile, '-mem', '2048', '-headless')
    $proc = Start-Process -FilePath $VmBin -ArgumentList $vmArgs -PassThru -WindowStyle Hidden
    $proc.WaitForExit(60000)
    if (-not $proc.HasExited) {
        try { Stop-Process -Id $proc.Id -Force } catch {}
        Write-Host "    RUN TIMEOUT"
        $failed++
        continue
    }
    if (Test-Path $outputFile) {
        $rawBytes = [System.IO.File]::ReadAllBytes($outputFile)
        $rawText = [System.Text.Encoding]::UTF8.GetString($rawBytes)
        $lines = $rawText -split "`n" | Where-Object { $_ -and -not $_.StartsWith('WD:') -and -not $_.StartsWith('HEAP:') -and -not $_.StartsWith('STACK:') }
        $result = ($lines -join "`n").Trim()
        Write-Host "    output: $result"
        $result | Out-File -FilePath (Join-Path $outDir 'result.txt') -Encoding UTF8
    }
    Remove-Item -Force $outputFile -ErrorAction SilentlyContinue
}

if ($failed -gt 0) { Write-Host "`n$failed benchmark(s) failed"; exit 1 }
Write-Host "`nDone. CDX + maps in $OutRoot"
