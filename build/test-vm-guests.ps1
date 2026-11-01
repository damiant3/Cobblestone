# test-vm-guests.ps1 -- boot one x86-64 test CDX as several codex-vm guests that
# share one -ivshmem file, and write the guests' filtered output, in sidecar
# order, to -OutFile. test.ps1 routes a test carrying a .vm-guests sidecar here.
#
# Sidecar: one guest per line, `<name> <start> <codex-vm arguments...>`; blank
# lines and # comments are ignored. <start> is `+<ms>` (start that long after
# the previous guest STARTED; the first guest's value is its delay from now) or
# `after` (start when the previous guest has EXITED). `{shm}` in an argument is
# a fresh zeroed 1 MiB file, one per run, shared by every guest of the run.
#
# Output: `GUEST <name>` and then that guest's output lines, HEAP:/WD:/STACK:/PM:
# dropped and trailing blank lines cut, for each guest. Exit 0 when every guest
# left within -TimeoutSec, else 1.
#
#   pwsh build/test-vm-guests.ps1 -Kernel x.cdx -OutFile x.raw -GuestsFile codex/test/apps/x.vm-guests
param(
    [Parameter(Mandatory=$true)][string]$Kernel,
    [Parameter(Mandatory=$true)][string]$OutFile,
    [Parameter(Mandatory=$true)][string]$GuestsFile,
    [int]$TimeoutSec = 120
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$utf8 = [System.Text.UTF8Encoding]::new($false)
$vm = Join-Path $PSScriptRoot '..\tools\codex-vm.exe'
$Kernel = (Resolve-Path -LiteralPath $Kernel).Path

$guests = [System.Collections.Generic.List[object]]::new()
foreach ($line in (Get-Content -LiteralPath $GuestsFile)) {
    $t = $line.Trim()
    if ($t -eq '' -or $t.StartsWith('#')) { continue }
    $parts = @($t -split '\s+')
    if ($parts.Count -lt 2 -or ($parts[1] -ne 'after' -and $parts[1] -notmatch '^\+\d+$')) {
        [System.IO.File]::WriteAllText($OutFile, '', $utf8)
        [Console]::Error.WriteLine("bad guest line in ${GuestsFile}: $t")
        exit 1
    }
    $guests.Add([pscustomobject]@{ Name = $parts[0]; Start = $parts[1]; Args = @($parts | Select-Object -Skip 2); Proc = $null })
}
if ($guests.Count -eq 0) {
    [System.IO.File]::WriteAllText($OutFile, '', $utf8)
    [Console]::Error.WriteLine("no guest in $GuestsFile")
    exit 1
}

$work = Join-Path ([System.IO.Path]::GetTempPath()) ('vm-guests-' + [guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $work)
$shm = Join-Path $work 'shm.bin'
[System.IO.File]::WriteAllBytes($shm, (New-Object byte[] 1048576))

$ok = $true
$deadline = (Get-Date).AddSeconds($TimeoutSec)
try {
    $prev = $null
    foreach ($g in $guests) {
        if ($g.Start -eq 'after') {
            if ($prev) { [void]$prev.Proc.WaitForExit([Math]::Max(0, [int]($deadline - (Get-Date)).TotalMilliseconds)) }
        } else {
            Start-Sleep -Milliseconds ([int]$g.Start.Substring(1))
        }
        $out = Join-Path $work "$($g.Name).out"
        $argv = @('-kernel', ('"' + $Kernel + '"'), '-headless', '-output', ('"' + $out + '"'))
        foreach ($a in $g.Args) { $argv += $(if ($a.Contains('{shm}')) { '"' + $a.Replace('{shm}', $shm) + '"' } else { $a }) }
        $g.Proc = Start-Process -FilePath $vm -ArgumentList $argv -PassThru -WindowStyle Hidden -RedirectStandardError (Join-Path $work "$($g.Name).err")
        $prev = $g
    }
    foreach ($g in $guests) {
        $left = [Math]::Max(0, [int]($deadline - (Get-Date)).TotalMilliseconds)
        if (-not $g.Proc.WaitForExit($left)) {
            Stop-Process -Id $g.Proc.Id -Force -ErrorAction SilentlyContinue
            [void]$g.Proc.WaitForExit(5000)
            $ok = $false
            [Console]::Error.WriteLine("guest $($g.Name): still running after $TimeoutSec s")
        }
    }
    $body = [System.Text.StringBuilder]::new()
    foreach ($g in $guests) {
        [void]$body.Append("GUEST $($g.Name)`n")
        $f = Join-Path $work "$($g.Name).out"
        $raw = if (Test-Path -PathType Leaf $f) { [System.IO.File]::ReadAllText($f) -replace "`r", '' } else { '' }
        $lines = [System.Collections.Generic.List[string]]::new()
        foreach ($l in ($raw -split "`n")) {
            if ($l.StartsWith('HEAP:') -or $l.StartsWith('WD:') -or $l.StartsWith('STACK:') -or $l.StartsWith('PM:')) { continue }
            $lines.Add($l)
        }
        while ($lines.Count -gt 0 -and $lines[$lines.Count - 1] -eq '') { $lines.RemoveAt($lines.Count - 1) }
        foreach ($l in $lines) { [void]$body.Append("$l`n") }
    }
    [System.IO.File]::WriteAllText($OutFile, $body.ToString(), $utf8)
} finally {
    foreach ($g in $guests) { if ($g.Proc -and -not $g.Proc.HasExited) { Stop-Process -Id $g.Proc.Id -Force -ErrorAction SilentlyContinue } }
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
if ($ok) { exit 0 } else { exit 1 }
