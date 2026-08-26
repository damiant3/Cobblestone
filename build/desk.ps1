# Run the Codex desktop (GopDesk) under codex-vm.
#
#   build/desk.ps1                       launch the interactive window
#   build/desk.ps1 -Shot desk.bmp        headless, capture one frame, exit
#   build/desk.ps1 -Keys '4000:4'        type into an ALREADY-FOCUSED pane
#                                        (it cannot open one; see -Keys below)
#   build/desk.ps1 -Disk seed/Codex.img  give the Files pane a real ESP
#
# GopDesk normally reaches the glass through an Option A boot image. This is
# the dev-box path: apps/works/DeskVm.codex reads codex-vm's own GOP cells
# instead of the boot stub's, so the desktop comes up from a plain CDX in a
# couple of seconds with nothing to flash and no firmware in the way.
[CmdletBinding()]
param(
    # 1600 is the step where the widget layer scales (GopDraw ui-wscale), so the
    # chrome comes up at twice the size instead of stranding a 1024-sized UI in a
    # big frame. 1920x1080 works too; this default is the one that fits inside a
    # 1920 monitor once the window has a title bar.
    [int]$Width  = 1600,
    [int]$Height = 900,
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
    # Scancode timeline, 't:scancode' separated by ';' or newlines.
    #
    # THIS CANNOT OPEN AN APP ANY MORE, and it could until 2026-08-26. The
    # desktop no longer launches from a keystroke (GopDesk, "THE DESKTOP NO
    # LONGER LAUNCHES AN APP FROM A KEYSTROKE"): desk-loop swallows every
    # scancode except F12 while no pane is focused, so a timeline of 33/46/37
    # against a bare desktop does nothing at all and looks exactly like key
    # injection being broken. Measured 2026-08-26: four codes over nine
    # seconds, no pane opened, and -hid-keys does not change it.
    #
    # Keys reach a pane that is ALREADY FOCUSED. To open one, click: the
    # Cobblestone pill opens the start menu and a row opens its app, which
    # needs -mouse rather than -keys-file (this script has no -Mouse; invoke
    # codex-vm directly with these same args plus -mouse-file).
    #
    # Two ADJACENT entries with the same code deliver as one: under
    # -hid-nak-unchanged an unchanged report is NAKed, so alternate codes
    # rather than repeating one to drive a pane.
    [string]$Keys = '',
    # Freeze the clock so a captured frame is comparable against a recorded
    # one; the taskbar paints the CMOS RTC and is otherwise host state.
    [string]$Rtc = '',
    # IDE disk image. Without one the Files pane says "no FAT ESP on the
    # boot medium" and desk-font falls back to the CBF face, both correctly.
    # seed/Codex.img is a real ESP and browses. codex-vm writes back and
    # flushes to the host, so this COPIES the image to build-output and
    # attaches the copy rather than letting a guest edit the depot artifact.
    # Defaulted rather than empty, and the pointer is why. With no disk the ESP
    # mount falls through to the USB medium, GopUsbMsc calls xhci-connect, and
    # that brings the controller up a SECOND time -- halt plus HCRST -- on the
    # controller usb-attach had already bound the keyboard and mouse to.
    # Measured: with no disk the guest resets the controller twice and then
    # rings no endpoint at all; with one it resets once and rings both (slot 5
    # ep 3, slot 2 ep 5) and the pointer moves. The keyboard survives either way
    # only because codex-vm keeps feeding the legacy mailbox, which is what hid
    # this for so long. That was WORKS-25, FIXED 2026-08-19 (msc-connect refuses
    # a second bring-up once usb-attach has walked); the default stays because
    # the Files pane wants an ESP to browse.
    [string]$Disk = 'seed/Codex.img',
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

# Staleness is judged against every chapter the desk can reach, not against
# DeskVm.codex alone. DeskVm is nine lines that call desk-run: it almost never
# changes, so a check that stats only the entry file serves the PREVIOUS binary
# after any edit to GopDesk or anything it cites, says "compiling" nowhere, and
# reports the old build's behaviour as the new build's. Measured 2026-08-15: six
# consecutive runs across two opposite versions of a pane produced frontier
# readings within 700 bytes of each other, because all six ran one binary.
$newest = Get-ChildItem -Path (Join-Path $Repo 'apps/works'), (Join-Path $Repo 'codex') `
              -Filter '*.codex' -Recurse -File |
          Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1

$stale = $Force -or -not (Test-Path $Out) -or `
         ($newest -and $newest.LastWriteTimeUtc -gt (Get-Item $Out).LastWriteTimeUtc)

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

# -hid-combo, always. Without it codex-vm's HID device carries ONE interface, a
# boot keyboard, so usb-attach finds no pointer at all: the Monitor pane reads
# "mouse no" and the window's mouse events have nothing to be delivered on. With
# it the same pane reads "mouse yes". Two interfaces on one device is also the
# shape the ASUS answered on 2026-08-04, so this is the bed matching the metal.
$vmArgs = @('-kernel', $Out, '-gop-width', $Width, '-gop-height', $Height, '-mem', $Mem, '-hid-combo')

if ($Keys) {
    $keyFile = Join-Path $outDir 'desk-keys.txt'
    Set-Content -Path $keyFile -Value ($Keys -replace ';', "`n")
    # -hid-nak-unchanged: under the default HID model a keystroke narrower
    # than the guest's poll interval does not exist (reek's measured table,
    # OperatorsManual). Every bed that injects keys carries the flag.
    $vmArgs += @('-keys-file', $keyFile, '-hid-nak-unchanged')
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
Write-Host "[desk] the Cobblestone button opens the start menu; an app opens in a window you move by its title bar and close with the x."
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
