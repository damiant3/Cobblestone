# test-qemu-x86.ps1 -- boot one x86-64 test CDX under QEMU once per arm of its
# .qemu-x86 sidecar and write the arms' filtered serial output, in order, to
# -OutFile. test.ps1 routes a test carrying that sidecar here instead of
# codex-vm, which emulates no virtio-pci device.
#
# Sidecar: one arm per line, `<name> <QEMU arguments...>`; blank lines and #
# comments are ignored. `{disk}` in an argument is the test's disk image
# (its .disk or minted .disk-mint), copied once to a writable file that every
# arm shares in order, so an arm can read what an earlier arm wrote.
#
# Input: `inject <arm> <trigger> => <cmd>; <cmd>; ...` gives that arm a QEMU
# monitor and sends the commands (`mouse_move dx dy`, `mouse_button n`,
# `sendkey ...`), 300 ms apart, once a serial line equal to <trigger> appears.
# QEMU's PS/2 mouse drops every event that arrives before the guest enables
# data reporting, so a timed send loses events; the guest prints the trigger
# after its handshake. An inject whose trigger never appears fails the arm.
#
# Output: `ARM <name>` and then that arm's serial lines, HEAP:/WD:/STACK:
# dropped and trailing blank lines cut, for each arm. Exit 0 when every arm
# left QEMU through isa-debug-exit 0 (QEMU's exit code 1), else 1.
#
#   pwsh build/test-qemu-x86.ps1 -Kernel x.cdx -OutFile x.raw -ArmsFile codex/test/apps/x.qemu-x86 [-DiskFile x.disk]
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Kernel,
    [Parameter(Mandatory=$true)][string]$OutFile,
    [Parameter(Mandatory=$true)][string]$ArmsFile,
    [string]$DiskFile = '',
    [int]$TimeoutSec = 30
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'vm-config.ps1')

$utf8 = [System.Text.UTF8Encoding]::new($false)
$qemu = $script:FallbackVmBin
if (-not $qemu -or -not (Test-Path -PathType Leaf $qemu)) {
    [System.IO.File]::WriteAllText($OutFile, '', $utf8)
    [Console]::Error.WriteLine('no qemu-system-x86_64: install QEMU or set QEMU_BIN')
    exit 1
}
$Kernel = (Resolve-Path -LiteralPath $Kernel).Path

$arms = [System.Collections.Generic.List[object]]::new()
$injects = @{}
foreach ($line in (Get-Content -LiteralPath $ArmsFile)) {
    $t = $line.Trim()
    if ($t -eq '' -or $t.StartsWith('#')) { continue }
    $parts = @($t -split '\s+')
    if ($parts[0] -eq 'inject') {
        $m = [regex]::Match($t, '^inject\s+(\S+)\s+(.+?)\s+=>\s+(.+)$')
        if (-not $m.Success) {
            [System.IO.File]::WriteAllText($OutFile, '', $utf8)
            [Console]::Error.WriteLine("malformed inject line in ${ArmsFile}: $t")
            exit 1
        }
        $injects[$m.Groups[1].Value] = [pscustomobject]@{
            Trigger = $m.Groups[2].Value
            Cmds = @($m.Groups[3].Value -split ';' | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' })
        }
        continue
    }
    $arms.Add([pscustomobject]@{ Name = $parts[0]; Args = @($parts | Select-Object -Skip 1) })
}
foreach ($k in $injects.Keys) {
    if (-not @($arms | Where-Object { $_.Name -eq $k })) {
        [System.IO.File]::WriteAllText($OutFile, '', $utf8)
        [Console]::Error.WriteLine("inject names arm '$k', which $ArmsFile does not define")
        exit 1
    }
}
if ($arms.Count -eq 0) {
    [System.IO.File]::WriteAllText($OutFile, '', $utf8)
    [Console]::Error.WriteLine("no arm in $ArmsFile")
    exit 1
}

$work = Join-Path ([System.IO.Path]::GetTempPath()) ('qemu-x86-' + [guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $work)
$disk = ''
if ($DiskFile -and (Test-Path -PathType Leaf $DiskFile)) {
    $disk = Join-Path $work 'disk.img'
    [System.IO.File]::WriteAllBytes($disk, [System.IO.File]::ReadAllBytes($DiskFile))
}

$body = [System.Text.StringBuilder]::new()
$ok = $true
try {
    foreach ($arm in $arms) {
        $serial = Join-Path $work "$($arm.Name).serial"
        $stderr = Join-Path $work "$($arm.Name).stderr"
        # The boot recipe of the pre-battery virtio proofs: the 0xfe8 loader
        # cell carries guest RAM size (0xC0000000 = 3 GiB), -cpu max is required
        # under TCG, and isa-debug-exit is what makes QEMU leave on the guest's
        # exit write.
        $argv = @('-accel', 'tcg', '-cpu', 'max', '-machine', 'pc,kernel-irqchip=off', '-m', '3072',
            '-kernel', ('"' + $Kernel + '"'), '-device', 'loader,addr=0xfe8,data=0xc0000000,data-len=4',
            '-serial', ('"file:' + $serial + '"'), '-serial', 'null',
            '-device', 'isa-debug-exit,iobase=0xf4,iosize=0x04', '-display', 'none', '-no-reboot')
        foreach ($a in $arm.Args) {
            if ($a.Contains('{disk}')) {
                if (-not $disk) { throw "arm $($arm.Name) names {disk} and the test has no disk" }
                $a = '"' + $a.Replace('{disk}', $disk) + '"'
            }
            $argv += $a
        }
        $inj = $injects[$arm.Name]
        $monPort = 0
        if ($inj) {
            # A port the OS just handed out and nothing holds, so the commands
            # cannot reach another run's monitor (L-SHARED).
            $probe = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
            $probe.Start(); $monPort = $probe.LocalEndpoint.Port; $probe.Stop()
            $argv += @('-monitor', "tcp:127.0.0.1:$monPort,server,nowait")
        }
        $p = Start-Process -FilePath $qemu -ArgumentList $argv -PassThru -WindowStyle Hidden -RedirectStandardError $stderr
        $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSec)
        $sent = $false
        $exited = $false
        while ($true) {
            if ($p.WaitForExit(100)) { $exited = $true; break }
            if ([DateTime]::UtcNow -ge $deadline) { break }
            if ($inj -and -not $sent -and (Test-Path -PathType Leaf $serial)) {
                $fs = [System.IO.FileStream]::new($serial, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
                $sr = [System.IO.StreamReader]::new($fs)
                $live = $sr.ReadToEnd(); $sr.Dispose()
                $seen = @(($live -replace "`r", '') -split "`n" | Where-Object { $_ -eq $inj.Trigger })
                if ($seen.Count -gt 0) {
                    $c = [System.Net.Sockets.TcpClient]::new('127.0.0.1', $monPort)
                    $s = $c.GetStream()
                    foreach ($cmd in $inj.Cmds) {
                        $b = [System.Text.Encoding]::ASCII.GetBytes($cmd + "`n"); $s.Write($b, 0, $b.Length); $s.Flush()
                        Start-Sleep -Milliseconds 300
                    }
                    $c.Close()
                    $sent = $true
                }
            }
        }
        if (-not $exited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue; $p.WaitForExit(5000) | Out-Null }
        if ($inj -and -not $sent) { $ok = $false; [Console]::Error.WriteLine("arm $($arm.Name): trigger '$($inj.Trigger)' never appeared, nothing injected") }
        [void]$body.Append("ARM $($arm.Name)`n")
        $raw = if (Test-Path -PathType Leaf $serial) { [System.IO.File]::ReadAllText($serial) -replace "`r", '' } else { '' }
        $lines = [System.Collections.Generic.List[string]]::new()
        foreach ($l in ($raw -split "`n")) {
            if ($l.StartsWith('HEAP:') -or $l.StartsWith('WD:') -or $l.StartsWith('STACK:')) { continue }
            $lines.Add($l)
        }
        while ($lines.Count -gt 0 -and $lines[$lines.Count - 1] -eq '') { $lines.RemoveAt($lines.Count - 1) }
        foreach ($l in $lines) { [void]$body.Append("$l`n") }
        if (-not $exited) { $ok = $false; [Console]::Error.WriteLine("arm $($arm.Name): QEMU timed out after $TimeoutSec s") }
        elseif ($p.ExitCode -ne 1) { $ok = $false; [Console]::Error.WriteLine("arm $($arm.Name): QEMU exited $($p.ExitCode), not through debug-exit 0"); if (Test-Path $stderr) { [Console]::Error.WriteLine((Get-Content -Raw $stderr)) } }
        else {
            # QEMU refusing its own arguments also exits 1, the code debug-exit 0
            # gives, so its error line on stderr is what tells the two apart.
            $refusal = @(Get-Content -LiteralPath $stderr -ErrorAction SilentlyContinue | Where-Object { $_ -match 'qemu-system-x86_64(\.exe)?: (?!warning:)' })
            if ($refusal.Count -gt 0) { $ok = $false; [Console]::Error.WriteLine("arm $($arm.Name): QEMU refused: $($refusal -join ' / ')") }
        }
        $p.Dispose()
    }
} finally {
    [System.IO.File]::WriteAllText($OutFile, $body.ToString(), $utf8)
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
if ($ok) { exit 0 } else { exit 1 }
