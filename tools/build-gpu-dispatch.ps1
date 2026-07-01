$vsPath = "C:\Program Files\Microsoft Visual Studio\2022\Community"
$vcvars = "$vsPath\VC\Auxiliary\Build\vcvars64.bat"
if (-not (Test-Path $vcvars)) { Write-Host "FAIL: vcvars64.bat not found"; exit 1 }
$nvcc = (Get-Command nvcc -ErrorAction SilentlyContinue).Source
if (-not $nvcc) {
    $cudaPaths = Get-ChildItem "C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA" -Directory -ErrorAction SilentlyContinue | Sort-Object Name -Descending
    if ($cudaPaths.Count -gt 0) { $nvcc = Join-Path $cudaPaths[0].FullName "bin\nvcc.exe" }
}
if (-not $nvcc -or -not (Test-Path $nvcc)) { Write-Host "FAIL: nvcc not found (install CUDA toolkit)"; exit 1 }
$src = Join-Path $PSScriptRoot "..\build\gpu-dispatch.cu"
$out = Join-Path $PSScriptRoot "..\build\gpu-dispatch.exe"
cmd /c "`"$vcvars`" >nul 2>&1 && `"$nvcc`" -O2 -lcublas -lcuda -o `"$out`" `"$src`""
if ($LASTEXITCODE -ne 0) { Write-Host "BUILD FAILED"; exit 1 }
Write-Host "Built: $out"
