# Extract per-function x86-64 disassembly from CDX binaries using the .map file.
#
# For each function in the .map, extracts raw bytes from the CDX .text section,
# wraps them in a minimal COFF .obj, and runs dumpbin /disasm to produce
# human-readable assembly.
#
# Output: bench/build-output/codex/<name>/<name>.disasm (all functions)
#         bench/build-output/codex/<name>/funcs/<func>.disasm (per-function)
[CmdletBinding()]
param(
    [string]$Name = '',
    [string[]]$Functions = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$BenchDir = $PSScriptRoot
$OutRoot  = Join-Path $BenchDir 'build-output' 'codex'

$vcvars = 'C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvarsall.bat'
if (-not (Test-Path $vcvars)) { Write-Error "vcvarsall.bat not found"; exit 1 }

function Make-CoffObj {
    param([byte[]]$Code, [string]$OutPath)

    $ms = [System.IO.MemoryStream]::new()
    $bw = [System.IO.BinaryWriter]::new($ms)

    # COFF Header (20 bytes)
    $bw.Write([uint16]0x8664)      # Machine: AMD64
    $bw.Write([uint16]1)           # NumberOfSections: 1
    $bw.Write([uint32]0)           # TimeDateStamp
    $bw.Write([uint32]0)           # PointerToSymbolTable: none
    $bw.Write([uint32]0)           # NumberOfSymbols
    $bw.Write([uint16]0)           # SizeOfOptionalHeader
    $bw.Write([uint16]0)           # Characteristics

    # Section Header (.text, 40 bytes)
    $nameBytes = [System.Text.Encoding]::ASCII.GetBytes('.text')
    $bw.Write($nameBytes)
    for ($i = $nameBytes.Length; $i -lt 8; $i++) { $bw.Write([byte]0) }
    $bw.Write([uint32]$Code.Length) # VirtualSize
    $bw.Write([uint32]0)            # VirtualAddress
    $bw.Write([uint32]$Code.Length) # SizeOfRawData
    $bw.Write([uint32]60)           # PointerToRawData (20 header + 40 section header)
    $bw.Write([uint32]0)            # PointerToRelocations
    $bw.Write([uint32]0)            # PointerToLinenumbers
    $bw.Write([uint16]0)            # NumberOfRelocations
    $bw.Write([uint16]0)            # NumberOfLinenumbers
    $bw.Write([uint32]0x60000020)   # Characteristics: CODE | EXECUTE | READ

    # Raw data
    $bw.Write($Code)

    $bw.Flush()
    [System.IO.File]::WriteAllBytes($OutPath, $ms.ToArray())
    $bw.Close()
    $ms.Close()
}

$script:disasmBat = [System.IO.Path]::GetTempFileName() + '.bat'

function Disasm-Obj {
    param([string]$ObjPath)
    $batLines = @(
        '@echo off',
        "call `"$vcvars`" x64 >nul 2>&1",
        "dumpbin /disasm `"$ObjPath`""
    )
    [System.IO.File]::WriteAllLines($script:disasmBat, $batLines, [System.Text.Encoding]::ASCII)
    $output = cmd /c $script:disasmBat 2>&1
    $lines = @($output | ForEach-Object { $_.ToString() })
    $result = [System.Collections.Generic.List[string]]::new()
    foreach ($l in $lines) {
        if ($l -match '^\s*[0-9A-Fa-f]{16}:') { [void]$result.Add($l) }
    }
    return $result
}

# Find all benchmarks to process
if ($Name) {
    $dirs = @(Join-Path $OutRoot $Name)
} else {
    $dirs = @(Get-ChildItem -Path $OutRoot -Directory | ForEach-Object { $_.FullName })
}

foreach ($dir in $dirs) {
    $benchName = Split-Path $dir -Leaf
    $cdxFile = Join-Path $dir "$benchName.cdx"
    $mapFile = Join-Path $dir "$benchName.map"

    if (-not (Test-Path $cdxFile)) { Write-Host "  SKIP $benchName (no .cdx)"; continue }
    if (-not (Test-Path $mapFile)) { Write-Host "  SKIP $benchName (no .map)"; continue }

    Write-Host "  [disasm] $benchName"

    # Read CDX header
    $cdx = [System.IO.File]::ReadAllBytes($cdxFile)
    $textOff = [BitConverter]::ToInt64($cdx, 168)
    $textSz  = [BitConverter]::ToInt64($cdx, 176)

    # Parse .map
    $entries = @()
    foreach ($line in [System.IO.File]::ReadAllLines($mapFile)) {
        if ($line -match '^(0x[0-9a-fA-F]+)\s+(\d+)\s+(.+)$') {
            $entries += @{
                Addr = [Convert]::ToInt64($matches[1], 16)
                Size = [int]$matches[2]
                Name = $matches[3].Trim()
            }
        }
    }

    if ($entries.Count -eq 0) { Write-Host "    no symbols in .map"; continue }

    $funcDir = Join-Path $dir 'funcs'
    New-Item -ItemType Directory -Force -Path $funcDir | Out-Null

    $allDisasm = [System.Collections.Generic.List[string]]::new()
    [void]$allDisasm.Add("# Disassembly: $benchName")
    [void]$allDisasm.Add("# CDX text section: $textSz bytes at offset $textOff")
    [void]$allDisasm.Add("# Functions: $($entries.Count)")
    [void]$allDisasm.Add("")

    $tmpObj = [System.IO.Path]::GetTempFileName() + '.obj'

    foreach ($e in $entries) {
        if ($Functions.Count -gt 0 -and $e.Name -notin $Functions) { continue }

        $offsetInText = $e.Addr - 0x100000
        if ($offsetInText -lt 0 -or $offsetInText + $e.Size -gt $textSz) {
            Write-Host "    SKIP $($e.Name) (out of bounds)"
            continue
        }

        $codeBytes = New-Object byte[] $e.Size
        [Array]::Copy($cdx, $textOff + $offsetInText, $codeBytes, 0, $e.Size)

        Make-CoffObj -Code $codeBytes -OutPath $tmpObj
        $asmLines = Disasm-Obj -ObjPath $tmpObj

        [void]$allDisasm.Add("--- $($e.Name) ($($e.Size) bytes, 0x$($e.Addr.ToString('X'))) ---")
        foreach ($al in $asmLines) {
            [void]$allDisasm.Add($al)
        }
        [void]$allDisasm.Add("")

        # Per-function file
        $funcFile = Join-Path $funcDir "$($e.Name).disasm"
        $funcLines = @("# $($e.Name) ($($e.Size) bytes)") + $asmLines
        [System.IO.File]::WriteAllLines($funcFile, $funcLines, [System.Text.UTF8Encoding]::new($false))
    }

    Remove-Item -Force $tmpObj -ErrorAction SilentlyContinue

    $allFile = Join-Path $dir "$benchName.disasm"
    [System.IO.File]::WriteAllLines($allFile, $allDisasm.ToArray(), [System.Text.UTF8Encoding]::new($false))
    Write-Host "    $($entries.Count) functions -> $allFile"
}

Remove-Item -Force $script:disasmBat -ErrorAction SilentlyContinue
Write-Host "`nDone."
