[CmdletBinding()]
param([string]$Kernel = '', [string]$OutDirectory = '', [string]$NativeProbe = '')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))

if ($NativeProbe) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ForwardProbe {
    [DllImport("kernel32", CharSet=CharSet.Unicode, SetLastError=true)] public static extern IntPtr LoadLibraryW(string path);
    [DllImport("kernel32", CharSet=CharSet.Ansi, ExactSpelling=true)] public static extern IntPtr GetProcAddress(IntPtr module, string name);
    [DllImport("kernel32", EntryPoint="GetProcAddress", ExactSpelling=true)] public static extern IntPtr GetOrdinal(IntPtr module, IntPtr ordinal);
    [DllImport("kernel32")] public static extern uint SetErrorMode(uint mode);
    [DllImport("kernel32", SetLastError=true)] public static extern IntPtr VirtualAlloc(IntPtr address, UIntPtr size, uint allocation, uint protect);
    [DllImport("kernel32")] public static extern bool VirtualFree(IntPtr address, UIntPtr size, uint type);
    [DllImport("kernel32")] public static extern bool FreeLibrary(IntPtr module);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] public delegate int CheckPlatform();
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet=CharSet.Unicode)] public delegate IntPtr Open(string agent, uint access, string proxy, string bypass, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] public delegate int Close(IntPtr handle);
}
'@
    [void][ForwardProbe]::SetErrorMode(3)
    $reserved = [ForwardProbe]::VirtualAlloc([IntPtr]::new(0x180000000), [UIntPtr]::new(0x100000), 0x2000, 1)
    $module = [ForwardProbe]::LoadLibraryW($NativeProbe)
    if ($module -eq [IntPtr]::Zero) { throw "LoadLibrary failed: $([Runtime.InteropServices.Marshal]::GetLastWin32Error())" }
    if ($module.ToInt64() -eq 0x180000000) { throw 'DLL did not rebase' }
    $address = [ForwardProbe]::GetProcAddress($module, 'WinHttpCheckPlatform')
    if ($address -eq [IntPtr]::Zero) { throw 'WinHttpCheckPlatform missing' }
    $check = [Runtime.InteropServices.Marshal]::GetDelegateForFunctionPointer($address, [ForwardProbe+CheckPlatform])
    if ($check.Invoke() -ne 1 -or $check.Invoke() -ne 1) { throw 'WinHttpCheckPlatform did not return TRUE twice' }
    $open = [Runtime.InteropServices.Marshal]::GetDelegateForFunctionPointer([ForwardProbe]::GetProcAddress($module, 'WinHttpOpen'), [ForwardProbe+Open])
    $close = [Runtime.InteropServices.Marshal]::GetDelegateForFunctionPointer([ForwardProbe]::GetProcAddress($module, 'WinHttpCloseHandle'), [ForwardProbe+Close])
    $handle = $open.Invoke('Codex forwarding probe', 1, $null, $null, 0)
    if ($handle -eq [IntPtr]::Zero -or $close.Invoke($handle) -ne 1) { throw 'WinHttpOpen/CloseHandle forwarding failed' }
    [void][ForwardProbe]::FreeLibrary($module)
    if ($reserved -ne [IntPtr]::Zero) { [void][ForwardProbe]::VirtualFree($reserved, [UIntPtr]::Zero, 0x8000) }
    Write-Output 'PASS rebased DLL: WinHttpCheckPlatform TRUE twice; WinHttpOpen/CloseHandle'
    exit 0
}

if (-not $Kernel) { $Kernel = Join-Path $repo 'seed/Codex.cdx' }
if (-not $OutDirectory) { $OutDirectory = Join-Path $repo 'build-output/pe-forward' }
$OutDirectory = [IO.Path]::GetFullPath($OutDirectory)
[void](New-Item -ItemType Directory -Force -Path $OutDirectory)
function Assert-Arm([bool]$Pass, [string]$Name) {
    if (-not $Pass) { throw "FAIL $Name" }
    Write-Output "PASS $Name"
}
function Assert-Ram {
    $freeKiB = (Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory
    if ($freeKiB -le 1572864) { throw "RAM admission refused: $freeKiB KiB free" }
    Write-Output "RAM admission: $freeKiB KiB free; one guest"
}
function Compile-Probe([string]$Source, [string]$Name) {
    Assert-Ram
    & pwsh -NoProfile -File (Join-Path $repo 'build/compile.ps1') -Src $Source -Out (Join-Path $OutDirectory "$Name.cdx") -Log (Join-Path $OutDirectory "$Name.compile.log") -Kernel $Kernel
    if ($LASTEXITCODE -ne 0) { Get-Content (Join-Path $OutDirectory "$Name.compile.log"); throw "$Name compile failed" }
}
function Write-Disk([string]$Source, [string]$Name) {
    $bytes = [IO.File]::ReadAllBytes($Source)
    $padded = [byte[]]::new(($bytes.Length + 511) -band -512)
    [Array]::Copy($bytes, $padded, $bytes.Length)
    $path = Join-Path $OutDirectory "$Name.disk"
    [IO.File]::WriteAllBytes($path, $padded)
    return $path
}
function Run-Guest([string]$Cdx, [string]$Disk, [string]$Name) {
    Assert-Ram
    $raw = Join-Path $OutDirectory "$Name.raw"
    $err = Join-Path $OutDirectory "$Name.stderr"
    $args = @('-kernel', ('"' + $Cdx + '"'), '-disk', ('"' + $Disk + '"'), '-output', ('"' + $raw + '"'), '-mem', '3072', '-headless')
    $guest = Start-Process (Join-Path $repo 'tools/codex-vm.exe') -WindowStyle Hidden -PassThru -ArgumentList $args -RedirectStandardError $err
    Write-Output "guest PID $($guest.Id): $Name; log $err"
    try {
        if (-not $guest.WaitForExit(60000)) { throw "$Name timed out" }
        $stderr = [IO.File]::ReadAllText($err)
        if ($stderr -match 'DROPPED|HOST CRASH' -or $guest.ExitCode -notin @(0, 1)) { throw "$Name guest failed: $stderr" }
        if ($stderr -notmatch 'debug_exit_code=0') { throw "$Name guest did not exit successfully: $stderr" }
    } finally { if (-not $guest.HasExited) { Stop-Process -Id $guest.Id } }
}
function Read-Exports([string]$Dll, [string]$Name) {
    $disk = Write-Disk $Dll $Name
    Run-Guest (Join-Path $OutDirectory 'exports.cdx') $disk $Name | Out-Host
    $lines = [IO.File]::ReadAllLines((Join-Path $OutDirectory "$Name.raw"))
    if ($lines -notcontains 'END') { throw "$Name export reader incomplete: $lines" }
    $rows = @($lines | Where-Object { $_ -match '^\d+ ' })
    if (-not $rows) { throw "$Name export reader returned no exports" }
    return $rows
}
function Run-Native([string]$Dll, [string]$Name) {
    $stdout = Join-Path $OutDirectory "$Name.stdout"
    $stderr = Join-Path $OutDirectory "$Name.stderr"
    $args = @('-NoProfile', '-File', ('"' + $PSCommandPath + '"'), '-NativeProbe', ('"' + $Dll + '"'))
    $probe = Start-Process pwsh -WindowStyle Hidden -PassThru -ArgumentList $args -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    try {
        if (-not $probe.WaitForExit(30000)) { throw "$Name native probe timed out" }
        return $probe.ExitCode
    } finally { if (-not $probe.HasExited) { Stop-Process -Id $probe.Id } }
}

$bundle = [Text.StringBuilder]::new()
foreach ($item in @(
    @('codex/plugs/common/ByteHelpers.codex','Common--ByteHelpers'),
    @('codex/foreword/core/CCE.codex','Foreword--CCE'),
    @('codex/plugs/pe/PeExports.codex','Pe--PeExports'),
    @('codex/plugs/pe/PeWriter.codex','Pe--PeWriter'),
    @('apps/modbuilder/pe-forward-probe.codex','PeForwardProbe'))) {
    $source = [IO.File]::ReadAllText((Join-Path $repo $item[0]))
    $source = [regex]::Replace($source, '(?m)^Chapter:.*$', ('Chapter: ' + $item[1]))
    [void]$bundle.AppendLine($source)
}
$unit = Join-Path $OutDirectory 'forward-unit.codex'
[IO.File]::WriteAllText($unit, $bundle.ToString(), [Text.UTF8Encoding]::new($false))
Compile-Probe $unit 'writer'
Compile-Probe (Join-Path $PSScriptRoot 'pe-exports-probe.codex') 'exports'
$system = Join-Path ([Environment]::SystemDirectory) 'winhttp.dll'
$systemDisk = Write-Disk $system 'system'
foreach ($name in @('first','second')) {
    Run-Guest (Join-Path $OutDirectory 'writer.cdx') $systemDisk $name
    $raw = [IO.File]::ReadAllBytes((Join-Path $OutDirectory "$name.raw"))
    $nl = [Array]::IndexOf($raw, [byte]10)
    if ($nl -lt 0 -or $nl -gt 40) { throw 'Missing writer size header' }
    $header = [Text.Encoding]::ASCII.GetString($raw, 0, $nl).TrimEnd("`r")
    if ($header -notmatch '^SIZE:(\d+)$') { throw "Writer refused: $header" }
    $size = [int]$Matches[1]
    if ($size -lt 512 -or $raw.Length -lt $nl + 1 + $size) { throw 'Truncated writer output' }
    $tail = [Text.Encoding]::ASCII.GetString($raw, $nl + 1 + $size, $raw.Length - $nl - 1 - $size)
    if ($tail -notmatch '^END\r?\n') { throw "Writer completion missing: $tail" }
    $dllBytes = [byte[]]::new($size)
    [Array]::Copy($raw, $nl + 1, $dllBytes, 0, $size)
    [IO.File]::WriteAllBytes((Join-Path $OutDirectory "$name.dll"), $dllBytes)
}
$goodDirectory = Join-Path $OutDirectory 'good'
$badDirectory = Join-Path $OutDirectory 'bad'
[void](New-Item -ItemType Directory -Force -Path $goodDirectory,$badDirectory)
$good = Join-Path $goodDirectory 'winhttp.dll'
Copy-Item -LiteralPath (Join-Path $OutDirectory 'first.dll') -Destination $good -Force
Assert-Arm ((Get-FileHash $good).Hash -eq (Get-FileHash (Join-Path $OutDirectory 'second.dll')).Hash) 'same inputs produce identical DLL bytes'
$expected = @(Read-Exports $system 'system-exports' | ForEach-Object { $_ -replace ' -> .*$', '' })
$actual = @(Read-Exports $good 'written-exports')
Assert-Arm ([string]::Equals(($expected -join "`n"), ($actual -join "`n"), [StringComparison]::Ordinal)) 'PeExports names and ordinals equal System32; no forwarder strings'
Assert-Arm ((Run-Native $good 'good-native') -eq 0) 'hosted forwarding and rebasing'
Get-Content (Join-Path $OutDirectory 'good-native.stdout')
$broken = [IO.File]::ReadAllBytes($good)
$pe = [BitConverter]::ToInt32($broken, 60)
$opt = $pe + 24
Assert-Arm (([BitConverter]::ToUInt16($broken,$pe+22) -band 0x2000) -ne 0) 'IMAGE_FILE_DLL'
Assert-Arm (([BitConverter]::ToUInt16($broken,$opt+70) -band 0x40) -ne 0) 'DYNAMIC_BASE'
$sections = $opt + [BitConverter]::ToUInt16($broken,$pe+20)
$textRaw = [BitConverter]::ToInt32($broken,$sections+20)
$relocRaw = [BitConverter]::ToInt32($broken,$sections+3*40+20)
Assert-Arm ([BitConverter]::ToInt32($broken,$relocRaw) -eq 0 -and [BitConverter]::ToInt32($broken,$relocRaw+4) -eq 8) 'empty relocation block'
$index = -1
for ($i=0; $i -lt $actual.Length; $i++) { if ($actual[$i] -match '^\d+ WinHttpCheckPlatform$') { $index=$i; break } }
Assert-Arm ($index -ge 0) 'crash control targets WinHttpCheckPlatform'
$init = $textRaw + 9 + $index * 14
Assert-Arm ([Convert]::ToHexString($broken,$init,3) -eq '488D05') 'control reached the DllMain slot initializer'
[Array]::Copy([byte[]]@(0x31,0xC0,0x90,0x90,0x90,0x90,0x90),0,$broken,$init,7)
$bad = Join-Path $badDirectory 'winhttp.dll'
[IO.File]::WriteAllBytes($bad,$broken)
$badExit = Run-Native $bad 'bad-native'
Assert-Arm ($badExit -eq -1073741819) "empty-slot control crashes with access violation (exit $badExit)"
Write-Output ('DLL SHA256: ' + (Get-FileHash $good).Hash)
