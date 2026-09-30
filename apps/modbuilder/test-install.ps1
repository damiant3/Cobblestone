# The install guards of target-toolchain.ps1, run on scratch directories.
#   pwsh apps/modbuilder/test-install.ps1      Exit 0 = every arm passed.
# A running valheim.exe makes every install refuse, so close the game first.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'target-toolchain.ps1')

$work = Join-Path ([IO.Path]::GetTempPath()) ('mb-install-' + [guid]::NewGuid().ToString('N'))
$fails = 0
function Arm([string]$Name, [bool]$Pass, [string]$Detail = '') { Write-Host ('  {0}  {1}{2}' -f $(if ($Pass) { 'ok  ' } else { 'FAIL' }), $Name, $(if ($Detail) { ': ' + $Detail } else { '' })); if (-not $Pass) { $script:fails++ } }
function New-Build([string]$Number, [string]$Bytes) {
    $dir = Join-Path $work "build-$Number"; [void](New-Item -ItemType Directory -Force -Path (Join-Path $dir 'native'))
    $dll = Join-Path $dir 'native/winhttp.dll'; [IO.File]::WriteAllText($dll, $Bytes)
    $rcpt = Join-Path $dir 'receipt.json'; [IO.File]::WriteAllText($rcpt, '{}')
    @{ artifact = $dll; receiptPath = $rcpt; prior = [pscustomobject]@{ artifactHash = (Get-FileHash $dll).Hash; buildNumber = $Number; features = @([pscustomobject]@{ id = 'hud' }) } }
}
function Try-Install($Built, [string]$Into) {
    try { $r = Install-PrismArtifact $Built ([ordered]@{}) $game $target ([pscustomobject]@{ installPath = $Into }); return @{ ok = $true; r = $r } }
    catch { return @{ ok = $false; err = $_.Exception.Message } }
}

try {
    $game = Join-Path $work 'game'; $play = Join-Path $work 'play'; $bare = Join-Path $work 'bare'
    foreach ($d in @($game, $play, (Join-Path $play 'Saves'), $bare)) { [void](New-Item -ItemType Directory -Force -Path $d) }
    foreach ($d in @($game, $play)) { [IO.File]::WriteAllText((Join-Path $d 'valheim.exe'), 'exe') }
    [IO.File]::WriteAllText((Join-Path $play 'Saves/world.fwl'), 'world')
    $target = [pscustomobject]@{ executable = 'valheim.exe' }
    $loader = Join-Path $play 'winhttp.dll'
    $b1 = New-Build '1' 'build one'; $b2 = New-Build '2' 'build two'; $b3 = New-Build '3' 'build three'

    $a = Try-Install $b1 $play
    $dep = if (Test-Path (Join-Path $play 'Support/deployment.json')) { Get-Content (Join-Path $play 'Support/deployment.json') -Raw | ConvertFrom-Json } else { $null }
    Arm 'a first install into a copy with no loader lands the build and records it' ($a.ok -and (Get-FileHash $loader).Hash -eq $b1.prior.artifactHash -and $dep.modHash -eq $b1.prior.artifactHash -and $dep.buildNumber -eq '1') ($a['err'])

    $r = Try-Install $b1 $play
    Arm 'the same build twice is refused' ((-not $r.ok) -and $r['err'] -match 'already installed') ($r['err'])

    $c = Try-Install $b2 $play
    $bk = Join-Path $play 'Support/Before-2'
    Arm 'a second build backs up the first and the saves' ($c.ok -and (Get-FileHash $loader).Hash -eq $b2.prior.artifactHash -and (Get-FileHash (Join-Path $bk 'winhttp.dll')).Hash -eq $b1.prior.artifactHash -and (Test-Path (Join-Path $bk 'Saves.zip'))) ($c['err'])

    Invoke-Expression (Get-Content (Join-Path $bk 'restore.json') -Raw | ConvertFrom-Json).restore
    Arm 'the restore manifest puts the first build back' ((Get-FileHash $loader).Hash -eq $b1.prior.artifactHash)

    [IO.File]::WriteAllText($loader, 'a foreign loader')
    $u = Try-Install $b3 $play
    Arm 'an unknown loader is refused and left untouched' ((-not $u.ok) -and $u['err'] -match 'Unknown loader' -and [IO.File]::ReadAllText($loader) -eq 'a foreign loader') ($u['err'])

    $o = Try-Install $b3 $game
    Arm 'the original game directory is refused' ((-not $o.ok) -and $o['err'] -match 'original game' -and -not (Test-Path (Join-Path $game 'winhttp.dll'))) ($o['err'])

    $n = Try-Install $b3 $bare
    Arm 'a directory with no game executable is refused' ((-not $n.ok) -and $n['err'] -match 'no game executable') ($n['err'])
} finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Host $(if ($fails) { "$fails arm(s) FAILED" } else { 'all arms passed' })
exit $(if ($fails) { 1 } else { 0 })
