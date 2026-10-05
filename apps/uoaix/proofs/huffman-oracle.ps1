[CmdletBinding()]
param([Parameter(Mandatory)][string]$ReferenceSource, [Parameter(Mandatory)][string]$OutFile)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$digest=(Get-FileHash -LiteralPath $ReferenceSource -Algorithm SHA256).Hash
if($digest -ne '807165537C00C83F295DC98F229F53D31460F055D1CC7FCBA9616E5966523042'){
    throw 'Require the unchanged pinned RunUO Server/Network/Compression.cs'
}
$source=[IO.File]::ReadAllText((Resolve-Path -LiteralPath $ReferenceSource))
$platform='namespace Server { public static class Core { public static bool Unix { get { return false; } } public static bool Is64Bit { get { return true; } } } }'
Add-Type -TypeDefinition ($source+"`n"+$platform) -CompilerOptions '/unsafe'
$out=[Collections.Generic.List[string]]::new()
function Add-Oracle([string]$Label,[byte[]]$InputBytes,[int]$Count){
    $outputBytes=[byte[]]::new(65536)
    $length=0
    [Server.Network.Compression]::Compress($InputBytes,0,$Count,$outputBytes,[ref]$length)
    if($length -le 0){throw "Foreign compressor returned no bytes for $Label"}
    $out.Add($Label+' '+[Convert]::ToHexString($outputBytes,0,$length))
}
Add-Oracle 'empty' ([byte[]]@(0)) 0
for($i=0;$i -lt 256;$i++){Add-Oracle "$i" ([byte[]]@($i)) 1}
$out.Add('singletons complete')
$pattern=[byte[]]::new(4096)
for($i=0;$i -lt $pattern.Length;$i++){$pattern[$i]=($i*73+19)-band 255}
Add-Oracle 'pattern' $pattern $pattern.Length
$out.Add('reject non-byte True')
$out.Add('reject budget True')
[IO.File]::WriteAllText([IO.Path]::GetFullPath($OutFile),($out -join "`n")+"`n",[Text.UTF8Encoding]::new($false))
"RunUO witness SHA256=$digest; output=$OutFile"
