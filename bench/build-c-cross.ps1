# Compile all C benchmark files with GCC cross-compilers (via WSL)
# for ARM64 and RISC-V at -O0, -O2, and -Os.
#
# Output: bench/build-output/{c-arm64,c-riscv}/<name>/{O0,O2,Os}/
#           <name>.o, <name>.s, <name>.disasm
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$BenchDir = $PSScriptRoot
$SrcDir   = Join-Path $BenchDir 'c'

function ToWslPath($WinPath) {
    $p = if (Test-Path $WinPath) { (Resolve-Path $WinPath).Path } else { $WinPath }
    '/mnt/' + $p.Substring(0,1).ToLower() + $p.Substring(2).Replace('\','/')
}

$archs = @(
    @{ Tag = 'c-arm64'; Gcc = 'aarch64-linux-gnu-gcc'; Objdump = 'aarch64-linux-gnu-objdump' }
    @{ Tag = 'c-riscv'; Gcc = 'riscv64-linux-gnu-gcc'; Objdump = 'riscv64-linux-gnu-objdump' }
)
$opts = @('O0', 'O2', 'Os')

$sources = Get-ChildItem -Path $SrcDir -Filter '*.c'
if ($sources.Count -eq 0) { Write-Host 'No .c files found'; exit 0 }

foreach ($arch in $archs) {
    Write-Host "=== $($arch.Tag) ==="
    foreach ($src in $sources) {
        $name = $src.BaseName
        foreach ($opt in $opts) {
            $outDir = Join-Path $BenchDir 'build-output' $arch.Tag $name $opt
            New-Item -ItemType Directory -Force -Path $outDir | Out-Null

            $wslSrc = ToWslPath $src.FullName
            $wslObj = ToWslPath "$outDir\$name.o"
            $wslAsm = ToWslPath "$outDir\$name.s"

            Write-Host "  [$opt] $name"

            $result = wsl -- $($arch.Gcc) "-$opt" -c $wslSrc -o $wslObj 2>&1
            $result | Out-File (Join-Path $outDir 'build.log') -Encoding UTF8
            if ($LASTEXITCODE -ne 0) {
                Write-Host "    FAIL (compile)"
                $result | ForEach-Object { Write-Host "    $_" }
                continue
            }

            wsl -- $($arch.Gcc) "-$opt" -S $wslSrc -o $wslAsm 2>&1 | Out-Null

            $disasm = wsl -- $($arch.Objdump) -d $wslObj 2>&1
            $disasmText = ($disasm | ForEach-Object { $_.ToString() }) -join "`n"
            [System.IO.File]::WriteAllText((Join-Path $outDir "$name.disasm"), $disasmText, [System.Text.UTF8Encoding]::new($false))
            Write-Host "    OK"
        }
    }
}

Write-Host "`nDone. Output in bench/build-output/{c-arm64,c-riscv}/"
