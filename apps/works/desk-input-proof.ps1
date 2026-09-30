[CmdletBinding()]
param(
    [ValidateSet('codex-vm','ovmf','both')][string]$Bed = 'both',
    [ValidateSet('positive','rearm','edge','overflow','all')][string]$Mode = 'all',
    [string]$Kernel = 'seed/Codex.cdx',
    [string]$OutDir = ''
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
Set-Location $repo
$kernelPath = (Resolve-Path $Kernel).Path
if (-not $OutDir) { $OutDir = Join-Path $repo ('build-output/desk-input-proof-' + [Guid]::NewGuid().ToString('N')) }
if (-not [IO.Path]::IsPathRooted($OutDir)) { $OutDir = Join-Path $repo $OutDir }
$OutDir = [IO.Path]::GetFullPath($OutDir)
if (Test-Path $OutDir) { throw 'Proof output directory already exists; choose a new directory' }
New-Item -ItemType Directory -Path $OutDir | Out-Null
$kernelHash = (Get-FileHash $kernelPath -Algorithm SHA256).Hash
$kernelSnapshot = Join-Path $OutDir 'kernel.cdx'
Copy-Item -LiteralPath $kernelPath -Destination $kernelSnapshot
if ((Get-FileHash $kernelSnapshot -Algorithm SHA256).Hash -ne $kernelHash -or (Get-FileHash $kernelPath -Algorithm SHA256).Hash -ne $kernelHash) { throw 'Compiler changed during snapshot' }
$kernelPath = $kernelSnapshot
$templatePath = Join-Path $OutDir 'template.codex'
& pwsh -NoProfile -File (Join-Path $repo 'build/bundle-app.ps1') -Src (Join-Path $PSScriptRoot 'proofs/desk-input-render.codex') -Out $templatePath
if ($LASTEXITCODE -ne 0) { throw 'Proof source snapshot failed' }
$template = [IO.File]::ReadAllText($templatePath)
$entry = '  opening : [Console, Device.Port, Device.Mmio] Nothing = dip-run 0'
if ($template.IndexOf($entry) -lt 0 -or $template.IndexOf($entry) -ne $template.LastIndexOf($entry)) { throw 'Proof template entry is not unique' }
$modes = if ($Mode -eq 'all') { @('positive','rearm','edge','overflow') } else { @($Mode) }
$beds = if ($Bed -eq 'both') { @('codex-vm','ovmf') } else { @($Bed) }
$modeNumber = @{ positive=0; rearm=1; edge=2; overflow=3 }
$records = [Collections.Generic.List[object]]::new()
function Assert-Ram {
    $freeKB = (Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory
    Write-Output "free physical KB: $freeKB; guests: 1"
    if ($freeKB -le 1572864) { throw 'RAM admission refused' }
}
foreach ($arm in $modes) {
    $src = Join-Path $OutDir "$arm.codex"
    $cdx = Join-Path $OutDir "$arm.cdx"
    $source = $template.Replace($entry, '  opening : [Console, Device.Port, Device.Mmio] Nothing = dip-run ' + $modeNumber[$arm])
    [IO.File]::WriteAllText($src,$source,[Text.UTF8Encoding]::new($false))
    Assert-Ram
    & pwsh -NoProfile -File (Join-Path $repo 'build/compile.ps1') -Src $src -Out $cdx -Log (Join-Path $OutDir "$arm-compile.log") -Kernel $kernelPath
    if ($LASTEXITCODE -ne 0) { throw "$arm compile failed; see $OutDir" }
    $cdxHash = (Get-FileHash $cdx -Algorithm SHA256).Hash
    foreach ($bedName in $beds) {
        $run = Join-Path $OutDir "$bedName-$arm"
        New-Item -ItemType Directory -Path $run | Out-Null
        $trace = Join-Path $run 'trace.log'
        if ($bedName -eq 'codex-vm') {
            Assert-Ram
            & pwsh -NoProfile -File (Join-Path $repo 'build/test-run.ps1') -Kernel $cdx -OutFile $trace -KeysFile (Join-Path $PSScriptRoot 'proofs/desk-input-render.keys') -VmArgsFile (Join-Path $PSScriptRoot 'proofs/desk-input-render.vmargs')
        } else {
            & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'desk-input-ovmf.ps1') -Cdx $cdx -OutDir $run
        }
        if ($LASTEXITCODE -ne 0) { throw "$bedName $arm execution failed; see $run" }
        $text = [IO.File]::ReadAllText($trace)
        if (-not $text.Contains('trace-end')) { throw "$bedName $arm trace incomplete" }
        $accepted = $text.Contains('accepted=yes')
        $lossMatch = [regex]::Match($text,'(?m)^motion=.* loss=(\d+) trace-loss=(\d+)')
        $armsMatch = [regex]::Match($text,'(?m)^collector-gap-ticks=.* rearms=(\d+)')
        $pendingMatch = [regex]::Match($text,'(?m)^input-gap-ticks=\d+ outstanding-ticks=(\d+)')
        $ok = if ($arm -eq 'positive') { $accepted }
            elseif ($arm -eq 'rearm') { -not $accepted -and $armsMatch.Success -and $armsMatch.Groups[1].Value -eq '0' -and $pendingMatch.Success -and [long]$pendingMatch.Groups[1].Value -gt 0 -and $text.Contains('sequence=NO motion=NO') -and $text.Contains('lossless=yes') }
            elseif ($arm -eq 'edge') { -not $accepted -and $text.Contains('sequence=NO motion=yes') -and $text.Contains('lossless=yes') }
            else { -not $accepted -and $lossMatch.Success -and [long]$lossMatch.Groups[1].Value -gt 0 -and $text.Contains('lossless=NO') }
        if ((Get-FileHash $cdx -Algorithm SHA256).Hash -ne $cdxHash -or (Get-FileHash $kernelPath -Algorithm SHA256).Hash -ne $kernelHash) { throw 'Proof artifact changed during execution' }
        $record = [ordered]@{ bed=$bedName; arm=$arm; passed=$ok; accepted=$accepted; kernel=$kernelHash; cdx=$cdxHash; source=(Get-FileHash $src -Algorithm SHA256).Hash; trace=$trace }
        if ($bedName -eq 'ovmf') { $record.image=(Get-Content (Join-Path $run 'image.sha256') -Raw).Trim() }
        $records.Add($record)
        $records | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $OutDir 'results.json') -Encoding utf8
        Write-Output "$(if ($ok) { 'PASS' } else { 'FAIL' }) $bedName ${arm}: $trace"
        if (-not $ok) { throw "$bedName $arm did not produce its required verdict" }
    }
}
Write-Output "Desk input proof passed: $OutDir"
