# Compile a single .codex source file to CDX, TEXT, or IR by booting
# the bare-metal self-host compiler (Codex.cdx) in a VM with memory-mapped I/O.
#
# Input is loaded into guest memory at the serial ring buffer address (0x500000)
# before boot. Output is captured from guest UART writes and dumped to file.
# No TCP sockets. No serial port polling.
#
# Usage: compile.ps1 -Src <source.codex> -Out <out.cdx> -Log <log.out>
#        compile.ps1 ... -Kernel seed\Codex.cdx     # boot a specific compiler
#        compile.ps1 ... -Peer 127.0.0.1:19300      # fetch quoted works from a peer
#
# The compiler booted is build-output\bare-metal\Codex.cdx unless -Kernel says
# otherwise. That is NOT the seed. See the note above $Stage0 before you trust
# any measurement taken with this script.
#
# Exit 0 = compile succeeded, output written.
# Exit non-zero = compile failed.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string]$Src,
    [Parameter(Mandatory=$true)] [string]$Out,
    [Parameter(Mandatory=$true)] [string]$Log,
    [int]$PCore = 1,
    [int]$MemMB = 3072,
    [switch]$IrUni,
    [switch]$IrCce,
    [switch]$Text,
    [switch]$Prose,
    [switch]$Repl,
    [switch]$Poison,
    [switch]$PoisonCompact,
    [switch]$DebugMode,
    [switch]$Profile,
    [switch]$Trace,
    [switch]$EscapeCheck,
    [switch]$Uefi,
    [switch]$Pet,
    [string]$Break,
    [ValidateRange(1, 10000)] [int]$Decks = 100,
    [string]$Passes = '',
    [string]$Survey = '',
    [string]$Kernel = '',
    [string]$DiskFile = '',
    [string]$Peer = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'vm-config.ps1')
. (Join-Path $PSScriptRoot 'work-wire.ps1')

# The survey multiplier system is gone: phase decks are fixed generous
# floors and the heap range is demand-paged (BuildSettings Demand Decks).
# -Survey is accepted and ignored for one transition cycle so callers
# that still pass it do not break.

# WHICH COMPILER THIS BOOTS, AND WHY IT MATTERS
#
# By default this is build-output\bare-metal\Codex.cdx -- NOT seed\Codex.cdx.
# That file is whatever the last build.ps1 left behind, which is the SUT, not
# the seed. Nothing keeps the two in step, and nothing warns when they drift.
#
# This has already produced a wrong answer. A per-chapter sweep of apps/works
# reported ~80 of 84 chapters compiling; the real figure against the depot seed
# was ~55, because build-output held a kernel from before the file-exists
# builtin was removed, and every chapter that called file-exists still resolved
# it. The sweep was measuring a compiler that no longer existed.
#
# So: the kernel is printed on stderr with its digest on every run. Read it.
# If you are measuring anything -- a sweep, a baseline, "does this chapter
# compile" -- pass -Kernel explicitly and know what you are asking.
#
#   compile.ps1 -Src x.codex -Out x.cdx -Log x.log -Kernel seed\Codex.cdx
#
# and compare that digest against the seed you think you are testing.

if ($Kernel) {
    $Stage0 = $Kernel
    if (-not (Test-Path -PathType Leaf $Stage0)) {
        [Console]::Error.WriteLine("MISSING: $Stage0 - the -Kernel you asked for is not there")
        exit 2
    }
} else {
    $Stage0 = 'build-output\bare-metal\Codex.cdx'
    if (-not (Test-Path -PathType Leaf $Stage0)) {
        [Console]::Error.WriteLine("MISSING: $Stage0 - run build.ps1 first, or pass -Kernel seed\Codex.cdx")
        exit 2
    }
}
$kernelHash = (Get-FileHash -Algorithm SHA256 $Stage0).Hash.Substring(0, 16)
[Console]::Error.WriteLine("kernel: $Stage0 [$kernelHash]")

# A drift warning, not a failure: the default kernel is legitimately the SUT
# during a build. But if you did not ask for one and it is not the seed, say so,
# because a measurement taken here is not a measurement of the seed.
if (-not $Kernel) {
    $seedPath = Join-Path (Split-Path $PSScriptRoot) 'seed\Codex.cdx'
    if (Test-Path -PathType Leaf $seedPath) {
        $seedHash = (Get-FileHash -Algorithm SHA256 $seedPath).Hash.Substring(0, 16)
        if ($seedHash -ne $kernelHash) {
            [Console]::Error.WriteLine("NOTE: this kernel is NOT seed\Codex.cdx [$seedHash]. It is whatever build.ps1 last left in build-output. Pass -Kernel to choose.")
        }
    }
}

if ($Break) {
    # Resolve the breakpoint against the kernel's OWN embedded MAP1, not a text
    # sidecar. This used to read seed\Codex.map, which drifts from seed\Codex.cdx
    # (see vm-config.ps1) -- so -Break on the seed patched INT3 at the address a
    # stale map claimed for the function, which is a byte in the middle of some
    # other function. A breakpoint that fires in the wrong place is worse than one
    # that does not fire, and this one had no way to tell you.
    $symAddr = Resolve-Name -Name $Break -Kernel $Stage0
    $breakAddr = if ($symAddr -gt 0) { $symAddr - 0x100000 + 224 } else { -1 }
    if ($breakAddr -ge 224) {
        $Stage0Copy = [System.IO.Path]::GetTempFileName()
        [System.IO.File]::Copy($Stage0, $Stage0Copy, $true)
        # File.Copy carries the read-only attribute across, and every depot file is
        # read-only under Perforce -- so the write below failed with "Access to the
        # path is denied" for any kernel synced from p4, which is every kernel worth
        # breaking on. -Break could not work on the seed at all until this line.
        (Get-Item $Stage0Copy).IsReadOnly = $false
        $cdxBytes = [System.IO.File]::ReadAllBytes($Stage0Copy)
        $origByte = $cdxBytes[$breakAddr]
        $cdxBytes[$breakAddr] = 0xCC
        [System.IO.File]::WriteAllBytes($Stage0Copy, $cdxBytes)
        $Stage0 = $Stage0Copy
        [Console]::Error.WriteLine("BREAK: patched INT3 at $Break (0x$(($breakAddr - 224 + 0x100000).ToString('X')), orig=0x$($origByte.ToString('X2')))")
    } else {
        [Console]::Error.WriteLine("BREAK: function '$Break' not found in map")
    }
}

$inputFile = $null
$outputFile = $null
$stderrFile = $null

. (Join-Path $PSScriptRoot 'quire-map.ps1')

try {
    $seedSeen = @{}
    $embeddedPat = '^Chapter:\s*(\w+)--(.+?)\s*$'
    $srcLines = [System.IO.File]::ReadAllLines($Src)
    foreach ($line in $srcLines) {
        if ($line -match $embeddedPat) { $seedSeen["$($matches[1])::$($matches[2])"] = $true }
    }
    try {
        $ordered = Resolve-CiteOrder -RootLines $srcLines -Repo '.' -SeedSeen $seedSeen
    } catch {
        "error 3010: $($_.Exception.Message)" | Set-Content -Path $Log -Encoding UTF8
        exit 8
    }
    # Only warn when compiling a source that is meant to be a self-contained
    # bundle: such a source carries embedded "Quire--Name" chapters (SeedSeen
    # non-empty), so any chapter resolved from a cite that is NOT already in
    # the bundle signals a genuine gap in the bundle. For a standalone test
    # compile SeedSeen is empty and every cited chapter is legitimately
    # resolved and prepended below (line ~115); warning there is stale noise.
    if ($ordered.Count -gt 0 -and $seedSeen.Count -gt 0) {
        [Console]::Error.WriteLine("WARNING: compile.ps1 resolved $($ordered.Count) chapter(s) not in bundled source:")
        foreach ($extra in $ordered) {
            [Console]::Error.WriteLine("  $($extra.Quire)::$($extra.Name) ($($extra.Path))")
        }
        [Console]::Error.WriteLine("These chapters are cited by bundled code but missing from the app build script.")
        [Console]::Error.WriteLine("Add them to the build script's chapter list, or remove the cites.")
    }
    # Mode header (base flags).
    $baseMode = if ($Text) { "TEXT" } elseif ($IrUni) { "IR-UNI" } elseif ($IrCce) { "IR-CCE" } else { "CDX" }
    if ($Prose) { $baseMode = "$baseMode prose" }
    if ($Repl) { $baseMode = "$baseMode repl" }
    # Symbol map is opt-in per request ('map' flag). One-shot CDX compiles
    # keep their .map sidecar; repl (seed) builds never emitted one.
    if (-not $Repl -and $baseMode.StartsWith('CDX')) { $baseMode = "$baseMode map" }
    if ($Poison) { $baseMode = "$baseMode poison" }
    if ($PoisonCompact) { $baseMode = "$baseMode poison-compact" }
    if ($DebugMode) { $baseMode = "$baseMode debug" }
    if ($Profile) { $baseMode = "$baseMode profile" }
    # Phase deck floors, as a percentage of the BuildSettings defaults.
    # 100 = the defaults; 200 = twice them. This is the escape hatch for a
    # source that outgrows the floors compiled into the kernel you are
    # compiling with -- without it, raising the constant cannot help, because
    # the kernel doing the compile enforces its own baked-in floor.
    if ($Decks -ne 100) { $baseMode = "$baseMode decks=$Decks" }
    # IR pass pipeline: comma-separated pass names ('fold-constants,inline-leaf-calls'),
    # or 'none' for the empty pipeline. Absent = the kernel's default pipeline.
    # This is the ablation knob: it exists so a pass can be switched off and measured.
    if ($Passes) { $baseMode = "$baseMode passes=$Passes" }
    if ($Trace) { $baseMode = "$baseMode trace" }
    if ($EscapeCheck) { $baseMode = "$baseMode escape-check" }
    if ($Uefi) { $baseMode = "$baseMode uefi" }
    # WatchdogPet: prologues pet the hang watchdog instead of relying on
    # heap-hwm progress. For interactive poll loops (boot menus), which
    # never allocate and would otherwise trip the no-progress panic.
    if ($Pet) { $baseMode = "$baseMode pet" }
    # Attach a fact-store disk so the compiler can resolve quotations from
    # DiskFacts as well as from the offered-works blob. The 'store' flag tells
    # the compiler a store disk is present; -disk gives codex-vm the image.
    if ($DiskFile) {
        if (-not (Test-Path -PathType Leaf $DiskFile)) {
            [Console]::Error.WriteLine("MISSING: -DiskFile $DiskFile is not there")
            exit 2
        }
        $baseMode = "$baseMode store"
    }

    # A quotation the source does not carry beside it can be fetched from a peer
    # and prepended to the %%QUOTED-WORKS%% blob (BACKLOG 6.2). This is the whole
    # of peer resolution and it lives here rather than in the compiler on
    # purpose: Library Rule 2 bars the compiler from citing the net stack, and it
    # does not need to -- it hashes the content, checks the signature, checks the
    # pinned key and checks the floor itself, so an untrusted messenger is safe.
    # The works go in; the trust stays in the source's `trusting` declarations.
    if ($Peer) { $srcLines = Add-PeerWorks -Lines $srcLines -Peer $Peer }

    # Body: cited chapters + source + EOT. Constant across attempts.
    $bodyBuilder = [System.Text.StringBuilder]::new(524288)
    foreach ($l in (Format-CiteChapters -Ordered $ordered)) { [void]$bodyBuilder.Append($l + "`n") }
    foreach ($line in $srcLines) { [void]$bodyBuilder.Append($line + "`n") }
    [void]$bodyBuilder.Append([char]4)  # EOT
    $bodyText = $bodyBuilder.ToString()

    $inputFile = [System.IO.Path]::GetTempFileName()
    [System.IO.File]::WriteAllText($inputFile, "$baseMode`n$bodyText", [System.Text.UTF8Encoding]::new($false))

    $outputFile = [System.IO.Path]::GetTempFileName()
    $stderrFile = [System.IO.Path]::GetTempFileName()

    $vmBin = Join-Path (Split-Path $PSScriptRoot) 'tools\codex-vm.exe'
    $curMem = $MemMB
    $attempt = 0
    $maxAttempts = 2
    :compile_loop while ($attempt -lt $maxAttempts) {
    $attempt++
    if (Test-Path $outputFile) { [System.IO.File]::WriteAllBytes($outputFile, [byte[]]::new(0)) }

    $vmArgs = @('-kernel', $Stage0, '-input', $inputFile, '-output', $outputFile, '-mem', "$curMem", '-headless')
    if ($DiskFile) { $vmArgs += @('-disk', $DiskFile) }
    $proc = Start-Process -FilePath $vmBin -ArgumentList $vmArgs -PassThru -WindowStyle Hidden -RedirectStandardError $stderrFile
    $proc.WaitForExit(600000)
    if (-not $proc.HasExited) {
        Stop-VmGraceful -ProcessId $proc.Id
        "FAIL: VM timed out" | Set-Content -Path $Log -Encoding UTF8
        exit 3
    }

    if (-not (Test-Path $outputFile) -or (Get-Item $outputFile).Length -eq 0) {
        if ($attempt -lt $maxAttempts -and $curMem -lt 3072) {
            [Console]::Error.WriteLine("  compile: no output with ${curMem}MB, retrying with 3072MB")
            $curMem = 3072; continue compile_loop
        }
        "FAIL: no output" | Set-Content -Path $Log -Encoding UTF8
        if (Test-Path $stderrFile) { Add-Content -Path $Log -Value (Get-Content $stderrFile -Raw) -Encoding UTF8 }
        exit 4
    }

    $outBytes = [System.IO.File]::ReadAllBytes($outputFile)
    $outText = [System.Text.Encoding]::UTF8.GetString($outBytes)
    $outLines = $outText -split "`n"

    Set-Content -Path $Log -Value '' -Encoding UTF8
    $binSize = 0; $binStart = -1
    $hitExc = $false
    for ($i = 0; $i -lt $outLines.Count; $i++) {
        $line = $outLines[$i].TrimEnd("`r")
        if ($line.StartsWith('SIZE:')) {
            if ($line.Substring(5) -match '^\d+') { $binSize = [int]$matches[0] }
            $binStart = $i + 1; break
        }
        if ($line.StartsWith('CODEGEN-HALTED') -or $line.StartsWith('CODEGEN-ERRORS')) {
            $errLines = [System.Collections.Generic.List[string]]::new()
            [void]$errLines.Add($line)
            for ($k = $i + 1; $k -lt $outLines.Count; $k++) {
                $el = $outLines[$k].TrimEnd("`r")
                if ($el -and -not $el.StartsWith('WD:') -and -not $el.StartsWith('HEAP:') -and -not $el.StartsWith('STACK:')) {
                    [void]$errLines.Add($el)
                }
            }
            foreach ($el in $errLines) { Add-Content -Path $Log -Value $el -Encoding UTF8 }
            exit 4
        }
        if ($line.StartsWith('!EXC')) {
            Add-Content -Path $Log -Value $line -Encoding UTF8
            # Resolve the crash before anyone has to. Format-CrashReport has
            # existed, and been documented as what "the harness prints", while
            # nothing anywhere called it -- so a compiler crash landed in the log
            # as a bare register dump and every investigation started by hand-
            # resolving a RIP. The symbols come from $Stage0's own embedded MAP1,
            # which is the binary that actually crashed.
            $report = Format-CrashReport -ExcLines @($line) -Kernel $Stage0
            foreach ($r in $report) {
                Add-Content -Path $Log -Value $r -Encoding UTF8
                [Console]::Error.WriteLine("  $r")
            }
            if ($attempt -lt $maxAttempts -and $curMem -lt 8192) {
                # The retry line used to say "retrying with 3584MB" while setting
                # 8192. Report the number actually used.
                $prevMem = $curMem
                $curMem = 8192
                [Console]::Error.WriteLine("  compile: crash with ${prevMem}MB, retrying with ${curMem}MB")
                $hitExc = $true; break
            }
            exit 4
        }
        if ($line -and -not $line.StartsWith('WD:')) { Add-Content -Path $Log -Value $line -Encoding UTF8 }
    }

    if ($binStart -ge 0) {
        $sizeLineEnd = 0; $nlCount = 0
        for ($j = 0; $j -lt $outBytes.Length; $j++) {
            if ($outBytes[$j] -eq 10) { $nlCount++; if ($nlCount -eq $binStart) { $sizeLineEnd = $j + 1; break } }
        }
        # Find end of IR binary data: scan for next LF-terminated line
        # The IR block ends with a LF from print-line; after that come
        # heap/stack diagnostic lines. If SIZE carried a byte count, use
        # it directly; otherwise scan forward for the first diagnostic.
        $binEnd = $outBytes.Length
        if ($binSize -gt 0 -and ($sizeLineEnd + $binSize) -le $outBytes.Length) {
            $binEnd = $sizeLineEnd + $binSize
        } else {
            for ($j = $sizeLineEnd; $j -lt $outBytes.Length; $j++) {
                if ($outBytes[$j] -eq 10) {
                    $afterNl = $j + 1
                    if ($afterNl + 3 -le $outBytes.Length) {
                        $tag = [System.Text.Encoding]::ASCII.GetString($outBytes, $afterNl, [Math]::Min(5, $outBytes.Length - $afterNl))
                        if ($tag.StartsWith('WD:') -or $tag.StartsWith('HEAP:') -or $tag.StartsWith('STACK:')) {
                            $binEnd = $j + 1; break
                        }
                    }
                }
            }
        }
        $actSize = $binEnd - $sizeLineEnd
        if ($actSize -gt 0) {
            $binBytes = New-Object byte[] $actSize
            [Array]::Copy($outBytes, $sizeLineEnd, $binBytes, 0, $actSize)
            [System.IO.File]::WriteAllBytes($Out, $binBytes)

            # Parse remaining output after binary for MAP and PROF lines
            $tailStart = $binEnd
            if ($tailStart -lt $outBytes.Length) {
                $tailText = [System.Text.Encoding]::UTF8.GetString($outBytes, $tailStart, $outBytes.Length - $tailStart)
                $tailLines = $tailText -split "`n"

                # Capture MAP
                $mapFile = [System.IO.Path]::ChangeExtension($Out, '.map')
                $inMap = $false
                $mapLines = [System.Collections.Generic.List[string]]::new()
                [void]$mapLines.Add('# Codex Symbol Map')
                [void]$mapLines.Add('# Address         Size  Name')

                # Capture PROF
                $profFile = [System.IO.Path]::ChangeExtension($Out, '.prof')
                $inProf = $false; $profCount = 0
                $profLines = [System.Collections.Generic.List[string]]::new()

                foreach ($tl in $tailLines) {
                    $tl = $tl.TrimEnd("`r")
                    if ($tl.StartsWith('MAP:')) { $inMap = $true; continue }
                    if ($tl.StartsWith('MAP-END')) { $inMap = $false; continue }
                    if ($inMap -and $tl.StartsWith('0x')) { [void]$mapLines.Add($tl) }
                    if ($tl.StartsWith('PROF:')) {
                        if (-not $inProf) {
                            $inProf = $true
                            if ($tl.Substring(5) -match '^\d+') { $profCount = [int]$matches[0] }
                        } else {
                            [void]$profLines.Add($tl.Substring(5))
                        }
                    }
                }
                if ($mapLines.Count -gt 2) {
                    [System.IO.File]::WriteAllLines($mapFile, $mapLines, [System.Text.UTF8Encoding]::new($false))
                }
                if ($profLines.Count -gt 0) {
                    [System.IO.File]::WriteAllLines($profFile, $profLines, [System.Text.UTF8Encoding]::new($false))
                    [Console]::Error.WriteLine("  profile: $($profLines.Count) samples -> $profFile")
                }
            }
            exit 0
        }
        "Binary size mismatch" | Add-Content -Path $Log -Encoding UTF8; exit 5
    }
    if ($hitExc) { continue compile_loop }
    exit 4
    } # end compile_loop
} finally {
    if ($inputFile) { Remove-Item -Force $inputFile -ErrorAction SilentlyContinue }
    if ($outputFile) { Remove-Item -Force $outputFile -ErrorAction SilentlyContinue }
    if ($stderrFile) { Remove-Item -Force $stderrFile -ErrorAction SilentlyContinue }
}
