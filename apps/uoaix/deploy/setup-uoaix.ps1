[CmdletBinding()]
param(
    [string]$ClientRoot = '',
    [string]$UoaixDir = (Join-Path $env:LOCALAPPDATA 'uoaix'),
    [switch]$CheckOnly
)
# UOAIX on Windows, set up once (docs/Designs/Active/Apps/UoaixWindowsDeployment.md). Everything client-derived is
# built here from the player's own 1.25 client; nothing is written into the repository or into that client.
#   pwsh apps/uoaix/deploy/setup-uoaix.ps1 -ClientRoot 'C:\UO'        then   pwsh <UoaixDir>\play-uoaix.ps1
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$uoaix = Join-Path $repo 'apps/uoaix'
function Fail([string]$why, [string]$fix) { Write-Host "SETUP STOPPED: $why" -ForegroundColor Red; if ($fix) { Write-Host "  Fix: $fix" }; exit 1 }
function Step([string]$text) { Write-Host "== $text" -ForegroundColor Cyan }

Step 'Checking this PC'
if ($PSVersionTable.PSVersion.Major -lt 7) { Fail "PowerShell $($PSVersionTable.PSVersion) is running; UOAIX needs PowerShell 7." 'Install it from https://aka.ms/powershell and run this script with pwsh.' }
if (-not (Test-Path (Join-Path $repo 'tools/codex-vm.exe'))) { Fail 'tools/codex-vm.exe is missing from this checkout.' 'Clone the whole repository.' }
if (-not (Test-Path (Join-Path $repo 'seed/Codex.cdx'))) { Fail 'seed/Codex.cdx is missing from this checkout.' 'Clone the whole repository.' }
# Asked of the platform codex-vm itself uses (WHvGetCapability, code 0: hypervisor present); the optional-feature table
# answered "disabled" on a PC where codex-vm runs (Windows 11 build 26300, 2026-10-19).
Add-Type -TypeDefinition 'using System.Runtime.InteropServices; public static class UoaixWhp { [DllImport("WinHvPlatform.dll")] public static extern int WHvGetCapability(int code, out int present, int size, out int written); }'
$whp = $false; try { $present = 0; $written = 0; $whp = [UoaixWhp]::WHvGetCapability(0, [ref]$present, 4, [ref]$written) -eq 0 -and $present -ne 0 } catch { $whp = $false }
if (-not $whp) { Fail 'Windows Hypervisor Platform is off; codex-vm needs it to run the server.' 'In an administrator PowerShell: Enable-WindowsOptionalFeature -Online -FeatureName HypervisorPlatform, then restart Windows.' }
$freeGiB = [math]::Round((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1MB, 1)
if ($freeGiB -lt 2) { Fail "$freeGiB GiB of memory is free; the server needs about 1.5 GiB and the setup's compile more." 'Close other programs and run the setup again.' }

Step 'Checking your Ultima Online client'
if (-not $ClientRoot) { $ClientRoot = Read-Host 'The folder of your 1.25 Ultima Online client (the one holding CLIENT.EXE and MAP0.MUL)' }
$ClientRoot = [IO.Path]::GetFullPath($ClientRoot.Trim('"'))
foreach ($f in 'CLIENT.EXE', 'MAP0.MUL', 'STAIDX0.MUL', 'STATICS0.MUL', 'TILEDATA.MUL', 'MULTI.IDX', 'MULTI.MUL', 'ART.MUL', 'ARTIDX.MUL') {
    if (-not (Test-Path (Join-Path $ClientRoot $f))) { Fail "$f is not in $ClientRoot." 'Point -ClientRoot at the folder of an installed 1.25 client (1.25.32).' }
}
# The client's statics must be the original ones the flora reference was taken from (PlayerClient.md).
$ref = Get-Content (Join-Path $uoaix 'flora-reference.json') -Raw | ConvertFrom-Json
foreach ($f in 'STAIDX0.MUL', 'STATICS0.MUL') {
    $h = (Get-FileHash (Join-Path $ClientRoot $f) -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($h -ne ([string]$ref.original.$f).ToLowerInvariant()) { Fail "$f in $ClientRoot is not the 1.25.32 original UOAIX knows (a patched or flora-stripped client?)." 'Use an unmodified 1.25.32 client folder.' }
}
if ($UoaixDir.StartsWith($repo, [StringComparison]::OrdinalIgnoreCase)) { Fail "The UOAIX home $UoaixDir is inside the repository." 'Pass -UoaixDir outside it (the default is %LOCALAPPDATA%\uoaix).' }
if (-not (Get-Command git -ErrorAction SilentlyContinue)) { Fail 'git is not installed; the setup fetches the ServUO and UOX3 door and sign data with it.' 'Install Git for Windows from https://git-scm.com.' }
if ($CheckOnly) { Write-Host 'CHECKS PASSED: this PC and this client can run UOAIX.' -ForegroundColor Green; exit 0 }
New-Item -ItemType Directory -Force $UoaixDir | Out-Null

# The door, sign and addon placements are read from ServUO (GPL-2.0) and UOX3 (GPL-2.0) on this machine; neither is
# shipped with UOAIX. import-decoration.ps1 reads <ReferenceRoot>\ServUO and <ReferenceRoot>\UOX3 (WorldDecoration.md).
Step 'Fetching the ServUO and UOX3 decoration data (read here, never shipped)'
$refRoot = Join-Path $UoaixDir 'reference'
# Pinned to the commits UOAIX's decoration was built and checked against. Only the folders import-decoration.ps1 reads
# are checked out: a full ServUO checkout passes Windows' 260-character path limit ("Filename too long").
function Invoke-RefGit([string]$dir, [string]$what) {
    & git -C $dir @args 2>&1 | ForEach-Object { Write-Host "  $_" }
    if ($LASTEXITCODE -ne 0) { Fail "$what failed (git exit $LASTEXITCODE)." 'Check the internet connection and run the setup again.' }
}
foreach ($r in @(
        @{ Name = 'ServUO'; Url = 'https://github.com/ServUO/ServUO.git'; Commit = 'd76bf4443cf76d081ddaf8f57c87ff33749256af'
            Paths = @('Server/Item.cs', 'Scripts/Items/Functional/', 'Scripts/Items/Addons/', 'Data/Decoration/Britannia/', 'Data/Decoration/Old/Britannia/') },
        @{ Name = 'UOX3'; Url = 'https://github.com/UOX3DevTeam/UOX3.git'; Commit = '4560ae841bac898817143d7aa95ce59f47ab98e0'
            Paths = @('data/js/jsdata/worldtemplates/') })) {
    $dir = Join-Path $refRoot $r.Name
    $need = @($r.Paths | ForEach-Object { Join-Path $dir $_ })
    if ((Test-Path (Join-Path $dir '.git')) -and (& git -C $dir rev-parse HEAD 2>$null) -eq $r.Commit -and @($need | Where-Object { -not (Test-Path $_) }).Count -eq 0) { Write-Host "  $($r.Name): already fetched"; continue }
    if (Test-Path $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }
    New-Item -ItemType Directory -Force $dir | Out-Null
    Invoke-RefGit $dir "creating the $($r.Name) folder" init -q
    Invoke-RefGit $dir "configuring $($r.Name)" config core.longpaths true
    Invoke-RefGit $dir "configuring $($r.Name)" config core.sparseCheckout true
    Set-Content -LiteralPath (Join-Path $dir '.git/info/sparse-checkout') -Value @($r.Paths | ForEach-Object { "/$_" }) -Encoding ascii
    Invoke-RefGit $dir "fetching $($r.Name) at $($r.Commit)" fetch -q --depth 1 $r.Url $r.Commit
    Invoke-RefGit $dir "checking out $($r.Name)" checkout -q FETCH_HEAD
    $missing = @($need | Where-Object { -not (Test-Path $_) })
    if ($missing.Count) { Fail "the $($r.Name) checkout lacks $($missing -join ', ')." 'Delete that folder and run the setup again.' }
}

Step 'Compiling the UOAIX server (a few minutes)'
$cdx = Join-Path $UoaixDir 'server.cdx'; $log = Join-Path $UoaixDir 'server.compile.log'
if (Test-Path $cdx) { Remove-Item -LiteralPath $cdx -Force }
& pwsh -NoProfile -File (Join-Path $repo 'build/compile.ps1') -Src (Join-Path $uoaix 'CompositeGameServer.codex') -Out $cdx -Log $log -Kernel (Join-Path $repo 'seed/Codex.cdx') | Out-Null
# compile.ps1 prints True on stdout either way (uoaix-init.md): the exit code and the output file decide.
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $cdx)) { Fail "the server did not compile (exit $LASTEXITCODE); its log is $log." 'Report it with the log.' }

Step 'Building the world from your client'
$dwd = Join-Path $UoaixDir 'britannia.dwd'
& pwsh -NoProfile -File (Join-Path $uoaix 'import-decoration.ps1') -ClientRoot $ClientRoot -OutFile $dwd -ReferenceRoot $refRoot
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $dwd)) { Fail "the decoration import failed (exit $LASTEXITCODE)." 'Report it with the output above.' }
$floraDir = Join-Path $UoaixDir 'flora'
& pwsh -NoProfile -File (Join-Path $uoaix 'transform-flora.ps1') -ClientRoot $ClientRoot -OutDir $floraDir
if ($LASTEXITCODE -ne 0 -or -not (Test-Path (Join-Path $floraDir 'flora.flr'))) { Fail "the flora strip failed (exit $LASTEXITCODE)." 'Report it with the output above.' }
$disk = Join-Path $UoaixDir 'world.disk'
if (Test-Path $disk) { Fail "a world already stands at $disk; the setup never overwrites a world." 'Run play-uoaix.ps1, or move that file away for a new world.' }
& pwsh -NoProfile -File (Join-Path $uoaix 'install-map-cache.ps1') -ClientRoot $ClientRoot -WorldDisk $disk -DecorationFile $dwd -FloraFile (Join-Path $floraDir 'flora.flr')
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $disk)) { Fail "the map cache install failed (exit $LASTEXITCODE)." 'Report it with the output above.' }

# The server owns the plants, so the client must not draw its own copy of them (PlayerClient.md): a UOAIX copy of the
# client with the stripped statics, logging in through the Assist. The player's own client folder is left as it was.
Step 'Making your UOAIX client copy'
$client = Join-Path $UoaixDir 'client'
if (-not (Test-Path $client)) { Copy-Item -LiteralPath $ClientRoot -Destination $client -Recurse }
foreach ($f in 'STAIDX0.MUL', 'STATICS0.MUL') { Copy-Item -LiteralPath (Join-Path $floraDir $f) -Destination (Join-Path $client $f) -Force }
Get-ChildItem -LiteralPath $client -Recurse -File | Where-Object IsReadOnly | ForEach-Object { $_.IsReadOnly = $false }
Set-Content -LiteralPath (Join-Path $client 'LOGIN.CFG') -Value 'LoginServer=127.0.0.1,2630' -Encoding ascii

Step 'Writing play-uoaix.ps1'
$play = @"
# Starts your UOAIX shard, the Assist and your UOAIX client (written by setup-uoaix.ps1). -NoClient starts the server only.
param([ValidateRange(1024, 65535)][int]`$Port = 2593, [switch]`$NoClient)
`$ErrorActionPreference = 'Stop'
`$logs = Join-Path '$UoaixDir' 'logs'; New-Item -ItemType Directory -Force `$logs | Out-Null
`$run = Join-Path `$logs ('run-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
`$start = Start-Process pwsh -WindowStyle Hidden -PassThru -ArgumentList '-NoProfile','-File','$uoaix\start-composite-game.ps1','-Artifact','$cdx','-StateFile','$disk','-OutDir',`$run,'-Port',"`$Port",'-StartupSeconds','180','-Testing','-Open','-NoPrompt' -RedirectStandardOutput "`$run.out" -RedirectStandardError "`$run.err"
Write-Host "Server starting (about a minute); log in `$run"
# The forwarded port listens from the moment codex-vm starts, so the start script's READY line is the signal.
`$ready = { (Test-Path "`$run.out") -and (Select-String -LiteralPath "`$run.out" -Pattern '^READY ' -Quiet) }
for (`$i = 0; `$i -lt 200 -and -not (& `$ready) -and -not `$start.HasExited; `$i++) { Start-Sleep -Seconds 1 }
if (-not (& `$ready)) { Write-Host "The server did not start; see `$run.out and `$run.err." -ForegroundColor Red; exit 1 }
if (`$NoClient) { Write-Host "UOAIX server is up on 127.0.0.1,`$Port." -ForegroundColor Green; exit 0 }
Start-Process pwsh -WindowStyle Hidden -ArgumentList '-NoProfile','-File','$uoaix\assist\assist.ps1','-LoginServer',"127.0.0.1,`$Port",'-ListenPort','2630','-LogDir',`$logs
for (`$i = 0; `$i -lt 120 -and -not (Get-NetTCPConnection -State Listen -LocalPort 2630 -ErrorAction SilentlyContinue); `$i++) { Start-Sleep -Seconds 1 }
Start-Process (Join-Path '$client' 'CLIENT.EXE') -WorkingDirectory '$client'
Write-Host 'UOAIX is up: log in with any account name and password; the Assist docks beside the client.' -ForegroundColor Green
"@
Set-Content -LiteralPath (Join-Path $UoaixDir 'play-uoaix.ps1') -Value $play
Write-Host "SETUP DONE. Play: pwsh `"$(Join-Path $UoaixDir 'play-uoaix.ps1')`"" -ForegroundColor Green
