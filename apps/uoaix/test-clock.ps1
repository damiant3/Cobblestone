[CmdletBinding()]
param(
    [string]$Kernel = '',
    [string]$OutDir = '',
    [ValidateSet('none','schedule','work','birth','death')][string]$Sabotage = 'none',
    [switch]$Poison
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (-not $Kernel) { $Kernel = Join-Path $repo 'seed/Codex.cdx' }
$Kernel = (Resolve-Path -LiteralPath $Kernel).Path
if (-not $OutDir) { $OutDir = Join-Path $repo ('build-output/uoaix/clock-' + [guid]::NewGuid().ToString('N')) }
$OutDir = [IO.Path]::GetFullPath($OutDir)
if (Test-Path -LiteralPath $OutDir) { throw 'OutDir must be new; retained evidence is never overwritten' }
[void](New-Item -ItemType Directory -Path $OutDir)
$source = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Townsfolk.codex')).Replace('Chapter: Townsfolk', 'Chapter: Uoaix--Townsfolk')
$mutations = @{
    schedule = @('else if action == 4 then t.tavern', 'else if action == 4 then t.temple')
    work = @('(t.bread + 4)', '(t.bread + 5)')
    birth = @('__record-set child "parent-b" mate.serial', '__record-set child "parent-b" 0')
    death = @('age >= p.death-age', 'age > p.death-age')
}
if ($Sabotage -ne 'none') {
    $before, $after = $mutations[$Sabotage]
    if ($source.IndexOf($before) -lt 0 -or $source.IndexOf($before) -ne $source.LastIndexOf($before)) { throw 'Sabotage target is absent or ambiguous' }
    $source = $source.Replace($before, $after)
}
$unit = Join-Path $OutDir 'clock-unit.codex'
[IO.File]::WriteAllText($unit, $source + "`r`n" + [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'TownClock.codex')))
$receipt = [ordered]@{ kernel = $Kernel; kernelHash = (Get-FileHash -LiteralPath $Kernel).Hash; unitHash = (Get-FileHash -LiteralPath $unit).Hash; sabotage = $Sabotage; poison = [bool]$Poison; compileExit = $null; runExit = $null; passed = $false }
$receiptPath = Join-Path $OutDir 'result.json'
$timer = [Diagnostics.Stopwatch]::StartNew()
try {
    $freeKiB = (Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory
    $receipt.freeKiBBeforeCompile = $freeKiB
    if ($freeKiB -lt 1572864) { throw 'Less than 1.5 GiB free before compile' }
    $cdx = Join-Path $OutDir 'clock.cdx'
    $compileArgs = @('-NoProfile','-File',(Join-Path $repo 'build/compile.ps1'),'-Src',$unit,'-Out',$cdx,'-Log',(Join-Path $OutDir 'compile.log'),'-Kernel',$Kernel)
    if ($Poison) { $compileArgs += '-Poison' }
    & pwsh @compileArgs
    $receipt.compileExit = $LASTEXITCODE
    if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath (Join-Path $OutDir 'compile.log'); throw 'Clock compilation failed' }
    $receipt.artifactHash = (Get-FileHash -LiteralPath $cdx).Hash
    $freeKiB = (Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory
    $receipt.freeKiBBeforeRun = $freeKiB
    if ($freeKiB -lt 1572864) { throw 'Less than 1.5 GiB free before run' }
    $actual = Join-Path $OutDir 'clock.out'
    & pwsh -NoProfile -File (Join-Path $repo 'build/test-run.ps1') -Kernel $cdx -OutFile $actual
    $receipt.runExit = $LASTEXITCODE
    if ($LASTEXITCODE -ne 0) { throw 'Clock guest failed' }
    $lines = [IO.File]::ReadAllLines($actual)
    $lines | ForEach-Object { Write-Output $_ }
    if ($lines -match '\bFAIL\b|!EXC|OUT OF MEMORY') { throw 'Clock assertions failed' }
    if (@($lines | Where-Object { $_ -eq 'UOAIX CLOCK PASS failures=0 hours=720' }).Count -ne 1) { throw 'Missing completion verdict' }
    $daily = @($lines | Where-Object { $_ -match '^DAY ' })
    if ($daily.Count -ne 60) { throw 'Incomplete daily census' }
    foreach ($town in @('Britain','Minoc')) {
        for ($day = 1; $day -le 30; $day++) {
            $row = @($daily | Where-Object { $_ -match "^DAY $day $town " })
            if ($row.Count -ne 1) { throw "Missing or duplicate day $day town $town" }
        }
    }
    $clock = @($lines | Where-Object { $_ -match '^CLOCK ' })
    if ($clock.Count -ne 30 -or @($clock | Where-Object { $_ -notmatch '^CLOCK \d+ heap=0 ok=1$' }).Count) { throw 'Clock retained heap or failed its census' }
    $receipt.passed = $true
} finally {
    $receipt.elapsedSeconds = $timer.Elapsed.TotalSeconds
    [IO.File]::WriteAllText($receiptPath, ($receipt | ConvertTo-Json -Depth 5))
    Write-Output "Evidence: $receiptPath"
}
