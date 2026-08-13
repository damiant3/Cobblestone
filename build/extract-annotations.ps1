# extract-annotations.ps1 -- Cut column-2 prose into blocks, name each block target, propose a verdict, write the audit report
# GENERATED FROM THE CODEX SHELL DSL. Do not edit by hand.
# A hand edit here must NOT be submitted. Change the generator under
# codex/build/, regenerate, and submit the generator and this file
# together. Until then build/check-generated-scripts.ps1 reports this
# file as drifted, and the next regeneration discards the edit.
[CmdletBinding()]
param(
    [string]$Src,
    [switch]$All,
    [string]$Out
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

$ProsePat = '^ [^ ]'
$DefPat = '^\s{2}(\S+)\s*[:=(|]'
$TypePat = '^\s{2}([A-Z][A-Za-z0-9]*)\s*[=(]'
$SectionPat = '^Section:\s*(.+?)\s*$'
$ChapterPat = '^Chapter:\s*(.+?)\s*$'
$SayWePat = '^ We say:\s*$'
$SpecPat = '(?i)(datasheet|data sheet|spec[a-z]*|section [0-9]|rfc ?[0-9]|ieee|802\.|uefi|xhci|ehci|acpi|smbios|dwarf|\belf\b|pe32|jpeg|utf-?8|unicode|protocol|the standard|per the|requires that|must be set|must be zero|reserved|register|wire format|byte order|endian|errata|vendor|opcode)'
$MagicPat = '(#[0-9A-Fa-f]{2,}|0x[0-9A-Fa-f]+|\b[0-9]{3,}\b)'
$PerfPat = '(?i)(constant.time|work factor|attacker|timing attack|crackab|side.channel|throughput|hot path|cache line|(?<![a-z])heap|allocation|fuel cap|blow.?up|quadratic|amortis|amortiz)'
$HazardPat = '(?i)(do not|does not|never |must not|cannot |silently|beware|hazard|footgun|breaks |trap\b|careful|wrong |fails |refuses)'
$InvarPat = '(?i)(invariant|always |guarantee|must hold|holds |precondition|postcondition|assumes )'
$MeasPat = '(?i)(measured|20[0-9][0-9]-[0-9][0-9]-[0-9][0-9]|\bCL [0-9]+)'
$DocRefPat = '(docs/|BACKLOG|CLAUDE\.md|\.md\b)'

if ((-not $Out)) {
    $Out = (Join-Path $Repo 'docs\Reports\AnnotationExtract.txt')
} else {
    $Out = ([System.IO.Path]::GetFullPath($Out))
}
$report = ([System.Text.StringBuilder]::new())
$script:blockCount = 0
$script:keepCount = 0
$script:annotateCount = 0
$script:proseLines = 0
$script:sayWeCount = 0
$script:filesWithProse = 0
$script:filesScanned = 0


function Classify-Block([string]$Text) {
    $sig = ([System.Collections.Generic.List[string]]::new())
    if (($Text -match $SpecPat)) {
        [void]$sig.Add('spec')
    }
    if (($Text -match $MagicPat)) {
        [void]$sig.Add('magic')
    }
    if (($Text -match $PerfPat)) {
        [void]$sig.Add('perf')
    }
    if (($Text -match $HazardPat)) {
        [void]$sig.Add('hazard')
    }
    if (($Text -match $InvarPat)) {
        [void]$sig.Add('invariant')
    }
    if (($Text -match $MeasPat)) {
        [void]$sig.Add('measured')
    }
    if (($Text -match $DocRefPat)) {
        [void]$sig.Add('docref')
    }
    $verdict = $(if (($sig -contains 'spec')) { 'KEEP' } else { $(if (($sig -contains 'perf')) { 'KEEP' } else { $(if (($sig -contains 'magic')) { 'KEEP' } else { $(if (($sig -contains 'hazard')) { 'ANNOTATE warning' } else { $(if (($sig -contains 'invariant')) { 'ANNOTATE invariant' } else { $(if (($sig -contains 'measured')) { 'ANNOTATE discovery' } else { 'ANNOTATE rationale' }) }) }) }) }) })
    $sigs = $(if ((@($sig).Count -eq 0)) { 'none' } else { ($sig -join ',') })
    return ([string]([string]$verdict + '|') + $sigs)
}


function Scan-File([string]$Path) {
    $lines = ([System.IO.File]::ReadAllLines($Path))
    $n = @($lines).Count
    $rel = $Path
    if ($Path.StartsWith($Repo)) {
        $rel = $Path.Substring(($Repo.Length + 1))
    }
    $chapter = ''
    $seenSection = $false
    $i = 0
    $found = 0
    while (($i -lt $n)) {
        $line = $lines[$i]
        if (($line -match $ChapterPat)) {
            $chapter = $matches[1]
            $i++
            continue
        }
        if (($line -match $SectionPat)) {
            $seenSection = $true
            $i++
            continue
        }
        if (($line -match $ProsePat)) {
            $blockStart = $i
            $buf = ([System.Collections.Generic.List[string]]::new())
            while ((($i -lt $n) -and ($lines[$i] -match $ProsePat))) {
                [void]$buf.Add($lines[$i])
                $i++
            }
            $blockEnd = $i
            if (((@($buf).Count -eq 1) -and ($buf[0] -match $SayWePat))) {
                $script:sayWeCount++
            } else {
                $cls = (Classify-Block ($buf -join ' '))
                $parts = ($cls -split '\|')
                $verdict = $parts[0]
                $sigs = $parts[1]
                $target = ([string]'chapter:' + $chapter)
                $j = $blockEnd
                while ((($j -lt $n) -and ($lines[$j].Trim() -eq ''))) {
                    $j++
                }
                if (($j -lt $n)) {
                    $nl = $lines[$j]
                    if (($nl -match $SectionPat)) {
                        $target = ([string]'section:' + $matches[1])
                    } else {
                        if (($nl -match $TypePat)) {
                            $target = ([string]'type:' + $matches[1])
                        } else {
                            if (($nl -match $DefPat)) {
                                $target = ([string]'function:' + $matches[1])
                            }
                        }
                    }
                }
                $k = ($blockStart - 1)
                while ((($k -ge 0) -and ($lines[$k].Trim() -eq ''))) {
                    $k--
                }
                if (($k -ge 0)) {
                    if (($lines[$k] -match $SectionPat)) {
                        $target = ([string]'section:' + $matches[1])
                    }
                }
                if ((-not $seenSection)) {
                    $target = ([string]'chapter:' + $chapter)
                }

                [void]$report.AppendLine(([string]([string]([string]([string]([string]([string]([string]([string]([string]([string]'@@ ' + $rel) + ' | ') + ($blockStart + 1)) + '-') + $blockEnd) + ' | ') + $target) + ' | ') + $verdict) + ([string]' | ' + $sigs)))
                foreach ($bl in $buf) {
                    [void]$report.AppendLine(([string]'> ' + $bl))
                }
                [void]$report.AppendLine('')
                $script:blockCount++
                $found++
                $script:proseLines += @($buf).Count
                if (($verdict -eq 'KEEP')) {
                    $script:keepCount++
                } else {
                    $script:annotateCount++
                }
            }
            continue
        }
        $i++
    }
    $script:filesScanned++
    if (($found -gt 0)) {
        $script:filesWithProse++
    }
}


if ($Src) {
    [void](Scan-File (Resolve-Path $Src).Path)
}
if (((-not $Src) -and $All)) {
    $files = @()
    foreach ($dir in @('codex', 'apps')) {
        $fullDir = (Join-Path $Repo $dir)
        if ((Test-Path -PathType Container $fullDir)) {
            $files += Get-ChildItem $fullDir -Recurse -Filter '*.codex' -File
        }
    }
    foreach ($f in $files) {
        [void](Scan-File $f.FullName)
    }
}

New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null
Set-Content -Path $Out -Value (@((@('# Annotation extraction report -- phase 1 of 2. READ ONLY; nothing was mutated.', '#', '# Edit the VERDICT field on each @@ header, then run build/apply-annotations.ps1.', '#   KEEP              leave the prose in the source', '#   ANNOTATE <kind>   move it to annotations/<path>.json and cut it from the source', '#   DELETE            cut it from the source and keep nothing', '#', '# This tool NEVER proposes DELETE. Whether a block explains our own code is', '# a judgement, and a keyword classifier answering it would be an instrument', '# that cannot fail. DELETE is a word only the auditor writes.', '#', '# kinds: invariant rationale warning discussion discovery todo doctrine', '# Body lines below each header are VERBATIM source. apply-annotations refuses', '# any block whose source no longer matches, so a stale report is a refusal', '# rather than a corruption. Do not reflow them.', '') -join "`n"), ([string]([string]([string]([string]([string]([string]'# scanned ' + $script:filesScanned) + ' chapters, ') + $script:filesWithProse) + ' with prose; ') + $script:blockCount) + ([string]([string]' blocks, ' + $script:proseLines) + ' prose lines')), ([string]([string]([string]([string]([string]'# proposed ' + $script:keepCount) + ' KEEP, ') + $script:annotateCount) + ' ANNOTATE, 0 DELETE; ') + ([string]$script:sayWeCount + ' We say: markers excluded')), '', $report.ToString()) -join "`n") -Encoding UTF8
Write-Host ([string]'report: ' + $Out)
Write-Host ([string]([string]([string]([string]([string]([string]'# scanned ' + $script:filesScanned) + ' chapters, ') + $script:filesWithProse) + ' with prose; ') + $script:blockCount) + ([string]([string]' blocks, ' + $script:proseLines) + ' prose lines'))
Write-Host ([string]([string]([string]([string]([string]'# proposed ' + $script:keepCount) + ' KEEP, ') + $script:annotateCount) + ' ANNOTATE, 0 DELETE; ') + ([string]$script:sayWeCount + ' We say: markers excluded'))
