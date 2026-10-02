[CmdletBinding()]
param([string]$Kernel='seed/Codex.cdx',[string]$OutDir='',[switch]$Sabotage)
$ErrorActionPreference='Stop'
$repo=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
Set-Location $repo
if(-not $OutDir){$OutDir=Join-Path $repo ('build-output/scene-gpu-refusal-'+[Guid]::NewGuid().ToString('N'))}
if(Test-Path -LiteralPath $OutDir){throw 'Choose a fresh output directory'}
New-Item -ItemType Directory -Path $OutDir | Out-Null
$OutDir=(Resolve-Path $OutDir).Path
function Admit {
    $os=Get-CimInstance Win32_OperatingSystem
    if($os.FreePhysicalMemory -lt 1572864){throw 'Less than 1.5 GiB free RAM'}
}
& pwsh -NoProfile -File build/bundle-app.ps1 -Src "$PSScriptRoot/scene-gpu-refusal.codex" -Out "$OutDir/source.codex"
if($LASTEXITCODE -ne 0){throw 'Bundle failed'}
$source=[IO.File]::ReadAllText("$OutDir/source.codex")
$probe='desk-gpu-present (dummy) = if gpu-in #403 == 1 then 1 else 0'
if($source.IndexOf($probe) -lt 0 -or $source.IndexOf($probe) -ne $source.LastIndexOf($probe)){throw 'Probe source differs'}
$source=$source.Replace($probe,'desk-gpu-present (dummy) = 0')
if($Sabotage){
    $guard='if sp.sp-gpu-present /= 1 then'
    if($source.IndexOf($guard) -lt 0 -or $source.IndexOf($guard) -ne $source.LastIndexOf($guard)){throw 'Guard source differs'}
    $source=$source.Replace($guard,'if False then')
}
[IO.File]::WriteAllText("$OutDir/source.codex",$source,[Text.UTF8Encoding]::new($false))
Admit
& pwsh -NoProfile -File build/compile.ps1 -Src "$OutDir/source.codex" -Out "$OutDir/probe.cdx" -Log "$OutDir/compile.log" -Kernel $Kernel
if($LASTEXITCODE -ne 0){Get-Content "$OutDir/compile.log";throw 'Compile failed'}
Admit
$guest=Start-Process "$repo/tools/codex-vm.exe" -ArgumentList @('-kernel',"`"$OutDir/probe.cdx`"",'-output',"`"$OutDir/serial.log`"",'-mem','2048','-headless','-smp','1') -WindowStyle Hidden -PassThru -RedirectStandardError "$OutDir/guest.err"
$guest.Id|Set-Content "$OutDir/guest.pid"
"owned codex-vm PID=$($guest.Id); log=$OutDir/serial.log"
try{
    if(-not $guest.WaitForExit(45000)){throw 'Guest timeout'}
    $stderr=[IO.File]::ReadAllText("$OutDir/guest.err")
    if($guest.ExitCode -ne 1 -or $stderr -notmatch 'FINAL: debug_exit_code=0 process_exit=1' -or $stderr -match 'DROPPED|Triple fault|HOST CRASH'){throw 'Invalid guest completion; see guest.err'}
}finally{
    if(-not $guest.HasExited){Stop-Process -Id $guest.Id -Force}
    $guest.WaitForExit();$guest.Dispose()
}
$actual=[IO.File]::ReadAllText("$OutDir/serial.log").Replace("`r",'')
$body=($actual -split "`n"|Where-Object {$_ -and $_ -notmatch '^(HEAP|WD|STACK|PM):'}) -join "`n"
$want=@('absent-preserves-software True','visible-reason True','repeated-refusal True','repaint-keeps-reason True','present-can-toggle True') -join "`n"
$pass=[string]::Equals($body,$want,[StringComparison]::Ordinal)
$body
if($Sabotage){
    if($pass -or $body -notmatch '^absent-preserves-software False' -or $body -notmatch 'visible-reason False'){throw 'Sabotage did not break refusal and visible reason'}
    'PASS: bypassed capability guard is rejected'
}else{
    if(-not $pass){throw 'Scene GPU refusal output differs'}
    'PASS: absent GPU refusal, visible pixels, repaint and present toggles'
}
