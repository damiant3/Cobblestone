# Concatenate codex/ source with quire-prefixed chapters and any
# cited foreword chapters preloaded. Chapters within each subdirectory
# are topologically sorted so cited chapters appear before consumers.
# Writes to stdout (or -OutFile).
[CmdletBinding()]
param(
    [string]$CodexDir,
    [string]$OutFile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $CodexDir) { $CodexDir = Join-Path $Repo 'codex\compiler' }
$CodexDir   = (Resolve-Path $CodexDir).Path
$ForewordDir = Join-Path $Repo 'codex\foreword\core'

$lines = [System.Collections.Generic.List[string]]::new()

function Add-WithQuire {
    param([string]$Path, [string]$Quire)
    $renamed = $false
    foreach ($l in [System.IO.File]::ReadAllLines($Path)) {
        if ((-not $renamed) -and $Quire -and $l -match '^Chapter:\s*(.+?)\s*$') {
            $lines.Add("Chapter: ${Quire}--$($matches[1])")
            $renamed = $true
        } else {
            $lines.Add($l)
        }
    }
    $lines.Add('')
    $lines.Add('')
}

# 1. Cited library chapters — scan compiler source for
#    "cites <LibraryQuire> chapter X", then transitively resolve each
#    pulled chapter's own library cites. The quire table must agree
#    with compile.ps1's Resolve-CiteOrder (same quires, same
#    "Quire--Chapter" prefix) or the gate's raw concat and the
#    per-compile resolution disagree about what a unit is.
$libQuireDirs = [ordered]@{
    'Foreword' = $ForewordDir
    'Math'     = Join-Path $Repo 'codex\foreword\math'
}
$quireAlt = ($libQuireDirs.Keys -join '|')
$citePat = "^\s*cites\s+($quireAlt)\s+chapter\s+([A-Za-z_][A-Za-z0-9_-]*)"
$queue = [System.Collections.Generic.Queue[object]]::new()
Get-ChildItem $CodexDir -Recurse -Depth 2 -Filter '*.codex' -File | ForEach-Object {
    foreach ($l in [System.IO.File]::ReadAllLines($_.FullName)) {
        if ($l -match $citePat) { $queue.Enqueue(@{ Quire = $matches[1]; Name = $matches[2] }) }
    }
}
$seen = [System.Collections.Generic.HashSet[string]]::new()
$ordered = @()
while ($queue.Count -gt 0) {
    $fw = $queue.Dequeue()
    if (-not $seen.Add("$($fw.Quire)|$($fw.Name)")) { continue }
    $fwPath = Join-Path $libQuireDirs[$fw.Quire] "$($fw.Name).codex"
    if (-not (Test-Path -PathType Leaf $fwPath)) { continue }
    $fwLines = [System.IO.File]::ReadAllLines($fwPath)
    foreach ($l in $fwLines) {
        if ($l -match $citePat) { $queue.Enqueue(@{ Quire = $matches[1]; Name = $matches[2] }) }
    }
    $ordered += @{ Quire = $fw.Quire; Name = $fw.Name; Path = $fwPath }
}
[array]::Reverse($ordered)
$emitted = [System.Collections.Generic.HashSet[string]]::new()
foreach ($entry in $ordered) {
    if (-not $emitted.Add("$($entry.Quire)|$($entry.Name)")) { continue }
    Add-WithQuire -Path $entry.Path -Quire $entry.Quire
}

# Ordinal name comparer for FileInfo / DirectoryInfo arrays.
$nameCmp = [System.Collections.Generic.Comparer[object]]::Create({
    param($a, $b)
    [System.StringComparer]::Ordinal.Compare($a.Name, $b.Name)
})

# Topological sort: files within a subdirectory may cite each other via
# "cites <QuireName> chapter <ChapterName>". Cited files must appear
# before consumers so the repl-mode compiler sees definitions in order.
# Topological sort within a subdirectory. Uses file stems as keys (not
# chapter names, which can collide across split files). Resolves cites
# by mapping chapter names back to the file that defines them; when
# multiple files share a chapter name, the first alphabetically wins
# the mapping but all files are emitted.
function Sort-ByDeps {
    param([System.IO.FileInfo[]]$Files, [string]$QuireName)
    if ($Files.Count -le 1) { return $Files }
    $internalCite = "^\s*cites\s+($QuireName|Codex)\s+chapter\s+(.+?)(?:\s*\(|$)"
    $chapterPat = '^Chapter:\s*(.+?)\s*$'
    $chapterToStem = @{}
    $stemToFile = @{}
    $deps = @{}
    foreach ($f in $Files) {
        $stem = [System.IO.Path]::GetFileNameWithoutExtension($f.Name)
        $stemToFile[$stem] = $f
        $firstLine = (Get-Content -TotalCount 1 $f.FullName)
        if ($firstLine -match $chapterPat) {
            $cname = $matches[1]
            if (-not $chapterToStem.ContainsKey($cname)) { $chapterToStem[$cname] = $stem }
        }
        $deps[$stem] = @()
        foreach ($l in [System.IO.File]::ReadAllLines($f.FullName)) {
            if ($l -match $internalCite) { $deps[$stem] += $matches[2] }
        }
    }
    $result = [System.Collections.Generic.List[System.IO.FileInfo]]::new()
    $visited = [System.Collections.Generic.HashSet[string]]::new()
    $visiting = [System.Collections.Generic.HashSet[string]]::new()
    function Visit([string]$Stem) {
        if ($visited.Contains($Stem)) { return }
        if ($visiting.Contains($Stem)) { return }
        [void]$visiting.Add($Stem)
        foreach ($citedChapter in $deps[$Stem]) {
            $depStem = $chapterToStem[$citedChapter]
            if ($depStem -and $stemToFile.ContainsKey($depStem)) { Visit $depStem }
        }
        [void]$visiting.Remove($Stem)
        [void]$visited.Add($Stem)
        $result.Add($stemToFile[$Stem])
    }
    $sorted = @($stemToFile.Keys) | Sort-Object
    foreach ($stem in $sorted) { Visit $stem }
    return $result.ToArray()
}

# 2. Root .codex (depth 0), sorted ordinal.
$rootFiles = @(Get-ChildItem $CodexDir -Filter '*.codex' -File)
[Array]::Sort($rootFiles, $nameCmp)
foreach ($f in $rootFiles) {
    Add-WithQuire -Path $f.FullName -Quire ''
}

# 3. Subdirs (depth 1), each .codex prefixed with subdir name, sorted ordinal.
$subDirs = @(Get-ChildItem $CodexDir -Directory)
[Array]::Sort($subDirs, $nameCmp)
foreach ($d in $subDirs) {
    $quire = $d.Name
    $subFiles = @(Get-ChildItem $d.FullName -Filter '*.codex' -File)
    [Array]::Sort($subFiles, $nameCmp)
    $subFiles = @($subFiles | Where-Object { $_.Name -match 'State|Encoder' }) + @($subFiles | Where-Object { $_.Name -notmatch 'State|Encoder' })
    foreach ($f in $subFiles) {
        Add-WithQuire -Path $f.FullName -Quire $quire
    }
}

$body = ($lines -join "`n") + "`n"
if ($OutFile) {
    [System.IO.File]::WriteAllText($OutFile, $body, [System.Text.UTF8Encoding]::new($false))
} else {
    [Console]::Out.Write($body)
}
