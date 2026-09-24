param(
    [Parameter(Mandatory)][string]$Source,
    [Parameter(Mandatory)][string]$Ir,
    [Parameter(Mandatory)][string]$BaselineIr,
    [switch]$RequireWitness,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot '../../build/ir-fidelity/ir-wire.ps1')
$sourcePath = (Resolve-Path -LiteralPath $Source).Path
$irPath = (Resolve-Path -LiteralPath $Ir).Path
$baselinePath = (Resolve-Path -LiteralPath $BaselineIr).Path
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputPath) { throw 'contract-selftest: output directory must be new' }
[void][IO.Directory]::CreateDirectory($outputPath)
$original = [IO.File]::ReadAllText($irPath)
$checker = Join-Path $PSScriptRoot 'method-template-contract-check.ps1'
function SC-Child($Node, [string]$Head) {
    foreach ($child in $Node) { if ((Get-IrHead $child) -ceq $Head) { return ,$child } }
    throw "contract-selftest: missing $Head"
}
function Check([string]$Tag, [string]$Path, [string]$ExpectedError, [string]$RoundTrip = '') {
    $arguments = @('-NoProfile','-File',$checker,'-Source',$sourcePath,'-Ir',$Path,'-ExpectedState','zero-demand','-ExpectedUses','0','-ExpectedSlots','0','-BaselineIr',$baselinePath)
    if ($RoundTrip) { $arguments += @('-RoundTrip',$RoundTrip) }
    if ($RequireWitness) { $arguments += '-RequireWitness' }
    $log = Join-Path $outputPath "$Tag.log"
    & pwsh @arguments *> $log
    $code = $LASTEXITCODE
    $text = [IO.File]::ReadAllText($log)
    if ($ExpectedError) {
        if ($code -eq 0 -or $text -notmatch [regex]::Escape($ExpectedError)) { throw "contract-selftest: $Tag failed to discriminate: $log" }
    } elseif ($code -ne 0) { throw "contract-selftest: positive control failed: $log" }
    Write-Host "$Tag calibrated; exit=$code"
}
Check 'positive' $irPath ''
$mutations = @(
    @{Name='missing-section';Error='exactly one method-templates'; Edit={ $script:tree = @($script:tree | Where-Object { (Get-IrHead $_) -cne 'method-templates' }) }},
    @{Name='duplicate-section';Error='exactly one method-templates'; Edit={ $script:tree += ,$script:section }},
    @{Name='version';Error='metadata version'; Edit={ (SC-Child $script:section 'version')[1] = [IrAtom]::new('2',$false) }},
    @{Name='omitted-ledger';Error='omitted or forged logical'; Edit={ $ledger=SC-Child $script:section 'logical'; $ledger[1]=[IrAtom]::new('omitted',$true) }},
    @{Name='duplicate-ledger';Error='duplicate logical'; Edit={ $ledger=SC-Child $script:section 'logical'; $ledger[2]=$ledger[1] }},
    @{Name='omitted-template-and-ledger';Error='source/template accounting'; Edit={ for($i=1;$i -lt $script:section.Count;$i++){ $head=Get-IrHead $script:section[$i]; if($head -cin @('logical','templates')){ $script:section[$i]=@(ConvertFrom-IrWire "($head)") }} }},
    @{Name='forged-class';Error='forged or repeated class'; Edit={ (SC-Child $script:template 'name')[1]=[IrAtom]::new('UserLookalike',$true) }},
    @{Name='forged-binder';Error='method-local scope'; Edit={ (SC-Child $script:method 'binders')[1]=[IrAtom]::new('q',$true) }},
    @{Name='unbound-declaration';Error='original method declaration'; Edit={ $decl=SC-Child $script:method 'declared'; $decl[1][2][1][1]=[IrAtom]::new('q',$true) }},
    @{Name='forged-implementation-signature';Error='checked implementation signature'; Edit={ (SC-Child $script:implementation 'checked-signature')[1]=ConvertFrom-IrWire '(a-fun (a-named "Integer") (a-fun (a-named "Integer") (a-named "Integer")))' }},
    @{Name='forged-recipe-parameter';Error='checked source recipe'; Edit={ $ctor=SC-Child $script:instance 'constructor'; $recipe=SC-Child $ctor 'recipe'; $recipe[1][2][1][2][1][1]=[IrAtom]::new('z',$true) }},
    @{Name='reference-payload';Error='malformed method field arity'; Edit={ for($i=2;$i -lt $script:implementation.Count;$i++){ if((Get-IrHead $script:implementation[$i]) -ceq 'method'){ $script:implementation[$i] += [IrAtom]::new('foreign',$true) }} }},
    @{Name='nested-target-quantifier';Error='nested target quantifier'; Edit={ $targets=SC-Child $script:implementation 'targets'; $targets[1][3]=ConvertFrom-IrWire '(fn int-default (fn (forall 57 (tvar 57)) (tvar 57)))' }},
    @{Name='inconsistent-target-variable';Error='inconsistent target variable'; Edit={ $targets=SC-Child $script:implementation 'targets'; $targets[1][3]=ConvertFrom-IrWire '(fn int-default (fn (tvar 57) (tvar 58)))' }},
    @{Name='invented-use';Error='use accounting differs'; Edit={ (SC-Child $script:template 'uses')[1]=[IrAtom]::new('1',$false) }},
    @{Name='clone-flag';Error='body cloning'; Edit={ (SC-Child $script:template 'cloned-bodies')[1]=[IrAtom]::new('1',$false) }},
    @{Name='hidden-cloned-body';Error='cloned, added or deleted'; Edit={ for($i=1;$i -lt $script:tree.Count;$i++){ if((Get-IrHead $script:tree[$i]) -ceq 'defs'){ $clone=ConvertFrom-IrWire (Format-IrNode $script:tree[$i][1]); $clone[1]=[IrAtom]::new('hidden-clone',$true); $script:tree[$i] += ,$clone }} }},
    @{Name='changed-body';Error='runtime definition changed'; Edit={ $defs=SC-Child $script:tree 'defs'; $defs[1][5]=ConvertFrom-IrWire '(int-lit 8)' }},
    @{Name='over-contract-bound';Error='section byte bound impossible'; Edit={ (SC-Child $script:template 'byte-bound')[1]=[IrAtom]::new('2147483648',$false) }},
    @{Name='reversed-span';Error='reversed source span'; Edit={ $span=SC-Child $script:template 'span'; $span[2]=[IrAtom]::new('10',$false); $span[3]=[IrAtom]::new('1',$false) }}
)
if ($RequireWitness) {
    $mutations += @(
        @{Name='wrong-source-offset';Error='source declaration span';Edit={ $span=SC-Child $script:template 'span'; $span[2]=[IrAtom]::new(([long]$span[2].Value + 1).ToString(),$false) }},
        @{Name='wrong-source-bytes';Error='source unit byte count';Edit={ (SC-Child (SC-Child $script:section 'resources') 'source-bytes')[1]=[IrAtom]::new('1',$false) }},
        @{Name='missing-reservation';Error='missing phase reservation witness';Edit={ $script:section=@($script:section|Where-Object {(Get-IrHead $_) -cne 'resources'}); for($i=1;$i -lt $script:tree.Count;$i++){if((Get-IrHead $script:tree[$i]) -ceq 'method-templates'){$script:tree[$i]=$script:section}} }},
        @{Name='reservation-overrun';Error='phase reservation witness violates bound';Edit={ (SC-Child (SC-Child $script:section 'resources') 'phase-capacity')[1]=[IrAtom]::new('1',$false) }},
        @{Name='content-overrun';Error='phase reservation witness violates bound';Edit={ (SC-Child (SC-Child $script:section 'resources') 'content-bytes')[1]=[IrAtom]::new('2147483647',$false) }},
        @{Name='forged-total-bound';Error='phase reservation witness violates bound';Edit={ (SC-Child (SC-Child $script:section 'resources') 'byte-bound')[1]=[IrAtom]::new('1',$false) }}
    )
}
foreach ($mutation in $mutations) {
    $script:tree = ConvertFrom-IrWire $original
    $script:section = SC-Child $script:tree 'method-templates'
    $script:template = (SC-Child $script:section 'templates')[1]
    $script:method = (SC-Child $script:template 'methods')[1]
    $script:instance = (SC-Child $script:template 'instances')[1]
    $script:implementation = (SC-Child $script:instance 'implementations')[1]
    & $mutation.Edit
    if ((Format-IrNode $script:tree) -ceq (Format-IrNode (ConvertFrom-IrWire $original))) { throw "contract-selftest: $($mutation.Name) did not change the tree" }
    $path = Join-Path $outputPath "$($mutation.Name).ir.txt"
    [IO.File]::WriteAllText($path, (Format-IrNode $script:tree), [Text.UTF8Encoding]::new($false))
    Check $mutation.Name $path $mutation.Error
}
$tree = ConvertFrom-IrWire $original
$section = SC-Child $tree 'method-templates'
$round = Join-Path $outputPath 'not-a-chapter.ir.txt'
[IO.File]::WriteAllText($round, ('(not-a-chapter ' + (Format-IrNode $section) + ')'), [Text.UTF8Encoding]::new($false))
Check 'malformed-roundtrip' $irPath 'inspection output is not an IR chapter' $round
Check 'same-artifact-roundtrip-control' $irPath '' $irPath
Write-Host 'Checker calibration PASS. The roundtrip positive is a harness control, not a parser execution.'
