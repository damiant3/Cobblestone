# The comparison the hosted harnesses grade with, in ONE place.
#
# Both hosted arms borrow the bare-metal battery's `.expected` sidecars, so the
# only defensible comparison is the one those sidecars were RECORDED through.
# That definition is `build/test-run.ps1:112-125`, and this mirrors it:
#
#   strip CR; strip a leading SOH from the ACTUAL; drop HEAP:/WD:/STACK: lines;
#   drop trailing blank lines; end with exactly one LF.
#
# Before this existed both harnesses did `-replace CRLF, LF` and nothing else,
# which made them STRICTER than the battery whose oracle they borrow: a subject
# whose output differed only by a trailing newline read as a codegen failure.
# Measured 2026-09-01, that was two false reds per arm on every run
# (apps/annotation-query-test, apps/diagnostic-boot), on wasm, linux and
# windows alike.
#
# WHAT IS DELIBERATELY NOT DONE HERE: the leading SOH is stripped from the
# ACTUAL only, never from the sidecar. 153 of 1446 `.expected` files begin with
# a raw 0x01 and PowerShell's `-eq` ignores it; stripping it from the sidecar as
# well, or moving to an ordinal comparison, is a fleet-wide event in Damian's
# battery and is his call (docs/ExaminersAssay.md, "Why the comparison has not
# simply been changed to ordinal"). Stripping it from both sides would also lose
# the ability to see an actual that begins with a stray SOH when its oracle does
# not, which is a real difference.
#
# The mutation arm for this file is codex/plugs/common/hosted-compare-mutation.ps1.
# Run it after ANY edit here: this decides pass and fail for both parity arms,
# and a relaxation that quietly stops catching a real difference reports exactly
# what a correct comparison reports (L-CAPABILITY-LOST).

function Get-HarnessActual {
    param([string]$Text)
    if ($null -eq $Text) { return '' }
    $raw = ($Text -replace "`r", '') -replace "^\x01", ''
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
# build/test.ps1 does. See the note above on why the SOH is left alone.
function Get-HarnessExpected {
    param([string]$Text)
    if ($null -eq $Text) { return '' }
    return ($Text -replace "`r", '')
}
