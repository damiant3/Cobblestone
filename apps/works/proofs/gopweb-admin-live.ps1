[CmdletBinding()]
param(
    [ValidateSet('both','codex-vm','ovmf')][string]$Bed='both',
    [ValidateSet('all','positive','no-lease','restart-noop')][string]$Mode='all',
    [string]$Kernel='seed/Codex.cdx',
    [string]$OutDir='',
    [int]$HostPort=59110,
    [int]$GuestMB=3072
)
$ErrorActionPreference='Stop'
$repo=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
Set-Location $repo
if(-not $OutDir){$OutDir=Join-Path $repo ('build-output/web-admin-'+[Guid]::NewGuid().ToString('N'))}
$OutDir=[IO.Path]::GetFullPath($OutDir)
if(Test-Path $OutDir){throw 'Choose a fresh output directory'}
New-Item -ItemType Directory $OutDir|Out-Null
Copy-Item $PSCommandPath "$OutDir/runner.ps1"
Copy-Item (Resolve-Path $Kernel).Path "$OutDir/kernel.cdx"
$kernelHash=(Get-FileHash "$OutDir/kernel.cdx").Hash
& pwsh -NoProfile -File build/bundle-app.ps1 -Src "$PSScriptRoot/gopweb-admin-live.codex" -Out "$OutDir/template.codex" -InputsOut "$OutDir/inputs.txt"
if($LASTEXITCODE -ne 0){throw 'Bundle failed'}
$template=[IO.File]::ReadAllText("$OutDir/template.codex")
$modes=@(if($Mode -eq 'all'){'positive';'no-lease';'restart-noop'}else{$Mode})
$beds=@(if($Bed -eq 'both'){'codex-vm';'ovmf'}else{$Bed})
$results=[Collections.Generic.List[object]]::new()
function Admit {
    $os=Get-CimInstance Win32_OperatingSystem
    "free KB=$($os.FreePhysicalMemory); commit headroom KB=$($os.FreeVirtualMemory); guests=1"
    if($os.FreePhysicalMemory -le 1572864 -or $os.FreeVirtualMemory -lt 3145728){throw 'Memory admission refused'}
}
function Read-Live([string]$Path) {
    if(-not (Test-Path $Path)){return ''}
    $stream=[IO.File]::Open($Path,'Open','Read','ReadWrite')
    try{
        if($stream.Length -gt 1MB){throw 'Serial capture exceeds bound'}
        $reader=[IO.StreamReader]::new($stream)
        try{return $reader.ReadToEnd()}finally{$reader.Dispose()}
    }finally{$stream.Dispose()}
}
function Replace-One([string]$Body,[string]$Old,[string]$New) {
    if($Body.IndexOf($Old) -lt 0 -or $Body.IndexOf($Old) -ne $Body.LastIndexOf($Old)){throw "Non-unique control: $Old"}
    $Body.Replace($Old,$New)
}
function Wait-Text([string]$Path,[string]$Text,[Diagnostics.Process]$Guest) {
    $watch=[Diagnostics.Stopwatch]::StartNew()
    do{
        $body=Read-Live $Path
        if($body.Contains($Text)){return $body}
        if($Guest.HasExited -or $body.Contains('!EXC') -or $body.Contains('web-admin-live-end')){throw "Guest ended before $Text; $body"}
        Start-Sleep -Milliseconds 100
    }while($watch.Elapsed.TotalSeconds -lt 45)
    throw "Missing $Text; $body"
}
function Connect-Client {
    $client=[Net.Sockets.TcpClient]::new()
    try{
        $task=$client.ConnectAsync('127.0.0.1',$HostPort)
        if(-not $task.Wait(5000)){throw 'Host forwarding connect timeout'}
        $null=$task.GetAwaiter().GetResult()
        return $client
    }catch{$client.Dispose();throw}
}
function Receive-Reply([Net.Sockets.NetworkStream]$Stream) {
    $received=[IO.MemoryStream]::new()
    try{
        $buffer=[byte[]]::new(4096)
        while($true){
            $failure=''
            try{$n=$Stream.Read($buffer,0,$buffer.Length)}catch [IO.IOException]{
                $socket=$_.Exception.InnerException
                if($socket -isnot [Net.Sockets.SocketException] -or $socket.SocketErrorCode -notin @('TimedOut','ConnectionReset','ConnectionAborted','Shutdown')){throw}
                $failure=[string]$socket.SocketErrorCode
                $n=0
            }
            if($n -eq 0){
                if($received.Length -ne 0){throw 'HTTP response ended after a partial message'}
                return @{status=0;body='';failure=$(if($failure){$failure}else{'Closed'})}
            }
            $received.Write($buffer,0,$n)
            if($received.Length -gt 65536){throw 'HTTP response exceeds proof bound'}
            $text=[Text.Encoding]::UTF8.GetString($received.ToArray())
            $split=$text.IndexOf("`r`n`r`n")
            if($split -lt 0){continue}
            $header=$text.Substring(0,$split)
            $status=[regex]::Match($header,'^HTTP/1\.[01] ([0-9]{3}) ')
            $length=[regex]::Match($header,'(?im)^Content-Length: *([0-9]+)\s*$')
            if(-not $status.Success -or -not $length.Success){throw 'Unexpected HTTP header'}
            $count=[int]$length.Groups[1].Value
            if($count -gt 65536){throw 'HTTP content length exceeds proof bound'}
            if($received.Length -ge $split+4+$count){
                return @{status=[int]$status.Groups[1].Value;body=$text.Substring($split+4);failure=''}
            }
        }
    }finally{$received.Dispose()}
}
function Request([string]$Path,[string]$Log,[bool]$Keep=$false,[int]$TimeoutMs=10000) {
    $client=Connect-Client
    try{
        $stream=$client.GetStream();$stream.ReadTimeout=$TimeoutMs;$stream.WriteTimeout=5000
        $wire=[Text.Encoding]::ASCII.GetBytes("GET $Path HTTP/1.0`r`nHost: localhost`r`n`r`n")
        $stream.Write($wire,0,$wire.Length);$stream.Flush()
        $record=Receive-Reply $stream
        @{path=$Path;status=$record.status;body=$record.body;failure=$record.failure}|ConvertTo-Json -Compress|Add-Content $Log
        $record.client=$(if($Keep){$client}else{$null})
        return $record
    }catch{$client.Dispose();throw}finally{if(-not $Keep){$client.Dispose()}}
}
function Closed-Connection([Net.Sockets.TcpClient]$Client) {
    $stream=$Client.GetStream();$stream.ReadTimeout=2000
    try{
        $byte=$stream.ReadByte()
        if($byte -ge 0){throw 'Unexpected bytes on retained connection'}
        return $true
    }catch [IO.IOException]{
        $socket=$_.Exception.InnerException
        if($socket -isnot [Net.Sockets.SocketException]){throw}
        if($socket.SocketErrorCode -eq 'TimedOut'){return $false}
        if($socket.SocketErrorCode -in @('ConnectionReset','ConnectionAborted','Shutdown')){return $true}
        throw
    }
}
try{
    foreach($modeName in $modes){
        $arm=Join-Path $OutDir $modeName
        New-Item -ItemType Directory $arm|Out-Null
        $body=$template
        if($modeName -eq 'no-lease'){
            $body=Replace-One $body 'gopweb-dhcp-window : Integer = 500' 'gopweb-dhcp-window : Integer = 0'
            $body=Replace-One $body 'live-negative : Integer = 0' 'live-negative : Integer = 1'
        }
        if($modeName -eq 'restart-noop'){
            $body=Replace-One $body 'command == gopweb-cmd-start & peek-qword blk gopweb-blk-state == 2' '(command == gopweb-cmd-start | command == gopweb-cmd-restart) & peek-qword blk gopweb-blk-state == 2'
        }
        [IO.File]::WriteAllText("$arm/source.codex",$body,[Text.UTF8Encoding]::new($false))
        Admit
        & pwsh -NoProfile -File build/compile.ps1 -Src "$arm/source.codex" -Out "$arm/probe.cdx" -Log "$arm/compile.log" -Kernel "$OutDir/kernel.cdx"
        if($LASTEXITCODE -ne 0){Get-Content "$arm/compile.log";throw 'Compile failed'}
        foreach($bedName in $beds){
            $run=Join-Path $arm $bedName
            New-Item -ItemType Directory $run|Out-Null
            if(Get-NetTCPConnection -LocalPort $HostPort -State Listen -ErrorAction SilentlyContinue){throw 'HTTP test port occupied'}
            if($bedName -eq 'ovmf'){
                & pwsh -NoProfile -File build/cdx-to-pe.ps1 -CdxInput "$arm/probe.cdx" -Out "$run/probe.efi" -HeapPages 32768 -ExitBootServices
                if($LASTEXITCODE -ne 0){throw 'PE conversion failed'}
                & pwsh -NoProfile -File build/build-img.ps1 -PeInput "$run/probe.efi" -Out "$run/probe.img"
                if($LASTEXITCODE -ne 0){throw 'Image packaging failed'}
                Copy-Item 'D:/Program Files/qemu/share/edk2-x86_64-code.fd' "$run/code.fd"
                Copy-Item 'D:/Program Files/qemu/share/edk2-i386-vars.fd' "$run/vars.fd"
                Set-ItemProperty "$run/vars.fd" -Name IsReadOnly -Value $false
                $exe='D:/Program Files/qemu/qemu-system-x86_64.exe'
                $guestArgs=@('-accel','tcg','-machine','pc','-m',"$GuestMB",'-smp','1','-drive',"if=pflash,format=raw,unit=0,readonly=on,file=$run/code.fd",'-drive',"if=pflash,format=raw,unit=1,file=$run/vars.fd",'-drive',"format=raw,file=$run/probe.img",'-netdev',"user,id=net0,net=192.168.76.0/24,host=192.168.76.2,dhcpstart=192.168.76.15,hostfwd=tcp:127.0.0.1:$HostPort-192.168.76.15:9100",'-device','e1000,netdev=net0','-serial',"file:$run/serial.log",'-display','none','-no-reboot')
                $guestArgs+=@('-object',"filter-dump,id=wire,netdev=net0,file=$run/network.pcap")
            }else{
                $exe="$repo/tools/codex-vm.exe"
                $guestArgs=@('-kernel',"$arm/probe.cdx",'-output',"$run/serial.log",'-mem',"$GuestMB",'-smp','1','-headless','-e1000-nat','-portfwd',"${HostPort}:9100")
            }
            Admit
            $bootHash=if($bedName -eq 'ovmf'){(Get-FileHash "$run/probe.img").Hash}else{''}
            $guestArgs|ConvertTo-Json|Set-Content "$run/guest-args.json"
            $guest=Start-Process $exe -ArgumentList @($guestArgs|ForEach-Object{'"'+$_+'"'}) -WindowStyle Hidden -PassThru -RedirectStandardError "$run/guest.err"
            $retained=$null;$probeClient=$null;$ackClient=$null
            try{
                $guest.Id|Set-Content "$run/guest.pid"
                "owned $bedName PID=$($guest.Id); log=$run/serial.log; HTTP=$HostPort"
                if($modeName -ne 'no-lease'){
                    $null=Wait-Text "$run/serial.log" 'phase=running' $guest
                    $first=Request '/' "$run/http.jsonl"
                    if($first.status -ne 200 -or $first.body -notmatch 'Cobblestone'){throw 'Root response failed'}
                    $null=Wait-Text "$run/serial.log" 'phase=stopped' $guest
                    $refusal=Request '/' "$run/http.jsonl" $true 1500
                    $probeClient=$refusal.client
                    if($refusal.status -ne 0 -or -not $refusal.failure){throw 'Stopped service answered HTTP'}
                    if((Read-Live "$run/serial.log").Contains('phase=started')){throw 'Stop interval ended before host confirmation'}
                    $ackClient=Connect-Client
                    $null=Wait-Text "$run/serial.log" 'phase=started' $guest
                    $ackClient.Dispose();$ackClient=$null;$probeClient.Dispose();$probeClient=$null
                    $second=Request '/api/status' "$run/http.jsonl" $true
                    $retained=$second.client
                    if($second.status -ne 200 -or $second.body -notmatch '"mode":"http"'){throw 'Start did not restore standard route'}
                    $null=Wait-Text "$run/serial.log" 'phase=restart-ready' $guest
                    if($retained.Client.Poll(200000,[Net.Sockets.SelectMode]::SelectRead)){throw 'Connection was not held open before Restart'}
                    $trigger=Request '/api/health' "$run/http.jsonl"
                    if($trigger.status -ne 200){throw 'Restart trigger request failed'}
                    $null=Wait-Text "$run/serial.log" 'phase=restarted' $guest
                    $closed=Closed-Connection $retained
                    $closed|Set-Content "$run/restart-closed.txt"
                    if($modeName -eq 'restart-noop'){
                        if($closed){throw 'No-op Restart control did not preserve the connection'}
                    }elseif(-not $closed){throw 'Restart did not close the existing connection'}
                    $third=Request '/missing' "$run/http.jsonl"
                    if($third.status -ne 404){throw 'Restart response failed'}
                }else{
                    $null=Wait-Text "$run/serial.log" 'phase=no-lease' $guest
                    $refusal=Request '/' "$run/http.jsonl" $true 1500
                    $probeClient=$refusal.client
                    if($refusal.status -ne 0 -or -not $refusal.failure){throw 'Unleased service answered HTTP'}
                    if($guest.HasExited){throw 'No-lease refusal occurred after guest exit'}
                    $ackClient=Connect-Client
                }
                $trace=Wait-Text "$run/serial.log" 'web-admin-live-end' $guest
                if($trace -notmatch 'service-ready=True' -or $trace -match '!EXC|=False'){throw "Invalid trace: $trace"}
                if($modeName -ne 'no-lease'){
                    if($trace -notmatch 'controls=True' -or $trace -notmatch 'requests=4 2xx=3 4xx=1 bytes=[1-9][0-9]*' -or $trace -notmatch 'log-path=/missing'){throw "Incomplete positive: $trace"}
                }elseif($trace -notmatch 'lease-refused=True' -or $trace -notmatch 'binding=0.0.0.0:9100'){throw 'No-lease control ineffective'}
                if($bedName -eq 'codex-vm'){
                    if(-not $guest.WaitForExit(5000)){throw 'Native guest did not exit'}
                    $stderr=Read-Live "$run/guest.err"
                    if($guest.ExitCode -ne 1 -or $stderr -notmatch 'FINAL: debug_exit_code=0 process_exit=1' -or $stderr -match 'DROPPED|HOST CRASH'){throw 'Invalid native exit'}
                }
                $grade=if($modeName -eq 'positive'){'PASS'}else{'CONTROL-PASS'}
                $record=[ordered]@{arm=$modeName;bed=$bedName;grade=$grade;kernel=$kernelHash;source=(Get-FileHash "$arm/source.codex").Hash;cdx=(Get-FileHash "$arm/probe.cdx").Hash;log="$run/serial.log"}
                if($bedName -eq 'ovmf'){$record.image=$bootHash}
                $results.Add([pscustomobject]$record)
                [IO.File]::WriteAllText("$OutDir/results.json",(ConvertTo-Json -InputObject @($results.ToArray()) -Depth 5))
                "$modeName $bedName $grade"
                $trace
            }finally{
                if($retained){$retained.Dispose()}
                if($probeClient){$probeClient.Dispose()}
                if($ackClient){$ackClient.Dispose()}
                if(-not $guest.HasExited){Stop-Process -Id $guest.Id -Force}
                $guest.WaitForExit();$guest.Dispose()
            }
        }
    }
    '0'|Set-Content "$OutDir/exit.code"
}catch{'1'|Set-Content "$OutDir/exit.code";throw}
