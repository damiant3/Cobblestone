# Fast cross-architecture test battery.
#
# Chunked batch compile (IR REPL + plug REPL) + QEMU runtime.
# Safe for 2 agents to run simultaneously on a 32GB host:
#   - Max 1 seed VM (3GB) + 1 plug VM (3GB) + 4 QEMU (256MB each) per agent
#   - Peak: ~8GB per agent, ~16GB for 2 agents
#
# Usage:
#   build/test-cross-fast.ps1 -Arch riscv64              # full battery
#   build/test-cross-fast.ps1 -Arch riscv64 -Filter vec  # filter by name
#   build/test-cross-fast.ps1 -Arch riscv64 -ChunkSize 30
[CmdletBinding()]
param(
    [ValidateSet('arm64','riscv64')]
    [string]$Arch = 'riscv64',
    [int]$ChunkSize = 40,
    [int]$QemuJobs = 4,
    [int]$QemuTimeoutMs = 1500,
    [string]$Filter = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $Repo
[Environment]::CurrentDirectory = $Repo
. (Join-Path $PSScriptRoot 'vm-config.ps1')
. (Join-Path $PSScriptRoot 'quire-map.ps1')

$plugName = if ($Arch -eq 'riscv64') { 'riscv' } else { 'arm64' }
$testDir = Join-Path $Repo 'codex\test'
$outRoot = Join-Path $Repo "test-output-cross\$Arch"
$plugCdx = Join-Path $Repo "codex\plugs\$plugName\build-output\$plugName-plug.cdx"
$Stage0 = Join-Path $Repo 'build-output\bare-metal\Codex.cdx'
$seedCdx = Join-Path $Repo 'seed\Codex.cdx'

New-Item -ItemType Directory -Force (Split-Path $Stage0) | Out-Null
if (-not (Test-Path $Stage0)) { Copy-Item -Force $seedCdx $Stage0 }
if (-not (Test-Path $plugCdx)) { Write-Error "Plug not built: $plugCdx"; exit 1 }

$qemuExe = if ($Arch -eq 'riscv64') {
    'D:\Program Files\qemu\qemu-system-riscv64.exe'
} else {
    'D:\Program Files\qemu\qemu-system-aarch64.exe'
}
$loadAddr = if ($Arch -eq 'riscv64') { 2147483648 } else { 1074790400 }
$emach = if ($Arch -eq 'riscv64') { 243 } else { 183 }

# ---- Discover tests ----
$allTests = Get-ChildItem "$testDir\*.codex" | Sort-Object Name
$eligible = [System.Collections.Generic.List[object]]::new()
$skipCount = 0

foreach ($tf in $allTests) {
    $name = $tf.BaseName
    $dir = $tf.DirectoryName
    if ($Filter -and $name -notlike "*$Filter*") { continue }
    $skipReason = $null
    if (Test-Path "$dir\$name.skip")    { $skipReason = (Get-Content -TotalCount 1 "$dir\$name.skip") }
    elseif (Test-Path "$dir\$name.slow")    { $skipReason = "slow" }
    elseif (Test-Path "$dir\$name.fatal")   { $skipReason = "fatal" }
    elseif (Test-Path "$dir\$name.failing") { $skipReason = "error test" }
    if ($skipReason) { $skipCount++; continue }
    $eligible.Add(@{ File = $tf; Name = $name; Dir = $dir })
}

$batteryStart = Get-Date
Write-Host "=== $($Arch.ToUpper()) Fast Cross Battery: $($eligible.Count) tests, $skipCount skipped, chunk=$ChunkSize ==="

# ---- Resolve sources ----
function Resolve-Source {
    param([string]$SrcPath)
    $lines = [System.IO.File]::ReadAllLines($SrcPath)
    $seedSeen = @{}; $embPat = '^Chapter:\s*(\w+)--(.+?)\s*$'
    foreach ($l in $lines) { if ($l -match $embPat) { $seedSeen["$($matches[1])::$($matches[2])"] = $true } }
    try { $ordered = Resolve-CiteOrder -RootLines $lines -Repo '.' -SeedSeen $seedSeen }
    catch { return $null }
    $sb = [System.Text.StringBuilder]::new(524288)
    foreach ($l in (Format-CiteChapters -Ordered $ordered)) { [void]$sb.Append($l + "`n") }
    foreach ($l in $lines) { [void]$sb.Append($l + "`n") }
    return $sb.ToString()
}

# ---- Phase 1: Chunked batch IR compile ----
$chunks = [System.Collections.Generic.List[System.Collections.Generic.List[object]]]::new()
$cur = [System.Collections.Generic.List[object]]::new()
foreach ($t in $eligible) {
    $cur.Add($t)
    if ($cur.Count -ge $ChunkSize) { $chunks.Add($cur); $cur = [System.Collections.Generic.List[object]]::new() }
}
if ($cur.Count -gt 0) { $chunks.Add($cur) }

Write-Host "`n--- Phase 1: IR compile ($($chunks.Count) chunks of <=$ChunkSize) ---"
$irStart = Get-Date
$irBlocks = [System.Collections.Generic.Dictionary[string,byte[]]]::new()
$allTestNames = [System.Collections.Generic.List[string]]::new()
$compileFailCount = 0

for ($ci = 0; $ci -lt $chunks.Count; $ci++) {
    $chunk = $chunks[$ci]
    $inputSb = [System.Text.StringBuilder]::new(4194304)
    $chunkNames = [System.Collections.Generic.List[string]]::new()
    foreach ($t in $chunk) {
        $name = $t.Name
        $testOut = Join-Path $outRoot $name
        New-Item -ItemType Directory -Force -Path $testOut | Out-Null
        $resolved = Resolve-Source ($t.File).FullName
        if ($null -eq $resolved) { $compileFailCount++; continue }
        $chunkNames.Add($name)
        $allTestNames.Add($name)
        [void]$inputSb.Append("IR-CCE repl`n")
        [void]$inputSb.Append($resolved)
        [void]$inputSb.Append([char]4)
    }
    if ($chunkNames.Count -eq 0) { continue }

    $inFile = [System.IO.Path]::GetTempFileName()
    [System.IO.File]::WriteAllText($inFile, $inputSb.ToString(), [System.Text.UTF8Encoding]::new($false))
    $outFile = [System.IO.Path]::GetTempFileName()

    $cs = Get-Date
    $proc = Start-Process -FilePath $script:CodexVmBin -ArgumentList @(
        '-kernel', $Stage0, '-input', $inFile, '-output', $outFile, '-mem', '3072', '-headless'
    ) -PassThru -WindowStyle Hidden
    $lastSz = 0; $stab = 0
    while (-not $proc.HasExited) {
        Start-Sleep -Milliseconds 500
        if (((Get-Date) - $cs).TotalSeconds -gt ($chunkNames.Count * 8 + 60)) { break }
        $sz = if (Test-Path $outFile) { (Get-Item $outFile).Length } else { 0 }
        if ($sz -eq $lastSz -and $sz -gt 0) { $stab++ } else { $stab = 0 }
        $lastSz = $sz
        if ($stab -ge 20) { break }
    }
    if (-not $proc.HasExited) { try { Stop-Process -Id $proc.Id -Force } catch {} }
    Start-Sleep -Milliseconds 300

    $raw = [byte[]]::new(0)
    if (Test-Path $outFile) {
        try { $raw = [System.IO.File]::ReadAllBytes($outFile) } catch {
            Start-Sleep -Milliseconds 500
            try { $raw = [System.IO.File]::ReadAllBytes($outFile) } catch {}
        }
    }
    $ce = Get-Date
    $ct = [math]::Round(($ce - $cs).TotalSeconds, 1)

    # Parse SIZE: markers
    $pos = 0; $tidx = 0
    while ($tidx -lt $chunkNames.Count -and $pos -lt $raw.Length) {
        $name = $chunkNames[$tidx]
        :tloop while ($pos -lt $raw.Length) {
            # Read line
            $lstart = $pos
            while ($pos -lt $raw.Length -and $raw[$pos] -ne 10) { $pos++ }
            $lend = $pos; if ($pos -lt $raw.Length) { $pos++ }
            $llen = $lend - $lstart
            if ($llen -gt 0 -and $raw[$lstart + $llen - 1] -eq 13) { $llen-- }
            if ($llen -le 0) { continue }
            $line = [System.Text.Encoding]::UTF8.GetString($raw, $lstart, $llen)
            if ($line.StartsWith('SIZE:')) {
                $bsz = 0
                if ($line.Substring(5) -match '^\d+') { $bsz = [int]$matches[0] }
                if ($bsz -gt 0 -and $pos + $bsz -le $raw.Length) {
                    $irb = New-Object byte[] $bsz
                    [Array]::Copy($raw, $pos, $irb, 0, $bsz)
                    $irBlocks[$name] = $irb
                    $pos = [Math]::Min($pos + $bsz, $raw.Length)
                }
                break tloop
            }
            elseif ($line.StartsWith('CODEGEN-HALTED') -or $line.StartsWith('CODEGEN-ERRORS')) {
                $compileFailCount++
                while ($pos -lt $raw.Length) {
                    $el = ''; $es = $pos
                    while ($pos -lt $raw.Length -and $raw[$pos] -ne 10) { $pos++ }
                    if ($pos -lt $raw.Length) { $pos++ }
                    $el = [System.Text.Encoding]::UTF8.GetString($raw, $es, $pos - $es - 1)
                    if ($el.StartsWith('CODEGEN-HALTED')) { break }
                }
                break tloop
            }
            elseif ($line.StartsWith('STACK:')) { break tloop }
        }
        $tidx++
    }

    Write-Host "  chunk $($ci+1)/$($chunks.Count): $($chunkNames.Count) tests, ${ct}s, $($irBlocks.Count - ($allTestNames.Count - $chunkNames.Count - $compileFailCount)) IR blocks"
    Remove-Item -Force $inFile, $outFile -ErrorAction SilentlyContinue
}

$irEnd = Get-Date
Write-Host "IR compile: $([math]::Round(($irEnd - $irStart).TotalSeconds, 1))s, $($irBlocks.Count) / $($allTestNames.Count) IR blocks"

# ---- Phase 1b: Retry failed tests individually ----
$retryNames = @($allTestNames | Where-Object { -not $irBlocks.ContainsKey($_) })
if ($retryNames.Count -gt 0) {
    Write-Host "`n--- Phase 1b: Individual compile for $($retryNames.Count) failed tests ---"
    $retryStart = Get-Date
    $retryCount = 0
    foreach ($name in $retryNames) {
        $tf = Get-ChildItem $testDir -Filter "$name.codex" -Recurse | Select-Object -First 1
        if (-not $tf) { continue }
        $testOut = Join-Path $outRoot $name
        $irOut = Join-Path $testOut "$name.ir"
        $irLog = Join-Path $testOut "ir-retry.log"
        $proc = Start-Process -FilePath 'pwsh' -ArgumentList @(
            '-NoProfile', '-File', 'build/compile.ps1',
            '-Src', $tf.FullName, '-Out', $irOut, '-Log', $irLog, '-IrCce'
        ) -PassThru -WindowStyle Hidden
        $proc.WaitForExit(30000)
        if (-not $proc.HasExited) { try { Stop-Process -Id $proc.Id -Force } catch {} }
        if ((Test-Path $irOut) -and (Get-Item $irOut).Length -gt 100) {
            $irBlocks[$name] = [System.IO.File]::ReadAllBytes($irOut)
            $retryCount++
        }
    }
    $retryEnd = Get-Date
    Write-Host "  Retried: $retryCount/$($retryNames.Count) in $([math]::Round(($retryEnd - $retryStart).TotalSeconds, 1))s"
    Write-Host "  Total IR: $($irBlocks.Count) / $($allTestNames.Count)"
}

# ---- Phase 2: Chunked batch plug codegen ----
Write-Host "`n--- Phase 2: Plug codegen ---"
$plugStart = Get-Date

$modeHdr = [System.Collections.Generic.List[byte]]::new()
foreach ($ch in "IR-CCE repl".ToCharArray()) { $modeHdr.Add([byte]$script:UnicodeToCce[[int]$ch]) }
$modeHdr.Add([byte]1)
$modeHeaderBytes = $modeHdr.ToArray()

$wireBlocks = [System.Collections.Generic.Dictionary[string,byte[]]]::new()
$plugChunks = [System.Collections.Generic.List[System.Collections.Generic.List[string]]]::new()
$curPlug = [System.Collections.Generic.List[string]]::new()
foreach ($name in $allTestNames) {
    if (-not $irBlocks.ContainsKey($name)) { continue }
    $curPlug.Add($name)
    if ($curPlug.Count -ge $ChunkSize) { $plugChunks.Add($curPlug); $curPlug = [System.Collections.Generic.List[string]]::new() }
}
if ($curPlug.Count -gt 0) { $plugChunks.Add($curPlug) }

for ($pci = 0; $pci -lt $plugChunks.Count; $pci++) {
    $pchunk = $plugChunks[$pci]
    $plugIn = [System.Collections.Generic.List[byte]]::new(4194304)
    foreach ($name in $pchunk) {
        $plugIn.AddRange($modeHeaderBytes)
        $plugIn.AddRange($irBlocks[$name])
        $plugIn.Add([byte]0)
    }
    $pInFile = [System.IO.Path]::GetTempFileName()
    [System.IO.File]::WriteAllBytes($pInFile, $plugIn.ToArray())
    $pOutFile = [System.IO.Path]::GetTempFileName()

    $ps = Get-Date
    $proc = Start-Process -FilePath $script:CodexVmBin -ArgumentList @(
        '-kernel', $plugCdx, '-input', $pInFile, '-output', $pOutFile, '-mem', '3072', '-headless'
    ) -PassThru -WindowStyle Hidden
    $lastSz = 0; $stab = 0
    while (-not $proc.HasExited) {
        Start-Sleep -Milliseconds 500
        if (((Get-Date) - $ps).TotalSeconds -gt ($pchunk.Count * 4 + 30)) { break }
        $sz = if (Test-Path $pOutFile) { (Get-Item $pOutFile).Length } else { 0 }
        if ($sz -eq $lastSz -and $sz -gt 0) { $stab++ } else { $stab = 0 }
        $lastSz = $sz
        if ($stab -ge 20) { break }
    }
    if (-not $proc.HasExited) { try { Stop-Process -Id $proc.Id -Force } catch {} }
    Start-Sleep -Milliseconds 300

    $plugRaw = [byte[]]::new(0)
    if (Test-Path $pOutFile) {
        try { $plugRaw = [System.IO.File]::ReadAllBytes($pOutFile) } catch {
            Start-Sleep -Milliseconds 500
            try { $plugRaw = [System.IO.File]::ReadAllBytes($pOutFile) } catch {}
        }
    }
    $pe = Get-Date

    # Parse wire blocks
    $pp = 0
    foreach ($name in $pchunk) {
        if ($pp + 12 -gt $plugRaw.Length) { break }
        $scanLim = [Math]::Min($pp + 128, $plugRaw.Length - 12)
        $fw = $false
        for ($sc = $pp; $sc -le $scanLim; $sc++) {
            $cl = [BitConverter]::ToInt32($plugRaw, $sc)
            $dl = [BitConverter]::ToInt32($plugRaw, $sc + 4)
            $fc = [BitConverter]::ToInt32($plugRaw, $sc + 8)
            if ($cl -gt 100 -and $cl -lt 16000000 -and $dl -ge 0 -and $dl -lt 1000000 -and $fc -gt 0 -and $fc -lt 10000) {
                $pp = $sc; $fw = $true; break
            }
        }
        if (-not $fw) {
            while ($pp -lt $plugRaw.Length -and $plugRaw[$pp] -ne 10) { $pp++ }
            if ($pp -lt $plugRaw.Length) { $pp++ }
            continue
        }
        $cl = [BitConverter]::ToInt32($plugRaw, $pp)
        $dl = [BitConverter]::ToInt32($plugRaw, $pp + 4)
        $fc = [BitConverter]::ToInt32($plugRaw, $pp + 8)
        $ftp = $pp + 12 + $cl + $dl
        for ($fi = 0; $fi -lt $fc -and $ftp + 2 -lt $plugRaw.Length; $fi++) {
            $nl = [BitConverter]::ToInt16($plugRaw, $ftp)
            $ftp += 2 + $nl + 4
        }
        $wsz = $ftp - $pp
        if ($wsz -gt 0 -and $ftp -le $plugRaw.Length) {
            $wb = New-Object byte[] $wsz
            [Array]::Copy($plugRaw, $pp, $wb, 0, $wsz)
            $wireBlocks[$name] = $wb
        }
        $pp = $ftp
        while ($pp -lt $plugRaw.Length) {
            if ($pp + 12 -le $plugRaw.Length) {
                $ncl = [BitConverter]::ToInt32($plugRaw, $pp)
                if ($ncl -gt 100 -and $ncl -lt 16000000) { break }
            }
            while ($pp -lt $plugRaw.Length -and $plugRaw[$pp] -ne 10) { $pp++ }
            if ($pp -lt $plugRaw.Length) { $pp++ }
        }
    }
    Write-Host "  plug chunk $($pci+1)/$($plugChunks.Count): $($pchunk.Count) tests, $([math]::Round(($pe-$ps).TotalSeconds,1))s"
    Remove-Item -Force $pInFile, $pOutFile -ErrorAction SilentlyContinue
}

$plugEnd = Get-Date
Write-Host "Plug codegen: $([math]::Round(($plugEnd - $plugStart).TotalSeconds, 1))s, $($wireBlocks.Count) wire blocks"

# ---- Phase 3: ELF assembly ----
Write-Host "`n--- Phase 3: ELF assembly ---"
$elfStart = Get-Date
$elfCount = 0

foreach ($name in $allTestNames) {
    if (-not $wireBlocks.ContainsKey($name)) { continue }
    $testOut = Join-Path $outRoot $name
    $wb = $wireBlocks[$name]
    $cl = [BitConverter]::ToInt32($wb, 0)
    $dl = [BitConverter]::ToInt32($wb, 4)
    $fc = [BitConverter]::ToInt32($wb, 8)
    $code = New-Object byte[] $cl
    [Array]::Copy($wb, 12, $code, 0, $cl)
    $data = New-Object byte[] $dl
    if ($dl -gt 0) { [Array]::Copy($wb, 12 + $cl, $data, 0, $dl) }

    $fo = 12 + $cl + $dl; $eo = 0
    $fents = [System.Collections.Generic.List[PSObject]]::new()
    for ($fi = 0; $fi -lt $fc -and $fo + 2 -lt $wb.Length; $fi++) {
        $nl = [BitConverter]::ToInt16($wb, $fo)
        $sb = [System.Text.StringBuilder]::new($nl)
        for ($ci2 = 0; $ci2 -lt $nl; $ci2++) {
            $cc = $wb[$fo + 2 + $ci2]
            if ($cc -lt $script:CceToUnicode.Length) { [void]$sb.Append([char]$script:CceToUnicode[$cc]) } else { [void]$sb.Append('?') }
        }
        $fn = $sb.ToString()
        $foff = [BitConverter]::ToInt32($wb, $fo + 2 + $nl)
        $fents.Add([PSCustomObject]@{Name=$fn;Offset=$foff})
        $fo += 2 + $nl + 4
    }
    if ($fents.Count -gt 0) { $eo = $fents[0].Offset }

    $hs = 64; $phs = 56; $he = $hs + $phs
    $ts = [int](($he + 15) -band 0xFFFFFFF0)
    $te = $ts + $cl
    $rs = [int](($te + 7) -band 0xFFFFFFF8)
    [uint64]$entry = $loadAddr + [uint64]$ts + [uint64]$eo
    $sfs = $rs + $dl - $ts; $sms = $sfs + 0x0F000000

    $elf = [System.IO.MemoryStream]::new()
    $bw = [System.IO.BinaryWriter]::new($elf)
    $bw.Write([byte[]]@(0x7F,0x45,0x4C,0x46))
    $bw.Write([byte]2); $bw.Write([byte]1); $bw.Write([byte]1); $bw.Write([byte[]]::new(9))
    $bw.Write([uint16]2); $bw.Write([uint16]$emach); $bw.Write([uint32]1)
    $bw.Write([uint64]$entry); $bw.Write([uint64]$hs); $bw.Write([uint64]0)
    $bw.Write([uint32]0); $bw.Write([uint16]$hs); $bw.Write([uint16]$phs)
    $bw.Write([uint16]1); $bw.Write([uint16]0); $bw.Write([uint16]0); $bw.Write([uint16]0)
    $bw.Write([uint32]1); $bw.Write([uint32]7)
    $bw.Write([uint64]$ts); $bw.Write([uint64]($loadAddr+[uint64]$ts)); $bw.Write([uint64]($loadAddr+[uint64]$ts))
    $bw.Write([uint64]$sfs); $bw.Write([uint64]$sms); $bw.Write([uint64]0x1000)
    $pad = $ts - $he; if ($pad -gt 0) { $bw.Write([byte[]]::new($pad)) }
    $bw.Write($code)
    $rp = $rs - $te; if ($rp -gt 0) { $bw.Write([byte[]]::new($rp)) }
    $bw.Write($data); $bw.Flush()
    [System.IO.File]::WriteAllBytes((Join-Path $testOut "$name.elf"), $elf.ToArray())
    $bw.Close()

    $flat = New-Object byte[] ($cl + ($rs - $te) + $dl)
    [Array]::Copy($code, 0, $flat, 0, $cl)
    if ($dl -gt 0) { [Array]::Copy($data, 0, $flat, $cl + ($rs - $te), $dl) }
    [System.IO.File]::WriteAllBytes((Join-Path $testOut "$name.bin"), $flat)

    $mapL = [System.Collections.Generic.List[string]]::new()
    $mapL.Add('# Symbol Map'); $mapL.Add('# Address         Size  Name')
    for ($mi = 0; $mi -lt $fents.Count; $mi++) {
        $fe = $fents[$mi]
        [uint64]$addr = $loadAddr + [uint64]$fe.Offset
        $noff = if ($mi+1 -lt $fents.Count) { $fents[$mi+1].Offset } else { $cl }
        $mapL.Add("0x$($addr.ToString('X8').PadLeft(8,'0')) $($noff - $fe.Offset) $($fe.Name)")
    }
    [System.IO.File]::WriteAllLines((Join-Path $testOut "$name.map"), $mapL)
    $elfCount++
}

$elfEnd = Get-Date
Write-Host "ELF assembly: $([math]::Round(($elfEnd - $elfStart).TotalSeconds, 1))s ($elfCount files)"

# ---- Phase 4: QEMU run ----
Write-Host "`n--- Phase 4: QEMU run ($QemuJobs slots, ${QemuTimeoutMs}ms) ---"
$toRun = [System.Collections.Generic.List[hashtable]]::new()
foreach ($name in $allTestNames) {
    $expFile = Join-Path $testDir "$name.expected"
    $binFile = Join-Path $outRoot "$name\$name.bin"
    if ((Test-Path $expFile) -and (Test-Path $binFile)) {
        $toRun.Add(@{ Name=$name; Bin=$binFile; Exp=(Resolve-Path $expFile).Path })
    }
}
Write-Host "$($toRun.Count) tests to run"
$runStart = Get-Date

$runResults = $toRun | ForEach-Object -ThrottleLimit $QemuJobs -Parallel {
    $t = $_; $name = $t.Name; $qemu = $using:qemuExe; $arch = $using:Arch; $outRoot = $using:outRoot
    $tms = $using:QemuTimeoutMs
    $ulog = Join-Path $outRoot "$name\uart-q.log"
    if (Test-Path $ulog) { [IO.File]::Delete($ulog) }
    $elfFile = $t.Bin -replace '\.bin$','.elf'
    $margs = if ($arch -eq 'riscv64') {
        @('-M','virt','-m','3072M','-display','none','-monitor','none','-bios','none','-device',"loader,file=$($t.Bin),addr=0x80000000",'-serial',"file:$ulog")
    } else {
        @('-M','virt','-cpu','cortex-a53','-m','3072M','-display','none','-monitor','none','-kernel',$elfFile,'-serial',"file:$ulog")
    }
    $p = Start-Process -FilePath $qemu -ArgumentList $margs -PassThru -NoNewWindow
    $p.WaitForExit($tms); if (!$p.HasExited) { try{$p.Kill()}catch{} }
    Start-Sleep -Milliseconds 50
    if (!(Test-Path $ulog)) { return [PSCustomObject]@{N=$name;S='FAIL_RUNTIME';R='no output'} }
    try { $raw = [IO.File]::ReadAllText($ulog) -replace "`r",'' } catch { return [PSCustomObject]@{N=$name;S='FAIL_RUNTIME';R='file locked'} }
    $al = @(($raw -split "`n") | Where-Object { $_ -ne '' -and !$_.StartsWith('HEAP:') -and !$_.StartsWith('WD:') -and !$_.StartsWith('STACK:') })
    $exp = [IO.File]::ReadAllText($t.Exp) -replace "`r",''
    $el = @(($exp -split "`n") | Where-Object { $_ -ne '' })
    if ($al.Count -gt $el.Count -and $el.Count -gt 0) { $al = $al[0..($el.Count-1)] }
    $a = ($al -join "`n")+"`n"; $e = ($el -join "`n")+"`n"
    if ($e -eq $a) { [PSCustomObject]@{N=$name;S='PASS_EXPECTED';R=''} } else { [PSCustomObject]@{N=$name;S='FAIL_OUTPUT';R="exp=$($el.Count)L act=$($al.Count)L"} }
}

$runEnd = Get-Date
Write-Host "Run phase: $([math]::Round(($runEnd - $runStart).TotalSeconds, 1))s"

# ---- Tally ----
$passCount = 0; $failCount = 0; $compileOnly = 0
foreach ($rr in $runResults) {
    try { $st = $rr.S } catch { continue }
    if ($null -eq $st) { continue }
    if ($st -eq 'PASS_EXPECTED') { $passCount++ } else { $failCount++ }
}
$compileOnly = $elfCount - $toRun.Count

$batteryEnd = Get-Date
$totalSec = [math]::Round(($batteryEnd - $batteryStart).TotalSeconds, 1)

Write-Host "`n=== $($Arch.ToUpper()) Fast Cross Battery ==="
Write-Host "  PASS:          $passCount"
Write-Host "  FAIL:          $failCount"
Write-Host "  COMPILE_ONLY:  $compileOnly"
Write-Host "  COMPILE_FAIL:  $compileFailCount"
Write-Host "  SKIPPED:       $skipCount"
Write-Host "  Total:         $totalSec s"

if ($failCount -gt 0) {
    Write-Host "`nFailures:"
    foreach ($rr in $runResults) {
        try { $st = $rr.S; $nm = $rr.N; $rs = $rr.R } catch { continue }
        if ($null -ne $st -and $st -ne 'PASS_EXPECTED') { Write-Host "  $st`: $nm -- $rs" }
    }
}
