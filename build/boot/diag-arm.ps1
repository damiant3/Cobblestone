# Rehearse the diagnostic stick: boot the EXACT image that would fly, in
# codex-vm and under OVMF, and require every channel to agree
# (docs/Designs/Active/OS/DiagnosticStick.md, "The bank path has a permanent
# runner"). Every arm here is a control as much as a check: the ones that must
# reach `bank=none` are what show the bank rows can say no.
#
#   build/boot/diag-arm.ps1                 # every arm
#   build/boot/diag-arm.ps1 -Only pass      # one arm
#   build/boot/diag-arm.ps1 -SkipOvmf       # codex-vm arms only (no QEMU)
#   build/boot/diag-arm.ps1 -Keep           # leave the working images
#
# Arms, and what each requires:
#
#   pass       codex-vm, image as boot medium and USB disk. Serial block from
#              `DIAG1` to `END` == DIAG.TXT read back off the disk image, row
#              for row; bank=ok; both stages reach a state; ends in END.
#   no-medium  codex-vm with no -disk at all: no USB mass-storage device, IDE
#              absent, so nothing carries an ESP. Ladder must still reach the
#              summary and say bank=none naming the mount stage.
#              (-usb-bot-drop 1 was tried first for this and is NOT a no-bank
#              arm any more: the MSC driver's recovery path re-issues the
#              transfer and the bank lands. Measured 2026-08-18.)
#   no-smbios  codex-vm -no-smbios: the ConfigurationTable carries no SMBIOS
#              entry; smbios=no-table and the box row says unnamed.
#   no-edid    codex-vm -no-edid: LocateProtocol finds no EDID; edid=absent.
#   edid-bad   codex-vm -edid-bad: the EDID checksum is wrong; edid=bad-checksum.
#   fat-full   codex-vm with every free cluster marked bad on the disk copy:
#              the mount and the DIAG.ID lock succeed and the WRITE is refused.
#              bank=none naming the write stage; no DIAG.TXT. (A read-only host
#              file does NOT force this: codex-vm serves the guest from memory
#              and only the flush fails, disk-arm.ps1 learned that.)
#   cfg-off    a second image built with `scene off` in the stub ring: the
#              scene stage reports skipped and the ladder banks the rest.
#   esp-cfg    a second image carrying DIAG.CFG on the ESP: the payload reads
#              it after the bank opens and reports cfg-file=1.
#   block-oob  a second image whose DIAG.CFG says lock lba=999999999: the
#              block stage's one-sector write is aimed past the medium and the
#              device refuses it; block=write-refused, every other stage as
#              in pass. The forced-failure arm of the write-side stage.
#   sink-shift a second image whose DIAG.CFG says sink shift=1: the 2.7 MB
#              write lands and the streaming verify compares against a pattern
#              shifted by one, so every byte is reported bad; sink=bad-bytes,
#              block and the rest as in pass. The oracle proving it can say no.
#   nic-pass   codex-vm -e1000 -e1000-nat: the bed has NO Intel card unless asked, so every
#              other arm reads nicsit=no-part (dim, honest); with the card the
#              stage reads it (verdict ok, registers, the poll calibration) and
#              says ok; nicinit ok; the NAT answers the ring stage's ARP so it
#              reads frames. The card's own fault switches are the arms for the
#              init and ring stages.
#   nic-nolink codex-vm -e1000-no-link: STATUS.LU never sets; nicinit=no-link (the
#              step durations still bank), nicring=quiet, nicsit ok.
#   nic-nomac  codex-vm -e1000-no-mac: RAL/RAH empty; nicinit=no-mac, nicsit ok.
#              nicring's own read of the same card is not asserted here (init
#              without a MAC still brings the receiver up on the model).
#   ovmf       QEMU + OVMF, the image on qemu-xhci usb-storage. Serial == bank,
#              bank=ok, and the summary QR decodes off the screendump
#              (tools/qr-read.ps1) to a body starting DIAG1;.
#   ovmf-ro    the same with the drive readonly=on: usb-storage refuses the
#              write; bank=none and the QR still decodes.
#
# The image is not rebuilt here. Rebuild with build/boot/build-diag.ps1 and
# this refuses to calibrate an image older than its sources.
[CmdletBinding()]
param(
    [string]$Img = 'build/boot/diag.img',
    [string]$Only = '',
    [switch]$Keep,
    [switch]$SkipOvmf,
    # codex-vm reaches END inside two seconds (measured 2026-08-18); the deadline
    # is only the backstop for a wedged arm, since the payload holds forever.
    [int]$Seconds = 30,
    [int]$OvmfSeconds = 100
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$Vm   = Join-Path $Repo 'tools\codex-vm.exe'
$ImgAbs = if ([IO.Path]::IsPathRooted($Img)) { $Img } else { Join-Path $Repo $Img }
foreach ($f in @($Vm, $ImgAbs)) {
    if (-not (Test-Path -PathType Leaf $f)) { Write-Host "FAIL: $f missing"; exit 1 }
}
$Efi = Join-Path $Repo 'build-output\diag.efi'

# REFUSE TO CALIBRATE A STALE IMAGE (ladder-arm.ps1's rule): a pass has to be a
# pass for the source on disk.
$imgTime = (Get-Item $ImgAbs).LastWriteTimeUtc
foreach ($s in @(Get-ChildItem (Join-Path $Repo 'build\boot\diag') -Filter 'Diag*.codex' -File)) {
    if ($s.LastWriteTimeUtc -gt $imgTime) {
        Write-Host "STALE: $($s.Name) is newer than $(Split-Path $ImgAbs -Leaf). Rebuild first: build/boot/build-diag.ps1"
        exit 1
    }
}

& pwsh -NoProfile -File (Join-Path $Repo 'build\check-diag-verdicts.ps1') | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host 'FAIL: check-diag-verdicts is red; a state word has no verdict row'; exit 1 }

# Derived from the workspace, never a fixed path (L-SHARED).
$Work = Join-Path ([IO.Path]::GetTempPath()) ("diag-arm-" + (Split-Path $Repo -Leaf))
New-Item -ItemType Directory -Force $Work | Out-Null

function New-Copy([string]$name, [string]$from = $ImgAbs) {
    $dst = Join-Path $Work $name
    Copy-Item $from $dst -Force
    Set-ItemProperty $dst -Name IsReadOnly -Value $false
    return $dst
}

# Boot in codex-vm with the image as both the boot medium (-kernel, whose
# BOOTX64.EFI the fake firmware runs) and the disk (-disk, which the xHCI
# mass-storage model serves and IDE answers as well). Wait, then read: the
# guest's serial goes to -output, flushed twice a second, and the payload
# holds its page in a spin the codex-vm watchdog ends on its own; the
# deadline is the backstop.
function Invoke-Vm([string]$name, [string]$kernel, [string]$disk, [string[]]$extra) {
    $out = Join-Path $Work "$name.out"
    $err = Join-Path $Work "$name.err"
    Remove-Item $out, $err -ErrorAction SilentlyContinue
    $a = @('-kernel', $kernel, '-uefi', '-headless', '-output', $out)
    if ($disk) { $a += @('-disk', $disk) }
    if ($extra) { $a += $extra }
    $p = Start-Process -FilePath $Vm -ArgumentList $a -NoNewWindow -PassThru `
            -RedirectStandardError $err -RedirectStandardOutput (Join-Path $Work "$name.stdout")
    $deadline = (Get-Date).AddSeconds($Seconds)
    while (-not $p.HasExited -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
    if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Milliseconds 600
    if (Test-Path $out) { return @(Get-Content $out -ErrorAction SilentlyContinue) }
    return @()
}

# The serial block: from the DIAG1 identity row to the END that follows the
# summary. The stub writes its own liveness marks to COM1 first, so the DIAG1
# row is looked for anywhere in its line rather than at column 0.
function Get-DiagBlock([string[]]$lines) {
    $block = @()
    $inBlock = $false
    foreach ($l in $lines) {
        if (-not $inBlock) {
            $i = $l.IndexOf('DIAG1 ')
            if ($i -ge 0) { $inBlock = $true; $block += $l.Substring($i) }
            continue
        }
        $block += $l
        if ($l -eq 'END') { break }
    }
    return ,$block
}

function Read-Bank([string]$disk, [string]$name) {
    $dir = Join-Path $Work "$name-bank"
    Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
    $r = & pwsh -NoProfile -File (Join-Path $Repo 'build\read-stick.ps1') -ImageFile $disk -Name 'DIAG.TXT' -OutDir $dir 2>&1
    $f = Join-Path $dir 'DIAG.TXT'
    if (Test-Path $f) { return @(Get-Content $f) }
    return $null
}

# Live progress lines ("<stage> entering <step>") are serial-only BY DESIGN: a
# stage that hangs inside a step cannot bank, so the last such line on the wire
# is the L-STATES row that names the step; they are not rows of the record and
# the compare skips them.
function Compare-Rows([string[]]$a, [string[]]$b) {
    if ($null -eq $a -or $null -eq $b) { return 'one side missing' }
    $a = @($a | Where-Object { $_ -notmatch '^[a-z0-9]+ entering ' })
    $b = @($b | Where-Object { $_ -notmatch '^[a-z0-9]+ entering ' })
    if ($a.Count -ne $b.Count) { return "row count $($a.Count) vs $($b.Count)" }
    for ($i = 0; $i -lt $a.Count; $i++) {
        if ($a[$i] -ne $b[$i]) { return "row $i differs: [$($a[$i])] vs [$($b[$i])]" }
    }
    return ''
}

function Field([string[]]$block, [string]$prefix) {
    $l = $block | Where-Object { $_.StartsWith($prefix) } | Select-Object -First 1
    if ($l) { return $l } else { return '' }
}

# Every free cluster marked bad, so gfat has nowhere to put DIAG.TXT. Copied
# from disk-arm.ps1's Set-FatFull; FAT16 only.
function Set-FatFull([string]$img) {
    $b = [IO.File]::ReadAllBytes($img)
    $p = 2048 * 512
    $bps = [BitConverter]::ToUInt16($b, $p + 11)
    $spc = $b[$p + 13]
    $rsvd = [BitConverter]::ToUInt16($b, $p + 14)
    $nfat = $b[$p + 16]
    $rootEnt = [BitConverter]::ToUInt16($b, $p + 17)
    $tot16 = [BitConverter]::ToUInt16($b, $p + 19)
    $fatSz = [BitConverter]::ToUInt16($b, $p + 22)
    $tot32 = [BitConverter]::ToUInt32($b, $p + 32)
    $total = if ($tot16 -ne 0) { [int]$tot16 } else { [int]$tot32 }
    $rootSectors = [int][math]::Ceiling(($rootEnt * 32) / $bps)
    $dataSectors = $total - ($rsvd + $nfat * $fatSz + $rootSectors)
    $clusters = [int][math]::Floor($dataSectors / $spc)
    $fatOff = $p + $rsvd * $bps
    $marked = 0
    for ($c = 2; $c -lt $clusters + 2; $c++) {
        $e = $fatOff + $c * 2
        if ($b[$e] -eq 0 -and $b[$e + 1] -eq 0) { $b[$e] = 0xF7; $b[$e + 1] = 0xFF; $marked++ }
    }
    for ($f = 1; $f -lt $nfat; $f++) {
        [Array]::Copy($b, $fatOff, $b, $fatOff + $f * $fatSz * $bps, $fatSz * $bps)
    }
    [IO.File]::WriteAllBytes($img, $b)
    return $marked
}

# A variant image from the stashed .efi with a different ring or an ESP file:
# same payload bytes, different stub or ESP.
function New-Variant([string]$name, [string]$stdinCfg, [string]$cfgFile) {
    if (-not (Test-Path $Efi)) { return '' }
    $idFile = Join-Path $Repo 'build-output\DIAG.ID'
    if (-not (Test-Path $idFile)) { return '' }
    $id = (Get-Content $idFile -Raw).Trim()
    $recipe = Join-Path $Repo 'build-output\diag-recipe.txt'
    $kernel = 'unknown'
    if (Test-Path $recipe) {
        $kl = Get-Content $recipe | Where-Object { $_.StartsWith('kernel=') } | Select-Object -First 1
        if ($kl) { $kernel = $kl.Substring(7) }
    }
    $cdx = Join-Path $Repo 'build-output\diag.cdx'
    $efi = Join-Path $Work "$name.efi"
    $stdin = "id $id`nkernel $kernel`n"
    if ($stdinCfg) { $stdin += $stdinCfg + "`n" }
    & pwsh -NoProfile -File (Join-Path $Repo 'build\cdx-to-pe.ps1') -CdxInput $cdx -Out $efi -HeapPages 32768 -ExitBootServices -Stdin $stdin 2>&1 | Out-Null
    if (-not (Test-Path $efi)) { return '' }
    $img = Join-Path $Work "$name.img"
    $extra = @("DIAG.ID=$idFile")
    $rcp = Join-Path $Repo 'build-output\DIAG.RCP'
    if (Test-Path $rcp) { $extra += "DIAG.RCP=$rcp" }
    if ($cfgFile) { $extra += "DIAG.CFG=$cfgFile" }
    & pwsh -NoProfile -File (Join-Path $Repo 'build\build-img.ps1') -PeInput $efi -Out $img -TotalSectors 32768 -Extra ($extra -join ';') 2>&1 | Out-Null
    if (-not (Test-Path $img)) { return '' }
    Set-ItemProperty $img -Name IsReadOnly -Value $false
    return $img
}

# Under OVMF (test-ovmf.ps1) the image rides qemu-xhci usb-storage, the serial
# lands in $env:TEMP\ovmf-serial-<tag>.log and the guest's writes land in the
# per-workspace disk copy $env:TEMP\ovmf-disk-<tag>.img, both named the way that
# script names them (L-SHARED). The screendump is what the QR is read from:
# tools/qr-read.ps1 is the decoder half of GopQr and its REPORT must start with
# the summary body's DIAG1; token.
function Invoke-Ovmf([string]$name, [bool]$readOnly) {
    $qemu = "D:\Program Files\qemu\qemu-system-x86_64.exe"
    if (-not (Test-Path $qemu)) { return '(skipped: QEMU not installed)' }
    $tag = (Split-Path $Repo -Leaf) -replace '[^A-Za-z0-9]',''
    $ser = Join-Path $env:TEMP "ovmf-serial-$tag.log"
    $disk = Join-Path $env:TEMP "ovmf-disk-$tag.img"
    $png = Join-Path $Work "$name.png"
    Remove-Item $ser, $png -ErrorAction SilentlyContinue
    $a = @('-Img', $ImgAbs, '-Out', $png, '-UsbDisk', '-Seconds', $OvmfSeconds)
    if ($readOnly) { $a += '-ReadOnlyDisk' }
    $log = & pwsh -NoProfile -File (Join-Path $Repo 'build\boot\test-ovmf.ps1') @a 2>&1
    $log | ForEach-Object { Write-Host "  $_" } | Out-Null
    if (-not (Test-Path $ser)) { return '(no serial log from OVMF)' }
    $lines = @(Get-Content $ser)
    $block = Get-DiagBlock $lines
    if ($block.Count -eq 0) { return '(no DIAG1 row on serial)' }
    if ($block[-1] -ne 'END') { return "(serial block did not reach END; last: $($block[-1]))" }
    foreach ($st in @('smbios', 'edid', 'cpu', 'pci', 'scene', 'block', 'sink', 'nicsit', 'nicinit', 'nicring')) { if (-not (Field $block "stage=$st ")) { return "(no $st stage row)" } }
    $bank = Field $block 'bank='
    $file = $null
    if (Test-Path $disk) { $file = Read-Bank $disk $name }
    if (-not $readOnly) {
        if (-not $bank.StartsWith('bank=ok')) { return "bank row is [$bank]" }
        if ($null -eq $file) { return 'bank=ok but no DIAG.TXT on the disk copy' }
        $d = Compare-Rows $block $file
        if ($d) { return "serial vs file: $d" }
    } else {
        if (-not $bank.StartsWith('bank=none')) { return "bank row is [$bank]" }
        if ($null -ne $file) { return 'bank=none but DIAG.TXT exists on the disk copy' }
    }
    if (-not (Test-Path $png)) { return '(no screendump)' }
    $qr = & pwsh -NoProfile -File (Join-Path $Repo 'tools\qr-read.ps1') -Path $png 2>&1
    $report = @($qr | ForEach-Object { "$_" })
    $i = [Array]::IndexOf($report, '================ REPORT ================')
    if ($i -lt 0) { return "QR did not decode: $($report | Where-Object { $_ -like '*[qr]*' } | Select-Object -Last 1)" }
    $body = $report[$i + 1]
    if (-not $body.StartsWith('DIAG1;')) { return "QR body is [$body]" }
    if ($report -match 'INCOMPLETE') { return 'QR decoded but INCOMPLETE' }
    return $expected[$name]
}

# The bed's own answers for every stage: codex-vm publishes SMBIOS (Codex
# Project Codex VM, its legacy 2.1 table plus a 3.0 entry), an EDID (CDX codex-vm dsp) and a hypervisor bit, so a passing
# boot there reads exactly this. The no-smbios/no-edid/edid-bad arms are the
# switches that show the three readers say no.
$bedStates = @{ smbios = 'ok'; edid = 'ok'; cpu = 'hypervisor'; pci = 'ok'; scene = 'rendered'; block = 'ok'; sink = 'ok'; nicsit = 'no-part'; nicinit = 'no-part'; nicring = 'no-part'; box = 'Codex Project Codex VM' }
# With no bank there is no medium selected, so the write-side stage says so and runs nothing.
$noBankStates = @{ smbios = 'ok'; edid = 'ok'; cpu = 'hypervisor'; pci = 'ok'; scene = 'rendered'; block = 'no-medium'; sink = 'no-medium'; nicsit = 'no-part'; nicinit = 'no-part'; nicring = 'no-part'; box = 'Codex Project Codex VM' }

$expected = [ordered]@{
    'pass'     = 'bank=ok serial==file every stage stated'
    'no-smbios' = 'smbios=no-table box=unnamed bank=ok'
    'no-edid'  = 'edid=absent bank=ok'
    'edid-bad' = 'edid=bad-checksum bank=ok'
    'no-medium' = 'bank=none (mount) summary reached, no file'
    'fat-full' = 'bank=none (write refused) summary reached, no file'
    'cfg-off'  = 'scene skipped, bank=ok'
    'esp-cfg'  = 'cfg-file=1, bank=ok'
    'block-oob' = 'block=write-refused (LBA past the medium), bank=ok'
    'sink-shift' = 'sink=bad-bytes (oracle shifted by one), bank=ok'
    'nic-pass'  = 'nicsit=ok nicinit=ok nicring=frames with -e1000 -e1000-nat (no card by default), bank=ok'
    'nic-nolink' = 'nicinit=no-link with -e1000-no-link; nicsit ok, nicring quiet, bank=ok'
    'nic-nomac' = 'nicinit=no-mac with -e1000-no-mac; nicsit ok, bank=ok'
    'ovmf'     = 'bank=ok serial==file QR decodes'
    'ovmf-ro'  = 'bank=none QR decodes'
}
$actual = [ordered]@{}
$names = if ($Only) { @($Only) } else { @($expected.Keys) }
foreach ($n in $names) { if (-not $expected.Contains($n)) { Write-Host "FAIL: no arm '$n'"; exit 1 } }
if ($SkipOvmf) { $names = @($names | Where-Object { -not $_.StartsWith('ovmf') }) }

function Judge-Vm([string]$name, [string[]]$lines, [string]$disk, [bool]$wantBank, [string]$bankNote, [hashtable]$states) {
    $block = Get-DiagBlock $lines
    if ($block.Count -eq 0) { return '(no DIAG1 row on serial)' }
    if ($block[-1] -ne 'END') { return "(serial block did not reach END; last: $($block[-1]))" }
    if (-not (Field $block 'summary run=')) { return '(no summary row)' }
    $bank = Field $block 'bank='
    $file = Read-Bank $disk $name
    if ($wantBank) {
        if (-not $bank.StartsWith('bank=ok')) { return "bank row is [$bank]" }
        if ($null -eq $file) { return 'bank=ok but no DIAG.TXT on the disk' }
        $d = Compare-Rows $block $file
        if ($d) { return "serial vs file: $d" }
    } else {
        if (-not $bank.StartsWith('bank=none')) { return "bank row is [$bank]" }
        if ($bankNote -and -not $bank.Contains($bankNote)) { return "bank note is [$bank], wanted $bankNote" }
        if ($null -ne $file) { return 'bank=none but DIAG.TXT exists on the disk' }
    }
    foreach ($st in @('smbios', 'edid', 'cpu', 'pci', 'scene', 'block', 'sink', 'nicsit', 'nicinit', 'nicring')) {
        $row = Field $block "stage=$st "
        if (-not $row) { return "(no $st stage row)" }
        if ($states.ContainsKey($st) -and -not $row.Contains("state=$($states[$st])")) { return "$st row is [$row]" }
    }
    if ($states.ContainsKey('box')) {
        $box = Field $block 'box='; if (-not $box.StartsWith('box=' + $states['box'])) { return "box row is [$box]" }
    }
    if ($name -eq 'esp-cfg' -and -not $bank.Contains('cfg-file=1')) { return "bank row is [$bank], wanted cfg-file=1" }
    return $expected[$name]
}

foreach ($name in $names) {
    Write-Host "[diag-arm] $name..."
    switch ($name) {
        'pass' {
            $k = New-Copy 'k-pass.img'
            $lines = Invoke-Vm 'pass' $k $k @()
            $actual['pass'] = Judge-Vm 'pass' $lines $k $true '' $bedStates
        }
        'no-smbios' {
            $k = New-Copy 'k-nosmbios.img'
            $lines = Invoke-Vm 'no-smbios' $k $k @('-no-smbios')
            $actual['no-smbios'] = Judge-Vm 'no-smbios' $lines $k $true '' @{ smbios = 'no-table'; box = 'unnamed'; edid = 'ok'; cpu = 'hypervisor'; pci = 'ok'; scene = 'rendered' }
        }
        'no-edid' {
            $k = New-Copy 'k-noedid.img'
            $lines = Invoke-Vm 'no-edid' $k $k @('-no-edid')
            $actual['no-edid'] = Judge-Vm 'no-edid' $lines $k $true '' @{ smbios = 'ok'; edid = 'absent'; cpu = 'hypervisor'; pci = 'ok'; scene = 'rendered' }
        }
        'edid-bad' {
            $k = New-Copy 'k-edidbad.img'
            $lines = Invoke-Vm 'edid-bad' $k $k @('-edid-bad')
            $actual['edid-bad'] = Judge-Vm 'edid-bad' $lines $k $true '' @{ smbios = 'ok'; edid = 'bad-checksum'; cpu = 'hypervisor'; pci = 'ok'; scene = 'rendered' }
        }
        'no-medium' {
            $k = New-Copy 'k-nomedium.img'
            $lines = Invoke-Vm 'no-medium' $k '' @()
            $actual['no-medium'] = Judge-Vm 'no-medium' $lines $k $false 'mount stage' $noBankStates
        }
        'fat-full' {
            $k = New-Copy 'k-fatfull.img'
            $marked = Set-FatFull $k
            Write-Host "  fat-full: $marked clusters marked bad"
            $lines = Invoke-Vm 'fat-full' $k $k @()
            $actual['fat-full'] = Judge-Vm 'fat-full' $lines $k $false 'write refused' $noBankStates
        }
        'cfg-off' {
            $k = New-Variant 'k-cfgoff' 'scene off' ''
            if (-not $k) { $actual['cfg-off'] = '(skipped: build-output/diag.efi, DIAG.ID or diag.cdx missing; run build-diag.ps1)' }
            else {
                $lines = Invoke-Vm 'cfg-off' $k $k @()
                $actual['cfg-off'] = Judge-Vm 'cfg-off' $lines $k $true '' @{ smbios = 'ok'; edid = 'ok'; cpu = 'hypervisor'; pci = 'ok'; scene = 'skipped' }
            }
        }
        'esp-cfg' {
            $cfg = Join-Path $Work 'DIAG.CFG'
            [IO.File]::WriteAllText($cfg, "pci on`n", [Text.ASCIIEncoding]::new())
            $k = New-Variant 'k-espcfg' '' $cfg
            if (-not $k) { $actual['esp-cfg'] = '(skipped: build-output/diag.efi, DIAG.ID or diag.cdx missing; run build-diag.ps1)' }
            else {
                $lines = Invoke-Vm 'esp-cfg' $k $k @()
                $actual['esp-cfg'] = Judge-Vm 'esp-cfg' $lines $k $true '' $bedStates
            }
        }
        'block-oob' {
            $cfg = Join-Path $Work 'DIAG-oob.CFG'
            [IO.File]::WriteAllText($cfg, "block lba=999999999`n", [Text.ASCIIEncoding]::new())
            $k = New-Variant 'k-blockoob' '' $cfg
            if (-not $k) { $actual['block-oob'] = '(skipped: build-output/diag.efi, DIAG.ID or diag.cdx missing; run build-diag.ps1)' }
            else {
                $lines = Invoke-Vm 'block-oob' $k $k @()
                $actual['block-oob'] = Judge-Vm 'block-oob' $lines $k $true '' @{ smbios = 'ok'; edid = 'ok'; cpu = 'hypervisor'; pci = 'ok'; scene = 'rendered'; block = 'write-refused'; sink = 'ok'; nicsit = 'no-part'; nicinit = 'no-part'; nicring = 'no-part' }
            }
        }
        'sink-shift' {
            $cfg = Join-Path $Work 'DIAG-shift.CFG'
            [IO.File]::WriteAllText($cfg, "sink shift=1`n", [Text.ASCIIEncoding]::new())
            $k = New-Variant 'k-sinkshift' '' $cfg
            if (-not $k) { $actual['sink-shift'] = '(skipped: build-output/diag.efi, DIAG.ID or diag.cdx missing; run build-diag.ps1)' }
            else {
                $lines = Invoke-Vm 'sink-shift' $k $k @()
                $actual['sink-shift'] = Judge-Vm 'sink-shift' $lines $k $true '' @{ smbios = 'ok'; edid = 'ok'; cpu = 'hypervisor'; pci = 'ok'; scene = 'rendered'; block = 'ok'; sink = 'bad-bytes'; nicsit = 'no-part'; nicinit = 'no-part'; nicring = 'no-part' }
            }
        }
        'nic-pass' {
            $k = New-Copy 'k-nicpass.img'
            $lines = Invoke-Vm 'nic-pass' $k $k @('-e1000', '-e1000-nat')
            $actual['nic-pass'] = Judge-Vm 'nic-pass' $lines $k $true '' @{ smbios = 'ok'; edid = 'ok'; cpu = 'hypervisor'; pci = 'ok'; scene = 'rendered'; block = 'ok'; sink = 'ok'; nicsit = 'ok'; nicinit = 'ok'; nicring = 'frames' }
        }
        'nic-nolink' {
            $k = New-Copy 'k-nicnolink.img'
            $lines = Invoke-Vm 'nic-nolink' $k $k @('-e1000-no-link')
            $actual['nic-nolink'] = Judge-Vm 'nic-nolink' $lines $k $true '' @{ smbios = 'ok'; edid = 'ok'; cpu = 'hypervisor'; pci = 'ok'; scene = 'rendered'; block = 'ok'; sink = 'ok'; nicsit = 'ok'; nicinit = 'no-link'; nicring = 'quiet' }
        }
        'nic-nomac' {
            $k = New-Copy 'k-nicnomac.img'
            $lines = Invoke-Vm 'nic-nomac' $k $k @('-e1000-no-mac')
            $actual['nic-nomac'] = Judge-Vm 'nic-nomac' $lines $k $true '' @{ smbios = 'ok'; edid = 'ok'; cpu = 'hypervisor'; pci = 'ok'; scene = 'rendered'; block = 'ok'; sink = 'ok'; nicsit = 'ok'; nicinit = 'no-mac' }
        }
        'ovmf' { $actual['ovmf'] = Invoke-Ovmf 'ovmf' $false }
        'ovmf-ro' { $actual['ovmf-ro'] = Invoke-Ovmf 'ovmf-ro' $true }
    }
}

$bad = 0
Write-Host ''
Write-Host 'arm        expected                                        actual'
Write-Host '---------  ----------------------------------------------  ------'
foreach ($name in $names) {
    $e = $expected[$name]; $a = $actual[$name]
    if ($a -ne $e) { $bad++ }
    $mark = if ($a -eq $e) { 'ok' } else { "MISMATCH: $a" }
    Write-Host ("{0,-10} {1,-46}  {2}" -f $name, $e, $mark)
}
Write-Host ''
if (-not $Keep) { Remove-Item (Join-Path $Work '*.img'), (Join-Path $Work '*.efi') -Force -ErrorAction SilentlyContinue }
if ($bad -gt 0) { Write-Host "DIAG LADDER NOT REHEARSED: $bad arm(s) disagree"; exit 1 }
Write-Host 'Diag ladder rehearsed: every arm answered as it should.'

# The rehearsal record (L-REHEARSE): flash-usb.ps1 -Rehearsed refuses any image
# whose SHA-256 is not on this list. Only a FULL run writes it -- every arm,
# both beds -- because "boot-and-read green" is not "mission green"; -Only and
# -SkipOvmf runs are dev loops and leave the record alone, and say so.
$record = Join-Path $Repo 'build\boot\diag.rehearsed'
if ($Only -or $SkipOvmf) {
    Write-Host "  (partial rehearsal: $(if ($Only) { "-Only $Only" } else { '-SkipOvmf' }); $record NOT updated -- run every arm to make this image flashable)"
} else {
    $imgHash = (Get-FileHash $ImgAbs -Algorithm SHA256).Hash
    $line = "$imgHash  $([DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ'))  arms=$($names.Count)  $(Split-Path $ImgAbs -Leaf)"
    $have = @(); if (Test-Path $record) { $have = @(Get-Content $record) }
    if ($have | Where-Object { $_ -like "$imgHash *" }) { Write-Host "  rehearsal record already carries $imgHash" }
    else {
        [IO.File]::AppendAllText($record, $line + "`n", [Text.ASCIIEncoding]::new())
        Write-Host "  rehearsal record: $record += $imgHash (flash-usb.ps1 -Rehearsed accepts this image now)"
    }
}
exit 0
