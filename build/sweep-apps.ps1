# sweep-apps.ps1 -- Compile every .codex under apps/ to verify no compiler crashes
# GENERATED FROM THE CODEX SHELL DSL. Do not edit by hand.
# A hand edit here must NOT be submitted. Change the generator under
# codex/build/, regenerate, and submit the generator and this file
# together. Until then build/check-generated-scripts.ps1 reports this
# file as drifted, and the next regeneration discards the edit.
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
[Environment]::CurrentDirectory = (Get-Location).Path


$CompileScript = (Join-Path $PSScriptRoot 'compile.ps1')
$stage0 = (Join-Path $Repo 'build-output\bare-metal\Codex.cdx')
$seedCdx = (Join-Path $Repo 'seed\Codex.cdx')
New-Item -ItemType Directory -Force (Split-Path $stage0) | Out-Null
Copy-Item -Force $seedCdx $stage0


$outDir = (Join-Path $Repo ([string]'build-output\sweep-' + $Dir))
if ((Test-Path -PathType Container $outDir)) {
    Remove-Item -Recurse -Force $outDir
}
New-Item -ItemType Directory -Force $outDir | Out-Null


$files = @((Get-ChildItem (Join-Path $Repo ([string]'apps\' + $Dir)) -Recurse -Filter '*.codex' -File | Sort-Object FullName))
Write-Host ([string]([string]([string]'Sweep: ' + @($files).Count) + ' files under apps/') + $Dir)


$pass = 0
$diag = 0
$crash = 0
$timeout = 0
$crashes = @()


foreach ($f in $files) {
    $name = $f.BaseName
    $rel = $f.FullName.Substring(($Repo.Length + 1))
    $cdxOut = (Join-Path $outDir ([string]$name + '.cdx'))
    $logOut = (Join-Path $outDir ([string]$name + '.log'))

    $proc = Start-Process -FilePath 'pwsh' -ArgumentList @('-NoProfile', '-File', $CompileScript, '-Src', $f.FullName, '-Out', $cdxOut, '-Log', $logOut) -WindowStyle Hidden -PassThru -RedirectStandardError (Join-Path $outDir ([string]$name + '.stderr'))
    $exited = $proc.WaitForExit(($TimeoutSec * 1000))
    if ((-not $exited)) {
        try { Stop-Process -Id $proc.Id -Force -ErrorAction Stop } catch {}
        Write-Host ([string]'  TIMEOUT  ' + $rel) -ForegroundColor Red
        $timeout++
        $crashes += ([string]([string]'TIMEOUT' + "`t") + $rel)
        continue
    }

    if (($proc.ExitCode -eq 0)) {
        $pass++
    } else {
        if (($proc.ExitCode -eq 4)) {
            $diag++
        } else {
            Write-Host ([string]([string]([string]([string]'CRASH(' + $proc.ExitCode) + ')') + '  ') + $rel) -ForegroundColor Red
            $crash++
            $crashes += ([string]([string]([string]([string]'CRASH(' + $proc.ExitCode) + ')') + "`t") + $rel)
        }
    }

}


Write-Host ''
Write-Host ([string]([string]([string]([string]([string]([string]([string]([string]([string]([string]([string]'Results: ' + @($files).Count) + ' files, ') + $pass) + ' compiled, ') + $diag) + ' diagnostic-halt, ') + $crash) + ' crash') + ', ') + $timeout) + ' timeout')
if ((@($crashes).Count -gt 0)) {
    Write-Host 'Crashes/timeouts:'
    foreach ($c in $crashes) {
        Write-Host ([string]'  ' + $c)
    }
}
exit ($crash + $timeout)
