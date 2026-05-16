# clean-zombies.ps1 — purge orphaned QEMU + WSL bridge processes.
#
# Use after a SIGKILL-style abort (taskkill /F, harness OOM, wedged
# WSL VM) when traps in the test / build / 3stage harnesses
# could not run. Safe between test runs; do NOT run while another test
# is in progress — it kills any QEMU regardless of which agent
# started it.
#
# Invoke directly:    powershell -NoProfile -File codex.build/clean-zombies.ps1
# Or from Git Bash:   codex.build/clean-zombies.ps1   (file association)
#
# Fast-path: skip steps for processes that aren't running. In
# particular, never call `wsl --shutdown` unless vmmemWSL is up —
# wsl.exe spins the VM up to shut it down, costing minutes when
# WSL was already idle.

$killed = $false

foreach ($name in 'qemu-system-x86_64', 'codex-vm', 'wsl') {
    if (Get-Process -Name $name -ErrorAction SilentlyContinue) {
        Stop-Process -Name $name -Force -ErrorAction SilentlyContinue
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
