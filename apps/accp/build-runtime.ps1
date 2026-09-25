[CmdletBinding()]
param([string]$Kernel='', [string]$OutDir='', [switch]$Reuse)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$repo=(Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if(-not $Kernel){$Kernel=Join-Path $repo 'seed/Codex.cdx'}
$Kernel=(Resolve-Path -LiteralPath $Kernel).Path
if(-not $OutDir){$OutDir=Join-Path $PSScriptRoot 'build-output'}
New-Item -ItemType Directory -Force -Path $OutDir|Out-Null
$OutDir=(Resolve-Path -LiteralPath $OutDir).Path
$scratch=Join-Path $OutDir 'runtime-build'
New-Item -ItemType Directory -Force -Path $scratch|Out-Null
$utf8=[Text.UTF8Encoding]::new($false)
$assembler=(Get-Command wat2wasm -ErrorAction Stop).Source
$seedHash=(Get-FileHash -LiteralPath $Kernel).Hash.ToLowerInvariant()
$inputs=[ordered]@{}
foreach($root in @('codex/compiler','codex/foreword','codex/plugs/common','codex/plugs/wasm')){
    $recursive=$root -in @('codex/compiler','codex/foreword')
    foreach($file in (Get-ChildItem -LiteralPath (Join-Path $repo $root) -Recurse:$recursive -Filter '*.codex' -File | Where-Object { $_.FullName -notmatch '[\\/]build-output[\\/]' } | Sort-Object FullName)){
        $relative=[IO.Path]::GetRelativePath($repo,$file.FullName).Replace('\','/')
        $inputs[$relative]=(Get-FileHash -LiteralPath $file.FullName).Hash.ToLowerInvariant()
    }
}
foreach($relative in @('build/compile.ps1','build/concat-codex-self.ps1','build/compiler-order.txt','build/quire-map.ps1','build/host/windows/quire-map.ps1','build/tool-catalog.json','build/vm-config.ps1','build/work-wire.ps1','build/plug-source-digest.ps1','codex/plugs/common/plug-build-lib.ps1','codex/plugs/pe/cdx-to-pe-console.ps1','apps/accp/build-runtime.ps1','apps/accp/HostIo.cs','tools/codex-vm.exe')){
    $inputs[$relative]=(Get-FileHash -LiteralPath (Join-Path $repo $relative)).Hash.ToLowerInvariant()
}
$identity=[ordered]@{seed=$seedHash;assembler=(Get-FileHash $assembler).Hash.ToLowerInvariant();inputs=$inputs}|ConvertTo-Json -Depth 4 -Compress
$fingerprint=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($utf8.GetBytes($identity))).ToLowerInvariant()
$manifestPath=Join-Path $OutDir 'runtime.json'
if($Reuse -and (Test-Path -LiteralPath $manifestPath)){
    $old=Get-Content -LiteralPath $manifestPath -Raw|ConvertFrom-Json
    if($old.fingerprint -ceq $fingerprint){
        $valid=$true
        foreach($name in @('codex-compiler.wasm','wasm-stdio.wasm')){
            $path=Join-Path $OutDir $name
            if(-not (Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant() -cne $old.files.$name){$valid=$false}
        }
        if($valid){Write-Host "ACCP runtime reused after input and artifact hash verification: $fingerprint";exit 0}
    }
}
. (Join-Path $repo 'codex/plugs/common/plug-build-lib.ps1')
Add-Type -Path (Join-Path $PSScriptRoot 'HostIo.cs')
function Compile-Unit([string]$Source,[string]$Output,[string]$Log,[switch]$IR,[switch]$Hosted){
    $free=(Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory/1MB
    if($free -le 1.5){throw "Compile admission refused: $free GiB free"}
    Write-Host "ACCP runtime compile: $Source; one guest; free RAM $free GiB"
    $options=@('-NoProfile','-File',(Join-Path $repo 'build/compile.ps1'),'-Src',$Source,'-Out',$Output,'-Log',$Log,'-Kernel',$Kernel)
    if($IR){$options+=@('-IrCce','-Passes','text-plug')}
    if($Hosted){$options+=@('-RawFlags','hosted-windows')}
    & pwsh @options
    if($LASTEXITCODE -ne 0){Get-Content -LiteralPath $Log;throw "Runtime compile failed: $Source"}
}
function Run-Bounded([string]$Name,[string]$Executable,[string[]]$Arguments,[byte[]]$InputBytes,[int]$Cap){
    $r=[AccpHost.Child]::Run($Executable,$Arguments,$InputBytes,600000,$Cap,4GB)
    [IO.File]::WriteAllBytes((Join-Path $scratch ($Name+'.stderr')),$r.Error)
    Write-Host "ACCP runtime $Name exit=$($r.ExitCode) limit=$($r.Limit) ms=$($r.Ms) peak_commit_bytes=$($r.PeakMemory)"
    if($r.Limit -or $r.ExitCode -ne 0){throw "$Name failed: $($r.Limit) $($utf8.GetString($r.Error))"}
    return ,$r.Output
}
$lines=[Collections.Generic.List[string]]::new()
foreach($relative in @('codex/compiler/Core/Name.codex','codex/compiler/Core/SourceText.codex','codex/compiler/Types/CodexType.codex','codex/compiler/Ast/AstNodes.codex','codex/compiler/IR/IRChapter.codex','codex/compiler/IR/ConstShare.codex')){
    $drop=if($relative -like '*AstNodes.codex'){@('Deck Copies')}else{@()}
    Add-PlugChapter -Lines $lines -Path (Join-Path $repo $relative) -Quire Wasm -DropSections $drop
}
foreach($relative in @('codex/plugs/common/PlugTypes.codex','codex/plugs/common/IRTextParser.codex','codex/plugs/common/HandlerLift.codex','codex/plugs/wasm/WasmEmitter.codex','codex/plugs/wasm/WasmStdio.codex','codex/plugs/common/PlugStdio.codex')){
    Add-PlugChapter -Lines $lines -Path (Join-Path $repo $relative) -Quire Wasm
}
$prelude=Resolve-PlugForewords $lines
$lensSource=Join-Path $scratch 'wasm-stdio.codex'
[IO.File]::WriteAllText($lensSource,(($prelude+$lines)-join "`n")+"`n",$utf8)
$emitterCdx=Join-Path $scratch 'wasm-emitter.cdx'
$emitterExe=Join-Path $scratch 'wasm-emitter.exe'
Compile-Unit $lensSource $emitterCdx (Join-Path $scratch 'emitter.compile.log') -Hosted
& pwsh -NoProfile -File (Join-Path $repo 'codex/plugs/pe/cdx-to-pe-console.ps1') -CdxInput $emitterCdx -Out $emitterExe
if($LASTEXITCODE -ne 0){throw 'Native WASM emitter packaging failed'}
function Emit-Module([string]$Name,[string]$Source){
    $ir=Join-Path $scratch ($Name+'.ir')
    Compile-Unit $Source $ir (Join-Path $scratch ($Name+'.compile.log')) -IR
    $bytes=[IO.File]::ReadAllBytes($ir)
    if($bytes.Length -gt 32MB){throw 'Runtime IR exceeds 32 MiB'}
    $decoded=ConvertFrom-CceBytesDetailed $bytes
    if($decoded.Unmapped -or $decoded.Malformed -or $decoded.Truncated){throw 'Invalid CCE runtime IR'}
    $bytes=$utf8.GetBytes($decoded.Text)
    if($bytes.Length -gt 32MB){throw 'Unicode runtime IR exceeds 32 MiB'}
    $moduleInput=[byte[]]::new($bytes.Length+1)
    [Buffer]::BlockCopy($bytes,0,$moduleInput,0,$bytes.Length)
    $output=Run-Bounded ($Name+'-emit') $emitterExe @() $moduleInput 128MB
    $wat=Join-Path $scratch ($Name+'.wat')
    [IO.File]::WriteAllBytes($wat,$output)
    if(-not $utf8.GetString($output,0,[Math]::Min(128,$output.Length)).TrimStart().StartsWith('(module')){throw "Missing WAT module: $Name"}
    $wasm=Join-Path $OutDir ($Name+'.wasm')
    $null=Run-Bounded ($Name+'-assemble') $assembler @('--enable-tail-call',$wat,'-o',$wasm) @() 1MB
    if(-not (Test-Path -LiteralPath $wasm) -or (Get-Item -LiteralPath $wasm).Length -gt 16MB){throw "Invalid runtime artifact size: $Name"}
}
Emit-Module 'wasm-stdio' $lensSource
$compilerSource=Join-Path $scratch 'compiler.codex'
& pwsh -NoProfile -File (Join-Path $repo 'build/concat-codex-self.ps1') -OutFile $compilerSource
if($LASTEXITCODE -ne 0){throw 'Compiler source assembly failed'}
Emit-Module 'codex-compiler' $compilerSource
$files=[ordered]@{}
foreach($name in @('codex-compiler.wasm','wasm-stdio.wasm')){$files[$name]=(Get-FileHash -LiteralPath (Join-Path $OutDir $name)).Hash.ToLowerInvariant()}
@{version=1;fingerprint=$fingerprint;seed=$seedHash;inputs=$inputs;assembler_sha256=(Get-FileHash $assembler).Hash.ToLowerInvariant();compiler_source_sha256=(Get-FileHash $compilerSource).Hash.ToLowerInvariant();lens_source_sha256=(Get-FileHash $lensSource).Hash.ToLowerInvariant();native_emitter_sha256=(Get-FileHash $emitterCdx).Hash.ToLowerInvariant();files=$files}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $manifestPath -Encoding utf8
Write-Host "ACCP runtime built from source: $manifestPath"
