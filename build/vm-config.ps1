# vm-config.ps1 -- Shared VM config + helpers for the harness
# GENERATED FROM THE CODEX SHELL DSL. Do not edit by hand.
# A hand edit here must NOT be submitted. Change the generator under
# codex/build/, regenerate, and submit the generator and this file
# together. Until then build/check-generated-scripts.ps1 reports this
# file as drifted, and the next regeneration discards the edit.
[CmdletBinding()]
param(
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Locate a VM host. codex-vm (the WHP hypervisor) is the primary and is
# Windows-only; QEMU is the fallback and the only host on Linux/WSL. Both
# paths are live: a missing codex-vm is not an error, and the hard failure
# is reserved for having NEITHER host. CODEX_VM_HOST=qemu forces the
# fallback on a box that HAS codex-vm, which is the only way to exercise
# the QEMU path from Windows without hiding the binary.
$script:CodexVmBin = Join-Path (Split-Path $PSScriptRoot) 'tools' 'codex-vm.exe'
$script:UseCodexVm = (Test-Path -PathType Leaf $script:CodexVmBin) -and ($env:CODEX_VM_HOST -ne 'qemu')
$script:FallbackVmBin = $env:QEMU_BIN
if ((-not $script:FallbackVmBin)) { $script:FallbackVmBin = $env:QEMU_BIN_WHPX }
if ((-not $script:FallbackVmBin)) {
    if ($IsWindows) {
        # A side-loaded versioned install wins over the in-place one, newest
        # first, so a new QEMU can be trialled without disturbing what the
        # rest of the machine uses: D:\qemu-11.1.0 beats D:\Program Files\qemu.
        # A directory whose suffix is not a version sorts last rather than
        # throwing.
        $sideload = @()
        foreach ($qroot in @('D:\', 'C:\')) {
            foreach ($d in (Get-ChildItem -Path $qroot -Directory -Filter 'qemu-*' -ErrorAction SilentlyContinue)) {
                $exe = Join-Path $d.FullName 'qemu-system-x86_64.exe'
                if (Test-Path -PathType Leaf $exe) {
                    $v = [version]'0.0'
                    if (-not [version]::TryParse(($d.Name -replace '^qemu-', ''), [ref]$v)) { $v = [version]'0.0' }
                    $sideload += [pscustomobject]@{ Exe = $exe; Ver = $v }
                }
            }
        }
        foreach ($s in ($sideload | Sort-Object Ver -Descending)) { $script:FallbackVmBin = $s.Exe; break }
        if ((-not $script:FallbackVmBin)) {
            foreach ($p in @('D:\Program Files\qemu\qemu-system-x86_64.exe', 'C:\Program Files\qemu\qemu-system-x86_64.exe')) {
                if (Test-Path -PathType Leaf $p) {
                    $script:FallbackVmBin = $p; break
                }
            }
        }
    } else {
        $qemuCmd = Get-Command qemu-system-x86_64 -ErrorAction SilentlyContinue
        if ($qemuCmd) { $script:FallbackVmBin = $qemuCmd.Source }
    }
}
if ((-not $script:UseCodexVm) -and (-not $script:FallbackVmBin)) {
    [Console]::Error.WriteLine("no VM host: codex-vm not at $($script:CodexVmBin) (build with tools/build-vm.ps1) and no qemu-system-x86_64 (install qemu or set QEMU_BIN).")
    throw "no VM host: codex-vm not at $($script:CodexVmBin) and no qemu-system-x86_64."
}
# The fallback accelerator: whpx on Windows, tcg on Linux, and CODEX_ACCEL
# overrides either. kvm is the opt-in rather than the Linux default because
# measured 2026-08-12 under WSL2 the nested-virt vmexit tax on byte-wise
# serial I/O makes it slower than plain TCG for small compiles (18-63s
# against ~11s). The name is kept, not just the flags: Start-VmRun's
# platform arguments depend on WHICH accelerator this is.
$script:FallbackAccel = if ($env:CODEX_ACCEL) { $env:CODEX_ACCEL } elseif ($IsWindows) { 'whpx' } else { 'tcg' }
$script:FallbackAccelFlags = @('-accel', $script:FallbackAccel)


# CCE encode/decode tables -- shared by plug run scripts.
#
# Ported from codex/foreword/core/CCE.codex, which is the authority. Tier 0 is
# 128 code points ordered by frequency; tier 1 is 11 blocks covering CCE 128..2175;
# tier 2 is 10 blocks from CCE 2176, each block's CCE base being the running sum of
# the sizes before it. A tier-0-only decoder does not fail on the rest -- it answers
# '?', which reads as a corrupt symbol rather than an unreadable one.
$script:CceToUnicode = @(
    0, 10, 32,
    48, 49, 50, 51, 52, 53, 54, 55, 56, 57,
    101, 116, 97, 111, 105, 110, 115, 104, 114, 100,
    108, 99, 117, 109, 119, 102, 103, 121, 112, 98,
    118, 107, 106, 120, 113, 122,
    69, 84, 65, 79, 73, 78, 83, 72, 82, 68,
    76, 67, 85, 77, 87, 70, 71, 89, 80, 66,
    86, 75, 74, 88, 81, 90,
    46, 44, 33, 63, 58, 59, 39, 34, 45, 40, 41,
    43, 61, 42, 60, 62,
    47, 64, 35, 38, 95, 92, 124, 91, 93, 123, 125, 126, 96,
    94,
    36, 37,
    233, 232, 234, 235, 225, 224, 226, 228,
    243, 244, 246, 250, 252, 241, 231, 237,
    1072, 1086, 1077, 1080, 1085, 1090, 1089, 1088,
    1074, 1083, 1082, 1084, 1076, 1087, 1091
)
# (unicode-base, size) pairs. Tier 1 CCE ranges are implied by the sizes and are
# recomputed by Get-CceTier1Block rather than stored, exactly as CCE.codex does.
$script:CceTier1Bases = @(128,256, 1024,128, 880,128, 1536,128, 1424,128, 2304,128, 3584,128, 4352,128, 19968,512, 12352,256, 8704,128)
$script:CceTier2Table = @(12288,64, 12352,96, 12448,96, 19968,20992, 13312,6592, 44032,11172, 3584,256, 8192,512, 127744,1024, 9728,256)
$script:CceTier2Base = 2176

$script:UnicodeToCce = [byte[]]::new(256)
for ($i = 0; $i -lt 256; $i++) {
    $script:UnicodeToCce[$i] = 68
}
# The `-lt 256` guard is load-bearing. Tier 0's last 15 entries are Cyrillic
# (1072..1091); the fold this loop used to do (`% 256`) lands 1072 on 48 and
# silently remaps the digit '0' to a Cyrillic code point. This table is the
# single-byte fast path only -- Convert-UnicodeToCce is the general answer.
for ($i = 0; $i -lt $script:CceToUnicode.Length; $i++) {
    $u = $script:CceToUnicode[$i]
    if ($u -lt 256) { $script:UnicodeToCce[$u] = [byte]$i }
}


function Get-CceTier1Block([int]$cp) {
    $v = $cp - 128
    if ($v -lt 0) { return -1 }
    if ($v -lt 256) { return 0 }
    if ($v -lt 1152) { return 1 + [int][math]::Floor(($v - 256) / 128) }
    if ($v -lt 1664) { return 8 }
    if ($v -lt 1920) { return 9 }
    if ($v -lt 2048) { return 10 }
    return -1
}

function Get-CceTier1Offset([int]$cp) {
    $v = $cp - 128
    if ($v -lt 256) { return $v }
    if ($v -lt 1152) { return ($v - 256) % 128 }
    if ($v -lt 1664) { return $v - 1152 }
    if ($v -lt 1920) { return $v - 1664 }
    return $v - 1920
}

function Get-CceTier2CceBase([int]$block) {
    $acc = $script:CceTier2Base
    for ($i = 0; $i -lt $block; $i++) { $acc += $script:CceTier2Table[$i * 2 + 1] }
    return $acc
}

# CCE code point -> Unicode. 65533 (U+FFFD) means unmapped, matching to-unicode.
function Convert-CceToUnicode([int]$cp) {
    if ($cp -lt 0) { return 65533 }
    if ($cp -lt 128) { return [int]$script:CceToUnicode[$cp] }
    if ($cp -lt 2176) {
        $b = Get-CceTier1Block $cp
        if ($b -lt 0) { return 65533 }
        return [int]$script:CceTier1Bases[$b * 2] + (Get-CceTier1Offset $cp)
    }
    if ($cp -lt 67712) {
        for ($b = 0; $b -lt 10; $b++) {
            $bb = Get-CceTier2CceBase $b
            $bs = [int]$script:CceTier2Table[$b * 2 + 1]
            if ($cp -ge $bb -and $cp -lt ($bb + $bs)) { return [int]$script:CceTier2Table[$b * 2] + ($cp - $bb) }
        }
        return 65533
    }
    return 65533
}

# Unicode -> CCE code point, -1 when unmapped. Tier order is load-bearing and not
# an optimisation: U+4E00 and U+3040 sit in a tier 1 block AND a tier 2 block, and
# from-unicode answers with the tier 1 code point. Searching tier 2 first would
# produce a different, larger encoding for the same character.
function Convert-UnicodeToCce([int]$u) {
    for ($i = 0; $i -lt $script:CceToUnicode.Length; $i++) {
        if ([int]$script:CceToUnicode[$i] -eq $u) { return $i }
    }
    for ($b = 0; $b -lt 11; $b++) {
        $base = [int]$script:CceTier1Bases[$b * 2]
        $size = [int]$script:CceTier1Bases[$b * 2 + 1]
        if ($u -ge $base -and $u -lt ($base + $size)) {
            $start = if ($b -eq 0) { 128 } elseif ($b -lt 8) { 256 + $b * 128 } elseif ($b -eq 8) { 1280 } elseif ($b -eq 9) { 1792 } else { 2048 }
            return $start + ($u - $base)
        }
    }
    for ($b = 0; $b -lt 10; $b++) {
        $base = [int]$script:CceTier2Table[$b * 2]
        $size = [int]$script:CceTier2Table[$b * 2 + 1]
        if ($u -ge $base -and $u -lt ($base + $size)) { return (Get-CceTier2CceBase $b) + ($u - $base) }
    }
    return -1
}

function Get-CceEncodeLength([int]$cp) {
    if ($cp -lt 128) { return 1 }
    if ($cp -lt 2176) { return 2 }
    if ($cp -lt 67712) { return 3 }
    return 4
}


# Multi-byte framing is UTF-8 SHAPED but is not UTF-8: the code point is biased by
# the tier base, so the same bytes decode to a different number. Treating a CCE
# stream as UTF-8 is wrong for every code point above 127.
function ConvertTo-CceBytes([string]$Text) {
    $out = [System.Collections.Generic.List[byte]]::new()
    for ($i = 0; $i -lt $Text.Length; $i++) {
        $u = [int]$Text[$i]
        if ([char]::IsHighSurrogate($Text[$i]) -and ($i + 1) -lt $Text.Length -and [char]::IsLowSurrogate($Text[$i + 1])) {
            $u = [char]::ConvertToUtf32($Text[$i], $Text[$i + 1])
            $i++
        }
        $cp = Convert-UnicodeToCce $u
        if ($cp -lt 0) { throw ("cce: U+{0:X4} has no CCE code point" -f $u) }
        if ($cp -lt 128) {
            $out.Add([byte]$cp)
        } elseif ($cp -lt 2176) {
            $v = $cp - 128
            $out.Add([byte](192 -bor ($v -shr 6))); $out.Add([byte](128 -bor ($v -band 63)))
        } elseif ($cp -lt 67712) {
            $v = $cp - 2176
            $out.Add([byte](224 -bor ($v -shr 12))); $out.Add([byte](128 -bor (($v -shr 6) -band 63))); $out.Add([byte](128 -bor ($v -band 63)))
        } else {
            $v = $cp - 67712
            $out.Add([byte](240 -bor ($v -shr 18))); $out.Add([byte](128 -bor (($v -shr 12) -band 63))); $out.Add([byte](128 -bor (($v -shr 6) -band 63))); $out.Add([byte](128 -bor ($v -band 63)))
        }
    }
    return $out.ToArray()
}

# Returns the text AND what could not be read, because a decoder that quietly
# substitutes a replacement character turns "I could not read this" into "this is
# what it said". Callers that report a negative result must check Unmapped.
function ConvertFrom-CceBytesDetailed([byte[]]$Bytes) {
    $sb = [System.Text.StringBuilder]::new()
    $unmapped = 0; $malformed = 0; $truncated = 0; $chars = 0
    $i = 0
    while ($i -lt $Bytes.Length) {
        $b0 = [int]$Bytes[$i]
        $stray = $false
        if (($b0 -band 128) -eq 0) { $len = 1 }
        elseif (($b0 -band 224) -eq 192) { $len = 2 }
        elseif (($b0 -band 240) -eq 224) { $len = 3 }
        elseif (($b0 -band 248) -eq 240) { $len = 4 }
        else { $len = 1; $stray = $true }
        if (($i + $len) -gt $Bytes.Length) { $truncated++; break }
        # A byte in 128..191 where a lead byte belongs is a continuation byte adrift.
        # cce-decode-length reads it as a 1-byte character, which yields a plausible
        # tier 1 letter for what is actually a framing error, so it is counted here
        # rather than answered.
        if ($stray) {
            $malformed++
            [void]$sb.Append([char]65533)
            $chars++
            $i += 1
            continue
        }
        if ($len -eq 1) { $cp = $b0 }
        elseif ($len -eq 2) { $cp = 128 + ((($b0 -band 31) -shl 6) -bor ([int]$Bytes[$i + 1] -band 63)) }
        elseif ($len -eq 3) { $cp = 2176 + ((($b0 -band 15) -shl 12) -bor (([int]$Bytes[$i + 1] -band 63) -shl 6) -bor ([int]$Bytes[$i + 2] -band 63)) }
        else { $cp = 67712 + ((($b0 -band 7) -shl 18) -bor (([int]$Bytes[$i + 1] -band 63) -shl 12) -bor (([int]$Bytes[$i + 2] -band 63) -shl 6) -bor ([int]$Bytes[$i + 3] -band 63)) }
        $u = Convert-CceToUnicode $cp
        if ($u -eq 65533) { $unmapped++ }
        if ($u -gt 65535) { [void]$sb.Append([char]::ConvertFromUtf32($u)) } else { [void]$sb.Append([char]$u) }
        $chars++
        $i += $len
    }
    return @{ Text = $sb.ToString(); Unmapped = $unmapped; Malformed = $malformed; Truncated = $truncated; Chars = $chars }
}

function ConvertFrom-CceBytes([byte[]]$Bytes) {
    return (ConvertFrom-CceBytesDetailed $Bytes).Text
}


# Symbol resolution.
#
# A CDX carries its own symbol map -- a MAP1 block the emitter writes into the
# data segment -- and that is the map these functions read. The text sidecar
# (seed/Codex.map) is a fallback and an explicit override, never the default.
#
# The reason is drift, and it is not hypothetical. A compiler build does emit a
# map (every CDX compile has since main 15088) and the build now carries it to
# build/output/Sut.map, but NOTHING installs it over seed/Codex.map except one
# release-gate step that routine work does not run. Measured 2026-08-15, the
# installed sidecar had 5,140 of its 5,187 rows at the wrong address while every
# name still resolved. So the sidecar silently describes an older binary than the seed beside
# it, and the resolver answered from it with total confidence: on 2026-07-16 it
# placed a crash in `sorted-builtin-names` when the faulting function was
# `find-effect-op-addr`, a different function entirely, and cost an hour. The
# sidecar's mtime was seven hours behind the seed's.
#
# An embedded map cannot drift from the binary it describes, because it ships
# inside it. That is the whole fix: not "remember to refresh the map", but a map
# with no opportunity to be stale.
#
# MAP1 layout (little-endian), as written by the emitter and read by
# apps/works/DevDebugger.codex:
#   +0  magic "MAP1" (le32 826361677)
#   +4  count
#   +8  string-table offset, always 12 + count*12 -- this is the validity check
#   +12 count x 12-byte entries: code-offset, code-size, name-offset
#   strings at (block + string-offset), NUL-terminated, CCE bytes.
# Names are CCE, not ASCII -- decode them with ConvertFrom-CceBytes or every symbol
# comes back as plausible garbage ("III", "UU") rather than as an error. The bytes
# are a CCE STREAM, not one byte per character: map-text-to-cce-bytes copies
# char-code-at over text-length, and both are byte-indexed, so a name holding any
# code point above 127 is written as a 2-4 byte sequence.
# Entry code-offsets are relative to the 1 MB load address.

$script:MapCache = @{}
$script:Map1Cache = @{}

function Get-DefaultKernel { Join-Path (Split-Path $PSScriptRoot) 'seed' 'Codex.cdx' }

# Parse the MAP1 block out of a CDX. Returns $null when the file has none.
function Get-Map1Symbols {
    param([string]$CdxFile)
    if (-not $CdxFile -or -not (Test-Path -PathType Leaf $CdxFile)) { return $null }
    $key = (Resolve-Path $CdxFile).Path
    if ($script:Map1Cache.ContainsKey($key)) { return $script:Map1Cache[$key] }

    $b = [System.IO.File]::ReadAllBytes($key)
    $at = -1
    for ($i = 0; $i -lt $b.Length - 12; $i++) {
        # "MAP1"
        if ($b[$i] -eq 0x4D -and $b[$i+1] -eq 0x41 -and $b[$i+2] -eq 0x50 -and $b[$i+3] -eq 0x31) {
            $c = [BitConverter]::ToUInt32($b, $i + 4)
            $s = [BitConverter]::ToUInt32($b, $i + 8)
            if ($c -gt 0 -and $c -lt 100000 -and $s -eq (12 + $c * 12)) { $at = $i; break }
        }
    }
    if ($at -lt 0) { $script:Map1Cache[$key] = $null; return $null }

    $count   = [BitConverter]::ToUInt32($b, $at + 4)
    $strBase = $at + [BitConverter]::ToUInt32($b, $at + 8)
    $entries = [System.Collections.Generic.List[object]]::new()
    for ($e = 0; $e -lt $count; $e++) {
        $a  = $at + 12 + $e * 12
        $no = [BitConverter]::ToUInt32($b, $a + 8)
        $p  = $strBase + $no
        $nameBytes = [System.Collections.Generic.List[byte]]::new()
        while ($p -lt $b.Length -and $b[$p] -ne 0) {
            $nameBytes.Add($b[$p])
            $p++
        }
        $decoded = ConvertFrom-CceBytes $nameBytes.ToArray()
        $entries.Add(@{
            Addr = [long]0x100000 + [BitConverter]::ToUInt32($b, $a)
            Size = [int][BitConverter]::ToUInt32($b, $a + 4)
            Name = $decoded
        })
    }
    $script:Map1Cache[$key] = $entries
    return $entries
}

function Get-TextMapSymbols {
    param([string]$MapFile)
    if (-not (Test-Path $MapFile)) { return $null }
    if (-not $script:MapCache.ContainsKey($MapFile)) {
        $entries = [System.Collections.Generic.List[object]]::new()
        foreach ($line in [System.IO.File]::ReadAllLines($MapFile)) {
            if ($line -match '^(0x[0-9a-fA-F]+)\s+(\d+)\s+(.+)$') {
                $entries.Add(@{ Addr = [Convert]::ToInt64($matches[1], 16); Size = [int]$matches[2]; Name = $matches[3] })
            }
        }
        $script:MapCache[$MapFile] = $entries
    }
    return $script:MapCache[$MapFile]
}

# -MapFile forces the text sidecar. Otherwise the symbols come from -Kernel's
# embedded MAP1 (default: the seed), and the sidecar is used only if that CDX
# has no MAP1 -- in which case say so, because a stale answer is worse than none.
function Get-Symbols {
    param([string]$MapFile = '', [string]$Kernel = '')
    if ($MapFile) { return Get-TextMapSymbols $MapFile }
    if (-not $Kernel) { $Kernel = Get-DefaultKernel }
    $syms = Get-Map1Symbols $Kernel
    if ($syms) { return $syms }
    $fallback = Join-Path (Split-Path $PSScriptRoot) 'seed' 'Codex.map'
    if (Test-Path $fallback) {
        Write-Warning "no MAP1 in '$Kernel'; falling back to $fallback, which may describe a different binary."
        return Get-TextMapSymbols $fallback
    }
    return $null
}

function Resolve-Rip {
    param([long]$Rip, [string]$MapFile = '', [string]$Kernel = '')
    $syms = Get-Symbols -MapFile $MapFile -Kernel $Kernel
    if (-not $syms) { return $null }
    foreach ($e in $syms) {
        if ($Rip -ge $e.Addr -and $Rip -lt ($e.Addr + $e.Size)) {
            return "$($e.Name)+0x$((($Rip - $e.Addr)).ToString('X'))"
        }
    }
    return $null
}


function Resolve-Name {
    param([string]$Name, [string]$MapFile = '', [string]$Kernel = '')
    $syms = Get-Symbols -MapFile $MapFile -Kernel $Kernel
    if (-not $syms) { return 0 }
    foreach ($e in $syms) {
        if ($e.Name -eq $Name) { return [long]$e.Addr }
    }
    return 0
}


# -Kernel names the CDX that crashed, so its own embedded MAP1 resolves the
# addresses. Without it the symbols come from the seed, which is the right answer
# only when the seed is what was running.
function Format-CrashReport {
    param([string[]]$ExcLines, [string]$Kernel = '')
    $report = [System.Collections.Generic.List[string]]::new()
    if ($ExcLines.Count -eq 0) { return $report }
    $excLine = $ExcLines[0]
    $vec = ''
    if ($excLine -match '!EXC=([0-9a-fA-F]+)') { $vec = $matches[1] }
    $vecInt = [Convert]::ToInt32($vec, 16)
    $vecName = switch ($vecInt) { 0 { 'divide error' } 6 { 'invalid opcode' } 13 { 'general protection' } 14 { 'page fault' } default { "vector $vecInt" } }
    $rip = 0
    if ($excLine -match 'RIP=([0-9a-fA-F]+)') { $rip = [Convert]::ToInt64($matches[1], 16) }
    $ripSym = Resolve-Rip -Rip $rip -Kernel $Kernel
    if (-not $ripSym) { $ripSym = "0x$($rip.ToString('X8'))" }
    # CR2 is the faulting address of a PAGE FAULT and of nothing else. The guest
    # dumps the register on every vector, so on a #GP or a #UD it holds whatever
    # the last page fault left there -- a plausible heap address, pointing at
    # nothing to do with this crash. Printing it unconditionally is presenting
    # stale garbage as evidence, and it sent a 2026-07-16 investigation chasing an
    # address the faulting instruction never touched. Show it only for vector 14.
    $cr2 = ''
    if ($vecInt -eq 14 -and $excLine -match 'CR2=([0-9a-fA-F]+)') {
        $cr2val = [Convert]::ToInt64($matches[1], 16)
        if ($cr2val -ne 0) { $cr2 = ", CR2=0x$($cr2val.ToString('X12'))" }
    }
    if ($vecInt -eq 13) { $cr2 = ', non-canonical or segment-limit; CR2 is not the faulting address here' }
    $header = "CRASH in $ripSym ($vecName$cr2)"
    $report.Add($header)
    $report.Add("  RIP   0x$($rip.ToString('X8').PadLeft(8,'0'))  $ripSym")

    # callR and RBP are stack addresses, not code addresses. callR in particular
    # is the interrupted RSP despite its name, read from offset 64 of the
    # handler's stack. Resolving either to a symbol can only ever be the value
    # landing inside some function's byte range by coincidence, and printing
    # that coincidence is the same failure the CR2 guard above exists to stop.
    foreach ($regName in @('callR','RBP','RBX','R12','R13','R14','R10','RDI','RSI','R15')) {
        if ($excLine -match "$regName=([0-9a-fA-F]+)") {
            $val = [Convert]::ToInt64($matches[1], 16)
            $isStackPtr = $regName -eq 'callR' -or $regName -eq 'RBP'
            $sym = if ($isStackPtr) { $null } else { Resolve-Rip -Rip $val -Kernel $Kernel }
            $hex = "0x$($val.ToString('X8').PadLeft(8,'0'))"
            $extra = ''
            if ($regName -eq 'callR') { $extra = '  (interrupted RSP)' }
            if ($regName -eq 'RBP')   { $extra = '  (frame chain root)' }
            if ($regName -eq 'R10') {
                $heapMB = [math]::Round(($val - 0x600000) / 1048576.0, 1)
                $extra = "  (heap @ $heapMB MB)"
            }
            if ($sym) { $report.Add("  $($regName.PadRight(5)) $hex  $sym$extra") }
            else { $report.Add("  $($regName.PadRight(5)) $hex$extra") }
        }
    }
    # F[] lines are an exact walk of the RBP frame chain, emitted by the guest
    # (emit-exc-frame-walk). Every address is the return slot of a frame reached
    # by following the chain, so unlike the S[] scan below there is nothing to
    # filter: an entry that fails to resolve means the chain broke there, which
    # is a finding rather than noise, and saying so beats dropping it silently.
    $frames = [System.Collections.Generic.List[string]]::new()
    foreach ($sl in $ExcLines) {
        if ($sl -match 'F\[([0-9a-fA-F]+)\]=([0-9a-fA-F]+)') {
            $fidx = [Convert]::ToInt32($matches[1], 16)
            $fval = [Convert]::ToInt64($matches[2], 16)
            $fsym = Resolve-Rip -Rip $fval -Kernel $Kernel
            if (-not $fsym) { $fsym = '<unresolved -- frame chain broke here>' }
            $frames.Add("    #$fidx 0x$($fval.ToString('X8').PadLeft(8,'0'))  $fsym")
        }
    }
    if ($frames.Count -gt 0) {
        $report.Add("  Stack trace (frame chain):")
        foreach ($f in $frames) { $report.Add($f) }
        return $report
    }
    # No frame chain in this dump, so fall back to the old scan. It prints any
    # stack qword that lands inside some function's byte range, which a stale
    # local or a plain integer can do by coincidence. Hence the label.
    for ($i = 1; $i -lt $ExcLines.Count; $i++) {
        $sl = $ExcLines[$i]
        if ($sl -match 'S\[([0-9a-fA-F]+)\]=([0-9a-fA-F]+)') {
            $soff = $matches[1]; $sval = [Convert]::ToInt64($matches[2], 16)
            $ssym = Resolve-Rip -Rip $sval -Kernel $Kernel
            if ($ssym) {
                if ($i -eq 1) { $report.Add("  Stack trace (heuristic):") }
                $report.Add("    S[$soff] 0x$($sval.ToString('X8').PadLeft(8,'0'))  $ssym")
            }
        }
    }
    return $report
}


function Write-SweepLog {
    param([string]$Message)
    $logPath = $env:CODEX_SWEEP_LOG
    if ($logPath) {
        $ts = (Get-Date).ToString('HH:mm:ss.fff')
        Add-Content -Path $logPath -Value "$ts $Message" -Encoding UTF8
    }
}


function Normalize-TripleNewlines {
    param([byte[]]$Bytes)
    $out = [System.Collections.Generic.List[byte]]::new($Bytes.Length)
    $nlRun = 0
    foreach ($b in $Bytes) {
        if ($b -eq 10) {
            $nlRun++
            if ($nlRun -le 2) { $out.Add($b) }
        } else {
            $nlRun = 0
            $out.Add($b)
        }
    }
    return $out.ToArray()
}


# TCP socket helpers (for explorer server and legacy TCP plugs).
# Host discovery ($script:UseCodexVm, $script:FallbackVmBin, accel flags)
# happens once at the top of this file.
$script:AgentSlot = switch -Wildcard ((Split-Path -Leaf (Get-Location).Path)) { '*-nib' { 1 }; default { 0 } }
$script:PerSlot = 3700

function Get-VmPort {
    param([int]$Attempt = 0)
    $offset = ((Get-Random -Min 0 -Max 32768) + $Attempt * 991) % $script:PerSlot
    return 50200 + ($script:AgentSlot * $script:PerSlot + $offset) * 2
}
function Get-VmChardevData { param([int]$Port) "socket,id=ch0,host=127.0.0.1,port=$Port,server=on,wait=on,nodelay=on" }
function Get-VmChardevCtrl { param([int]$Port) "socket,id=ch1,host=127.0.0.1,port=$Port,server=on,wait=on,nodelay=on" }


function Connect-Vm {
    param([int]$DataPort, [int]$CtrlPort, [int]$TimeoutSec = 30)
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        $data = $null; $ctrl = $null
        try {
            $data = [System.Net.Sockets.TcpClient]::new()
            $data.Connect('127.0.0.1', $DataPort)
            $ctrl = [System.Net.Sockets.TcpClient]::new()
            $ctrl.Connect('127.0.0.1', $CtrlPort)
            return @{ Data = $data; Ctrl = $ctrl }
        } catch {
            if ($data) { $data.Dispose() }
            if ($ctrl) { $ctrl.Dispose() }
            Start-Sleep -Milliseconds 500
        }
    }
    return $null
}


function Read-StreamLine {
    param([System.IO.Stream]$Stream, [int]$TimeoutSec = 60)
    $buf = [System.Collections.Generic.List[byte]]::new()
    $one = New-Object byte[] 1
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ($true) {
        $remainMs = [int][math]::Max(100, ($deadline - (Get-Date)).TotalMilliseconds)
        if ($Stream.CanTimeout) { $Stream.ReadTimeout = $remainMs }
        try { $n = $Stream.Read($one, 0, 1) } catch { return $null }
        if ($n -le 0) { return $null }
        if ($one[0] -eq 10) {
            $bytes = $buf.ToArray()
            return [System.Text.Encoding]::UTF8.GetString($bytes).TrimEnd("`r")
        }
        $buf.Add($one[0])
    }
}

function Read-StreamBytes {
    param([System.IO.Stream]$Stream, [int]$Count, [int]$TimeoutSec = 60)
    $buf = New-Object byte[] $Count
    $offset = 0
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ($offset -lt $Count) {
        $remainMs = [int][math]::Max(100, ($deadline - (Get-Date)).TotalMilliseconds)
        if ($Stream.CanTimeout) { $Stream.ReadTimeout = $remainMs }
        try { $n = $Stream.Read($buf, $offset, $Count - $offset) } catch { return $null }
        if ($n -le 0) { return $null }
        $offset += $n
    }
    return $buf
}


function Read-VmReady {
    param($Conn, [int]$TimeoutSec = 60)
    if (-not $Conn -or -not $Conn.Ctrl) { return $false }
    $stream = $Conn.Ctrl.GetStream()
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSec)
    while ([DateTime]::UtcNow -lt $deadline) {
        $remaining = [int]($deadline - [DateTime]::UtcNow).TotalSeconds
        if ($remaining -le 0) { return $false }
        $line = Read-StreamLine -Stream $stream -TimeoutSec $remaining
        if ($line -and $line.StartsWith('READY')) { return $true }
        if (-not $line) { return $false }
    }
    return $false
}


function Stop-VmGraceful {
    param([int]$ProcessId, [int]$TimeoutMs = 5000)
    $evtName = "Global\CodexVmShutdown_$ProcessId"
    $evt = $null
    try { $evt = [System.Threading.EventWaitHandle]::OpenExisting($evtName) } catch {}
    if ($evt) {
        $evt.Set() | Out-Null
        $evt.Dispose()
        $proc = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
        if ($proc -and -not $proc.HasExited) { $proc.WaitForExit($TimeoutMs) | Out-Null }
        $proc = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
        if ($proc -and -not $proc.HasExited) {
            try { Stop-Process -Id $ProcessId -Force -ErrorAction Stop } catch {}
        }
    } else {
        try { Stop-Process -Id $ProcessId -Force -ErrorAction Stop } catch {}
    }
}


function Close-Vm {
    param($Conn, $Process)
    if ($Conn) {
        if ($Conn.Data) { try { $Conn.Data.Dispose() } catch {} }
        if ($Conn.Ctrl) { try { $Conn.Ctrl.Dispose() } catch {} }
    }
    if ($Process -and -not $Process.HasExited) {
        for ($i = 0; $i -lt 50 -and -not $Process.HasExited; $i++) { Start-Sleep -Milliseconds 100 }
        if (-not $Process.HasExited) {
            Stop-VmGraceful -ProcessId $Process.Id
        }
    }
}


function Start-VmRun {
    param(
        [string]$Kernel, [int]$ConnectTimeoutSec = 30,
        [int]$MemMB = 3072, [int]$PCore = 1, [string[]]$ExtraArgs = @()
    )
    if ($script:UseCodexVm) { return Start-CodexVmRun @PSBoundParameters }
    if (-not $script:FallbackVmBin) { Write-Host "No fallback VM"; return $null }
    # QEMU needs two things pre-boot that codex-vm does for the guest, and
    # both track the ACCELERATOR, not the host OS. Measured on Windows
    # 2026-08-13, seed 9B84FA86, qemu 11.0.0, boot to READY on the ctrl serial:
    #
    #   accel  -cpu max  0xfe8 loader   boots
    #   whpx   no        no             no     <- what this path shipped before
    #   whpx   yes       yes            no
    #   whpx   no        yes            YES
    #   tcg    no        no             no
    #   tcg    no        yes            no
    #   tcg    yes       yes            YES
    #
    # The 0xFE8 cell is the guest RAM size ("dynamic RSP", ram-size-addr in
    # Emit/X86_64Boot.codex); without it the boot stub computes RSP from
    # zeroed memory and triple-faults before the first serial byte. Every
    # accelerator needs it. (data-len=4 not 8: QEMU's generic loader asserts
    # data_len < 8, and -m stays under 4 GB.)
    #
    # -cpu max is required under TCG, whose default qemu64 model lacks
    # features the seed uses, and is FATAL under WHPX, which passes the host
    # model through already.
    #
    # kernel-irqchip=off is what WHPX needs. Under KVM the userspace-APIC
    # path it forces is deprecated and the guest dies before READY, while
    # the in-kernel irqchip boots clean -- so drop the flag exactly there.
    $ramBytes = [long]$MemMB * 1048576
    $platformArgs = @('-device', ('loader,addr=0xfe8,data=0x{0:x},data-len=4' -f $ramBytes))
    if ($script:FallbackAccel -notmatch 'whpx') { $platformArgs = @('-cpu', 'max') + $platformArgs }
    $machineArgs = @('-machine', 'kernel-irqchip=off')
    if ($script:FallbackAccel -match 'kvm') { $machineArgs = @() }
    $stdoutFile = [System.IO.Path]::GetTempFileName()
    $stderrFile = [System.IO.Path]::GetTempFileName()
    for ($attempt = 0; $attempt -lt 4; $attempt++) {
        $dataPort = Get-VmPort -Attempt $attempt
        $ctrlPort = $dataPort + 1
        $args = @($script:FallbackAccelFlags) + $machineArgs + $platformArgs + @(
            '-kernel', $Kernel,
            '-chardev', (Get-VmChardevData -Port $dataPort), '-chardev', (Get-VmChardevCtrl -Port $ctrlPort),
            '-serial', 'chardev:ch0', '-serial', 'chardev:ch1',
            '-device', 'isa-debug-exit,iobase=0xf4,iosize=0x04',
            '-netdev', 'user,id=net0', '-device', 'ne2k_isa,netdev=net0,irq=9,iobase=0x300,mac=52:54:00:12:34:56',
            '-display', 'none', '-no-reboot', '-m', "$MemMB"
        )
        if ($ExtraArgs.Count -gt 0) { $args += $ExtraArgs }
        $startArgs = @{
            FilePath = $script:FallbackVmBin; ArgumentList = $args; PassThru = $true
            RedirectStandardOutput = $stdoutFile; RedirectStandardError = $stderrFile
        }
        # -WindowStyle throws on non-Windows editions of pwsh.
        if ($IsWindows) { $startArgs.WindowStyle = 'Hidden' }
        $proc = Start-Process @startArgs
        Start-Sleep -Milliseconds 500
        if ($proc.HasExited) { continue }
        $conn = Connect-Vm -DataPort $dataPort -CtrlPort $ctrlPort -TimeoutSec $ConnectTimeoutSec
        if ($conn) { return @{ Process = $proc; Conn = $conn; StdoutFile = $stdoutFile; StderrFile = $stderrFile } }
        Stop-VmGraceful -ProcessId $proc.Id -TimeoutMs 3000
        Start-Sleep -Milliseconds 500
    }
    Remove-Item -Force $stdoutFile, $stderrFile -ErrorAction SilentlyContinue
    return $null
}


function Start-CodexVmRun {
    param(
        [string]$Kernel, [int]$ConnectTimeoutSec = 30,
        [int]$MemMB = 3072, [int]$PCore = 1, [string[]]$ExtraArgs = @()
    )
    $stdoutFile = [System.IO.Path]::GetTempFileName()
    $stderrFile = [System.IO.Path]::GetTempFileName()
    $disk = $null
    foreach ($ea in $ExtraArgs) { if ($ea -match 'file=([^,]+)') { $disk = $Matches[1] } }
    for ($attempt = 0; $attempt -lt 4; $attempt++) {
        $dataPort = Get-VmPort -Attempt $attempt
        $ctrlPort = $dataPort + 1
        $vmArgs = @('-kernel', $Kernel, '-data-port', "$dataPort", '-ctrl-port', "$ctrlPort", '-mem', "$MemMB", '-headless')
        if ($disk) { $vmArgs += @('-disk', $disk) }
        foreach ($ea in $ExtraArgs) { if ($ea -notmatch 'file=') { $vmArgs += $ea } }
        $proc = Start-Process -FilePath $script:CodexVmBin -ArgumentList $vmArgs -PassThru -WindowStyle Hidden -RedirectStandardOutput $stdoutFile -RedirectStandardError $stderrFile
        Start-Sleep -Milliseconds 500
        if ($proc.HasExited) { continue }
        $conn = Connect-Vm -DataPort $dataPort -CtrlPort $ctrlPort -TimeoutSec $ConnectTimeoutSec
        if ($conn) { return @{ Process = $proc; Conn = $conn; StdoutFile = $stdoutFile; StderrFile = $stderrFile } }
        Stop-VmGraceful -ProcessId $proc.Id -TimeoutMs 3000
        Start-Sleep -Milliseconds 500
    }
    Remove-Item -Force $stdoutFile, $stderrFile -ErrorAction SilentlyContinue
    return $null
}


# The total byte length of a complete compile answer, or -1 while one has
# not arrived yet: everything up to the first line starting SIZE:<n>, plus
# n payload bytes after that line's newline. The trailer (MAP/PROF/HEAP
# lines) comes after this point and is drained separately.
function Get-SizePayloadEnd {
    param([byte[]]$Bytes)
    # ASCII decoding turns each byte into exactly one char (bytes beyond
    # ASCII become '?'), so an index into this string is also an index
    # into $Bytes -- which is what makes string matching safe on a buffer
    # that is part text, part binary.
    $text = [System.Text.Encoding]::ASCII.GetString($Bytes)
    # The size line matches at a line start only, the same way the output
    # parser in compile.ps1 matches it.
    $sizeLine = [regex]::Match($text, '(?m)^SIZE:(\d+)')
    if (-not $sizeLine.Success) { return -1 }
    $lineEnd = $text.IndexOf("`n", $sizeLine.Index)
    if ($lineEnd -lt 0) { return -1 }   # the SIZE line itself is still streaming in
    $payloadStart = $lineEnd + 1
    $payloadLength = [long]$sizeLine.Groups[1].Value
    return $payloadStart + $payloadLength
}


# One compile through the QEMU fallback's serial wire, honouring codex-vm's
# -input/-output file contract so compile.ps1 parses the result the same
# way for both hosts: InputFile (mode line + assembled unit + EOT) streams
# to the data serial once the guest says READY, and everything the guest
# answers -- log lines, SIZE:<n>, the binary, the MAP/PROF/HEAP trailer --
# lands byte-for-byte in OutputFile.
#
# The read stops when the SIZE payload is complete plus a short trailer
# drain, not on idle alone: the guest stays running after a compile, so a
# purely idle-based read always pays the full idle timeout.
#
# Returns $true when OutputFile holds a complete answer, INCLUDING a
# compiler error report or crash dump -- those are the caller's to judge,
# exactly as they are when codex-vm writes the file. $false means boot
# failure, no READY, or deadline with no answer.
function Invoke-VmCompileFallback {
    param(
        [string]$Kernel, [string]$InputFile, [string]$OutputFile,
        [int]$MemMB = 3072, [int]$TimeoutSec = 600, [string]$DiskFile = ''
    )
    $extra = @()
    if ($DiskFile) { $extra = @('-drive', "file=$DiskFile,format=raw,if=ide,index=0") }
    $vm = Start-VmRun -Kernel $Kernel -MemMB $MemMB -ExtraArgs $extra
    if (-not $vm) { return $false }
    try {
        if (-not (Read-VmReady -Conn $vm.Conn -TimeoutSec 120)) {
            [Console]::Error.WriteLine("  qemu fallback: no READY from guest")
            return $false
        }
        $stream = $vm.Conn.Data.GetStream()
        $blob = [System.IO.File]::ReadAllBytes($InputFile)
        # Sending the unit in one burst stalls intermittently on large
        # units: the guest ends up waiting for bytes it never saw -- and a
        # lost EOT is a hang on both sides -- while small compiles never
        # hit it. codex-vm is immune because it preloads the input into
        # guest RAM before boot. So give the guest's reader a moment after
        # READY, then pace the stream in small flushed chunks; the cost is
        # well under a second on a 500 KB unit.
        Start-Sleep -Milliseconds 200
        $chunkSize = 8192
        for ($off = 0; $off -lt $blob.Length; $off += $chunkSize) {
            $len = [Math]::Min($chunkSize, $blob.Length - $off)
            $stream.Write($blob, $off, $len)
            $stream.Flush()
            Start-Sleep -Milliseconds 5
        }

        $out = [System.IO.MemoryStream]::new()
        $buf = New-Object byte[] 65536
        $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSec)
        $needed = [long](-1)
        $sawTerminalReport = $false
        while ([DateTime]::UtcNow -lt $deadline) {
            # Once SIZE's declared payload is fully in hand the loop is only
            # draining a trailer that in practice is never there, and the
            # read below blocks for the whole timeout before breaking. At
            # 2000ms that was a flat two seconds on every QEMU compile,
            # measured as roughly half the fixed floor.
            $stream.ReadTimeout = if ($needed -ge 0 -and $out.Length -ge $needed) { 250 } else { 5000 }
            $n = 0
            try { $n = $stream.Read($buf, 0, $buf.Length) } catch { $n = -1 }
            if ($n -eq 0) { break }
            if ($n -lt 0) {
                # Idle. Done if the payload is complete (the short pass above
                # was the trailer drain), or if the guest sent a terminal
                # report that has no SIZE line and never will.
                if ($needed -ge 0 -and $out.Length -ge $needed) { break }
                if ($sawTerminalReport) { break }
                # A read timeout and an abortive socket close both arrive
                # here as an exception. A dead guest has nothing more to
                # send, and without this the second case spins the loop hot
                # until the full deadline.
                if ($vm.Process.HasExited) { break }
                continue
            }
            $out.Write($buf, 0, $n)
            if ($needed -lt 0) {
                $bytes = $out.ToArray()
                $needed = Get-SizePayloadEnd -Bytes $bytes
                if ($needed -lt 0) {
                    $txt = [System.Text.Encoding]::ASCII.GetString($bytes)
                    if ($txt.Contains('!EXC') -or $txt.Contains('CODEGEN-HALTED') -or $txt.Contains('CODEGEN-ERRORS')) {
                        $sawTerminalReport = $true
                    }
                }
            }
        }
        [System.IO.File]::WriteAllBytes($OutputFile, $out.ToArray())
        if (($needed -ge 0 -and $out.Length -ge $needed) -or $sawTerminalReport) { return $true }
        [Console]::Error.WriteLine("  qemu fallback: deadline (${TimeoutSec}s) with no complete answer ($($out.Length) bytes so far)")
        return $false
    } finally {
        if ($vm.Conn) {
            if ($vm.Conn.Data) { try { $vm.Conn.Data.Dispose() } catch {} }
            if ($vm.Conn.Ctrl) { try { $vm.Conn.Ctrl.Dispose() } catch {} }
        }
        # The guest never exits on its own after a compile; kill it now
        # rather than paying Close-Vm's five-second wait for an exit that
        # is not coming.
        if ($vm.Process -and -not $vm.Process.HasExited) {
            Stop-VmGraceful -ProcessId $vm.Process.Id -TimeoutMs 1000
        }
        Remove-Item -Force $vm.StdoutFile, $vm.StderrFile -ErrorAction SilentlyContinue
    }
}
