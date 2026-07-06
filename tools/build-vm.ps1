$vsPath = "C:\Program Files\Microsoft Visual Studio\2022\Community"
$vcvars = "$vsPath\VC\Auxiliary\Build\vcvars64.bat"
if (-not (Test-Path $vcvars)) { Write-Host "FAIL: vcvars64.bat not found"; exit 1 }
$src = Join-Path $PSScriptRoot "codex-vm.c"
$out = Join-Path $PSScriptRoot "codex-vm.exe"
cmd /c "`"$vcvars`" >nul 2>&1 && cl /O2 /W3 /Brepro /Fe:`"$out`" `"$src`" /link WinHvPlatform.lib ws2_32.lib winmm.lib /Brepro"
if ($LASTEXITCODE -ne 0) { Write-Host "BUILD FAILED"; exit 1 }
Write-Host "Built: $out"
