# The comparison the hosted harnesses grade with, in ONE place.
#
# Both hosted arms borrow the bare-metal battery's `.expected` sidecars, so the
# only defensible comparison is the one those sidecars were RECORDED through.
# That definition is `build/test-run.ps1:112-125`, and this mirrors it:
#
#   strip CR; drop HEAP:/WD:/STACK: lines; drop trailing blank lines; end with
#   exactly one LF; compare ordinally, as build/test.ps1 does.
#
# Before this existed both harnesses did `-replace CRLF, LF` and nothing else,
# which made them STRICTER than the battery whose oracle they borrow: a subject
# whose output differed only by a trailing newline read as a codegen failure.
# Measured 2026-09-01, that was two false reds per arm on every run
# (apps/annotation-query-test, apps/diagnostic-boot), on wasm, linux and
# windows alike.
#
# The mutation arm for this file is codex/plugs/common/hosted-compare-mutation.ps1.
# Run it after ANY edit here: this decides pass and fail for both parity arms,
# and a relaxation that quietly stops catching a real difference reports exactly
# what a correct comparison reports (L-CAPABILITY-LOST).

function Get-HarnessActual {
    param([string]$Text)
    if ($null -eq $Text) { return '' }
    $raw = $Text -replace "`r", ''
    $lines = [System.Collections.Generic.List[string]]::new()
    foreach ($l in ($raw -split "`n")) {
        if ($l.StartsWith('HEAP:') -or $l.StartsWith('WD:') -or $l.StartsWith('STACK:')) { continue }
        [void]$lines.Add($l)
    }
    while ($lines.Count -gt 0 -and $lines[$lines.Count - 1] -eq '') { $lines.RemoveAt($lines.Count - 1) }
    if ($lines.Count -gt 0) { return (($lines -join "`n") + "`n") }
    return ''
}

# The sidecar side gets CR stripped and nothing else, which is what
# build/test.ps1 does.
function Get-HarnessExpected {
    param([string]$Text)
    if ($null -eq $Text) { return '' }
    return ($Text -replace "`r", '')
}

# PowerShell's -eq is culture-sensitive and case-insensitive: it ignores SOH and
# NUL and equates "A" with "a".
function Test-HarnessMatch {
    param([string]$Got, [string]$Want)
    return [string]::Equals($Got, $Want, [StringComparison]::Ordinal)
}
