[CmdletBinding()]
param([Parameter(Mandatory)][string]$Repo, [string]$Catalog = '')
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$Repo=(Resolve-Path -LiteralPath $Repo).Path
if(-not $Catalog){$Catalog=Join-Path $Repo 'build/tool-catalog.json'}
$data=Get-Content -LiteralPath $Catalog -Raw -Encoding utf8 | ConvertFrom-Json
function Tool-Path([string]$Path) {
    if($Path -match '^old/'){return $false}
    $ext=[IO.Path]::GetExtension($Path)
    if($data.discovery.extensions -contains $ext){return $true}
    if($data.discovery.nativeRoots -contains ($Path -split '/')[0] -and $data.discovery.nativeExtensions -contains $ext){return $true}
    foreach($root in $data.discovery.codexRoots){if($Path.StartsWith($root+'/') -and $ext -eq '.codex'){return $true}}
    return $false
}
Push-Location $Repo
try {
    $info=p4 -ztag -F '%clientStream%' info
    if($LASTEXITCODE -ne 0 -or @($info).Count -ne 1 -or $info -notmatch '^//Codex/'){throw 'Cannot identify Perforce stream'}
    $tracked=p4 -ztag -F '%depotFile%|%action%' files "$info/...#have"
    if($LASTEXITCODE -ne 0){throw 'Cannot inventory tracked tools'}
    $opened=p4 -ztag -F '%depotFile%|%action%' opened 2>&1
    if($LASTEXITCODE -ne 0 -and "$opened" -notmatch 'not opened'){throw "Cannot inventory open tools: $opened"}
    $current=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($row in @($tracked)+@($opened)) {
        $path,$action=[string]$row -split '\|',2
        if(-not $path.StartsWith($info+'/')){continue}
        $path=$path.Substring($info.Length+1)
        if($action -match 'delete'){[void]$current.Remove($path)}elseif(Tool-Path $path){[void]$current.Add($path)}
    }
    $baseline=p4 -ztag -F '%depotFile%|%action%' files "//Codex/main/...@$($data.legacyMain)"
    if($LASTEXITCODE -ne 0){throw 'Cannot verify legacy baseline'}
    $legacy=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($row in $baseline){$p,$a=$row -split '\|',2;if($a -notmatch 'delete'){[void]$legacy.Add(($p -replace '^//Codex/main/',''))}}
    $ids=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $paths=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $problems=[Collections.Generic.List[string]]::new()
    foreach($tool in $data.tools) {
        if(-not $ids.Add($tool.id)){$problems.Add("Duplicate ID: $($tool.id)")}
        if(-not $paths.Add($tool.path)){$problems.Add("Duplicate path: $($tool.path)")}
        foreach($path in @($tool.path,$tool.source)) {
            if($path -match '(^/|^\w:|(^|/)\.\.(/|$))' -or -not (Test-Path -LiteralPath (Join-Path $Repo $path) -PathType Leaf)){$problems.Add("Missing or invalid path: $path")}
        }
        if($tool.disposition -eq 'legacy-pending') {
            if(-not $legacy.Contains($tool.path)){$problems.Add("New tool lacks classification: $($tool.path)")}
        } else {
            if($tool.disposition -notin @('native-operation','host-adapter','independent-witness','repository-tool','temporary-investigation')){$problems.Add("Invalid disposition: $($tool.path)")}
            foreach($field in @('owner','callers','inputs','outputs','capabilities','replacement')) {
                if(-not $tool.PSObject.Properties[$field] -or -not $tool.$field -or $tool.$field -eq 'pending'){$problems.Add("Missing $field contract: $($tool.path)")}
            }
        }
    }
    foreach($path in $current){if(-not $paths.Contains($path)){$problems.Add("Uncataloged tool: $path")}}
    if($problems.Count){$problems | ForEach-Object {[Console]::Error.WriteLine($_)};exit 1}
    "tool-catalog: $($data.tools.Count) entries; $(@($data.tools | Where-Object disposition -eq legacy-pending).Count) legacy classifications pending"
} finally {Pop-Location}
