[CmdletBinding()]
param([Parameter(Mandatory)][string]$ReferenceRoot,[Parameter(Mandatory)][string]$OutDirectory)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$reference=(Resolve-Path -LiteralPath $ReferenceRoot).Path
$out=[IO.Path]::GetFullPath($OutDirectory)
$hashes=@{
    'blowfish.cpp'='1AC4200144481102B43D8BBC41CAC5D456CEAA614BE478F4DD47654EDD5B8447'
    'blowfish.h'='18782F09AEED1B8BC10E813A0668AB50BB5E355CAD61C769183B4F54F1B94D9E'
    'cryptbase.h'='CE5B86A59FE7BD28B17085BB9A087A023CC65229715CB24F2B95BACA2E21B7B9'
}
New-Item -ItemType Directory -Force (Join-Path $out 'pol/crypt')|Out-Null
foreach($file in $hashes.Keys){
    $path=Join-Path $reference $file
    if((Get-FileHash -LiteralPath $path).Hash -ne $hashes[$file]){throw "Pinned POL source changed: $file"}
    Copy-Item -LiteralPath $path -Destination (Join-Path $out ('pol/crypt/'+$file)) -Force
}
$header=[IO.File]::ReadAllText((Join-Path $reference 'cryptbase.h'))
$boundary=$header.IndexOf('#include',[StringComparison]::Ordinal)
if($boundary -lt 0){throw 'Foreign header dependency boundary absent'}
$constants=$header.Substring(0,$boundary)+"`n#endif`n"
[IO.File]::WriteAllText((Join-Path $out 'oracle-constants.h'),$constants,[Text.UTF8Encoding]::new($false))
$vcvars='C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat'
if(-not (Test-Path -LiteralPath $vcvars)){throw 'MSVC 2022 environment unavailable'}
$environment=@(cmd /c '"C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat" >nul 2>&1 && set')
if($LASTEXITCODE -ne 0){throw 'MSVC environment failed'}
foreach($line in $environment){
    if($line -match '^(PATH|INCLUDE|LIB|LIBPATH)=(.*)$'){
        [Environment]::SetEnvironmentVariable($Matches[1],$Matches[2],'Process')
    }
}
$compiler=(Get-Command cl.exe -ErrorAction Stop).Source
Push-Location $out
try{
    foreach($name in @('cipher-oracle','encrypt-replay')){
        & $compiler /nologo /O2 /std:c++17 /EHsc /I. /FIoracle-constants.h (Join-Path $PSScriptRoot ($name+'.cpp')) 'pol/crypt/blowfish.cpp' ('/Fe:'+$name+'.exe')
        if($LASTEXITCODE -ne 0){throw "Foreign witness build failed: $name"}
    }
}finally{Pop-Location}
'Built unchanged POL cipher with its constants/macros; socket declarations excluded by the original header guard.'
