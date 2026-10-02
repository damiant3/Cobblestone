# check-tool-catalog-trigger.ps1 -- the Perforce change-content trigger that
# refuses a submit adding a tool file the stream's build/tool-catalog.json does
# not classify. Server-side twin of build/checks/tool-catalog.ps1 for the one
# failure that check kept finding after the fact.
#
# Installed copy: D:\PerforceRoot\triggers\check-tool-catalog-trigger.ps1, named
# in `p4 triggers` as check-tool-catalog on change-content //Codex/main/....
# The depot file is the source; re-copy it there after editing
# (PerforceProcess.md 4.9). Hand-written; no generator emits this.
#
# WHAT IT GRADES. Every file the change ADDS to a path (add, branch, move/add,
# import) that the catalog's own discovery rules call a tool. Each must have a
# catalog entry whose disposition is classified and whose six contract fields
# are filled. The catalog is read as the change will leave it: from the change
# when the change carries build/tool-catalog.json, else from the stream head.
# Edits to tools already present are not graded here; the full check stays the
# lane's (build/checks/tool-catalog.ps1).
param(
    [Parameter(Mandatory)][string]$Changelist,
    [Parameter(Mandatory)][string]$ServerPort
)

$ErrorActionPreference = 'Stop'
$p4 = @('-p', $ServerPort, '-u', 'damian', '-C', 'utf8')
$cmp = [StringComparer]::OrdinalIgnoreCase

$rows = [System.Collections.Generic.List[object]]::new()
$tag = & p4 @p4 -ztag files "//Codex/...@=$Changelist" 2>&1
$file = $null; $action = $null
foreach ($l in @($tag) + @('... depotFile END')) {
    $l = [string]$l
    if ($l -like '... depotFile *') {
        if ($file) { $rows.Add([pscustomobject]@{ File = $file; Action = $action }) }
        $file = $l.Substring(13).Trim(); $action = $null
    } elseif ($l -like '... action *') { $action = $l.Substring(11).Trim() }
}

$byStream = @{}
foreach ($r in $rows) {
    if ($r.File -notmatch '^(//Codex/[^/]+)/(.+)$') { continue }
    $stream = $Matches[1]; $rel = $Matches[2]
    if (-not $byStream.ContainsKey($stream)) { $byStream[$stream] = [System.Collections.Generic.List[object]]::new() }
    $byStream[$stream].Add([pscustomobject]@{ Rel = $rel; Action = $r.Action })
}

$bad = [System.Collections.Generic.List[string]]::new()
foreach ($stream in $byStream.Keys) {
    $items = $byStream[$stream]
    $added = @($items | Where-Object { $_.Action -in @('add', 'branch', 'move/add', 'import') })
    if ($added.Count -eq 0) { continue }

    $catRel = 'build/tool-catalog.json'
    $inChange = @($items | Where-Object { $cmp.Equals($_.Rel, $catRel) -and $_.Action -notlike '*delete' }).Count -gt 0
    $spec = if ($inChange) { "$stream/$catRel@=$Changelist" } else { "$stream/$catRel" }
    $tmp = [System.IO.Path]::GetTempFileName()
    try {
        & p4 @p4 print -q -o $tmp $spec | Out-Null
        $data = Get-Content -LiteralPath $tmp -Raw -Encoding utf8 | ConvertFrom-Json
    } catch {
        $bad.Add("$stream/$catRel cannot be read or parsed: $_"); continue
    } finally { Remove-Item $tmp -ErrorAction SilentlyContinue }

    $entries = @{}
    foreach ($t in $data.tools) { $entries[$t.path.ToLowerInvariant()] = $t }
    $classes = @('native-operation', 'host-adapter', 'independent-witness', 'repository-tool', 'temporary-investigation')

    foreach ($a in $added) {
        $path = $a.Rel
        if ($path -match '^old/') { continue }
        $ext = [IO.Path]::GetExtension($path)
        $isTool = ($data.discovery.extensions -contains $ext) -or
            (($data.discovery.nativeRoots -contains ($path -split '/')[0]) -and ($data.discovery.nativeExtensions -contains $ext))
        foreach ($root in $data.discovery.codexRoots) { if ($path.StartsWith($root + '/') -and $ext -eq '.codex') { $isTool = $true } }
        if (-not $isTool) { continue }
        $t = $entries[$path.ToLowerInvariant()]
        if (-not $t) { $bad.Add("Uncataloged tool: $stream/$path"); continue }
        if ($t.disposition -notin $classes) { $bad.Add("New tool lacks classification: $stream/$path"); continue }
        foreach ($field in @('owner', 'callers', 'inputs', 'outputs', 'capabilities', 'replacement')) {
            if (-not $t.PSObject.Properties[$field] -or -not $t.$field -or $t.$field -eq 'pending') { $bad.Add("Missing $field contract: $stream/$path") }
        }
    }
}

if ($bad.Count -eq 0) { exit 0 }
Write-Output "check-tool-catalog: change $Changelist adds a tool that build/tool-catalog.json does not classify."
Write-Output "Add the entry (disposition plus owner, callers, inputs, outputs, capabilities, replacement) in the same change (PerforceProcess.md 4.9)."
foreach ($x in $bad) { Write-Output "  $x" }
exit 1
