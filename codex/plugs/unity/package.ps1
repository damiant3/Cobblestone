[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ManagedLibrary,
    [Parameter(Mandatory)][string]$OutDirectory,
    [Parameter(Mandatory)][string]$MsvcRoot,
    [Parameter(Mandatory)][string]$WindowsSdkRoot,
    [Parameter(Mandatory)][string]$WindowsSdkVersion
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ManagedLibrary = (Resolve-Path -LiteralPath $ManagedLibrary).Path
$OutDirectory = [IO.Path]::GetFullPath($OutDirectory)
if ((Test-Path -LiteralPath $OutDirectory) -and @(Get-ChildItem -LiteralPath $OutDirectory -Force).Count) { throw 'Package output directory must be empty' }
if ($WindowsSdkVersion -notmatch '^\d+\.\d+\.\d+\.\d+$') { throw 'Invalid Windows SDK version' }
$compiler = Join-Path $MsvcRoot 'bin/Hostx64/x64/cl.exe'
$resourceCompiler = Join-Path $WindowsSdkRoot "bin/$WindowsSdkVersion/x64/rc.exe"
$source = Join-Path $PSScriptRoot 'WindowsBootstrap.cpp'
foreach ($tool in @($compiler, $resourceCompiler, $source)) { if (-not (Test-Path -LiteralPath $tool -PathType Leaf)) { throw "Missing tool or source: $tool" } }
[void](New-Item -ItemType Directory -Path $OutDirectory -Force)
$resource = Join-Path $OutDirectory 'payload.rc'
$resourceObject = Join-Path $OutDirectory 'payload.res'
if ($ManagedLibrary -match '[\x00-\x1f"]') { throw 'Invalid managed library path' }
[IO.File]::WriteAllText($resource, ('101 RCDATA "' + $ManagedLibrary.Replace('\','\\') + '"'), [Text.UTF8Encoding]::new($false))
& $resourceCompiler /nologo /fo $resourceObject $resource
if ($LASTEXITCODE -ne 0) { throw 'Managed payload resource compilation failed' }
$dll = Join-Path $OutDirectory 'winhttp.dll'
. (Join-Path $PSScriptRoot 'windows-proxy.ps1')
$proxy = Write-PrismWindowsProxy -OutDirectory $OutDirectory -MsvcRoot $MsvcRoot
$nativeArgs = @('/nologo','/LD','/MT','/EHsc','/O2','/DUNICODE','/D_UNICODE',
    "/I$OutDirectory", "/I$MsvcRoot/include", "/I$WindowsSdkRoot/Include/$WindowsSdkVersion/ucrt",
    "/I$WindowsSdkRoot/Include/$WindowsSdkVersion/um", "/I$WindowsSdkRoot/Include/$WindowsSdkVersion/shared",
    ('/Fo' + (Join-Path $OutDirectory 'PrismBootstrap.obj')), $source, $resourceObject, $proxy.object, '/link', ('/DEF:' + $proxy.definition), '/IGNORE:4222',
    "/LIBPATH:$MsvcRoot/lib/x64", "/LIBPATH:$WindowsSdkRoot/Lib/$WindowsSdkVersion/um/x64",
    "/LIBPATH:$WindowsSdkRoot/Lib/$WindowsSdkVersion/ucrt/x64",
    ('/IMPLIB:' + (Join-Path $OutDirectory 'PrismBootstrap.lib')), ('/OUT:' + $dll))
$previousPath = $env:PATH
try {
    $env:PATH = (Join-Path $MsvcRoot 'bin/Hostx64/x64') + ';' + $env:PATH
    & $compiler @nativeArgs
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $dll)) { throw 'Native package link failed' }
} finally { $env:PATH = $previousPath }
Test-PrismWindowsProxy -Artifact $dll -MsvcRoot $MsvcRoot -Proxy $proxy
$receipt = [ordered]@{
    artifact = $dll; artifactHash = (Get-FileHash -LiteralPath $dll).Hash;
    managedHash = (Get-FileHash -LiteralPath $ManagedLibrary).Hash;
    bootstrapSource = $source; bootstrapSourceHash = (Get-FileHash -LiteralPath $source).Hash;
    compilerHash = (Get-FileHash -LiteralPath $compiler).Hash;
    windowsProxy = $proxy;
    windowsProxySourceHash = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'windows-proxy.ps1')).Hash;
    runtimeVerified = $false; installed = $false
}
[IO.File]::WriteAllText((Join-Path $OutDirectory 'package.json'), ($receipt | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
"Package: $dll"
