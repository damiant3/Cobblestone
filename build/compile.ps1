# Compile a single .codex source file to CDX, TEXT, or IR by booting
# the bare-metal self-host compiler (Codex.cdx) in a VM with memory-mapped I/O.
#
# Input is loaded into guest memory at the serial ring buffer address (0x500000)
# before boot. Output is captured from guest UART writes and dumped to file.
# No TCP sockets. No serial port polling.
#
# Usage: compile.ps1 -Src <source.codex> -Out <out.cdx> -Log <log.out>
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
    [string]$Survey = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'vm-config.ps1')

# The survey multiplier system is gone: phase decks are fixed generous
# floors and the heap range is demand-paged (BuildSettings Demand Decks).
# -Survey is accepted and ignored for one transition cycle so callers
# that still pass it do not break.

$Stage0 = 'build-output\bare-metal\Codex.cdx'
if (-not (Test-Path -PathType Leaf $Stage0)) {
    [Console]::Error.WriteLine("MISSING: $Stage0 - run build.ps1 first")
    exit 2
}
$kernelHash = (Get-FileHash -Algorithm SHA256 $Stage0).Hash.Substring(0, 16)
[Console]::Error.WriteLine("kernel: $Stage0 [$kernelHash]")

if ($Break) {
    $mapFile = Join-Path (Split-Path $Stage0) 'Codex.map'
    if (-not (Test-Path $mapFile)) { $mapFile = Join-Path (Split-Path $PSScriptRoot) 'seed\Codex.map' }
    $breakAddr = -1
    if (Test-Path $mapFile) {
        foreach ($ml in Get-Content $mapFile) {
            if ($ml -match "^(0x[0-9a-fA-F]+)\s+\d+\s+$([regex]::Escape($Break))$") {
                $breakAddr = [Convert]::ToInt64($matches[1], 16) - 0x100000 + 224
                break
            }
        }
    }
    if ($breakAddr -ge 224) {
        $Stage0Copy = [System.IO.Path]::GetTempFileName()
        [System.IO.File]::Copy($Stage0, $Stage0Copy, $true)
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
    $baseMode = if ($IrUni) { "IR-UNI" } elseif ($IrCce) { "IR-CCE" } else { "CDX" }
    if ($Prose) { $baseMode = "$baseMode prose" }
    if ($Repl) { $baseMode = "$baseMode repl" }
    # Symbol map is opt-in per request ('map' flag). One-shot CDX compiles
    # keep their .map sidecar; repl (seed) builds never emitted one.
    if (-not $Repl -and $baseMode.StartsWith('CDX')) { $baseMode = "$baseMode map" }
    if ($Poison) { $baseMode = "$baseMode poison" }
    if ($PoisonCompact) { $baseMode = "$baseMode poison-compact" }
    if ($DebugMode) { $baseMode = "$baseMode debug" }
    if ($Profile) { $baseMode = "$baseMode profile" }
    if ($Trace) { $baseMode = "$baseMode trace" }
    if ($EscapeCheck) { $baseMode = "$baseMode escape-check" }
    if ($Uefi) { $baseMode = "$baseMode uefi" }
    # WatchdogPet: prologues pet the hang watchdog instead of relying on
    # heap-hwm progress. For interactive poll loops (boot menus), which
    # never allocate and would otherwise trip the no-progress panic.
    if ($Pet) { $baseMode = "$baseMode pet" }

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
            if ($attempt -lt $maxAttempts -and $curMem -lt 8192) {
                [Console]::Error.WriteLine("  compile: crash with ${curMem}MB, retrying with 3584MB")
                $curMem = 8192; $hitExc = $true; break
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
