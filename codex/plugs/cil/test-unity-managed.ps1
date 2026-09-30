[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Reflection.Metadata
$runtimePath=Join-Path $PSScriptRoot 'build-output/runtime/PrismRuntime.dll'
$runtime=[Reflection.Assembly]::Load([IO.File]::ReadAllBytes($runtimePath))
$runtimeHash=(Get-FileHash $runtimePath).Hash
$resolver=[ResolveEventHandler]{param($sender,$event) if(([Reflection.AssemblyName]::new($event.Name)).Name -eq 'PrismRuntime'){return $runtime};return $null}
[AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)
try {
    $flags=[Reflection.BindingFlags]'Public,NonPublic,Static,Instance,DeclaredOnly'
    $catalogue=Get-Content -LiteralPath (Join-Path $PSScriptRoot '../../../apps/modbuilder/mods/valheim/features.json') -Raw | ConvertFrom-Json
    foreach($name in @($catalogue.features.id)+@('all')){
        $bytes=[IO.File]::ReadAllBytes((Join-Path $PSScriptRoot "build-output/test/unity-$name.dll"))
        $stream=[IO.MemoryStream]::new($bytes,$false)
        $reader=[Reflection.PortableExecutable.PEReader]::new($stream)
        try {
            $metadata=[Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($reader)
            $mvid=$metadata.GetGuid($metadata.GetModuleDefinition().Mvid).ToByteArray()
            $offset=$reader.PEHeaders.MetadataStartOffset+[Reflection.Metadata.Ecma335.MetadataReaderExtensions]::GetHeapMetadataOffset($metadata,[Reflection.Metadata.Ecma335.HeapIndex]::Guid)
            $zeroed=[byte[]]$bytes.Clone();[Array]::Clear($zeroed,$offset,16)
            $digest=[Security.Cryptography.SHA256]::HashData($zeroed)
            if([Convert]::ToHexString($mvid) -ne [Convert]::ToHexString($digest[0..15])){throw "$name resource-bearing module identifier differs"}
        } finally {$reader.Dispose();$stream.Dispose()}
        $assembly=[Reflection.Assembly]::Load($bytes)
        $runtime.GetType('PrismGenerated.PrismRuntimeModel',$true).GetField('model',[Reflection.BindingFlags]'NonPublic,Static').SetValue($null,$assembly)
        $identity=$runtime.GetType('PrismGenerated.PrismRuntimeModel',$true).GetProperty('BuildNumber').GetValue($null)
        if($identity -cne $assembly.ManifestModule.ModuleVersionId.ToString('N')){throw "$name runtime identity differs from the compiled model"}
        $resource=$assembly.GetManifestResourceStream('PrismRuntime.dll')
        if($null -eq $resource){throw "$name adapter resource missing"}
        try {if([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($resource)) -ne $runtimeHash){throw "$name embedded adapter differs"}} finally {$resource.Dispose()}
        foreach($type in $assembly.GetTypes()){
            foreach($method in @($type.GetConstructors($flags))+@($type.GetMethods($flags))){
                try {[Runtime.CompilerServices.RuntimeHelpers]::PrepareMethod($method.MethodHandle)}
                catch {throw "$name $($type.Name).$($method.Name): $($_.Exception)"}
            }
        }
        if($name -eq 'hover'){
            $model=$assembly.GetType('PrismGenerated.Codex_Program',$true)
            $decode=$runtime.GetType('PrismGenerated._Cce',$true).GetMethod('ToUnicode')
            $clock=$model.GetMethod('hover-clock').Invoke($null,@(3701L))
            if($decode.Invoke($null,@($clock)) -cne '1:01:41'){throw 'CIL hover-clock conversion differs'}
            $line=$model.GetMethod('smelter-hover-line').Invoke($null,@(4L,10L,2L,3L,2L,10L))
            if($decode.Invoke($null,@($line)) -cne 'next in 0:08, 2 done in 0:18, then out of fuel'){throw 'CIL smelter text differs'}
            Write-Host 'PASS actual adapter text conversion and compiled hover calculations'
        }
        Write-Host "PASS unity-$name metadata, embedded bytes and all generated methods JIT"
    }
} finally {[AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver)}
