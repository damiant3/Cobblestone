# Assemble the landing site.
#
# The site is ONE deployable bundle; nothing in it deploys standalone
# (Damian, 2026-08-26), so on-disk shape is a build decision rather than a
# constraint. Two kinds of thing live in it:
#
#   static   web/ itself, and web/compile/ -- the wasm self-compile page,
#            copied in whole. The compiler is a wasm module the browser
#            runs, so it needs nothing serving it.
#
#   live     Prism and Steve Howell's REPL. Both are servers that compile
#            on demand: apps/prism/run.ps1 boots prism.cdx inside codex-vm
#            with networking, and the REPL is a Flask app that shells out
#            to zig per run. Neither has a static form to copy -- the
#            canned IR that would have given Prism one is what Damian's
#            2026-08-24 ruling removed. serve.ps1 puts them behind the
#            same origin as the static files, at /prism/ and /repl/, so
#            the page's links are plain relative paths either way.
#
# build.ps1 assembles. serve.ps1 runs the bundle. Flags:
#   -Page    regenerate landing.html only, skipping the expensive compile page
#   -Repl    build the REPL's Python venv (opt-in; touches the network)
[CmdletBinding()]
param(
    [switch]$Page,
    [switch]$Repl,
    [string]$Kernel
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$AppDir = (Resolve-Path $PSScriptRoot).Path
$Repo   = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$Web    = Join-Path $AppDir 'web'

if (-not $Kernel) { $Kernel = Join-Path $Repo 'seed\Codex.cdx' }
if (-not (Test-Path -PathType Leaf $Kernel)) {
    Write-Host "REFUSE: no kernel at $Kernel"; exit 2
}

# --- 1. The page ------------------------------------------------------
# codex/plugs/html/run.ps1 takes no -Kernel and compiles through whatever
# build-output/bare-metal/Codex.cdx happens to hold, which is the trap
# CLAUDE.md names: that path is whichever kernel ran LAST. Put the chosen
# kernel there for the duration and restore it, so the shipped artifact is
# always built by a kernel we named.
$defaultKernel = Join-Path $Repo 'build-output\bare-metal\Codex.cdx'
$saved = "$defaultKernel.landing-save"
$restore = $false
if (Test-Path -PathType Leaf $defaultKernel) {
    $a = (Get-FileHash -Algorithm SHA256 $defaultKernel).Hash
    $b = (Get-FileHash -Algorithm SHA256 $Kernel).Hash
    if ($a -ne $b) {
        Copy-Item $defaultKernel $saved -Force
        Copy-Item $Kernel $defaultKernel -Force
        Set-ItemProperty $defaultKernel -Name IsReadOnly -Value $false
        $restore = $true
    }
} else {
    New-Item -ItemType Directory -Force -Path (Split-Path $defaultKernel) | Out-Null
    Copy-Item $Kernel $defaultKernel -Force
    Set-ItemProperty $defaultKernel -Name IsReadOnly -Value $false
}

try {
    Write-Host "[landing] generating landing.html ..."
    & pwsh -NoProfile -File (Join-Path $Repo 'codex\plugs\html\run.ps1') `
        -Src (Join-Path $AppDir 'LandingPage.codex') `
        -Out (Join-Path $Web 'landing.html')
    if ($LASTEXITCODE -ne 0) { Write-Host '[landing] FAIL: page generation'; exit 3 }
} finally {
    if ($restore) { Copy-Item $saved $defaultKernel -Force; Remove-Item $saved -Force }
}

# --- 2. -Repl : the REPL's Python environment -------------------------
# Opt-in and network-touching, so it never runs as part of a plain build.
# A venv under build-output rather than the box's Python: the REPL is a
# packaged part of this site, not a machine-wide install, and deleting the
# directory undoes it completely.
if ($Repl) {
    $replRepo = 'D:\Projects\essay-repl-server-main'
    if (-not (Test-Path $replRepo)) {
        Write-Host "[landing] REFUSE: no REPL source at $replRepo"; exit 7
    }
    $venv = Join-Path $AppDir 'build-output\repl-venv'
    $py = Join-Path $venv 'Scripts\python.exe'
    if (-not (Test-Path $py)) {
        Write-Host '[landing] creating the REPL venv ...'
        & python -m venv $venv
        if ($LASTEXITCODE -ne 0) { Write-Host '[landing] FAIL: venv'; exit 7 }
    }
    Write-Host '[landing] installing flask, waitress, markdown ...'
    & $py -m pip install --quiet --disable-pip-version-check flask waitress markdown
    if ($LASTEXITCODE -ne 0) { Write-Host '[landing] FAIL: pip'; exit 7 }
    & $py -c "import flask, waitress, markdown; print('[landing] REPL deps OK')"
}

if ($Page) {
    Write-Host "[landing] -Page given; skipping compile/."
    exit 0
}

# --- 3. compile/ : the wasm self-compile page -------------------------
$pageSrc = Join-Path $Repo 'codex\plugs\wasm\build-output\page'
Write-Host "[landing] building the wasm self-compile page ..."
& pwsh -NoProfile -File (Join-Path $Repo 'codex\plugs\wasm\build-page.ps1') -Kernel $Kernel
if ($LASTEXITCODE -ne 0) { Write-Host '[landing] FAIL: wasm page build'; exit 4 }

$dst = Join-Path $Web 'compile'
New-Item -ItemType Directory -Force -Path $dst | Out-Null
foreach ($f in 'index.html', 'codex-compiler.wasm', 'Codex.codex', 'roundabout.jpg', 'prism.html', 'examples.json') {
    $from = Join-Path $pageSrc $f
    if (-not (Test-Path -PathType Leaf $from)) { Write-Host "[landing] FAIL: missing $f"; exit 5 }
    Copy-Item $from (Join-Path $dst $f) -Force
}
# The target plugs are optional: a lens without its module stays dark, which is
# a page that says so rather than a build that stops.
# This step copies INTO web/compile and never cleans it, so a file that stops
# being produced lingers there and keeps being served. Retire them by name.
foreach ($f in 'prism-offline.html', 'mosaic.svg') {
    $gone = Join-Path $dst $f
    if (Test-Path -PathType Leaf $gone) { Remove-Item $gone -Force; Write-Host "[landing] retired $f" }
}
foreach ($f in 'javascript-stdio.wasm', 'csharp-stdio.wasm', 'evidence-stdio.wasm',
           'python-stdio.wasm', 'typescript-stdio.wasm', 'zig-stdio.wasm', 'html-stdio.wasm',
           'react-stdio.wasm', 'vue-stdio.wasm', 'swiftui-stdio.wasm', 'winforms-stdio.wasm',
           'pe-bytes.wasm', 'img-bytes.wasm') {
    $from = Join-Path $pageSrc $f
    if (Test-Path -PathType Leaf $from) { Copy-Item $from (Join-Path $dst $f) -Force }
    else { Write-Host "[landing] note: $f absent; that lens stays dark" }
}

# The page asserts byte-identity against an anchor injected at ITS build.
# If the placeholder survived, the copy would claim a match it never made.
$idx = [IO.File]::ReadAllText((Join-Path $dst 'index.html'))
if ($idx.Contains('__X86_HASH__')) {
    Write-Host '[landing] FAIL: the compile page still holds __X86_HASH__; its anchor was never injected.'
    exit 6
}

Write-Host ''
Write-Host '[landing] assembled:'
foreach ($f in (Get-ChildItem $Web -File | Sort-Object Name)) {
    '  {0,-22} {1,10:N0}' -f $f.Name, $f.Length
}
foreach ($f in (Get-ChildItem $dst -File | Sort-Object Name)) {
    '  compile/{0,-13} {1,10:N0}' -f $f.Name, $f.Length
}
'  {0,-22} {1,10:N0}' -f 'TOTAL', ((Get-ChildItem $Web -File -Recurse | Measure-Object Length -Sum).Sum)