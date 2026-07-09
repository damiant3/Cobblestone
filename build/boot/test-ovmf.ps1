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
    # 'q35' is the modern default (AHCI, no legacy IDE). Use 'pc' to give the
    # payload a real legacy IDE controller at 0x1F0 for the post-EBS PIO path.
    [string]$Machine = 'q35'
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
$imgCopy = Join-Path $scratch 'ovmf-disk.img'
$varsCopy = Join-Path $scratch 'ovmf-vars.fd'
$codeCopy = Join-Path $scratch 'ovmf-code.fd'
$ppm = Join-Path $scratch 'ovmf-shot.ppm'
$ser = Join-Path $scratch 'ovmf-serial.log'
Copy-Item $imgAbs $imgCopy -Force
Copy-Item $code $codeCopy -Force            # firmware path has spaces; copy to space-free temp
if (Test-Path $varsSrc) { Copy-Item $varsSrc $varsCopy -Force }
$code = $codeCopy
Remove-Item $ppm,$ser -ErrorAction SilentlyContinue

$monPort = 55700
# pflash ORDER MATTERS: OVMF expects CODE at unit 0 and VARS at unit 1.
$qargs = @(
    '-accel','tcg','-m','2048','-machine',$Machine,
    '-drive', "if=pflash,format=raw,unit=0,readonly=on,file=$code",
    '-drive', "if=pflash,format=raw,unit=1,file=$varsCopy",
    '-drive', "format=raw,file=$imgCopy,if=ide,index=0",
    '-serial', "file:$ser",
    '-monitor', "tcp:127.0.0.1:$monPort,server,nowait",
    '-display','none','-vga','std','-rtc','base=utc'
)
if ($Keys) {
    # send scancodes via QMP later; simpler: use sendkey through monitor after boot
}

Write-Host "[ovmf] booting $imgAbs ..."
$proc = Start-Process -FilePath $qemu -ArgumentList $qargs -PassThru -WindowStyle Hidden -RedirectStandardError (Join-Path $scratch 'qemu-err.log')
Start-Sleep -Seconds 3
if ($proc.HasExited) {
    Write-Host "[ovmf] QEMU exited early. stderr:"
    Get-Content (Join-Path $scratch 'qemu-err.log') -ErrorAction SilentlyContinue | Select-Object -First 20 | ForEach-Object { Write-Host "  $_" }
    return
}

# Optional keystrokes via monitor sendkey (Set-1 scancode names differ; use qcodes)
function Send-Mon($lines) {
    $c = [System.Net.Sockets.TcpClient]::new('127.0.0.1',$monPort)
    $s = $c.GetStream(); $s.ReadTimeout = 1000
    Start-Sleep -Milliseconds 200
    foreach ($ln in $lines) {
        $b = [System.Text.Encoding]::ASCII.GetBytes($ln + "`n"); $s.Write($b,0,$b.Length); $s.Flush(); Start-Sleep -Milliseconds 250
    }
    try { $buf = New-Object byte[] 4096; $s.Read($buf,0,4096) | Out-Null } catch {}
    $c.Close()
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
    if ($seq) { Send-Mon $seq; Start-Sleep -Seconds $AfterKeys }
}
Send-Mon @("screendump $ppm")
Start-Sleep -Milliseconds 800
try { if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force } } catch {}

if (Test-Path $ppm) {
    python (Join-Path $PSScriptRoot 'ppm2png.py') $ppm $outAbs
    Write-Host "[ovmf] screenshot -> $outAbs"
} else {
    Write-Host "[ovmf] no screendump produced"
}
Write-Host "[ovmf] --- serial log (first 30 lines) ---"
Get-Content $ser -ErrorAction SilentlyContinue | Select-Object -First 30 | ForEach-Object { Write-Host "  $_" }
