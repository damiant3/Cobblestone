# A build whose output is open for INTEGRATE is thrown away at submit.
#
# Perforce drops an edit made on top of an integrate-only open (see
# docs/Agents/PerforceProcess.md), and codex-vm.exe is the file in this tree
# most likely to be in that state: a merge-down integrates it, and rebuilding
# is the obvious next thing to do. The binary then submits as the version that
# came DOWN, carrying none of your source change, and the first thing that
# notices is a test going red in somebody else's battery.
#
# The ordering that works: submit the merge-down FIRST, then `p4 edit
# tools/codex-vm.exe` and build. This refuses rather than warns, because a
# warning in a build that prints "Built:" at the end is a warning nobody reads.
$opened = & p4 opened tools/codex-vm.exe 2>$null
if ($LASTEXITCODE -eq 0 -and $opened -match '- integrate ') {
    Write-Host ''
    Write-Host 'REFUSED: tools/codex-vm.exe is open for INTEGRATE, not edit.' -ForegroundColor Red
    Write-Host '  ' $opened
    Write-Host ''
    Write-Host 'A rebuild now is dropped at submit and the binary lands without your'
    Write-Host 'change. Submit the merge-down first, then:'
    Write-Host '    p4 edit -c <cl> tools/codex-vm.exe'
    Write-Host '    tools/build-vm.ps1'
    Write-Host ''
    exit 1
}
$vsPath = "C:\Program Files\Microsoft Visual Studio\2022\Community"
$vcvars = "$vsPath\VC\Auxiliary\Build\vcvars64.bat"
if (-not (Test-Path $vcvars)) { Write-Host "FAIL: vcvars64.bat not found"; exit 1 }
$src = Join-Path $PSScriptRoot "codex-vm.c"
$out = Join-Path $PSScriptRoot "codex-vm.exe"
cmd /c "`"$vcvars`" >nul 2>&1 && cl /O2 /W3 /Brepro /Fe:`"$out`" `"$src`" /link WinHvPlatform.lib ws2_32.lib winmm.lib /Brepro"
if ($LASTEXITCODE -ne 0) { Write-Host "BUILD FAILED"; exit 1 }
Write-Host "Built: $out"
