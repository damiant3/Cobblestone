# prose-blocks.ps1 -- R-PROSE campaign helper. Lists each column-2 prose block of the named chapters as
# <file#>.<block#> with the code line it sits over; with -Delete "1.2,3.1" removes those blocks and one
# adjacent blank line. A bullet's indented continuation lines belong to its block. Pair every use with
# tools/agents/prose-probe.ps1 before and after, and read the diff: it must remove prose lines only.
# Call it in-process (& tools/agents/prose-blocks.ps1 -Files $list): pwsh -File binds only an array's first element.
param([Parameter(Mandatory)][string[]]$Files, [string]$Delete = '', [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path)
$del = @{}; foreach ($d in ($Delete -split '[,\s]+' | Where-Object { $_ })) { $del[$d] = $true }
$fi = 0
foreach ($rel in $Files) {
    $fi++
    $f = Join-Path $Root $rel
    $raw = [IO.File]::ReadAllText($f); $nl = if ($raw.Contains("`r`n")) { "`r`n" } else { "`n" }
    $L = [Collections.Generic.List[string]]($raw -split "`r?`n")
    $blocks = @(); $i = 0
    while ($i -lt $L.Count) {
        if ($L[$i] -match '^ \S') {
            $s = $i; $bullet = $L[$i] -match '^ - '
            while ($i + 1 -lt $L.Count -and ($L[$i + 1] -match '^ \S' -or ($bullet -and $L[$i + 1] -match '^\s{2,}\S') -or ($L[$i + 1].Trim() -eq '' -and $i + 2 -lt $L.Count -and $L[$i + 2] -match '^ \S'))) {
                $i++
                if ($L[$i] -match '^ - ') { $bullet = $true } elseif ($L[$i] -match '^ \S') { $bullet = $false }
            }
            $blocks += ,@($s, $i)
        }
        $i++
    }
    if (-not $Delete) {
        Write-Output "### $fi $rel"
        $bi = 0
        foreach ($b in $blocks) { $bi++; $next = $b[1] + 1; while ($next -lt $L.Count -and $L[$next].Trim() -eq '') { $next++ }
            Write-Output "[$fi.$bi] L$($b[0] + 1)-$($b[1] + 1)"; for ($k = $b[0]; $k -le $b[1]; $k++) { if ($L[$k].Trim()) { Write-Output ('  |' + $L[$k].Substring(1)) } }
            if ($next -lt $L.Count) { Write-Output ('  > ' + $L[$next].Trim()) } }
    } else {
        $bi = $blocks.Count
        for ($x = $blocks.Count - 1; $x -ge 0; $x--) {
            if ($del.ContainsKey("$fi.$($x + 1)")) {
                $b = $blocks[$x]; $L.RemoveRange($b[0], $b[1] - $b[0] + 1)
                if ($b[0] -lt $L.Count -and $L[$b[0]].Trim() -eq '' -and $b[0] -gt 0 -and $L[$b[0] - 1].Trim() -eq '') { $L.RemoveAt($b[0]) }
            }
        }
        [IO.File]::WriteAllText($f, ($L -join $nl))
    }
}
