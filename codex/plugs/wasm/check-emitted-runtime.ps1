# Check the invariants of the runtime this plug emits into EVERY module.
#
#     pwsh -File codex/plugs/wasm/check-emitted-runtime.ps1 <module.wat>
#
# WHY THIS EXISTS AND WHY IT IS STRUCTURAL. The two properties below cost
# nothing when they hold and an order of magnitude when they do not, and
# NEITHER IS VISIBLE TO wasm-e2e.ps1's grading: a module that grows its memory
# one page at a time and one that grows it in 16 MB steps print exactly the
# same bytes, agree with x86-64 exactly as well, and differ by 205 seconds on
# the Codex compiler's own source. A behavioural check would have to count
# host calls or time the run, and wasm has no way to observe either from
# inside; timing is not deterministic and this bed is graded, not benchmarked.
# So the invariant is asserted on the emitted TEXT, where it is exact.
#
# It runs on any subject because the runtime is emitted whole into every
# module -- so wasm-e2e.ps1 calls it on the first one it emits and the cost is
# a file read.
#
# TO SEE IT FIRE, which is the only thing that makes a green run mean
# anything: change 256 to 1 in `wasm-grow-step-pages`, or put `memory.grow`
# back inline in `$bump_alloc`, and re-emit any subject.
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Wat)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -PathType Leaf $Wat)) { Write-Host "REFUSE: no such module: $Wat"; exit 2 }
$text = [IO.File]::ReadAllText($Wat)
$problems = @()

function Get-FuncBody([string]$src, [string]$name) {
    # A definition runs from its own `  (func $name` to the next one at the
    # same indent. Every function this plug emits is at two spaces.
    $start = $src.IndexOf("  (func `$$name ")
    if ($start -lt 0) { $start = $src.IndexOf("  (func `$$name`n") }
    if ($start -lt 0) { return $null }
    $next = $src.IndexOf("`n  (func ", $start + 8)
    if ($next -lt 0) { $next = $src.Length }
    return $src.Substring($start, $next - $start)
}

# 1. THE GROWTH POLICY HAS ONE HOME, AND A FLOOR.
#
# `$bump_alloc` grew by exactly the pages an allocation needed, which is one
# memory.grow per 64 KB -- about 56,000 of them to reach the 3.7 GB the Codex
# compiler wants for its own source. V8's cost per grow rises with the memory
# it already holds, so that is 167 seconds of a 223 second compile under node
# and 0.21 seconds under wasmtime. A browser pays what node pays.
#
# The floor is checked rather than the exact step, because raising the step is
# somebody's call to make and removing it is not. A fixed step is also the
# point: geometric growth overshoots by up to 12.5%, and a compiler-sized
# subject sits at 90% of wasm32's hard 4 GiB ceiling.
$grows = ([regex]::Matches($text, 'memory\.grow')).Count
if ($grows -ne 1) {
    $problems += "memory.grow appears $grows times; the growth policy must have exactly ONE home (`$grow_by), or it will be changed in one place and not the other"
}
$growBy = Get-FuncBody $text 'grow_by'
if (-not $growBy) {
    $problems += "no `$grow_by: the growth policy has no home, so nothing bounds how often memory.grow is called"
} else {
    if ($growBy -notmatch 'memory\.grow') {
        $problems += "`$grow_by does not call memory.grow, so the one home is not where growing happens"
    }
    $floor = [regex]::Match($growBy, 'i32\.lt_u \(local\.get \$pages\) \(i32\.const (\d+)\)')
    if (-not $floor.Success) {
        $problems += "`$grow_by does not clamp `$pages to a minimum: it grows by exactly what was asked for, which is one memory.grow per 64 KB"
    } elseif ([int]$floor.Groups[1].Value -lt 256) {
        $problems += "`$grow_by's step floor is $($floor.Groups[1].Value) pages; it must be at least 256 (16 MB)"
    }
}

# 2. THE READER READS IN BLOCKS.
#
# `$read_byte` hands out one byte, which every caller depends on -- a line
# ends at a newline and the boundary decisions above are made a byte at a
# time. What it must not do is ISSUE one fd_read per byte: reading a 2.9 MB
# source that way is 2.9 million host calls.
$readByte = Get-FuncBody $text 'read_byte'
if (-not $readByte) {
    $problems += "no `$read_byte"
} else {
    $len = [regex]::Match($readByte, '\(i32\.store \(i32\.const 4\) \(i32\.const (\d+)\)\)')
    if (-not $len.Success) {
        $problems += "`$read_byte's fd_read length is not a constant this check can read; if the shape changed, change this check with it"
    } elseif ([int]$len.Groups[1].Value -lt 4096) {
        $problems += "`$read_byte asks fd_read for $($len.Groups[1].Value) bytes at a time; it must fill a buffer of at least 4096"
    }
}

# 3. THE TEXT READERS GROW; THEY DO NOT TRUNCATE.
#
# `$read_file_uni` and `$read_serial_cce` each used to reserve a fixed 4 MiB
# and then read the rest of the wire while discarding it. What that produces
# is not a truncation message: it is `CDX3002 Undefined name: <x>` about a
# file that plainly defines <x>, because the prefix no longer defines what it
# references. Nothing anywhere mentions the input, which is the one fact that
# would explain the error.
#
# `$read_serial_cce` is the one that mattered most, and not for source -- it
# is how WasmPlug.codex receives IR TEXT, which runs several times the size of
# the program it came from.
#
# Both now extend by re-bumping. Two `$bump_alloc` calls in the body is the
# signature: one for the initial reservation and one inside the loop.
foreach ($fn in @('read_file_uni', 'read_serial_cce')) {
    $body = Get-FuncBody $text $fn
    if (-not $body) { $problems += "no `$$fn"; continue }
    $bumps = ([regex]::Matches($body, [regex]::Escape('call $bump_alloc'))).Count
    if ($bumps -lt 2) {
        $problems += "`$$fn calls bump_alloc $bumps time(s): it reserves once and never extends, so an input past that reservation is read and DISCARDED"
    }
    $fixed = [regex]::Match($body, 'local\.set \$cap \(i32\.const (\d+)\)')
    if ($fixed.Success -and [int]$fixed.Groups[1].Value -gt 2097152) {
        $problems += "`$$fn reserves $($fixed.Groups[1].Value) bytes up front; the growing form starts small and doubles"
    }
}

# 4. ONE READER OF THE STREAM.
#
# `$read_file_raw` reads fd 0 for itself, so it must first drain whatever
# `$read_byte` buffered and did not hand out. Without that, a program that
# calls read-line-uni and then read-file-raw silently loses up to a buffer.
$rawFile = Get-FuncBody $text 'read_file_raw'
if ($rawFile -and $rawFile -notmatch '\$rd_pos') {
    $problems += "`$read_file_raw never touches `$rd_pos, so it does not drain what `$read_byte buffered: read-line-uni followed by read-file-raw silently loses those bytes"
}

if ($problems.Count -gt 0) {
    Write-Host "FAIL emitted-runtime invariants ($($problems.Count)) in $Wat"
    foreach ($p in $problems) { Write-Host "  - $p" }
    exit 1
}
Write-Host "[emitted-runtime] ok: one growth policy with a >=256 page floor, a blocked reader that grows, one reader of the stream"
exit 0
