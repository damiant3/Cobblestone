# Validate TTF fonts by reading their headers.
# Reports: sfnt version, number of tables, unitsPerEm, numGlyphs.
param([string]$Dir = "fonts/cc0")

$ErrorActionPreference = 'Stop'

function Read-U16BE($bytes, $off) {
    return $bytes[$off] * 256 + $bytes[$off+1]
}
function Read-U32BE($bytes, $off) {
    return $bytes[$off] * 16777216 + $bytes[$off+1] * 65536 + $bytes[$off+2] * 256 + $bytes[$off+3]
}
function Read-Tag($bytes, $off) {
    return [System.Text.Encoding]::ASCII.GetString($bytes, $off, 4)
}

function Find-Table($bytes, $numTables, $tag) {
    for ($i = 0; $i -lt $numTables; $i++) {
        $toff = 12 + $i * 16
        $t = Read-Tag $bytes $toff
        if ($t -eq $tag) { return Read-U32BE $bytes ($toff + 8) }
    }
    return -1
}

$ttfs = Get-ChildItem $Dir -Recurse -Filter "*.ttf"
Write-Host ("=" * 80)
Write-Host ("Font Validation Report: $($ttfs.Count) fonts")
Write-Host ("=" * 80)
Write-Host ""

$results = @()
foreach ($f in $ttfs) {
    $bytes = [System.IO.File]::ReadAllBytes($f.FullName)
    $sfnt = Read-U32BE $bytes 0
    $numTables = Read-U16BE $bytes 4

    $headOff = Find-Table $bytes $numTables "head"
    $maxpOff = Find-Table $bytes $numTables "maxp"
    $cmapOff = Find-Table $bytes $numTables "cmap"
    $glyfOff = Find-Table $bytes $numTables "glyf"

    $upem = if ($headOff -ge 0) { Read-U16BE $bytes ($headOff + 18) } else { 0 }
    $numGlyphs = if ($maxpOff -ge 0) { Read-U16BE $bytes ($maxpOff + 4) } else { 0 }
    $hasCmap = $cmapOff -ge 0
    $hasGlyf = $glyfOff -ge 0
    $isTTF = $sfnt -eq 0x00010000

    $status = if ($isTTF -and $hasCmap -and $hasGlyf) { "OK" } else { "WARN" }
    $sizeKB = [math]::Round($f.Length / 1024)

    Write-Host "$status  $($f.Name.PadRight(35)) ${sizeKB}KB  ${numGlyphs} glyphs  ${upem} UPM  tables=$numTables  cmap=$hasCmap glyf=$hasGlyf"
    $results += @{ Name=$f.Name; Status=$status; Glyphs=$numGlyphs; UPM=$upem }
}

Write-Host ""
$ok = ($results | Where-Object { $_.Status -eq "OK" }).Count
Write-Host "$ok / $($results.Count) fonts validated OK"
