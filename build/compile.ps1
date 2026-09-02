# compile.ps1 -- Compile a single .codex source file to CDX, TEXT, or IR
# GENERATED FROM THE CODEX SHELL DSL. Do not edit by hand.
# A hand edit here must NOT be submitted. Change the generator under
# codex/build/, regenerate, and submit the generator and this file
# together. Until then build/check-generated-scripts.ps1 reports this
# file as drifted, and the next regeneration discards the edit.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$Src,
    [Parameter(Mandatory=$true)]
    [string]$Out,
    [Parameter(Mandatory=$true)]
    [string]$Log,
    [int]$PCore = 1,
    [int]$MemMB = 3072,
    [switch]$MemNoCap,
    [int]$TimeoutSec = 600,
    [switch]$IrUni,
    [switch]$IrCce,
    [switch]$Text,
    [switch]$Measure,
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
    [ValidateRange(0, 10000)]
    [int]$Decks = 0,
    [string]$RawFlags = '',
    [string]$Passes = '',
    [string]$Survey = '',
    [string]$Kernel = '',
    [string]$DiskFile = '',
    [string]$Peer = '',
    [string]$Registry = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'vm-config.ps1')
. (Join-Path $PSScriptRoot 'work-wire.ps1')

if ($Kernel) {
    $Stage0 = $Kernel
    if ((-not (Test-Path -PathType Leaf $Stage0))) {
        [Console]::Error.WriteLine("MISSING: $Stage0 - the -Kernel you asked for is not there")
        exit 2
    }
} else {
    $Stage0 = Join-Path 'build-output' 'bare-metal' 'Codex.cdx'
    if ((-not (Test-Path -PathType Leaf $Stage0))) {
        [Console]::Error.WriteLine("MISSING: $Stage0 - run build.ps1 first, or pass -Kernel seed\Codex.cdx")
        exit 2
    }
}

$kernelHash = (Get-FileHash -Algorithm SHA256 $Stage0).Hash.Substring(0, 16)
[Console]::Error.WriteLine("kernel: $Stage0 [$kernelHash]")

if ((-not $Kernel)) {
    $seedPath = Join-Path (Split-Path $PSScriptRoot) 'seed' 'Codex.cdx'
    if ((Test-Path -PathType Leaf $seedPath)) {
        $seedHash = (Get-FileHash -Algorithm SHA256 $seedPath).Hash.Substring(0, 16)
        if (($seedHash -ne $kernelHash)) {
            [Console]::Error.WriteLine("NOTE: this kernel is NOT seed\Codex.cdx [$seedHash]. It is whatever build.ps1 last left in build-output. Pass -Kernel to choose.")
        }
    }
}



if ($Break) {
    $symAddr = (Resolve-Name -Name $Break -Kernel $Stage0)
    $breakAddr = $(if (($symAddr -gt 0)) { $symAddr - 0x100000 + 224 } else { -1 })
    if (($breakAddr -ge 224)) {
        $Stage0Copy = [System.IO.Path]::GetTempFileName()
        [System.IO.File]::Copy($Stage0, $Stage0Copy, $true)
        Set-ItemProperty $Stage0Copy -Name IsReadOnly -Value $false -ErrorAction SilentlyContinue
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
        if (($line -match $embeddedPat)) {
            $seedSeen["$($matches[1])::$($matches[2])"] = $true
        }
    }
    try {
        $ordered = Resolve-CiteOrder -RootLines $srcLines -Repo '.' -SeedSeen $seedSeen
    } catch {
        Set-Content -Path $Log -Value "error 3010: $($_.Exception.Message)" -Encoding UTF8
        exit 8
    }
    $implicitNames = @('ListUtils', 'Tuple')
    $unbundled = @($ordered | Where-Object { -not ($_.Quire -eq 'Foreword' -and $implicitNames -contains $_.Name) })
    if (($unbundled.Count -gt 0 -and $seedSeen.Count -gt 0)) {
        [Console]::Error.WriteLine("WARNING: compile.ps1 resolved $($unbundled.Count) chapter(s) not in bundled source:")
        foreach ($extra in $unbundled) {
            [Console]::Error.WriteLine("  $($extra.Quire)::$($extra.Name) ($($extra.Path))")
        }
        [Console]::Error.WriteLine('These chapters are cited by bundled code but missing from the app build script.')
        [Console]::Error.WriteLine('Add them to the build script''s chapter list, or remove the cites.')
    }


    $baseMode = if ($Measure) { "MEASURE" } elseif ($Text) { "TEXT" } elseif ($IrUni) { "IR-UNI" } elseif ($IrCce) { "IR-CCE" } else { "CDX" }
    if ($Prose) {
        $baseMode = "$baseMode prose"
    }
    if ($Repl) {
        $baseMode = "$baseMode repl"
    }
    if ($baseMode.StartsWith('CDX')) {
        $baseMode = "$baseMode map"
    }
    if ($Poison) {
        $baseMode = "$baseMode poison"
    }
    if ($PoisonCompact) {
        $baseMode = "$baseMode poison-compact"
    }
    if ($DebugMode) {
        $baseMode = "$baseMode debug"
    }
    if ($Profile) {
        $baseMode = "$baseMode profile"
    }
    if ($Decks -ne 0) {
        $baseMode = "$baseMode decks=$Decks"
    }
    if ($Passes) {
        $baseMode = "$baseMode passes=$Passes"
    }
    if ($RawFlags) {
        $baseMode = "$baseMode $RawFlags"
    }
    if ($Trace) {
        $baseMode = "$baseMode trace"
    }
    if ($EscapeCheck) {
        $baseMode = "$baseMode escape-check"
    }
    if ($Uefi) {
        $baseMode = "$baseMode uefi"
    }
    if ($Pet) {
        $baseMode = "$baseMode pet"
    }
    if ($DiskFile) {
        if ((-not (Test-Path -PathType Leaf $DiskFile))) {
            [Console]::Error.WriteLine("MISSING: -DiskFile $DiskFile is not there")
            exit 2
        }
        $baseMode = "$baseMode store"
    }


    if ($Peer) {
        $srcLines = (Add-PeerWorks -Lines $srcLines -Peer $Peer)
    } else {
        if ($Registry) {
            $srcLines = (Add-PeerWorks -Lines $srcLines -Registry $Registry)
        }
    }


    $script:DiagRegions = Get-DiagRegions -Ordered $ordered -SrcPath $Src
    # Streamed, not built. The StringBuilder held the whole unit, ToString copied it, the mode-line interpolation copied it a third time and WriteAllText encoded a fourth, all while srcLines stayed live. Same defect as the batch driver at 21043 and concat-codex-self at 21098, one copy worse. Same bytes: the mode line, a newline, every cite line with a newline, every source line with a newline, then the EOT.
    $inputFile = [System.IO.Path]::GetTempFileName()
    $bodyWriter = [System.IO.StreamWriter]::new($inputFile, $false, [System.Text.UTF8Encoding]::new($false))
    $bodyWriter.Write($baseMode)
    $bodyWriter.Write("`n")
    foreach ($l in (Format-CiteChapters -Ordered $ordered)) {
        $bodyWriter.Write($l)
        $bodyWriter.Write("`n")
    }
    foreach ($line in $srcLines) {
        $bodyWriter.Write($line)
        $bodyWriter.Write("`n")
    }
    $bodyWriter.Write([char]4)
    $bodyWriter.Dispose()
    $outputFile = [System.IO.Path]::GetTempFileName()
    $stderrFile = [System.IO.Path]::GetTempFileName()


    $vmBin = Join-Path (Split-Path $PSScriptRoot) 'tools\codex-vm.exe'
    $curMem = $MemMB
    $attempt = 0
    $maxAttempts = 2
    :compile_loop while (($attempt -lt $maxAttempts)) {
        $attempt++
        if ((Test-Path -PathType Leaf $outputFile)) {
            [System.IO.File]::WriteAllBytes($outputFile, [byte[]]::new(0))
        }
        if ($script:UseCodexVm) {
            $vmArgs = @('-kernel', $Stage0, '-input', $inputFile, '-output', $outputFile, '-mem', "$curMem", '-headless')
            if ($MemNoCap) {
                $vmArgs += '-mem-nocap'
            }
            if ($DiskFile) {
                $vmArgs += @('-disk', $DiskFile)
            }
            $proc = Start-Process -FilePath $vmBin -ArgumentList $vmArgs -PassThru -WindowStyle Hidden -RedirectStandardError $stderrFile
            $proc.WaitForExit($TimeoutSec * 1000)
            if ((-not $proc.HasExited)) {
                Stop-VmGraceful -ProcessId $proc.Id
                Set-Content -Path $Log -Value 'FAIL: VM timed out' -Encoding UTF8
                exit 3
            }
        } else {
            # No codex-vm on this host: the QEMU fallback serves the same
            # -input/-output contract over the serial wire, and the parsing
            # below cannot tell the difference. (-MemNoCap needs no translation:
            # the 3040 MB cap is codex-vm's, and the 0xFE8 cell the QEMU path
            # seeds already carries the real -m value.)
            $ok = Invoke-VmCompileFallback -Kernel $Stage0 -InputFile $inputFile -OutputFile $outputFile -MemMB $curMem -TimeoutSec $TimeoutSec -DiskFile $DiskFile
            if ((-not $ok)) {
                Set-Content -Path $Log -Value 'FAIL: VM timed out' -Encoding UTF8
                exit 3
            }
        }
        if (((-not (Test-Path -PathType Leaf $outputFile)) -or (Get-Item $outputFile).Length -eq 0)) {
            if (($attempt -lt $maxAttempts -and ($curMem -lt 3072))) {
                [Console]::Error.WriteLine("  compile: no output with ${curMem}MB, retrying with 3072MB")
                $curMem = 3072
                continue compile_loop
            } else {
                Set-Content -Path $Log -Value 'FAIL: no output' -Encoding UTF8
                if ((Test-Path -PathType Leaf $stderrFile)) {
                    Add-Content -Path $Log -Value (Get-Content $stderrFile -Raw) -Encoding UTF8
                }
                exit 4
            }
        }


        $outBytes = [System.IO.File]::ReadAllBytes($outputFile)
        $outText = ([System.Text.Encoding]::UTF8.GetString($outBytes))
        $outLines = $outText -split "`n"
        function Remap-Diag([string]$l) {
            return (Convert-DiagLine -Line $l -Regions $script:DiagRegions)
        }
        Set-Content -Path $Log -Value '' -Encoding UTF8
        $binSize = 0; $binStart = -1; $hitExc = $false
        for ($i = 0; $i -lt $outLines.Count; $i++) {
            $line = $outLines[$i].TrimEnd("`r")
            if ($line.StartsWith('SIZE:')) {
                if (($line.Substring(5) -match '^\d+')) {
                    $binSize = [int]$matches[0]
                }
                $binStart = $i + 1
                break
            }
            if (($line.StartsWith('CODEGEN-HALTED') -or $line.StartsWith('CODEGEN-ERRORS'))) {
                $errLines = [System.Collections.Generic.List[string]]::new()
                [void]$errLines.Add($line)
                for ($k = $i + 1; $k -lt $outLines.Count; $k++) { $el = $outLines[$k].TrimEnd("`r"); if ($el -and -not $el.StartsWith('WD:') -and -not $el.StartsWith('HEAP:') -and -not $el.StartsWith('STACK:')) { [void]$errLines.Add($el) } }
                foreach ($el in $errLines) {
                    Add-Content -Path $Log -Value (Remap-Diag $el) -Encoding UTF8
                }
                exit 4
            }
            if ($line.StartsWith('!EXC')) {
                Add-Content -Path $Log -Value $line -Encoding UTF8
                $report = (Format-CrashReport -ExcLines @($line) -Kernel $Stage0)
                foreach ($r in $report) {
                    Add-Content -Path $Log -Value $r -Encoding UTF8
                    [Console]::Error.WriteLine("  $r")
                }
                if (($attempt -lt $maxAttempts -and ($curMem -lt 8192))) {
                    $prevMem = $curMem
                    $curMem = 8192
                    [Console]::Error.WriteLine("  compile: crash with ${prevMem}MB, retrying with ${curMem}MB")
                    $hitExc = $true
                    break
                } else {
                    exit 4
                }
            }
            if (($line -and (-not $line.StartsWith('WD:')))) {
                Add-Content -Path $Log -Value (Remap-Diag $line) -Encoding UTF8
            }
        }


        if ($binStart -ge 0) {
            $sizeLineEnd = 0; $nlCount = 0
            for ($j = 0; $j -lt $outBytes.Length; $j++) {
                if ($outBytes[$j] -eq 10) {
                    $nlCount++; if ($nlCount -eq $binStart) { $sizeLineEnd = $j + 1; break }
                }
            }
            $binEnd = $outBytes.Length
            if (($binSize -gt 0 -and ($sizeLineEnd + $binSize) -le $outBytes.Length)) {
                $binEnd = $sizeLineEnd + $binSize
            } else {
                for ($j = $sizeLineEnd; $j -lt $outBytes.Length; $j++) { if ($outBytes[$j] -eq 10) { $afterNl = $j + 1; if ($afterNl + 3 -le $outBytes.Length) { $tag = [System.Text.Encoding]::ASCII.GetString($outBytes, $afterNl, [Math]::Min(5, $outBytes.Length - $afterNl)); if ($tag.StartsWith('WD:') -or $tag.StartsWith('HEAP:') -or $tag.StartsWith('STACK:')) { $binEnd = $j + 1; break } } } }
            }
            $actSize = $binEnd - $sizeLineEnd
            if ($actSize -gt 0) {
                $binBytes = New-Object byte[] $actSize
                [Array]::Copy($outBytes, $sizeLineEnd, $binBytes, 0, $actSize)
                [System.IO.File]::WriteAllBytes($Out, $binBytes)
                $tailStart = $binEnd
                if (($tailStart -lt $outBytes.Length)) {
                    $tailText = ([System.Text.Encoding]::UTF8.GetString($outBytes, $tailStart, $outBytes.Length - $tailStart))
                    $tailLines = $tailText -split "`n"
                    foreach ($tl in $tailLines) {
                        $t = $tl.TrimEnd("`r")
                        if ((($t.StartsWith('WD:') -or $t.StartsWith('HEAP:')) -or $t.StartsWith('STACK:'))) {
                            Add-Content -Path $Log -Value $t -Encoding UTF8
                        }
                    }

                    $mapFile = [System.IO.Path]::ChangeExtension($Out, '.map')
                    $inMap = $false
                    $mapLines = [System.Collections.Generic.List[string]]::new()
                    [void]$mapLines.Add('# Codex Symbol Map'); [void]$mapLines.Add('# Address         Size  Name')
                    $profFile = [System.IO.Path]::ChangeExtension($Out, '.prof')
                    $inProf = $false; $profCount = 0
                    $profLines = [System.Collections.Generic.List[string]]::new()
                    foreach ($tl in $tailLines) {
                        $tl = $tl.TrimEnd("`r")
                        if ($tl.StartsWith('MAP:')) {
                            $inMap = $true
                            continue
                        }
                        if ($tl.StartsWith('MAP-END')) {
                            $inMap = $false
                            continue
                        }
                        if (($inMap -and $tl.StartsWith('0x'))) {
                            [void]$mapLines.Add($tl)
                        }
                        if ($tl.StartsWith('PROF:')) {
                            if ((-not $inProf)) {
                                $inProf = $true
                                if (($tl.Substring(5) -match '^\d+')) {
                                    $profCount = [int]$matches[0]
                                }
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
            } else {
                Add-Content -Path $Log -Value "Binary size mismatch: the SIZE: line declared $binSize byte(s), the stream carried $actSize after it, out of $($outBytes.Length) total" -Encoding UTF8
                exit 5
            }
        }
        if ($hitExc) {
            continue compile_loop
        }
        # Output, but nothing that ends the scan: no SIZE:, no CODEGEN-*, no
        # !EXC. The log above holds only what survived the WD: filter, so when
        # the VM died early it reads exactly like a SUCCESSFUL compile and the
        # exit code is the only thing that disagrees. Both things that would
        # answer it were being discarded: codex-vm's stderr, which carries
        # `Output: N bytes` and any `SERIAL: ... DROPPED`, and the raw output.
        Add-Content -Path $Log -Value 'FAIL: the VM produced output but no SIZE: line, no CODEGEN-HALTED or CODEGEN-ERRORS, and no !EXC, so there is neither a binary nor a diagnostic to report.' -Encoding UTF8
        Add-Content -Path $Log -Value "  output: $($outBytes.Length) byte(s) in $($outLines.Count) line(s)" -Encoding UTF8
        if ((Test-Path -PathType Leaf $stderrFile)) {
            # The TAIL: the lines that answer this are the last ones. Bounded
            # because a repeated device failure prints per occurrence, which
            # put 29,610 bytes through here in one measured run.
            $errLinesAll = @(Get-Content $stderrFile -ErrorAction SilentlyContinue)
            if ($errLinesAll.Count -gt 0) {
                $errFrom = [Math]::Max(0, $errLinesAll.Count - 40)
                $hdr = if ($errFrom -gt 0) { "  codex-vm stderr, last 40 of $($errLinesAll.Count) line(s):" } else { "  codex-vm stderr:" }
                Add-Content -Path $Log -Value $hdr -Encoding UTF8
                for ($i = $errFrom; $i -lt $errLinesAll.Count; $i++) {
                    Add-Content -Path $Log -Value "    $($errLinesAll[$i])" -Encoding UTF8
                }
            }
        }
        $keepFrom = [Math]::Max(0, $outLines.Count - 40)
        Add-Content -Path $Log -Value "  last $($outLines.Count - $keepFrom) output line(s), WD:/HEAP:/STACK: included:" -Encoding UTF8
        for ($i = $keepFrom; $i -lt $outLines.Count; $i++) {
            $ol = $outLines[$i].TrimEnd("`r")
            if ($ol.Length -gt 200) { $ol = $ol.Substring(0, 200) + ' ...(truncated)' }
            Add-Content -Path $Log -Value "    $ol" -Encoding UTF8
        }
        exit 4

    }
} finally {
    if ($inputFile) {
        Remove-Item -Force -ErrorAction SilentlyContinue $inputFile
    }
    if ($outputFile) {
        Remove-Item -Force -ErrorAction SilentlyContinue $outputFile
    }
    if ($stderrFile) {
        Remove-Item -Force -ErrorAction SilentlyContinue $stderrFile
    }
}
