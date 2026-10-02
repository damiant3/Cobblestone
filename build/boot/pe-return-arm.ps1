[CmdletBinding()]
param(
    [string]$Kernel = 'seed\Codex.cdx',
    [int]$HoldSeconds = 10,
    [int]$MarkerSeconds = 90
)

# plugs-backlog 2.108's regression arm. build/boot/PeReturnArm.codex starts the
# timer through runtime-init, prints its marker and RETURNS from opening into
# the cdx-to-pe stub's epilog. Under OVMF with -no-reboot, an epilog whose halt
# a timer interrupt resumes runs into the bytes after it and QEMU exits; a
# terminal epilog leaves QEMU halted and alive.
#
#   pwsh build/boot/pe-return-arm.ps1     # exit 0: QEMU alive HoldSeconds after the marker
#
# Exit 1 on a red, 2 when the arm cannot run (compile, PE, image, memory).

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Set-Location $Repo
$Qemu = 'D:\Program Files\qemu\qemu-system-x86_64.exe'
$Work = Join-Path $env:TEMP ("pe-return-arm-" + (Split-Path $Repo -Leaf))
New-Item -ItemType Directory -Force $Work | Out-Null

$freeGiB = (Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1MB
if ($freeGiB -lt 3.5) { Write-Host ("pe-return-arm: {0:N1} GiB free, the guest takes 2; not started" -f $freeGiB); exit 2 }

$cdx = Join-Path $Work 'pera.cdx'
if (Test-Path $cdx) { [IO.File]::Delete($cdx) }
& pwsh -NoProfile -File build/compile.ps1 -Src build/boot/PeReturnArm.codex -Out $cdx -Log (Join-Path $Work 'pera.log') -Kernel $Kernel | Out-Null
if (-not (Test-Path -PathType Leaf $cdx)) { Write-Host "pe-return-arm: compile failed, see $Work\pera.log"; exit 2 }
& pwsh -NoProfile -File build/cdx-to-pe.ps1 -CdxInput $cdx -Out (Join-Path $Work 'pera.efi') -HeapPages 32768 -ExitBootServices | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host 'pe-return-arm: PE conversion failed'; exit 2 }
& pwsh -NoProfile -File build/build-img.ps1 -PeInput (Join-Path $Work 'pera.efi') -Out (Join-Path $Work 'pera.img') | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host 'pe-return-arm: image build failed'; exit 2 }

$code = Join-Path $Work 'code.fd'; $vars = Join-Path $Work 'vars.fd'; $serial = Join-Path $Work 'serial.log'
Copy-Item -Force 'D:\Program Files\qemu\share\edk2-x86_64-code.fd' $code
Copy-Item -Force 'D:\Program Files\qemu\share\edk2-i386-vars.fd' $vars
Set-ItemProperty -LiteralPath $vars -Name IsReadOnly -Value $false
if (Test-Path $serial) { [IO.File]::Delete($serial) }
$qargs = @('-accel', 'tcg', '-m', '2048', '-machine', 'q35',
    '-drive', "if=pflash,format=raw,unit=0,readonly=on,file=$code", '-drive', "if=pflash,format=raw,unit=1,file=$vars",
    '-drive', "format=raw,file=$(Join-Path $Work 'pera.img')", '-serial', "file:$serial", '-display', 'none', '-vga', 'std', '-no-reboot')
$guest = Start-Process -FilePath $Qemu -ArgumentList @($qargs | ForEach-Object { '"' + $_ + '"' }) -WindowStyle Hidden -PassThru
Write-Host "pe-return-arm: QEMU pid $($guest.Id), serial $serial"

function Read-Serial { if (-not (Test-Path $serial)) { return '' }; $f = [IO.File]::Open($serial, 'Open', 'Read', 'ReadWrite'); try { [IO.StreamReader]::new($f).ReadToEnd() } finally { $f.Dispose() } }
$verdict = 1
try {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while (-not (Read-Serial).Contains('pe-return-arm-end') -and -not $guest.HasExited -and $sw.Elapsed.TotalSeconds -lt $MarkerSeconds) { Start-Sleep -Milliseconds 200 }
    if (-not (Read-Serial).Contains('pe-return-arm-end')) {
        Write-Host "pe-return-arm: no marker (QEMU exited: $($guest.HasExited)); the arm did not reach the epilog"
        $verdict = 2
    } else {
        $sw.Restart()
        while (-not $guest.HasExited -and $sw.Elapsed.TotalSeconds -lt $HoldSeconds) { Start-Sleep -Milliseconds 200 }
        if ($guest.HasExited) { Write-Host ("RED: QEMU exited {0:N1} s after the marker: the epilog resumed past its halt" -f $sw.Elapsed.TotalSeconds) }
        else { Write-Host "GREEN: QEMU halted and alive $HoldSeconds s after the marker"; $verdict = 0 }
    }
} finally {
    if (-not $guest.HasExited) { Stop-Process -Id $guest.Id -Force }
    [void]$guest.WaitForExit(5000)
}
exit $verdict
