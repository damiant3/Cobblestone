function MC-TextCost([string]$Text) {
    $length = $Text.Length
    if ($Text -match '[^\x00-\x7f]') {
        . (Join-Path $PSScriptRoot '../../build/vm-config.ps1')
        $length = (ConvertTo-CceBytes $Text).Length
    }
    if ($length -gt 8388607) { throw 'contract: text exceeds reservation arithmetic domain' }
    return [bigint]256 * $length
}
function MC-NameListCost($Node) {
    [bigint]$cost = 0
    for ($i=1; $i -lt $Node.Count; $i++) { $cost += 2048 + (MC-TextCost (MC-Atom $Node[$i])) }
    return $cost
}
function MC-AstCost($Node) {
    [bigint]$cost = 2048
    switch -CaseSensitive (Get-IrHead $Node) {
        'a-named' { return $cost + (MC-TextCost (MC-Atom $Node[1])) }
        'a-fun' { return $cost + (MC-AstCost $Node[1]) + (MC-AstCost $Node[2]) }
        'a-app' {
            $cost += MC-AstCost $Node[1]
            $args = MC-Child $Node 'args'
            for ($i=1; $i -lt $args.Count; $i++) { $cost += MC-AstCost $args[$i] }
            return $cost
        }
        default { throw 'contract: ungraded AST bound case' }
    }
}
function MC-RecipeCost($Node) {
    [bigint]$cost = 2048
    switch -CaseSensitive (Get-IrHead $Node) {
        'name' { return $cost + (MC-TextCost (MC-Atom $Node[1])) }
        'lit' { return $cost + (MC-TextCost (MC-Atom $Node[2])) }
        'binary' { return $cost + (MC-RecipeCost $Node[2]) + (MC-RecipeCost $Node[3]) }
        'lambda' { return $cost + (MC-NameListCost $Node[1]) + (MC-RecipeCost $Node[2]) }
        'record' {
            $cost += MC-TextCost (MC-Atom $Node[1])
            $fields = MC-Child $Node 'fields'
            for ($i=1; $i -lt $fields.Count; $i++) { $cost += (MC-TextCost (MC-Atom $fields[$i][1])) + (MC-RecipeCost $fields[$i][2]) }
            return $cost
        }
        default { throw 'contract: ungraded recipe bound case' }
    }
}
function MC-IrCost($Node) {
    [bigint]$cost = 2048
    if ($Node -is [IrAtom]) {
        if ($Node.Value -cnotin @('int-default','boolean','text')) { throw 'contract: ungraded IR bound atom' }
        return $cost
    }
    switch -CaseSensitive (Get-IrHead $Node) {
        'tvar' { return $cost }
        'forall' { return $cost + (MC-IrCost $Node[2]) }
        'list' { return $cost + (MC-IrCost $Node[1]) }
        'fn' {
            if ($BoundProfile -ceq '23DB8046-minimum') { $cost += 2048 }
            $cost += (MC-IrCost $Node[1]) + (MC-IrCost $Node[2])
            if ($Node.Count -gt 3) {
                $row = MC-Child $Node 'row'
                $labels = MC-Child $row 'labels'
                for ($i=1; $i -lt $labels.Count; $i++) { $cost += 2048 + (MC-TextCost (MC-Atom $labels[$i][1])) + (MC-TextCost (MC-Atom $labels[$i][2])) }
                $cost += MC-TextCost (MC-Atom $row[2])
            }
            return $cost
        }
        default { throw 'contract: ungraded retained CHECK bound case' }
    }
}
function MC-OriginBound($Template, $Methods) {
    [bigint]$cost = 16384 + (MC-TextCost (MC-Atom (MC-Child $Template 'name')[1]))
    $schema = (MC-Child $Template 'schema')[1]
    $fields = MC-Child $schema 'fields'
    for ($i=1; $i -lt $fields.Count; $i++) { $cost += (MC-TextCost (MC-Atom $fields[$i][1])) + (MC-AstCost $fields[$i][2]) }
    $instances = MC-Child $Template 'instances'
    for ($i=1; $i -lt $instances.Count; $i++) {
        $instance = $instances[$i]
        $constructor = MC-Child $instance 'constructor'
        $declared = (MC-Child $constructor 'declared')[1]
        $head = (MC-Child $instance 'head')[1]
        $cost += 16384 + (MC-AstCost $head) + (MC-AstCost $declared) + (MC-RecipeCost (MC-Child $constructor 'recipe')[1]) + (MC-TextCost (MC-Atom $constructor[1]))
        $cost += 2048 + (MC-TextCost (MC-Atom $schema[1])) + (MC-IrCost (ConvertFrom-IrWire (MC-AstToIr $head)))
        $implementations = MC-Child $instance 'implementations'
        for ($j=1; $j -lt $implementations.Count; $j++) {
            $implementation = $implementations[$j]
            $methodId = MC-Atom (MC-Child $implementation 'method')[1]
            $cost += 8192 + (MC-TextCost $Methods[$methodId].Name) + (MC-NameListCost (MC-Child $implementation 'params'))
            $targets = MC-Child $implementation 'targets'
            for ($k=1; $k -lt $targets.Count; $k++) { $cost += 2048 + (MC-TextCost (MC-Atom $targets[$k][1])) + (MC-IrCost $targets[$k][3]) }
        }
    }
    return $cost
}
