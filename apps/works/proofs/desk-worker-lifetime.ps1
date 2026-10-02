[CmdletBinding()]
param(
    [ValidateSet('both','codex-vm','ovmf')][string]$Bed='both',
    [ValidateSet('all','positive','transient','generation','closed','borrowed','duplicate')][string]$Mode='all',
    [string]$Kernel='seed/Codex.cdx',
    [string]$OutDir=''
)
$ErrorActionPreference='Stop'
$repo=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
Set-Location $repo
if(-not $OutDir){$OutDir=Join-Path $repo ('build-output/desk-worker-lifetime-'+[Guid]::NewGuid().ToString('N'))}
if(-not [IO.Path]::IsPathRooted($OutDir)){$OutDir=Join-Path $repo $OutDir}
$OutDir=[IO.Path]::GetFullPath($OutDir)
if(Test-Path -LiteralPath $OutDir){throw 'Choose a fresh output directory'}
New-Item -ItemType Directory -Path $OutDir | Out-Null
Copy-Item -LiteralPath $PSCommandPath -Destination "$OutDir/runner.ps1"
Copy-Item -LiteralPath (Resolve-Path $Kernel).Path -Destination "$OutDir/kernel.cdx"
$kernelHash=(Get-FileHash "$OutDir/kernel.cdx").Hash
$vmHash=(Get-FileHash "$repo/tools/codex-vm.exe").Hash
& pwsh -NoProfile -File build/bundle-app.ps1 -Src "$PSScriptRoot/desk-worker-lifetime.codex" -Out "$OutDir/template.codex" -InputsOut "$OutDir/inputs.txt"
if($LASTEXITCODE -ne 0){throw 'Source bundling failed'}
$template=[IO.File]::ReadAllText("$OutDir/template.codex")
$needle='lifetime-control : Integer = 0'
if($template.IndexOf($needle) -lt 0 -or $template.IndexOf($needle) -ne $template.LastIndexOf($needle)){throw 'Control declaration is not unique'}
$controls=[ordered]@{positive=0;transient=1;generation=2;closed=3;borrowed=4;duplicate=5}
$modes=@(if($Mode -eq 'all'){$controls.Keys}else{$Mode})
$beds=@(if($Bed -eq 'both'){'codex-vm';'ovmf'}else{$Bed})
$results=[Collections.Generic.List[object]]::new()
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
        & pwsh -NoProfile -File build/cdx-to-pe.ps1 -CdxInput $Cdx -Out "$Dir/probe.efi" -HeapPages 32768 -ExitBootServices
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
            }until($serial.Contains('worker-lifetime-end') -or $watch.Elapsed.TotalSeconds -ge 45)
            if(-not $serial.Contains('worker-lifetime-end')){throw 'OVMF guest timeout'}
        }
    }finally{
        if(-not $guest.HasExited){Stop-Process -Id $guest.Id -Force}
        $guest.WaitForExit();$guest.Dispose()
    }
}
try{
    foreach($modeName in $modes){
        $dir=Join-Path $OutDir $modeName
        New-Item -ItemType Directory $dir | Out-Null
        $control=$controls[$modeName]
        [IO.File]::WriteAllText("$dir/source.codex",$template.Replace($needle,"lifetime-control : Integer = $control"),[Text.UTF8Encoding]::new($false))
        Admit
        & pwsh -NoProfile -File build/compile.ps1 -Src "$dir/source.codex" -Out "$dir/probe.cdx" -Log "$dir/compile.log" -Kernel "$OutDir/kernel.cdx"
        if($LASTEXITCODE -ne 0){Get-Content "$dir/compile.log";throw "Compile failed: $modeName"}
        $expected=@("control=$control",'setup=True',('transient='+$(if($control -eq 1){'False'}else{'True'})),('owned-copy='+$(if($control -in 1,4){'False'}else{'True'})),('stale='+$(if($control -eq 2){'False'}else{'True'})),('closed='+$(if($control -eq 3){'False'}else{'True'})),('duplicate='+$(if($control -eq 5){'False'}else{'True'})),'worker-lifetime-end') -join "`n"
        [IO.File]::WriteAllText("$dir/expected.txt",$expected+"`n")
        foreach($bedName in $beds){
            $run=Join-Path $dir $bedName
            New-Item -ItemType Directory $run | Out-Null
            Run-Bed "$dir/probe.cdx" $run $bedName
            $serial=(Read-Live "$run/serial.log").Replace("`r",'')
            $begin=$serial.IndexOf("control=$control`n")
            $trace=if($begin -ge 0){$serial.Substring($begin)}else{''}
            $pass=$trace -ceq ($expected+"`n") -and $serial -notmatch '!EXC'
            $grade=if($pass){if($control -eq 0){'PASS'}else{'CONTROL-PASS'}}else{'FAIL'}
            $record=[ordered]@{arm=$modeName;bed=$bedName;grade=$grade;kernel=$kernelHash;vm=$vmHash;source=(Get-FileHash "$dir/source.codex").Hash;cdx=(Get-FileHash "$dir/probe.cdx").Hash;log="$run/serial.log"}
            if($bedName -eq 'ovmf'){$record.image=(Get-FileHash "$run/probe.img").Hash;$record.firmware=(Get-FileHash "$run/code.fd").Hash}
            $results.Add([pscustomobject]$record)
            [IO.File]::WriteAllText("$OutDir/results.json",(ConvertTo-Json -InputObject @($results.ToArray()) -Depth 5))
            "$modeName $bedName $grade"
            $trace
            if(-not $pass){throw "Unexpected trace: $modeName $bedName"}
        }
    }
    '0' | Set-Content "$OutDir/exit.code"
}catch{
    '1' | Set-Content "$OutDir/exit.code"
    throw
}
