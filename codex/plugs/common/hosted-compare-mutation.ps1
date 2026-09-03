# The mutation arm for hosted-compare-lib.ps1.
#
# Relaxing a comparison is the change L-CAPABILITY-LOST is about: a checker that
# stops asking a question reports exactly what one that asks and agrees reports,
# so re-running the arms that already passed measures nothing. This runs the
# cases aimed AT the direction of the relaxation -- trailing whitespace -- and,
# more importantly, the GUARD cases a careless relaxation would swallow.
#
# It grades BOTH comparisons so the change is a before/after rather than an
# assertion: OLD is the `-replace CRLF, LF` the hosted harnesses used before,
# NEW is the library. A case is only interesting when the two disagree.
#
#   pwsh codex/plugs/common/hosted-compare-mutation.ps1
# Exit 0 every case behaves as required, 1 otherwise.

[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'hosted-compare-lib.ps1')

function Compare-Old {
    param([string]$Got, [string]$Want)
    return (($Got -replace "`r`n", "`n") -eq ($Want -replace "`r`n", "`n"))
}
function Compare-New {
    param([string]$Got, [string]$Want)
    return ((Get-HarnessActual $Got) -eq (Get-HarnessExpected $Want))
}

$SOH = [char]1

# kind: RELAX = the case the change exists for, must become equal.
#       GUARD = a real difference the change must NOT start ignoring.
#       SAME  = behaviour that must not move in either direction.
$cases = @(
    @{ k='RELAX'; n='one extra trailing LF';            got="a`nb`n`n";        want="a`nb`n";      req=$true  }
    @{ k='RELAX'; n='missing final LF';                 got="a`nb";            want="a`nb`n";      req=$true  }
    @{ k='RELAX'; n='several trailing blank lines';     got="a`nb`n`n`n`n";    want="a`nb`n";      req=$true  }
    @{ k='RELAX'; n='CRLF actual against LF oracle';    got="a`r`nb`r`n";      want="a`nb`n";      req=$true  }
    @{ k='RELAX'; n='leading SOH on the ACTUAL';        got="${SOH}a`nb`n";    want="a`nb`n";      req=$true  }
    @{ k='RELAX'; n='HEAP: telemetry line';             got="HEAP: 1`na`nb`n"; want="a`nb`n";      req=$true  }
    @{ k='RELAX'; n='WD: and STACK: telemetry';         got="WD:x`na`nSTACK:y`nb`n"; want="a`nb`n"; req=$true }

    @{ k='GUARD'; n='one character differs';            got="a`nc`n";          want="a`nb`n";      req=$false }
    @{ k='GUARD'; n='final content line MISSING';       got="a`n";             want="a`nb`n";      req=$false }
    @{ k='GUARD'; n='extra final content line';         got="a`nb`nc`n";       want="a`nb`n";      req=$false }
    @{ k='GUARD'; n='empty actual';                     got='';                want="a`nb`n";      req=$false }
    @{ k='GUARD'; n='whitespace-only actual';           got="`n`n`n";          want="a`nb`n";      req=$false }
    @{ k='GUARD'; n='INTERIOR blank line dropped';      got="a`nb`n";          want="a`n`nb`n";    req=$false }
    @{ k='GUARD'; n='truncated mid-line';               got='ab';              want="abc`n";       req=$false }
    @{ k='GUARD'; n='leading space added';              got=" a`nb`n";         want="a`nb`n";      req=$false }
    @{ k='GUARD'; n='TRAILING space on a line';         got="a `nb`n";         want="a`nb`n";      req=$false }
    @{ k='GUARD'; n='line order swapped';               got="b`na`n";          want="a`nb`n";      req=$false }
    @{ k='GUARD'; n='actual gains a HEAP-LIKE content line'; got="a`nHEAPX`nb`n"; want="a`nb`n";   req=$false }

    @{ k='SAME';  n='identical';                        got="a`nb`n";          want="a`nb`n";      req=$true  }
    @{ k='SAME';  n='both empty';                       got='';                want='';            req=$true  }
)

$fails = 0
$moved = 0
Write-Host ''
Write-Host ('{0,-6} {1,-42} {2,-6} {3,-6} {4}' -f 'kind', 'case', 'OLD', 'NEW', 'verdict')
Write-Host ('-' * 86)
foreach ($c in $cases) {
    $old = Compare-Old $c.got $c.want
    $new = Compare-New $c.got $c.want
    $ok = ($new -eq $c.req)
    if (-not $ok) { $fails++ }
    if ($old -ne $new) { $moved++ }
    $verdict = if (-not $ok) { "FAIL: NEW says $new, required $($c.req)" }
               elseif ($old -ne $new) { 'moved (this is the repair)' }
               else { 'ok' }
    Write-Host ('{0,-6} {1,-42} {2,-6} {3,-6} {4}' -f $c.k, $c.n, $old, $new, $verdict)
}
Write-Host ''
Write-Host "cases: $($cases.Count), moved by the change: $moved, wrong: $fails"

# A run where nothing moved is not a pass, it is an arm that did not reach its
# subject: the whole point is that the RELAX cases change answer.
$relaxMoved = 0
foreach ($c in ($cases | Where-Object { $_.k -eq 'RELAX' })) {
    if ((Compare-Old $c.got $c.want) -ne (Compare-New $c.got $c.want)) { $relaxMoved++ }
}
if ($relaxMoved -eq 0) {
    Write-Host 'REFUSE: no RELAX case changed answer, so this arm measured nothing (L-VACUOUS).'
    exit 1
}
if ($fails -ne 0) { Write-Host "MUTATION ARM FAILED: $fails case(s) wrong"; exit 1 }
Write-Host "MUTATION ARM PASSED: $relaxMoved relax case(s) repaired, every guard still refuses."
exit 0
