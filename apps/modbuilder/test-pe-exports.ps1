# The Codex PE export reader (codex/plugs/pe/PeExports.codex) against dumpbin, an
# independent reader, over real system DLLs attached to codex-vm as a disk.
#   pwsh apps/modbuilder/test-pe-exports.ps1 [-MsvcRoot <dir>] [-Kernel <cdx>]
# Exit 0 = every arm passed. The DLLs are read from this machine's System32 and
# never copied into the depot.
[CmdletBinding()]
param([string]$MsvcRoot = '', [string]$Kernel = '')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (-not $Kernel) { $Kernel = Join-Path $repo 'seed/Codex.cdx' }
if (-not $MsvcRoot) {
    $prof = Join-Path $repo 'build-output/prism-targets/valheim-local.prism-target.json'
    if (Test-Path -LiteralPath $prof) { $MsvcRoot = (Get-Content -LiteralPath $prof -Raw | ConvertFrom-Json).msvcRoot }
}
$dumpbin = Join-Path $MsvcRoot 'bin/Hostx64/x64/dumpbin.exe'
if (-not (Test-Path -LiteralPath $dumpbin -PathType Leaf)) { throw "dumpbin not found under -MsvcRoot '$MsvcRoot'" }

$work = Join-Path ([IO.Path]::GetTempPath()) ('pe-exports-' + [guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Force -Path $work)
$fails = 0
function Arm([string]$Name, [bool]$Pass, [string]$Detail = '') { Write-Host ('  {0}  {1}{2}' -f $(if ($Pass) { 'ok  ' } else { 'FAIL' }), $Name, $(if ($Detail) { ': ' + $Detail } else { '' })); if (-not $Pass) { $script:fails++ } }
function Write-Disk([byte[]]$Bytes, [string]$Path) { $pad = New-Object byte[] ([math]::Ceiling($Bytes.Length / 512) * 512); [Array]::Copy($Bytes, $pad, $Bytes.Length); [IO.File]::WriteAllBytes($Path, $pad) }
function Read-Probe([string]$Disk) {
    $out = Join-Path $work ([IO.Path]::GetFileNameWithoutExtension($Disk) + '.out')
    & pwsh -NoProfile -File (Join-Path $repo 'build/test-run.ps1') -Kernel $probe -OutFile $out -DiskFile $Disk | Out-Null
    return @(Get-Content -LiteralPath $out)
}
function Read-Dumpbin([string]$Dll) {
    $map = @{}
    foreach ($l in @(& $dumpbin /nologo /exports $Dll)) {
        if ($l -match '^\s+(\d+)\s+[0-9A-F]+\s+(?:[0-9A-F]{8}\s+)?([A-Za-z_?@$][^\s(]*)(?:\s+\(forwarded to (\S+)\))?\s*$') { $map[[int]$Matches[1]] = @{ name = $Matches[2]; fwd = $(if ($Matches[3]) { $Matches[3] } else { '' }) } }
    }
    return $map
}
function Compare-Exports([string]$Label, [string]$Dll) {
    $disk = Join-Path $work ($Label + '.disk'); Write-Disk ([IO.File]::ReadAllBytes($Dll)) $disk
    $lines = Read-Probe $disk
    $mine = @{}
    foreach ($l in $lines) { if ($l -match '^(\d+) (\S+)(?: -> (\S+))?$') { $mine[[int]$Matches[1]] = @{ name = $Matches[2]; fwd = $(if ($Matches[3]) { $Matches[3] } else { '' }) } } }
    $theirs = Read-Dumpbin $Dll
    $bad = @(foreach ($k in $theirs.Keys) { if (-not $mine.ContainsKey($k) -or $mine[$k].name -cne $theirs[$k].name -or $mine[$k].fwd -cne $theirs[$k].fwd) { "$k" } })
    $extra = @(foreach ($k in $mine.Keys) { if (-not $theirs.ContainsKey($k)) { "$k" } })
    $fwd = @($theirs.Values | Where-Object { $_.fwd }).Count
    Arm "$Label exports equal dumpbin's by ordinal, name and forwarder ($($theirs.Count) named, $fwd forwarded)" ($theirs.Count -gt 0 -and $bad.Count -eq 0 -and $extra.Count -eq 0 -and ($lines -contains 'END')) ("mismatch " + ($bad -join ',') + " extra " + ($extra -join ','))
    return $fwd
}

try {
    $probe = Join-Path $work 'probe.cdx'
    & pwsh -NoProfile -File (Join-Path $repo 'build/compile.ps1') -Src (Join-Path $PSScriptRoot 'pe-exports-probe.codex') -Out $probe -Log (Join-Path $work 'probe.log') -Kernel $Kernel | Out-Null
    if (Select-String -LiteralPath (Join-Path $work 'probe.log') -Pattern 'error CDX' -Quiet) { throw 'the probe did not compile; see ' + (Join-Path $work 'probe.log') }
    $system = [Environment]::SystemDirectory
    [void](Compare-Exports 'winhttp' (Join-Path $system 'winhttp.dll'))
    $kfwd = Compare-Exports 'kernel32' (Join-Path $system 'kernel32.dll')
    Arm 'the forwarder arm reached forwarders' ($kfwd -gt 0) "$kfwd forwarded"
    $bytes = [IO.File]::ReadAllBytes((Join-Path $system 'winhttp.dll'))
    $pe = [BitConverter]::ToInt32($bytes, 60); $bytes[$pe] = 81
    $disk = Join-Path $work 'broken.disk'; Write-Disk $bytes $disk
    $lines = Read-Probe $disk
    Arm 'control: a flipped PE signature is refused' (($lines -join ' ') -match 'REFUSED no PE signature') ($lines -join ' ')
} finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Host $(if ($fails) { "$fails arm(s) FAILED" } else { 'all arms passed' })
exit $(if ($fails) { 1 } else { 0 })
