[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Kernel,
    [Parameter(Mandatory)][string]$OutDirectory,
    [switch]$Poison
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$compiler=(Resolve-Path -LiteralPath $Kernel).Path
$out=[IO.Path]::GetFullPath($OutDirectory)
if(Test-Path -LiteralPath $out){throw 'OutDirectory must be new'}
[void](New-Item -ItemType Directory -Path $out)
$receipt=[ordered]@{kernel=(Get-FileHash $compiler).Hash;poison=[bool]$Poison;runs=@();passed=$false}
function Admit {
    $free=(Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory
    if($free -lt 1572864){throw 'Less than 1.5 GiB free before guest'}
    return $free
}
try {
    $receipt.freeCompile=Admit
    $cdx=Join-Path $out 'GameStoreProof.cdx'
    $argv=@('-NoProfile','-File',(Join-Path $repo 'build/compile.ps1'),'-Src',(Join-Path $PSScriptRoot 'GameStoreProof.codex'),'-Out',$cdx,'-Log',(Join-Path $out 'compile.log'),'-Kernel',$compiler)
    if($Poison){$argv+='-Poison'}
    & pwsh @argv
    if($LASTEXITCODE -ne 0){Get-Content (Join-Path $out 'compile.log');throw 'GameStore proof compile failed'}
    $receipt.artifact=(Get-FileHash $cdx).Hash
    $disk=Join-Path $out 'game-store.disk'
    $file=[IO.File]::Open($disk,[IO.FileMode]::CreateNew,[IO.FileAccess]::ReadWrite)
    try{$file.SetLength(4194304)}finally{$file.Dispose()}
    foreach($mode in @('write','read')){
        $free=Admit
        $modeFile=Join-Path $out ($mode+'.input')
        [IO.File]::WriteAllText($modeFile,$mode+"`n",[Text.UTF8Encoding]::new($false))
        $raw=Join-Path $out ($mode+'.raw')
        $err=Join-Path $out ($mode+'.stderr')
        $vmArgs=@('-kernel',('"'+$cdx+'"'),'-disk',('"'+$disk+'"'),'-input',('"'+$modeFile+'"'),'-output',('"'+$raw+'"'),'-mem','3072','-headless')
        $p=Start-Process (Join-Path $repo 'tools/codex-vm.exe') -WindowStyle Hidden -PassThru -ArgumentList $vmArgs -RedirectStandardError $err
        try{
            if(-not $p.WaitForExit(60000)){throw "$mode guest timed out"}
            $receipt.runs+=@{mode=$mode;pid=$p.Id;exit=$p.ExitCode;freeKiB=$free;disk=(Get-FileHash $disk).Hash}
            $stderr=[IO.File]::ReadAllText($err)
            if($p.ExitCode -ne 1 -or $stderr -notmatch 'FINAL: debug_exit_code=0 process_exit=1'){throw "$mode guest exit failed: $stderr"}
            if($stderr -match 'DROPPED|cannot be opened for write|every write.*LOST|(?:nopath|nodata|oob|openfail)=[1-9]'){throw "$mode lost output or disk writes"}
            $text=[IO.File]::ReadAllText($raw) -replace "`r",''
            $body=((($text -split "`n" | Where-Object {$_ -notmatch '^(HEAP:|WD:|STACK:)'}) -join "`n").TrimEnd([char]10))+"`n"
            [IO.File]::WriteAllText((Join-Path $out ($mode+'.actual')),$body)
            $expected=[IO.File]::ReadAllText((Join-Path $PSScriptRoot ('GameStoreProof.'+$mode+'.expected'))) -replace "`r",''
            if($body -cne $expected){$body;throw "$mode exact oracle mismatch"}
            if($mode -eq 'write'){
                $bytes=[IO.File]::ReadAllBytes($disk)
                $at=0;$lastSequence=-1
                while($at+512 -le $bytes.Length -and [BitConverter]::ToInt64($bytes,$at) -eq 0x31534F55){
                    $length=[BitConverter]::ToInt64($bytes,$at+40)
                    if($length -lt 1 -or $length -gt 4194304){throw 'Invalid fixture record extent'}
                    $lastSequence=[BitConverter]::ToInt64($bytes,$at+24)
                    $at+=[int](512*(1+[Math]::Ceiling($length/512.0)))
                }
                if($lastSequence -ne 3 -or $at+1024 -gt $bytes.Length){throw 'Pending fixture boundary absent'}
                for($i=0;$i -lt 512;$i++){if($bytes[$at+$i] -ne 0){throw 'Pending fixture commit header is not empty'}}
                if($bytes[$at+512] -ne 85){throw 'Pending fixture payload is not present'}
                $receipt.pendingSector=$at/512
            }
            "PASS game store $mode"
        }finally{
            if(-not $p.HasExited){Stop-Process -Id $p.Id}
            $p.Dispose()
        }
    }
    if($receipt.runs.Count -ne 2 -or $receipt.runs[0].disk -ne $receipt.runs[1].disk){throw 'Recovery changed disk or proof incomplete'}
    $receipt.passed=$true
}finally{
    [IO.File]::WriteAllText((Join-Path $out 'result.json'),($receipt | ConvertTo-Json -Depth 6))
    "Evidence: $out/result.json"
}
