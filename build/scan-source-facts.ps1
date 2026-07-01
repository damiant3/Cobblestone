# Scan the source tree for .codex files, compute SHA-256 hashes,
# and output a SourceDefinition manifest (pipe-delimited).
# Format: sha256-hex|relative-path|quire-name|chapter-name
#
# The manifest is unsigned. Ed25519 signing happens in the Codex
# persistence layer (RepoProtocolPersist, kind 30).
[CmdletBinding()]
param(
    [string]$Root,
    [string]$OutFile,
    [switch]$IncludeTests
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $Root) { $Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path }
$Root = (Resolve-Path $Root).Path

function Is-Excluded {
    param([string]$RelPath)
    foreach ($part in $RelPath.Split('/')) {
        if ($part -eq 'old' -or $part -eq 'build-output' -or $part -eq 'test-output' -or $part -eq 'test-input' -or $part -eq 'output' -or $part -eq 'Done') { return $true }
        if ($part -match '^output-\d{8}') { return $true }
    }
    return $false
}

function Capitalize {
    param([string]$s)
    if ($s.Length -eq 0) { return $s }
    return $s.Substring(0,1).ToUpper() + $s.Substring(1)
}

function Get-QuireName {
    param([string]$RelPath)
    $parts = $RelPath.Split('/')

    if ($parts[0] -eq 'codex' -and $parts.Count -ge 3) {
        if ($parts[1] -eq 'foreword') {
            if ($parts[2] -eq 'core') { return 'Foreword' }
            return Capitalize $parts[2]
        }
        if ($parts[1] -eq 'compiler') { return 'Codex' }
        if ($parts[1] -eq 'os') { return Capitalize $parts[2] }
        if ($parts[1] -eq 'plugs') { return Capitalize $parts[2] }
        if ($parts[1] -eq 'test') { return 'Test' }
        if ($parts[1] -eq 'boards') { return 'Boards' }
    }
    if ($parts[0] -eq 'apps' -and $parts.Count -ge 3) {
        $parentDir = [System.IO.Path]::GetDirectoryName($RelPath).Replace('\', '/')
        $leaf = ($parentDir -split '/')[-1]
        if ($leaf -eq 'pages' -or $leaf -eq 'tests' -or $leaf -eq 'web') {
            $leaf = ($parentDir -split '/')[-2]
        }
        return Capitalize $leaf
    }

    $parent = Split-Path (Split-Path $RelPath) -Leaf
    if ($parent) { return Capitalize $parent }
    return 'Unknown'
}

$chapterPat = '^\s*Chapter:\s*(.+?)\s*$'

$files = Get-ChildItem -Path $Root -Recurse -Filter '*.codex' -File |
    Where-Object {
        $rel = $_.FullName.Substring($Root.Length + 1).Replace('\', '/')
        if (Is-Excluded $rel) { return $false }
        if ($rel -like 'docs/*') { return $false }
        if (-not $IncludeTests -and $rel -like 'codex/test/*') { return $false }
        return $true
    }

$manifest = [System.Collections.Generic.List[string]]::new()

foreach ($f in $files) {
    $relPath = $f.FullName.Substring($Root.Length + 1).Replace('\', '/')
    $hash = (Get-FileHash -Algorithm SHA256 $f.FullName).Hash.ToLower()
    $quire = Get-QuireName $relPath

    $chapter = ''
    $head = Get-Content -TotalCount 5 $f.FullName -ErrorAction SilentlyContinue
    if ($head) {
        foreach ($line in $head) {
            if ($line -match $chapterPat) {
                $chapter = $matches[1]
                break
            }
        }
    }
    if (-not $chapter) {
        $chapter = [System.IO.Path]::GetFileNameWithoutExtension($f.Name)
    }

    $manifest.Add("$hash|$relPath|$quire|$chapter")
}

$manifest.Sort()

if ($OutFile) {
    $dir = Split-Path $OutFile -Parent
    if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force $dir | Out-Null }
    [System.IO.File]::WriteAllText($OutFile, ($manifest -join "`n") + "`n",
        [System.Text.UTF8Encoding]::new($false))
    Write-Host "$($manifest.Count) source definitions written to $OutFile"
} else {
    $manifest | ForEach-Object { Write-Output $_ }
}
