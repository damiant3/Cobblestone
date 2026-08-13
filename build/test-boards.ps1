# test-boards.ps1 -- Cross-architecture board tests via Renode
# GENERATED FROM THE CODEX SHELL DSL. Do not edit by hand.
# A hand edit here must NOT be submitted. Change the generator under
# codex/build/, regenerate, and submit the generator and this file
# together. Until then build/check-generated-scripts.ps1 reports this
# file as drifted, and the next regeneration discards the edit.
[CmdletBinding()]
param(
    [ValidateSet('all','arm64','riscv64')]
    [string]$Arch = 'all'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'renode-config.ps1')
$RenodeExe = Get-RenodeExe -Repo $Repo
$OutDir = Join-Path $PSScriptRoot 'output\boards'
New-Item -ItemType Directory -Force $OutDir | Out-Null

$SeedCdx = Join-Path $Repo 'seed\Codex.cdx'
$Stage0 = Join-Path $Repo 'build-output\bare-metal\Codex.cdx'
New-Item -ItemType Directory -Force (Split-Path $Stage0) | Out-Null
if ((-not (Test-Path -PathType Leaf $Stage0))) {
    Copy-Item -Force $SeedCdx $Stage0
}

if ((-not $RenodeExe)) {
    Write-RenodeSkip
    exit 0
}


$HelloSrc = Join-Path $OutDir 'hello-board.codex'
@"
Chapter: Hello
  cites Foreword chapter Console
Section: Main
  opening : [Console] Nothing = act
    print-line "BOARD-TEST-OK"
  end
"@ | Set-Content $HelloSrc -Encoding UTF8


$boards = @(
    @{ Name = 'arm64';   Plug = 'arm64';  Compile = 'compile-arm64.ps1'; Board = 'codex-arm64.repl';   Expected = 'BOARD-TEST-OK' },
    @{ Name = 'riscv64'; Plug = 'riscv';  Compile = 'compile-riscv.ps1'; Board = 'codex-riscv64.repl'; Expected = 'BOARD-TEST-OK' }
)

if (($Arch -ne 'all')) {
    $boards = $boards | Where-Object { $_.Name -eq $Arch }
}

$pass = 0; $fail = 0; $skip = 0


foreach ($b in $boards) {
    $plugCdx = Join-Path $Repo "codex\plugs\$($b.Plug)\build-output\$($b.Plug)-plug.cdx"
    if ((-not (Test-Path -PathType Leaf $plugCdx))) {
        Write-Host "  SKIP  $($b.Name): plug not built ($plugCdx)" -ForegroundColor Yellow
        $skip++; continue
    }

    $boardRepl = Join-Path $Repo "tools\renode\codex\$($b.Board)"
    if ((-not (Test-Path -PathType Leaf $boardRepl))) {
        Write-Host "  SKIP  $($b.Name): board definition missing ($boardRepl)" -ForegroundColor Yellow
        $skip++; continue
    }

    $elfOut = Join-Path $OutDir "hello-$($b.Name).elf"
    $compileScript = Join-Path $Repo "codex\plugs\$($b.Plug)\$($b.Compile)"

    Write-Host "  $($b.Name): compiling..." -NoNewline
    $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    & pwsh -NoProfile -File $compileScript -Src $HelloSrc -Out $elfOut 2>&1 | Out-Null
    $ErrorActionPreference = $prev
    if (((-not ($LASTEXITCODE -eq 0)) -or (-not (Test-Path -PathType Leaf $elfOut)))) {
        Write-Host " FAIL (compile)" -ForegroundColor Red
        $fail++; continue
    }


    $uartLog = Join-Path $OutDir "uart-$($b.Name).log"
    if ((Test-Path -PathType Leaf $uartLog)) {
        Remove-Item -Force -ErrorAction SilentlyContinue $uartLog
    }

    $elfPath = (Resolve-Path $elfOut).Path -replace '\\','/'
    $boardPath = (Resolve-Path $boardRepl).Path -replace '\\','/'
    $uartPath = $uartLog -replace '\\','/'

    $rescLines = @(
        'mach create "codex"'
        "machine LoadPlatformDescription @$boardPath"
        "sysbus LoadELF @$elfPath"
        "uart0 CreateFileBackend @$uartPath true"
        'start'
        'sleep 3'
        'quit'
    )
    $rescFile = Join-Path $OutDir "test-$($b.Name).resc"
    [System.IO.File]::WriteAllLines($rescFile, $rescLines)
    $rescPath = $rescFile -replace '\\','/'

    $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    & $RenodeExe --disable-xwt --console -e "include @$rescPath" 2>&1 | Out-Null
    $ErrorActionPreference = $prev
    Start-Sleep -Milliseconds 500


    if ((Test-Path -PathType Leaf $uartLog)) {
        $uart = Get-Content $uartLog -Raw -ErrorAction SilentlyContinue
        if (($uart -and $uart -match [regex]::Escape($b.Expected))) {
            Write-Host " PASS" -ForegroundColor Green
            $pass++
        } else {
            $preview = if ($uart) { $uart.Substring(0, [Math]::Min(60, $uart.Length)).Trim() } else { "(empty)" }
            Write-Host " FAIL (uart: $preview)" -ForegroundColor Red
            $fail++
        }
    } else {
        Write-Host " FAIL (no uart log)" -ForegroundColor Red
        $fail++
    }

}


Write-Host ''
Write-Host "Board tests: $pass pass, $fail fail, $skip skip" -ForegroundColor $(if ($fail -gt 0) { 'Red' } else { 'Green' })
exit $fail
