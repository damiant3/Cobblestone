# check-run-list.ps1 -- the arms for codex-vm -run-list (CurrentPlan "The
# battery choreography" item 2).
#
# Hand-maintained, like the other check-*.ps1 in this directory.
#
# -run-list spawns a FRESH codex-vm child per line, so a batch is
# byte-identical to N single runs by construction rather than by test. These
# arms are what says the construction is actually what the binary does.
#
# Every arm carries the reading that would falsify it, because an arm that
# cannot go red measures nothing (L-FALSIF). Arm 1 is ablated in place: the
# same comparison is run against a deliberately wrong kernel and must differ.

[CmdletBinding()]
param(
    [string]$Vm = (Join-Path (Split-Path $PSScriptRoot) 'tools' 'codex-vm.exe'),
    [switch]$KeepWork
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot
$fail = 0
function Fail([string]$m) { Write-Host "FAIL: $m" -ForegroundColor Red; $script:fail++ }
function Ok([string]$m)   { Write-Host "ok:   $m" }

if (-not (Test-Path -PathType Leaf $Vm)) { Write-Host "REFUSED: no codex-vm at $Vm"; exit 1 }

# The kernels the arms need. REFUSE rather than skip: a check that quietly
# does nothing when its inputs are missing reports green for having run
# nothing, which is the failure this file exists to catch one level down.
$canary  = Join-Path $root 'build\output\canary-factorial.cdx'
$signer  = Join-Path $root 'build\output\cdx-sign.cdx'
$sut     = Join-Path $root 'build\output\Sut.cdx'
$bigsrc  = Join-Path $root 'codex\compiler\opening.codex'
foreach ($p in @($canary, $signer, $sut, $bigsrc)) {
    if (-not (Test-Path -PathType Leaf $p)) {
        Write-Host "REFUSED: missing $p -- build first" -ForegroundColor Red
        exit 1
    }
}

# Arm 1 needs kernels that FINISH. The compiler kernel does not: measured
# 2026-08-22 it runs past 60 s on a 151-byte input as readily as on a
# 113 KB one, which is why it is arm 3's subject and never arm 1's.
$oracles = @()
foreach ($n in 'oracle-cce', 'oracle-scalar', 'oracle-vector') {
    $p = Join-Path $root "build-output\$n.cdx"
    if (Test-Path -PathType Leaf $p) { $oracles += @{ n = $n; a = @('-kernel', $p) } }
}

$work = Join-Path ([System.IO.Path]::GetTempPath()) ("crl-" + [System.Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $work | Out-Null
$A = Join-Path $work 'A'; $B = Join-Path $work 'B'
New-Item -ItemType Directory -Force $A, $B | Out-Null

function Invoke-RunList([string]$listPath, [string]$errPath, [int]$WallMs = 0) {
    $vmArgs = @('-run-list', $listPath)
    if ($WallMs -gt 0) { $vmArgs += @('-run-list-wall', "$WallMs") }
    $p = Start-Process -FilePath $Vm -ArgumentList $vmArgs -PassThru -NoNewWindow -Wait -RedirectStandardError $errPath
    return $p.ExitCode
}

function Get-EndLines([string]$errPath) {
    return @(Get-Content $errPath | Where-Object { $_ -like 'RUN-LIST END *' })
}

# Quote every token, which is what -run-list's own splitter expects and what
# keeps a path with a space in one piece.
#
# Build list lines ONLY through this. Writing them inline as 'a' + $x + 'b',
# 'c' + $y + 'd' does not make a two-element array: PowerShell binds the
# comma TIGHTER than +, so that is one string plus an array, flattened with
# $OFS, and the whole list arrives as a single line. codex-vm then parses it
# as one run in which the last -kernel and -output win, which looks exactly
# like the neighbours having been taken by a failing line.
function Format-RunLine([string[]]$Tokens) {
    return ($Tokens | ForEach-Object { '"' + $_ + '"' }) -join ' '
}

try {

# ---- arm 1: a batch is byte-identical to N single runs -------------------
$subjects = @(
    @{ n = 'canary'; a = @('-kernel', $canary) },
    @{ n = 'signer'; a = @('-kernel', $signer) }
) + $oracles
Write-Host "arm1 subjects: $(($subjects | ForEach-Object { $_.n }) -join ', ')"
if ($oracles.Count -eq 0) {
    Write-Host "note: no oracle kernels in build-output, so arm1 runs on 2 subjects, not 5"
}
foreach ($s in $subjects) {
    $out = Join-Path $A ($s.n + '.txt')
    $vmArgs = $s.a + @('-output', $out, '-mem', '3072', '-headless')
    Start-Process -FilePath $Vm -ArgumentList $vmArgs -NoNewWindow -Wait -RedirectStandardError (Join-Path $work 'single.err') | Out-Null
}
$lines = foreach ($s in $subjects) {
    $out = Join-Path $B ($s.n + '.txt')
    Format-RunLine ($s.a + @('-output', $out, '-mem', '3072', '-headless'))
}
$list1 = Join-Path $work 'arm1.txt'
Set-Content $list1 $lines
Invoke-RunList $list1 (Join-Path $work 'arm1.err') | Out-Null

$identical = 0
foreach ($s in $subjects) {
    $pa = Join-Path $A ($s.n + '.txt'); $pb = Join-Path $B ($s.n + '.txt')
    if (-not (Test-Path $pa) -or -not (Test-Path $pb)) { Fail "arm1 $($s.n): an arm produced no output"; continue }
    if ((Get-Item $pa).Length -eq 0) { Fail "arm1 $($s.n): the single run produced an EMPTY output, so the comparison proves nothing"; continue }
    if ((Get-FileHash $pa).Hash -eq (Get-FileHash $pb).Hash) { $identical++ }
    else { Fail "arm1 $($s.n): batch output differs from the single run" }
}
if ($identical -eq $subjects.Count) {
    $sizes = ($subjects | ForEach-Object { (Get-Item (Join-Path $A ($_.n + '.txt'))).Length }) -join '/'
    Ok "arm1 byte-identical on $identical of $($subjects.Count), sizes $sizes"
}

# ---- arm 1 ablation: the same comparison must be able to go red ----------
$wrong = Join-Path $work 'abl.txt'
Set-Content $wrong ('"-kernel" "' + $signer + '" "-output" "' + (Join-Path $B 'canary.txt') + '" "-mem" "3072" "-headless"')
Invoke-RunList $wrong (Join-Path $work 'abl.err') | Out-Null
if ((Get-FileHash (Join-Path $A 'canary.txt')).Hash -eq (Get-FileHash (Join-Path $B 'canary.txt')).Hash) {
    Fail 'arm1 ablation: a deliberately wrong kernel still compared IDENTICAL, so arm1 is blind'
} else { Ok 'arm1 ablation: the wrong kernel differs, so arm1 can fail' }

# ---- arm 2: a child that is stopped does not take the rest of the list ---
$corrupt = Join-Path $work 'corrupt.cdx'
$bytes = New-Object byte[] 4096
(New-Object System.Random 7).NextBytes($bytes)
[System.IO.File]::WriteAllBytes($corrupt, $bytes)
$o1 = Join-Path $work 'h1.txt'; $o3 = Join-Path $work 'h3.txt'
$list2 = Join-Path $work 'arm2.txt'
Set-Content $list2 @(
    (Format-RunLine @('-kernel', $canary,  '-output', $o1, '-mem', '3072', '-headless')),
    (Format-RunLine @('-kernel', $corrupt, '-output', (Join-Path $work 'h2.txt'), '-mem', '3072', '-headless')),
    (Format-RunLine @('-kernel', $canary,  '-output', $o3, '-mem', '3072', '-headless'))
)
Invoke-RunList $list2 (Join-Path $work 'arm2.err') | Out-Null
if ((Test-Path $o1) -and (Test-Path $o3) -and (Get-Item $o1).Length -gt 0 -and (Get-Item $o3).Length -gt 0) {
    Ok 'arm2 a corrupt kernel mid-list did not take its neighbours'
} else { Fail 'arm2 a corrupt kernel mid-list took its neighbours with it' }

# ---- arm 3: the wall budget stops one line and only that line ------------
# 2000 ms against a compile that runs past 60 s and neighbours at ~80 ms, so
# neither side of the comparison sits near the budget (L-ADJECTIVE: the
# margin is a number here, not a word).
$t1 = Join-Path $work 't1.txt'; $t3 = Join-Path $work 't3.txt'
$list3 = Join-Path $work 'arm3.txt'
Set-Content $list3 @(
    (Format-RunLine @('-kernel', $canary, '-output', $t1, '-mem', '3072', '-headless')),
    (Format-RunLine @('-kernel', $sut, '-input', $bigsrc, '-output', (Join-Path $work 't2.txt'), '-mem', '3072', '-headless')),
    (Format-RunLine @('-kernel', $canary, '-output', $t3, '-mem', '3072', '-headless'))
)
$rc3 = Invoke-RunList $list3 (Join-Path $work 'arm3.err') 2000
$ends3 = Get-EndLines (Join-Path $work 'arm3.err')
$timeouts = @($ends3 | Where-Object { $_ -match 'exit=TIMEOUT' })
if ($timeouts.Count -ne 1) { Fail "arm3 expected exactly one TIMEOUT line, saw $($timeouts.Count)" }
elseif (-not ($timeouts[0] -match '\[2/3\]')) { Fail 'arm3 the TIMEOUT landed on the wrong line' }
elseif (-not ((Test-Path $t1) -and (Test-Path $t3))) { Fail 'arm3 the stopped child took its neighbours with it' }
elseif ($rc3 -eq 0) { Fail 'arm3 the supervisor reported success despite a timeout' }
else { Ok 'arm3 the wall budget stopped line 2 alone, neighbours ran, supervisor exit 1' }

# ---- arm 4: a dropped-bytes report is attributed to its own line ---------
# -args echoes its string to the child's stderr, which is how a child is made
# to emit the phrase without a real drop. This proves the SCANNER and the
# ATTRIBUTION; it does not provoke a guest-side drop, and nothing external
# can. Line 3 is the negative control: the GPU triangle-cap warning also
# contains the word DROPPED and must NOT be counted.
$list4 = Join-Path $work 'arm4.txt'
Set-Content $list4 @(
    (Format-RunLine @('-kernel', $canary, '-output', (Join-Path $work 'd1.txt'), '-mem', '3072', '-headless')),
    (Format-RunLine @('-kernel', $canary, '-output', (Join-Path $work 'd2.txt'), '-mem', '3072', '-headless',
                      '-args', 'SERIAL: 12345 guest serial byte(s) DROPPED (synthetic)')),
    (Format-RunLine @('-kernel', $canary, '-output', (Join-Path $work 'd3.txt'), '-mem', '3072', '-headless',
                      '-args', 'GPU: frame submitted 9 triangles, cap 4 -- the excess is DROPPED')),
    (Format-RunLine @('-kernel', $canary, '-output', (Join-Path $work 'd4.txt'), '-mem', '3072', '-headless'))
)
Invoke-RunList $list4 (Join-Path $work 'arm4.err') | Out-Null
$ends4 = Get-EndLines (Join-Path $work 'arm4.err')
if ($ends4.Count -ne 4) { Fail "arm4 expected 4 END lines, saw $($ends4.Count)" }
else {
    $bad = 0
    if (-not ($ends4[1] -match 'dropped=12345')) { Fail 'arm4 the SERIAL DROPPED phrase was not counted on its own line'; $bad++ }
    foreach ($i in 0, 2, 3) {
        if (-not ($ends4[$i] -match 'dropped=0')) { Fail "arm4 line $($i+1) inherited a drop that was not its own"; $bad++ }
    }
    if ($bad -eq 0) { Ok 'arm4 the drop is counted on its own line, neighbours read 0, and the GPU DROPPED line is not counted' }
}

# ---- arm 5: a list may not nest -----------------------------------------
# A line carrying -run-list would have a child supervise a list of its own,
# and a line naming its OWN file is an unbounded fork bomb on a box the whole
# fleet shares. The second line proves the refusal is per-line and does not
# abandon the rest.
$n2 = Join-Path $work 'n2.txt'
$list5 = Join-Path $work 'arm5.txt'
Set-Content $list5 @(
    (Format-RunLine @('-run-list', $list5)),
    (Format-RunLine @('-kernel', $canary, '-output', $n2, '-mem', '3072', '-headless'))
)
$rc5 = Invoke-RunList $list5 (Join-Path $work 'arm5.err')
$ends5 = Get-EndLines (Join-Path $work 'arm5.err')
if ($ends5.Count -ne 2) { Fail "arm5 expected 2 END lines, saw $($ends5.Count)" }
elseif (-not ($ends5[0] -match 'exit=SPAWNFAIL')) { Fail 'arm5 a nested -run-list line was not refused' }
elseif (-not (Test-Path $n2)) { Fail 'arm5 the refused line took the rest of the list with it' }
elseif ($rc5 -eq 0) { Fail 'arm5 the supervisor reported success despite refusing a line' }
else { Ok 'arm5 a nested -run-list line is refused and the next line still runs' }

# ---- arm 6: a short WRITE is reported, not merely a short capture --------
# Arm 4 is about bytes that never reached the BUFFER. This is about bytes that
# reached the buffer and not the FILE. fwrite's return and fclose's were
# discarded and "Output: N bytes" printed output_len whatever had happened, so
# a truncated file read as a healthy run and -run-list reported that same
# self-made number as output=N: nothing in the loop compared what codex-vm
# said it wrote against what the file holds. The real causes are a full disk
# and an I/O error, so CODEX_VM_SHORT_WRITE_AT arranges it instead.
#
# The cap is the CONTROL's own length minus one, so exactly one byte is lost
# and the arm asserts dropped=1 rather than "nonzero". A fixed cap would pass
# vacuously the day the canary's output falls below it (L-VACUOUS).
$s1 = Join-Path $work 's1.txt'
$s2 = Join-Path $work 's2.txt'
$list6 = Join-Path $work 'arm6.txt'
Set-Content $list6 @( (Format-RunLine @('-kernel', $canary, '-output', $s1, '-mem', '3072', '-headless')) )
Invoke-RunList $list6 (Join-Path $work 'arm6-ctl.err') | Out-Null
# @() at the call site, not inside Get-EndLines: a function returning a
# one-element array hands back the ELEMENT, and every arm above happens to
# expect two or more lines so none of them met it.
$ctl = @(Get-EndLines (Join-Path $work 'arm6-ctl.err'))
$ctlLen = if (Test-Path $s1) { (Get-Item $s1).Length } else { 0 }

if ($ctl.Count -ne 1) { Fail "arm6 control expected 1 END line, saw $($ctl.Count)" }
elseif ($ctlLen -lt 2) { Fail "arm6 the canary wrote $ctlLen byte(s); too little to truncate, so the arm would pass vacuously" }
elseif (-not ($ctl[0] -match 'dropped=0')) { Fail 'arm6 the control reported a drop of its own' }
elseif (-not ($ctl[0] -match "output=$ctlLen\b")) { Fail "arm6 the control said output=? but the file holds $ctlLen bytes" }
else {
    $cap = $ctlLen - 1
    $list6b = Join-Path $work 'arm6b.txt'
    Set-Content $list6b @( (Format-RunLine @('-kernel', $canary, '-output', $s2, '-mem', '3072', '-headless')) )
    $env:CODEX_VM_SHORT_WRITE_AT = "$cap"
    try { Invoke-RunList $list6b (Join-Path $work 'arm6.err') | Out-Null }
    finally { Remove-Item Env:\CODEX_VM_SHORT_WRITE_AT -ErrorAction SilentlyContinue }
    $inj = @(Get-EndLines (Join-Path $work 'arm6.err'))
    $injText = (Get-Content (Join-Path $work 'arm6.err') -Raw)
    $injLen = if (Test-Path $s2) { (Get-Item $s2).Length } else { -1 }

    $bad = 0
    if ($inj.Count -ne 1) { Fail "arm6 injected expected 1 END line, saw $($inj.Count)"; $bad++ }
    if ($injLen -ne $cap) { Fail "arm6 the injection was meant to leave $cap bytes on disk and left $injLen"; $bad++ }
    if (-not ($injText -match 'the loss is the WRITER')) { Fail 'arm6 the short write did not name the writer as the layer'; $bad++ }
    if (-not ($injText -match 'guest serial byte\(s\) DROPPED')) { Fail 'arm6 the short write did not use the phrase every reader refuses on'; $bad++ }
    if ($inj.Count -eq 1 -and -not ($inj[0] -match 'dropped=1\b')) { Fail "arm6 one lost byte was not counted as dropped=1: $($inj[0])"; $bad++ }
    if ($inj.Count -eq 1 -and -not ($inj[0] -match "output=$cap\b")) { Fail "arm6 output= still reported the buffer's length rather than the bytes written: $($inj[0])"; $bad++ }
    if ($bad -eq 0) { Ok "arm6 a write short by one byte is counted, named as the WRITER, and output= reports what reached the file ($cap of $ctlLen)" }
}

} finally {
    if ($KeepWork) { Write-Host "work kept at $work" }
    else { Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue }
}

if ($fail -gt 0) { Write-Host ""; Write-Host "check-run-list: $fail arm(s) FAILED" -ForegroundColor Red; exit 1 }
Write-Host ""
Write-Host "check-run-list: all arms green"
exit 0
