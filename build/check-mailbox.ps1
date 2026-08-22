# Report fleet messages of yours that never arrived.
#
# THREE WAYS A MESSAGE FAILS SILENTLY, all found on 2026-08-20 within an hour
# of each other, and every one of them looks exactly like success from the
# sender's side: no error, no bounce, the file goes somewhere plausible.
#
#   1. Written into ANOTHER agent's inbox/ instead of your own outbox/.
#      AgentGrid never sees it, so it is never delivered. blu lost FOUR
#      messages this way in one morning, including a finished diagnostic
#      stage and a closed ruling, and red spent the morning reporting that
#      lane as silent.
#   2. Left in outbox/failed/. Delivery was attempted and refused, and the
#      file sits there. root's sat for two days.
#   3. Wrong extension. val's .TXT was never picked up at all.
#
# CoordinationProtocol has documented (1) and (2) in prose the whole time and
# both happened anyway. That is the failure this project already has a name
# for: an assertion with no runner. This script is the runner. Run it at init
# and after any send you care about.
#
# WHAT IT DOES NOT DO, and this matters more than what it does: it does NOT
# report your inbox/ as lost mail. Inbox root is DELIVERED mail that you have
# already read (val, 2026-08-20, correcting a fleet broadcast of red's that
# would have had people reporting a hundred phantom losses). A file in your
# own inbox is normal. Only a file somebody hand-wrote into SOMEONE ELSE'S
# inbox is a loss, and that is invisible from the receiving side -- which is
# why the only reliable check is sender-side, on your own outbox.
#
# Exit 1 if anything is stranded, 0 if clean.
[CmdletBinding()]
param(
    [string]$AgentGridFile = '',
    [switch]$Quiet
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$cfgPath = if ($AgentGridFile) { $AgentGridFile } else { Join-Path $repo '.agentgrid' }
if (-not (Test-Path -PathType Leaf $cfgPath)) {
    Write-Host "check-mailbox: no .agentgrid in $repo -- AgentGrid is not managing this workspace, nothing to check."
    exit 0
}
$cfg = Get-Content $cfgPath -Raw | ConvertFrom-Json
$agent = $cfg.agent
$outbox = $cfg.outbox
if (-not (Test-Path -PathType Container $outbox)) {
    Write-Host "check-mailbox: outbox $outbox does not exist."
    exit 1
}

$bad = 0

# 1. Still sitting in outbox root. AgentGrid moves a delivered file to sent/
#    within a few seconds, so anything lingering here was never picked up.
#    A file written in the last few seconds is in flight, not stranded.
$now = Get-Date
$stuck = @(Get-ChildItem $outbox -File -ErrorAction SilentlyContinue |
    Where-Object { ($now - $_.LastWriteTime).TotalSeconds -gt 30 })
if ($stuck.Count -gt 0) {
    $bad += $stuck.Count
    Write-Host "STRANDED in outbox root ($($stuck.Count)) -- written but never picked up:"
    foreach ($f in $stuck) { Write-Host ("  {0}  {1}" -f $f.LastWriteTime.ToString('MM-dd HH:mm'), $f.Name) }
    Write-Host "  AgentGrid is not running, or was not when these were written. Re-send them."
}

# 2. Delivery attempted and refused.
$failedDir = Join-Path $outbox 'failed'
$failed = @(Get-ChildItem $failedDir -File -ErrorAction SilentlyContinue)
if ($failed.Count -gt 0) {
    $bad += $failed.Count
    Write-Host "FAILED delivery ($($failed.Count)) -- these did NOT reach a mailbox:"
    foreach ($f in $failed) { Write-Host ("  {0}  {1}" -f $f.LastWriteTime.ToString('MM-dd HH:mm'), $f.Name) }
    Write-Host "  Read each one, fix what it says, and re-send. Nobody received these."
}

# 3. Wrong extension anywhere in the outbox tree: never picked up, and it
#    looks filed rather than failed.
$wrong = @(Get-ChildItem $outbox -File -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.Extension -ne '.json' })
if ($wrong.Count -gt 0) {
    $bad += $wrong.Count
    Write-Host "WRONG EXTENSION ($($wrong.Count)) -- not .json, so never picked up:"
    foreach ($f in $wrong) { Write-Host ("  {0}  {1}" -f $f.LastWriteTime.ToString('MM-dd HH:mm'), $f.FullName.Replace($outbox, '')) }
}

if ($bad -gt 0) {
    Write-Host ""
    Write-Host "check-mailbox ($agent): $bad message(s) never arrived."
    Write-Host "If any of them carried a durable fact, put the fact in the doc that owns"
    Write-Host "the subject rather than only re-sending it -- a message is a pointer and"
    Write-Host "the doc is the record."
    exit 1
}

if (-not $Quiet) {
    $sent = @(Get-ChildItem (Join-Path $outbox 'sent') -File -ErrorAction SilentlyContinue).Count
    Write-Host "check-mailbox ($agent): OK -- outbox clean, $sent delivered."
    Write-Host "Your own inbox/ is DELIVERED mail and is not checked here; files in it are normal."
}
exit 0
