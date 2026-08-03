# Boot a disk image under REAL UEFI firmware (edk2/OVMF) in QEMU and screenshot
# the result. This is the faithful boot test codex-vm cannot be (its firmware is
# a lenient fake). Use it to validate a boot image before flashing hardware.
#
#   pwsh build/boot/test-ovmf.ps1 -Img build/boot/optiona.img -Out build/boot/ovmf.png [-Seconds 14] [-Keys "80,80,28"]
[CmdletBinding()]
param(
    [string]$Img = 'build/boot/optiona.img',
    [string]$Out = 'build/boot/ovmf.png',
    [int]$Seconds = 14,
    [string]$Keys = '',
    [int]$AfterKeys = 2,
    # Gap between sendkey lines. The boot payload's keyboard input is a
    # one-scancode mailbox by design: a key struck while the guest is busy
    # (ed25519 keygen under TCG takes seconds) is dropped, not replayed into
    # the next screen. A human presses keys when a prompt is on the glass;
    # this script fires on a wall clock, so it must pace itself slower than
    # the longest busy stretch to imitate one.
    [int]$KeyDelayMs = 250,
    # 'q35' is the modern default (AHCI, no legacy IDE). Use 'pc' to give the
    # payload a real legacy IDE controller at 0x1F0 for the post-EBS PIO path.
    [string]$Machine = 'q35',
    # -UsbDisk attaches the image as USB mass storage on a qemu-xhci
    # controller instead of IDE/AHCI -- the real-hardware topology, where
    # the boot medium is reachable only through the USB stack. QEMU's
    # xHCI is a spec-strict implementation: this is the verdict bed for
    # the post-EBS USB drivers (GopXhci/GopUsbMsc).
    [switch]$UsbDisk,
    # -UsbKbd attaches a USB HID keyboard to the same qemu-xhci controller --
    # the verdict bed for the post-EBS HID transport (GopUsbKbd). -Keys
    # sendkey lines then arrive as interrupt IN boot reports, not PS/2.
    [switch]$UsbKbd,
    # -NoPs2 removes the i8042 controller (q35 only): the honest model of a
    # modern machine, where firmware keyboard emulation dies at EBS and the
    # USB HID path is the ONLY input. Combine with -UsbKbd.
    [switch]$NoPs2,
    # -UsbMouse attaches a USB HID boot-protocol mouse to the same xHCI
    # controller -- the verdict bed for GopUsbMouse. Drive it with
    # -MouseCmds: semicolon-separated QEMU monitor lines sent AFTER the
    # -Keys sequence, e.g. 'mouse_move 0 -60;mouse_button 1;mouse_button 0'.
    [switch]$UsbMouse,
    [string]$MouseCmds = '',
    # -UsbHub interposes a hub: root port 1 holds a usb-hub and the keyboard
    # attaches BEHIND it (port 1.1), the topology of a real laptop's internal
    # wiring. The disk (with -UsbDisk) stays on root port 2. Verdict bed for
    # the hub enumeration in GopUsb (route strings, port power/reset).
    [switch]$UsbHub,
    # -Decoy attaches a second raw image as a SATA disk: the "internal drive"
    # of a real machine, which answers on AHCI before the boot stick answers
    # on USB. Verdict bed for medium selection (GopMedium) -- the payload
    # must read the medium carrying its own CODEX.CDX, not this one.
    [string]$Decoy = '',
    # -NvmeDisk attaches the image as an NVMe namespace instead of SATA --
    # the modern-laptop topology, where the internal disk is reachable only
    # through the NVMe queues. Verdict bed for GopNvme.
    [switch]$NvmeDisk,
    # -MonCmds: semicolon-separated QEMU monitor lines sent just before the
    # screendump, with the REPLY PRINTED. -MouseCmds already sent arbitrary
    # monitor lines but threw the answer away, so the monitor's query commands
    # were unreachable from this script: `info registers` at a spin or a halt
    # reads out the state a payload with no serial port cannot tell you, and
    # `xp/4gx <addr>` reads memory the payload never printed.
    [string]$MonCmds = '',
    # -NecXhci uses QEMU's nec-usb-xhci controller model instead of the
    # default qemu-xhci: a different xHCI implementation (different PCI id,
    # capability layout, port count) that catches controller-model
    # assumptions in GopXhci. A second opinion on the bring-up until a real
    # Intel controller is in hand.
    [switch]$NecXhci
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$qemu = "D:\Program Files\qemu\qemu-system-x86_64.exe"
$code = "D:\Program Files\qemu\share\edk2-x86_64-code.fd"
$varsSrc = "D:\Program Files\qemu\share\edk2-i386-vars.fd"
foreach ($f in $qemu,$code) { if (-not (Test-Path $f)) { throw "missing: $f" } }

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$imgAbs = if ([System.IO.Path]::IsPathRooted($Img)) { $Img } else { Join-Path $repo $Img }
$outAbs = if ([System.IO.Path]::IsPathRooted($Out)) { $Out } else { Join-Path $repo $Out }
$scratch = $env:TEMP   # no spaces (C:\Users\...\AppData\Local\Temp)
# EVERY name below and the monitor port are per-WORKSPACE. They used to be
# fixed, and $env:TEMP is per-USER while the whole fleet runs as one user, so
# two agents gating at once shared one disk file and one monitor socket: the
# second Copy-Item replaced the first agent's image under its running QEMU,
# and Send-Mon's screendump went to whichever VM owned the port. Measured
# 2026-07-29 by handing this script a PciProbe image and photographing another
# agent's GopBoot welcome screen. A gate that can boot someone else's artifact
# and report it as yours is worse than no gate.
$tag = (Split-Path $repo -Leaf) -replace '[^A-Za-z0-9]',''
$imgCopy = Join-Path $scratch "ovmf-disk-$tag.img"
$varsCopy = Join-Path $scratch "ovmf-vars-$tag.fd"
$codeCopy = Join-Path $scratch "ovmf-code-$tag.fd"
$ppm = Join-Path $scratch "ovmf-shot-$tag.ppm"
$ser = Join-Path $scratch "ovmf-serial-$tag.log"
$errLog = Join-Path $scratch "qemu-err-$tag.log"
Copy-Item $imgAbs $imgCopy -Force
Copy-Item $code $codeCopy -Force            # firmware path has spaces; copy to space-free temp
if (Test-Path $varsSrc) { Copy-Item $varsSrc $varsCopy -Force }
$code = $codeCopy
Remove-Item $ppm,$ser -ErrorAction SilentlyContinue

# Deterministic per-workspace port in 55700..55899, so a stray QEMU is
# attributable to the agent that left it rather than anonymous.
$portHash = 0
foreach ($ch in $tag.ToCharArray()) { $portHash = ($portHash * 31 + [int]$ch) % 200 }
$monPort = 55700 + $portHash
$inUse = @(Get-NetTCPConnection -LocalPort $monPort -State Listen -ErrorAction SilentlyContinue)
if ($inUse.Count -gt 0) {
    throw "monitor port $monPort (workspace '$tag') is already listening, PID $($inUse[0].OwningProcess). A previous run of THIS workspace is still alive. Refusing to start: continuing would screendump that VM and report its screen as this run's result."
}
$machineArg = if ($NoPs2) { "$Machine,i8042=off" } else { $Machine }
# pflash ORDER MATTERS: OVMF expects CODE at unit 0 and VARS at unit 1.
$qargs = @(
    '-accel','tcg','-m','2048','-machine',$machineArg,
    '-drive', "if=pflash,format=raw,unit=0,readonly=on,file=$code",
    '-drive', "if=pflash,format=raw,unit=1,file=$varsCopy"
)
if ($UsbDisk -or $UsbKbd -or $UsbMouse) {
    $xhciModel = if ($NecXhci) { 'nec-usb-xhci' } else { 'qemu-xhci' }
    $qargs += @('-device',"$xhciModel,id=xhci")
}
if ($UsbHub) {
    $qargs += @('-device','usb-hub,bus=xhci.0,port=1')
}
if ($UsbDisk) {
    $stickPort = if ($UsbHub) { 'port=2,' } else { '' }
    $qargs += @(
        '-drive', "if=none,id=stick,format=raw,file=$imgCopy",
        '-device',"usb-storage,bus=xhci.0,${stickPort}drive=stick"
    )
} elseif ($NvmeDisk) {
    $qargs += @(
        '-drive', "if=none,id=nvst,format=raw,file=$imgCopy",
        '-device','nvme,drive=nvst,serial=codex1'
    )
} else {
    # With a decoy present the decoy takes SATA index 0 -- the position an
    # internal drive holds on a real machine -- and the boot image sits
    # behind it, so the probe must SKIP the first disk to find its medium.
    $imgIndex = if ($Decoy) { 1 } else { 0 }
    $qargs += @('-drive', "format=raw,file=$imgCopy,if=ide,index=$imgIndex")
}
if ($Decoy) {
    $decoyAbs = if ([System.IO.Path]::IsPathRooted($Decoy)) { $Decoy } else { Join-Path $repo $Decoy }
    $decoyCopy = Join-Path $scratch "ovmf-decoy-$tag.img"
    Copy-Item $decoyAbs $decoyCopy -Force
    $qargs += @('-drive', "format=raw,file=$decoyCopy,if=ide,index=0")
}
if ($UsbKbd) {
    $kbdPort = if ($UsbHub) { ',port=1.1' } else { '' }
    $qargs += @('-device',"usb-kbd,bus=xhci.0$kbdPort")
}
if ($UsbMouse) {
    $qargs += @('-device','usb-mouse,bus=xhci.0')
}
$qargs += @(
    '-serial', "file:$ser",
    '-monitor', "tcp:127.0.0.1:$monPort,server,nowait",
    '-display','none','-vga','std','-rtc','base=utc'
)
if ($Keys) {
    # send scancodes via QMP later; simpler: use sendkey through monitor after boot
}

Write-Host "[ovmf] booting $imgAbs ..."
$proc = Start-Process -FilePath $qemu -ArgumentList $qargs -PassThru -WindowStyle Hidden -RedirectStandardError $errLog
Start-Sleep -Seconds 3
if ($proc.HasExited) {
    Write-Host "[ovmf] QEMU exited early. stderr:"
    Get-Content $errLog -ErrorAction SilentlyContinue | Select-Object -First 20 | ForEach-Object { Write-Host "  $_" }
    return
}

# Optional keystrokes via monitor sendkey (Set-1 scancode names differ; use qcodes)
function Send-Mon($lines, $gapMs = 250, [switch]$Echo) {
    $c = [System.Net.Sockets.TcpClient]::new('127.0.0.1',$monPort)
    $s = $c.GetStream(); $s.ReadTimeout = 1000
    Start-Sleep -Milliseconds 200
    foreach ($ln in $lines) {
        $b = [System.Text.Encoding]::ASCII.GetBytes($ln + "`n"); $s.Write($b,0,$b.Length); $s.Flush(); Start-Sleep -Milliseconds $gapMs
    }
    # Drain until the socket goes quiet rather than taking one 4 KB bite: an
    # `info registers` reply arrives in several segments and a single Read
    # truncates it mid-register.
    $out = ''
    try {
        $buf = New-Object byte[] 8192
        for ($i = 0; $i -lt 12; $i++) {
            $n = $s.Read($buf,0,$buf.Length)
            if ($n -le 0) { break }
            $out += [System.Text.Encoding]::ASCII.GetString($buf,0,$n)
        }
    } catch {}
    $c.Close()
    if ($Echo) { return $out }
}

Start-Sleep -Seconds ([Math]::Max(1,$Seconds-3))
if ($Keys) {
    # Set-1 make codes -> QEMU qcodes. Letters/digits cover the wizard's
    # text fields; sendkey emits make+break, so pass make codes only.
    $qmap = @{ '72'='up'; '80'='down'; '28'='ret'; '1'='esc'; '14'='backspace'; '57'='spc';
               '2'='1'; '3'='2'; '4'='3'; '5'='4'; '6'='5'; '7'='6'; '8'='7'; '9'='8'; '10'='9'; '11'='0';
               '16'='q'; '17'='w'; '18'='e'; '19'='r'; '20'='t'; '21'='y'; '22'='u'; '23'='i'; '24'='o'; '25'='p';
               '30'='a'; '31'='s'; '32'='d'; '33'='f'; '34'='g'; '35'='h'; '36'='j'; '37'='k'; '38'='l';
               '44'='z'; '45'='x'; '46'='c'; '47'='v'; '48'='b'; '49'='n'; '50'='m' }
    $seq = @()
    foreach ($k in ($Keys -split ',')) { if ($qmap.ContainsKey($k.Trim())) { $seq += "sendkey $($qmap[$k.Trim()])" } }
    if ($seq) { Send-Mon $seq $KeyDelayMs; Start-Sleep -Seconds $AfterKeys }
}
if ($MouseCmds) {
    Send-Mon ($MouseCmds -split ';') 600
    Start-Sleep -Seconds 2
}
if ($MonCmds) {
    Write-Host "[ovmf] --- monitor ---"
    $reply = Send-Mon ($MonCmds -split ';') 400 -Echo
    ($reply -split "`r?`n") | ForEach-Object { Write-Host "  $_" }
}
Send-Mon @("screendump $ppm")
Start-Sleep -Milliseconds 800
try { if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force } } catch {}

if (Test-Path $ppm) {
    & (Join-Path $PSScriptRoot 'ppm2png.ps1') $ppm $outAbs
    Write-Host "[ovmf] screenshot -> $outAbs"
} else {
    Write-Host "[ovmf] no screendump produced"
}
Write-Host "[ovmf] --- serial log (first 30 lines) ---"
Get-Content $ser -ErrorAction SilentlyContinue | Select-Object -First 30 | ForEach-Object { Write-Host "  $_" }
