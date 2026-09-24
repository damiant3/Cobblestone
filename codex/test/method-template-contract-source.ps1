function Get-MethodContractUnit {
    param([Parameter(Mandatory)][string]$Source)
    . (Join-Path $PSScriptRoot '../../build/quire-map.ps1')
    . (Join-Path $PSScriptRoot '../../build/vm-config.ps1')
    $repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
    $lines = [IO.File]::ReadAllLines((Resolve-Path -LiteralPath $Source).Path)
    $ordered = Resolve-CiteOrder -RootLines $lines -Repo $repo
    $prefix = Format-CiteChapters -Ordered $ordered
    [long]$offset = 0
    foreach ($line in $prefix) { $offset += @(ConvertTo-CceBytes ($line + "`n")).Count }
    $offsets = [Collections.Generic.List[long]]::new()
    foreach ($line in $lines) {
        $offsets.Add($offset)
        $offset += @(ConvertTo-CceBytes ($line + "`n")).Count
    }
    [pscustomobject]@{ SourceLines=$lines; LineOffsets=$offsets.ToArray(); SourceBytes=$offset }
}

function Get-MethodContractSource {
    param([Parameter(Mandatory)][string]$Source)
    $path = (Resolve-Path -LiteralPath $Source).Path
    $text = [IO.File]::ReadAllText($path)
    $lines = $text -split '\r?\n'
    $classes = [Collections.Generic.List[object]]::new()
    $instances = [Collections.Generic.List[object]]::new()
    $chapter = $null
    $current = $null
    $currentInstance = $null
    for ($i = 0; $i -lt $lines.Length; $i++) {
        $line = $lines[$i]
        if ($line -match '^Chapter: ([A-Za-z][A-Za-z0-9]*)\s*$') {
            if ($chapter) { throw 'contract-source: multiple chapters are outside the fixture grammar' }
            $chapter = $Matches[1]
        } elseif ($line -match '^class ([A-Z][A-Za-z0-9]*) where\s*$') {
            $current = [pscustomobject]@{
                Name = $Matches[1]; Line = $i + 1
                ClassBinders = @('a'); Methods = [Collections.Generic.List[object]]::new()
            }
            $classes.Add($current)
            $currentInstance = $null
        } elseif ($line -match '^instance ([A-Z][A-Za-z0-9]*) (Integer|Boolean) where\s*$') {
            $currentInstance = [pscustomobject]@{
                Class = $Matches[1]; Head = $Matches[2]; Line = $i + 1
                Methods = [Collections.Generic.List[object]]::new()
            }
            $instances.Add($currentInstance)
            $current = $null
        } elseif ($line -match '^(class|instance)\b') {
            throw "contract-source: unsupported declaration at line $($i + 1)"
        } elseif ($line -match '^Section:') {
            $current = $null
            $currentInstance = $null
        } elseif ($null -ne $current -and $line -match '^  ([a-z][a-z0-9-]*) : (.+)$') {
            $name = $Matches[1]
            $signature = $Matches[2]
            $parts = @($signature -split '\s*(?:,|->)\s*')
            if ($signature -notmatch '->' -or $parts.Count -lt 2) {
                throw "contract-source: unsupported method signature at line $($i + 1)"
            }
            $binders = [Collections.Generic.List[string]]::new()
            $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            foreach ($part in $parts) {
                if ($part -cnotmatch '^(?:[a-z]|Integer|Boolean|Text)$') {
                    throw "contract-source: unsupported type '$part' at line $($i + 1)"
                }
                if ($part -cmatch '^[a-z]$' -and $part -cne 'a' -and $seen.Add($part)) {
                    $binders.Add($part)
                }
            }
            $schema = [Text.StringBuilder]::new()
            for ($j = 0; $j -lt $parts.Count - 1; $j++) {
                [void]$schema.Append('(a-fun (a-named "').Append($parts[$j]).Append('") ')
            }
            [void]$schema.Append('(a-named "').Append($parts[-1]).Append('")')
            [void]$schema.Append(')', $parts.Count - 1)
            $current.Methods.Add([pscustomobject]@{
                Name = $name; Line = $i + 1; Signature = $signature
                MethodBinders = @($binders.ToArray()); Schema = $schema.ToString()
            })
        } elseif ($null -ne $current -and $line.Trim()) {
            throw "contract-source: unsupported class content at line $($i + 1)"
        } elseif ($null -ne $currentInstance -and $line -match '^  ([a-z][a-z0-9-]*) ((?:\([a-z][a-z0-9-]*\)\s*)+) = (.+)$') {
            $currentInstance.Methods.Add([pscustomobject]@{
                Name = $Matches[1]; Parameters = $Matches[2].Trim()
                Body = $Matches[3]; Line = $i + 1
            })
        } elseif ($null -ne $currentInstance -and $line.Trim()) {
            throw "contract-source: unsupported instance content at line $($i + 1)"
        }
    }
    if (-not $chapter) { throw 'contract-source: missing chapter' }
    $classesByName = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    $methodSets = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    foreach ($class in $classes) {
        if ($classesByName.ContainsKey($class.Name) -or $class.Methods.Count -eq 0) {
            throw "contract-source: duplicate or empty class $($class.Name)"
        }
        $classesByName.Add($class.Name, $class)
        $methodNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($method in $class.Methods) {
            if (-not $methodNames.Add($method.Name)) { throw "contract-source: duplicate method $($method.Name)" }
        }
        $methodSets.Add($class.Name, $methodNames)
    }
    foreach ($instance in $instances) {
        if (-not $classesByName.ContainsKey($instance.Class)) { throw 'contract-source: instance without fixture class' }
        $class = $classesByName[$instance.Class]
        $methodNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($method in $instance.Methods) {
            if (-not $methodNames.Add($method.Name) -or -not $methodSets[$instance.Class].Contains($method.Name)) {
                throw "contract-source: duplicate or unknown instance method $($method.Name)"
            }
        }
        if ($methodNames.Count -ne $class.Methods.Count) { throw 'contract-source: missing instance method' }
    }
    [pscustomobject]@{
        Source = $path; Sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        Chapter = $chapter; Classes = @($classes.ToArray()); Instances = @($instances.ToArray())
    }
}
