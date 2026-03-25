# Strips "File: ..." and markdown fence lines injected by VS Copilot tools.
# Only removes lines matching the known pollution patterns at the top of files.
# Does NOT rewrite any other content — just trims the prefix garbage.

param([string]$Path)

$lines = [System.IO.File]::ReadAllLines($Path, [System.Text.Encoding]::UTF8)
$start = 0

# Skip "File: <path>" line if present
if ($lines.Length -gt 0 -and $lines[0] -match '^File: ') {
    $start++
}

# Skip markdown code fence if present
if ($lines.Length -gt $start -and $lines[$start] -match '^`') {
    $start++
}

if ($start -eq 0) {
    Write-Output "  $Path - clean (no pollution found)"
    return
}

$clean = $lines[$start..($lines.Length - 1)]
$utf8NoBom = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllLines($Path, $clean, $utf8NoBom)
Write-Output "  $Path - stripped $start lines"
