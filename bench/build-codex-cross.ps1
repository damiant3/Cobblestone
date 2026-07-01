# Compile all Codex benchmark files through the ARM64 and RISC-V plug
# pipelines, producing ELF binaries, symbol maps, and per-function
# disassembly.
#
# Output: bench/build-output/{codex-arm64,codex-riscv}/<name>/
#           <name>.elf, <name>.map, <name>.disasm, funcs/<func>.disasm
#
# Prerequisites: plugs must be built first
#   codex/plugs/arm64/build.ps1
#   codex/plugs/riscv/build.ps1
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$BenchDir = $PSScriptRoot
$SrcDir   = Join-Path $BenchDir 'codex'
$Repo     = Split-Path $BenchDir

function Safe-FileName($Name) {
    return ($Name -replace '[<>:"/\\|?*]', '_')
}

function ToWslPath($WinPath) {
    $p = if (Test-Path $WinPath) { (Resolve-Path $WinPath).Path } else { $WinPath }
    '/mnt/' + $p.Substring(0,1).ToLower() + $p.Substring(2).Replace('\','/')
}

function Find-WireStart {
    param([byte[]]$Raw)
    for ($i = 0; $i -lt [Math]::Min(64, $Raw.Length - 12); $i++) {
        $cl = [BitConverter]::ToInt32($Raw, $i)
        $dl = [BitConverter]::ToInt32($Raw, $i + 4)
        $fc = [BitConverter]::ToInt32($Raw, $i + 8)
        if ($cl -gt 0 -and $cl -lt 1000000 -and $dl -ge 0 -and $dl -lt 100000 -and $fc -gt 0 -and $fc -lt 500) {
            return $i
        }
    }
    return 0
}

function Parse-WireProtocol {
    param([byte[]]$Wire, [int]$Offset)
    $codeLen  = [BitConverter]::ToInt32($Wire, $Offset)
    $dataLen  = [BitConverter]::ToInt32($Wire, $Offset + 4)
    $funcCount = [BitConverter]::ToInt32($Wire, $Offset + 8)
    $codeStart = $Offset + 12

    $entries = [System.Collections.Generic.List[hashtable]]::new()
    $off = $codeStart + $codeLen + $dataLen
    for ($i = 0; $i -lt $funcCount; $i++) {
        $nameLen = [BitConverter]::ToInt16($Wire, $off)
        $chars = [char[]]::new($nameLen)
        for ($ci = 0; $ci -lt $nameLen; $ci++) {
            $chars[$ci] = [char]$Wire[$off + 2 + $ci]
        }
        $funcOff = [BitConverter]::ToInt32($Wire, $off + 2 + $nameLen)
        [void]$entries.Add(@{ Name = [string]::new($chars); Offset = $funcOff })
        $off += 2 + $nameLen + 4
    }

    $sorted = @($entries | Sort-Object { $_.Offset })
    for ($i = 0; $i -lt $sorted.Count; $i++) {
        $nextOff = if ($i + 1 -lt $sorted.Count) { $sorted[$i + 1].Offset } else { $codeLen }
        $sorted[$i].Size = $nextOff - $sorted[$i].Offset
    }
    return @{ CodeLen = $codeLen; DataLen = $dataLen; CodeStart = $codeStart; Funcs = $sorted }
}

# The plug appends a Unicode function-name manifest after the binary wire
# (FUNCMAP-BEGIN .. FUNCMAP-END, each row "<byte-offset> <name>"). Names there
# are correct Unicode -- no CCE decoding needed. Returns a byte-offset -> name map.
function Parse-FuncManifest {
    param([byte[]]$Wire)
    $text = [System.Text.Encoding]::UTF8.GetString($Wire)
    $map = @{}
    $inBlock = $false
    foreach ($line in ($text -split "`n")) {
        $line = $line.TrimEnd("`r")
        if ($line -match 'FUNCMAP-BEGIN') { $inBlock = $true; continue }
        if ($line -match 'FUNCMAP-END') { break }
        if ($inBlock -and $line -match '^(\d+)\s+(.+)$') { $map[[int]$matches[1]] = $matches[2] }
    }
    return $map
}

$archs = @(
    @{
        Tag = 'arm64'; OutDir = 'codex-arm64'
        CompileScript = Join-Path $Repo 'codex' 'plugs' 'arm64' 'compile-arm64.ps1'
        WireFile      = Join-Path $Repo 'codex' 'plugs' 'arm64' 'build-output' 'last-compile.arm64.bin'
        Objdump = 'aarch64-linux-gnu-objdump'; ObjdumpArch = 'aarch64'
        HasPreamble = $true
    }
    @{
        Tag = 'riscv'; OutDir = 'codex-riscv'
        CompileScript = Join-Path $Repo 'codex' 'plugs' 'riscv' 'compile-riscv.ps1'
        WireFile      = Join-Path $Repo 'codex' 'plugs' 'riscv' 'build-output' 'last-compile.riscv.bin'
        Objdump = 'riscv64-linux-gnu-objdump'; ObjdumpArch = 'riscv:rv64'
        HasPreamble = $true
    }
)

$sources = Get-ChildItem -Path $SrcDir -Filter '*.codex'
if ($sources.Count -eq 0) { Write-Host 'No .codex files found'; exit 0 }

$failed = 0
foreach ($arch in $archs) {
    Write-Host "=== Codex $($arch.Tag) ==="
    if (-not (Test-Path $arch.CompileScript)) {
        Write-Host "  SKIP: $($arch.CompileScript) not found"; continue
    }

    foreach ($src in $sources) {
        $name = $src.BaseName
        $outDir = Join-Path $BenchDir 'build-output' $arch.OutDir $name
        New-Item -ItemType Directory -Force -Path $outDir | Out-Null

        $elfFile = Join-Path $outDir "$name.elf"
        Write-Host "  [compile] $name -> $($arch.Tag)"
        & pwsh -NoProfile -File $arch.CompileScript -Src $src.FullName -Out $elfFile
        if ($LASTEXITCODE -ne 0) {
            Write-Host "    FAIL (exit $LASTEXITCODE)"; $failed++; continue
        }

        if (-not (Test-Path $arch.WireFile)) {
            Write-Host "    SKIP: wire file not found"; $failed++; continue
        }
        $wireBytes = [System.IO.File]::ReadAllBytes($arch.WireFile)
        Copy-Item $arch.WireFile (Join-Path $outDir "$name.wire") -Force

        $wireOff = if ($arch.HasPreamble) { Find-WireStart $wireBytes } else { 0 }
        $parsed = Parse-WireProtocol -Wire $wireBytes -Offset $wireOff

        # Replace CCE-garbled wire names with the plug's Unicode manifest (matched by byte offset).
        $nameMap = Parse-FuncManifest -Wire $wireBytes
        if ($nameMap.Count -gt 0) {
            foreach ($fn in $parsed.Funcs) { if ($nameMap.ContainsKey([int]$fn.Offset)) { $fn.Name = $nameMap[[int]$fn.Offset] } }
        }

        # Write .map
        $mapLines = [System.Collections.Generic.List[string]]::new()
        [void]$mapLines.Add("# Codex $($arch.Tag) Symbol Map")
        [void]$mapLines.Add("# Offset     Size  Name")
        foreach ($f in $parsed.Funcs) {
            $hexOff = '0x' + ([int]$f.Offset).ToString('X8')
            [void]$mapLines.Add("$hexOff $($f.Size) $($f.Name)")
        }
        [System.IO.File]::WriteAllLines((Join-Path $outDir "$name.map"), $mapLines.ToArray(), [System.Text.UTF8Encoding]::new($false))

        # Disassemble each function
        $funcDir = Join-Path $outDir 'funcs'
        New-Item -ItemType Directory -Force -Path $funcDir | Out-Null
        $allDisasm = [System.Collections.Generic.List[string]]::new()
        [void]$allDisasm.Add("# Disassembly: $name ($($arch.Tag))")
        [void]$allDisasm.Add("# Code: $($parsed.CodeLen) bytes, Functions: $($parsed.Funcs.Count)")
        [void]$allDisasm.Add("")

        foreach ($f in $parsed.Funcs) {
            if ($f.Size -le 0) { continue }
            $funcBytes = New-Object byte[] $f.Size
            [Array]::Copy($wireBytes, $parsed.CodeStart + $f.Offset, $funcBytes, 0, $f.Size)
            $tmpBin = [System.IO.Path]::GetTempFileName()
            [System.IO.File]::WriteAllBytes($tmpBin, $funcBytes)
            $wslTmp = ToWslPath $tmpBin

            $disasm = wsl -- $($arch.Objdump) -b binary -m $($arch.ObjdumpArch) -D $wslTmp 2>&1
            $instrLines = @($disasm | ForEach-Object { $_.ToString() } | Where-Object { $_ -match '^\s+[0-9a-f]+:' })

            [void]$allDisasm.Add("--- $($f.Name) ($($f.Size) bytes) ---")
            foreach ($il in $instrLines) { [void]$allDisasm.Add($il) }
            [void]$allDisasm.Add("")

            $safeName = Safe-FileName $f.Name
            if ($safeName.Length -gt 0 -and $safeName.Length -lt 200) {
                try {
                    $funcContent = @("# $($f.Name) ($($f.Size) bytes)") + $instrLines
                    [System.IO.File]::WriteAllLines((Join-Path $funcDir "$safeName.disasm"), $funcContent, [System.Text.UTF8Encoding]::new($false))
                } catch {}
            }

            Remove-Item $tmpBin -Force -ErrorAction SilentlyContinue
        }

        [System.IO.File]::WriteAllLines((Join-Path $outDir "$name.disasm"), $allDisasm.ToArray(), [System.Text.UTF8Encoding]::new($false))
        Write-Host "    $($parsed.Funcs.Count) functions, code=$($parsed.CodeLen) bytes"
    }
}

if ($failed -gt 0) { Write-Host "`n$failed benchmark(s) failed"; exit 1 }
Write-Host "`nDone."
