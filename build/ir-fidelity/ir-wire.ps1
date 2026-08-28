# A reader for the IR text wire with no plug opinion in it.
#
# It shares no code with codex/plugs/common/IRTextParser.codex on purpose: that
# parser is one of the things the fidelity arm measures, and it normalises (it
# rebuilds arrows, and until recently discarded unique-params). This reader
# preserves what is on the wire and nothing else -- it does not resolve, widen,
# default, or rebuild any cell.
#
# Escaping follows ir-quote-char (codex/compiler/Emit/IRTextEmitter.codex:71):
# backslash, double quote and newline are the only escapes emitted.

class IrAtom {
    [string]$Value
    [bool]$Quoted
    IrAtom([string]$v, [bool]$q) { $this.Value = $v; $this.Quoted = $q }
}

function ConvertFrom-IrWire {
    param([Parameter(Mandatory)][string]$Text)

    $i = 0
    $n = $Text.Length

    function Read-Node {
        while ($script:i -lt $script:n -and [char]::IsWhiteSpace($script:Text[$script:i])) { $script:i++ }
        if ($script:i -ge $script:n) { throw "ir-wire: unexpected end of input" }

        $c = $script:Text[$script:i]

        if ($c -eq '(') {
            $script:i++
            $items = [System.Collections.Generic.List[object]]::new()
            while ($true) {
                while ($script:i -lt $script:n -and [char]::IsWhiteSpace($script:Text[$script:i])) { $script:i++ }
                if ($script:i -ge $script:n) { throw "ir-wire: unterminated list" }
                if ($script:Text[$script:i] -eq ')') { $script:i++; break }
                [void]$items.Add((Read-Node))
            }
            return , $items.ToArray()
        }

        if ($c -eq ')') { throw "ir-wire: unexpected ')' at $($script:i)" }

        if ($c -eq '"') {
            $script:i++
            $sb = [System.Text.StringBuilder]::new()
            while ($true) {
                if ($script:i -ge $script:n) { throw "ir-wire: unterminated string" }
                $ch = $script:Text[$script:i]
                if ($ch -eq '"') { $script:i++; break }
                if ($ch -eq '\') {
                    $script:i++
                    if ($script:i -ge $script:n) { throw "ir-wire: dangling escape" }
                    $e = $script:Text[$script:i]
                    switch ($e) {
                        'n'  { [void]$sb.Append("`n") }
                        '"'  { [void]$sb.Append('"') }
                        '\'  { [void]$sb.Append('\') }
                        default { throw "ir-wire: unknown escape \$e" }
                    }
                    $script:i++
                    continue
                }
                [void]$sb.Append($ch)
                $script:i++
            }
            return [IrAtom]::new($sb.ToString(), $true)
        }

        $start = $script:i
        while ($script:i -lt $script:n) {
            $ch = $script:Text[$script:i]
            if ([char]::IsWhiteSpace($ch) -or $ch -eq '(' -or $ch -eq ')' -or $ch -eq '"') { break }
            $script:i++
        }
        if ($script:i -eq $start) { throw "ir-wire: empty atom at $start" }
        return [IrAtom]::new($Text.Substring($start, $script:i - $start), $false)
    }

    $script:Text = $Text
    $script:i = 0
    $script:n = $n
    $node = Read-Node
    while ($script:i -lt $script:n -and [char]::IsWhiteSpace($script:Text[$script:i])) { $script:i++ }
    if ($script:i -lt $script:n) { throw "ir-wire: trailing input at $($script:i)" }
    return $node
}

function Format-IrNode {
    param([Parameter(Mandatory)][AllowNull()]$Node)

    if ($null -eq $Node) { return '<null>' }
    if ($Node -is [IrAtom]) {
        if ($Node.Quoted) {
            $v = $Node.Value.Replace('\', '\\').Replace('"', '\"').Replace("`n", '\n')
            return '"' + $v + '"'
        }
        return $Node.Value
    }
    $parts = @($Node | ForEach-Object { Format-IrNode $_ })
    return '(' + ($parts -join ' ') + ')'
}

function Get-IrWireText {
    # Pulls the IR text out of a compile.ps1 -Log file. In -IrUni / -IrCce mode
    # compile.ps1 emits no SIZE: line, so it writes the whole guest output into
    # -Log, never writes -Out, and always falls through to exit 4. The exit code
    # carries no information in this mode; the IR-BEGIN/IR-END markers do.
    param([Parameter(Mandatory)][string]$LogPath)

    if (-not (Test-Path -PathType Leaf $LogPath)) { throw "ir-wire: no log at $LogPath" }
    # @() or a one-line log returns a string and indexing it yields a char.
    $lines = @(Get-Content -LiteralPath $LogPath)
    $b = -1; $e = -1
    for ($k = 0; $k -lt $lines.Count; $k++) {
        if ($lines[$k].Trim() -eq 'IR-BEGIN') { $b = $k }
        elseif ($lines[$k].Trim() -eq 'IR-END') { $e = $k; break }
    }
    if ($b -lt 0 -or $e -lt 0) { return $null }
    return (($lines[($b + 1)..($e - 1)]) -join "`n")
}

function Get-IrDef {
    param([Parameter(Mandatory)]$Chapter, [Parameter(Mandatory)][string]$Name)

    $defs = $null
    foreach ($node in $Chapter) {
        if ($node -isnot [IrAtom] -and $node.Count -gt 0 -and $node[0] -is [IrAtom] -and $node[0].Value -eq 'defs') {
            $defs = $node
            break
        }
    }
    if ($null -eq $defs) { return $null }
    for ($k = 1; $k -lt $defs.Count; $k++) {
        $d = $defs[$k]
        if ($d -isnot [IrAtom] -and $d.Count -ge 2 -and $d[1] -is [IrAtom] -and $d[1].Value -eq $Name) {
            return $d
        }
    }
    return $null
}

function Get-IrHead {
    param([AllowNull()]$Node)
    if ($null -eq $Node -or $Node -is [IrAtom]) { return $null }
    if ($Node.Count -eq 0) { return $null }
    if ($Node[0] -isnot [IrAtom] -or $Node[0].Quoted) { return $null }
    return $Node[0].Value
}

function Find-IrChild {
    # The immediate child list whose head atom is $Head.
    param([AllowNull()]$Node, [string]$Head)
    if ($null -eq $Node -or $Node -is [IrAtom]) { return $null }
    foreach ($ch in $Node) {
        if ((Get-IrHead $ch) -eq $Head) { return $ch }
    }
    return $null
}

function Find-IrDescendant {
    # Depth-first, first list whose head atom is $Head. Used to reach a cell
    # nested inside a body -- a lambda's parameter, say -- without the path
    # having to know the enclosing expression shapes.
    param([AllowNull()]$Node, [string]$Head)
    if ($null -eq $Node -or $Node -is [IrAtom]) { return $null }
    if ((Get-IrHead $Node) -eq $Head) { return $Node }
    foreach ($ch in $Node) {
        $hit = Find-IrDescendant -Node $ch -Head $Head
        if ($null -ne $hit) { return $hit }
    }
    return $null
}

function Find-IrAtomSites {
    # Every occurrence of a bare atom, reported with the head of the form it
    # sits in and its position there. That pairing is the attribution: the wire
    # says `error` in a dozen places and they do not come from one producer, so
    # a total tells you nothing and a census by site tells you what to fix
    # (L-PARTIAL: build the instrument that counts SITES, not totals).
    # ParentNode travels with the hit because the head and slot alone cannot
    # separate two producers that emit the same shape. `list-expr` is the case
    # that forced it: an EMPTY literal comes from lower-empty-list and a
    # populated one from lower-nonempty-list, and telling them apart means
    # looking at the form's own (elems), not at a regex over the whole wire,
    # which attributes every hit in a program by whatever that program happens
    # to contain somewhere else.
    param(
        [Parameter(Mandatory)][AllowNull()]$Node,
        [Parameter(Mandatory)][string]$Atom,
        [string]$ParentHead = '<root>',
        [AllowNull()]$ParentNode = $null,
        [int]$Index = -1
    )

    $out = [System.Collections.Generic.List[object]]::new()
    if ($null -eq $Node) { return $out }

    if ($Node -is [IrAtom]) {
        if (-not $Node.Quoted -and $Node.Value -eq $Atom) {
            [void]$out.Add([pscustomobject]@{
                Parent = $ParentHead; Index = $Index; ParentNode = $ParentNode
            })
        }
        return $out
    }

    $head = Get-IrHead $Node
    if (-not $head) { $head = '<list>' }
    for ($k = 0; $k -lt $Node.Count; $k++) {
        foreach ($hit in (Find-IrAtomSites -Node $Node[$k] -Atom $Atom -ParentHead $head -ParentNode $Node -Index $k)) {
            [void]$out.Add($hit)
        }
    }
    return $out
}

function Get-IrCell {
    # Path grammar, deliberately small. Segments apply left to right:
    #
    #   def:<name>       root: the def form of that name
    #   type             a def's declared type cell (positional slot 4)
    #   body             a def's body expression (positional slot 5)
    #   params           the (params ...) child of the current node
    #   param/<i>        the TYPE cell of parameter i (0-based) of the current
    #                    node, found via its (params ...) child, so it works for
    #                    a def and for a lambda alike
    #   find:<head>      depth-first descendant whose head atom is <head>
    #   slot/<n>         positional element n of the current node
    #
    # Anything it cannot locate returns $null, which the harness reports as
    # UNSUPPORTED rather than as agreement (L-CAPABILITY-LOST).
    param([Parameter(Mandatory)]$Chapter, [Parameter(Mandatory)][string]$Path)

    $parts = $Path.Split('/')
    if ($parts[0] -notlike 'def:*') { throw "ir-wire: unsupported path root '$($parts[0])'" }
    $cur = Get-IrDef -Chapter $Chapter -Name $parts[0].Substring(4)

    $k = 1
    while ($k -lt $parts.Count) {
        if ($null -eq $cur) { return $null }
        $seg = $parts[$k]

        if ($seg -like 'find:*') {
            $cur = Find-IrDescendant -Node $cur -Head $seg.Substring(5)
            $k++
            continue
        }

        switch ($seg) {
            'params' { $cur = Find-IrChild -Node $cur -Head 'params'; $k++ }
            'type'   { $cur = if ($cur -isnot [IrAtom] -and $cur.Count -gt 4) { $cur[4] } else { $null }; $k++ }
            'body'   { $cur = if ($cur -isnot [IrAtom] -and $cur.Count -gt 5) { $cur[5] } else { $null }; $k++ }
            'param'  {
                if ($k + 1 -ge $parts.Count) { return $null }
                $idx = [int]$parts[$k + 1]
                $ps = Find-IrChild -Node $cur -Head 'params'
                if ($null -eq $ps -or ($idx + 1) -ge $ps.Count) { return $null }
                $p = $ps[$idx + 1]
                # (param "name" TYPE)
                $cur = if ($p -isnot [IrAtom] -and $p.Count -ge 3) { $p[2] } else { $null }
                $k += 2
            }
            'slot'   {
                if ($k + 1 -ge $parts.Count) { return $null }
                $n = [int]$parts[$k + 1]
                $cur = if ($cur -isnot [IrAtom] -and $n -lt $cur.Count) { $cur[$n] } else { $null }
                $k += 2
            }
            default { throw "ir-wire: unsupported path segment '$seg'" }
        }
    }
    return $cur
}
