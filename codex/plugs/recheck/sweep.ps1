# Run the rechecker over many chapters and report the disagreement set.
#
# This is section 9's promotion evidence: the design admits the rechecker to
# build.ps1 only after it has run clean over the whole tree with zero false
# positives across two seed generations. Nothing can say that until the tree
# has actually been swept, and until this script existed it never had been.
#
# EXPECT DISAGREEMENTS TO BE THE RECHECKER'S OWN. Four of its bugs have already
# been found this way and every one was caught by a control rather than by
# reading the code. The failure to guard against is the opposite one: a
# rechecker quietly tuned until it agrees reintroduces the correlation it
# exists to break. So any relaxation made in response to this sweep re-runs
# kill-rate.ps1 in the same change -- a fix that reduces disagreements is
# exactly the kind that can silently destroy sensitivity.
#
# PORT 9100 IS SHARED BY THE WHOLE FLEET. Every plug's run script binds it on
# loopback, $env:TEMP is per-user, and four agents run from separate
# workspaces on one box. A guest reaches the host at 10.0.2.2 through its own
# NAT, so another agent's plug VM can connect to whichever listener owns the
# port. This script refuses to start when the port is already held rather than
# reporting someone else's plug as its own result (L-SHARED). It cannot close
# the narrower window where another agent starts a plug run mid-sweep, and
# says so rather than implying otherwise.
#
#   pwsh codex/plugs/recheck/sweep.ps1                      # codex/test, capped
#   pwsh codex/plugs/recheck/sweep.ps1 -Dir codex/foreword/core -Limit 40
#   pwsh codex/plugs/recheck/sweep.ps1 -Limit 0             # no cap
[CmdletBinding()]
param(
    [string]$Dir = 'codex/test',
    [int]$Limit = 25,
    [string]$Kernel = '',
    [string]$Passes = 'none',
    [string]$Report = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '..' '..' '..' 'build' 'vm-config.ps1')

$Repo    = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..')).Path
$PlugDir = (Resolve-Path $PSScriptRoot).Path
$PlugCdx = Join-Path $PlugDir 'build-output\recheck-plug.cdx'
$WorkDir = Join-Path $PlugDir 'build-output\sweep'
if (-not $Kernel) { $Kernel = Join-Path $Repo 'seed\Codex.cdx' }
if (-not $Report) { $Report = Join-Path $WorkDir 'sweep.log' }

if (-not (Test-Path -PathType Leaf $PlugCdx)) {
    [Console]::Error.WriteLine("MISSING: $PlugCdx -- run codex/plugs/recheck/build.ps1 first")
    exit 2
}
New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null

# Refuse a held port rather than answer with another agent's run.
try {
    $probe = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 9100)
    $probe.Start(); $probe.Stop()
} catch {
    [Console]::Error.WriteLine("REFUSING: TCP 9100 is already held on this box.")
    [Console]::Error.WriteLine("  Another agent's plug run owns it. Results would be theirs, not yours.")
    exit 3
}

$files = Get-ChildItem (Join-Path $Repo $Dir) -Filter '*.codex' -File |
         Sort-Object Name
if ($Limit -gt 0) { $files = $files | Select-Object -First $Limit }
if (-not $files) { [Console]::Error.WriteLine("no .codex files under $Dir"); exit 1 }

function Invoke-Plug([string]$IrPath) {
    $stderrFile = [System.IO.Path]::GetTempFileName()
    $proc = $null
    try {
        $proc = Start-Process -FilePath $script:CodexVmBin `
            -ArgumentList @('-kernel', $PlugCdx, '-mem', '3072', '-headless') `
            -PassThru -WindowStyle Hidden -RedirectStandardError $stderrFile
        # Binding the SAME port once per chapter in ONE process is what the
        # single-shot scripts never do, so only the sweep meets TIME_WAIT: the
        # previous chapter's socket still holds the address for a moment and
        # Start() throws "only one usage of each socket address". The first
        # full sweep died on it 400 chapters in.
        #
        # This retries rather than setting SO_REUSEADDR, deliberately. On
        # Windows that option lets a bind SUCCEED on a port another process is
        # actively listening on, which would destroy the refusal above: the
        # sweep would silently start answering with another agent's plug
        # instead of refusing. Waiting is the option that keeps the guarantee.
        $listener = $null
        for ($try = 0; $try -lt 40; $try++) {
            try {
                $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 9100)
                $listener.Start()
                break
            } catch {
                $listener = $null
                Start-Sleep -Milliseconds 250
            }
        }
        if (-not $listener) { return '' }
        $deadline = (Get-Date).AddSeconds(30)
        while (-not $listener.Pending() -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 100 }
        if (-not $listener.Pending()) { $listener.Stop(); return '' }
        $client = $listener.AcceptTcpClient()
        $listener.Stop()
        $ns = $client.GetStream()
        $data = [System.IO.File]::ReadAllBytes($IrPath)
        $ns.Write([BitConverter]::GetBytes([int]($data.Length + 1)), 0, 4)
        $ns.WriteByte(1)
        $off = 0
        while ($off -lt $data.Length) {
            $n = [Math]::Min(4096, $data.Length - $off)
            $ns.Write($data, $off, $n); $ns.Flush(); $off += $n
            if ($off -lt $data.Length) { Start-Sleep -Milliseconds 20 }
        }
        $ns.ReadTimeout = 300000
        $resp = [System.Collections.Generic.List[byte]]::new()
        $buf = New-Object byte[] 65536
        while ($true) {
            try { $n = $ns.Read($buf, 0, $buf.Length) } catch { break }
            if ($n -le 0) { break }
            for ($i = 0; $i -lt $n; $i++) { $resp.Add($buf[$i]) }
        }
        $client.Close()
        return [System.Text.Encoding]::ASCII.GetString($resp.ToArray())
    } finally {
        if ($proc -and -not $proc.HasExited) { try { Stop-Process -Id $proc.Id -Force -ErrorAction Stop } catch {} }
        Remove-Item -Force $stderrFile -ErrorAction SilentlyContinue
    }
}

# A test with a .flags sidecar is run by the battery with those flags, and this
# sweep does not apply them -- it forces -Passes to answer one question about
# which program it checked, and a sidecar setting `passes=` would contradict
# that. Four of codex/test carry one (measured 2026-08-03). They are EXCLUDED
# and counted separately rather than skipped into the same bucket as a chapter
# that genuinely does not compile: a hand run that silently drops a sidecar
# reports the invocation rather than the subject (L-SIDECAR).
$log = [System.Collections.Generic.List[string]]::new()
$kinds = @{}
$swept = 0; $skipped = 0; $filesWithDisagree = 0; $plugDied = 0
$totDefs = 0; $totAgree = 0; $totDis = 0; $totUns = 0
$started = Get-Date

$excluded = 0
foreach ($f in $files) {
    $name = $f.BaseName
    # .flags: the battery runs these with flags this sweep does not apply.
    # .failing / .fatal: the tree already marks the program as one that does
    # not compile or does not run. Its IR is the IR of a known-bad program, so
    # a disagreement there says nothing about the rechecker or the compiler.
    # Both of the two disagreements in the first 450-chapter sweep were
    # .failing tests, and in both the rechecker independently found what the
    # sidecar records -- an application whose callee is not a function, which
    # is what an unresolved type-class instance leaves in the IR. That is
    # worth knowing and it is not a finding to carry in the disagreement set.
    $skipSidecar = ''
    foreach ($sc in @('flags', 'failing', 'fatal', 'skip')) {
        if (Test-Path (Join-Path $f.DirectoryName "$name.$sc")) { $skipSidecar = $sc; break }
    }
    if ($skipSidecar) {
        $excluded++
        $log.Add("EXCLUDE $name (.$skipSidecar sidecar)")
        Write-Host ("  {0,-34} excluded (.{1})" -f $name, $skipSidecar)
        continue
    }
    $ir  = Join-Path $WorkDir "$name.ir"
    $lg  = Join-Path $WorkDir "$name.log"
    & pwsh -NoProfile -File (Join-Path $Repo 'build\compile.ps1') -Src $f.FullName -Out $ir `
        -Log $lg -IrCce -Passes $Passes -Kernel $Kernel 2>&1 | Out-Null
    # A chapter that does not compile standalone is not a rechecker finding.
    # Most of codex/test does compile; the ones that need a sidecar or a
    # citation do not, and counting them as swept would inflate the coverage
    # this sweep claims.
    # Staleness, not deletion. A previous run's .ir sitting in this directory
    # would be swept as though it were this run's output, so the file must be
    # newer than the run rather than merely present.
    $fresh = (Test-Path $ir) -and ((Get-Item $ir).LastWriteTime -ge $started)
    if ((Select-String -Path $lg -Pattern 'error CDX' -Quiet) -or (-not $fresh)) {
        $skipped++
        $log.Add("SKIP $name (did not compile standalone)")
        continue
    }
    # A plug that dies mid-exchange must cost ONE chapter, not the run. Two
    # full sweeps were lost at roughly 450 of 492 to an unhandled exception
    # here: a large UI payload killed the plug VM and the host was still
    # writing, so Write threw "an existing connection was forcibly closed".
    # The tokenizer's own prose records a 48 MB IR double-faulting this plug,
    # so a chapter that kills it is expected and is a finding about coverage
    # rather than a reason to lose the other 491 results.
    $out = ''
    try { $out = Invoke-Plug $ir } catch { $out = '' }
    if (-not $out) {
        $plugDied++
        $log.Add("PLUG-FAILED $name (no answer; payload $((Get-Item $ir).Length) bytes)")
        Write-Host ("  {0,-34} PLUG FAILED ({1} bytes)" -f $name, (Get-Item $ir).Length)
        continue
    }
    $swept++
    $log.Add("=== $name")
    foreach ($line in ($out -split "`n")) {
        $line = $line.TrimEnd("`r")
        if ($line -match '^STAGE (\d) DEFS (\d+) AGREE (\d+) DISAGREE (\d+) UNSUPPORTED (\d+)') {
            $log.Add("  $line")
            if ($matches[1] -eq '1') { $totDefs += [int]$matches[2] }
            $totAgree += [int]$matches[3]; $totDis += [int]$matches[4]; $totUns += [int]$matches[5]
        } elseif ($line -match '^(DISAGREE|UNSUPPORTED) (\S+) \[([^\]]+)\] (.*)$') {
            $log.Add("  $line")
            $k = "$($matches[1]) [$($matches[3])]"
            if (-not $kinds.ContainsKey($k)) { $kinds[$k] = [System.Collections.Generic.List[string]]::new() }
            $kinds[$k].Add("$name/$($matches[2]): $($matches[4])")
        } elseif ($line -match '^FUEL-EXHAUSTED') {
            $log.Add("  $line")
        }
    }
    if ($out -match '(?m)^DISAGREE ') { $filesWithDisagree++ }
    Write-Host ("  {0,-34} {1}" -f $name, $(if ($out -match '(?m)^DISAGREE ') { 'DISAGREE' } else { 'clean' }))
}

$elapsed = ((Get-Date) - $started).TotalSeconds
[System.IO.File]::WriteAllLines($Report, $log)

Write-Host ''
Write-Host '=== SWEEP ==='
Write-Host ("  dir                    : {0}  (passes={1})" -f $Dir, $Passes)
Write-Host ("  chapters swept         : {0}" -f $swept)
Write-Host ("  skipped, did not build : {0}" -f $skipped)
Write-Host ("  excluded by sidecar    : {0}" -f $excluded)
Write-Host ("  plug died on payload   : {0}" -f $plugDied)
Write-Host ("  definitions (stage 1)  : {0}" -f $totDefs)
Write-Host ("  chapters disagreeing   : {0}" -f $filesWithDisagree)
Write-Host ("  verdicts across stages : AGREE {0}  DISAGREE {1}  UNSUPPORTED {2}" -f $totAgree, $totDis, $totUns)
Write-Host ("  elapsed                : {0:N0}s" -f $elapsed)
Write-Host ''
if ($kinds.Count -eq 0) {
    Write-Host '  no disagreements and nothing unsupported'
} else {
    Write-Host '=== BY KIND ==='
    foreach ($k in ($kinds.Keys | Sort-Object)) {
        Write-Host ("  {0,-40} {1}" -f $k, $kinds[$k].Count)
        foreach ($ex in ($kinds[$k] | Select-Object -First 3)) { Write-Host "      $ex" }
        if ($kinds[$k].Count -gt 3) { Write-Host ("      ... and {0} more" -f ($kinds[$k].Count - 3)) }
    }
}
Write-Host ''
Write-Host "  full log: $Report"
Write-Host '  A disagreement here is a bug report against ONE of the two implementations'
Write-Host '  and is unresolved until a human reads it. Do not tune this quiet.'
