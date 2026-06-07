# Smoke test for the MAUI plug.
# Compiles smoke.codex -> IR -> MAUI C# via the plug, then verifies
# the output contains expected MAUI control types and runtime functions.
[CmdletBinding()]
param([switch]$Build)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$PlugDir  = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$TestDir  = (Resolve-Path $PSScriptRoot).Path
$Repo     = (Resolve-Path (Join-Path $PlugDir '..' '..' '..')).Path
$OutDir   = Join-Path $TestDir 'output'
$ProjDir  = Join-Path $OutDir 'CodexApp'
$OutCs    = Join-Path $ProjDir 'MainPage.xaml.cs'

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$src = Join-Path $TestDir 'smoke.codex'

Write-Host "=== MAUI Plug Smoke Test ==="
Write-Host ""

# -- Run the plug pipeline --------------------------------------------
Write-Host "[1/3] Running MAUI plug pipeline..."
& pwsh -NoProfile -File (Join-Path $PlugDir 'run.ps1') -Src $src -Out $OutCs -ProjectDir $ProjDir 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "FAIL: plug pipeline returned exit $LASTEXITCODE" -ForegroundColor Red
    exit 1
}

if (-not (Test-Path $OutCs)) {
    Write-Host "FAIL: output file not created: $OutCs" -ForegroundColor Red
    exit 1
}

$cs = Get-Content $OutCs -Raw
$bytes = (Get-Item $OutCs).Length
Write-Host "[1/3] OK: $OutCs ($bytes bytes)"
Write-Host ""

# -- Verify expected content ------------------------------------------
Write-Host "[2/3] Verifying generated C#..."
$checks = @(
    @{ Name = "namespace declaration";     Pattern = "namespace.*;" }
    @{ Name = "MainPage class";            Pattern = "public partial class MainPage : ContentPage" }
    @{ Name = "constructor";               Pattern = "public MainPage\(\)" }
    @{ Name = "_page assignment";          Pattern = "_page = this" }
    @{ Name = "opening() call";            Pattern = "opening\(\)" }
    @{ Name = "console fallback";          Pattern = "if \(Content == null\)" }
    @{ Name = "color runtime _cc()";       Pattern = "static Color _cc\(long n\)" }
    @{ Name = "state runtime state_get";   Pattern = "static dynamic state_get" }
    @{ Name = "state runtime state_set";   Pattern = "static dynamic state_set" }
    @{ Name = "reactive set_render";       Pattern = "static long set_render" }
    @{ Name = "reactive request_render";   Pattern = "static long request_render" }
    @{ Name = "widget _wk()";             Pattern = "static View _wk\(dynamic" }
    @{ Name = "widget _wkStyle()";        Pattern = "static void _wkStyle" }
    @{ Name = "widget _wkSS()";           Pattern = "static dynamic _wkSS" }
    @{ Name = "widget mount_widget";       Pattern = "static long mount_widget\(dynamic" }
    @{ Name = "widget mount_widget_themed";Pattern = "static long mount_widget_themed" }
    @{ Name = "handler register_handlers"; Pattern = "static long register_handlers" }
    @{ Name = "WkPanel dispatch";          Pattern = "WkPanel" }
    @{ Name = "WkLabel dispatch";          Pattern = "WkLabel" }
    @{ Name = "WkButton dispatch";         Pattern = "WkButton" }
    @{ Name = "WkGauge dispatch";          Pattern = "WkGauge" }
    @{ Name = "WkSeparator dispatch";      Pattern = "WkSeparator" }
    @{ Name = "WkInput dispatch";          Pattern = "WkInput" }
    @{ Name = "VerticalStackLayout";       Pattern = "VerticalStackLayout" }
    @{ Name = "HorizontalStackLayout";     Pattern = "HorizontalStackLayout" }
    @{ Name = "ProgressBar";              Pattern = "ProgressBar" }
    @{ Name = "dialog show_alert";         Pattern = "static long show_alert" }
    @{ Name = "network fetch_json";        Pattern = "static long fetch_json" }
    @{ Name = "storage local_storage_get"; Pattern = "static string local_storage_get" }
    @{ Name = "custom _wkCustom";          Pattern = "static View _wkCustom" }
    @{ Name = "theme _wkTheme";            Pattern = "_wkTheme" }
    @{ Name = "MainThread dispatch";       Pattern = "MainThread.BeginInvokeOnMainThread" }
    @{ Name = "builtin print_line";        Pattern = "static void print_line" }
    @{ Name = "builtin show";             Pattern = "static string show" }
    @{ Name = "builtin list_at";           Pattern = "static dynamic list_at" }
    @{ Name = "user fn render";            Pattern = "static dynamic render" }
    @{ Name = "user fn on_click";          Pattern = "static dynamic on_click" }
    @{ Name = "user fn on_pick";           Pattern = "static dynamic on_pick" }
)

$pass = 0
$fail = 0
foreach ($chk in $checks) {
    if ($cs -match $chk.Pattern) {
        $pass++
    } else {
        Write-Host "  MISS: $($chk.Name) (/$($chk.Pattern)/)" -ForegroundColor Yellow
        $fail++
    }
}
Write-Host "[2/3] $pass/$($pass + $fail) checks passed"
if ($fail -gt 0) {
    Write-Host "  $fail checks failed" -ForegroundColor Yellow
}
Write-Host ""

# -- Verify project template ------------------------------------------
Write-Host "[3/3] Verifying project template..."
$templateFiles = @(
    "CodexApp.csproj"
    "MauiProgram.cs"
    "App.xaml"
    "App.xaml.cs"
    "MainPage.xaml"
    "MainPage.xaml.cs"
)
$tmplPass = 0
$tmplFail = 0
foreach ($f in $templateFiles) {
    $fp = Join-Path $ProjDir $f
    if (Test-Path $fp) {
        $tmplPass++
    } else {
        Write-Host "  MISS: $f" -ForegroundColor Yellow
        $tmplFail++
    }
}
Write-Host "[3/3] $tmplPass/$($tmplPass + $tmplFail) template files present"
Write-Host ""

# -- Optional dotnet build ---------------------------------------------
if ($Build) {
    Write-Host "[bonus] Running dotnet build..."
    $buildOut = & dotnet build (Join-Path $ProjDir 'CodexApp.csproj') -f net8.0-windows10.0.19041.0 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Host "[bonus] BUILD OK" -ForegroundColor Green
    } else {
        Write-Host "[bonus] BUILD FAILED" -ForegroundColor Red
        $buildOut | Select-Object -Last 20 | ForEach-Object { Write-Host "  $_" }
    }
    Write-Host ""
}

# -- Summary -----------------------------------------------------------
$total = $pass + $fail + $tmplPass + $tmplFail
$totalPass = $pass + $tmplPass
if ($fail -eq 0 -and $tmplFail -eq 0) {
    Write-Host "=== ALL $totalPass/$total CHECKS PASSED ===" -ForegroundColor Green
    exit 0
} else {
    Write-Host "=== $totalPass/$total CHECKS PASSED, $($fail + $tmplFail) FAILED ===" -ForegroundColor Yellow
    exit 1
}
