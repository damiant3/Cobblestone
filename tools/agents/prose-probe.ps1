# prose-probe.ps1 -- R-PROSE campaign proof. For each chapter in -ListFile (repo-relative paths, one per line)
# compiles the chapter if it declares opening, else a one-line wrapper that cites it by the quire
# build/host/windows/quire-map.ps1 maps its directory to, against seed/Codex.cdx, and writes probe-<Tag>.csv
# (exit and CDX hash, or the sorted error codes) beside the list. Run it before and after an edit and compare;
# a chapter in an unmapped directory reports NOQUIRE and is proven by diff shape alone. A wrapped chapter's hash is
# the wrapper's (dead code is dropped, so every wrapped chapter hashes alike): it proves the chapter compiles, and the
# diff shape is what proves nothing but prose moved.
param([Parameter(Mandatory)][string]$ListFile, [Parameter(Mandatory)][string]$Tag, [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path, [int]$Jobs = 10)
$out = Join-Path (Split-Path $ListFile) "probe-$Tag"; New-Item -ItemType Directory -Force $out | Out-Null
$qm = Get-Content (Join-Path $Root 'build/host/windows/quire-map.ps1') -Raw
$dirs = @{}; foreach ($m in [regex]::Matches($qm, "'(\w+)'\s*=\s*'([^']+)'")) { $dirs[$m.Groups[2].Value.ToLowerInvariant()] = $m.Groups[1].Value }
foreach ($m in [regex]::Matches($qm, "\`$QuireDirs\['(\w+)'\]\s*=\s*'([^']+)'")) { $dirs[$m.Groups[2].Value.ToLowerInvariant()] = $m.Groups[1].Value }
$items = foreach ($rel in Get-Content $ListFile) {
    $full = Join-Path $Root $rel
    $dir = (Split-Path $rel -Parent).Replace('/', '\').ToLowerInvariant()
    $q = $null; $d = $dir; while ($d) { if ($dirs.ContainsKey($d)) { $q = $dirs[$d]; break }; $d = Split-Path $d -Parent }
    $text = Get-Content $full
    $name = (($text | Where-Object { $_ -match '^Chapter:' } | Select-Object -First 1) -replace '^Chapter:\s*', '').Trim()
    $entry = [bool]($text | Where-Object { $_ -match '^\s+opening\s*(:|=)' } | Select-Object -First 1)
    [pscustomobject]@{ rel = $rel; full = $full; quire = $q; name = $name; entry = $entry }
}
$res = $items | ForEach-Object -ThrottleLimit $Jobs -Parallel {
    $it = $_; $o = $using:out; $root = $using:Root
    $b = ($it.rel -replace '[\\/]', '_') -replace '\.codex$', ''
    if ($it.entry) { $src = $it.full }
    elseif ($it.quire) { $src = Join-Path $o "$b.wrap.codex"; "Chapter: ProseProbe`n  cites $($it.quire) chapter $($it.name)`n`nSection: Entry`n  opening : [Console] Nothing = act`n    print-line-uni ""probe""`n  end" | Set-Content $src }
    else { return [pscustomobject]@{ rel = $it.rel; exit = 'NOQUIRE'; hash = '' } }
    Push-Location $root
    & pwsh -NoProfile -File build/compile.ps1 -Src $src -Out (Join-Path $o "$b.cdx") -Log (Join-Path $o "$b.log") -Kernel seed\Codex.cdx *> $null
    $ex = $LASTEXITCODE; Pop-Location
    $h = if ($ex -eq 0 -and (Test-Path (Join-Path $o "$b.cdx"))) { (Get-FileHash (Join-Path $o "$b.cdx")).Hash } else { (@(Select-String -Path (Join-Path $o "$b.log") -Pattern 'error (CDX)?\d+' -AllMatches -ErrorAction SilentlyContinue | ForEach-Object { $_.Matches.Value }) | Sort-Object) -join ',' }
    [pscustomobject]@{ rel = $it.rel; exit = "$ex"; hash = $h }
}
$res | Sort-Object rel | Export-Csv -NoTypeInformation (Join-Path (Split-Path $ListFile) "probe-$Tag.csv")
"probe $Tag : $(@($res).Count) chapters; exit 0: $(@($res | Where-Object exit -eq '0').Count); no quire: $(@($res | Where-Object exit -eq 'NOQUIRE').Count)"
