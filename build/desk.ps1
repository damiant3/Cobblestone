# Run the Codex desktop (GopDesk) under codex-vm.
#
#   build/desk.ps1                       launch the interactive window
#   build/desk.ps1 -Shot desk.bmp        headless, capture one frame, exit
#   build/desk.ps1 -Keys '4000:4'        drive it with a scancode timeline
#   build/desk.ps1 -Disk seed/Codex.img  give the Files pane a real ESP
#
# GopDesk normally reaches the glass through an Option A boot image. This is
# the dev-box path: apps/works/DeskVm.codex reads codex-vm's own GOP cells
# instead of the boot stub's, so the desktop comes up from a plain CDX in a
# couple of seconds with nothing to flash and no firmware in the way.
[CmdletBinding()]
param(
    [int]$Width  = 1280,
    [int]$Height = 800,
    [int]$Mem    = 3072,
    # Compile even when the CDX is newer than the source.
    [switch]$Force,
    # The compiler this is built with. Pinned to the depot seed by default:
    # build-output/ is not in the depot, so "whatever was there" gives an
    # artifact with no provenance.
    [string]$Kernel = 'seed/Codex.cdx',
    # Headless single-frame capture instead of a window.
    [string]$Shot = '',
    [int]$ShotDelayMs = 6000,
    # Scancode timeline, 't:scancode' separated by ';' or newlines. 33 is f,
    # 4 is 3, 1 is Esc.
    [string]$Keys = '',
    # Freeze the clock so a captured frame is comparable against a recorded
    # one; the taskbar paints the CMOS RTC and is otherwise host state.
    [string]$Rtc = '',
    # IDE disk image. Without one the Files pane says "no FAT ESP on the
    # boot medium" and desk-font falls back to the CBF face, both correctly.
    # seed/Codex.img is a real ESP and browses. codex-vm writes back and
    # flushes to the host, so this COPIES the image to build-output and
    # attaches the copy rather than letting a guest edit the depot artifact.
    [string]$Disk = '',
    [switch]$Wait
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Src  = Join-Path $Repo 'apps/works/DeskVm.codex'
$Out  = Join-Path $Repo 'build-output/desk.cdx'
$Log  = Join-Path $Repo 'build-output/desk.log'
$Vm   = Join-Path $Repo 'tools/codex-vm.exe'

if (-not (Test-Path $Src)) { throw "desk source not found: $Src" }
if (-not (Test-Path $Vm))  { throw "codex-vm not found: $Vm (build it with tools/build-vm.ps1)" }
$outDir = Split-Path $Out -Parent
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }

$stale = $Force -or -not (Test-Path $Out) -or `
         ((Get-Item $Src).LastWriteTimeUtc -gt (Get-Item $Out).LastWriteTimeUtc)

if ($stale) {
    Write-Host "[desk] compiling $([IO.Path]::GetFileName($Src)) against $Kernel"
    & (Join-Path $PSScriptRoot 'compile.ps1') -Src $Src -Out $Out -Log $Log -Kernel $Kernel *>$null
    # compile.ps1 prints True on a FAILED compile, so the log is the verdict.
    if (Select-String -Path $Log -Pattern 'error CDX' -Quiet) {
        Select-String -Path $Log -Pattern 'error CDX' | Select-Object -First 12 |
            ForEach-Object { Write-Host $_.Line.Trim() }
        throw "desk compile failed; full log at $Log"
    }
    Write-Host ("[desk] built {0:N0} bytes" -f (Get-Item $Out).Length)
} else {
    Write-Host "[desk] using existing $Out (pass -Force to rebuild)"
}

$vmArgs = @('-kernel', $Out, '-gop-width', $Width, '-gop-height', $Height, '-mem', $Mem)

if ($Keys) {
    $keyFile = Join-Path $outDir 'desk-keys.txt'
    Set-Content -Path $keyFile -Value ($Keys -replace ';', "`n")
    $vmArgs += @('-keys-file', $keyFile)
}
if ($Rtc) { $vmArgs += @('-rtc', $Rtc) }

if ($Disk) {
    $diskSrc = if ([IO.Path]::IsPathRooted($Disk)) { $Disk } else { Join-Path $Repo $Disk }
    if (-not (Test-Path $diskSrc)) { throw "disk image not found: $diskSrc" }
    $diskCopy = Join-Path $outDir ('desk-' + [IO.Path]::GetFileName($diskSrc))
    Copy-Item $diskSrc $diskCopy -Force
    Set-ItemProperty $diskCopy -Name IsReadOnly -Value $false
    Write-Host "[desk] disk $([IO.Path]::GetFileName($diskSrc)) (working copy, the original is untouched)"
    $vmArgs += @('-disk', $diskCopy)
}

if ($Shot) {
    $shotPath = if ([IO.Path]::IsPathRooted($Shot)) { $Shot } else { Join-Path $Repo $Shot }
    $vmArgs += @('-headless', '-screenshot', $shotPath, '-screenshot-delay', $ShotDelayMs)
    Write-Host "[desk] headless, frame at ${ShotDelayMs}ms -> $shotPath"
    & $Vm @vmArgs *>$null
    if (-not (Test-Path $shotPath)) { throw "no frame captured; the guest did not reach the screenshot deadline" }
    Write-Host "[desk] captured $shotPath"
    exit 0
}

Write-Host "[desk] ${Width}x${Height}; desk paints about 1.5s after launch"
Write-Host "[desk] keys: f = Files, 3 = 3D View, Esc leaves a view. Editor/Terminal/Monitor/Settings are inert."
Write-Host "[desk] the desk itself does not exit by key: Shutdown (bottom-left) or Stop-Process."

if ($Wait) {
    & $Vm @vmArgs
} else {
    # Several agents run VMs on this box, so hand back the PID: killing
    # codex-vm by NAME takes down everyone else's guest too.
    $proc = Start-Process -FilePath $Vm -ArgumentList $vmArgs -PassThru
    Start-Sleep -Milliseconds 500
    Write-Host ("[desk] codex-vm PID {0}" -f $proc.Id)
    Write-Host ("[desk] close with: Stop-Process -Id {0} -Force" -f $proc.Id)
}
