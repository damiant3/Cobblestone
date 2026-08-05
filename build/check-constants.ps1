# check-constants.ps1 -- Verify seed constants hash matches compiler source
# Generated from Codex Shell DSL. Do not edit by hand.
[CmdletBinding()]
param(
    [switch]$Update
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$BootFile = (Join-Path $Repo 'codex\compiler\Emit\X86_64Boot.codex')
$SettingsFile = (Join-Path $Repo 'codex\compiler\Core\BuildSettings.codex')
$HashFile = (Join-Path $Repo 'seed\constants.hash')


$constants = [System.Collections.Generic.SortedDictionary[string,string]]::new()
foreach ($file in @($BootFile, $SettingsFile)) {
    foreach ($line in ([System.IO.File]::ReadAllLines($file))) {
        if (($line -match '^\s+(\S+)\s*:\s*Integer\s*=\s*(\-?\d+)\s*$')) {
            $constants[$matches[1]] = $matches[2]
        }
    }
}


$sb = [System.Text.StringBuilder]::new()
foreach ($kv in $constants.GetEnumerator()) {
    [void]$sb.AppendLine(([string]([string]$kv.Key + '=') + $kv.Value))
}
$text = $sb.ToString()
$bytes = ([System.Text.Encoding]::UTF8).GetBytes($text)
$hash = ([System.Security.Cryptography.SHA256]::Create()).ComputeHash($bytes)
$hexHash = (($hash | ForEach-Object { $_.ToString('x2') }) -join '')


if ($Update) {
    $current = ''
    if ((Test-Path -PathType Leaf $HashFile)) {
        $current = (Get-Content $HashFile -First 1).Trim()
    }

    if (($current -eq $hexHash)) {
        Write-Host ([string]([string]'constants.hash unchanged: ' + $hexHash) + ([string]([string]' (' + $constants.Count) + ' constants)'))
        exit 0
    }
    if ((Test-Path -PathType Leaf $HashFile)) {
        Set-ItemProperty $HashFile -Name IsReadOnly -Value $false -ErrorAction SilentlyContinue
    }
    [System.IO.File]::WriteAllText($HashFile, ([string]$hexHash + "`n"), ([System.Text.UTF8Encoding]::new($false)))
    Write-Host ([string]([string]'constants.hash CHANGED: ' + $hexHash) + ([string]([string]' (' + $constants.Count) + ' constants)'))
    Write-Host '  the constants moved, so this file is part of the change -- p4 edit and submit it with the seed'
    exit 0
}


if ((-not (Test-Path -PathType Leaf $HashFile))) {
    Write-Host 'WARNING: seed/constants.hash not found -- run ''check-constants.ps1 -Update'' after seed rebuild'
    exit 1
}
$savedHash = (Get-Content $HashFile -First 1).Trim()
if (($hexHash -eq $savedHash)) {
    Write-Host ([string]'Constants MATCH seed' + ([string]([string]' (' + $constants.Count) + ' constants)'))
    exit 0
} else {
    Write-Host 'WARNING: Constants MISMATCH -- source constants differ from seed'
    Write-Host ([string]'  Source hash: ' + $hexHash)
    Write-Host ([string]'  Seed hash:   ' + $savedHash)
    Write-Host '  A seed rebuild is required before the changes take effect.'
    Write-Host '  Run: build/build.ps1'
    exit 1
}
