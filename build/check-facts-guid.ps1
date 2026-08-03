# The fact-store partition type GUID exists in three places and they must
# agree. A runner, not a promise: a copied constant is exactly the divergence
# Foreword chapter FactLog exists to prevent, and the three copies are here
# because the alternatives are worse -- the IMG plug is its own compilation
# unit and citing Gpt would drag Device.Block into an image writer, and
# build/build-img.ps1 is PowerShell and cites nothing.
#
#   codex/foreword/core/Gpt.codex        gpt-codex-facts-guid   (the reader)
#   codex/plugs/img/GptWriter.codex      gpt-codex-facts-guid   (the plug)
#   build/build-img.ps1                  a [byte[]] literal     (the shipped stick)
#
# A disagreement is silent and expensive: the stick is written with one type
# and the guest looks for another, so the store finds no region, refuses every
# write, and reports "0 disk facts" forever with nothing anywhere saying why.
#
# Exit 1 on disagreement. Wired into build/build.ps1.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

function Fail($msg) { Write-Host "FAIL: $msg"; exit 1 }

# --- the two Codex sources: a decimal list on the gpt-codex-facts-guid line ---
function Get-CodexGuid([string]$relPath) {
    $path = Join-Path $Repo $relPath
    if (-not (Test-Path -PathType Leaf $path)) { Fail "$relPath is missing" }
    $line = Select-String -Path $path -Pattern 'gpt-codex-facts-guid\s*:\s*List Integer\s*=\s*\[' |
            Select-Object -First 1
    if (-not $line) { Fail "$relPath declares no gpt-codex-facts-guid" }
    if ($line.Line -notmatch '\[([^\]]*)\]') { Fail "$relPath : could not read the byte list" }
    $bytes = $matches[1].Split(',') | ForEach-Object { [int]$_.Trim() }
    if ($bytes.Count -ne 16) { Fail "$relPath : GUID has $($bytes.Count) bytes, want 16" }
    return ,$bytes
}

# --- the PowerShell writer: a hex [byte[]] literal on the marked line ---
function Get-Ps1Guid([string]$relPath) {
    $path = Join-Path $Repo $relPath
    if (-not (Test-Path -PathType Leaf $path)) { Fail "$relPath is missing" }
    $line = Select-String -Path $path -Pattern 'WBytes \$fsOff \(\[byte\[\]\]@\(' |
            Select-Object -First 1
    if (-not $line) { Fail "$relPath writes no fact-store type GUID" }
    if ($line.Line -notmatch '@\(([^\)]*)\)') { Fail "$relPath : could not read the byte list" }
    $bytes = $matches[1].Split(',') | ForEach-Object { [int]("0x" + ($_.Trim() -replace '^0x','')) }
    if ($bytes.Count -ne 16) { Fail "$relPath : GUID has $($bytes.Count) bytes, want 16" }
    return ,$bytes
}

$sources = @(
    @{ path = 'codex/foreword/core/Gpt.codex';    bytes = (Get-CodexGuid 'codex/foreword/core/Gpt.codex') }
    @{ path = 'codex/plugs/img/GptWriter.codex';  bytes = (Get-CodexGuid 'codex/plugs/img/GptWriter.codex') }
    @{ path = 'build/build-img.ps1';              bytes = (Get-Ps1Guid   'build/build-img.ps1') }
)

$reference = $sources[0]
$bad = @()
foreach ($s in $sources) {
    $joined = ($s.bytes -join ',')
    if ($joined -ne ($reference.bytes -join ',')) { $bad += "$($s.path): $joined" }
}

if ($bad.Count -gt 0) {
    Write-Host "FAIL: the fact-store partition type GUID disagrees across writers."
    Write-Host "  $($reference.path): $($reference.bytes -join ',')"
    $bad | ForEach-Object { Write-Host "  $_" }
    exit 1
}

Write-Host "check-facts-guid: OK ($($sources.Count) sources agree)"
exit 0
