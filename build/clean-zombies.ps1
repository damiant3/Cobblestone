# clean-zombies.ps1 -- Purge orphaned VM and WSL bridge processes
# GENERATED FROM THE CODEX SHELL DSL. Do not edit by hand.
# A hand edit here must NOT be submitted. Change the generator under
# codex/build/, regenerate, and submit the generator and this file
# together. Until then build/check-generated-scripts.ps1 reports this
# file as drifted, and the next regeneration discards the edit.
[CmdletBinding()]
param(
)

$killed = $false


foreach ($name in @('qemu-system-x86_64', 'codex-vm', 'wsl')) {
    $procs = Get-Process -Name $name -ErrorAction SilentlyContinue
    if ((($name -eq 'codex-vm') -and $procs)) {
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
        if (($name -eq 'codex-vm')) {
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
        $procs | Where-Object { -not $_.HasExited } | ForEach-Object { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }
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
