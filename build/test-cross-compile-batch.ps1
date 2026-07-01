# Batch cross-compile: boots one seed VM (IR-CCE REPL) + one plug VM (REPL)
# to compile all tests. Two VM boots total instead of 2*N.
#
# Usage:
#   build/test-cross-compile-batch.ps1 -ListFile <sources.txt> -OutRoot <dir> -Arch riscv64
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string]$ListFile,
    [Parameter(Mandatory=$true)] [string]$OutRoot,
    [ValidateSet('arm64','riscv64')] [string]$Arch = 'riscv64'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
[Environment]::CurrentDirectory = (Get-Location).Path
. (Join-Path $PSScriptRoot 'vm-config.ps1')
. (Join-Path $PSScriptRoot 'quire-map.ps1')

$sources = @(Get-Content -Path $ListFile | Where-Object { $_.Trim() -ne '' })
if ($sources.Count -eq 0) { exit 0 }

$plugName = if ($Arch -eq 'riscv64') { 'riscv' } else { 'arm64' }
$plugCdx = Join-Path '.' "codex\plugs\$plugName\build-output\$plugName-plug.cdx"
$Stage0 = Join-Path '.' 'build-output\bare-metal\Codex.cdx'
$seedCdx = Join-Path '.' 'seed\Codex.cdx'
New-Item -ItemType Directory -Force (Split-Path $Stage0) | Out-Null
if (-not (Test-Path $Stage0)) { Copy-Item -Force $seedCdx $Stage0 }

function Resolve-Source {
    param([string]$SrcPath)
    $lines = [System.IO.File]::ReadAllLines($SrcPath)
    $seedSeen = @{}; $embPat = '^Chapter:\s*(\w+)--(.+?)\s*$'
    foreach ($l in $lines) {
        if ($l -match $embPat) { $seedSeen["$($matches[1])::$($matches[2])"] = $true }
    }
    try { $ordered = Resolve-CiteOrder -RootLines $lines -Repo '.' -SeedSeen $seedSeen }
    catch { return $null }
    $sb = [System.Text.StringBuilder]::new(524288)
    foreach ($l in (Format-CiteChapters -Ordered $ordered)) { [void]$sb.Append($l + "`n") }
    foreach ($l in $lines) { [void]$sb.Append($l + "`n") }
    return $sb.ToString()
}

# ---- Phase 1: Batch IR compilation (one seed VM) ----
Write-Host "--- Phase 1: Batch IR compile ($($sources.Count) tests, 1 VM) ---"
$inputSb = [System.Text.StringBuilder]::new(10485760)
$testNames = [System.Collections.Generic.List[string]]::new()
for ($i = 0; $i -lt $sources.Count; $i++) {
    $name = [System.IO.Path]::GetFileNameWithoutExtension($sources[$i])
    $testOut = Join-Path $OutRoot $name
    New-Item -ItemType Directory -Force -Path $testOut | Out-Null
    $resolved = Resolve-Source $sources[$i]
    if ($null -eq $resolved) {
        "8" | Set-Content -Path (Join-Path $testOut '.exitcode') -Encoding UTF8
        continue
    }
    $testNames.Add($name)
    [void]$inputSb.Append("IR-CCE repl`n")
    [void]$inputSb.Append($resolved)
    [void]$inputSb.Append([char]4)
}
if ($testNames.Count -eq 0) { exit 0 }

$irInputFile = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($irInputFile, $inputSb.ToString(), [System.Text.UTF8Encoding]::new($false))
$irOutputFile = [System.IO.Path]::GetTempFileName()
$irStderrFile = [System.IO.Path]::GetTempFileName()

$irStart = Get-Date
$proc = Start-Process -FilePath $script:CodexVmBin -ArgumentList @(
    '-kernel', $Stage0, '-input', $irInputFile, '-output', $irOutputFile, '-mem', '3072', '-headless'
) -PassThru -WindowStyle Hidden -RedirectStandardError $irStderrFile
$irMaxWait = [Math]::Max(60, $testNames.Count * 2 + 30)
$irWaitStart = Get-Date
$lastSize = 0; $stableCount = 0
while (-not $proc.HasExited) {
    Start-Sleep -Milliseconds 500
    $elapsed = ((Get-Date) - $irWaitStart).TotalSeconds
    if ($elapsed -gt $irMaxWait) { break }
    $curSize = if (Test-Path $irOutputFile) { (Get-Item $irOutputFile).Length } else { 0 }
    if ($curSize -eq $lastSize -and $curSize -gt 0) { $stableCount++ } else { $stableCount = 0 }
    $lastSize = $curSize
    if ($stableCount -ge 6) { break }
}
if (-not $proc.HasExited) { try { Stop-Process -Id $proc.Id -Force } catch {} }
Start-Sleep -Milliseconds 300
$irEnd = Get-Date
Write-Host "  IR compile: $([math]::Round(($irEnd - $irStart).TotalSeconds, 1))s"

# Parse IR output: extract per-test IR blocks via SIZE: markers
Start-Sleep -Milliseconds 200
$raw = [byte[]]::new(0)
if (Test-Path $irOutputFile) {
    try { $raw = [System.IO.File]::ReadAllBytes($irOutputFile) } catch {
        Start-Sleep -Milliseconds 500
        try { $raw = [System.IO.File]::ReadAllBytes($irOutputFile) } catch { $raw = [byte[]]::new(0) }
    }
}
$pos = 0; $testIdx = 0
$irBlocks = [System.Collections.Generic.Dictionary[string,byte[]]]::new()

function NextLine {
    if ($script:pos -ge $raw.Length) { return $null }
    $start = $script:pos
    while ($script:pos -lt $raw.Length -and $raw[$script:pos] -ne 10) { $script:pos++ }
    $end = $script:pos
    if ($script:pos -lt $raw.Length) { $script:pos++ }
    $len = $end - $start
    if ($len -gt 0 -and $raw[$start + $len - 1] -eq 13) { $len-- }
    if ($len -le 0) { return '' }
    return [System.Text.Encoding]::UTF8.GetString($raw, $start, $len)
}

while ($testIdx -lt $testNames.Count -and $pos -lt $raw.Length) {
    $name = $testNames[$testIdx]
    $testOut = Join-Path $OutRoot $name
    $found = $false
    :testloop while ($pos -lt $raw.Length) {
        $line = NextLine
        if ($null -eq $line) { break }
        if ($line.StartsWith('SIZE:')) {
            $binSize = 0
            if ($line.Substring(5) -match '^\d+') { $binSize = [int]$matches[0] }
            if ($binSize -gt 0 -and $pos + $binSize -le $raw.Length) {
                $irBytes = New-Object byte[] $binSize
                [Array]::Copy($raw, $pos, $irBytes, 0, $binSize)
                $irBlocks[$name] = $irBytes
                $pos = [Math]::Min($pos + $binSize, $raw.Length)
                $found = $true
            }
            break testloop
        }
        elseif ($line.StartsWith('CODEGEN-HALTED') -or $line.StartsWith('CODEGEN-ERRORS')) {
            while ($pos -lt $raw.Length) {
                $el = NextLine; if ($null -eq $el) { break }
                if ($el.StartsWith('CODEGEN-HALTED')) { break }
            }
            break testloop
        }
        elseif ($line.StartsWith('STACK:')) { break testloop }
    }
    if (-not $found) { "7" | Set-Content -Path (Join-Path $testOut '.exitcode') -Encoding UTF8 }
    $testIdx++
}

Write-Host "  IR blocks: $($irBlocks.Count) / $($testNames.Count)"
Remove-Item -Force $irInputFile, $irOutputFile, $irStderrFile -ErrorAction SilentlyContinue

if ($irBlocks.Count -eq 0) { Write-Host "No IR blocks produced"; exit 1 }

# ---- Phase 2: Batch plug codegen (one plug VM) ----
Write-Host "--- Phase 2: Batch plug codegen ($($irBlocks.Count) tests, 1 VM) ---"
$plugInputList = [System.Collections.Generic.List[byte]]::new(4194304)
$plugTestNames = [System.Collections.Generic.List[string]]::new()

# Build CCE mode header for "IR-CCE repl"
$modeHdr = [System.Collections.Generic.List[byte]]::new()
foreach ($ch in "IR-CCE repl".ToCharArray()) {
    $u = [int]$ch
    if ($u -lt 256) { $modeHdr.Add([byte]$script:UnicodeToCce[$u]) }
}
$modeHdr.Add([byte]1)  # CCE newline
$modeHeaderBytes = $modeHdr.ToArray()

foreach ($name in $testNames) {
    if (-not $irBlocks.ContainsKey($name)) { continue }
    $plugTestNames.Add($name)
    $plugInputList.AddRange($modeHeaderBytes)
    $plugInputList.AddRange($irBlocks[$name])
    $plugInputList.Add([byte]0)  # null terminator for read-file
}

$plugInputFile = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllBytes($plugInputFile, $plugInputList.ToArray())
$plugOutputFile = [System.IO.Path]::GetTempFileName()
$plugStderrFile = [System.IO.Path]::GetTempFileName()

$plugStart = Get-Date
$proc = Start-Process -FilePath $script:CodexVmBin -ArgumentList @(
    '-kernel', $plugCdx, '-input', $plugInputFile, '-output', $plugOutputFile, '-mem', '3072', '-headless'
) -PassThru -WindowStyle Hidden -RedirectStandardError $plugStderrFile
$plugMaxWait = [Math]::Max(30, $plugTestNames.Count * 1 + 20)
$plugWaitStart = Get-Date
$lastSize = 0; $stableCount = 0
while (-not $proc.HasExited) {
    Start-Sleep -Milliseconds 500
    $elapsed = ((Get-Date) - $plugWaitStart).TotalSeconds
    if ($elapsed -gt $plugMaxWait) { break }
    $curSize = if (Test-Path $plugOutputFile) { (Get-Item $plugOutputFile).Length } else { 0 }
    if ($curSize -eq $lastSize -and $curSize -gt 0) { $stableCount++ } else { $stableCount = 0 }
    $lastSize = $curSize
    if ($stableCount -ge 6) { break }
}
if (-not $proc.HasExited) { try { Stop-Process -Id $proc.Id -Force } catch {} }
Start-Sleep -Milliseconds 300
$plugEnd = Get-Date
Write-Host "  Plug codegen: $([math]::Round(($plugEnd - $plugStart).TotalSeconds, 1))s"

# Parse plug output: split wire blocks by WIRE-END markers
Start-Sleep -Milliseconds 200
$plugRaw = [byte[]]::new(0)
if (Test-Path $plugOutputFile) {
    try { $plugRaw = [System.IO.File]::ReadAllBytes($plugOutputFile) } catch {
        Start-Sleep -Milliseconds 500
        try { $plugRaw = [System.IO.File]::ReadAllBytes($plugOutputFile) } catch { $plugRaw = [byte[]]::new(0) }
    }
}
Write-Host "  Plug output: $($plugRaw.Length) bytes"

# The plug outputs: [wire bytes][text: WCET lines + "WIRE-END\n"] per test
# We need to find wire protocol boundaries. Each wire starts with a valid
# code-len (i32) and is followed by text lines ending with WIRE-END.
$ppos = 0; $plugIdx = 0
$wireBlocks = [System.Collections.Generic.Dictionary[string,byte[]]]::new()
$loadAddr = if ($Arch -eq 'riscv64') { 2147483648 } else { 1074790400 }  # 0x80000000 / 0x40100000

foreach ($name in $plugTestNames) {
    if ($ppos + 12 -gt $plugRaw.Length) { break }
    # Scan forward to find valid wire header (skip text preamble/diagnostics)
    $scanLimit = [Math]::Min($ppos + 128, $plugRaw.Length - 12)
    $foundWire = $false
    for ($scan = $ppos; $scan -le $scanLimit; $scan++) {
        $codeLen = [BitConverter]::ToInt32($plugRaw, $scan)
        $dataLen = [BitConverter]::ToInt32($plugRaw, $scan + 4)
        $funcCount = [BitConverter]::ToInt32($plugRaw, $scan + 8)
        if ($codeLen -gt 100 -and $codeLen -lt 16000000 -and $dataLen -ge 0 -and $dataLen -lt 1000000 -and $funcCount -gt 0 -and $funcCount -lt 10000) {
            $ppos = $scan; $foundWire = $true; break
        }
    }
    if (-not $foundWire) {
        # Skip past WIRE-END marker
        while ($ppos -lt $plugRaw.Length) {
            if ($ppos + 8 -le $plugRaw.Length) {
                $chunk = [Text.Encoding]::UTF8.GetString($plugRaw, $ppos, [Math]::Min(8, $plugRaw.Length - $ppos))
                if ($chunk.StartsWith("WIRE-END")) { $ppos += 10; break }
            }
            $ppos++
        }
        $plugIdx++; continue
    }

    # Calculate wire block size
    $wireDataStart = $ppos + 12
    $funcTableStart = $wireDataStart + $codeLen + $dataLen
    $ftPos = $funcTableStart
    for ($fi = 0; $fi -lt $funcCount -and $ftPos + 2 -lt $plugRaw.Length; $fi++) {
        $nameLen = [BitConverter]::ToInt16($plugRaw, $ftPos)
        $ftPos += 2 + $nameLen + 4
    }
    $wireEnd = $ftPos
    $wireSize = $wireEnd - $ppos

    if ($wireSize -gt 0 -and $wireEnd -le $plugRaw.Length) {
        $wireBytes = New-Object byte[] $wireSize
        [Array]::Copy($plugRaw, $ppos, $wireBytes, 0, $wireSize)
        $wireBlocks[$name] = $wireBytes
    }
    $ppos = $wireEnd

    # Skip past WIRE-END text and any WCET/diagnostic lines
    while ($ppos -lt $plugRaw.Length) {
        if ($plugRaw[$ppos] -eq 10) { $ppos++; continue }
        # Check if next bytes look like a wire header (valid code-len)
        if ($ppos + 12 -le $plugRaw.Length) {
            $nextCl = [BitConverter]::ToInt32($plugRaw, $ppos)
            if ($nextCl -gt 0 -and $nextCl -lt 16000000) { break }
        }
        # Skip this text line
        while ($ppos -lt $plugRaw.Length -and $plugRaw[$ppos] -ne 10) { $ppos++ }
        if ($ppos -lt $plugRaw.Length) { $ppos++ }
    }
    $plugIdx++
}

Write-Host "  Wire blocks: $($wireBlocks.Count) / $($plugTestNames.Count)"
Remove-Item -Force $plugInputFile, $plugOutputFile, $plugStderrFile -ErrorAction SilentlyContinue

# ---- Phase 3: Build ELF64 from wire data ----
Write-Host "--- Phase 3: ELF assembly ($($wireBlocks.Count) tests) ---"
$elfStart = Get-Date
$elfCount = 0

foreach ($name in $plugTestNames) {
    if (-not $wireBlocks.ContainsKey($name)) { continue }
    $testOut = Join-Path $OutRoot $name
    $wireBytes = $wireBlocks[$name]

    $codeLen = [BitConverter]::ToInt32($wireBytes, 0)
    $dataLen = [BitConverter]::ToInt32($wireBytes, 4)
    $funcCount = [BitConverter]::ToInt32($wireBytes, 8)

    $codeStart = 12
    $dataStart2 = $codeStart + $codeLen
    $code = New-Object byte[] $codeLen
    [Array]::Copy($wireBytes, $codeStart, $code, 0, $codeLen)
    $data = New-Object byte[] $dataLen
    if ($dataLen -gt 0) { [Array]::Copy($wireBytes, $dataStart2, $data, 0, $dataLen) }

    $funcOff = $dataStart2 + $dataLen
    $entryOffset = 0
    $funcEntries = [System.Collections.Generic.List[PSObject]]::new()
    for ($fi = 0; $fi -lt $funcCount -and $funcOff + 2 -lt $wireBytes.Length; $fi++) {
        $nameLen = [BitConverter]::ToInt16($wireBytes, $funcOff)
        $sb = [System.Text.StringBuilder]::new($nameLen)
        for ($ci = 0; $ci -lt $nameLen; $ci++) {
            $cce = $wireBytes[$funcOff + 2 + $ci]
            if ($cce -lt $script:CceToUnicode.Length) { [void]$sb.Append([char]$script:CceToUnicode[$cce]) }
            else { [void]$sb.Append('?') }
        }
        $fname = $sb.ToString()
        $foff = [BitConverter]::ToInt32($wireBytes, $funcOff + 2 + $nameLen)
        $funcEntries.Add([PSCustomObject]@{ Name=$fname; Offset=$foff })
        $funcOff += 2 + $nameLen + 4
    }
    if ($funcEntries.Count -gt 0) { $entryOffset = $funcEntries[0].Offset }

    # Build ELF64
    $headerSize = 64; $phdrSize = 56; $phdrCount = 1
    $headersEnd = $headerSize + $phdrSize * $phdrCount
    $textStart = [int](($headersEnd + 15) -band 0xFFFFFFF0)
    $textEnd = $textStart + $codeLen
    $rodataStart = [int](($textEnd + 7) -band 0xFFFFFFF8)
    [uint64]$entry = $loadAddr + [uint64]$textStart + [uint64]$entryOffset
    $segFilesz = $rodataStart + $dataLen - $textStart
    $segMemsz = $segFilesz + 0x0F000000
    $emach = if ($Arch -eq 'riscv64') { 243 } else { 183 }

    $elf = [System.IO.MemoryStream]::new()
    $bw = [System.IO.BinaryWriter]::new($elf)
    $bw.Write([byte[]]@(0x7F,0x45,0x4C,0x46))
    $bw.Write([byte]2); $bw.Write([byte]1); $bw.Write([byte]1); $bw.Write([byte[]]::new(9))
    $bw.Write([uint16]2); $bw.Write([uint16]$emach); $bw.Write([uint32]1)
    $bw.Write([uint64]$entry); $bw.Write([uint64]$headerSize); $bw.Write([uint64]0)
    $bw.Write([uint32]0); $bw.Write([uint16]$headerSize); $bw.Write([uint16]$phdrSize)
    $bw.Write([uint16]$phdrCount); $bw.Write([uint16]0); $bw.Write([uint16]0); $bw.Write([uint16]0)
    $bw.Write([uint32]1); $bw.Write([uint32]7)
    $bw.Write([uint64]$textStart); $bw.Write([uint64]($loadAddr + [uint64]$textStart))
    $bw.Write([uint64]($loadAddr + [uint64]$textStart))
    $bw.Write([uint64]$segFilesz); $bw.Write([uint64]$segMemsz); $bw.Write([uint64]0x1000)
    $padding = $textStart - $headersEnd
    if ($padding -gt 0) { $bw.Write([byte[]]::new($padding)) }
    $bw.Write($code)
    $rodataPad = $rodataStart - $textEnd
    if ($rodataPad -gt 0) { $bw.Write([byte[]]::new($rodataPad)) }
    $bw.Write($data)
    $bw.Flush()
    [System.IO.File]::WriteAllBytes((Join-Path $testOut "$name.elf"), $elf.ToArray())
    $bw.Close()

    # Flat binary
    $flatData = New-Object byte[] ($codeLen + ($rodataStart - $textEnd) + $dataLen)
    [Array]::Copy($code, 0, $flatData, 0, $codeLen)
    if ($dataLen -gt 0) { [Array]::Copy($data, 0, $flatData, $codeLen + ($rodataStart - $textEnd), $dataLen) }
    [System.IO.File]::WriteAllBytes((Join-Path $testOut "$name.bin"), $flatData)

    # Symbol map
    $mapLines = [System.Collections.Generic.List[string]]::new()
    $mapLines.Add('# RISC-V Symbol Map')
    $mapLines.Add('# Address         Size  Name')
    for ($mi = 0; $mi -lt $funcEntries.Count; $mi++) {
        $fe = $funcEntries[$mi]
        [uint64]$addr = $loadAddr + [uint64]$textStart + [uint64]$fe.Offset
        $nextOff = if ($mi + 1 -lt $funcEntries.Count) { $funcEntries[$mi + 1].Offset } else { $codeLen }
        $mapLines.Add("0x$($addr.ToString('X8').PadLeft(8,'0')) $($nextOff - $fe.Offset) $($fe.Name)")
    }
    [System.IO.File]::WriteAllLines((Join-Path $testOut "$name.map"), $mapLines)

    "0" | Set-Content -Path (Join-Path $testOut '.exitcode') -Encoding UTF8
    $elfCount++
}

$elfEnd = Get-Date
Write-Host "  ELF assembly: $([math]::Round(($elfEnd - $elfStart).TotalSeconds, 1))s ($elfCount files)"
Write-Host "=== Batch cross-compile done: $elfCount / $($testNames.Count) ==="
