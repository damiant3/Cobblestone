# Compile all Codex benchmark files to CDX, producing .cdx and .map files.
# Then run each in codex-vm to verify correctness.
#
# Output: bench/build-output/codex/<name>/<name>.cdx, .map, result.txt
#         bench/build-output/codex/kernel.txt (the compiler used, and its digest)
#
# -Kernel defaults to seed/Codex.cdx, the shipping seed. It is passed to
# compile.ps1 explicitly because compile.ps1's own default,
# build-output/bare-metal/Codex.cdx, holds whichever kernel ran LAST
# (OperatorsManual, "Pass -Kernel when you do") and a benchmark number
# taken against it is a number against an unknown compiler.
[CmdletBinding()]
param(
    [string]$Kernel = ''
)

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

if (-not $Kernel) { $Kernel = Join-Path $Repo 'seed' 'Codex.cdx' }
if (-not (Test-Path $Kernel)) { Write-Error "kernel not found at $Kernel"; exit 1 }
$Kernel = (Resolve-Path $Kernel).Path
$kernelHash = (Get-FileHash -Algorithm SHA256 $Kernel).Hash.Substring(0, 16)
Write-Host "  kernel: $Kernel [$kernelHash]"
New-Item -ItemType Directory -Force -Path $OutRoot | Out-Null
"$Kernel [$kernelHash]" | Out-File -FilePath (Join-Path $OutRoot 'kernel.txt') -Encoding UTF8

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
    & pwsh -NoProfile -File $Compile -Src $src.FullName -Out $cdx -Log $log -Kernel $Kernel
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
