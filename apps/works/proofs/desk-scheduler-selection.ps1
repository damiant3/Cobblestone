[CmdletBinding()]
param(
    [ValidateSet('both','codex-vm','ovmf')][string]$Bed='both',
    [ValidateSet('all','pair','three','wake','background','background-two')][string]$Case='all',
    [ValidateSet('both','yield','timer')][string]$Dispatch='both',
    [ValidateSet('all','positive','sabotage')][string]$Mode='all',
    [string]$Kernel='seed/Codex.cdx',
    [string]$OutDir=''
)
$ErrorActionPreference='Stop'
$repo=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
Set-Location $repo
if(-not $OutDir){$OutDir=Join-Path $repo ('build-output/desk-selection-'+[Guid]::NewGuid().ToString('N'))}
if(-not [IO.Path]::IsPathRooted($OutDir)){$OutDir=Join-Path $repo $OutDir}
$OutDir=[IO.Path]::GetFullPath($OutDir)
if(Test-Path -LiteralPath $OutDir){throw 'Choose a fresh output directory'}
New-Item -ItemType Directory -Path $OutDir | Out-Null
Copy-Item -LiteralPath $PSCommandPath -Destination "$OutDir/runner.ps1"
$kernelSource=(Resolve-Path -LiteralPath $Kernel).Path
$kernelHash=(Get-FileHash -LiteralPath $kernelSource).Hash
Copy-Item -LiteralPath $kernelSource -Destination "$OutDir/kernel.cdx"
if((Get-FileHash "$OutDir/kernel.cdx").Hash -ne $kernelHash){throw 'Compiler snapshot differs'}
$vmHash=(Get-FileHash "$repo/tools/codex-vm.exe").Hash
& pwsh -NoProfile -File build/bundle-app.ps1 -Src "$repo/codex/test/apps/desk-scheduler-selection.codex" -Out "$OutDir/template.codex" -InputsOut "$OutDir/inputs.txt"
if($LASTEXITCODE -ne 0){throw 'Source bundling failed'}
$template=[IO.File]::ReadAllText("$OutDir/template.codex")
$cases=[ordered]@{pair=1;three=4;wake=2;background=3;'background-two'=5}
$caseNames=@(if($Case -eq 'all'){$cases.Keys}else{$Case})
$dispatches=@(if($Dispatch -eq 'both'){'yield';'timer'}else{$Dispatch})
$modes=@(if($Mode -eq 'all'){'positive';'sabotage'}else{$Mode})
$beds=@(if($Bed -eq 'both'){'codex-vm';'ovmf'}else{$Bed})
$results=[Collections.Generic.List[object]]::new()
function Admit {
    $os=Get-CimInstance Win32_OperatingSystem
    $mem=Get-CimInstance Win32_PerfFormattedData_PerfOS_Memory
    "free KB=$($os.FreePhysicalMemory) commit=$($mem.CommittedBytes) limit=$($mem.CommitLimit) guests=1"
    if($os.FreePhysicalMemory -le 1572864 -or $mem.CommitLimit-$mem.CommittedBytes -lt 3GB){throw 'Memory admission refused'}
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
function Replace-One([string]$Text,[string]$Old,[string]$New) {
    if($Text.IndexOf($Old) -lt 0 -or $Text.IndexOf($Old) -ne $Text.LastIndexOf($Old)){throw "Mutation not unique: $Old"}
    $Text.Replace($Old,$New)
}
function Run-Native([string]$Cdx,[string]$Dir) {
    Admit
    $args=@('-kernel',$Cdx,'-output',"$Dir/serial.log",'-mem','2048','-headless','-smp','1')
    $guest=Start-Process "$repo/tools/codex-vm.exe" -ArgumentList @($args | ForEach-Object {'"'+$_+'"'}) -WindowStyle Hidden -PassThru -RedirectStandardError "$Dir/guest.err"
    $guest.Id | Set-Content "$Dir/guest.pid"
    "owned codex-vm PID=$($guest.Id) log=$Dir/serial.log"
    try{
        if(-not $guest.WaitForExit(45000)){throw 'Native guest timeout'}
        $exitCode=$guest.ExitCode
        $stderr=[IO.File]::ReadAllText("$Dir/guest.err")
        if($exitCode -ne 1 -or -not $stderr.Contains('FINAL: debug_exit_code=0 process_exit=1')){throw "Native exit $exitCode; see guest.err"}
        if($stderr -match 'DROPPED|FAIL|Triple fault'){throw 'Native stderr reports failure'}
    }finally{
        if(-not $guest.HasExited){Stop-Process -Id $guest.Id -Force}
        $guest.WaitForExit();$guest.Dispose()
    }
}
function Run-Ovmf([string]$Cdx,[string]$Dir) {
    & pwsh -NoProfile -File build/cdx-to-pe.ps1 -CdxInput $Cdx -Out "$Dir/probe.efi" -HeapPages 32768 -ExitBootServices
    if($LASTEXITCODE -ne 0){throw 'PE conversion failed'}
    & pwsh -NoProfile -File build/build-img.ps1 -PeInput "$Dir/probe.efi" -Out "$Dir/probe.img"
    if($LASTEXITCODE -ne 0){throw 'Image build failed'}
    Copy-Item -LiteralPath 'D:/Program Files/qemu/share/edk2-x86_64-code.fd' -Destination "$Dir/code.fd"
    Copy-Item -LiteralPath 'D:/Program Files/qemu/share/edk2-i386-vars.fd' -Destination "$Dir/vars.fd"
    Set-ItemProperty -LiteralPath "$Dir/vars.fd" -Name IsReadOnly -Value $false
    Admit
    $args=@('-accel','tcg','-m','2048','-smp','1','-machine','q35','-drive',"if=pflash,format=raw,unit=0,readonly=on,file=$Dir/code.fd",'-drive',"if=pflash,format=raw,unit=1,file=$Dir/vars.fd",'-drive',"format=raw,file=$Dir/probe.img",'-serial',"file:$Dir/serial.log",'-display','none','-vga','std','-no-reboot')
    $guest=Start-Process 'D:/Program Files/qemu/qemu-system-x86_64.exe' -ArgumentList @($args | ForEach-Object {'"'+$_+'"'}) -WindowStyle Hidden -PassThru -RedirectStandardError "$Dir/guest.err"
    $guest.Id | Set-Content "$Dir/guest.pid"
    "owned OVMF PID=$($guest.Id) log=$Dir/serial.log"
    try{
        $watch=[Diagnostics.Stopwatch]::StartNew()
        do{
            Start-Sleep -Milliseconds 100
            $serial=Read-Live "$Dir/serial.log"
            if($guest.HasExited){throw 'OVMF exited before capture completed'}
        }until($serial.Contains('scheduler-selection-end') -or $watch.Elapsed.TotalSeconds -ge 60)
        if(-not $serial.Contains('scheduler-selection-end')){throw 'OVMF guest timeout'}
    }finally{
        if(-not $guest.HasExited){Stop-Process -Id $guest.Id -Force}
        $guest.WaitForExit();$guest.Dispose()
    }
}
foreach($caseName in $caseNames){foreach($dispatchName in $dispatches){foreach($modeName in $modes){
    $arm="$caseName-$dispatchName-$modeName"
    $dir=Join-Path $OutDir $arm
    New-Item -ItemType Directory -Path $dir | Out-Null
    $source=Replace-One $template 'selection-case : Integer = 4' "selection-case : Integer = $($cases[$caseName])"
    $source=Replace-One $source 'selection-timer : Integer = 1' ('selection-timer : Integer = '+$(if($dispatchName -eq 'timer'){1}else{0}))
    $source=Replace-One $source 'selection-sabotage : Integer = 0' ('selection-sabotage : Integer = '+$(if($modeName -eq 'sabotage'){1}else{0}))
    $source=Replace-One $source 'selection-verbose : Integer = 0' 'selection-verbose : Integer = 1'
    [IO.File]::WriteAllText("$dir/source.codex",$source,[Text.UTF8Encoding]::new($false))
    Admit
    & pwsh -NoProfile -File build/compile.ps1 -Src "$dir/source.codex" -Out "$dir/probe.cdx" -Log "$dir/compile.log" -Kernel "$OutDir/kernel.cdx"
    if($LASTEXITCODE -ne 0){Get-Content "$dir/compile.log";throw "Compile failed: $arm"}
    foreach($bedName in $beds){
        $run=Join-Path $dir $bedName
        New-Item -ItemType Directory -Path $run | Out-Null
        if($bedName -eq 'codex-vm'){Run-Native "$dir/probe.cdx" $run}else{Run-Ovmf "$dir/probe.cdx" $run}
        $serial=Read-Live "$run/serial.log"
        $serial=$serial.Replace("`r",'')
        $timer=if($dispatchName -eq 'timer'){1}else{0}
        $sabotage=if($modeName -eq 'sabotage'){1}else{0}
        $window=if($timer -eq 1 -and $caseName -eq 'background-two'){2500}elseif($timer -eq 1 -and $caseName -eq 'background'){1250}else{80}
        $header="case=$($cases[$caseName]) timer=$timer sabotage=$sabotage window=$window"
        $begin=$serial.IndexOf($header+"`n")
        if($begin -lt 0 -or -not $serial.Contains('scheduler-selection-end') -or $serial.Contains('!EXC')){throw "Incomplete/faulted trace: $arm $bedName"}
        $trace=$serial.Substring($begin)
        if($trace -notmatch 'setup=True complete=True rate=[1-9][0-9]*'){throw "Fixture setup or horizon failed: $arm $bedName"}
        $verdict=[regex]::Matches($trace,'(?m)^accepted=(True|False)$')
        if($verdict.Count -ne 1){throw 'Missing or repeated verdict'}
        $accepted=$verdict[0].Groups[1].Value -eq 'True'
        if($modeName -eq 'sabotage'){
            $reason=if($caseName -eq 'wake'){
                $wake=[regex]::Match($trace,'wake-ready=True value=77 tick-gap=([0-9]+) us-gap=([0-9]+)')
                $wake.Success -and ([long]$wake.Groups[1].Value -gt 4 -or [long]$wake.Groups[2].Value -gt 50000)
            }elseif($caseName -in @('background','background-two')){$trace -match 'worker=3 samples=0 '}
            else{$trace -match 'worker=2 samples=0 '}
            if(-not $reason){"Sabotage did not reach its designated failure: $arm $bedName"}
        }
        $grade=if($modeName -eq 'positive'){if($accepted){'PASS'}else{'PROPERTY-FAIL'}}else{if(-not $accepted -and $reason){'CONTROL-PASS'}else{'CONTROL-FAIL'}}
        $record=[ordered]@{arm=$arm;bed=$bedName;grade=$grade;accepted=$accepted;kernel=$kernelHash;vm=$vmHash;source=(Get-FileHash "$dir/source.codex").Hash;cdx=(Get-FileHash "$dir/probe.cdx").Hash;log="$run/serial.log"}
        if($bedName -eq 'ovmf'){$record.image=(Get-FileHash "$run/probe.img").Hash;$record.firmware=(Get-FileHash "$run/code.fd").Hash}
        $results.Add([pscustomobject]$record)
        [IO.File]::WriteAllText("$OutDir/results.json",(ConvertTo-Json -InputObject @($results.ToArray()) -Depth 5),[Text.UTF8Encoding]::new($false))
        "$arm $bedName $grade"
        $trace
    }
}}}
if(@($results | Where-Object grade -in @('PROPERTY-FAIL','CONTROL-FAIL')).Count){'1' | Set-Content "$OutDir/exit.code";exit 1}
'0' | Set-Content "$OutDir/exit.code"
exit 0
