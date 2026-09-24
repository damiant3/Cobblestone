[CmdletBinding()]
param(
    [string]$Only = '',
    [string]$Diff = '',
    [string]$OutRoot = '',
    [string]$Kernel = '',
    [switch]$AllowStaleKernel,
    [switch]$Update,
    [switch]$UpdateBytes
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $Repo

$BaseFile = Join-Path $Repo 'build\generated-scripts-baseline.txt'
$BytesFile = Join-Path $Repo 'build\generated-scripts-bytes.txt'

function Read-BytesResidue([string]$path) {
    $t = @{}
    if (-not (Test-Path -PathType Leaf $path)) { return $t }
    foreach ($line in Get-Content $path) {
        $bare = ($line -split '#')[0]
        if ($bare.Trim() -eq '') { continue }
        $p = $bare -split '\s+' | Where-Object { $_ -ne '' }
        if ($p.Count -ge 4) { $t[$p[0]] = @{ S = [int]$p[1]; E = [int]$p[2]; I = [int]$p[3] } }
    }
    return $t
}

function Get-ByteCause($r) {
    $blanks = $r.ByteS - $r.ByteE
    $word = if ($blanks -lt 0) { 'more' } else { 'fewer' }
    if ($r.ByteI -eq 0 -and $blanks -ne 0) { return "blank lines only, emitted $([Math]::Abs($blanks)) $word" }
    if ($r.ByteI -eq 0) { return 'blank lines only, a blank moved' }
    if ($blanks -eq 0) { return "indentation only, $($r.ByteI) line(s)" }
    return "indentation on $($r.ByteI) line(s), and emitted $([Math]::Abs($blanks)) $word blank line(s)"
}

function Write-Record([string]$Path, [string[]]$Lines, [string]$What) {
    $new = ($Lines -join "`r`n") + "`r`n"
    if ((Test-Path -PathType Leaf $Path) -and ((Get-Content $Path -Raw) -eq $new)) {
        Write-Host "$What unchanged"
        return
    }
    try { Set-Content -Path $Path -Value $new -NoNewline -Encoding utf8; Write-Host "$What updated" }
    catch { Write-Host "$What NOT written (read-only?): $Path"; Write-Host "  p4 edit it and re-run -Update" }
}

& (Join-Path $Repo 'build/checks/tool-catalog.ps1') -Repo $Repo
if ($LASTEXITCODE -ne 0) { exit 1 }
$toolCatalog = Get-Content -LiteralPath (Join-Path $Repo 'build/tool-catalog.json') -Raw -Encoding utf8 | ConvertFrom-Json

$DepotSeed = Join-Path $Repo 'seed\Codex.cdx'
if (-not $Kernel) { $Kernel = $DepotSeed }
if (-not (Test-Path -PathType Leaf $Kernel)) {
    Write-Host "MISSING kernel: $Kernel"
    exit 2
}
$digest = (Get-FileHash $Kernel -Algorithm SHA256).Hash.Substring(0, 16)
$seedDigest = if (Test-Path -PathType Leaf $DepotSeed) { (Get-FileHash $DepotSeed -Algorithm SHA256).Hash.Substring(0, 16) } else { '' }
if ((-not $AllowStaleKernel) -and ($Kernel -like '*build-output*') -and $seedDigest -and ($digest -ne $seedDigest)) {
    Write-Host "REFUSED: the kernel is a build-output binary and is not the depot seed."
    Write-Host "         kernel $digest, seed $seedDigest. build-output holds whichever compiler ran last,"
    Write-Host "         so the drift table would describe that binary rather than the tree's compiler."
    Write-Host "         Pass -Kernel explicitly, or -AllowStaleKernel if that is what you mean."
    exit 2
}
Write-Host "compiler: $($Kernel.Replace($Repo + [System.IO.Path]::DirectorySeparatorChar, '')) [$digest]"

$ownRoot = -not $OutRoot
if (-not $OutRoot) { $OutRoot = Join-Path ([System.IO.Path]::GetTempPath()) "genscripts-$PID" }
New-Item -ItemType Directory -Force -Path $OutRoot | Out-Null

function Remove-OwnRoot {
    if ($ownRoot -and (Test-Path -PathType Container $OutRoot)) {
        Remove-Item -Recurse -Force $OutRoot -ErrorAction SilentlyContinue
    }
}

$specs = @()
$claimed = @{}
foreach ($g in (Get-ChildItem (Join-Path $Repo 'codex\build') -Filter '*Script.codex' -File)) {
    $text = [System.IO.File]::ReadAllText($g.FullName)
    $m = [regex]::Match($text, 'sh-script\s+"([^"]+)"')
    if (-not $m.Success) { $m = [regex]::Match($text, 'pl-name\s*=\s*"([^"]+)"') }
    if (-not $m.Success) {
        foreach ($c in [regex]::Matches($text, '(?m)^\s*cites\s+Build\s+chapter\s+(\S+)')) {
            $sib = Join-Path $g.DirectoryName ($c.Groups[1].Value + '.codex')
            if (-not (Test-Path -PathType Leaf $sib)) {
                $sib = (Get-ChildItem $g.DirectoryName -Filter '*.codex' -File |
                    Where-Object { [regex]::IsMatch([System.IO.File]::ReadAllText($_.FullName), '(?m)^Chapter:\s+' + [regex]::Escape($c.Groups[1].Value) + '\s*$') } |
                    Select-Object -First 1 -ExpandProperty FullName)
            }
            if ($sib -and (Test-Path -PathType Leaf $sib)) {
                $sibText = [System.IO.File]::ReadAllText($sib)
                $m = [regex]::Match($sibText, 'pl-name\s*=\s*"([^"]+)"')
                if ($m.Success) { break }
            }
        }
    }
    if (-not $m.Success) { continue }
    $name = $m.Groups[1].Value
    $ext = if ($text -match 'emit-bash') { 'sh' } else { 'ps1' }
    $target = "build\$name.$ext"
    $catalogTargets = @($toolCatalog.tools | Where-Object { $_.source -eq "codex/build/$($g.Name)" -and $_.path.EndsWith(".$ext") })
    if ($ext -eq 'ps1' -and $catalogTargets.Count -ne 1) { throw "Catalog must identify exactly one target for $($g.Name)" }
    if ($catalogTargets.Count -eq 1) { $target = $catalogTargets[0].path.Replace('/', '\') }
    $specs += [pscustomobject]@{
        Generator = $g
        Emits     = $name
        Target    = $target
        Present   = (Test-Path -PathType Leaf (Join-Path $Repo $target))
    }
    $claimed[$target.ToLower()] = $g.Name
}

$wanted = if ($Diff) { $Diff } else { $Only }
if ($wanted) {
    $specs = @($specs | Where-Object { $_.Emits -eq $wanted })
    if ($specs.Count -eq 0) { Write-Host "no generator emits '$wanted'"; Remove-OwnRoot; exit 2 }
}

if ($specs.Count -eq 0) { Write-Host "nothing to check"; Remove-OwnRoot; exit 0 }

$listFile = Join-Path $OutRoot 'generators.txt'
$specs.Generator.FullName | Set-Content -Path $listFile -Encoding UTF8
& (Join-Path $PSScriptRoot 'test-compile-batch.ps1') -ListFile $listFile -OutRoot $OutRoot -Kernel $Kernel *> $null

$rows = @()
$parseErrs = @{}
$bareHits = @{}
foreach ($s in $specs) {
    $stem = [System.IO.Path]::GetFileNameWithoutExtension($s.Generator.Name)
    $dir = Join-Path $OutRoot $stem
    $code = Join-Path $dir '.exitcode'
    if (-not (Test-Path -PathType Leaf $code) -or (Get-Content $code -Raw).Trim() -ne '0') {
        $rows += [pscustomobject]@{ Emits = $s.Emits; Status = 'COMPILE FAILED'; Lines = 0; Drift = 0 }
        continue
    }
    $cdx = Get-ChildItem $dir -Filter '*.cdx' | Select-Object -First 1
    $emitted = Join-Path $dir 'emitted.txt'

    $runExit = 0
    $firstExit = 0
    for ($attempt = 1; $attempt -le 2; $attempt++) {
        & (Join-Path $PSScriptRoot 'test-run.ps1') -Kernel $cdx.FullName -OutFile $emitted *> $null
        $runExit = $LASTEXITCODE
        if ($attempt -eq 1) { $firstExit = $runExit }
        if ((Test-Path -PathType Leaf $emitted) -and (Get-Item $emitted).Length -gt 0) {
            if ($attempt -gt 1) { Write-Host "  note: $($s.Emits) emitted nothing on attempt 1 (exit $firstExit), succeeded on retry" }
            break
        }
    }
    if (-not (Test-Path -PathType Leaf $emitted) -or (Get-Item $emitted).Length -eq 0) {
        $rows += [pscustomobject]@{ Emits = $s.Emits; Status = "EMITTED NOTHING (x2, exit $runExit)"; Lines = 0; Drift = 0 }
        continue
    }

    $madeText = [System.IO.File]::ReadAllText($emitted)
    $stubs = @([regex]::Matches($madeText, '(?m)^[ \t]*# <unknown-cmd>[ \t]*$|"<unknown-expr>"')).Count
    if ($stubs -gt 0) {
        $rows += [pscustomobject]@{ Emits = $s.Emits; Status = "UNHANDLED NODES ($stubs)"; Lines = 0; Drift = $stubs }
        continue
    }

    if ($s.Target -like '*.ps1') {
        $perr = $null
        [void][System.Management.Automation.Language.Parser]::ParseFile($emitted, [ref]$null, [ref]$perr)
        if ($perr.Count -gt 0) {
            $parseErrs[$s.Emits] = $perr
            $rows += [pscustomobject]@{ Emits = $s.Emits; Status = "PARSE ERRORS ($($perr.Count))"; Lines = 0; Drift = $perr.Count }
            continue
        }
    }

    if ($s.Target -like '*.ps1') {
        $bareInner = 'Join-Path|Split-Path|Resolve-Path|Get-Item|Get-Content|Get-ChildItem|Get-Date|Get-FileHash|New-Object'
        $bareOuter = 'Copy-Item|Move-Item|Rename-Item|New-Item|Remove-Item|Set-Content|Add-Content|Out-File|Test-Path'
        $bare = @()
        $ln = 0
        foreach ($line in [System.IO.File]::ReadAllLines($emitted)) {
            $ln++
            $t = $line.Trim()
            if ($t.StartsWith('#') -or $t -notmatch "\b($bareOuter)\b") { continue }
            foreach ($m in [regex]::Matches($t, "\b($bareInner)\b")) {
                $pre = $t.Substring(0, $m.Index)
                if ($pre -match '[\(\|=&]\s*$') { continue }
                $oh = [regex]::Matches($pre, "\b($bareOuter)\b")
                if ($oh.Count -eq 0) { continue }
                $last = $oh[$oh.Count - 1]
                if ($pre.Substring($last.Index + $last.Length) -match '[{}|;)]') { continue }
                $bare += "line ${ln}: $($m.Value) bare in: $t"
            }
        }
        if ($bare.Count -gt 0) {
            $bareHits[$s.Emits] = $bare
            $rows += [pscustomobject]@{ Emits = $s.Emits; Status = "BARE CMDLET ARG ($($bare.Count))"; Lines = 0; Drift = $bare.Count }
            continue
        }
    }

    if (-not $s.Present) { continue }

    $shipped = [System.IO.File]::ReadAllLines((Join-Path $Repo $s.Target))
    $made = [System.IO.File]::ReadAllLines($emitted)
    $a = @($shipped | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' })
    $b = @($made | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' })
    $delta = @(Compare-Object $a $b).Count

    if ($Diff) {
        Write-Host ""
        Write-Host "$($s.Target)  <=  codex\build\$($s.Generator.Name)"
        Write-Host "  '<=' is in the shipped script only, '=>' is what the generator emits."
        Write-Host ""
        Compare-Object $a $b | Format-Table -AutoSize SideIndicator, InputObject | Out-String -Width 200 | Write-Host
        if ($delta -eq 0) { Remove-OwnRoot }
        exit ($(if ($delta -gt 0) { 1 } else { 0 }))
    }

    $shipTxt = [regex]::Split([System.IO.File]::ReadAllText((Join-Path $Repo $s.Target)), "`r?`n")
    $madeTxt = [regex]::Split([System.IO.File]::ReadAllText($emitted), "`r?`n")
    $shipNB = @($shipTxt | Where-Object { $_.Trim() -ne '' })
    $madeNB = @($madeTxt | Where-Object { $_.Trim() -ne '' })
    $indentDiffs = 0
    for ($k = 0; $k -lt [Math]::Min($shipNB.Count, $madeNB.Count); $k++) {
        if ($shipNB[$k] -ne $madeNB[$k]) { $indentDiffs++ }
    }
    $byteSame = ($shipTxt.Count -eq $madeTxt.Count) -and ($indentDiffs -eq 0)
    if ($byteSame) {
        for ($k = 0; $k -lt $shipTxt.Count; $k++) {
            if ($shipTxt[$k] -ne $madeTxt[$k]) { $byteSame = $false; break }
        }
    }

    $rows += [pscustomobject]@{
        Emits  = $s.Emits
        Status = if ($delta -eq 0) { 'match' } else { 'DRIFTED' }
        Lines  = $shipped.Count
        Drift  = $delta
        ByteS  = $shipTxt.Count
        ByteE  = $madeTxt.Count
        ByteI  = $indentDiffs
        ByteOk = $byteSame
    }
}

$rows | Sort-Object Drift -Descending | Format-Table -AutoSize | Out-String -Width 200 | Write-Host

$NoTargetByDesign = @{
    'build\test-run.sh' = 'the only bash generator, kept so the unhandled-node scan reaches BashEmit; no .sh has ever shipped'
}

$dead           = @($specs | Where-Object { -not $_.Present })
$deadDeclared   = @($dead | Where-Object { $NoTargetByDesign.ContainsKey($_.Target) })
$deadUndeclared = @($dead | Where-Object { -not $NoTargetByDesign.ContainsKey($_.Target) })
if ($deadDeclared.Count -gt 0) {
    Write-Host "no target, by design ($($deadDeclared.Count)):"
    foreach ($d in $deadDeclared) {
        Write-Host "  $($d.Generator.Name) -> $($d.Target)"
        Write-Host "      $($NoTargetByDesign[$d.Target])"
    }
}
if ($deadUndeclared.Count -gt 0) {
    Write-Host "generators whose target does not exist ($($deadUndeclared.Count)):"
    foreach ($d in $deadUndeclared) { Write-Host "  $($d.Generator.Name) -> $($d.Target)" }
}

if (-not $wanted) {
    $invFile = Join-Path $Repo 'build\handwritten-scripts.txt'
    $onDisk = @(Get-ChildItem (Join-Path $Repo 'build') -Filter '*.ps1' -File |
        Where-Object { -not $claimed.ContainsKey("build\$($_.Name)".ToLower()) } |
        ForEach-Object { $_.Name } | Sort-Object)

    if ($Update) {
        $invHeader = @(
            "# handwritten-scripts.txt -- generated by build/check-generated-scripts.ps1 -Update",
            "#",
            "# Scripts under build/ that no generator in codex/build/ emits. This is an",
            "# INVENTORY, not a debt list: most of these are meant to be hand-written",
            "# (probes, flight arms, interop harnesses, one-offs, and the checker",
            "# itself, which must run when the generators are broken).",
            "#",
            "# The check reports a script that is NOT listed here and never fails on",
            "# one. A new name means somebody added a script; whether it wants a",
            "# generator is a judgement, so answer it either by writing the .codex or",
            "# by recording the name here.",
            ""
        )
        Write-Record $invFile ($invHeader + $onDisk) "inventory ($($onDisk.Count) hand-written script(s))"
    } elseif (Test-Path -PathType Leaf $invFile) {
        $known = @(Get-Content $invFile |
            Where-Object { $_ -notmatch '^\s*#' -and $_ -match '\S' } |
            ForEach-Object { $_.Trim() })
        $fresh = @($onDisk | Where-Object { $known -notcontains $_ })
        $gone  = @($known | Where-Object { $onDisk -notcontains $_ })
        if ($fresh.Count -gt 0) {
            Write-Host ""
            Write-Host "hand-written scripts not in the inventory ($($fresh.Count)) -- report only, not a failure:"
            foreach ($f in $fresh) { Write-Host "  build\$f  (no generator in codex/build/ emits this)" }
            Write-Host "  Write the generator, or record it: build/check-generated-scripts.ps1 -Update"
        }
        if ($gone.Count -gt 0) {
            Write-Host ""
            Write-Host "inventory names $($gone.Count) script(s) that are gone or now generated:"
            foreach ($g2 in $gone) { Write-Host "  $g2 (drop it: -Update)" }
        }
    } else {
        Write-Host ""
        Write-Host "no inventory at $invFile -- run with -Update to record the $($onDisk.Count) hand-written script(s)"
    }
}

$broken     = @($rows | Where-Object { $_.Status -ne 'match' -and $_.Status -ne 'DRIFTED' })
$driftedNow = @($rows | Where-Object { $_.Status -eq 'DRIFTED' } | ForEach-Object { $_.Emits } | Sort-Object)

if ($Update -or $UpdateBytes) {
  if ($Update) {
    $header = @(
        "# generated-scripts-baseline.txt -- generated by build/check-generated-scripts.ps1 -Update",
        "#",
        "# Generators under codex/build/ that no longer emit the script they are",
        "# shipped beside. Measured 2026-08-03 over all 40 with a live target: in",
        "# every case the SHIPPED script was the maintained side and the generator",
        "# was the abandoned one. So this is a list of generators that have fallen",
        "# behind, NOT of scripts that are wrong -- which is why the fix is to port",
        "# the drift back by hand and why there is no -Write flag.",
        "#",
        "# This file is the KNOWN residue. The check fails on a generator that",
        "# drifts and is not listed here. Shrinking it is the work; read a drift",
        "# with -Diff <name>. Growing it needs a reason in the CL description.",
        "#",
        "# A compile failure, an emitter that produces nothing, an unhandled node",
        "# stub, and emitted text that PowerShell cannot parse are NOT recordable",
        "# here: those fail whatever this file says.",
        ""
    )
    Write-Host ""
    Write-Record $BaseFile ($header + $driftedNow) "baseline ($($driftedNow.Count) known drift(s))"
  }

    if (-not $wanted) {
        $notByte = @($rows | Where-Object { -not $_.ByteOk } | Sort-Object Emits)
        $bHeader = @(
            "# generated-scripts-bytes.txt -- generated by build/check-generated-scripts.ps1 -Update",
            "#",
            "# Generators that are STATEMENT-identical to the script they ship beside and",
            "# NOT byte-identical to it, as",
            "#   <generator> <shipped-lines> <emitted-lines> <indent-diff-lines>  # cause",
            "#",
            "# The check above trims every line and drops the empty ones, so it decides",
            "# statements; these are the differences it cannot see. NONE of them is a",
            "# content difference: every row here is blank lines, indentation, or both,",
            "# measured 2026-09-08 when the arm was written.",
            "#",
            "# The check FAILS on a generator that is not byte-identical and not listed",
            "# here, on a listed generator whose numbers move, and on a listed generator",
            "# that has become byte-identical -- that last one so the record shrinks as",
            "# each is repaired rather than granting permission for the difference to",
            "# come back. Lower it in the changelist that did the repair: -Update.",
            ""
        )
        $bRows = foreach ($r in $notByte) {
            '{0} {1} {2} {3}  # {4}' -f $r.Emits, $r.ByteS, $r.ByteE, $r.ByteI, (Get-ByteCause $r)
        }
        Write-Record $BytesFile ($bHeader + @($bRows)) "byte residue ($($notByte.Count) not byte-identical)"
    }

    Remove-OwnRoot
    exit 0
}

Write-Host ""
Write-Host "Checked $($rows.Count) generators, $($driftedNow.Count) drifted, $($broken.Count) broken, $($dead.Count) with no target."

if ($deadUndeclared.Count -gt 0) {
    Write-Host ""
    Write-Host "check-generated-scripts: FAIL -- $($deadUndeclared.Count) generator(s) emit a script that does not exist:"
    foreach ($d in $deadUndeclared) { Write-Host "  $($d.Generator.Name) -> $($d.Target)" }
    Write-Host "  A generator with no target is compared against nothing, so it reports"
    Write-Host "  neither match nor drift and its output is unchecked by anything."
    Write-Host "  Either the target moved, in which case update build/tool-catalog.json,"
    Write-Host "  or it is gone and the generator should be deleted, or it is absent on"
    Write-Host "  purpose, in which case declare it in `$NoTargetByDesign with the reason."
    Remove-OwnRoot
    exit 1
}

if ($broken.Count -gt 0) {
    Write-Host ""
    Write-Host "check-generated-scripts: FAIL -- $($broken.Count) generator(s) broken, not merely behind:"
    foreach ($b in $broken) { Write-Host "  $($b.Emits): $($b.Status)" }
    if (@($broken | Where-Object { $_.Status -like 'UNHANDLED NODES*' }).Count -gt 0) {
        Write-Host "  An emitter answers '# <unknown-cmd>' for a node it does not handle rather"
        Write-Host "  than failing, so the script it writes is silently wrong, not absent."
        Write-Host "  Add the arm in BashEmit / KshEmit / the PowerShell emitter."
    }
    if ($bareHits.Count -gt 0) {
        Write-Host "  A value-producing cmdlet emitted as a BARE positional argument. This"
        Write-Host "  PARSES, so the parse check above cannot see it, and it fails when the"
        Write-Host "  script is RUN. Parenthesise it at the CALL SITE in the generator --"
        Write-Host "  SeRaw is passed through verbatim by design and fixing the emitter"
        Write-Host "  would re-drift every generator that currently matches."
        foreach ($k in ($bareHits.Keys | Sort-Object)) {
            Write-Host "  ${k}:"
            foreach ($e in $bareHits[$k]) { Write-Host "    $e" }
        }
    }
    if ($parseErrs.Count -gt 0) {
        Write-Host "  Emitted text that PowerShell cannot parse is a script that cannot run"
        Write-Host "  at all. An error count is not a defect count: 59 of these reduced to"
        Write-Host "  four causes and eleven sites, so read the FIRST error of each and"
        Write-Host "  expect the rest to be cascade."
        foreach ($k in ($parseErrs.Keys | Sort-Object)) {
            Write-Host "  $k :"
            foreach ($e in ($parseErrs[$k] | Select-Object -First 3)) {
                Write-Host "    line $($e.Extent.StartLineNumber): $($e.Message)"
            }
        }
    }
    exit 1
}

if (-not (Test-Path -PathType Leaf $BaseFile)) {
    Write-Host "check-generated-scripts: no baseline at $BaseFile -- run with -Update"
    exit 1
}

$baseline = @(Get-Content $BaseFile |
    Where-Object { $_ -notmatch '^\s*#' -and $_ -match '\S' } |
    ForEach-Object { $_.Trim() })

$checked  = @($rows | ForEach-Object { $_.Emits })
$newDrift = @($driftedNow | Where-Object { $baseline -notcontains $_ })
$fixed    = @($baseline | Where-Object { $checked -contains $_ -and $driftedNow -notcontains $_ })

if ($newDrift.Count -gt 0) {
    Write-Host ""
    Write-Host "check-generated-scripts: FAIL -- $($newDrift.Count) generator(s) newly drifted:"
    foreach ($n in $newDrift) { Write-Host "  $n" }
    Write-Host "  Read it:   build/check-generated-scripts.ps1 -Diff $($newDrift[0])"
    Write-Host "  Port the change back into codex/build/, or record it:"
    Write-Host "             build/check-generated-scripts.ps1 -Update"
    exit 1
}

$residue = Read-BytesResidue $BytesFile
$byteNew = New-Object System.Collections.Generic.List[string]
$byteMoved = New-Object System.Collections.Generic.List[string]
$byteFixed = New-Object System.Collections.Generic.List[string]
foreach ($r in $rows) {
    $known = $residue.ContainsKey($r.Emits)
    if ($r.ByteOk) {
        if ($known) { $byteFixed.Add("  $($r.Emits) is byte-identical now") }
        continue
    }
    if (-not $known) {
        $byteNew.Add(("  {0}: shipped {1} lines, emitted {2}, {3} indented differently -- {4}" -f $r.Emits, $r.ByteS, $r.ByteE, $r.ByteI, (Get-ByteCause $r)))
        continue
    }
    $was = $residue[$r.Emits]
    if ($was.S -ne $r.ByteS -or $was.E -ne $r.ByteE -or $was.I -ne $r.ByteI) {
        $byteMoved.Add(("  {0}: recorded {1}/{2}/{3}, now {4}/{5}/{6} -- {7}" -f $r.Emits, $was.S, $was.E, $was.I, $r.ByteS, $r.ByteE, $r.ByteI, (Get-ByteCause $r)))
    }
}

if ($byteNew.Count -gt 0 -or $byteMoved.Count -gt 0 -or $byteFixed.Count -gt 0) {
    Write-Host ""
    Write-Host "check-generated-scripts: FAIL -- the byte arm."
    if ($byteNew.Count -gt 0) {
        Write-Host "$($byteNew.Count) generator(s) are statement-identical and NOT byte-identical, and are not in the record:"
        $byteNew | ForEach-Object { Write-Host $_ }
    }
    if ($byteMoved.Count -gt 0) {
        Write-Host "$($byteMoved.Count) recorded generator(s) changed their difference:"
        $byteMoved | ForEach-Object { Write-Host $_ }
    }
    if ($byteFixed.Count -gt 0) {
        Write-Host "$($byteFixed.Count) recorded generator(s) are repaired and the record still lists them:"
        $byteFixed | ForEach-Object { Write-Host $_ }
        Write-Host "  Lower it in the same changelist: build/check-generated-scripts.ps1 -Update"
    }
    Write-Host "  The statement check trims lines and drops blanks, so it cannot see any of this."
    Write-Host "  Record: build\generated-scripts-bytes.txt"
    exit 1
}

if ($fixed.Count -gt 0) {
    Write-Host "check-generated-scripts: OK -- and $($fixed.Count) baselined generator(s) match again:"
    $fixed | ForEach-Object { Write-Host "  $_ (drop it from the baseline)" }
    Remove-OwnRoot
    exit 0
}

Write-Host "check-generated-scripts: OK ($($driftedNow.Count) known drift(s)), byte arm level with its record"
Remove-OwnRoot
exit 0
