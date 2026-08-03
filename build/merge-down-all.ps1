# merge-down-all.ps1 -- merge main into every agent's dev stream, in one command.
#
# For each development stream under //Codex/main: sync the agent's workspace,
# merge down, resolve ONLY what resolves automatically, and submit if nothing
# is left in doubt. A stream with a real conflict is left with its changelist
# open and named in the summary, for that agent or a human to finish. Each
# agent's `_main` client is synced too.
#
# WHAT IT WILL NOT TOUCH, and why. A workspace with files already open is
# skipped outright: those files are somebody's work in flight, and on-disk
# files are the source of truth for a build, so syncing under a running gate
# would contaminate it silently. A workspace with a shelved changelist is
# skipped for the same reason -- a shelf is the normal state while an agent
# holds the build token, and the token protocol has them merge down themselves
# at that point. -Force overrides the shelf check only; nothing overrides the
# open-files check.
#
# This command submits, across every agent's workspace, and the AgentGrid build
# token does not cover it. So it requires -ApprovedBy damian and refuses
# without it, exactly as build/test.ps1 does.
#
# The gate is not there to stop him asking an agent to run it. In his words:
# when he asks for a command it is as if he popped a terminal himself. What it
# stops is this BECOMING A TOOL AN AGENT REACHES FOR without him asking -- and
# a refusal printed on the console cannot be quietly buried, where a sentence
# in this header can. That sentence is what used to be here, and it was a claim
# with nothing re-reading it, which is the failure mode this repository has
# documented everywhere else.
#
# -DryRun needs no approval. It submits nothing and opens nothing, so an agent
# can answer "which streams have work" without spending his attention.
#
# It only ever writes to dev streams -- it never copies up and never touches
# //Codex/main -- so it cannot race a copy-up.
#
#   pwsh build/merge-down-all.ps1 -ApprovedBy damian       # all agents
#   pwsh build/merge-down-all.ps1 -DryRun                  # no approval needed
#   pwsh build/merge-down-all.ps1 -Agents blu,val -ApprovedBy damian
#   pwsh build/merge-down-all.ps1 -Force -ApprovedBy damian  # ignore shelf skips
#
# -DryRun's file counts are APPROXIMATE and can differ from the real run in
# both directions, because a dry run deliberately does not sync: `p4 merge -n`
# answers against the workspace as it stands. One stream reported "already
# integrated" dry and merged three files wet, and another the reverse. Read the
# dry run as "which streams have work", not as a count.
#
# Exit 0 if every stream ended clean (merged, or already current, or safely
# skipped). Exit 1 if any stream needs intervention.

[CmdletBinding()]
param(
    [string[]]$Agents = @(),
    [switch]$DryRun,
    [switch]$Force,
    [string]$Port = 'localhost:1666',
    [string]$User = 'damian',
    [string]$Mainline = '//Codex/main',
    [string]$ApprovedBy
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $DryRun -and $ApprovedBy -ne 'damian') {
    Write-Host ''
    Write-Host 'REFUSED: the fleet merge-down runs only with explicit human approval.' -ForegroundColor Red
    Write-Host ''
    Write-Host 'This submits a changelist into every agent workspace on the box. The'
    Write-Host 'AgentGrid build token does not cover it, so nothing else serialises it.'
    Write-Host ''
    Write-Host 'Agent: passing the approval flag without Damian asking for it in THIS'
    Write-Host 'session is a lie. If you want to know which streams have work, that'
    Write-Host 'question is free and needs no approval:'
    Write-Host ''
    Write-Host '    pwsh build/merge-down-all.ps1 -DryRun'
    Write-Host ''
    Write-Host 'To merge only your own stream, do it yourself under the build token:'
    Write-Host ''
    Write-Host '    p4 merge -S //Codex/<you> -r'
    Write-Host '    p4 resolve -am'
    Write-Host ''
    exit 2
}

# Stamped into every changelist this script creates, so a later run can tell its
# own unfinished work from an agent's.
$MergeTag = 'merge-down-all'

# Resolved once, and called through the variable everywhere below. PowerShell
# command lookup is case-insensitive, so a helper named `P4` shadows `p4.exe`
# and every argument then binds against the helper's parameters instead --
# which fails as "the parameter name 'p' is ambiguous", naming no p4 command
# at all. Hence both the name and the explicit path.
$P4Exe = @(Get-Command p4.exe -CommandType Application -ErrorAction SilentlyContinue)[0]
if (-not $P4Exe) { throw 'p4.exe not found on PATH' }
$P4Exe = $P4Exe.Source

function Invoke-P4 {
    # Every call names its client and its working directory explicitly. A bare
    # p4 here would pick up whatever .p4config the current directory happens to
    # sit under, which is another agent's workspace as often as not.
    #
    # The p4 arguments arrive as ONE array rather than as remaining arguments,
    # because p4's flags collide with any parameter this function could have:
    # `-r` bound to -Root, `-c` would bind to -Client. A caller passes
    # @('merge', '-n', '-S', $stream, '-r') and nothing is interpreted here.
    param([string]$Client, [string]$Root, [string[]]$Cmd)
    $base = @('-p', $Port, '-u', $User, '-c', $Client)
    if ($Root) { $base += @('-d', $Root) }
    return @(& $script:P4Exe @base @Cmd 2>&1 | ForEach-Object { "$_" })
}

Write-Host "merge-down-all: mainline $Mainline"

# ---------------------------------------------------------------------------
# Discover the fleet rather than hardcoding it. A new agent stream should be
# picked up by adding the stream and the client, not by editing this script.
# Task streams are excluded: their parent is a dev stream, not the mainline.
# ---------------------------------------------------------------------------
$streams = @(@(& $P4Exe -p $Port -u $User streams 2>&1 | ForEach-Object { "$_" }) |
    ForEach-Object {
        if ($_ -match '^Stream (?<s>\S+) development ' + [regex]::Escape($Mainline) + ' ') { $matches['s'] }
    })

$clientLines = @(& $P4Exe -p $Port -u $User clients 2>&1 | ForEach-Object { "$_" })
$clients = @{}
foreach ($l in $clientLines) {
    if ($l -match "^Client (?<c>\S+) \S+ root (?<r>.+?) '") { $clients[$matches['c']] = $matches['r'].Trim() }
    elseif ($l -match '^Client (?<c>\S+) \S+ root (?<r>.+)$') { $clients[$matches['c']] = $matches['r'].Trim() }
}

# Map stream -> the client that is ON that stream. Read from the client spec,
# because the name is a convention and the spec is the fact.
$onStream = @{}
foreach ($c in $clients.Keys) {
    $spec = @(& $P4Exe -p $Port -u $User client -o $c 2>&1 | ForEach-Object { "$_" })
    $s = ($spec | Where-Object { $_ -match '^Stream:\s*(\S+)' } | ForEach-Object { $matches[1] } | Select-Object -First 1)
    if ($s) { if (-not $onStream.ContainsKey($s)) { $onStream[$s] = @() }; $onStream[$s] += $c }
}

$work = @()
foreach ($s in ($streams | Sort-Object)) {
    $agent = $s.Substring($s.LastIndexOf('/') + 1)
    if ($Agents.Count -gt 0 -and $Agents -notcontains $agent) { continue }
    $dev = @($onStream[$s]) | Select-Object -First 1
    if (-not $dev) { Write-Host "  $agent : no client on $s -- skipped"; continue }
    $mainCandidates = @($onStream[$Mainline]) | Where-Object { $_ -like "*_$agent`_main" -or $_ -eq "$($dev)_main" }
    $work += [pscustomobject]@{
        Agent = $agent; Stream = $s
        Dev = $dev; DevRoot = $clients[$dev]
        Main = (@($mainCandidates) | Select-Object -First 1)
    }
}

if ($work.Count -eq 0) { Write-Host 'merge-down-all: nothing to do'; exit 0 }
Write-Host "merge-down-all: $($work.Count) stream(s): $(($work | ForEach-Object { $_.Agent }) -join ', ')"
if ($DryRun) { Write-Host 'merge-down-all: DRY RUN, nothing will be submitted' }
Write-Host ''

$results = @()

foreach ($w in $work) {
    Write-Host "=== $($w.Agent)  ($($w.Stream) via $($w.Dev))"
    $status = ''; $detail = ''

    if (-not (Test-Path $w.DevRoot)) {
        $results += [pscustomobject]@{ Agent = $w.Agent; Status = 'SKIPPED'; Detail = "workspace root missing: $($w.DevRoot)" }
        Write-Host "  workspace root missing: $($w.DevRoot)"
        Write-Host ''
        continue
    }

    # --- refuse to touch work in flight ------------------------------------
    $opened = Invoke-P4 $w.Dev $w.DevRoot @('opened')
    if ($opened -notmatch 'not opened on this client') {
        $n = @($opened | Where-Object { $_ -match ' - (edit|add|delete|integrate|branch|move)' }).Count

        # Is this an agent working, or is it OUR OWN conflict changelist from a
        # previous run, still waiting for a decision nobody made? Without this
        # the second case reports a benign SKIPPED and exits 0 forever, and a
        # stuck stream silently looks fine -- the same decay this fleet keeps
        # finding in skip reasons and stale register entries.
        #
        # The test is STRUCTURAL, not textual. Matching the description alone
        # was wrong the first time it ran: the changelist that ADDED this script
        # mentions its own name, so it matched and a healthy stream reported
        # STUCK. A merge-down changelist consists only of integration records --
        # integrate, branch, delete, or the move/add + move/delete pair a rename
        # on the mainline produces -- so a bare `add` or `edit` means a person
        # put it there. Both conditions must hold.
        #
        # `move/add` and `move/delete` belong on the merge side of that line and
        # were on the wrong side once: red's merge carried 25 of each from
        # renames upstream, which read as hand-written and silenced the check
        # for the one stream it existed to catch.
        $mine = @()
        foreach ($p in @($opened | ForEach-Object { if ($_ -match ' change (?<c>\d+) ') { $matches['c'] } } | Sort-Object -Unique)) {
            # Joined to ONE string first. `-match` and `-notmatch` against an
            # ARRAY are filters, not tests: they return the matching (or
            # non-matching) elements, and a non-empty result is truthy. So
            # `if ($lines -notmatch 'x')` is true whenever ANY line lacks x,
            # which is nearly always, and this test silently never fired.
            $d = (Invoke-P4 $w.Dev $w.DevRoot @('describe','-s',$p)) -join "`n"
            if ($d -notmatch [regex]::Escape($MergeTag)) { continue }
            $handWritten = @($opened | Where-Object { $_ -match " change $p " -and $_ -match ' - (add|edit) ' })
            if ($handWritten.Count -eq 0) { $mine += $p }
        }
        if ($mine.Count -gt 0) {
            $results += [pscustomobject]@{ Agent = $w.Agent; Status = 'STUCK'; Detail = "CL $($mine -join ', ') from an earlier run is still unresolved ($n file(s))" }
            Write-Host "  STUCK: CL $($mine -join ', ') is an earlier merge-down-all changelist, still unresolved."
            Write-Host "  Finish or revert it before this stream can move."
            Write-Host ''
            continue
        }

        $results += [pscustomobject]@{ Agent = $w.Agent; Status = 'SKIPPED'; Detail = "$n file(s) already open -- work in flight" }
        Write-Host "  $n file(s) already open. Not touching a workspace someone is using."
        Write-Host ''
        continue
    }

    $shelved = @(Invoke-P4 $w.Dev $w.DevRoot @('changes','-s','shelved','-c',$w.Dev) | Where-Object { $_ -match '^Change (\d+)' })
    if ($shelved.Count -gt 0 -and -not $Force) {
        $cls = ($shelved | ForEach-Object { if ($_ -match '^Change (\d+)') { $matches[1] } }) -join ', '
        $results += [pscustomobject]@{ Agent = $w.Agent; Status = 'SKIPPED'; Detail = "shelved CL $cls -- agent is mid-flight; -Force to override" }
        Write-Host "  shelved CL $cls. An agent holding the build token merges down itself; skipping."
        Write-Host ''
        continue
    }

    # --- sync, then see whether there is anything to merge -----------------
    if (-not $DryRun) { Invoke-P4 $w.Dev $w.DevRoot @('sync','-q') | Out-Null }

    $preview = Invoke-P4 $w.Dev $w.DevRoot @('merge','-n','-S',$w.Stream,'-r')
    if ($preview -match 'All revision\(s\) already integrated') {
        $results += [pscustomobject]@{ Agent = $w.Agent; Status = 'CURRENT'; Detail = 'already integrated' }
        Write-Host '  already integrated'
        Write-Host ''
        continue
    }
    $wouldOpen = @($preview | Where-Object { $_ -match '^//' }).Count
    Write-Host "  $wouldOpen file(s) to integrate"

    if ($DryRun) {
        $results += [pscustomobject]@{ Agent = $w.Agent; Status = 'WOULD MERGE'; Detail = "$wouldOpen file(s)" }
        Write-Host ''
        continue
    }

    # --- merge into a numbered CL so a conflict leaves something named -----
    $descr = "Merge down from $Mainline ($MergeTag)"
    $spec = @("Change: new", "Client: $($w.Dev)", "User: $User", "Status: new", "Description:", "`t$descr") -join "`n"
    $created = @($spec | & $P4Exe -p $Port -u $User -c $w.Dev -d $w.DevRoot change -i 2>&1 | ForEach-Object { "$_" })
    $cl = ($created | ForEach-Object { if ($_ -match 'Change (\d+) created') { $matches[1] } } | Select-Object -First 1)
    if (-not $cl) {
        $results += [pscustomobject]@{ Agent = $w.Agent; Status = 'ERROR'; Detail = "could not create a changelist: $($created -join ' ')" }
        Write-Host "  could not create a changelist"
        Write-Host ''
        continue
    }

    Invoke-P4 $w.Dev $w.DevRoot @('merge','-c',$cl,'-S',$w.Stream,'-r') | Out-Null

    # -am is the easy resolve: it accepts a merged result only where the two
    # sides do not overlap, and leaves a genuinely conflicting file alone.
    Invoke-P4 $w.Dev $w.DevRoot @('resolve','-am','-c',$cl) | Out-Null

    # `p4 resolve -n` reports the LOCAL path, not the depot path, and emits one
    # line per outstanding resolve -- so a single file with both a filetype and
    # a content resolve appears twice. Group by file and keep the kinds, since
    # they are fixed differently: a filetype resolve is `-at`/`-ay` on the
    # type, a content one needs somebody to read the merge.
    $left = Invoke-P4 $w.Dev $w.DevRoot @('resolve','-n','-c',$cl)
    $stuck = @{}
    foreach ($l in $left) {
        if ($l -match '^(?<f>.+?) - (?<kind>resolving \w+|merging|resolve) ') {
            $f = $matches['f']; $k = $matches['kind'] -replace '^resolving ', ''
            if (-not $stuck.ContainsKey($f)) { $stuck[$f] = @() }
            if ($stuck[$f] -notcontains $k) { $stuck[$f] += $k }
        }
    }
    if ($stuck.Count -gt 0) {
        $results += [pscustomobject]@{ Agent = $w.Agent; Status = 'CONFLICT'; Detail = "CL $cl, $($stuck.Count) file(s) need a decision" }
        Write-Host "  CONFLICT: CL $cl left open, $($stuck.Count) file(s) need a decision:"
        foreach ($f in ($stuck.Keys | Sort-Object | Select-Object -First 12)) {
            Write-Host "    $f  [$(($stuck[$f]) -join ', ')]"
        }
        if ($stuck.Count -gt 12) { Write-Host "    ... and $($stuck.Count - 12) more" }
        Write-Host ''
        continue
    }

    $stillOpen = @(Invoke-P4 $w.Dev $w.DevRoot @('opened','-c',$cl) | Where-Object { $_ -match '^//' })
    if ($stillOpen.Count -eq 0) {
        Invoke-P4 $w.Dev $w.DevRoot @('change','-d',$cl) | Out-Null
        $results += [pscustomobject]@{ Agent = $w.Agent; Status = 'CURRENT'; Detail = 'nothing opened after merge' }
        Write-Host '  nothing to submit'
        Write-Host ''
        continue
    }

    $sub = Invoke-P4 $w.Dev $w.DevRoot @('submit','-c',$cl)
    $submitted = ($sub | ForEach-Object { if ($_ -match 'Change (\d+) submitted') { $matches[1] } elseif ($_ -match 'renamed change (\d+) and submitted') { $matches[1] } } | Select-Object -First 1)
    if ($submitted) {
        $results += [pscustomobject]@{ Agent = $w.Agent; Status = 'MERGED'; Detail = "CL $submitted, $($stillOpen.Count) file(s)" }
        Write-Host "  submitted as CL $submitted ($($stillOpen.Count) files)"
    } else {
        $results += [pscustomobject]@{ Agent = $w.Agent; Status = 'ERROR'; Detail = "submit failed, CL $cl left open: $(($sub | Select-Object -Last 2) -join ' ')" }
        Write-Host "  submit FAILED, CL $cl left open"
        $sub | Select-Object -Last 4 | ForEach-Object { Write-Host "    $_" }
    }
    Write-Host ''
}

# ---------------------------------------------------------------------------
# The -main clients. Sync only: these are copy-up/proofing workspaces and
# nothing here should ever write to the mainline.
# ---------------------------------------------------------------------------
Write-Host '=== syncing the -main clients'
$mainResults = @()
foreach ($w in $work) {
    if (-not $w.Main) { Write-Host "  $($w.Agent): no _main client"; continue }
    $root = $clients[$w.Main]
    if (-not $root -or -not (Test-Path $root)) { Write-Host "  $($w.Agent): $($w.Main) root missing"; continue }
    $op = Invoke-P4 $w.Main $root @('opened')
    if ($op -notmatch 'not opened on this client') {
        Write-Host "  $($w.Agent): $($w.Main) has files open -- skipped"
        $mainResults += "$($w.Agent):skipped"
        continue
    }
    if ($DryRun) { Write-Host "  $($w.Agent): would sync $($w.Main)"; continue }
    $out = Invoke-P4 $w.Main $root @('sync')
    if ($out -match 'up-to-date') {
        Write-Host "  $($w.Agent): $($w.Main) up to date"
    } else {
        $n = @($out | Where-Object { $_ -match '^//' }).Count
        Write-Host "  $($w.Agent): $($w.Main) synced $n file(s)"
    }
    $mainResults += "$($w.Agent):ok"
}

Write-Host ''
Write-Host '-- Summary ----------------------------------------'
foreach ($r in $results) { Write-Host ("  {0,-10} {1,-12} {2}" -f $r.Agent, $r.Status, $r.Detail) }

$needsHelp = @($results | Where-Object { $_.Status -in @('CONFLICT','ERROR','STUCK') })
if ($needsHelp.Count -gt 0) {
    Write-Host ''
    Write-Host "$($needsHelp.Count) stream(s) need an agent or a human:" -ForegroundColor Yellow
    foreach ($r in $needsHelp) { Write-Host "  $($r.Agent): $($r.Detail)" -ForegroundColor Yellow }
    Write-Host ''
    Write-Host 'Finish one with:  p4 -c <client> resolve        (interactive)'
    Write-Host '           then:  p4 -c <client> submit -c <CL>'
    exit 1
}

Write-Host ''
Write-Host 'merge-down-all: all streams clean'
exit 0
