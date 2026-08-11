# test-cross.ps1 -- Cross-architecture test harness -- compile to ARM64/RISC-V, boot on Renode, compare UART output
# Generated from Codex Shell DSL. Do not edit by hand.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [ValidateSet('arm64','riscv64')]
    [string]$Arch,
    [Parameter(Mandatory=$true)]
    [string]$Test,
    [int]$TimeoutSec = 10
)

# Usage:
#   build/test-cross.ps1 -Arch arm64 -Test arithmetic
#   build/test-cross.ps1 -Arch riscv64 -Test factorial
#   build/test-cross.ps1 -Arch arm64 -Test cce-tier1 -TimeoutSec 60
# 
# Exit status: 0 on pass, 1 on failure.


Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
[Environment]::CurrentDirectory = (Get-Location).Path

$Repo = (Get-Location).Path
. (Join-Path $PSScriptRoot 'renode-config.ps1')
$RenodeExe = Get-RenodeExe -Repo $Repo
if ((-not $RenodeExe)) {
    Write-RenodeSkip
    exit 0
}


$SeedCdx = Join-Path $Repo 'seed\Codex.cdx'
$Stage0 = Join-Path $Repo 'build-output\bare-metal\Codex.cdx'
New-Item -ItemType Directory -Force (Split-Path $Stage0) | Out-Null
if ((-not (Test-Path -PathType Leaf $Stage0))) {
    Copy-Item -Force $SeedCdx $Stage0
}

$plugName = if ($Arch -eq 'arm64') { 'arm64' } else { 'riscv' }
$plugCdx = Join-Path $Repo "codex\plugs\$plugName\build-output\$plugName-plug.cdx"
if ((-not (Test-Path -PathType Leaf $plugCdx))) {
    Write-Host "SKIP: plug not built ($plugCdx)" -ForegroundColor Yellow
    Write-Host "  Build with: codex\plugs\$plugName\build.ps1"
    exit 0
}

$boardRepl = Join-Path $Repo "tools\renode\codex\codex-${Arch}.repl"
if ((-not (Test-Path -PathType Leaf $boardRepl))) {
    Write-Host "SKIP: board definition missing ($boardRepl)" -ForegroundColor Yellow
    exit 0
}
$compileScript = Join-Path $Repo "codex\plugs\$plugName\compile-$plugName.ps1"


# -- Locate test --
$testFile = Get-Item -Path "codex\test\$Test.codex" -ErrorAction SilentlyContinue
if ((-not $testFile)) {
    $testFile = Get-Item -Path "codex\test\ops\$Test.codex" -ErrorAction SilentlyContinue
}
if ((-not $testFile)) {
    Write-Host "ERROR: test not found: codex\test\$Test.codex (or codex\test\ops\)" -ForegroundColor Red
    exit 1
}
$name = $testFile.BaseName
$dir = $testFile.DirectoryName


# -- Skip logic --
$skipFile = Join-Path $dir "$name.skip"
$slowFile = Join-Path $dir "$name.slow"
$fatalFile = Join-Path $dir "$name.fatal"
$failingFile = Join-Path $dir "$name.failing"
$smpFile = Join-Path $dir "$name.smp"
# .no-cross: the test is real and runs on x86, but the generic cross-arch
# battery is the wrong place for it -- x86 port/MMIO hardware, a block device
# this lane does not attach, or a board with its own harness. The sidecar's
# first line MUST say which, because a skip list whose entries do not carry
# their reason is where tests go to be quietly abandoned. It is never the
# answer for a test that fails because the TARGET cannot do something: that
# is a gap, and a gap is written down where it stays visible.
$noCrossFile = Join-Path $dir "$name.no-cross"
# .cross-refusal: the test's DESIGNED behavior on a cross lane is a compile
# refusal. Each non-comment line names a builtin whose "[UNSUPPORTED] <name>"
# report must appear in the compile log, and the compile must fail. This is
# what keeps refusal-by-design a tested behavior: if the plug's
# refusal arms are ever lost, the test compiles clean and this run goes red.
$refusalFile = Join-Path $dir "$name.cross-refusal"


if ((Test-Path -PathType Leaf $smpFile)) {
    Write-Host "SKIPPED: $name (multi-core -- run build/test-cross-smp.ps1)" -ForegroundColor Yellow
    exit 0
}
if ((Test-Path -PathType Leaf $skipFile)) {
    $reason = (Get-Content -TotalCount 1 $skipFile)
    Write-Host "SKIPPED: $name ($reason)" -ForegroundColor Yellow
    exit 0
}
if ((Test-Path -PathType Leaf $noCrossFile)) {
    $reason = (Get-Content -TotalCount 1 $noCrossFile)
    Write-Host "SKIPPED: $name (no-cross: $reason)" -ForegroundColor Yellow
    exit 0
}
if ((Test-Path -PathType Leaf $slowFile)) {
    Write-Host "SKIPPED: $name (slow)" -ForegroundColor Yellow
    exit 0
}
if ((Test-Path -PathType Leaf $fatalFile)) {
    Write-Host "SKIPPED: $name (fatal)" -ForegroundColor Yellow
    exit 0
}
if ((Test-Path -PathType Leaf $failingFile)) {
    Write-Host "SKIPPED: $name (error test -- frontend only)" -ForegroundColor Yellow
    exit 0
}


Write-Host "=== $($Arch.ToUpper()) : $name ===" -ForegroundColor Cyan


# -- Compile --
$OutDir = Join-Path $Repo "test-output-cross\$Arch"
$testOutDir = Join-Path $OutDir $name
New-Item -ItemType Directory -Force $testOutDir | Out-Null
$elfOut = Join-Path $testOutDir "$name.elf"
$compileLog = Join-Path $testOutDir 'compile.log'

Write-Host -NoNewline '  compile ... '
$prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
& pwsh -NoProfile -File $compileScript -Src $testFile.FullName -Out $elfOut 2>&1 | Out-File -FilePath $compileLog -Encoding UTF8
$compileExit = $LASTEXITCODE
$ErrorActionPreference = $prev

if ((Test-Path -PathType Leaf $refusalFile)) {
    $tags = @(Get-Content $refusalFile | Where-Object { $_ -and -not $_.StartsWith('#') })
    if (($compileExit -eq 0 -and (Test-Path $elfOut))) {
        Write-Host 'FAIL (compiled clean; expected [UNSUPPORTED] refusal)' -ForegroundColor Red
        exit 1
    }
    $logText = if (Test-Path $compileLog) { [System.IO.File]::ReadAllText($compileLog) } else { '' }
    $missing = @($tags | Where-Object { -not $logText.Contains("[UNSUPPORTED] $_") })
    if ($missing.Count -eq 0) {
        Write-Host "PASS (refused by design: $($tags -join ', '))" -ForegroundColor Green
        exit 0
    }
    Write-Host "FAIL (compile failed without expected refusal tag(s): $($missing -join ', '))" -ForegroundColor Red
    exit 1
}
if (($compileExit -ne 0 -or (-not (Test-Path -PathType Leaf $elfOut)))) {
    Write-Host 'FAIL (compile)' -ForegroundColor Red
    exit 1
}
Write-Host 'OK'


# -- Check for .expected --
$expectedFile = Join-Path $dir "$name.expected"
if ((-not (Test-Path -PathType Leaf $expectedFile))) {
    Write-Host '  PASS (compile only)' -ForegroundColor DarkGreen
    exit 0
}


# -- Boot on Renode --
Write-Host -NoNewline '  run ... '
$elfPath = (Resolve-Path $elfOut).Path -replace '\\','/'
$boardPath = (Resolve-Path $boardRepl).Path -replace '\\','/'
$uartLog = (Join-Path $testOutDir 'uart.log') -replace '\\','/'
if ((Test-Path -PathType Leaf $uartLog)) {
    Remove-Item -Force -ErrorAction SilentlyContinue $uartLog
}

$rescContent = @(
    'mach create "codex"'
    "machine LoadPlatformDescription @$boardPath"
    "sysbus LoadELF @$elfPath"
    "uart0 CreateFileBackend @$uartLog true"
    'start'
    "sleep $TimeoutSec"
    'quit'
) -join "`n"
$rescFile = Join-Path $testOutDir 'run.resc'
[System.IO.File]::WriteAllText($rescFile, $rescContent)
$rescPath = $rescFile -replace '\\','/'

$prev2 = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
& $RenodeExe --disable-xwt --console -e "include @$rescPath" 2>&1 | Out-Null
$ErrorActionPreference = $prev2
Start-Sleep -Milliseconds 300


# -- Capture and compare --
$uartLogWin = $uartLog -replace '/','\' 
if ((-not (Test-Path $uartLogWin))) {
    Write-Host 'FAIL (no uart output)' -ForegroundColor Red
    exit 1
}

$raw = [System.IO.File]::ReadAllText($uartLogWin) -replace "`r",''
$allLines = $raw -split "`n"
$lines = [System.Collections.Generic.List[string]]::new()
foreach ($l in $allLines) {
    if ((($l.StartsWith('HEAP:') -or $l.StartsWith('WD:')) -or $l.StartsWith('STACK:'))) {
        continue
    }
    $lines.Add($l)
}
while ($lines.Count -gt 0 -and $lines[$lines.Count - 1] -eq '') { $lines.RemoveAt($lines.Count - 1) }


$expectedText = [System.IO.File]::ReadAllText($expectedFile) -replace "`r",''
$expAllLines = @($expectedText -split "`n")
while ($expAllLines.Count -gt 0 -and $expAllLines[$expAllLines.Count - 1] -eq '') { $expAllLines = $expAllLines[0..($expAllLines.Count - 2)] }
$expLineCount = $expAllLines.Count
if (($lines.Count -gt $expLineCount -and $expLineCount -gt 0)) {
    $lines = [System.Collections.Generic.List[string]]::new([string[]]@($lines | Select-Object -First $expLineCount))
}

$actual = if ($lines.Count -gt 0) { ($lines -join "`n") + "`n" } else { '' }

# The expected side gets the same normalization the actual side got above
# (trailing empties trimmed, one final newline restored). Comparing $actual
# against the RAW file text means a sidecar without a trailing newline can
# never pass: the forced final "`n" on actual has no counterpart. Measured
# 2026-08-06: all 1198 .expected sidecars currently end with a newline, so
# this arm is dormant -- it is what stops the next one that does not from
# failing for a reason that is not about the test.
$expected = if ($expLineCount -gt 0) { ($expAllLines -join "`n") + "`n" } else { '' }

$actualFile = Join-Path $testOutDir 'runtime.actual'
[System.IO.File]::WriteAllText($actualFile, $actual, [System.Text.UTF8Encoding]::new($false))

if (($expected -eq $actual)) {
    Write-Host 'PASS' -ForegroundColor Green
    exit 0
} else {
    Write-Host 'FAIL (output mismatch)' -ForegroundColor Red
    $diffFile = Join-Path $testOutDir 'diff.txt'
    $expLines = $expected -split "`n"
    $actLines = $actual -split "`n"
    $diffOut = [System.Collections.Generic.List[string]]::new()
    $maxLines = [Math]::Max($expLines.Count, $actLines.Count)
    for ($i = 0; $i -lt [Math]::Min($maxLines, 20); $i++) {
        $e = if ($i -lt $expLines.Count) { $expLines[$i] } else { '(missing)' }
        $a2 = if ($i -lt $actLines.Count) { $actLines[$i] } else { '(missing)' }
        if (($e -ne $a2)) {
            $diffOut.Add("line $($i+1):  exp=[$e]  act=[$a2]")
        }
    }
    if ($diffOut.Count -gt 0) {
        [System.IO.File]::WriteAllText($diffFile, ($diffOut -join "`n"), [System.Text.UTF8Encoding]::new($false))
        Get-Content $diffFile
    }
    exit 1
}
