[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$GamePath,
    [Parameter(Mandatory)][string]$ManagedLibrary,
    [Parameter(Mandatory)][string]$OutDirectory,
    [string]$Kernel = '',
    [string]$Template = ''
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$GamePath=(Resolve-Path -LiteralPath $GamePath).Path
$ManagedLibrary=(Resolve-Path -LiteralPath $ManagedLibrary).Path
$OutDirectory=[IO.Path]::GetFullPath($OutDirectory)
if(-not $Kernel){$Kernel=Join-Path $repo 'seed/Codex.cdx'}
if(Test-Path -LiteralPath $OutDirectory){throw 'Proof output directory must be new'}
[void](New-Item -ItemType Directory -Path $OutDirectory)
foreach($name in @('first','second')){
    & pwsh -NoProfile -File (Join-Path $repo 'codex/plugs/unity/package.ps1') -ManagedLibrary $ManagedLibrary -OutDirectory (Join-Path $OutDirectory $name) -Kernel $Kernel
    if($LASTEXITCODE -ne 0){throw "Package $name failed"}
}
$first=Join-Path $OutDirectory 'first/winhttp.dll'
$second=Join-Path $OutDirectory 'second/winhttp.dll'
if((Get-FileHash $first).Hash -cne (Get-FileHash $second).Hash){throw 'Loader emission is nondeterministic'}
Write-Host 'PASS identical loader bytes from identical inputs'
$reader=Join-Path $OutDirectory 'exports.cdx'
$free=(Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory
if($free -le 1572864){throw "RAM admission refused: $free KiB"}
& pwsh -NoProfile -File (Join-Path $repo 'build/compile.ps1') -Src (Join-Path $PSScriptRoot 'pe-exports-probe.codex') -Out $reader -Log (Join-Path $OutDirectory 'exports.compile.log') -Kernel $Kernel
if($LASTEXITCODE -ne 0){throw 'Export reader compilation failed'}
$exports=@{}
foreach($entry in @(@('system',(Join-Path ([Environment]::SystemDirectory) 'winhttp.dll')),@('loader',$first))){
    $inputBytes=[IO.File]::ReadAllBytes($entry[1])
    $disk=[byte[]]::new(($inputBytes.Length+511) -band -512)
    [Array]::Copy($inputBytes,$disk,$inputBytes.Length)
    $diskPath=Join-Path $OutDirectory ($entry[0]+'.disk')
    $output=Join-Path $OutDirectory ($entry[0]+'.exports')
    [IO.File]::WriteAllBytes($diskPath,$disk)
    $free=(Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory
    if($free -le 1572864){throw "RAM admission refused: $free KiB"}
    & pwsh -NoProfile -File (Join-Path $repo 'build/test-run.ps1') -Kernel $reader -DiskFile $diskPath -OutFile $output
    if($LASTEXITCODE -ne 0){throw 'Export reader execution failed'}
    $lines=[IO.File]::ReadAllLines($output)
    if($lines -notcontains 'END'){throw 'Export reader did not finish'}
    $exports[$entry[0]]=@($lines|Where-Object {$_ -match '^\d+ '}|ForEach-Object {$_ -replace ' -> .*$',''})
}
if(-not $exports.system -or -not [string]::Equals(($exports.system -join "`n"),($exports.loader -join "`n"),[StringComparison]::Ordinal)){throw 'Stage 2 export names or ordinals differ from System32'}
Write-Host 'PASS stage 2 PeExports names and ordinals match System32'
& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'test-pe-forward.ps1') -NativeProbe $first
if($LASTEXITCODE -ne 0){throw 'Loader broke WinHTTP forwarding or rebasing'}
foreach($probe in @('startup','storage')){
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'test-game.ps1') -GamePath $GamePath -Package $first -OutDirectory (Join-Path $OutDirectory $probe) -Probe $probe
    if($LASTEXITCODE -ne 0){throw "Game $probe acceptance failed"}
}
if($Template){
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class BootstrapResource {
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] public static extern IntPtr BeginUpdateResourceW(string path,bool deleteExisting);
 [DllImport("kernel32.dll",SetLastError=true)] public static extern bool UpdateResourceW(IntPtr update,IntPtr type,IntPtr name,ushort language,byte[] data,uint size);
 [DllImport("kernel32.dll",SetLastError=true)] public static extern bool EndUpdateResourceW(IntPtr update,bool discard);
}
'@
    $repacked=Join-Path $OutDirectory 'resource-updated.dll'
    Copy-Item -LiteralPath $Template -Destination $repacked
    $payload=[IO.File]::ReadAllBytes($ManagedLibrary)
    $update=[BootstrapResource]::BeginUpdateResourceW($repacked,$true)
    if($update -eq [IntPtr]::Zero){throw 'Native resource update refused template'}
    $updated=[BootstrapResource]::UpdateResourceW($update,[IntPtr]::new(10),[IntPtr]::new(101),0,$payload,$payload.Length)
    $ended=[BootstrapResource]::EndUpdateResourceW($update,(-not $updated))
    if(-not $updated -or -not $ended){throw 'Native resource update failed'}
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'test-game.ps1') -GamePath $GamePath -Package $repacked -OutDirectory (Join-Path $OutDirectory 'native-resource-storage') -Probe storage
    if($LASTEXITCODE -ne 0){throw 'Native helper resource replacement broke loader acceptance'}
    Write-Host 'PASS template resource replacement follows the native helper path and passes game storage'
}
$original=[IO.File]::ReadAllBytes($first)
$pe=[BitConverter]::ToInt32($original,60)
$optional=$pe+24
foreach($control in @('no-resource','bad-image','missing-mono','missing-mono-early','missing-initializer')){
    $bytes=[byte[]]$original.Clone()
    if($control -eq 'no-resource'){
        [Array]::Clear($bytes,$optional+128,8)
        $expected='REFUSED managed payload missing'
    }elseif($control -eq 'bad-image'){
        $sections=$optional+[BitConverter]::ToUInt16($bytes,$pe+20)
        $resourceRaw=[BitConverter]::ToInt32($bytes,$sections+6*40+20)
        if($bytes[$resourceRaw+88] -ne 77 -or $bytes[$resourceRaw+89] -ne 90){throw 'Control did not reach the embedded assembly signature'}
        $bytes[$resourceRaw+88]=0
        $expected='REFUSED managed image'
    }else{
        $name=if($control -eq 'missing-mono-early'){'mono_method_get_name'}elseif($control -eq 'missing-initializer'){'Initialize'}else{'mono_class_from_name'}
        $needle=[Text.Encoding]::ASCII.GetBytes($name)
        $sections=$optional+[BitConverter]::ToUInt16($bytes,$pe+20)
        $dataRaw=[BitConverter]::ToInt32($bytes,$sections+5*40+20)
        $dataSize=[BitConverter]::ToInt32($bytes,$sections+5*40+16)
        $found=-1
        for($i=$dataRaw;$i -le $dataRaw+$dataSize-$needle.Length;$i++){
            $match=$true
            for($j=0;$j -lt $needle.Length;$j++){if($bytes[$i+$j] -ne $needle[$j]){$match=$false;break}}
            if($match){if($found -ge 0){throw 'Ambiguous Mono control string'};$found=$i}
        }
        if($found -lt 0){throw 'Missing Mono control string'}
        $bytes[$found]=120
        $expected=if($control -eq 'missing-initializer'){'REFUSED managed initializer missing'}else{'REFUSED missing Mono entry points'}
    }
    $dll=Join-Path $OutDirectory ($control+'.dll')
    [IO.File]::WriteAllBytes($dll,$bytes)
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'test-game.ps1') -GamePath $GamePath -Package $dll -OutDirectory (Join-Path $OutDirectory $control) -Probe startup -ExpectedBootstrapRefusal $expected
    if($LASTEXITCODE -ne 0){throw "Refusal control $control failed"}
    Write-Host "PASS $control logs refusal and leaves the game running without managed initialization"
}
Write-Host ('Loader SHA256: '+(Get-FileHash $first).Hash)
