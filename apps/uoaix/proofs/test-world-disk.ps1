[CmdletBinding()]
param([string]$Kernel = '', [string]$OutDir = '')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
if (-not $Kernel) { $Kernel = Join-Path $repo 'seed/Codex.cdx' }
$Kernel = (Resolve-Path -LiteralPath $Kernel).Path
if (-not $OutDir) { $OutDir = Join-Path $repo ('build-output/uoaix/disk-' + [guid]::NewGuid().ToString('N')) }
$OutDir = [IO.Path]::GetFullPath($OutDir)
if (Test-Path -LiteralPath $OutDir) { throw 'OutDir must be new' }
[void](New-Item -ItemType Directory -Path $OutDir)
$receipt = [ordered]@{ kernelHash = (Get-FileHash -LiteralPath $Kernel).Hash; runs = @(); passed = $false }
function Admit {
    $free = (Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory
    if ($free -lt 1572864) { throw 'Less than 1.5 GiB free before guest' }
    return $free
}
function Fnv([byte[]]$Bytes, [int]$Skip = -1) {
    [long]$hash = 2166136261
    for ($i=0; $i -lt $Bytes.Length; $i++) {
        if ($Skip -ge 0 -and $i -ge $Skip -and $i -lt $Skip+8) { continue }
        $hash = (($hash -bxor $Bytes[$i]) * 16777619) -band 4294967295
    }
    return $hash
}
function Compile([string]$Name) {
    $free = Admit
    $src = Join-Path $PSScriptRoot "$Name.codex"
    $cdx = Join-Path $OutDir "$Name.cdx"
    & pwsh -NoProfile -File (Join-Path $repo 'build/compile.ps1') -Src $src -Out $cdx -Log (Join-Path $OutDir "$Name.compile.log") -Kernel $Kernel | Out-Host
    if ($LASTEXITCODE -ne 0) { Get-Content (Join-Path $OutDir "$Name.compile.log") | Out-Host; throw "$Name compile failed" }
    return $cdx
}
function Run([string]$Name, [string]$Cdx, [string]$Disk, [string]$Mode, [string]$Expected) {
    $free = Admit
    $actual = Join-Path $OutDir "$Name.raw"
    $err = Join-Path $OutDir "$Name.stderr"
    $argv = @('-kernel', ('"' + $Cdx + '"'), '-disk', ('"' + $Disk + '"'), '-output', ('"' + $actual + '"'), '-mem','3072','-headless')
    if ($Mode) {
        $inputPath = Join-Path $OutDir "$Name.input"
        [IO.File]::WriteAllText($inputPath, $Mode + "`n")
        $argv += @('-input', ('"' + $inputPath + '"'))
    }
    $p = Start-Process -FilePath (Join-Path $repo 'tools/codex-vm.exe') -ArgumentList $argv -WindowStyle Hidden -PassThru -RedirectStandardError $err
    try {
        if (-not $p.WaitForExit(30000)) { throw "$Name timed out" }
        $run = [ordered]@{ name=$Name; pid=$p.Id; freeKiB=$free; exit=$p.ExitCode; artifactHash=(Get-FileHash -LiteralPath $Cdx).Hash; diskHash=(Get-FileHash -LiteralPath $Disk).Hash }
        $receipt.runs += $run
        $stderr = [IO.File]::ReadAllText($err)
        if ($p.ExitCode -ne 1 -or $stderr -notmatch 'FINAL: debug_exit_code=0 process_exit=1') { throw "$Name did not exit normally: $stderr" }
        if ($stderr -match 'DROPPED|cannot be opened for write|every write.*LOST|(?:nopath|nodata|oob|openfail)=[1-9]') { throw "$Name lost output or disk writes: $stderr" }
        $raw = [IO.File]::ReadAllText($actual) -replace "`r", ''
        $lines = @($raw -split "`n" | Where-Object { $_ -notmatch '^(HEAP:|WD:|STACK:)' })
        $body = (($lines -join "`n").TrimEnd([char]10)) + "`n"
        [IO.File]::WriteAllText((Join-Path $OutDir "$Name.actual"),$body)
        if ($body -cne $Expected) { Write-Output $body; throw "$Name exact oracle mismatch" }
        Write-Output "PASS $Name"
    } finally {
        if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
        $p.Dispose()
    }
}
try {
    $disk = Join-Path $OutDir 'world.disk'
    $f = [IO.File]::Open($disk,[IO.FileMode]::CreateNew,[IO.FileAccess]::ReadWrite)
    try { $f.SetLength(32768) } finally { $f.Dispose() }
    $write = Compile 'WorldDiskWrite'
    $expectedWrite = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'WorldDiskWrite.expected')) -replace "`r", ''
    Run 'write' $write $disk '' $expectedWrite
    $fixture = [IO.File]::ReadAllBytes($disk)
    if ($fixture[11*512] -ne 99) { throw 'Pending payload fixture was not written' }
    for ($i=10*512; $i -lt 11*512; $i++) { if ($fixture[$i] -ne 0) { throw 'Pending commit sector is not empty' } }
    $read = Compile 'WorldDiskRead'
    $expectedRead = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'WorldDiskRead.expected')) -replace "`r", ''
    Run 'reboot' $read $disk 'clean' $expectedRead
    $tail = Join-Path $OutDir 'torn-header.disk'
    [IO.File]::Copy($disk,$tail)
    $f = [IO.File]::OpenWrite($tail)
    try { $f.Position = 10*512; $f.WriteByte(85) } finally { $f.Dispose() }
    Run 'torn-header' $read $tail 'tail' $expectedRead
    $corrupt = Join-Path $OutDir 'corrupt-payload.disk'
    [IO.File]::Copy($disk,$corrupt)
    $f = [IO.File]::Open($corrupt,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite)
    try { $f.Position = 512; $value = $f.ReadByte(); $f.Position = 512; $f.WriteByte($value -bxor 1) } finally { $f.Dispose() }
    Run 'committed-corruption' $read $corrupt 'corrupt' "PASS committed corruption refused`n"
    $foreign = Join-Path $OutDir 'foreign-header.disk'
    [IO.File]::Copy($disk,$foreign)
    $f = [IO.File]::OpenWrite($foreign)
    try { $f.Position = 0; $f.WriteByte(0) } finally { $f.Dispose() }
    Run 'foreign-header' $read $foreign 'foreign' "PASS unrecognized initial store refused`n"
    $metadata = Join-Path $OutDir 'mismatched-metadata.disk'
    $bytes = [IO.File]::ReadAllBytes($disk)
    [BitConverter]::GetBytes([long]199).CopyTo($bytes,7*512+24)
    $payload = [byte[]]$bytes[(7*512)..(7*512+319)]
    [BitConverter]::GetBytes([long](Fnv $payload 48)).CopyTo($bytes,7*512+48)
    $payload = [byte[]]$bytes[(7*512)..(7*512+319)]
    [BitConverter]::GetBytes([long](Fnv $payload)).CopyTo($bytes,6*512+56)
    $header = [byte[]]$bytes[(6*512)..(6*512+63)]
    [BitConverter]::GetBytes([long](Fnv $header 48)).CopyTo($bytes,6*512+48)
    [IO.File]::WriteAllBytes($metadata,$bytes)
    Run 'metadata-binding' $read $metadata 'metadata' "PASS committed prefix recovered`nPASS payload metadata mismatch refused`n"
    if ($receipt.runs.Count -ne 6) { throw 'Incomplete disk proof' }
    $receipt.passed = $true
} finally {
    [IO.File]::WriteAllText((Join-Path $OutDir 'result.json'),($receipt | ConvertTo-Json -Depth 6))
    Write-Output "Evidence: $OutDir"
}
