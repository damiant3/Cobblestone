[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutDirectory)
$ErrorActionPreference='Stop'
$out=[IO.Path]::GetFullPath($OutDirectory)
if(Test-Path $out){throw 'OutDirectory must be new'}
[void](New-Item -ItemType Directory $out)
$vcvars='C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat'
if(-not (Test-Path $vcvars)){throw 'vcvars64.bat not found'}
$source=Join-Path $PSScriptRoot 'nat-receive-test.c'
$exe=Join-Path $out 'nat-receive-test.exe'
$obj=Join-Path $out 'nat-receive-test.obj'
$command='"{0}" >nul 2>&1 && cl /O2 /W3 /Brepro /Fe:"{1}" /Fo:"{2}" "{3}" /link WinHvPlatform.lib ws2_32.lib winmm.lib /Brepro' -f $vcvars,$exe,$obj,$source
& cmd /c $command *> (Join-Path $out 'compile.log')
if($LASTEXITCODE -ne 0){Get-Content (Join-Path $out 'compile.log');throw 'Native test compile failed'}
& $exe > (Join-Path $out 'actual.txt') 2> (Join-Path $out 'stderr.txt')
$code=$LASTEXITCODE
Get-Content (Join-Path $out 'actual.txt')
@{sourceHash=(Get-FileHash (Join-Path $PSScriptRoot 'codex-vm.c')).Hash;testHash=(Get-FileHash $source).Hash;exitCode=$code;passed=($code -eq 0)} | ConvertTo-Json | Set-Content (Join-Path $out 'result.json')
if($code -ne 0){throw 'Native TCP sequence admission failed'}
