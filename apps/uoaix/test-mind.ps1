[CmdletBinding()]
param(
    [string]$Kernel = '',
    [string]$OutDir = '',
    [ValidateSet('none','policy','inventory','replay','audit')][string]$Sabotage = 'none',
    [switch]$Poison
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (-not $Kernel) { $Kernel = Join-Path $repo 'seed/Codex.cdx' }
$Kernel = (Resolve-Path -LiteralPath $Kernel).Path
if (-not $OutDir) { $OutDir = Join-Path $repo ('build-output/uoaix/mind-' + [guid]::NewGuid().ToString('N')) }
$OutDir = [IO.Path]::GetFullPath($OutDir)
if (Test-Path -LiteralPath $OutDir) { throw 'OutDir must be new' }
[void](New-Item -ItemType Directory -Path $OutDir)
$source = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'TownMind.codex')).Replace('Chapter: TownMind','Chapter: Uoaix--TownMind')
$mutations = @{
    policy = @('event /= 2 | persona.gift-limit == 0', 'persona.gift-limit == 0')
    inventory = @('tm-stock actor proposal.item < proposal.quantity', 'False')
    replay = @('request <= persona.request', 'request < persona.request')
    audit = @('__record-set m "fallbacks" (m.fallbacks + 1)', '__record-set m "fallbacks" m.fallbacks')
}
if ($Sabotage -ne 'none') {
    $before, $after = $mutations[$Sabotage]
    if ($source.IndexOf($before) -lt 0 -or $source.IndexOf($before) -ne $source.LastIndexOf($before)) { throw 'Sabotage target is absent or ambiguous' }
    $source = $source.Replace($before, $after)
}
$unit = Join-Path $OutDir 'mind-unit.codex'
$body = $source + "`r`n" + [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'MindProof.codex'))
. (Join-Path $repo 'build/quire-map.ps1')
$ordered = Resolve-CiteOrder -RootLines ($body -split '\r?\n') -Repo $repo -SeedSeen @{'Uoaix::TownMind'=$true}
$dependencies = (Format-CiteChapters -Ordered $ordered) -join "`r`n"
[IO.File]::WriteAllText($unit, $dependencies + "`r`n" + $body)
$receipt = [ordered]@{kernel=$Kernel;kernelHash=(Get-FileHash -LiteralPath $Kernel).Hash;unitHash=(Get-FileHash -LiteralPath $unit).Hash;sabotage=$Sabotage;poison=[bool]$Poison;compileExit=$null;runExit=$null;passed=$false}
try {
    $receipt.freeKiBBeforeCompile = (Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory
    if ($receipt.freeKiBBeforeCompile -lt 1572864) { throw 'Less than 1.5 GiB free before compile' }
    $cdx = Join-Path $OutDir 'mind.cdx'
    $compileArgs = @('-NoProfile','-File',(Join-Path $repo 'build/compile.ps1'),'-Src',$unit,'-Out',$cdx,'-Log',(Join-Path $OutDir 'compile.log'),'-Kernel',$Kernel)
    if ($Poison) { $compileArgs += '-Poison' }
    & pwsh @compileArgs
    $receipt.compileExit = $LASTEXITCODE
    if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath (Join-Path $OutDir 'compile.log'); throw 'Mind compilation failed' }
    $receipt.artifactHash = (Get-FileHash -LiteralPath $cdx).Hash
    $receipt.freeKiBBeforeRun = (Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory
    if ($receipt.freeKiBBeforeRun -lt 1572864) { throw 'Less than 1.5 GiB free before run' }
    $actual = Join-Path $OutDir 'mind.out'
    & pwsh -NoProfile -File (Join-Path $repo 'build/test-run.ps1') -Kernel $cdx -OutFile $actual
    $receipt.runExit = $LASTEXITCODE
    if ($LASTEXITCODE -ne 0) { throw 'Mind guest failed' }
    $lines = [IO.File]::ReadAllLines($actual)
    $lines | ForEach-Object { Write-Output $_ }
    if ($lines -match '\bFAIL\b|!EXC|OUT OF MEMORY') { throw 'Mind assertions failed' }
    if (@($lines | Where-Object { $_ -eq 'UOAIX MIND PASS failures=0' }).Count -ne 1) { throw 'Missing completion verdict' }
    $rows = @($lines | Where-Object { $_ -match '^ADVERSARIAL ' })
    if ($rows.Count -ne 12 -or @($rows | Where-Object { $_ -notmatch '^ADVERSARIAL \d+ moved=0 outcome=event-policy PASS$' }).Count) { throw 'Adversarial corpus did not preserve inventory' }
    $receipt.passed = $true
} finally {
    [IO.File]::WriteAllText((Join-Path $OutDir 'result.json'),($receipt | ConvertTo-Json -Depth 5))
    Write-Output "Evidence: $OutDir/result.json"
}
