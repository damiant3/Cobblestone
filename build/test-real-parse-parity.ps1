# Compare executed runtime decimal-parser bits against one explicitly named seed.
# -ExpectDivergence grades a preserved old plug; malformed or missing rows never count.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][ValidateSet('zig', 'wasm')][string]$Only,
    [Parameter(Mandatory=$true)][string]$Kernel,
    [Parameter(Mandatory=$true)][string]$PlugCdx,
    [Parameter(Mandatory=$true)][string]$WorkDir,
    [switch]$ExpectDivergence
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Kernel = (Resolve-Path $Kernel).Path
$PlugCdx = (Resolve-Path $PlugCdx).Path
$WorkDir = [IO.Path]::GetFullPath($WorkDir)
if (Test-Path $WorkDir) { throw 'WorkDir must be new: preserved results cannot be overwritten.' }
New-Item -ItemType Directory -Path $WorkDir | Out-Null
$subject = Join-Path $repo 'codex/test/ops/real-parse-parity.codex'
$fixture = [IO.File]::ReadAllText($subject)
$cases = [regex]::Matches($fixture, '(?m)^\s+report "([^"]+)" "([^"]*)"')
if ($cases.Count -eq 0) { throw 'No fixture rows found.' }
$expected = [Collections.Generic.List[string]]::new()
foreach ($case in $cases) {
    $value = $case.Groups[2].Value
    if ($value -ne '' -and $value -notmatch '^-?\d+(\.\d+)?$') { throw "Invalid decimal fixture: $value" }
    if ($value.Contains('.') -and $value.Length - $value.IndexOf('.') - 1 -gt 108) {
        throw "Fixture exceeds the 108 fractional digit parser domain: $($case.Groups[1].Value)"
    }
    $bits = if ($value -eq '') { 0L } else {
        [BitConverter]::DoubleToInt64Bits([double]::Parse($value, [Globalization.CultureInfo]::InvariantCulture))
    }
    $expected.Add($case.Groups[1].Value + ' ' + $bits)
}
[IO.File]::WriteAllLines((Join-Path $WorkDir 'host.expected'), $expected)
$kernelHash = (Get-FileHash $Kernel).Hash
$plugHash = (Get-FileHash $PlugCdx).Hash
Write-Host "kernel: $Kernel [$kernelHash]"
Write-Host "plug: $PlugCdx [$plugHash]"
@{ kernel=$Kernel; kernelSha256=$kernelHash; plug=$PlugCdx; plugSha256=$plugHash;
   fixtureSha256=(Get-FileHash $subject).Hash; rows=$cases.Count; backend=$Only } |
    ConvertTo-Json | Set-Content (Join-Path $WorkDir 'provenance.json')

function Assert-Ram {
    $free = (Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1MB
    Write-Host "RAM: $free GiB free; launching one guest"
    if ($free -le 1.5) { throw 'RAM admission refused: need more than 1.5 GiB free.' }
}
function Invoke-Checked([string]$Name, [string]$Exe, [string[]]$Arguments, [switch]$GuestExit) {
    $log = Join-Path $WorkDir "$Name.log"
    $watch = [Diagnostics.Stopwatch]::StartNew()
    & $Exe @Arguments *> $log
    $rc = $LASTEXITCODE
    $watch.Stop()
    Write-Host "$Name exit=$rc seconds=$($watch.Elapsed.TotalSeconds) log=$log"
    Get-Content $log
    $guestOk = $GuestExit -and $rc -eq 1 -and
        ([IO.File]::ReadAllText($log) -match '(?m)^FINAL: debug_exit_code=0 process_exit=1\r?$')
    if ($rc -ne 0 -and -not $guestOk) { throw "$Name failed with exit $rc" }
}
function Read-Rows([string]$Path) {
    $body = [IO.File]::ReadAllText($Path).Replace("`r", '')
    if (-not $body.EndsWith("`n")) { throw "Missing final newline: $Path" }
    $rows = $body.Substring(0, $body.Length - 1).Split("`n")
    if ($rows.Count -ne $cases.Count) { throw "Wrong row count in ${Path}: $($rows.Count), expected $($cases.Count)" }
    for ($i = 0; $i -lt $rows.Count; $i++) {
        $prefix = $cases[$i].Groups[1].Value + ' '
        if (-not $rows[$i].StartsWith($prefix) -or $rows[$i].Substring($prefix.Length) -notmatch '^-?\d+$') {
            throw "Malformed or reordered row $i in $Path"
        }
        $null = [long]::Parse($rows[$i].Substring($prefix.Length))
    }
    return ,$rows
}
try {
    $cdx = Join-Path $WorkDir 'subject.cdx'
    $ir = Join-Path $WorkDir 'subject.ir'
    Assert-Ram
    Invoke-Checked 'compile-cdx' 'pwsh' @('-NoProfile', '-File', "$PSScriptRoot/compile.ps1", '-Src', $subject, '-Out', $cdx, '-Log', "$WorkDir/compile-cdx.diag", '-Kernel', $Kernel)
    Assert-Ram
    Invoke-Checked 'run-seed' 'pwsh' @('-NoProfile', '-File', "$PSScriptRoot/test-run.ps1", '-Kernel', $cdx, '-OutFile', "$WorkDir/seed.out")
    $truth = Read-Rows "$WorkDir/seed.out"
    $seedDifferences = 0
    for ($i = 0; $i -lt $truth.Count; $i++) {
        if ($truth[$i] -cne $expected[$i]) {
            Write-Host "SEED DIFF seed=$($truth[$i]); host=$($expected[$i])"
            $seedDifferences++
        }
    }
    Assert-Ram
    Invoke-Checked 'compile-ir' 'pwsh' @('-NoProfile', '-File', "$PSScriptRoot/compile.ps1", '-Src', $subject, '-Out', $ir, '-Log', "$WorkDir/compile-ir.diag", '-Kernel', $Kernel, '-IrCce', '-Passes', 'text-plug')
    Assert-Ram
    if ($Only -eq 'zig') {
        Invoke-Checked 'emit' 'pwsh' @('-NoProfile', '-File', "$PSScriptRoot/run-plug.ps1", '-Plug', $PlugCdx, '-InFile', $ir, '-Output', "$WorkDir/subject.zig", '-Port', '9145', '-MemMB', '3072')
        Invoke-Checked 'host-build' 'zig' @('build-exe', "$WorkDir/subject.zig", "-femit-bin=$WorkDir/subject.exe")
        Invoke-Checked 'plug-output' "$WorkDir/subject.exe" @()
    } else {
        . "$PSScriptRoot/vm-config.ps1"
        $hdr = ConvertTo-CceBytes "IR-CCE`n"
        $data = [IO.File]::ReadAllBytes($ir)
        $inputBytes = [byte[]]::new($hdr.Length + $data.Length + 1)
        [Array]::Copy($hdr, 0, $inputBytes, 0, $hdr.Length)
        [Array]::Copy($data, 0, $inputBytes, $hdr.Length, $data.Length)
        [IO.File]::WriteAllBytes("$WorkDir/plug.input", $inputBytes)
        Invoke-Checked 'emit' "$repo/tools/codex-vm.exe" @('-kernel', $PlugCdx, '-input', "$WorkDir/plug.input", '-output', "$WorkDir/plug.raw", '-mem', '3072', '-headless') -GuestExit
        $raw = [IO.File]::ReadAllText("$WorkDir/plug.raw") -replace '^\x01', ''
        $diag = [IO.File]::ReadAllText("$WorkDir/emit.log")
        if (($raw + $diag) -match 'DROPPED|!EXC|OUT OF MEMORY') { throw 'Wasm emission failed or lost bytes.' }
        $wat = ($raw -split "`n" | Where-Object { $_ -notmatch '^(HEAP|WD|STACK|PM):' }) -join "`n"
        [IO.File]::WriteAllText("$WorkDir/subject.wat", $wat)
        Invoke-Checked 'host-build' 'wat2wasm' @('--enable-tail-call', "$WorkDir/subject.wat", '-o', "$WorkDir/subject.wasm")
        Invoke-Checked 'plug-output' 'wasmtime' @('run', "$WorkDir/subject.wasm")
    }
    $got = Read-Rows "$WorkDir/plug-output.log"
    $differences = 0
    $plugHostDifferences = 0
    for ($i = 0; $i -lt $truth.Count; $i++) {
        if ($got[$i] -cne $expected[$i]) { $plugHostDifferences++ }
        if ($got[$i] -cne $truth[$i]) {
            Write-Host "DIFF seed=$($truth[$i]); $Only=$($got[$i])"
            $differences++
        }
    }
    Write-Host "host comparison: seed differences=$seedDifferences; plug differences=$plugHostDifferences"
    @{ rows=$truth.Count; seedHostDifferences=$seedDifferences;
       plugHostDifferences=$plugHostDifferences; seedPlugDifferences=$differences } |
        ConvertTo-Json | Set-Content "$WorkDir/comparison.json"
    if ($seedDifferences -gt 0) { throw "Seed disagrees with host on $seedDifferences rows; plug differs from seed on $differences rows." }
    if ($ExpectDivergence) {
        if ($differences -eq 0) { throw 'Negative control did not diverge.' }
        $verdict = "PASS negative control: $differences/$($truth.Count) rows differ"
    } else {
        if ($differences -gt 0) { throw "$differences/$($truth.Count) rows differ" }
        $verdict = "PASS parity: $($truth.Count)/$($truth.Count) rows agree"
    }
    Write-Host $verdict
    Set-Content "$WorkDir/result.txt" $verdict
} catch {
    Set-Content "$WorkDir/result.txt" "FAIL: $_"
    throw
}
