# codex-agent-verify.ps1 — Post-write verification and auto-decontamination.
#
# Run after ANY file write (edit_file, create_file) to detect and strip
# tool-injected metadata (TEF-008). This is PREVENTION, not recovery.
#
# Usage:
#   pwsh -File tools/codex-agent-verify.ps1 <file> [<file2> ...]
#
# What it checks:
#   1. Line 1 matches "File: <path>" pattern → strips it
#   2. Line 1 or 2 is a markdown code fence (backticks) → strips it
#   3. Reports what it found and fixed
#
# Exit codes:
#   0 — file was clean or successfully decontaminated
#   1 — no file specified

param(
    [Parameter(Mandatory=$true, Position=0, ValueFromRemainingArguments=$true)]
    [string[]]$Files
)

if ($Files.Count -eq 0) {
    Write-Host "Usage: pwsh -File tools/codex-agent-verify.ps1 <file> [<file2> ...]" -ForegroundColor Yellow
    exit 1
}

$utf8NoBom = New-Object System.Text.UTF8Encoding $false
$totalFixed = 0

foreach ($file in $Files) {
    if (-not (Test-Path $file)) {
        Write-Host "  SKIP $file (not found)" -ForegroundColor Yellow
        continue
    }

    $lines = [System.IO.File]::ReadAllLines($file, [System.Text.Encoding]::UTF8)
    if ($lines.Count -eq 0) {
        Write-Host "  OK   $file (empty)" -ForegroundColor Green
        continue
    }

    $stripped = 0

    # Check for "File: <path>" on line 1
    if ($lines[0] -match '^File: ') {
        $stripped++
    }

    # Check for markdown code fence on the next line
    if ($lines.Count -gt $stripped -and $lines[$stripped] -match '^`') {
        $stripped++
    }

    if ($stripped -eq 0) {
        Write-Host "  OK   $file" -ForegroundColor Green
    } else {
        $clean = $lines[$stripped..($lines.Count - 1)]
        [System.IO.File]::WriteAllLines($file, $clean, $utf8NoBom)
        Write-Host "  FIX  $file (stripped $stripped lines: TEF-008)" -ForegroundColor Red
        $totalFixed++
    }
}

if ($totalFixed -gt 0) {
    Write-Host ""
    Write-Host "  $totalFixed file(s) decontaminated. Run 'codex-agent peek <file> 1 3' to confirm." -ForegroundColor Yellow
} else {
    Write-Host ""
    Write-Host "  All files clean." -ForegroundColor Green
}
