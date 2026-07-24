# Pins CDX9004: a list literal past max-list-literal-elems is REFUSED, and the
# refusal points at the literal rather than at its first element.
#
# The fixture is generated rather than committed. At 65505 elements it is
# ~447 KB of source, which is the wrong thing to carry in the depot for one
# diagnostic -- and it is also the reason this diagnostic went unpinned.
#
# Two legs, because one proves nothing:
#   over  -- 65505 elements must fail with CDX9004
#   under -- 65504 elements (exactly the ceiling) must COMPILE
# A checker that refuses every large literal passes the first leg alone.
[CmdletBinding()]
param([string]$Kernel = 'seed\Codex.cdx')

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$out  = Join-Path $repo 'build-output\list-ceiling'
New-Item -ItemType Directory -Force $out | Out-Null

$ceiling = 65504

function New-Fixture([int]$n, [string]$path) {
    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.Append("Chapter: List Ceiling`r`n`r`nSection: Entry`r`n`r`n  big : List Integer`r`n  big = [")
    for ($i = 1; $i -le $n; $i++) { if ($i -gt 1) { [void]$sb.Append(', ') }; [void]$sb.Append($i % 7) }
    [void]$sb.Append("]`r`n`r`n  opening : [Console] Integer`r`n  opening = act`r`n    (print-line-uni (show (list-length big)))`r`n    0`r`n  end`r`n")
    [System.IO.File]::WriteAllText($path, $sb.ToString(), (New-Object System.Text.UTF8Encoding($false)))
    # the '[' sits at column 9 of "  big = [", the first element at column 10.
    # One column apart is all that is needed to tell the two spans apart.
    return 9
}

$fail = 0

# --- leg 1: over the ceiling must be refused, at the literal's own span
$over = Join-Path $out 'over.codex'
$bracketCol = New-Fixture ($ceiling + 1) $over
$log = Join-Path $out 'over.log'
& pwsh (Join-Path $repo 'build\compile.ps1') -Src $over -Out (Join-Path $out 'over.cdx') -Log $log -Kernel $Kernel *> $null
$hit = Select-String -Path $log -Pattern 'CDX9004' | Select-Object -First 1
if (-not $hit) {
    Write-Host "FAIL over: no CDX9004 for $($ceiling + 1) elements"; $fail++
} else {
    Write-Host "  over: $($hit.Line.Trim())"
    if ($hit.Line -match ':(\d+):(\d+): error CDX9004') {
        $col = [int]$matches[2]
        if ($col -eq $bracketCol) { Write-Host "  over: span is the literal (column $col)" }
        else { Write-Host "FAIL over: CDX9004 column $col, expected the literal's bracket at $bracketCol"; $fail++ }
    } else { Write-Host "FAIL over: could not read a line:column off the diagnostic"; $fail++ }
}

# --- leg 2: exactly at the ceiling must still compile
$under = Join-Path $out 'under.codex'
[void](New-Fixture $ceiling $under)
$ulog = Join-Path $out 'under.log'
& pwsh (Join-Path $repo 'build\compile.ps1') -Src $under -Out (Join-Path $out 'under.cdx') -Log $ulog -Kernel $Kernel *> $null
if (Select-String -Path $ulog -Pattern 'CDX9004' -Quiet) {
    Write-Host "FAIL under: $ceiling elements refused; the ceiling is off by one"; $fail++
} else { Write-Host "  under: $ceiling elements accepted" }

if ($fail -gt 0) { Write-Host "list-ceiling: FAILED ($fail)"; exit 1 }
Write-Host 'list-ceiling: OK (over refused at the literal, under accepted)'
exit 0
