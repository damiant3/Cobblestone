# Extract matching turns from the fleet's session transcripts, one row each.
# The one formula for reading what was said and when, for TheLostParadise.md
# (root, 2026-09-09): a claim about who asked what, or who answered what,
# cites the row this prints (lane, session, timestamp), never a memory of it.
#
#   build/transcript-extract.ps1 -Pattern 'sitting|stick|metal' -Role user
#   build/transcript-extract.ps1 -Pattern 'e1000-reset' -Role assistant -Lane blu,fester
#   build/transcript-extract.ps1 -Pattern 'sitting' -Role user -Out sittings.csv -Window 600
#
# Reads every *.jsonl under both project directories a lane has had
# (D--Projects-Cobblestone-<lane> since 2026-08-25, D--Projects-NewRepository-<lane>
# before the rename), skips sidechain (subagent) records, and prints one row per
# non-sidechain user or assistant TEXT turn whose text matches -Pattern.
# `timestamp` is the box's LOCAL time (the time every doc and flight card
# uses); `utc` is the same instant in UTC.
# Tool calls and tool results are not turns and are not searched. A user row
# beginning "Base directory for this skill" is a skill load and is dropped
# unless -KeepSkills. Text is cut to -Window characters around the FIRST match
# (0 = whole text). Rows are sorted by timestamp across lanes.
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$Pattern,
    [ValidateSet('user', 'assistant', 'all')] [string]$Role = 'user',
    [string[]]$Lane = @('root', 'blu', 'fester', 'red', 'reek', 'val'),
    [int]$Window = 400,
    [string]$Out = '',
    [switch]$KeepSkills,
    # Damian's own turns only: drops every user-role record the harness, a
    # skill or a peer wrote (task notifications, cross-session messages,
    # compaction summaries, interrupts, images, the coordinator's typed fleet
    # messages, command echoes, system reminders, skill prompts). What
    # remains is what a person typed.
    [switch]$HumanOnly,
    [string]$ProjectsDir = (Join-Path $env:USERPROFILE '.claude\projects')
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$rx = [regex]::new($Pattern, 'IgnoreCase')
$rows = New-Object System.Collections.Generic.List[object]
foreach ($ln in $Lane) {
    foreach ($prefix in 'D--Projects-Cobblestone-', 'D--Projects-NewRepository-') {
        $dir = Join-Path $ProjectsDir ($prefix + $ln)
        if (-not (Test-Path $dir)) { continue }
        foreach ($f in Get-ChildItem $dir -Filter *.jsonl) {
            foreach ($l in [System.IO.File]::ReadLines($f.FullName)) {
                if ($l.Length -lt 20) { continue }
                if ($Role -eq 'user' -and $l -notmatch '"type":"user"') { continue }
                if ($Role -eq 'assistant' -and $l -notmatch '"type":"assistant"') { continue }
                if ($l -match '"isSidechain":true') { continue }
                try { $o = $l | ConvertFrom-Json } catch { continue }
                if ($o.type -ne 'user' -and $o.type -ne 'assistant') { continue }
                if (-not $o.message) { continue }
                $c = $o.message.content
                $t = if ($c -is [string]) { $c } else { (@($c) | Where-Object { $_.type -eq 'text' } | ForEach-Object { $_.text }) -join "`n" }
                if (-not $t) { continue }
                if ($o.type -eq 'user' -and -not $KeepSkills -and $t.StartsWith('Base directory for this skill')) { continue }
                if ($o.type -eq 'user' -and $HumanOnly) {
                    if ($t -match '^\s*<' -or $t -match '^\s*# /' -or $t -match '^(Another Claude session|This session is being continued|\[Request interrupted|Commander pulse|\[Image|\[fleet message|Caveat: The messages|Approach this as)' -or $t -match 'from-name=') { continue }
                }
                $m = $rx.Match($t)
                if (-not $m.Success) { continue }
                $snip = $t
                if ($Window -gt 0 -and $t.Length -gt $Window) {
                    $start = [Math]::Max(0, $m.Index - [int]($Window / 2))
                    $snip = $t.Substring($start, [Math]::Min($Window, $t.Length - $start))
                }
                $rows.Add([pscustomobject]@{
                    # ConvertFrom-Json hands the ISO 'Z' timestamp back as a
                    # DateTime whose VALUE is UTC and whose Kind is Unspecified;
                    # ToUniversalTime on it shifts a second time (measured
                    # 2026-09-09: 09:12 UTC printed as 16:12). Fix the kind.
                    timestamp = ([DateTime]::SpecifyKind([DateTime]$o.timestamp, 'Utc')).ToLocalTime().ToString('yyyy-MM-dd HH:mm:ss')
                    utc       = ([DateTime]::SpecifyKind([DateTime]$o.timestamp, 'Utc')).ToString('yyyy-MM-dd HH:mm:ss')
                    lane      = $ln
                    session   = $f.BaseName.Substring(0, 8)
                    role      = $o.type
                    text      = ($snip -replace '\s+', ' ').Trim()
                })
            }
        }
    }
}
$sorted = $rows | Sort-Object timestamp
if ($Out) { $sorted | Export-Csv -Path $Out -NoTypeInformation -Encoding UTF8; Write-Host "$($sorted.Count) rows -> $Out" }
else { $sorted | Format-Table -AutoSize -Wrap }
