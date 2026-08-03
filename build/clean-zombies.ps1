# clean-zombies.ps1 -- purge orphaned VM + WSL bridge processes.
#
# Use after a SIGKILL-style abort (taskkill /F, harness OOM, wedged
# WSL VM) when traps in the test / build / 3stage harnesses
# could not run. Safe between test runs; do NOT run while another test
# is in progress -- it kills any VM regardless of which agent
# started it.
#
# Invoke directly:    powershell -NoProfile -File build/clean-zombies.ps1
# Or from Git Bash:   build/clean-zombies.ps1   (file association)
#
# Fast-path: skip steps for processes that aren't running. In
# particular, never call `wsl --shutdown` unless vmmemWSL is up --
# wsl.exe spins the VM up to shut it down, costing minutes when
# WSL was already idle.

$killed = $false

foreach ($name in 'qemu-system-x86_64', 'codex-vm', 'wsl') {
    $procs = Get-Process -Name $name -ErrorAction SilentlyContinue
    if ($name -eq 'codex-vm' -and $procs) {
        # Spare long-running SERVER VMs (webservices booted with -portfwd,
        # e.g. apps/ideas). Compile/test VMs never use -portfwd, so any
        # codex-vm with it on the command line is deliberately serving,
        # not an orphan. Killing it breaks another agent's live demo.
        $procs = @($procs | Where-Object {
            $cl = (Get-CimInstance Win32_Process -Filter "ProcessId=$($_.Id)" -ErrorAction SilentlyContinue).CommandLine
            "$cl" -notlike '*-portfwd*'
        })
    }
    if ($procs) {
        if ($name -eq 'codex-vm') {
            foreach ($p in $procs) {
                $evtName = "Global\CodexVmShutdown_$($p.Id)"
                try {
                    $evt = [System.Threading.EventWaitHandle]::OpenExisting($evtName)
                    $evt.Set() | Out-Null
                    $evt.Dispose()
                } catch {}
            }
            Start-Sleep -Milliseconds 3000
        }
        $procs | Where-Object { -not $_.HasExited } | ForEach-Object {
            Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
        }
        $killed = $true
    }
}

if (Get-Process -Name vmmemWSL -ErrorAction SilentlyContinue) {
    & wsl.exe --shutdown 2>$null | Out-Null
    $killed = $true
}

if ($killed) {
    Write-Output "clean-zombies: done"
} else {
    Write-Output "clean-zombies: nothing to do"
}
