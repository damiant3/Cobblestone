param(
    [Parameter(Mandatory)][string]$Baseline,
    [Parameter(Mandatory)][string]$Candidate,
    [Parameter(Mandatory)][string]$Generic,
    [Parameter(Mandatory)][string]$Nested
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../../build/ir-fidelity/ir-wire.ps1')

function Read-Chapter([string]$Path) {
    ConvertFrom-IrWire ([IO.File]::ReadAllText((Resolve-Path $Path).Path))
}
function Cell($Chapter, [string]$Path) {
    $value = Get-IrCell -Chapter $Chapter -Path $Path
    if ($null -eq $value) { throw "Missing IR cell: $Path" }
    Format-IrNode $value
}
function Require($Chapter, [string]$Path, [string]$Expected) {
    $actual = Cell $Chapter $Path
    if ($actual -cne $Expected) { throw "$Path expected $Expected, found $actual" }
}

$old = Read-Chapter $Baseline
$new = Read-Chapter $Candidate
if ((Cell $old 'def:Showable-dict-Integer/type/find:args/slot/1') -notmatch '^\(tvar \d+\)$') {
    throw 'Baseline does not expose the unresolved Integer dictionary'
}
foreach ($type in @(@('Integer', 'int-default'), @('Boolean', 'boolean'))) {
    $dict = 'Showable-dict-' + $type[0]
    $method = 'to-text-' + $type[0]
    Require $new "def:$dict/type/find:args/slot/1" $type[1]
    Require $new "def:$dict/body/slot/3/find:args/slot/1" $type[1]
    if ($type[0] -eq 'Integer') { Require $new "def:$method/param/0" $type[1] }
    $def = Get-IrDef -Chapter $new -Name $dict
    $fields = Find-IrChild -Node $def[5] -Head 'fields'
    $reference = $fields[1][2]
    if ((Get-IrHead $reference) -ne 'name') { throw "$dict has no lifted method reference" }
    $lambdaName = $reference[1].Value
    Require $new "def:$lambdaName/param/0" $type[1]
    $lambda = Get-IrDef -Chapter $new -Name $lambdaName
    $parameterName = $lambda[3][1][1].Value
    $uses = @(Find-IrAtomSites -Node $lambda[5] -Atom 'name' | Where-Object { $_.Parent -eq 'name' -and $_.ParentNode[1].Value -eq $parameterName })
    if ($uses.Count -eq 0) { throw "$lambdaName does not use its parameter" }
    foreach ($use in $uses) {
        if ((Format-IrNode $use.ParentNode[2]) -cne $type[1]) { throw "$lambdaName parameter use has inconsistent type" }
    }
}
Require $old 'def:Showable-dict-Boolean/type/find:args/slot/1' 'boolean'
$genericChapter = Read-Chapter $Generic
$parameter = Cell $genericChapter 'def:describe-value/param/1'
if ($parameter -notmatch '^\(tvar \d+\)$') { throw 'Generic class parameter was defaulted' }
Require $genericChapter 'def:describe-value/param/0/find:args/slot/1' $parameter
$nestedChapter = Read-Chapter $Nested
Require $nestedChapter 'def:measure-list/param/0' '(list int-default)'
Require $nestedChapter 'def:MeasureList-dict-Integer/type/find:args/slot/1' 'int-default'
$parameter = Cell $nestedChapter 'def:generic-length/param/0/slot/1'
if ($parameter -notmatch '^\(tvar \d+\)$') { throw 'Generic list element was defaulted' }
Write-Host 'PASS: concrete dictionaries, methods and lifted bodies; recursive signature substitution; generic variables retained.'
