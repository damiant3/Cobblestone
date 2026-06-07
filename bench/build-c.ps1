# Compile all C benchmark files with MSVC at /Od and /O2, producing
# assembly listings (/FA) and executables for each.
#
# Output: bench/build-output/c/<name>/{Od,O2}/{name}.exe, {name}.asm
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$BenchDir = $PSScriptRoot
$SrcDir   = Join-Path $BenchDir 'c'
$OutRoot  = Join-Path $BenchDir 'build-output' 'c'

$vcvars = 'C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvarsall.bat'
if (-not (Test-Path $vcvars)) {
    Write-Error "vcvarsall.bat not found at $vcvars"
    exit 1
}

$sources = Get-ChildItem -Path $SrcDir -Filter '*.c'
if ($sources.Count -eq 0) { Write-Host 'No .c files found'; exit 0 }

$batFile = [System.IO.Path]::GetTempFileName() + '.bat'

foreach ($src in $sources) {
    $name = $src.BaseName
    foreach ($opt in @('Od', 'O2')) {
        $outDir = Join-Path $OutRoot $name $opt
        New-Item -ItemType Directory -Force -Path $outDir | Out-Null
        $exe = Join-Path $outDir "$name.exe"
        $asmDir = $outDir

        $batLines = @(
            '@echo off',
            "call `"$vcvars`" x64 >nul 2>&1",
            "cd /d `"$($src.Directory.FullName)`"",
            "cl.exe /nologo /$opt /FA /Fo$outDir\ /Fa$outDir\ /Fe$exe $($src.Name)"
        )
        [System.IO.File]::WriteAllLines($batFile, $batLines, [System.Text.Encoding]::ASCII)

        Write-Host "  [$opt] $name"
        $result = cmd /c $batFile 2>&1
        $exitCode = $LASTEXITCODE
        $result | Out-File -FilePath (Join-Path $outDir 'build.log') -Encoding UTF8
        if ($exitCode -ne 0) {
            Write-Host "    FAIL (exit $exitCode)"
            $result | ForEach-Object { Write-Host "    $_" }
        } else {
            $output = & $exe 2>&1
            Write-Host "    output: $output"
            $output | Out-File -FilePath (Join-Path $outDir 'result.txt') -Encoding UTF8
        }
        Remove-Item -Force (Join-Path $outDir '*.obj') -ErrorAction SilentlyContinue
    }
}

Remove-Item -Force $batFile -ErrorAction SilentlyContinue

Write-Host "`nDone. Assembly listings in $OutRoot"
