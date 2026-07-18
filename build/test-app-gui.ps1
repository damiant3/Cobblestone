# Run an application's GUI battery: every .uiscript in apps/<app>/tests/,
# driven headless against the app's compiled CDX, each frame compared to its
# recorded .expected.bmp. This is app-BVT: does the thing render, do the menus
# open, does the status bar have content, does a click land.
#
#   build/test-app-gui.ps1 -App circuits
#   build/test-app-gui.ps1 -App circuits -Build          # compile the app first
#   build/test-app-gui.ps1 -App circuits -Accept         # (re)record every frame
#   build/test-app-gui.ps1 -App circuits -Only menu      # scripts matching *menu*
#
# Nothing touches the host cursor and no window takes focus (see
# docs/ExaminersAssay.md, GUI Tests), so a battery can run beside other work.

param(
    [Parameter(Mandatory = $true)][string]$App,
    [string]$Kernel,
    [string]$Only,
    [int]$Width  = 1024,
    [int]$Height = 768,
    [int]$Tolerance = 0,
    [switch]$Build,
    [switch]$Accept
)

$ErrorActionPreference = 'Stop'

$testDir = "apps/$App/tests"
if (-not (Test-Path $testDir)) { throw "no test directory: $testDir" }
if (-not $Kernel) { $Kernel = "build/output/$App.cdx" }

if ($Build) {
    $buildScript = "apps/$App/build.ps1"
    if (-not (Test-Path $buildScript)) { throw "no build script: $buildScript" }
    Write-Host "[gui] building $App ..."
    & pwsh $buildScript -Out $Kernel -Log "build/output/$App.log" | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Host "[gui] BUILD FAILED"; exit 1 }
}
if (-not (Test-Path $Kernel)) {
    throw "kernel not found: $Kernel (pass -Build to compile it, or -Kernel <path>)"
}

$scripts = Get-ChildItem "$testDir/*.uiscript" | Sort-Object Name
if ($Only) { $scripts = $scripts | Where-Object { $_.Name -like "*$Only*" } }
if (-not $scripts) { throw "no .uiscript files in $testDir" }

Write-Host ("[gui] {0}: {1} test(s), kernel {2}" -f $App, $scripts.Count, $Kernel)

$pass = 0; $fail = 0
$failed = @()
foreach ($s in $scripts) {
    $args = @(
        "build/test-gui.ps1",
        "-Kernel", $Kernel,
        "-Script", $s.FullName,
        "-Width", $Width, "-Height", $Height,
        "-Tolerance", $Tolerance,
        "-OutDir", "test-output/gui/$App"
    )
    if ($Accept) { $args += "-Accept" }
    & pwsh @args
    if ($LASTEXITCODE -eq 0) { $pass++ } else { $fail++; $failed += $s.BaseName }
}

Write-Host ""
Write-Host ("[gui] {0}: {1} pass / {2} fail" -f $App, $pass, $fail)
if ($fail -gt 0) {
    Write-Host ("[gui] failed: {0}" -f ($failed -join ", "))
    Write-Host "[gui] actual frames are in test-output/gui/$App -- LOOK at them before re-recording"
    exit 1
}
exit 0
