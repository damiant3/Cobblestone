[CmdletBinding()]
param([Parameter(Mandatory)][string]$ManagedLibrary, [Parameter(Mandatory)][string]$OutDirectory, [string]$Kernel = '')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$ManagedLibrary = (Resolve-Path -LiteralPath $ManagedLibrary).Path
$OutDirectory = [IO.Path]::GetFullPath($OutDirectory)
if (-not $Kernel) { $Kernel = Join-Path $repo 'seed/Codex.cdx' }
$Kernel = (Resolve-Path -LiteralPath $Kernel).Path
if ((Test-Path -LiteralPath $OutDirectory) -and @(Get-ChildItem -LiteralPath $OutDirectory -Force).Count) { throw 'Package output directory must be empty' }
[void](New-Item -ItemType Directory -Path $OutDirectory -Force)
$managed = [IO.File]::ReadAllBytes($ManagedLibrary)
if ($managed.Length -lt 1 -or $managed.Length -gt 67108864) { throw 'Managed payload outside 1..64 MiB' }
$systemPath = Join-Path ([Environment]::SystemDirectory) 'winhttp.dll'
$system = [IO.File]::ReadAllBytes($systemPath)
$disk = [byte[]]::new((512 + $system.Length + $managed.Length + 511) -band -512)
[Array]::Copy([BitConverter]::GetBytes($system.Length),0,$disk,0,4)
[Array]::Copy([BitConverter]::GetBytes($managed.Length),0,$disk,4,4)
[Array]::Copy($system,0,$disk,512,$system.Length)
[Array]::Copy($managed,0,$disk,512+$system.Length,$managed.Length)
$diskPath = Join-Path $OutDirectory 'package.disk'
[IO.File]::WriteAllBytes($diskPath,$disk)
$bundle = [Text.StringBuilder]::new()
$inputs = @(
    @('codex/plugs/common/ByteHelpers.codex','Common--ByteHelpers'),
    @('codex/foreword/core/CCE.codex','Foreword--CCE'),
    @('codex/plugs/pe/PeExports.codex','Pe--PeExports'),
    @('codex/plugs/pe/PeWriter.codex','Pe--PeWriter'),
    @('codex/plugs/pe/PeBootstrap.codex','Pe--PeBootstrap'),
    @('codex/plugs/pe/PeBootstrapDriver.codex','PeBootstrapDriver'))
foreach ($item in $inputs) {
    $source = [IO.File]::ReadAllText((Join-Path $repo $item[0]))
    [void]$bundle.AppendLine([regex]::Replace($source,'(?m)^Chapter:.*$',('Chapter: '+$item[1])))
}
$unit = Join-Path $OutDirectory 'bootstrap.codex'
$cdx = Join-Path $OutDirectory 'bootstrap.cdx'
$log = Join-Path $OutDirectory 'compile.log'
[IO.File]::WriteAllText($unit,$bundle.ToString(),[Text.UTF8Encoding]::new($false))
function Assert-Ram {
    $free = (Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory
    if ($free -le 1572864) { throw "RAM admission refused: $free KiB free" }
    Write-Host "RAM admission: $free KiB free; one guest"
}
Assert-Ram
& pwsh -NoProfile -File (Join-Path $repo 'build/compile.ps1') -Src $unit -Out $cdx -Log $log -Kernel $Kernel
if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath $log; throw 'Codex bootstrap compilation failed' }
$rawPath = Join-Path $OutDirectory 'writer.raw'
$errPath = Join-Path $OutDirectory 'writer.stderr'
Assert-Ram
$guest = Start-Process (Join-Path $repo 'tools/codex-vm.exe') -WindowStyle Hidden -PassThru -ArgumentList @('-kernel',('"'+$cdx+'"'),'-disk',('"'+$diskPath+'"'),'-output',('"'+$rawPath+'"'),'-mem','3072','-headless') -RedirectStandardError $errPath
Write-Host "Bootstrap writer PID $($guest.Id); log $errPath"
try {
    if (-not $guest.WaitForExit(180000)) { throw 'Bootstrap writer timed out' }
    $stderr = [IO.File]::ReadAllText($errPath)
    if ($guest.ExitCode -notin @(0,1) -or $stderr -match 'DROPPED|HOST CRASH' -or $stderr -notmatch 'debug_exit_code=0') { throw "Bootstrap writer failed: $stderr" }
} finally { if (-not $guest.HasExited) { Stop-Process -Id $guest.Id } }
$raw = [IO.File]::ReadAllBytes($rawPath)
$nl = [Array]::IndexOf($raw,[byte]10)
if ($nl -lt 0 -or $nl -gt 128) { throw 'Bootstrap writer omitted its size header' }
$header = [Text.Encoding]::ASCII.GetString($raw,0,$nl).TrimEnd([char]13)
if ($header -notmatch '^SIZE:(\d+)$') { throw "Bootstrap writer refused: $header" }
$size = [int]$Matches[1]
if ($size -lt 1024 -or $raw.Length -lt $nl + 1 + $size) { throw 'Truncated bootstrap output' }
$tail = [Text.Encoding]::ASCII.GetString($raw,$nl+1+$size,$raw.Length-$nl-1-$size)
if ($tail -notmatch '^END\r?\n') { throw "Bootstrap writer omitted completion: $tail" }
$bytes = [byte[]]::new($size)
[Array]::Copy($raw,$nl+1,$bytes,0,$size)
$dll = Join-Path $OutDirectory 'winhttp.dll'
[IO.File]::WriteAllBytes($dll,$bytes)
$receipt = [ordered]@{
    artifact=$dll; artifactHash=(Get-FileHash -LiteralPath $dll).Hash;
    managedHash=(Get-FileHash -LiteralPath $ManagedLibrary).Hash;
    compilerHash=(Get-FileHash -LiteralPath $Kernel).Hash;
    systemLibrary=$systemPath; systemHash=(Get-FileHash -LiteralPath $systemPath).Hash;
    sources=@($inputs | ForEach-Object { @{path=$_[0];sha256=(Get-FileHash -LiteralPath (Join-Path $repo $_[0])).Hash} });
    runtimeVerified=$false; installed=$false
}
[IO.File]::WriteAllText((Join-Path $OutDirectory 'package.json'),($receipt|ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
"Package: $dll"
