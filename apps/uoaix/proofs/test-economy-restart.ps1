[CmdletBinding()]
param([string]$Kernel = '', [string]$OutDir = '')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
if (-not $Kernel) { $Kernel = Join-Path $repo 'seed/Codex.cdx' }
$Kernel = (Resolve-Path -LiteralPath $Kernel).Path
if (-not $OutDir) { $OutDir = Join-Path $repo ('build-output/uoaix/economy-restart-' + [guid]::NewGuid().ToString('N')) }
$OutDir = [IO.Path]::GetFullPath($OutDir)
if (Test-Path -LiteralPath $OutDir) { throw 'OutDir must be new' }
[void](New-Item -ItemType Directory -Path $OutDir)
$receipt = [ordered]@{kernel=(Get-FileHash -LiteralPath $Kernel).Hash;runs=@();passed=$false}
function Admit {
    $free = (Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory
    if ($free -lt 1572864) { throw 'Less than 1.5 GiB free before guest' }
    return $free
}
function Run-Entry([string]$Name,[string]$Disk) {
    $freeCompile = Admit
    $cdx = Join-Path $OutDir "$Name.cdx"
    & pwsh -NoProfile -File (Join-Path $repo 'build/compile.ps1') -Src (Join-Path $PSScriptRoot "$Name.codex") -Out $cdx -Log (Join-Path $OutDir "$Name.compile.log") -Kernel $Kernel | Out-Host
    if ($LASTEXITCODE -ne 0) { Get-Content (Join-Path $OutDir "$Name.compile.log") | Out-Host; throw "$Name compile failed" }
    $freeRun = Admit
    $raw = Join-Path $OutDir "$Name.raw"
    $err = Join-Path $OutDir "$Name.stderr"
    $vmArgs = @('-kernel',('"'+$cdx+'"'),'-disk',('"'+$Disk+'"'),'-output',('"'+$raw+'"'),'-mem','3072','-headless')
    $p = Start-Process -FilePath (Join-Path $repo 'tools/codex-vm.exe') -ArgumentList $vmArgs -WindowStyle Hidden -PassThru -RedirectStandardError $err
    try {
        if (-not $p.WaitForExit(60000)) { throw "$Name timed out" }
        $receipt.runs += [ordered]@{name=$Name;pid=$p.Id;exit=$p.ExitCode;freeKiBCompile=$freeCompile;freeKiBRun=$freeRun;artifact=(Get-FileHash -LiteralPath $cdx).Hash;disk=(Get-FileHash -LiteralPath $Disk).Hash}
        $stderr = [IO.File]::ReadAllText($err)
        if ($p.ExitCode -ne 1 -or $stderr -notmatch 'FINAL: debug_exit_code=0 process_exit=1') { throw "$Name guest did not exit normally: $stderr" }
        if ($stderr -match 'DROPPED|cannot be opened for write|every write.*LOST|(?:nopath|nodata|oob|openfail)=[1-9]') { throw "$Name lost output or disk writes: $stderr" }
        $text = [IO.File]::ReadAllText($raw) -replace "`r",''
        $body = ((($text -split "`n" | Where-Object {$_ -notmatch '^(HEAP:|WD:|STACK:)'}) -join "`n").TrimEnd([char]10)) + "`n"
        [IO.File]::WriteAllText((Join-Path $OutDir "$Name.actual"),$body)
        $expected = [IO.File]::ReadAllText((Join-Path $PSScriptRoot "$Name.expected")) -replace "`r",''
        $body | Write-Output
        if ($body -cne $expected) { throw "$Name exact oracle mismatch" }
    } finally {
        if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
        $p.Dispose()
    }
}
try {
    $disk = Join-Path $OutDir 'economy-state.disk'
    $file = [IO.File]::Open($disk,[IO.FileMode]::CreateNew,[IO.FileAccess]::ReadWrite)
    try { $file.SetLength(4194304) } finally { $file.Dispose() }
    Run-Entry 'EconomyRestartWrite' $disk
    Run-Entry 'EconomyRestartRead' $disk
    if ($receipt.runs.Count -ne 2) { throw 'Incomplete restart proof' }
    if ($receipt.runs[0].disk -ne $receipt.runs[1].disk) { throw 'Reader changed the persisted image' }
    $receipt.passed = $true
} finally {
    [IO.File]::WriteAllText((Join-Path $OutDir 'result.json'),($receipt | ConvertTo-Json -Depth 5))
    Write-Output "Evidence: $OutDir/result.json"
}
