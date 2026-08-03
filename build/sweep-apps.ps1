# sweep-apps.ps1 -- compile every .codex under apps/ to verify no crashes.
# Compile failures (missing entry point, unresolved names) are expected
# for library modules. The sweep checks for compiler crashes (timeout,
# GPF, non-zero exit without clean diagnostic).
[CmdletBinding()]
param(
    [string]$Dir = 'games',
    [int]$Jobs = 8,
    [int]$TimeoutSec = 60
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $Repo
[Environment]::CurrentDirectory = $Repo

$CompileScript = Join-Path $PSScriptRoot 'compile.ps1'
$stage0 = Join-Path $Repo 'build-output\bare-metal\Codex.cdx'
$seedCdx = Join-Path $Repo 'seed\Codex.cdx'
New-Item -ItemType Directory -Force -Path (Split-Path $stage0) | Out-Null
Copy-Item -Force $seedCdx $stage0

$outDir = Join-Path $Repo "build-output\sweep-$Dir"
if (Test-Path $outDir) { Remove-Item -Recurse -Force $outDir }
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$files = @(Get-ChildItem -Path (Join-Path $Repo "apps\$Dir") -Recurse -Include '*.codex' -File | Sort-Object FullName)
Write-Host "Sweep: $($files.Count) files under apps/$Dir"

$pass = 0; $diag = 0; $crash = 0; $timeout = 0
$crashes = @()

foreach ($f in $files) {
    $name = $f.BaseName
    $rel = $f.FullName.Substring($Repo.Length + 1)
    $cdxOut = Join-Path $outDir "$name.cdx"
    $logOut = Join-Path $outDir "$name.log"

    $proc = Start-Process -FilePath 'pwsh' -ArgumentList @(
        '-NoProfile', '-File', $CompileScript,
        '-Src', $f.FullName,
        '-Out', $cdxOut,
        '-Log', $logOut
    ) -PassThru -WindowStyle Hidden -RedirectStandardError (Join-Path $outDir "$name.stderr")

    $exited = $proc.WaitForExit($TimeoutSec * 1000)
    if (-not $exited) {
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
        Write-Host "  TIMEOUT  $rel" -ForegroundColor Red
        $timeout++
        $crashes += "TIMEOUT`t$rel"
        continue
    }

    if ($proc.ExitCode -eq 0) {
        $pass++
    } elseif ($proc.ExitCode -eq 4) {
        # Exit 4 = compile halted with diagnostics (clean failure)
        $diag++
    } else {
        Write-Host "  CRASH($($proc.ExitCode))  $rel" -ForegroundColor Red
        $crash++
        $crashes += "CRASH($($proc.ExitCode))`t$rel"
    }
}

Write-Host ""
Write-Host "Results: $($files.Count) files, $pass compiled, $diag diagnostic-halt, $crash crash, $timeout timeout"
if ($crashes.Count -gt 0) {
    Write-Host "Crashes/timeouts:"
    foreach ($c in $crashes) { Write-Host "  $c" }
}
exit $crash + $timeout
