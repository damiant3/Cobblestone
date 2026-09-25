# Generate apps/c64/RomData.codex from the three C64 ROM images.
#
# The bare-metal machine pokes each ROM into memory at boot: BASIC at #A10000,
# KERNAL at #A12000, CHARGEN at #A14000. Each ROM is split into 200-byte
# functions of little-endian 32-bit words; a zero word is skipped because the
# region starts zeroed.
#
#   pwsh apps/c64/build-rom-data.ps1 -BasicRom <path> -KernalRom <path> -ChargenRom <path>
#   pwsh apps/c64/build-rom-data.ps1 -Disk apps/c64/c64-roms.disk
param(
    [string]$BasicRom,
    [string]$KernalRom,
    [string]$ChargenRom,
    [string]$Disk,
    [string]$Out = "apps/c64/RomData.codex"
)
$ErrorActionPreference = 'Stop'

if ($Disk) {
    $d = [System.IO.File]::ReadAllBytes($Disk)
    $basic = $d[0..8191]; $kernal = $d[8192..16383]; $chargen = $d[16384..20479]
} else {
    $basic = [System.IO.File]::ReadAllBytes($BasicRom)
    $kernal = [System.IO.File]::ReadAllBytes($KernalRom)
    $chargen = [System.IO.File]::ReadAllBytes($ChargenRom)
}
foreach ($p in @(@{N='BASIC';B=$basic;S=8192}, @{N='KERNAL';B=$kernal;S=8192}, @{N='CHARGEN';B=$chargen;S=4096})) {
    if ($p.B.Length -ne $p.S) { Write-Error "$($p.N) ROM is $($p.B.Length) bytes, expected $($p.S)"; exit 1 }
}

$sb = [System.Text.StringBuilder]::new()
function L([string]$s) { [void]$sb.Append($s); [void]$sb.Append("`r`n") }

L 'Chapter: RomData'
L ''
$roms = @(@{N='basic';T='BASIC ROM';Base=0xA10000;B=$basic}, @{N='kernal';T='KERNAL ROM';Base=0xA12000;B=$kernal}, @{N='chargen';T='CHARGEN ROM';Base=0xA14000;B=$chargen})
foreach ($r in $roms) {
    L ('Section: ' + $r.T)
    $chunks = [int][Math]::Ceiling($r.B.Length / 200)
    for ($j = 0; $j -lt $chunks; $j++) {
        $addr = '#' + ($r.Base + $j * 200).ToString('X6')
        L ('  init-' + $r.N + '-' + $j + ' : Integer -> Integer')
        L ('  init-' + $r.N + '-' + $j + ' (dummy) =')
        $k = 0
        $end = [Math]::Min(200, $r.B.Length - $j * 200)
        for ($o = 0; $o -lt $end; $o += 4) {
            $i = $j * 200 + $o
            $w = [uint32]$r.B[$i] -bor ([uint32]$r.B[$i+1] -shl 8) -bor ([uint32]$r.B[$i+2] -shl 16) -bor ([uint32]$r.B[$i+3] -shl 24)
            if ($w -eq 0) { continue }
            $lead = if ($k -eq 0) { '    let' } else { '    in let' }
            L ($lead + ' d' + $k + ' = poke-32 ' + $addr + ' ' + $o + ' ' + $w)
            $k++
        }
        L '    in d0'
    }
    L ''
    L ('  init-' + $r.N + '-rom : Integer -> Integer')
    L ('  init-' + $r.N + '-rom (dummy) = ' + ((0..($chunks - 1) | ForEach-Object { 'init-' + $r.N + '-' + $_ + ' 0' }) -join ' + '))
    L ''
}
L '  init-all-roms : Integer -> Integer'
L '  init-all-roms (dummy) = init-basic-rom 0 + init-kernal-rom 0 + init-chargen-rom 0'
[System.IO.File]::WriteAllText($Out, $sb.ToString(), [System.Text.UTF8Encoding]::new($false))
Write-Host "RomData: $Out ($((Get-Item $Out).Length) bytes)"