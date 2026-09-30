[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Reflection.Metadata
$root = Join-Path $PSScriptRoot 'build-output/test'
function Assert($condition,$name) { if(-not $condition){throw $name};Write-Host "PASS $name" }
function Load-Cil([string]$name) {
    $bytes=[IO.File]::ReadAllBytes((Join-Path $root "$name.dll"))
    $stream=[IO.MemoryStream]::new($bytes,$false)
    $reader=[Reflection.PortableExecutable.PEReader]::new($stream)
    try {
        Assert ($reader.HasMetadata -and $null -ne $reader.PEHeaders.CorHeader) "$name managed PE headers"
        Assert (($reader.PEHeaders.CoffHeader.Characteristics -band [Reflection.PortableExecutable.Characteristics]::Dll) -ne 0) "$name DLL flag"
        $metadata=[Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($reader)
        Assert ($metadata.GetString($metadata.GetAssemblyDefinition().Name) -eq 'PrismMod') "$name assembly metadata"
        $mvid=$metadata.GetGuid($metadata.GetModuleDefinition().Mvid).ToByteArray()
        $guidOffset=$reader.PEHeaders.MetadataStartOffset+[Reflection.Metadata.Ecma335.MetadataReaderExtensions]::GetHeapMetadataOffset($metadata,[Reflection.Metadata.Ecma335.HeapIndex]::Guid)
        $zeroed=[byte[]]$bytes.Clone();[Array]::Clear($zeroed,$guidOffset,16)
        $digest=[Security.Cryptography.SHA256]::HashData($zeroed)
        Assert ([Convert]::ToHexString($mvid) -eq [Convert]::ToHexString($digest[0..15])) "$name content-derived module identifier"
        foreach($handle in $metadata.AssemblyReferences) {
            $reference=$metadata.GetAssemblyReference($handle)
            Assert ($metadata.GetString($reference.Name) -eq 'mscorlib' -and $reference.Version.Major -eq 4) "$name Mono core-library reference"
        }
    } finally { $reader.Dispose();$stream.Dispose() }
    return [Reflection.Assembly]::Load($bytes).GetType('PrismGenerated.Codex_Program',$true)
}
function Invoke-Cil($type,[string]$name,[object[]]$values) { $type.GetMethod($name).Invoke($null,$values) }
$tail=Load-Cil tail
Assert ((Invoke-Cil $tail opening @()) -eq 21L) 'deep self tail recursion uses a loop and preserves parallel argument evaluation'
$closure=Load-Cil closure
Assert ((Invoke-Cil $closure opening @()) -eq 5L) 'escaping partial application and indirect invocation'
$callbacks=Load-Cil callbacks
Assert ((Invoke-Cil $callbacks opening @()) -eq 151L) 'curried callbacks, returned functions and lexical function shadowing'
$first=Invoke-Cil $callbacks choose-fn @($false)
$second=Invoke-Cil $callbacks choose-fn @($true)
Assert ($first.Invoke(123L) -eq 133L -and $second.Invoke(123L) -eq 124L) 'returned managed delegates retain captures and invoke static methods'
$storage=Load-Cil storage
Assert ((Invoke-Cil $storage opening @()) -eq 3L) 'Valheim storage model emits executable deposit, withdrawal and commit guards'
$slotType=$storage.Assembly.GetType('PrismGenerated.StorageSlot',$true)
$slotCtor=$slotType.GetConstructors()[0]
$slotsType=[Collections.Generic.List``1].MakeGenericType($slotType)
$slots=[Activator]::CreateInstance($slotsType)
$slots.Add($slotCtor.Invoke([object[]]@(0L,8L,10L,$true)))
$slots.Add($slotCtor.Invoke([object[]]@(1L,0L,10L,$true)))
$deposit=$storage.GetMethod('storage-plan-deposit').Invoke($null,[object[]]@($slots,7L))
Assert ($deposit.'sp-valid' -and $deposit.'sp-remainder' -eq 0L -and $deposit.'sp-moves'[0].'sm-amount' -eq 2L -and $deposit.'sp-moves'[1].'sm-amount' -eq 5L) 'deposit fills an existing stack before an empty slot'
$withdrawal=$storage.GetMethod('storage-plan-withdraw').Invoke($null,[object[]]@($slots,9L))
Assert (-not $withdrawal.'sp-valid' -and $withdrawal.'sp-moves'.Count -eq 0 -and $withdrawal.'sp-remainder' -eq 9L) 'insufficient stock refuses the entire withdrawal'
$slots[0].'ss-quantity'=7L
Assert (-not $storage.GetMethod('storage-plan-current').Invoke($null,[object[]]@($deposit,$slots,0L))) 'commit guard rejects a changed inventory snapshot'
$scalar=Load-Cil scalar
Assert ((Invoke-Cil $scalar opening @()) -eq 128L) 'cross-method calls and recursive factorial'
Assert ((Invoke-Cil $scalar choose @(-5L)) -eq -7L -and (Invoke-Cil $scalar choose @(5L)) -eq 8L) 'branch fixups, locals and lexical shadowing'
Assert ((Invoke-Cil $scalar add @(-9000000000L,7000000000L)) -eq -2000000000L) 'signed 64-bit argument and return ABI'
$overflow=$false
try { $null=Invoke-Cil $scalar add @([long]::MaxValue,1L) } catch { $overflow=$_.Exception.ToString().Contains('OverflowException') }
Assert $overflow 'checked integer addition traps overflow'
$real=Load-Cil real
Assert ((Invoke-Cil $real opening @()) -eq $true) 'real arithmetic and comparison'
Assert ((Invoke-Cil $real less-equal @([double]::NaN,0.0)) -eq $false) 'unordered real comparison'
$literal=Load-Cil literal
$text=Invoke-Cil $literal opening @()
Assert ((($text.ToCharArray() | ForEach-Object {[int]$_}) -join ',') -eq '20,13,23,23,16') 'CIL user string preserves internal CCE bytes'
$list=Load-Cil list
Assert ((Invoke-Cil $list opening @()) -eq 33L) 'generic list construction, append, indexing and recursive sum'
$items=Invoke-Cil $list make-list @(10L)
$items=[Collections.Generic.List[long]]::new([long[]]$items)
$listArgument=[object[]]::new(1);$listArgument[0]=$items
$grown=$list.GetMethod('grow-list').Invoke($null,$listArgument)
Assert ([object]::ReferenceEquals($items,$grown) -and $items.Count -eq 4 -and $items[3] -eq 9L) 'list-push preserves mutable list identity'
$set=$list.GetMethod('set-list').Invoke($null,$listArgument)
Assert ([object]::ReferenceEquals($items,$set) -and $items[1] -eq 99L) 'list-set-at preserves identity and updates a slot'
$joined=$list.GetMethod('append-lists').Invoke($null,[object[]]@($items,$items))
Assert ($joined.Count -eq 8 -and -not [object]::ReferenceEquals($joined,$items)) 'append constructs an independent list'
$record=Load-Cil record
Assert ((Invoke-Cil $record opening @()) -eq 35L) 'record construction reorders named fields and supports field updates'
$point=Invoke-Cil $record make-point @(10L,20L)
$changed=$record.GetMethod('update-point').Invoke($null,[object[]]@($point))
Assert ([object]::ReferenceEquals($point,$changed) -and $point.x -eq 15L -and $point.y -eq 20L) 'record field-store returns the original record'
