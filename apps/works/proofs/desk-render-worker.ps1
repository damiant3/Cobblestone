[CmdletBinding()]
param(
    [ValidateSet('both','codex-vm','ovmf')][string]$Bed='both',
    [ValidateSet('render','lifecycle','admission')][string]$Unit='render',
    [ValidateSet('all','positive','owner','token','closed','borrowed','duplicate','copy-bound','close-hook','replace-hook','hide-hook','worker-budget','color-tail','depth-tail')][string]$Mode='all',
    [string]$Kernel='seed/Codex.cdx',
    [switch]$OwnedProcessPool,
    [string]$OutDir=''
)
$ErrorActionPreference='Stop'
if($OwnedProcessPool -and $Bed -ne 'ovmf'){throw 'OwnedProcessPool requires -Bed ovmf'}
$repo=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
Set-Location $repo
if(-not $OutDir){$OutDir=Join-Path $repo ('build-output/render-worker-proof-'+[Guid]::NewGuid().ToString('N'))}
if(-not [IO.Path]::IsPathRooted($OutDir)){$OutDir=Join-Path $repo $OutDir}
$OutDir=[IO.Path]::GetFullPath($OutDir)
if(Test-Path -LiteralPath $OutDir){throw 'Choose a fresh output directory'}
New-Item -ItemType Directory -Path $OutDir|Out-Null
Copy-Item -LiteralPath $PSCommandPath -Destination "$OutDir/runner.ps1"
Copy-Item -LiteralPath (Resolve-Path $Kernel).Path -Destination "$OutDir/kernel.cdx"
$kernelHash=(Get-FileHash "$OutDir/kernel.cdx").Hash
$vmHash=(Get-FileHash "$repo/tools/codex-vm.exe").Hash
$subject=switch($Unit){lifecycle{'codex/test/apps/desk-worker-lifecycle'}admission{'apps/works/proofs/render-worker-admission'}default{'codex/test/apps/scene-worker-render'}}
$marker=switch($Unit){lifecycle{'desk-worker-lifecycle-end'}admission{'render-admission-end'}default{'scene-worker-end'}}
& pwsh -NoProfile -File build/bundle-app.ps1 -Src "$subject.codex" -Out "$OutDir/template.codex" -InputsOut "$OutDir/inputs.txt"
if($LASTEXITCODE -ne 0){throw 'Source bundling failed'}
$template=[IO.File]::ReadAllText("$OutDir/template.codex").Replace("`r",'')
$positive=[IO.File]::ReadAllText("$repo/$subject.expected").Replace("`r",'').Trim()
$controls=switch($Unit){lifecycle{[ordered]@{positive='';'close-hook'='close-before-reclaim';'replace-hook'='replace-before-reclaim';'hide-hook'='hide-acknowledged';'worker-budget'='budget-pauses-worker'}}admission{[ordered]@{positive='';'color-tail'='held-color';'depth-tail'='held-depth'}}default{[ordered]@{positive='';owner='stale-owner';token='stale-request';closed='closed';borrowed='owned-after-slot-reuse';duplicate='duplicate';'copy-bound'='bounded-copy'}}}
if($Mode -ne 'all' -and -not $controls.Contains($Mode)){throw "Mode $Mode does not apply to $Unit"}
$modes=@(if($Mode -eq 'all'){$controls.Keys}else{$Mode})
$beds=@(if($Bed -eq 'both'){'codex-vm';'ovmf'}else{$Bed})
$results=[Collections.Generic.List[object]]::new()
function Replace-One([string]$body,[string]$old,[string]$new){
    if($body.IndexOf($old) -lt 0 -or $body.IndexOf($old) -ne $body.LastIndexOf($old)){throw "Mutation is not unique: $old"}
    return $body.Replace($old,$new)
}
function Admit {
    $os=Get-CimInstance Win32_OperatingSystem
    "free KB=$($os.FreePhysicalMemory); commit headroom KB=$($os.FreeVirtualMemory); guests=1"
    if($os.FreePhysicalMemory -le 1572864 -or $os.FreeVirtualMemory -lt 3145728){throw 'Memory admission refused'}
}
function Read-Live([string]$Path) {
    if(-not (Test-Path -LiteralPath $Path)){return ''}
    $file=[IO.File]::Open($Path,'Open','Read','ReadWrite')
    try{
        if($file.Length -gt 1MB){throw 'Serial output exceeded diagnostic bound'}
        $reader=[IO.StreamReader]::new($file)
        try{return $reader.ReadToEnd()}finally{$reader.Dispose()}
    }finally{$file.Dispose()}
}
function Run-Bed([string]$Cdx,[string]$Dir,[string]$Which) {
    if($Which -eq 'ovmf'){
        $poolArgs=@(if($OwnedProcessPool){'-OwnedProcessPool'})
        & pwsh -NoProfile -File build/cdx-to-pe.ps1 -CdxInput $Cdx -Out "$Dir/probe.efi" -HeapPages 32768 -ExitBootServices @poolArgs
        if($LASTEXITCODE -ne 0){throw 'PE conversion failed'}
        & pwsh -NoProfile -File build/build-img.ps1 -PeInput "$Dir/probe.efi" -Out "$Dir/probe.img"
        if($LASTEXITCODE -ne 0){throw 'Image build failed'}
        Copy-Item 'D:/Program Files/qemu/share/edk2-x86_64-code.fd' "$Dir/code.fd"
        Copy-Item 'D:/Program Files/qemu/share/edk2-i386-vars.fd' "$Dir/vars.fd"
        Set-ItemProperty "$Dir/vars.fd" -Name IsReadOnly -Value $false
        $exe='D:/Program Files/qemu/qemu-system-x86_64.exe'
        $guestArgs=@('-accel','tcg','-m','2048','-smp','1','-machine','q35','-drive',"if=pflash,format=raw,unit=0,readonly=on,file=$Dir/code.fd",'-drive',"if=pflash,format=raw,unit=1,file=$Dir/vars.fd",'-drive',"format=raw,file=$Dir/probe.img",'-serial',"file:$Dir/serial.log",'-display','none','-vga','std','-no-reboot')
    }else{
        $exe="$repo/tools/codex-vm.exe"
        $guestArgs=@('-kernel',$Cdx,'-output',"$Dir/serial.log",'-mem','2048','-headless','-smp','1')
    }
    Admit
    $guest=Start-Process $exe -ArgumentList @($guestArgs|ForEach-Object{'"'+$_+'"'}) -WindowStyle Hidden -PassThru -RedirectStandardError "$Dir/guest.err"
    $guest.Id | Set-Content "$Dir/guest.pid"
    "owned $Which PID=$($guest.Id); log=$Dir/serial.log"
    try{
        if($Which -eq 'codex-vm'){
            if(-not $guest.WaitForExit(45000)){throw 'Native guest timeout'}
            $stderr=Read-Live "$Dir/guest.err"
            if($guest.ExitCode -ne 1 -or $stderr -notmatch 'FINAL: debug_exit_code=0 process_exit=1' -or $stderr -match 'DROPPED|Triple fault|HOST CRASH'){throw 'Invalid native exit; see guest.err'}
        }else{
            $watch=[Diagnostics.Stopwatch]::StartNew()
            do{
                Start-Sleep -Milliseconds 100
                $serial=Read-Live "$Dir/serial.log"
                if($guest.HasExited){throw 'OVMF exited before completion'}
            }until($serial.Contains($marker) -or $watch.Elapsed.TotalSeconds -ge 45)
            if(-not $serial.Contains($marker)){throw 'OVMF guest timeout'}
        }
    }finally{
        if(-not $guest.HasExited){Stop-Process -Id $guest.Id -Force}
        $guest.WaitForExit();$guest.Dispose()
    }
}
try{
    foreach($modeName in $modes){
        $dir=Join-Path $OutDir $modeName
        New-Item -ItemType Directory $dir|Out-Null
        $source=$template
        if($OwnedProcessPool){
            $source=Replace-One $source 'initialized <- runtime-init 0' ('initialized <- runtime-init 0'+"`n"+'    print-line-uni ("\nproof-owned-pool=" & show (peek-32 126976 8 == 4 & peek-32 126976 12 >= 256 & peek-qword 126976 248 == 536870912 & peek-qword 127200 0 > 0))')
        }
        switch($modeName){
            color-tail {$source=Replace-One $source 'rwa-control : Integer = 0' 'rwa-control : Integer = 1'}
            depth-tail {$source=Replace-One $source 'rwa-control : Integer = 0' 'rwa-control : Integer = 2'}
            owner {
                $source=Replace-One $source 'peek-qword cell grm-owner == owner & peek-qword cell grm-stop == 0' 'peek-qword cell grm-stop == 0'
                $source=Replace-One $source 'peek-qword cell grm-reply-owner == owner & peek-qword cell grm-state == 2' 'peek-qword cell grm-state == 2'
            }
            token {
                $source=Replace-One $source 'in token > 0 & peek-qword cell grm-reply == token & peek-qword cell grm-reply-owner == owner' 'in token > 0 & peek-qword cell grm-reply-owner == owner'
            }
            closed {
                $source=Replace-One $source "grm-close (cell) (owner) =`n    if grm-current cell owner == False then 0" "grm-close (cell) (owner) =`n    if True then 0"
            }
            borrowed {
                $source=Replace-One $source 'else let completed = target.r3t-base' 'else let completed = peek-qword cell grm-pixels'
            }
            duplicate {
                $source=Replace-One $source ' & peek-qword cell grm-ack /= token & peek-qword cell grm-pixels > 0' ' & peek-qword cell grm-pixels > 0'
            }
            copy-bound {
                $source=Replace-One $source 'grm-copy-pixels : Integer = 1024' 'grm-copy-pixels : Integer = 2048'
            }
            close-hook {
                $source=Replace-One $source "desk-render-close (ds) (fid) (depth) =`n    let cell = peek-32 ds dk-render-cell" "desk-render-close (ds) (fid) (depth) =`n    let cell = 0"
            }
            replace-hook {
                $source=Replace-One $source 'in let canceled = if controller == 0 then 0 else grm-close controller (peek-qword controller grm-owner)' 'in let canceled = 0'
            }
            hide-hook {
                $source=Replace-One $source "desk-render-pause (ds) (fid) =`n    let cell = peek-32 ds dk-render-cell" "desk-render-pause (ds) (fid) =`n    let cell = 0"
            }
            worker-budget {
                $source=Replace-One $source 'else if peek-qword cell grm-allowed == 0 then act' 'else if False then act'
            }
        }
        [IO.File]::WriteAllText("$dir/source.codex",$source,[Text.UTF8Encoding]::new($false))
        $expected=$positive
        if($modeName -ne 'positive'){$expected=Replace-One $expected ($controls[$modeName]+'=True') ($controls[$modeName]+'=False')}
        [IO.File]::WriteAllText("$dir/expected.txt",$expected+"`n")
        Admit
        & pwsh -NoProfile -File build/compile.ps1 -Src "$dir/source.codex" -Out "$dir/probe.cdx" -Log "$dir/compile.log" -Kernel "$OutDir/kernel.cdx"
        if($LASTEXITCODE -ne 0){Get-Content "$dir/compile.log";throw "Compile failed: $modeName"}
        $cdxHash=(Get-FileHash "$dir/probe.cdx").Hash
        foreach($bedName in $beds){
            $run=Join-Path $dir $bedName
            New-Item -ItemType Directory $run|Out-Null
            Run-Bed "$dir/probe.cdx" $run $bedName
            $serial=(Read-Live "$run/serial.log").Replace("`r",'')
            if($OwnedProcessPool -and @([regex]::Matches($serial,'(?m)^proof-owned-pool=True$')).Count -ne 1){throw 'Guest did not confirm the owned process pool'}
            $begin=$serial.IndexOf("setup=")
            $trace=if($begin -ge 0){$serial.Substring($begin).Trim()}else{''}
            $pass=$trace -ceq $expected -and $serial -notmatch '!EXC'
            if((Get-FileHash "$dir/probe.cdx").Hash -ne $cdxHash -or (Get-FileHash "$OutDir/kernel.cdx").Hash -ne $kernelHash -or (Get-FileHash "$repo/tools/codex-vm.exe").Hash -ne $vmHash){throw 'Proof artifact changed during run'}
            $grade=if($pass){if($modeName -eq 'positive'){'PASS'}else{'CONTROL-PASS'}}else{'FAIL'}
            $record=[ordered]@{unit=$Unit;arm=$modeName;bed=$bedName;grade=$grade;kernel=$kernelHash;vm=$vmHash;source=(Get-FileHash "$dir/source.codex").Hash;cdx=$cdxHash;log="$run/serial.log"}
            $record.processPool=if($bedName -eq 'codex-vm'){'native-fixed'}elseif($OwnedProcessPool){'uefi-owned'}else{'uefi-legacy'}
            if($bedName -eq 'ovmf'){$record.image=(Get-FileHash "$run/probe.img").Hash;$record.firmware=(Get-FileHash "$run/code.fd").Hash}
            $results.Add([pscustomobject]$record)
            [IO.File]::WriteAllText("$OutDir/results.json",(ConvertTo-Json -InputObject @($results.ToArray()) -Depth 5))
            "$modeName $bedName $grade"
            $trace
            if(-not $pass){throw "Unexpected trace: $modeName $bedName"}
        }
    }
    '0'|Set-Content "$OutDir/exit.code"
}catch{
    '1'|Set-Content "$OutDir/exit.code"
    throw
}

