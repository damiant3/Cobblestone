[CmdletBinding()]
param([Parameter(Mandatory)][string]$Artifact,[Parameter(Mandatory)][string]$StateFile,[Parameter(Mandatory)][string]$OutDir,
    [switch]$Testing,[switch]$Dev,[switch]$Hosted,[switch]$Admin,[string]$Vm='',
    [ValidateRange(1024,65535)][int]$Port=2593,[ValidateRange(1024,65535)][int]$AdminPort=2594,
    [ValidateRange(10,120)][int]$StartupSeconds=60,[ValidateRange(1,600)][int]$BackoffSeconds=5,
    [ValidateRange(1,3600)][int]$MaxBackoffSeconds=300,[ValidateRange(1,100)][int]$MaxRestarts=5,
    [ValidateRange(60,86400)][int]$RestartWindowSeconds=600,[ValidateRange(0,3600)][int]$HeartbeatSeconds=180)
# Relaunches a composite shard after every guest exit; CompositeGame.md documents the contract.
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$launcher=Join-Path $PSScriptRoot 'start-composite-game.ps1'
$OutDir=[IO.Path]::GetFullPath($OutDir)
if(Test-Path -LiteralPath $OutDir){throw 'OutDir must be new'}
[void](New-Item -ItemType Directory $OutDir)
$crashes=Join-Path $OutDir 'crashes.log';$stop=Join-Path $OutDir 'stop';$state=Join-Path $OutDir 'supervisor.json'
function Write-Crash([string]$Text){[IO.File]::AppendAllText($crashes,$Text+"`r`n",[Text.Encoding]::UTF8)}
function Read-Shared([string]$Path){
    if(-not (Test-Path -LiteralPath $Path)){return ''}
    $f=[IO.File]::Open($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
    $r=[IO.StreamReader]::new($f);try{return $r.ReadToEnd()}finally{$r.Dispose()}
}
function Read-From([string]$Path,[ref]$At){
    if(-not (Test-Path -LiteralPath $Path)){return ''}
    $f=[IO.File]::Open($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
    try{if($f.Length -lt $At.Value){$At.Value=0};[void]$f.Seek($At.Value,[IO.SeekOrigin]::Begin);$r=[IO.StreamReader]::new($f);$s=$r.ReadToEnd();$At.Value=$f.Position;return $s}finally{$f.Dispose()}
}function Save-State([hashtable]$S){$S|ConvertTo-Json|Set-Content -LiteralPath $state}
function Stop-Guest([int]$Id){
    $p=Get-Process -Id $Id -ErrorAction SilentlyContinue;if(-not $p){return}
    try{$h=[Threading.EventWaitHandle]::OpenExisting("Global\CodexVmShutdown_$Id");try{[void]$h.Set()}finally{$h.Dispose()};[void]$p.WaitForExit(15000)}catch{}
    if(-not $p.HasExited){Stop-Process -Id $Id -Force;[void]$p.WaitForExit(5000)}
}
$run=0;$delay=$BackoffSeconds;$starts=[Collections.Generic.List[datetime]]::new();$guestId=0
$info=@{supervisor=$PID;outDir=$OutDir;port=$Port;run=0;guest=0;state='starting';restarts=0}
Save-State $info
"SUPERVISOR PID=$PID port=$Port; create $stop to stop"
try{
    while($true){
        if(Test-Path -LiteralPath $stop){break}
        $now=[datetime]::UtcNow
        while($starts.Count -gt 0 -and ($now-$starts[0]).TotalSeconds -gt $RestartWindowSeconds){$starts.RemoveAt(0)}
        if($starts.Count -gt $MaxRestarts){
            Write-Crash "$($now.ToString('o')) GIVING UP: $($starts.Count) launches within $RestartWindowSeconds s"
            $info.state='gave-up';Save-State $info;throw "supervisor gave up after $($starts.Count) launches within $RestartWindowSeconds s; see $crashes"
        }
        $run++;$dir=Join-Path $OutDir ('run-'+$run);$starts.Add($now)
        $launch=@{Artifact=$Artifact;StateFile=$StateFile;OutDir=$dir;StartupSeconds=$StartupSeconds;Port=$Port;AdminPort=$AdminPort;NoPrompt=($run -gt 1)}
        if($Testing){$launch.Testing=$true};if($Dev){$launch.Dev=$true};if($Hosted){$launch.Hosted=$true};if($Admin){$launch.Admin=$true};if($Vm){$launch.Vm=$Vm}
        $started=[datetime]::UtcNow
        try{& $launcher @launch | ForEach-Object {"run-${run}: $_"}}
        catch{
            Write-Crash "$($started.ToString('o')) run-$run LAUNCH FAILED: $($_.Exception.Message)"
            "run-${run}: launch failed: $($_.Exception.Message); retry in $delay s"
            $info.state='backoff';Save-State $info
            for($w=0;$w -lt $delay -and -not (Test-Path -LiteralPath $stop);$w++){Start-Sleep -Seconds 1}
            $delay=[Math]::Min($MaxBackoffSeconds,$delay*2);continue
        }
        $guestId=(Get-Content -LiteralPath (Join-Path $dir 'run.json') | ConvertFrom-Json).pid
        $info.run=$run;$info.guest=$guestId;$info.state='serving';$info.restarts=$run-1;Save-State $info
        $guest=Get-Process -Id $guestId -ErrorAction SilentlyContinue;if($guest){$null=$guest.Handle}
        $logPath=Join-Path $dir 'server.log';$at=[long]0;$beat=$null;$wedged=$false
        while($guest -and -not $guest.WaitForExit(2000)){
            if(Test-Path -LiteralPath $stop){break}
            if($HeartbeatSeconds -gt 0){
                if((Read-From $logPath ([ref]$at)) -match '(?m)^WATERMARK'){$beat=[datetime]::UtcNow}
                elseif($beat -and ([datetime]::UtcNow-$beat).TotalSeconds -gt $HeartbeatSeconds){$wedged=$true;break}
            }
        }
        if($wedged){Write-Crash "$([datetime]::UtcNow.ToString('o')) run-$run WEDGED: no WATERMARK line for $HeartbeatSeconds s; stopping the guest";Stop-Guest $guestId}
        if(Test-Path -LiteralPath $stop){break}
        $code=if($guest){try{$guest.ExitCode}catch{'unknown'}}else{'gone'}
        $up=[int]([datetime]::UtcNow-$started).TotalSeconds
        $text=Read-Shared (Join-Path $dir 'server.log')
        $lines=@($text -split "`r?`n" | Where-Object {$_})
        $cause=@($lines | Where-Object {$_ -match 'STOP serving|requires restart|!EXC|OUT OF MEMORY|SAVE REFUSED for|^FAIL'} | Select-Object -Last 3)
        $err=Read-Shared (Join-Path $dir 'guest.stderr')
        Write-Crash "$([datetime]::UtcNow.ToString('o')) run-$run EXIT code=$code uptime=${up}s log=$(Join-Path $dir 'server.log')"
        foreach($c in $cause){Write-Crash "  cause: $c"}
        foreach($l in ($lines | Select-Object -Last 20)){Write-Crash "  tail: $l"}
        if($err.Trim()){foreach($l in (@($err -split "`r?`n" | Where-Object {$_}) | Select-Object -Last 5)){Write-Crash "  stderr: $l"}}
        $delay=if($up -lt 60){[Math]::Min($MaxBackoffSeconds,$delay*2)}else{$BackoffSeconds}
        "run-${run}: guest exited code=$code after ${up}s; relaunch in $delay s"
        $info.state='backoff';$info.guest=0;Save-State $info
        for($w=0;$w -lt $delay -and -not (Test-Path -LiteralPath $stop);$w++){Start-Sleep -Seconds 1}
    }
}finally{
    if($guestId){Stop-Guest $guestId}
    $info.state='stopped';$info.guest=0;Save-State $info
    "SUPERVISOR stopped after $run launches"
}
