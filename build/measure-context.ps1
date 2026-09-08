# Measure a lane's context from its transcript FILE. The only number the
# fleet uses for context: never a lane's estimate, never AgentGrid's display,
# never status.json's own field (Damian, 2026-09-08: after /compact the
# AgentGrid numbers are wrong; the files are the source).
#
#   build/measure-context.ps1                 # every lane, one row each
#   build/measure-context.ps1 -Lane val       # one lane
#   build/measure-context.ps1 -Lane val -Percent   # bare whole-number percent, for status.json
#
# The formula (AgentGrid's ContextService.cs): the newest transcript in the
# lane's project directory, the last non-sidechain record carrying
# message.usage, input_tokens + cache_creation_input_tokens +
# cache_read_input_tokens, against a 1,000,000 window. A /compact writes a
# system record with subtype compact_boundary; a usage record before that
# boundary describes the pre-compaction context, so the row says
# "compacted HH:MM, no turn since" until the lane's first turn after it.
# at-rest is True when the last stop_reason is not tool_use (the lane is at
# the prompt, not mid-turn).
[CmdletBinding()]
param(
    [string[]]$Lane = @('root', 'blu', 'fester', 'red', 'reek', 'val'),
    [switch]$Percent,
    [string]$ProjectsDir = (Join-Path $env:USERPROFILE '.claude\projects'),
    [string]$WorkspacePrefix = 'D--Projects-Cobblestone-'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Measure-Lane([string]$name) {
    $dir = Join-Path $ProjectsDir ($WorkspacePrefix + $name)
    $f = Get-ChildItem $dir -Filter *.jsonl -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    if (-not $f) { return [pscustomobject]@{ lane = $name; state = 'no transcript'; percent = $null; atRest = $null; lastWrite = $null; compacted = $null } }
    $u = $null; $rest = $null; $compactAt = $null; $usageAfterCompact = $true
    foreach ($l in [System.IO.File]::ReadLines($f.FullName)) {
        if ($l -match '"compact_boundary"') {
            try { $j = $l | ConvertFrom-Json; if ($j.subtype -eq 'compact_boundary') { $compactAt = $j.timestamp; $usageAfterCompact = $false } } catch {}
            continue
        }
        if ($l -match '"usage"') {
            try {
                $j = $l | ConvertFrom-Json
                if ($j.isSidechain -eq $true) { continue }
                if ($j.message.usage) { $u = $j.message.usage; $rest = $j.message.stop_reason; $usageAfterCompact = $true }
            } catch {}
        }
    }
    $compactLocal = if ($compactAt) { ([datetime]$compactAt).ToLocalTime().ToString('HH:mm') } else { $null }
    if (-not $usageAfterCompact) {
        return [pscustomobject]@{ lane = $name; state = "compacted $compactLocal, no turn since"; percent = $null; atRest = $null; lastWrite = $f.LastWriteTime; compacted = $compactLocal }
    }
    if (-not $u) { return [pscustomobject]@{ lane = $name; state = 'no usage record'; percent = $null; atRest = $null; lastWrite = $f.LastWriteTime; compacted = $compactLocal } }
    $used = [int64]$u.input_tokens + [int64]$u.cache_creation_input_tokens + [int64]$u.cache_read_input_tokens
    [pscustomobject]@{
        lane = $name; state = 'measured'; percent = [math]::Round(100 * $used / 1000000, 1)
        atRest = ($rest -ne 'tool_use'); lastWrite = $f.LastWriteTime; compacted = $compactLocal
    }
}

$rows = @($Lane | ForEach-Object { Measure-Lane $_ })
if ($Percent) {
    if ($rows.Count -ne 1) { throw '-Percent wants exactly one -Lane' }
    if ($null -eq $rows[0].percent) { throw "$($rows[0].lane): $($rows[0].state)" }
    [int][math]::Round($rows[0].percent)
    return
}
foreach ($r in $rows) {
    if ($r.state -ne 'measured') { '{0,-7} {1}' -f $r.lane, $r.state; continue }
    '{0,-7} {1,5:N1}%  at-rest {2,-5}  {3}  compact={4}' -f $r.lane, $r.percent, $r.atRest, $r.lastWrite.ToString('HH:mm'), ($(if ($r.compacted) { $r.compacted } else { 'none' }))
}
