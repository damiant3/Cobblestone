# check-dashes.ps1 -- CLAUDE.md rule 11 instrument.
#
# Reports (and with -Fix, removes) em-dashes and en-dashes in depot-tracked
# files. On-demand only: this is NOT wired into build.ps1 or the battery.
#
#   build/check-dashes.ps1                    # report everything in scope
#   build/check-dashes.ps1 -Path docs         # report one subtree
#   build/check-dashes.ps1 -Path docs -Fix    # rewrite that subtree
#
# Rule 11 bans the em-dash outright and the en-dash outside a numeric range,
# so a digit-en-dash-digit span is left alone and counted separately.
#
# .codex files are handled by a different rule from every other file type.
# An em-dash in column-2 prose or a comment is text nobody executes; an
# em-dash inside a double-quoted string literal is program OUTPUT, and
# rewriting it changes what an application prints and can break a paired
# .expected sidecar. -Fix therefore skips string literals in .codex and
# reports them for per-site judgement instead.

[CmdletBinding()]
param(
  [string]   $Path,
  [switch]   $Fix,
  [switch]   $IncludeStringLiterals,
  [string[]] $Exclude = @(
    'old\',                        # retired reference compiler: do not edit
    '.psv',                        # Magic card data: external, not ours
    'docs\PM\Stories\Vision\',     # the founding text, quoted verbatim
    'build\output'                 # generated; the gate's clean phase deletes it
  )
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Push-Location $repo
try {
  $DashEm = [char]0x2014
  $DashEn = [char]0x2013

  # p4 have prints "<depot>#<rev> - <local>", and the local half comes back with
  # mixed separators (D:/Projects/...\docs\x.md), so it has to be normalised
  # before any prefix or wildcard test will match.
  $prefix = (Get-Location).Path.TrimEnd('\')
  $tracked = @()
  & p4 have //Codex/... 2>$null | ForEach-Object {
    $i = $_.IndexOf(' - ')
    if ($i -le 0) { return }
    $local = $_.Substring($i + 3).Trim().Replace('/', '\')
    if ($local.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
      $tracked += $local.Substring($prefix.Length).TrimStart('\')
    }
  }
  if ($tracked.Count -eq 0) { throw "p4 have returned no file under $prefix; is .p4config present?" }

  # Binary is decided by content, not by extension. A .glb, a .wasm and a .disk2
  # all decode as UTF-8 into replacement characters and invent dash counts; an
  # extension list to exclude them is a list that is always one format short.
  function Test-Binary([byte[]]$bytes) {
    $n = [Math]::Min($bytes.Length, 8192)
    for ($i = 0; $i -lt $n; $i++) { if ($bytes[$i] -eq 0) { return $true } }
    return $false
  }

  $files = $tracked | Where-Object {
    $rel = $_
    if ($Path -and -not ($rel -like "$Path*")) { return $false }
    foreach ($x in $Exclude) { if ($rel.ToLower().Contains($x.ToLower())) { return $false } }
    Test-Path -LiteralPath $rel
  }

  # A file's encoding must survive the rewrite. Detect the BOM, decode, and
  # re-encode the same way; a pure string replace leaves line endings alone.
  function Read-Doc([string]$p) {
    $bytes = [IO.File]::ReadAllBytes($p)
    if (Test-Binary $bytes) { return $null }
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
      return @{ Text = [Text.Encoding]::UTF8.GetString($bytes, 3, $bytes.Length - 3); Enc = 'utf8bom' }
    }
    if ($bytes.Length -ge 2 -and $bytes[0] -eq 0xFF -and $bytes[1] -eq 0xFE) {
      return @{ Text = [Text.Encoding]::Unicode.GetString($bytes, 2, $bytes.Length - 2); Enc = 'utf16le' }
    }
    return @{ Text = [Text.Encoding]::UTF8.GetString($bytes); Enc = 'utf8' }
  }

  function Write-Doc([string]$p, [string]$text, [string]$enc) {
    switch ($enc) {
      'utf8bom' { [IO.File]::WriteAllBytes($p, ([byte[]](0xEF,0xBB,0xBF)) + [Text.Encoding]::UTF8.GetBytes($text)) }
      'utf16le' { [IO.File]::WriteAllBytes($p, ([byte[]](0xFF,0xFE)) + [Text.Encoding]::Unicode.GetBytes($text)) }
      default   { [IO.File]::WriteAllBytes($p, [Text.Encoding]::UTF8.GetBytes($text)) }
    }
  }

  # True when the character at $idx sits inside a double-quoted string, judged
  # by the parity of the quotes to its left on the same line.
  function In-StringLiteral([string]$line, [int]$idx) {
    return (([regex]::Matches($line.Substring(0, $idx), '"')).Count % 2) -eq 1
  }

  $report   = @()
  $totEm    = 0
  $totEn    = 0
  $totRange = 0
  $totLit   = 0
  $changed  = 0

  foreach ($rel in $files) {
    $doc = Read-Doc $rel
    if ($null -eq $doc) { continue }
    if ($doc.Text.IndexOf($DashEm) -lt 0 -and $doc.Text.IndexOf($DashEn) -lt 0) { continue }

    $isCodex = [IO.Path]::GetExtension($rel).ToLower() -eq '.codex'
    $lines   = $doc.Text -split "`n", 0, 'SimpleMatch'
    # These counters must NOT be named $em/$en: PowerShell variable names are
    # case-insensitive, so $em and $DashEm-style constants collide silently and
    # the scan then searches for whatever the counter was last set to.
    $cntEm = 0; $cntEn = 0; $range = 0; $lit = 0; $touched = $false

    for ($i = 0; $i -lt $lines.Count; $i++) {
      $L = $lines[$i]
      if ($L.IndexOf($DashEm) -lt 0 -and $L.IndexOf($DashEn) -lt 0) { continue }
      $sb = [Text.StringBuilder]::new()
      for ($j = 0; $j -lt $L.Length; $j++) {
        $c = $L[$j]
        if ($c -eq $DashEm) {
          $cntEm++
          if ($isCodex -and -not $IncludeStringLiterals -and (In-StringLiteral $L $j)) {
            $lit++; [void]$sb.Append($c); continue
          }
          [void]$sb.Append('--'); $touched = $true; continue
        }
        if ($c -eq $DashEn) {
          $cntEn++
          $prev = if ($j -gt 0) { $L[$j-1] } else { ' ' }
          $next = if ($j -lt $L.Length - 1) { $L[$j+1] } else { ' ' }
          if ([char]::IsDigit($prev) -and [char]::IsDigit($next)) {
            $range++; [void]$sb.Append($c); continue     # rule 11 permits a numeric range
          }
          if ($isCodex -and -not $IncludeStringLiterals -and (In-StringLiteral $L $j)) {
            $lit++; [void]$sb.Append($c); continue
          }
          [void]$sb.Append('-'); $touched = $true; continue
        }
        [void]$sb.Append($c)
      }
      $lines[$i] = $sb.ToString()
    }

    $totEm += $cntEm; $totEn += $cntEn; $totRange += $range; $totLit += $lit
    $report += [pscustomobject]@{
      File = $rel; Em = $cntEm; En = $cntEn; NumRange = $range; InLiteral = $lit
    }

    if ($Fix -and $touched) {
      Write-Doc $rel ($lines -join "`n") $doc.Enc
      $changed++
    }
  }

  $report | Sort-Object { -($_.Em + $_.En) } | Format-Table -AutoSize | Out-String -Width 200 | Write-Host

  Write-Host ""
  Write-Host ("files with a banned dash : {0}" -f $report.Count)
  Write-Host ("em-dashes                : {0}" -f $totEm)
  Write-Host ("en-dashes                : {0}  ({1} in a numeric range, permitted and left alone)" -f $totEn, $totRange)
  if ($totLit -gt 0) {
    Write-Host ("in .codex string literals: {0}  (program output; -Fix skips these, pass -IncludeStringLiterals to take them)" -f $totLit)
  }
  if ($Fix) {
    Write-Host ("files rewritten           : {0}" -f $changed)
  }

  $remaining = $totEm - $totLit + $totEn - $totRange
  if ($Fix) { exit 0 }
  if ($remaining -gt 0) { exit 1 }
  exit 0
}
finally { Pop-Location }
