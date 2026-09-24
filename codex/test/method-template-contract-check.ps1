param(
    [Parameter(Mandatory)][string]$Source,
    [Parameter(Mandatory)][string]$Ir,
    [Parameter(Mandatory)][ValidateSet('none','zero-demand','materialized','ineligible')][string]$ExpectedState,
    [int]$ExpectedUses = -1,
    [int]$ExpectedSlots = -1,
    [string]$BaselineIr = '',
    [ValidateSet('none','8C145D24','23DB8046-minimum')][string]$BoundProfile = 'none',
    [string]$RoundTrip = '',
    [switch]$RequireWitness,
    [string]$Result = ''
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot '../../build/ir-fidelity/ir-wire.ps1')
. (Join-Path $PSScriptRoot 'method-template-contract-source.ps1')
. (Join-Path $PSScriptRoot 'method-template-contract-bound.ps1')

function MC-Atom($Node) {
    if ($Node -isnot [IrAtom]) { throw 'contract: expected atom' }
    return $Node.Value
}
function MC-Children($Node, [string]$Head) {
    return ,@($Node | Where-Object { (Get-IrHead $_) -ceq $Head })
}
function MC-Child($Node, [string]$Head) {
    $found = MC-Children $Node $Head
    if ($found.Count -ne 1) { throw "contract: expected exactly one $Head" }
    $arity = @{version=2;name=2;head=2;state=3;uses=2;'cloned-bodies'=2;'byte-bound'=2;span=4;declared=2;'checked-signature'=2;method=2;key=2;'recipe-field'=2;type=2;recipe=2}
    if ($arity.ContainsKey($Head) -and $found[0].Count -ne $arity[$Head]) { throw "contract: malformed $Head field arity" }
    return ,$found[0]
}
function MC-Fields($Node, [int]$Start, [string[]]$Names) {
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    for ($i = $Start; $i -lt $Node.Count; $i++) {
        $head = Get-IrHead $Node[$i]
        if ($Names -cnotcontains $head -or -not $seen.Add($head)) { throw "contract: unexpected or duplicate field $head" }
    }
    if ($seen.Count -ne $Names.Count) { throw 'contract: missing field' }
}
function MC-Equal($Node, [string]$Expected, [string]$Label) {
    $actual = Format-IrNode $Node
    if ($actual -cne $Expected) { throw "contract: $Label expected $Expected, found $actual" }
}
function MC-Int($Node) {
    $text = MC-Atom $Node
    if ($text -cnotmatch '^\d+$') { throw 'contract: nonnegative integer required' }
    return [long]::Parse($text, [Globalization.CultureInfo]::InvariantCulture)
}
function MC-Id($Node) {
    $id = MC-Atom $Node[1]
    if (-not $id -or -not $script:declarations.Add($id)) { throw 'contract: duplicate or empty declaration identity' }
    $span = MC-Child $Node 'span'
    if ($span.Count -ne 4) { throw 'contract: malformed span' }
    $file = MC-Int $span[1]; $start = MC-Int $span[2]; $end = MC-Int $span[3]
    if ($end -lt $start) { throw 'contract: reversed source span' }
    return $id
}
function MC-SourceSpan($Node, $Declaration, [string]$Token, [string]$Kind, [string]$Identity) {
    if (-not $RequireWitness) { return }
    $line = $script:unit.SourceLines[$Declaration.Line - 1]
    $column = $line.IndexOf($Token, [StringComparison]::Ordinal)
    if ($column -lt 0) { throw 'contract: source declaration token missing' }
    $start = $script:unit.LineOffsets[$Declaration.Line - 1] + @(ConvertTo-CceBytes $line.Substring(0,$column)).Count
    $end = $start + @(ConvertTo-CceBytes $Token).Count
    MC-Equal (MC-Child $Node 'span') "(span 1 $start $end)" 'source declaration span'
    if ((MC-Atom $Node[1]) -cne "${Kind}:1:${start}:$Identity") { throw 'contract: source declaration identity mismatch' }
}
function MC-IrTypeToAst($Type) {
    if ($Type -is [IrAtom]) {
        switch -CaseSensitive ($Type.Value) {
            'int-default' { return '(a-named "Integer")' }
            'boolean' { return '(a-named "Boolean")' }
            'text' { return '(a-named "Text")' }
            default { throw "contract: ungraded argument type $($Type.Value)" }
        }
    }
    if ((Get-IrHead $Type) -ceq 'list' -and $Type.Count -eq 2) {
        return '(a-app (a-named "List") (args ' + (MC-IrTypeToAst $Type[1]) + '))'
    }
    throw "contract: open or ungraded argument type $(Format-IrNode $Type)"
}
function MC-Substitute($Type, $Bindings) {
    if ((Get-IrHead $Type) -ceq 'a-bounded') {
        $wire = Format-IrNode $Type
        if ($wire -ceq '(a-bounded (a-named "Integer") i64-min 9223372036854775807 0)' -or $wire -ceq '(a-bounded (a-named "Integer") i64-min 9223372036854775807 ov-error)') { return '(a-named "Integer")' }
        throw 'contract: ungraded bounded type'
    }
    if ((Get-IrHead $Type) -ceq 'a-named') {
        $name = MC-Atom $Type[1]
        if ($Bindings.ContainsKey($name)) { return $Bindings[$name] }
        return Format-IrNode $Type
    }
    if ((Get-IrHead $Type) -ceq 'a-fun' -and $Type.Count -eq 3) {
        return '(a-fun ' + (MC-Substitute $Type[1] $Bindings) + ' ' + (MC-Substitute $Type[2] $Bindings) + ')'
    }
    if ((Get-IrHead $Type) -ceq 'a-app' -and $Type.Count -eq 3) {
        $args = MC-Child $Type 'args'
        $parts = [Collections.Generic.List[string]]::new()
        for ($i = 1; $i -lt $args.Count; $i++) { $parts.Add((MC-Substitute $args[$i] $Bindings)) }
        return '(a-app ' + (MC-Substitute $Type[1] $Bindings) + ' (args ' + ($parts -join ' ') + '))'
    }
    throw 'contract: ungraded source type grammar'
}
function MC-RuntimeSchema($Node) {
    if ((Get-IrHead $Node) -cne 'rec-def' -or $Node.Count -ne 4) { throw 'contract: ungraded runtime schema shape' }
    $fields = MC-Child $Node 'fields'
    $parts = [Collections.Generic.List[string]]::new()
    for ($i=1; $i -lt $fields.Count; $i++) {
        if ((Get-IrHead $fields[$i]) -cne 'rec-field' -or $fields[$i].Count -ne 3) { throw 'contract: malformed runtime field' }
        $parts.Add('(rec-field ' + (Format-IrNode $fields[$i][1]) + ' ' + (MC-Substitute $fields[$i][2] @{}) + ')')
    }
    return '(rec-def ' + (Format-IrNode $Node[1]) + ' ' + (Format-IrNode (MC-Child $Node 'tparams')) + ' (fields ' + ($parts -join ' ') + '))'
}
function MC-Key($Type) {
    if ($Type -is [IrAtom]) { [void](MC-IrTypeToAst $Type); return $Type.Value }
    if ((Get-IrHead $Type) -ceq 'list' -and $Type.Count -eq 2) { return '(list ' + (MC-Key $Type[1]) + ')' }
    if ((Get-IrHead $Type) -ceq 'fn' -and $Type.Count -eq 3) {
        return '(function ' + (MC-Key $Type[1]) + ' ' + (MC-Key $Type[2]) + ' (effects))'
    }
    throw 'contract: ungraded or open use type'
}
function MC-AstToIr($Type) {
    if ((Get-IrHead $Type) -ceq 'a-named') {
        switch -CaseSensitive (MC-Atom $Type[1]) {
            'Integer' { return 'int-default' }
            'Boolean' { return 'boolean' }
            'Text' { return 'text' }
            default { throw 'contract: unresolved slot binder' }
        }
    }
    if ((Get-IrHead $Type) -ceq 'a-fun' -and $Type.Count -eq 3) { return '(fn ' + (MC-AstToIr $Type[1]) + ' ' + (MC-AstToIr $Type[2]) + ')' }
    if ((Get-IrHead $Type) -ceq 'a-app' -and (MC-Atom $Type[1][1]) -ceq 'List' -and $Type[2].Count -eq 2) {
        return '(list ' + (MC-AstToIr $Type[2][1]) + ')'
    }
    throw 'contract: ungraded slot type'
}
function MC-TargetType($Formal, $Actual, $Variables, $Quantified, [bool]$RootScheme = $true) {
    if ((Get-IrHead $Actual) -ceq 'forall' -and $Actual.Count -eq 3) {
        if (-not $RootScheme) { throw 'contract: nested target quantifier outside declaration scope' }
        $var = MC-Atom $Actual[1]
        if (-not $Quantified.Add($var)) { throw 'contract: duplicate target quantifier' }
        MC-TargetType $Formal $Actual[2] $Variables $Quantified $true
        return
    }
    if ((Get-IrHead $Formal) -ceq 'a-fun') {
        if ((Get-IrHead $Actual) -cne 'fn' -or $Actual.Count -ne 3) { throw 'contract: target function shape mismatch' }
        MC-TargetType $Formal[1] $Actual[1] $Variables $Quantified $false
        MC-TargetType $Formal[2] $Actual[2] $Variables $Quantified $false
        return
    }
    $name = MC-Atom $Formal[1]
    if ($name -cmatch '^[a-z]$') {
        if ((Get-IrHead $Actual) -cne 'tvar' -or $Actual.Count -ne 2) { throw 'contract: generic target binder defaulted' }
        $id = MC-Atom $Actual[1]
        if ($Quantified.Count -gt 0 -and -not $Quantified.Contains($id)) { throw 'contract: unbound target variable' }
        if ($Variables.ContainsKey($name) -and $Variables[$name] -cne $id) { throw 'contract: inconsistent target variable' }
        $Variables[$name] = $id
    } else { MC-Equal $Actual (MC-AstToIr $Formal) 'checked target type' }
}
function MC-Normalized($Node, $TypeIds, $RowIds) {
    if ($Node -is [IrAtom]) { return Format-IrNode $Node }
    $head = Get-IrHead $Node
    if ($head -cin @('tvar','forall')) {
        if (($head -ceq 'tvar' -and $Node.Count -ne 2) -or ($head -ceq 'forall' -and $Node.Count -ne 3)) { throw 'contract: malformed variable or quantifier in runtime body' }
        $id = MC-Atom $Node[1]
        if (-not $TypeIds.ContainsKey($id)) { $TypeIds[$id] = 'v' + $TypeIds.Count }
        if ($head -ceq 'tvar') { return '(tvar ' + $TypeIds[$id] + ')' }
        return '(forall ' + $TypeIds[$id] + ' ' + (MC-Normalized $Node[2] $TypeIds $RowIds) + ')'
    }
    $parts = [Collections.Generic.List[string]]::new()
    for ($i=0; $i -lt $Node.Count; $i++) {
        if ($head -ceq 'row' -and $i -eq 3 -and (MC-Atom $Node[$i]) -cne '-1') {
            $id = MC-Atom $Node[$i]
            if (-not $RowIds.ContainsKey($id)) { $RowIds[$id] = 'e' + $RowIds.Count }
            $parts.Add($RowIds[$id])
        } else { $parts.Add((MC-Normalized $Node[$i] $TypeIds $RowIds)) }
    }
    return '(' + ($parts -join ' ') + ')'
}
function MC-RecipeBody([string]$Body) {
    if ($Body -cmatch '^"([^"\\]*)"$') { return '(lit 2 "' + $Matches[1] + '")' }
    if ($Body -cmatch '^[a-z][a-z0-9-]*$') { return '(name "' + $Body + '")' }
    if ($Body -cmatch '^([a-z][a-z0-9-]*) \+ (\d+)$') { return '(binary 0 (name "' + $Matches[1] + '") (lit 0 "' + $Matches[2] + '"))' }
    throw 'contract: ungraded fixture implementation body'
}
function MC-WalkUses($Node, $Targets, $Keys, $Counts) {
    if ($Node -is [IrAtom]) { return }
    if ((Get-IrHead $Node) -ceq 'name' -and $Node.Count -eq 3) {
        $name = MC-Atom $Node[1]
        if ($Targets.ContainsKey($name)) {
            $id = $Targets[$name]
            $key = MC-Key $Node[2]
            $Counts[$id]++
            if ($BoundProfile -cne 'none') { $script:observedUseCost += MC-IrCost $Node[2] }
            [void]$Keys[$id].Add($key)
        }
    }
    foreach ($child in $Node) { MC-WalkUses $child $Targets $Keys $Counts }
}

$sourceInfo = Get-MethodContractSource $Source
if ($RequireWitness) { . (Join-Path $PSScriptRoot '../../build/vm-config.ps1') }
$script:unit = if ($RequireWitness) { Get-MethodContractUnit $Source } else { $null }
$chapter = ConvertFrom-IrWire ([IO.File]::ReadAllText((Resolve-Path -LiteralPath $Ir).Path))
if ((Get-IrHead $chapter) -cne 'chapter') { throw 'contract: missing IR chapter' }
$sections = MC-Children $chapter 'method-templates'
$runtime = MC-Child $chapter 'type-defs'
$defs = MC-Child $chapter 'defs'
$summaries = [Collections.Generic.List[object]]::new()
if ($sourceInfo.Classes.Count -eq 0) {
    if ($sections.Count -ne 0 -or $ExpectedState -cne 'none') { throw 'contract: user source has no trusted class origins' }
} else {
    $section = MC-Child $chapter 'method-templates'
    $resourceNodes = MC-Children $section 'resources'
    if ($RequireWitness -and $resourceNodes.Count -ne 1) { throw 'contract: missing phase reservation witness' }
    MC-Fields $section 1 $(if ($resourceNodes.Count) { @('version','resources','logical','templates') } else { @('version','logical','templates') })
    MC-Equal (MC-Child $section 'version') '(version 1)' 'metadata version'
    $templates = MC-Child $section 'templates'
    if ($templates.Count - 1 -ne $sourceInfo.Classes.Count) { throw 'contract: source/template accounting mismatch' }
    $script:declarations = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $classNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    [bigint]$totalBound = 8192
    for ($ti = 1; $ti -lt $templates.Count; $ti++) {
        $template = $templates[$ti]
        if ((Get-IrHead $template) -cne 'template') { throw 'contract: unknown template form' }
        MC-Fields $template 2 @('name','class-binders','schema','methods','instances','state','uses','cloned-bodies','byte-bound','slots','span')
        $classId = MC-Id $template
        $className = MC-Atom (MC-Child $template 'name')[1]
        $matches = @($sourceInfo.Classes | Where-Object Name -CEQ $className)
        if ($matches.Count -ne 1 -or -not $classNames.Add($className)) { throw 'contract: forged or repeated class origin' }
        $class = $matches[0]
        MC-SourceSpan $template $class $class.Name 'class' $class.Name
        MC-Equal (MC-Child $template 'class-binders') '(class-binders "a")' 'class scope'
        $methodNodes = MC-Child $template 'methods'
        if ($methodNodes.Count - 1 -ne $class.Methods.Count) { throw 'contract: source/method accounting mismatch' }
        $methods = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
        $methodNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $schemaFields = [Collections.Generic.List[string]]::new()
        for ($mi = 1; $mi -lt $methodNodes.Count; $mi++) {
            $methodNode = $methodNodes[$mi]
            if ((Get-IrHead $methodNode) -cne 'method') { throw 'contract: invalid method entry' }
            MC-Fields $methodNode 3 @('binders','row-binders','declared','span')
            $id = MC-Id $methodNode
            $name = MC-Atom $methodNode[2]
            $match = @($class.Methods | Where-Object Name -CEQ $name)
            if ($match.Count -ne 1 -or -not $methodNames.Add($name)) { throw 'contract: forged or repeated method origin' }
            $method = $match[0]
            MC-SourceSpan $methodNode $method $method.Name 'method' ($className + '/' + $method.Name)
            $binders = ($method.MethodBinders | ForEach-Object { ' "' + $_ + '"' }) -join ''
            MC-Equal (MC-Child $methodNode 'binders') "(binders$binders)" 'method-local scope'
            MC-Equal (MC-Child $methodNode 'row-binders') '(row-binders)' 'method row scope'
            MC-Equal (MC-Child $methodNode 'declared') ('(declared ' + $method.Schema + ')') 'original method declaration'
            $methods.Add($id, $method)
        }
        foreach ($method in $class.Methods) { $schemaFields.Add('(rec-field "' + $method.Name + '-impl" ' + $method.Schema + ')') }
        $original = '(rec-def "' + $className + 'Dict" (tparams "a") (fields ' + ($schemaFields -join ' ') + ') (immutable))'
        MC-Equal (MC-Child $template 'schema') ('(schema ' + $original + ')') 'complete original schema'
        $instances = MC-Child $template 'instances'
        $sourceInstances = @($sourceInfo.Instances | Where-Object Class -CEQ $className)
        if ($instances.Count -ne 2 -or $sourceInstances.Count -ne 1) { throw 'contract: ungraded or incorrect instance accounting' }
        $instance = $instances[1]
        if ((Get-IrHead $instance) -cne 'instance') { throw 'contract: invalid instance entry' }
        MC-Fields $instance 2 @('head','binders','constructor','implementations','span')
        $instanceId = MC-Id $instance
        $head = $sourceInstances[0].Head
        MC-SourceSpan $instance $sourceInstances[0] $className 'instance' ($className + '/' + $head)
        MC-Equal (MC-Child $instance 'head') ('(head (a-named "' + $head + '"))') 'instance head'
        MC-Equal (MC-Child $instance 'binders') '(binders)' 'instance scope'
        $constructor = MC-Child $instance 'constructor'
        MC-Fields $constructor 2 @('declared','recipe')
        if ((MC-Atom $constructor[1]) -cne "$className-dict-$head") { throw 'contract: constructor identity mismatch' }
        MC-Equal (MC-Child $constructor 'declared') ('(declared (a-app (a-named "' + $className + 'Dict") (args (a-named "' + $head + '"))))') 'constructor declaration'
        $fields = [Collections.Generic.List[string]]::new()
        foreach ($implementation in $sourceInstances[0].Methods) {
            $parameters = @([regex]::Matches($implementation.Parameters, '\(([a-z][a-z0-9-]*)\)') | ForEach-Object { $_.Groups[1].Value })
            $parameterText = ($parameters | ForEach-Object { ' "' + $_ + '"' }) -join ''
            $fields.Add('(field "' + $implementation.Name + '-impl" (lambda (params' + $parameterText + ') ' + (MC-RecipeBody $implementation.Body) + '))')
        }
        MC-Equal (MC-Child $constructor 'recipe') ('(recipe (record "' + $className + 'Dict" (fields ' + ($fields -join ' ') + ')))') 'checked source recipe'
        $implementations = MC-Child $instance 'implementations'
        if ($implementations.Count - 1 -ne $class.Methods.Count) { throw 'contract: implementation accounting mismatch' }
        $implemented = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $targets = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
        $useKeys = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
        $useCounts = [Collections.Generic.Dictionary[string,int]]::new([StringComparer]::Ordinal)
        for ($ii = 1; $ii -lt $implementations.Count; $ii++) {
            $implementation = $implementations[$ii]
            if ((Get-IrHead $implementation) -cne 'implementation') { throw 'contract: invalid implementation entry' }
            MC-Fields $implementation 2 @('method','params','checked-signature','recipe-field','targets','span')
            $implementationId = MC-Id $implementation
            $methodId = MC-Atom (MC-Child $implementation 'method')[1]
            if (-not $methods.ContainsKey($methodId) -or -not $implemented.Add($methodId)) { throw 'contract: invalid implementation method link' }
            $method = $methods[$methodId]
            $sourceImplementation = @($sourceInstances[0].Methods | Where-Object Name -CEQ $method.Name)[0]
            MC-SourceSpan $implementation $sourceImplementation $method.Name 'implementation' ($className + '/' + $head + '/' + $method.Name)
            $parameters = @([regex]::Matches($sourceImplementation.Parameters, '\(([a-z][a-z0-9-]*)\)') | ForEach-Object { ' "' + $_.Groups[1].Value + '"' }) -join ''
            MC-Equal (MC-Child $implementation 'params') "(params$parameters)" 'implementation parameters'
            $specialized = MC-Substitute (ConvertFrom-IrWire $method.Schema) @{a='(a-named "' + $head + '")'}
            MC-Equal (MC-Child $implementation 'checked-signature') "(checked-signature $specialized)" 'checked implementation signature'
            MC-Equal (MC-Child $implementation 'recipe-field') ('(recipe-field "' + $method.Name + '-impl")') 'recipe link'
            $targetNodes = MC-Child $implementation 'targets'
            if ($targetNodes.Count -ne 3) { throw 'contract: missing checked method targets' }
            $sourceTargets = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            for ($j = 1; $j -lt $targetNodes.Count; $j++) {
                $target = $targetNodes[$j]
                if ((Get-IrHead $target) -cne 'target' -or $target.Count -ne 4) { throw 'contract: malformed checked target' }
                $sourceName = MC-Atom $target[1]; $runtimeName = MC-Atom $target[2]
                if (@($method.Name, ($method.Name + '-' + $head)) -cnotcontains $sourceName -or -not $sourceTargets.Add($sourceName)) { throw 'contract: forged target source' }
                if ($runtimeName -cne $sourceName -or $targets.ContainsKey($runtimeName)) { throw 'contract: ungraded target renaming or duplicate target' }
                $variables = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
                $quantified = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
                MC-TargetType (ConvertFrom-IrWire $specialized) $target[3] $variables $quantified
                $variableIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
                foreach ($id in $variables.Values) { [void]$variableIds.Add($id) }
                if ($variables.Count -ne $method.MethodBinders.Count -or $variableIds.Count -ne $variables.Count -or ($quantified.Count -gt 0 -and -not $quantified.SetEquals($variableIds))) { throw 'contract: checked target scope mismatch' }
                $targets.Add($runtimeName, $methodId)
            }
            $useKeys.Add($methodId, [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal))
            $useCounts.Add($methodId, 0)
        }
        $state = MC-Child $template 'state'
        if ($state.Count -ne 3 -or (MC-Atom $state[1]) -cne $ExpectedState) { throw 'contract: lifecycle mismatch' }
        MC-Equal (MC-Child $template 'cloned-bodies') '(cloned-bodies 0)' 'body cloning'
        $boundNode = MC-Child $template 'byte-bound'
        if ($boundNode.Count -ne 2) { throw 'contract: malformed byte bound' }
        $bound = MC-Int $boundNode[1]
        $totalBound += $bound
        $slots = MC-Child $template 'slots'
        [bigint]$script:observedUseCost = 0
        $runtimeTypes = @($runtime | Where-Object { (Get-IrHead $_) -ceq 'rec-def' -and (MC-Atom $_[1]) -ceq ($className + 'Dict') })
        if ($ExpectedState -ceq 'ineligible') {
            MC-Equal (MC-Child $template 'uses') '(uses ungraded)' 'ineligible use accounting'
            if ($slots.Count -ne 1 -or $runtimeTypes.Count -ne 1) { throw 'contract: ineligible runtime layout changed' }
            MC-Equal $runtimeTypes[0] $original.Replace(' (immutable))', ')') 'ineligible runtime schema'
        } else {
            MC-WalkUses $defs $targets $useKeys $useCounts
            $observed = 0; foreach ($count in $useCounts.Values) { $observed += $count }
            $uses = MC-Child $template 'uses'
            if ($uses.Count -ne 2 -or (MC-Int $uses[1]) -ne $observed) { throw 'contract: use accounting differs from runtime IR' }
            if ($ExpectedUses -ge 0 -and $observed -ne $ExpectedUses) { throw 'contract: fixture use census changed' }
            if ($ExpectedSlots -ge 0 -and $slots.Count - 1 -ne $ExpectedSlots) { throw 'contract: fixture slot census changed' }
            if ($slots.Count - 1 -gt $observed) { throw 'contract: more slots than observed uses' }
            $slotNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            $slotKeys = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
            foreach ($methodId in $methods.Keys) { $slotKeys.Add($methodId, [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)) }
            $runtimeFields = [Collections.Generic.List[string]]::new()
            for ($si = 1; $si -lt $slots.Count; $si++) {
                $slot = $slots[$si]
                if ((Get-IrHead $slot) -cne 'slot') { throw 'contract: invalid slot' }
                MC-Fields $slot 2 @('method','key','arguments','type')
                $slotName = MC-Atom $slot[1]
                if (-not $slotNames.Add($slotName)) { throw 'contract: duplicate slot name' }
                $methodId = MC-Atom (MC-Child $slot 'method')[1]
                if (-not $methods.ContainsKey($methodId)) { throw 'contract: slot references foreign method' }
                $method = $methods[$methodId]
                $arguments = MC-Child $slot 'arguments'
                if ($arguments.Count - 1 -ne $method.MethodBinders.Count) { throw 'contract: slot binder arity mismatch' }
                $bindings = @{ a = '(a-named "' + $head + '")' }
                for ($j = 1; $j -lt $arguments.Count; $j++) { $bindings[$method.MethodBinders[$j - 1]] = MC-IrTypeToAst $arguments[$j] }
                $concrete = MC-Substitute (ConvertFrom-IrWire $method.Schema) $bindings
                $slotType = MC-Child $slot 'type'
                if ($slotType.Count -ne 2 -or (MC-Substitute $slotType[1] @{a=$bindings.a}) -cne $concrete) { throw 'contract: unbound or incorrect slot type' }
                $key = MC-Atom (MC-Child $slot 'key')[1]
                $derivedKey = MC-Key (ConvertFrom-IrWire (MC-AstToIr (ConvertFrom-IrWire $concrete)))
                if ($key -cne $derivedKey -or -not $slotKeys[$methodId].Add($key)) { throw 'contract: forged or duplicate structural slot key' }
                $runtimeFields.Add('(rec-field "' + $slotName + '" ' + (MC-Substitute $slotType[1] @{}) + ')')
            }
            foreach ($methodId in $methods.Keys) {
                if (-not $slotKeys[$methodId].SetEquals($useKeys[$methodId])) { throw 'contract: slots differ from observed unique demands' }
            }
            if ($ExpectedState -ceq 'zero-demand') {
                if ($observed -ne 0 -or $slots.Count -ne 1 -or $runtimeTypes.Count -ne 0) { throw 'contract: zero-demand template invented runtime layout' }
            } else {
                if ($runtimeTypes.Count -ne 1 -or $slots.Count -le 1) { throw 'contract: materialized runtime schema missing' }
                $expectedRuntime = '(rec-def "' + $className + 'Dict" (tparams "a") (fields ' + ($runtimeFields -join ' ') + '))'
                if ((MC-RuntimeSchema $runtimeTypes[0]) -cne $expectedRuntime) { throw 'contract: materialized runtime fields differ from slots' }
            }
        }
        $boundGrade = 'UNRUN'
        $derivedBound = $null
        if ($BoundProfile -cne 'none') {
            if ($ExpectedState -ceq 'ineligible' -and (MC-Atom $state[2]) -cne 'monomorphic') { $boundGrade = 'UNGRADED: ineligible partial scan' }
            else {
                $derivedBound = (MC-OriginBound $template $methods) + $script:observedUseCost
                if ($derivedBound -gt 2147483647 -or $bound -lt $derivedBound -or ($BoundProfile -ceq '8C145D24' -and $derivedBound -ne $bound)) { throw "contract: input-derived bound expected $derivedBound, found $bound" }
                $boundGrade = if ($BoundProfile -ceq '8C145D24') { 'PASS_8C145D24_FORMULA' } else { 'VISIBLE_LOWER_BOUND_ONLY: hidden row chains ungraded' }
            }
        }
        $summaries.Add(@{Class=$className;State=$ExpectedState;Uses=$(if($ExpectedState -ceq 'ineligible'){'ungraded'}else{$observed});Slots=$slots.Count-1;ByteBound=$bound;BoundFormulaGrade=$boundGrade;VisibleInputMinimum=$(if($null -eq $derivedBound){$null}else{[long]$derivedBound})})
    }
    $logical = MC-Child $section 'logical'
    $ledger = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    for ($i=1; $i -lt $logical.Count; $i++) { if (-not $ledger.Add((MC-Atom $logical[$i]))) { throw 'contract: duplicate logical declaration' } }
    if (-not $ledger.SetEquals($script:declarations)) { throw 'contract: omitted or forged logical declaration' }
    $wireBytes = [long]((MC-TextCost (Format-IrNode $section)) / 256)
    if ($totalBound -gt 2147483648 -or $totalBound -lt $wireBytes) { throw 'contract: section byte bound impossible' }
    if ($resourceNodes.Count) {
        $resource = MC-Child $section 'resources'
        MC-Fields $resource 1 @('source-bytes','phase-capacity','planning-bytes','content-bytes','byte-bound')
        $witness = @{}
        foreach ($field in @('source-bytes','phase-capacity','planning-bytes','content-bytes','byte-bound')) {
            $node = MC-Child $resource $field
            if ($node.Count -ne 2) { throw 'contract: malformed resource witness' }
            $witness[$field] = [bigint](MC-Int $node[1])
        }
        if ($witness['byte-bound'] -ne $totalBound -or $witness['planning-bytes'] + $totalBound -ge $witness['phase-capacity'] -or $witness['content-bytes'] -gt $totalBound) { throw 'contract: phase reservation witness violates bound' }
        if ($RequireWitness -and $witness['source-bytes'] -ne $script:unit.SourceBytes) { throw 'contract: source unit byte count mismatch' }
    }
}
if ($BaselineIr) {
    $baseline = ConvertFrom-IrWire ([IO.File]::ReadAllText((Resolve-Path -LiteralPath $BaselineIr).Path))
    $oldDefs = MC-Child $baseline 'defs'
    $oldNames = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
    for ($i=1; $i -lt $oldDefs.Count; $i++) {
        $name = MC-Atom $oldDefs[$i][1]
        $oldNames.Add($name, (MC-Normalized $oldDefs[$i] @{} @{}))
    }
    if ($oldNames.Count -ne $defs.Count - 1) { throw 'contract: cloned, added or deleted runtime definition' }
    $newNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    for ($i=1; $i -lt $defs.Count; $i++) {
        $name = MC-Atom $defs[$i][1]
        if (-not $newNames.Add($name) -or -not $oldNames.ContainsKey($name) -or $oldNames[$name] -cne (MC-Normalized $defs[$i] @{} @{})) { throw "contract: runtime definition changed: $name" }
    }
    if ($ExpectedState -ceq 'none') { MC-Equal $runtime (Format-IrNode (MC-Child $baseline 'type-defs')) 'user runtime types' }
}
if ($RoundTrip) {
    $round = ConvertFrom-IrWire ([IO.File]::ReadAllText((Resolve-Path -LiteralPath $RoundTrip).Path))
    if ((Get-IrHead $round) -cne 'chapter') { throw 'contract: inspection output is not an IR chapter' }
    [void](MC-Child $round 'type-defs')
    [void](MC-Child $round 'defs')
    $roundSections = MC-Children $round 'method-templates'
    if ($roundSections.Count -ne $sections.Count) { throw 'contract: inspection lost or duplicated section' }
    if ($sections.Count -eq 1) { MC-Equal $roundSections[0] (Format-IrNode $sections[0]) 'inspection metadata roundtrip' }
}
$report = [ordered]@{
    SourceSha256 = $sourceInfo.Sha256; IrSha256 = (Get-FileHash -LiteralPath $Ir).Hash
    StructuralFixtureGrade = 'PASS'; Templates = @($summaries.ToArray())
    InspectionGrade = $(if ($RoundTrip) {'PASS'} else {'UNRUN'})
    BodyReuseGrade = $(if ($BaselineIr) {'PASS_MODULO_TYPE_VARIABLE_IDS'} else {'UNRUN: no baseline IR supplied'})
    PhaseReservationGrade = $(if ($RequireWitness -and $sections.Count) {'PASS_RECORDED_INEQUALITIES: producer guard execution required separately'} elseif ($sections.Count -eq 0) {'NOT_APPLICABLE'} else {'UNRUN'})
    SourceSpanGrade = $(if ($RequireWitness) {'PASS_SOURCE_UNIT_OFFSETS'} else {'RANGE_ONLY: unit offset correspondence ungraded'})
}
if ($Result) { $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $Result -Encoding utf8 }
$report | ConvertTo-Json -Depth 8
