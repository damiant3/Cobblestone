# check-constants.ps1 — verify seed constants match source
#
# Extracts load-bearing constant values from the compiler source
# (X86_64Boot.codex Memory Address Map + BuildSettings.codex) and
# compares their SHA256 hash against seed/constants.hash. If they
# differ, the seed was built with different values and a rebuild
# is required.
#
# Usage: check-constants.ps1 [-Update]
#   Without -Update: prints MATCH or MISMATCH + exits 0 or 1
#   With -Update: writes new hash to seed/constants.hash

param([switch]$Update)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$BootFile = Join-Path $Repo 'codex\compiler\Emit\X86_64Boot.codex'
$SettingsFile = Join-Path $Repo 'codex\compiler\Core\BuildSettings.codex'
$HashFile = Join-Path $Repo 'seed\constants.hash'

$constants = [System.Collections.Generic.SortedDictionary[string,string]]::new()

foreach ($file in @($BootFile, $SettingsFile)) {
    foreach ($line in [System.IO.File]::ReadAllLines($file)) {
        if ($line -match '^\s+(\S+)\s*:\s*Integer\s*=\s*(\-?\d+)\s*$') {
            $constants[$matches[1]] = $matches[2]
        }
    }
}

$sb = [System.Text.StringBuilder]::new()
foreach ($kv in $constants.GetEnumerator()) {
    [void]$sb.AppendLine("$($kv.Key)=$($kv.Value)")
}
$text = $sb.ToString()
$bytes = [System.Text.Encoding]::UTF8.GetBytes($text)
$hash = [System.Security.Cryptography.SHA256]::Create().ComputeHash($bytes)
$hexHash = ($hash | ForEach-Object { $_.ToString("x2") }) -join ''

if ($Update) {
    [System.IO.File]::WriteAllText($HashFile, "$hexHash`n", [System.Text.UTF8Encoding]::new($false))
    Write-Host "constants.hash updated: $hexHash ($($constants.Count) constants)"
    exit 0
}

if (-not (Test-Path $HashFile)) {
    Write-Host "WARNING: seed/constants.hash not found — run 'check-constants.ps1 -Update' after seed rebuild"
    exit 1
}

$savedHash = (Get-Content $HashFile -First 1).Trim()
if ($hexHash -eq $savedHash) {
    Write-Host "Constants MATCH seed ($($constants.Count) constants)"
    exit 0
} else {
    Write-Host "WARNING: Constants MISMATCH — source constants differ from seed"
    Write-Host "  Source hash: $hexHash"
    Write-Host "  Seed hash:   $savedHash"
    Write-Host "  A seed rebuild is required before the changes take effect."
    Write-Host "  Run: build/build.ps1"
    exit 1
}
