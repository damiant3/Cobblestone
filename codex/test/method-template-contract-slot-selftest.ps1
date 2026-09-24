param(
    [Parameter(Mandatory)][string]$BaselineIr,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot '../../build/ir-fidelity/ir-wire.ps1')
. (Join-Path $PSScriptRoot 'method-template-contract-source.ps1')
$source = Join-Path $PSScriptRoot 'method-template-contract-demands.codex'
$inventory = Get-MethodContractSource $source
$class = $inventory.Classes[0]
$baseline = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $BaselineIr).Path)
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'contract-slot-selftest: output directory must be new' }
[void][IO.Directory]::CreateDirectory($output)
$schemaFields = ($class.Methods | ForEach-Object { '(rec-field "' + $_.Name + '-impl" ' + $_.Schema + ')' }) -join ' '
$schema = '(rec-def "DemandContractDict" (tparams "a") (fields ' + $schemaFields + '))'
$methods = [Collections.Generic.List[string]]::new()
$implementations = [Collections.Generic.List[string]]::new()
for ($i=0; $i -lt 2; $i++) {
    $method = $class.Methods[$i]
    $methods.Add('(method "method:' + $i + '" "' + $method.Name + '" (binders "b") (row-binders) (declared ' + $method.Schema + ') (span 1 0 1))')
    $checked = $method.Schema.Replace('(a-named "a")','(a-named "Integer")')
    $result = if ($i -eq 0) { '(tvar 57)' } else { 'int-default' }
    $targetType = '(fn int-default (fn (tvar 57) ' + $result + '))'
    $implementations.Add('(implementation "implementation:' + $i + '" (method "method:' + $i + '") (params "x" "y") (checked-signature ' + $checked + ') (recipe-field "' + $method.Name + '-impl") (targets (target "' + $method.Name + '-Integer" "' + $method.Name + '-Integer" ' + $targetType + ') (target "' + $method.Name + '" "' + $method.Name + '" ' + $targetType + ')) (span 1 0 1))')
}
$slots = [Collections.Generic.List[string]]::new()
$fields = [Collections.Generic.List[string]]::new()
$specs = @(@(0,'Boolean','boolean'),@(0,'Text','text'),@(1,'Boolean','boolean'),@(1,'Text','text'),@(1,'Integer','int-default'))
for ($i=0; $i -lt $specs.Count; $i++) {
    $spec = $specs[$i]
    $slotType = $class.Methods[$spec[0]].Schema.Replace('(a-named "b")','(a-named "' + $spec[1] + '")')
    $result = if ($spec[0] -eq 0) { $spec[2] } else { 'int-default' }
    $key = '(function int-default (function ' + $spec[2] + ' ' + $result + ' (effects)) (effects))'
    $slots.Add('(slot "slot-' + $i + '" (method "method:' + $spec[0] + '") (key "' + $key + '") (arguments ' + $spec[2] + ') (type ' + $slotType + '))')
    $fields.Add('(rec-field "slot-' + $i + '" ' + $slotType + ')')
}
$recipe = '(record "DemandContractDict" (fields (field "demand-left-impl" (lambda (params "x" "y") (name "y"))) (field "demand-right-impl" (lambda (params "x" "y") (binary 0 (name "x") (lit 0 "1"))))))'
$instance = '(instance "instance:0" (head (a-named "Integer")) (binders) (constructor "DemandContract-dict-Integer" (declared (a-app (a-named "DemandContractDict") (args (a-named "Integer")))) (recipe ' + $recipe + ')) (implementations ' + ($implementations -join ' ') + ') (span 1 0 1))'
$metadata = '(method-templates (version 1) (logical "class:0" "method:0" "method:1" "instance:0" "implementation:0" "implementation:1") (templates (template "class:0" (name "DemandContract") (class-binders "a") (schema ' + $schema.Substring(0,$schema.Length-1) + ' (immutable))) (methods ' + ($methods -join ' ') + ') (instances ' + $instance + ') (state materialized "") (uses 6) (cloned-bodies 0) (byte-bound 1048576) (slots ' + ($slots -join ' ') + ') (span 1 0 1))))'
$runtimeSchema = '(rec-def "DemandContractDict" (tparams "a") (fields ' + ($fields -join ' ') + '))'
if (-not $baseline.Contains($schema)) { throw 'contract-slot-selftest: original schema differs from source' }
$model = $baseline.Replace($schema,$runtimeSchema).Replace('(type-defs',($metadata + "`n(type-defs"))
$modelPath = Join-Path $output 'synthetic-materialized.ir.txt'
[IO.File]::WriteAllText($modelPath,$model,[Text.UTF8Encoding]::new($false))
function SC-Child($Node, [string]$Head) {
    foreach ($child in $Node) { if ((Get-IrHead $child) -ceq $Head) { return ,$child } }
    throw "contract-slot-selftest: missing $Head"
}
function Check([string]$Name,[string]$Path,[string]$ErrorText) {
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'method-template-contract-check.ps1') -Source $source -Ir $Path -ExpectedState materialized -ExpectedUses 6 -ExpectedSlots 5 -BaselineIr $BaselineIr *> (Join-Path $output "$Name.log")
    $exitCode = $LASTEXITCODE
    $log = [IO.File]::ReadAllText((Join-Path $output "$Name.log"))
    if ($ErrorText) { if ($exitCode -eq 0 -or $log -notmatch [regex]::Escape($ErrorText)) { throw "contract-slot-selftest: $Name failed to discriminate" } }
    elseif ($exitCode -ne 0) { throw 'contract-slot-selftest: positive synthetic model failed' }
    Write-Host "$Name calibrated; exit=$exitCode"
}
Check 'positive-synthetic-model' $modelPath ''
$mutations = @(
    @{Name='cross-product';Error='fixture slot census';Edit={ $script:template[$script:slotIndex] += ,$script:slotNodes[1] }},
    @{Name='duplicate-key';Error='forged or duplicate structural slot key';Edit={ $copy=ConvertFrom-IrWire (Format-IrNode $script:slotNodes[1]); $copy[1]=[IrAtom]::new('unique-name',$true); $script:slotNodes[2]=$copy }},
    @{Name='forged-key';Error='forged or duplicate structural slot key';Edit={ (SC-Child $script:slotNodes[1] 'key')[1]=[IrAtom]::new('forged',$true) }},
    @{Name='wrong-argument';Error='unbound or incorrect slot type';Edit={ (SC-Child $script:slotNodes[1] 'arguments')[1]=[IrAtom]::new('text',$false) }},
    @{Name='foreign-method';Error='slot references foreign method';Edit={ (SC-Child $script:slotNodes[1] 'method')[1]=[IrAtom]::new('foreign',$true) }},
    @{Name='unbound-slot';Error='unbound or incorrect slot type';Edit={ (SC-Child $script:slotNodes[1] 'type')[1]=ConvertFrom-IrWire '(a-fun (a-named "a") (a-fun (a-named "q") (a-named "q")))' }},
    @{Name='changed-runtime-field';Error='materialized runtime fields';Edit={ $types=SC-Child $script:tree 'type-defs'; foreach($type in $types){ if((Get-IrHead $type) -ceq 'rec-def' -and $type[1].Value -ceq 'DemandContractDict'){ (SC-Child $type 'fields')[1][1]=[IrAtom]::new('different-field',$true) }} }}
)
foreach ($mutation in $mutations) {
    $script:tree=ConvertFrom-IrWire $model
    $script:template=(SC-Child (SC-Child $script:tree 'method-templates') 'templates')[1]
    $script:slotNodes=SC-Child $script:template 'slots'
    for($i=2;$i -lt $script:template.Count;$i++){if((Get-IrHead $script:template[$i]) -ceq 'slots'){$script:slotIndex=$i}}
    & $mutation.Edit
    $changed=Format-IrNode $script:tree
    if($changed -ceq (Format-IrNode (ConvertFrom-IrWire $model))){throw 'contract-slot-selftest: mutation unchanged'}
    $path=Join-Path $output "$($mutation.Name).ir.txt"
    [IO.File]::WriteAllText($path,$changed,[Text.UTF8Encoding]::new($false))
    Check $mutation.Name $path $mutation.Error
}
Write-Host 'Synthetic slot calibration PASS; no production materializer acceptance is claimed.'
