# Convert a CDX binary to a PE32+ UEFI application.
#
# Usage: cdx-to-pe.ps1 -CdxInput <file.cdx> -Out <file.efi> [-HeapPages <n>]
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string]$CdxInput,
    [Parameter(Mandatory=$true)] [string]$Out,
    [int]$HeapPages = 512
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$cdx = [System.IO.File]::ReadAllBytes($CdxInput)
function R32($off) { [BitConverter]::ToInt32($cdx, $off) }
function R64($off) { [BitConverter]::ToInt64($cdx, $off) }

$textOff   = R64 168
$textSz    = R64 176
$rodataOff = R64 184
$rodataSz  = R64 192
$entryOff  = R64 200     # __start offset (text-relative)
$debugOff  = R32 220

# --- Find 'opening' in the debug map ---
# CCE encoding: o=16 p=31 e=13 n=18 i=17 n=18 g=29
$openingCce = [byte[]]@(16, 31, 13, 18, 17, 18, 29)
$openingFuncOff = -1

if ($debugOff -gt 0 -and $debugOff -lt $cdx.Length) {
    $mapCount = R32 ($debugOff + 4)
    $mapStringsOff = R32 ($debugOff + 8)
    $stringsBase = $debugOff + $mapStringsOff
    $entriesBase = $debugOff + 12
    # Find 'opening' string in the string table
    $openingStrPos = -1
    for ($i = $stringsBase; $i -lt $cdx.Length - $openingCce.Length; $i++) {
        $match = $true
        for ($j = 0; $j -lt $openingCce.Length -and $match; $j++) {
            if ($cdx[$i+$j] -ne $openingCce[$j]) { $match = $false }
        }
        if ($match -and ($i + $openingCce.Length -ge $cdx.Length -or $cdx[$i + $openingCce.Length] -eq 0)) {
            $openingStrPos = $i - $stringsBase
            break
        }
    }
    if ($openingStrPos -ge 0) {
        for ($i = 0; $i -lt $mapCount; $i++) {
            $eOff = $entriesBase + $i * 12
            $strOff = R32 ($eOff + 8)
            if ($strOff -eq $openingStrPos) {
                $openingFuncOff = R32 $eOff
                break
            }
        }
    }
}

if ($openingFuncOff -lt 0) {
    Write-Host "[cdx-to-pe] WARN: 'opening' not found in debug map, using __start"
    $openingFuncOff = $entryOff
}

Write-Host "[cdx-to-pe] text=$textSz @ $textOff  rodata=$rodataSz @ $rodataOff  opening=0x$($openingFuncOff.ToString('X'))"

$FileAlign    = 512
$SectionAlign = 4096
$HeaderSize   = 512
$ImageBase    = 0x100000

$textAligned = ($textSz + 7) -band -8
$dataVaddr = $ImageBase + $textAligned

# Memory layout constants (must match X86_64Boot.codex)
$DeckPosAddr = 28720            # 0x7030
$DeckBoundCounterAddr = 28904   # 0x70E8
$HeapHwmAddr = 28728            # 0x7038
$BivySaveAddr = 28912           # 0x70F0
$SysTableAddr = 0x8000

# --- Assemble the UEFI app stub ---
$ms = [System.IO.MemoryStream]::new()
$bw = [System.IO.BinaryWriter]::new($ms)

# Prolog: save all callee-saved regs
$bw.Write([byte[]]@(
    0x55,                         # push rbp
    0x48, 0x89, 0xE5,             # mov rbp, rsp
    0x53,                         # push rbx
    0x41, 0x54, 0x41, 0x55, 0x41, 0x56, 0x41, 0x57,  # push r12-r15
    0x56, 0x57,                   # push rsi, rdi
    0x48, 0x83, 0xEC, 0x28,       # sub rsp, 0x28
    0x49, 0x89, 0xD7,             # mov r15, rdx (SystemTable)
    0x49, 0x89, 0xCE              # mov r14, rcx (ImageHandle)
))

# Disable watchdog: BootServices->SetWatchdogTimer(0,0,0,NULL)
$bw.Write([byte[]]@(
    0x49, 0x8B, 0x47, 0x60,       # mov rax, [r15+0x60]
    0x48, 0x31, 0xC9,             # xor rcx, rcx
    0x48, 0x31, 0xD2,             # xor rdx, rdx
    0x4D, 0x31, 0xC0,             # xor r8, r8
    0x4C, 0x89, 0x44, 0x24, 0x20, # mov [rsp+0x20], r8
    0xFF, 0x90, 0x00, 0x01, 0x00, 0x00  # call [rax+0x100]
))

# Get current IP (for relative addressing of text/rodata after stub)
# call $+5; pop rbx; sub rbx, 5  -> rbx = address of this call instruction
$getIpOff = $ms.Position
$bw.Write([byte[]]@(0xE8, 0x00, 0x00, 0x00, 0x00, 0x5B, 0x48, 0x83, 0xEB, 0x05))

# AllocatePages for code+rodata
$codePages = [int](($textSz + $rodataSz + 4095) / 4096)
$bw.Write([byte[]]@(0x48, 0x31, 0xC9))                          # xor rcx, rcx (AllocateAnyPages)
$bw.Write([byte[]]@(0x48, 0xC7, 0xC2, 0x01, 0x00, 0x00, 0x00))  # mov rdx, 1 (EfiLoaderCode)
$bw.Write([byte[]]@(0x49, 0xC7, 0xC0))                          # mov r8, codePages
$bw.Write([BitConverter]::GetBytes([int]$codePages))
$bw.Write([byte[]]@(0x4C, 0x8D, 0x4C, 0x24, 0x30))              # lea r9, [rsp+0x30]
$bw.Write([byte[]]@(0x49, 0x8B, 0x47, 0x60))                    # mov rax, [r15+0x60]
$bw.Write([byte[]]@(0xFF, 0x50, 0x28))                          # call [rax+0x28]

# AllocatePages for heap at fixed address 0x1000000 (past page tables + UEFI structures)
# AllocateAddress=2, EfiLoaderData=2, pages=HeapPages, addr=[rsp+0x38]=0x1000000
$bw.Write([byte[]]@(0x48, 0xC7, 0x44, 0x24, 0x38))              # mov qword [rsp+0x38], 0x1000000
$bw.Write([BitConverter]::GetBytes([int]0x1000000))
$bw.Write([byte[]]@(0x48, 0xC7, 0xC1, 0x02, 0x00, 0x00, 0x00))  # mov rcx, 2 (AllocateAddress)
$bw.Write([byte[]]@(0x48, 0xC7, 0xC2, 0x02, 0x00, 0x00, 0x00))  # mov rdx, 2 (EfiLoaderData)
$bw.Write([byte[]]@(0x49, 0xC7, 0xC0))                          # mov r8, HeapPages
$bw.Write([BitConverter]::GetBytes([int]$HeapPages))
$bw.Write([byte[]]@(0x4C, 0x8D, 0x4C, 0x24, 0x38))              # lea r9, [rsp+0x38]
$bw.Write([byte[]]@(0x49, 0x8B, 0x47, 0x60))                    # mov rax, [r15+0x60]
$bw.Write([byte[]]@(0xFF, 0x50, 0x28))                          # call [rax+0x28]

# Store SystemTable at 0x8000
$bw.Write([byte[]]@(0x4C, 0x89, 0x3C, 0x25))                    # mov [SysTableAddr], r15
$bw.Write([BitConverter]::GetBytes([int]$SysTableAddr))

# r10 = 0x1000000 (heap base, past page tables)
$bw.Write([byte[]]@(0x49, 0xBA))                                 # mov r10, 0x1000000
$bw.Write([BitConverter]::GetBytes([long]0x1000000))

# Set RSP to top of heap allocation (bare-metal stack-above-heap layout)
$stackTop = 0x1000000 + $HeapPages * 4096
$bw.Write([byte[]]@(0x48, 0xB8))                                 # mov rax, stackTop
$bw.Write([BitConverter]::GetBytes([long]$stackTop))
$bw.Write([byte[]]@(0x48, 0x89, 0xC4))                          # mov rsp, rax
$bw.Write([byte[]]@(0x48, 0x89, 0xC5))                          # mov rbp, rax

# Store stackTop at stack-min-rsp-addr (overflow check tracking)
$StackMinRspAddr = 28736
$bw.Write([byte[]]@(0x48, 0xBF))                                 # mov rdi, StackMinRspAddr
$bw.Write([BitConverter]::GetBytes([long]$StackMinRspAddr))
$bw.Write([byte[]]@(0x48, 0x89, 0x07))                          # mov [rdi], rax (stackTop still in rax)

# Store heap base at deck-pos-addr
$bw.Write([byte[]]@(0x48, 0xBF))                                 # mov rdi, DeckPosAddr
$bw.Write([BitConverter]::GetBytes([long]$DeckPosAddr))
$bw.Write([byte[]]@(0x4C, 0x89, 0x17))                          # mov [rdi], r10

# Store 0 at deck-bound-counter-addr
$bw.Write([byte[]]@(0x48, 0xBF))                                 # mov rdi, DeckBoundCounterAddr
$bw.Write([BitConverter]::GetBytes([long]$DeckBoundCounterAddr))
$bw.Write([byte[]]@(0x48, 0x31, 0xC0))                          # xor rax, rax
$bw.Write([byte[]]@(0x48, 0x89, 0x07))                          # mov [rdi], rax

# Store heap base at heap-hwm-addr
$bw.Write([byte[]]@(0x48, 0xBF))                                 # mov rdi, HeapHwmAddr
$bw.Write([BitConverter]::GetBytes([long]$HeapHwmAddr))
$bw.Write([byte[]]@(0x4C, 0x89, 0x17))                          # mov [rdi], r10

# Store 0 at bivy-save-addr
$bw.Write([byte[]]@(0x48, 0xBF))                                 # mov rdi, BivySaveAddr
$bw.Write([BitConverter]::GetBytes([long]$BivySaveAddr))
$bw.Write([byte[]]@(0x48, 0x89, 0x07))                          # mov [rdi], rax

# Copy text section: memcpy(0x100000, rbx + textDataOff, textSz)
# We'll patch textDataOff after we know the full stub size (two-pass)
$copyTextPatchPos = $ms.Position + 3  # offset of the imm32 in lea rsi, [rbx+imm32]
$bw.Write([byte[]]@(0x48, 0x8D, 0xB3))                          # lea rsi, [rbx + textDataOff]
$bw.Write([int]0)                                                # placeholder
$bw.Write([byte[]]@(0x48, 0xBF))                                 # mov rdi, ImageBase
$bw.Write([BitConverter]::GetBytes([long]$ImageBase))
$bw.Write([byte[]]@(0x48, 0xB9))                                 # mov rcx, textSz
$bw.Write([BitConverter]::GetBytes([long]$textSz))
$bw.Write([byte[]]@(0xF3, 0xA4))                                # rep movsb

# Copy rodata section
$copyRodataPatchPos = $ms.Position + 3
$bw.Write([byte[]]@(0x48, 0x8D, 0xB3))                          # lea rsi, [rbx + rodataDataOff]
$bw.Write([int]0)                                                # placeholder
$bw.Write([byte[]]@(0x48, 0xBF))                                 # mov rdi, dataVaddr
$bw.Write([BitConverter]::GetBytes([long]$dataVaddr))
$bw.Write([byte[]]@(0x48, 0xB9))                                 # mov rcx, rodataSz
$bw.Write([BitConverter]::GetBytes([long]$rodataSz))
$bw.Write([byte[]]@(0xF3, 0xA4))                                # rep movsb

# Clear screen
$bw.Write([byte[]]@(
    0x49, 0x8B, 0x47, 0x40,       # mov rax, [r15+0x40] (ConOut)
    0x48, 0x89, 0xC1,             # mov rcx, rax
    0xFF, 0x50, 0x30              # call [rax+0x30] (ClearScreen)
))

# Call opening: mov rax, ImageBase + openingFuncOff; call rax
$bw.Write([byte[]]@(0x48, 0xB8))                                 # mov rax, imm64
$bw.Write([BitConverter]::GetBytes([long]($ImageBase + $openingFuncOff)))
$bw.Write([byte[]]@(0xFF, 0xD0))                                # call rax

# Epilog: halt (we replaced the stack so we can't return to UEFI)
$bw.Write([byte[]]@(0xF4))                                      # hlt

$bw.Close()
$stub = $ms.ToArray()

# Patch the copy offsets: text data starts right after the stub
# rbx = address of the getIP call instruction
$rbxOff = $getIpOff  # offset of getIP within stub
$textDataOff = $stub.Length - $rbxOff          # text starts after stub, relative to rbx
$rodataDataOff = $stub.Length + $textSz - $rbxOff  # rodata after text

[BitConverter]::GetBytes([int]$textDataOff).CopyTo($stub, $copyTextPatchPos)
[BitConverter]::GetBytes([int]$rodataDataOff).CopyTo($stub, $copyRodataPatchPos)

# --- Extract text and rodata from CDX ---
$textBytes = New-Object byte[] $textSz
[Array]::Copy($cdx, $textOff, $textBytes, 0, $textSz)
$rodataBytes = New-Object byte[] $rodataSz
if ($rodataSz -gt 0) { [Array]::Copy($cdx, $rodataOff, $rodataBytes, 0, $rodataSz) }

$code = $stub + $textBytes + $rodataBytes
$codeSize = $code.Length
$codeRaw = (($codeSize + $FileAlign - 1) -band (-bnot ($FileAlign - 1)))
$codePad = $codeRaw - $codeSize
$codeSecEnd = (($codeSize + $SectionAlign - 1) -band (-bnot ($SectionAlign - 1)))
$relocRva = $SectionAlign + $codeSecEnd
$imageSize = (($relocRva + $SectionAlign + $SectionAlign - 1) -band (-bnot ($SectionAlign - 1)))
$relocRawOff = $HeaderSize + $codeRaw

# --- Build PE headers ---
$hms = [System.IO.MemoryStream]::new()
$hbw = [System.IO.BinaryWriter]::new($hms)

# DOS header
$hbw.Write([byte[]]@(0x4D, 0x5A))
$hbw.Write((New-Object byte[] 58))
$hbw.Write([int]128)
$hbw.Write((New-Object byte[] (128 - 64)))

# PE signature
$hbw.Write([byte[]]@(0x50, 0x45, 0x00, 0x00))

# COFF header
$hbw.Write([ushort]0x8664); $hbw.Write([ushort]2)
$hbw.Write([int]0); $hbw.Write([int]0); $hbw.Write([int]0)
$hbw.Write([ushort]240); $hbw.Write([ushort]0x0022)

# Optional header (PE32+)
$hbw.Write([ushort]0x020B)
$hbw.Write([byte]0); $hbw.Write([byte]0)
$hbw.Write([int]$codeRaw); $hbw.Write([int]0); $hbw.Write([int]0)
$hbw.Write([int]$SectionAlign)        # AddressOfEntryPoint
$hbw.Write([int]$SectionAlign)        # BaseOfCode
$hbw.Write([long]0)                   # ImageBase
$hbw.Write([int]$SectionAlign); $hbw.Write([int]$FileAlign)
$hbw.Write([ushort]0); $hbw.Write([ushort]0)
$hbw.Write([ushort]0); $hbw.Write([ushort]0)
$hbw.Write([ushort]0); $hbw.Write([ushort]0)
$hbw.Write([int]0)
$hbw.Write([int]$imageSize); $hbw.Write([int]$HeaderSize); $hbw.Write([int]0)
$hbw.Write([ushort]10)               # EFI Application
$hbw.Write([ushort]0x0160)
$hbw.Write([long]0); $hbw.Write([long]0)
$hbw.Write([long]0); $hbw.Write([long]0)
$hbw.Write([int]0); $hbw.Write([int]16)

for ($i = 0; $i -lt 16; $i++) {
    if ($i -eq 5) { $hbw.Write([int]$relocRva); $hbw.Write([int]8) }
    else { $hbw.Write([long]0) }
}

# .text section
$hbw.Write([System.Text.Encoding]::ASCII.GetBytes(".text`0`0`0"))
$hbw.Write([int]$codeSize); $hbw.Write([int]$SectionAlign)
$hbw.Write([int]$codeRaw); $hbw.Write([int]$HeaderSize)
$hbw.Write([int]0); $hbw.Write([int]0)
$hbw.Write([ushort]0); $hbw.Write([ushort]0)
$hbw.Write([int]0x60000020)

# .reloc section
$hbw.Write([System.Text.Encoding]::ASCII.GetBytes(".reloc`0`0"))
$hbw.Write([int]8); $hbw.Write([int]$relocRva)
$hbw.Write([int]$FileAlign); $hbw.Write([int]$relocRawOff)
$hbw.Write([int]0); $hbw.Write([int]0)
$hbw.Write([ushort]0); $hbw.Write([ushort]0)
$hbw.Write([int]0x42000040)

$hbw.Close()
$headers = $hms.ToArray()
$headerPad = New-Object byte[] ($HeaderSize - $headers.Length)

$relocBlock = New-Object byte[] $FileAlign
[BitConverter]::GetBytes([int]$relocRva).CopyTo($relocBlock, 0)
[BitConverter]::GetBytes([int]8).CopyTo($relocBlock, 4)

# --- Write PE ---
$outMs = [System.IO.MemoryStream]::new()
$outMs.Write($headers, 0, $headers.Length)
$outMs.Write($headerPad, 0, $headerPad.Length)
$outMs.Write($code, 0, $code.Length)
if ($codePad -gt 0) { $outMs.Write((New-Object byte[] $codePad), 0, $codePad) }
$outMs.Write($relocBlock, 0, $relocBlock.Length)

[System.IO.File]::WriteAllBytes($Out, $outMs.ToArray())
Write-Host "[cdx-to-pe] OK: $Out ($($outMs.Length) bytes, stub=$($stub.Length) opening=0x$($openingFuncOff.ToString('X')))"
