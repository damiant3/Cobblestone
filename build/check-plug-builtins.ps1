# check-plug-builtins.ps1 -- a plug must have an arm for every builtin that reaches its wire
# GENERATED FROM THE CODEX SHELL DSL. Do not edit by hand.
# A hand edit here must NOT be submitted. Change the generator under
# codex/build/, regenerate, and submit the generator and this file
# together. Until then build/check-generated-scripts.ps1 reports this
# file as drifted, and the next regeneration discards the edit.
[CmdletBinding()]
param(
    [switch]$Update
)

# a plug must have an arm for every builtin that reaches its wire
# 
# A plug that does not know a builtin EMITS SOMETHING ANYWAY and reports OK:
# the name passes through as an ordinary call to a function the target never
# defines. The target's own toolchain is the first thing to notice, and for
# most plugs nothing downstream ever runs. Nothing compared a plug's builtin
# table against what actually arrives until this script.
# 
# THE SUBJECT IS THE WIRE, NOT THE SOURCE, and that is the whole design.
# Two source-based versions were measured and thrown away first: asking
# whether a name is MENTIONED in compiler source answers 109 of 161 and is
# noise, and asking whether it appears as a CALL still leaves 21, every one
# of them benign, because literals, erased proof builtins and names the
# compiler only lists in its own emitter tables all look like calls. What
# can actually break is a name arriving in the IR a plug RECEIVES, and
# that set is decided by lowering and dead-code elimination.
# 
# The compiler as a subject is NOT re-checked here. That case is the DDC
# witness, which already fails on a missing builtin because Roslyn refuses
# C# naming a method the emitted prelude does not define. Re-scanning its
# 13.5 MB of IR would buy nothing. Red's split, 2026-08-17.
# 
# Usage: check-plug-builtins.ps1 [-Update]
#   Without -Update: prints OK or the new gaps + exits 0 or 1
#   With -Update: rewrites the baseline (read the delta before taking it)


Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$BuiltinsFile = (Join-Path $Repo 'codex\compiler\Types\Builtins.codex')
$Subject = (Join-Path $Repo 'codex\test\plug-oracle-arith.codex')
$Seed = (Join-Path $Repo 'seed\Codex.cdx')
$CompileScript = (Join-Path $Repo 'build\compile.ps1')
$IrOut = (Join-Path $Repo 'build\output\plug-builtins.ir')
$IrLog = (Join-Path $Repo 'build\output\plug-builtins.ir.log')
$BaseFile = (Join-Path $Repo 'build\plug-builtin-baseline.txt')

foreach ($f in @($BuiltinsFile, $Subject, $Seed, $CompileScript)) {
    if ((-not (Test-Path -PathType Leaf $f))) {
        Write-Host ([string]'check-plug-builtins: missing ' + $f)
        exit 1
    }
}


$declared = @{}
foreach ($line in ([System.IO.File]::ReadAllLines($BuiltinsFile))) {
    foreach ($m in ([regex]::Matches($line, 'bs-name = "(?<h>[^"]+)"'))) {
        $declared[$m.Groups['h'].Value] = $true
    }
}


# -Passes 'text-plug' is REQUIRED and is not a detail. It is the pipeline a
# source plug receives, and it changes what reaches the wire: with the inline
# passes on, a polymorphic definition arrives already specialised. IR taken
# under the default pipeline would be measuring a wire no plug reads.
# 
# The IR goes to the LOG rather than -Out under -IrUni. Both are passed so
# the compile behaves exactly as the plug pipeline drives it.
# 
# A builtin call arrives as (apply (name "show" (fn ..)) ..), which is the
# SAME form a local call takes, (name "store-two" ..). Intersecting with the
# declared set is the only thing separating the two; a check keying on the
# shape alone would report every local function as a missing builtin arm.

& 'pwsh' -NoProfile -File $CompileScript -Src $Subject -Out $IrOut -Log $IrLog -IrUni -Passes 'text-plug' -Kernel $Seed
if ((-not (Test-Path -PathType Leaf $IrLog))) {
    Write-Host ([string]'check-plug-builtins: the compile produced no IR log at ' + $IrLog)
    exit 1
}
$wire = @{}
foreach ($line in ([System.IO.File]::ReadAllLines($IrLog))) {
    foreach ($m in ([regex]::Matches($line, '\(name "(?<h>[^"]+)"'))) {
        if ($declared.ContainsKey($m.Groups['h'].Value)) {
            $wire[$m.Groups['h'].Value] = $true
        }
    }
}


# An empty side compares clean against anything. A check that cannot fail is
# a comment, so refuse rather than report OK.

if ((($declared.Count -eq 0) -or ($wire.Count -eq 0))) {
    Write-Host ([string]'check-plug-builtins: FAILED to extract (declared=' + ([string]$declared.Count + ([string]' wire=' + ([string]$wire.Count + ')'))))
    Write-Host '  the extraction broke, not the plugs -- fix this script before trusting a green'
    exit 1
}


# TWO registration shapes, and modelling one accuses the other. python, zig
# and csharp use a record table (name = "x"); javascript and wasm use an
# if/else dispatch chain (n == "x"). Measured 2026-08-17: with only the
# table pattern, javascript and wasm extract ZERO and every builtin on the
# wire reads as missing -- seven phantom rows per plug, which is the gate
# nobody reads twice.
# 
# A THIRD shape, added 2026-08-18: an ordered names list dispatched by
# INDEX. pascal, ada, babbage, cobol, elixir, fortran, nim, objc and riscv
# all write one. Keying on the quoted names alone would take every emitted
# fragment in the file with them, so the extraction opens on a line matching
# 'builtin-names = [', takes quoted names while it is open, and closes on
# the first ']'. pascal extracts 23 that way against 6 before: the 20-name
# list, plus True, False and Nothing, which are declared builtins this plug
# really does answer at 'n == "True"'.
# 
# ONLY THE WIRED PLUGS ARE COVERED, and that is a limit rather than an
# oversight. Modelling a shape does not wire a plug: the list above is what
# is checked, and a plug is added to it only once its extraction has been
# read name by name against its table. A plug whose shape this script does
# not model must be left alone, not accused. The floor below is what
# enforces that: a thin extraction is nearly as dangerous as an empty one,
# and it fails LOUDLY here instead of quietly listing phantoms.
# 
# ada, elixir, nim, objc and cobol were read name by name and wired
# 2026-08-18. ada extracts 60 against a 46-name list, the other four 31
# against 27; the surplus in every case is True, False, Nothing and
# __narrow, answered at 'n == "x"'. ada's 'n ==' also catches Integer,
# Text, Boolean and six real-* TYPE names, and cobol's table pattern
# catches the emitted fragments "0", "1" and "WS-". None of those nine is
# a DECLARED builtin, so none can reach the wire and none can mask a gap.
# 
# THREE PLUGS THAT WRITE THIS SHAPE ARE STILL OUT, each for its own
# reason. fortran extracts 50 and belongs to plugs-backlog 1.7, which
# measures it against a wider subject. riscv is fester's lane (1.3).
# babbage extracts 12 and FAILS the floor, which is the floor being right:
# the Analytical Engine has no text and no list, so list-at, list-length,
# list-push and list-snoc are absent on purpose and babbage answers them
# with the !UNSUPPORTED: refusal instead of an arm. Wiring it would mean
# lowering the floor for everyone to accuse a plug that is behaving.

$wireNames = @(($wire.Keys | Sort-Object))
$divergent = @()
foreach ($p in @('python', 'javascript', 'zig', 'wasm', 'csharp', 'pascal', 'ada', 'elixir', 'nim', 'objc', 'cobol')) {
    $reg = @{}
    $inList = $false
    foreach ($f in Get-ChildItem (Join-Path (Join-Path $Repo 'codex\plugs') $p) -Filter '*.codex' -File) {
        foreach ($line in ([System.IO.File]::ReadAllLines($f.FullName))) {
            foreach ($m in ([regex]::Matches($line, 'name = "(?<h>[^"]+)"'))) {
                $reg[$m.Groups['h'].Value] = $true
            }
            foreach ($m in ([regex]::Matches($line, 'n == "(?<h>[^"]+)"'))) {
                $reg[$m.Groups['h'].Value] = $true
            }
            if (($line -match 'builtin-names\s*=\s*\[')) {
                $inList = $true
            }
            if ($inList) {
                foreach ($m in ([regex]::Matches($line, '"(?<h>[^"]+)"'))) {
                    $reg[$m.Groups['h'].Value] = $true
                }
                if (($line -match ']')) {
                    $inList = $false
                }
            }
        }
    }
    if ((-not ($reg.Count -gt 20))) {
        Write-Host ([string]'check-plug-builtins: FAILED to extract for ' + ([string]$p + ([string]' (' + ([string]$reg.Count + ([string]' names, floor ' + ([string]20 + ')'))))))
        Write-Host '  this plug registers builtins in a shape neither pattern models.'
        Write-Host '  teach the script that shape; do NOT read the gaps below as real.'
        exit 1
    }
    foreach ($b in $wireNames) {
        if ((-not $reg.ContainsKey($b))) {
            $divergent += ([string]$p + ([string]' ' + $b))
        }
    }
}
$divergent = @(($divergent | Sort-Object))


if ($Update) {
    $header = @('# plug-builtin-baseline.txt -- generated by build/check-plug-builtins.ps1 -Update', '#', '# One line per ''<plug> <builtin>'': a builtin that REACHES the plug''s wire', '# and has no arm in that plug''s table. The plug emits a call to a function', '# its target never defines, and only that target''s toolchain would notice.', '#', '# This file is the KNOWN residue. The check fails on anything not listed', '# here. Shrinking it is the work -- give the plug an arm. Growing it needs', '# a reason in the CL description.', '')
    Set-Content -Path $BaseFile -Value ($header + $divergent) -Encoding UTF8
    Write-Host ([string]'baseline updated: ' + ([string]@($divergent).Count + ' known gap(s)'))
    foreach ($d in $divergent) {
        Write-Host ([string]'  ' + $d)
    }
    exit 0
}


if ((-not (Test-Path -PathType Leaf $BaseFile))) {
    Write-Host ([string]([string]'check-plug-builtins: no baseline at ' + $BaseFile) + ' -- run with -Update')
    exit 1
}

$baseline = @((([System.IO.File]::ReadAllLines($BaseFile)) | Where-Object { ((-not ($_ -match '^\s*#')) -and ($_ -match '\S')) } | ForEach-Object { $_.Trim() }))

$new = @(($divergent | Where-Object { (-not ($baseline -contains $_)) }))
$fixed = @(($baseline | Where-Object { (-not ($divergent -contains $_)) }))


if ((@($new).Count -gt 0)) {
    Write-Host ([string]'check-plug-builtins: FAIL -- ' + ([string]@($new).Count + ' new builtin gap(s)'))
    foreach ($g in $new) {
        Write-Host ([string]'  ' + $g)
    }
    Write-Host ''
    Write-Host '  Each line is ''<plug> <builtin>'': that builtin reaches the plug''s wire'
    Write-Host '  and the plug has no arm for it, so it emits a call the target never'
    Write-Host '  defines. Add the arm, or record it: build/check-plug-builtins.ps1 -Update'
    exit 1
}

if ((@($fixed).Count -gt 0)) {
    Write-Host ([string]'check-plug-builtins: OK -- and ' + ([string]@($fixed).Count + ' baselined gap(s) are gone:'))
    foreach ($g in $fixed) {
        Write-Host ([string]([string]'  ' + $g) + ' (now handled -- drop it from the baseline)')
    }
    exit 0
}

Write-Host ([string]'check-plug-builtins: OK (' + ([string]$wire.Count + ([string]' builtins on the wire, ' + ([string]@($divergent).Count + ' known gap(s))'))))
exit 0
