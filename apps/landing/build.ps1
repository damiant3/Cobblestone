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
#   -Page    regenerate landing.html only, skipping the games and the
#            expensive compile page
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

# Sections that reassemble a directory delete it first, and Copy-Item PRESERVES
# the read-only bit Perforce puts on tracked sources. So the first build leaves
# read-only copies and the SECOND one dies in the delete, which makes this a
# defect that only ever appears on a rebuild:
#   Exception calling "Delete": Access to the path 'AlphaCoverageKernel.codex' is denied.
# Measured 2026-09-02 on the third assembly of the day.
function Remove-BuiltDir {
    param([string]$Path)
    if (-not (Test-Path $Path)) { return }
    Get-ChildItem $Path -Recurse -File -Force | ForEach-Object { $_.IsReadOnly = $false }
    [IO.Directory]::Delete((Resolve-Path $Path), $true)
}

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
    Write-Host "[landing] -Page given; skipping games/ and compile/."
    exit 0
}

# --- 3. games/ : one wasm module per playable game --------------------
# The pages under web/games are tracked source; the modules beside them are
# build output (.p4ignore) and are produced here, so the bundle is
# reproducible from the depot rather than from whoever last ran the game
# build. A game that fails to build is a PARITY finding for the wasm plug
# lane, not something to route around: the build stops and names it.
# The list is READ OUT OF build-wasm.ps1's own $Games table rather than
# copied here. A second copy is how the bundle ends up shipping the module
# for a game the arcade no longer lists, or missing one it does, and neither
# shows up until somebody opens the page.
Write-Host "[landing] preparing the arcade art ..."
& pwsh -NoProfile -File (Join-Path $Repo 'apps\games\build-art.ps1')
if ($LASTEXITCODE -ne 0) { Write-Host "[landing] FAIL: arcade art"; exit 8 }

$gameBuild = Join-Path $Repo 'apps\games\build-wasm.ps1'
$GamesWasm = [regex]::Matches((Get-Content $gameBuild -Raw), "(?m)^    '(?<g>[a-z0-9]+)' = @\{") |
    ForEach-Object { $_.Groups['g'].Value } | Sort-Object
if ($GamesWasm.Count -lt 1) { Write-Host "[landing] FAIL: no games found in $gameBuild"; exit 8 }
Write-Host "[landing] $($GamesWasm.Count) game modules to build."
foreach ($g in $GamesWasm) {
    Write-Host "[landing] building the $g module ..."
    & pwsh -NoProfile -File (Join-Path $Repo 'apps\games\build-wasm.ps1') -Game $g -Kernel $Kernel
    if ($LASTEXITCODE -ne 0) { Write-Host "[landing] FAIL: $g wasm build"; exit 8 }
}

# --- 3b. c64/ : the emulator's module ---------------------------------
# Same contract as the games above: web/c64/index.html is tracked source and
# c64.wasm beside it is build output. The emulator is one module rather than a
# table of them, so there is no list to read out.
Write-Host '[landing] building the c64 module ...'
& pwsh -NoProfile -File (Join-Path $Repo 'apps\c64\build-wasm.ps1') -Kernel $Kernel
if ($LASTEXITCODE -ne 0) { Write-Host '[landing] FAIL: c64 wasm build'; exit 8 }

# --- 3c. mathbook/ : the notebook's evaluator -------------------------
# web/mathbook/index.html is tracked source, mathbook.wasm beside it is build
# output. This module is a WASI program rather than an exported-function one,
# so the page drives it through _start; apps/mathbook/build-wasm.ps1 has the
# reasoning.
Write-Host '[landing] building the mathbook module ...'
& pwsh -NoProfile -File (Join-Path $Repo 'apps\mathbook\build-wasm.ps1') -Kernel $Kernel
if ($LASTEXITCODE -ne 0) { Write-Host '[landing] FAIL: mathbook wasm build'; exit 8 }

# --- 3d. data/ : the relational engine --------------------------------
# web/data/index.html is tracked source, data.wasm beside it is build output.
# Same WASI shape as mathbook: the page drives it through _start.
Write-Host '[landing] building the data module ...'
& pwsh -NoProfile -File (Join-Path $Repo 'apps\data\build-wasm.ps1') -Kernel $Kernel
if ($LASTEXITCODE -ne 0) { Write-Host '[landing] FAIL: data wasm build'; exit 8 }

# --- 3e. safari/ : Steve Howell's driving screensaver -----------------
# web/safari/index.html is tracked source and safari.wasm beside it is build
# output, and the build script copies it there. This one takes the ARCADE shape
# rather than mathbook's: it exports functions and the page paints the draw
# commands it writes into linear memory, so -Page selects the browser chapter
# (SafariWasm) over the intake driver and generates the export wrappers.
Write-Host '[landing] building the safari module ...'
& pwsh -NoProfile -File (Join-Path $Repo 'apps\safari\build-wasm.ps1') -Page -Wasm -Kernel $Kernel
if ($LASTEXITCODE -ne 0) { Write-Host '[landing] FAIL: safari wasm build'; exit 8 }
Copy-Item (Join-Path $Repo 'apps\safari\build-output\safari-page.wasm') `
          (Join-Path $Repo 'apps\landing\web\safari\safari.wasm') -Force

# --- 4. compile/ : the wasm self-compile page -------------------------
$pageSrc = Join-Path $Repo 'codex\plugs\wasm\build-output\page'
Write-Host "[landing] building the wasm self-compile page ..."
& pwsh -NoProfile -File (Join-Path $Repo 'codex\plugs\wasm\build-page.ps1') -Kernel $Kernel
if ($LASTEXITCODE -ne 0) { Write-Host '[landing] FAIL: wasm page build'; exit 4 }

$dst = Join-Path $Web 'compile'
New-Item -ItemType Directory -Force -Path $dst | Out-Null
# library.img.gz is the 650-chapter library as a FAT16 volume, 1.58 MB gzipped.
# prism.html resolves it as EMBED['library.img.gz'] ? b64ToBytes(...) : fetch(...)
# and it DOES ride the embed, but a fetch fallback that 404s is a cliff rather
# than a fallback, and it is a built artifact this bundle simply was not copying.
foreach ($f in 'codex-compiler.wasm', 'Codex.codex', 'roundabout.jpg', 'prism.html', 'examples.json', 'library.img.gz') {
    $from = Join-Path $pageSrc $f
    if (-not (Test-Path -PathType Leaf $from)) { Write-Host "[landing] FAIL: missing $f"; exit 5 }
    Copy-Item $from (Join-Path $dst $f) -Force
}
# The target plugs are optional: a lens without its module stays dark, which is
# a page that says so rather than a build that stops.
# This step copies INTO web/compile and never cleans it, so a file that stops
# being produced lingers there and keeps being served. Retire them by name.
# index.html was the dedicated self-compile page. Damian, 2026-09-02: it goes,
# from the landing and from the site, because Prism does that job now. Retired
# BY NAME rather than merely unlinked, because this step copies into web/compile
# and never cleans it, so an unlinked page would go on being served.
foreach ($f in 'prism-offline.html', 'mosaic.svg', 'index.html') {
    $gone = Join-Path $dst $f
    if (Test-Path -PathType Leaf $gone) { Remove-Item $gone -Force; Write-Host "[landing] retired $f" }
}
# Taken from what build-page.ps1 actually produced rather than from a list
# kept by hand here. The hand list was a second register of the same set and
# it drifted: measured 2026-08-27 it named 13 modules against the page's 48,
# so every lens added since it was written shipped only because web/compile is
# never cleaned and old copies lingered. A module the page did not build this
# run is one this bundle must not carry.
$mods = @(Get-ChildItem $pageSrc -Filter '*.wasm' -File |
          Where-Object { $_.Name -ne 'codex-compiler.wasm' })
foreach ($m in $mods) { Copy-Item $m.FullName (Join-Path $dst $m.Name) -Force }
Write-Host ("[landing] target modules: {0}" -f $mods.Count)


# --- 5. gpushow/ : the WebGPU showcase --------------------------------
# Build output on the same terms as compile/: .p4ignore'd, reassembled from
# apps/gpushow, which is its own served root. kernels/ ships BOTH the .wgsl
# and the [Device] .codex it came from: the gallery's Source button fetches
# the .codex. Assembled fresh each run so a retired page cannot linger.
$gpuSrc = Join-Path $Repo 'apps\gpushow'
$gpuDst = Join-Path $Web 'gpushow'
$noWgsl = @(Get-ChildItem (Join-Path $gpuSrc 'kernels') -Filter *.codex -File |
            Where-Object { -not (Test-Path -PathType Leaf (Join-Path $gpuSrc ('kernels\' + $_.BaseName + '.wgsl'))) })
if ($noWgsl.Count -gt 0) {
    Write-Host ('[landing] FAIL: gpushow kernel(s) with no .wgsl: ' + (($noWgsl | ForEach-Object BaseName) -join ', '))
    exit 9
}
Remove-BuiltDir $gpuDst
foreach ($d in 'web', 'kernels', 'screenshots') {
    $to = Join-Path $gpuDst $d
    New-Item -ItemType Directory -Force -Path $to | Out-Null
    Copy-Item (Join-Path $gpuSrc ($d + '\*')) $to -Force
}
# A server-root absolute asset path resolves only under tools/serve.mjs and
# 404s from a site subdirectory, which paints a blank canvas rather than an
# error.
$rooted = @(Get-ChildItem (Join-Path $gpuDst 'web') -File |
            Select-String -Pattern "['""(]/(kernels|web|screenshots)/")
if ($rooted.Count -gt 0) {
    Write-Host ('[landing] FAIL: ' + $rooted.Count + ' gpushow page ref(s) are server-root absolute')
    exit 9
}
Write-Host ('[landing] gpushow: {0} pages, {1} kernels, {2} shots' -f
    (Get-ChildItem (Join-Path $gpuDst 'web') -File).Count,
    (Get-ChildItem (Join-Path $gpuDst 'kernels') -Filter *.wgsl -File).Count,
    (Get-ChildItem (Join-Path $gpuDst 'screenshots') -File).Count)
# --- 5b. fireworks/ : the shell show ----------------------------------
# Assembled on gpushow's terms and in gpushow's shape: web/ is the served
# page, kernels/ ships both the .wgsl the page fetches and the [Device]
# .codex it was lowered from, and the URL keeps the web/ segment. The bare
# metal app that shares this directory (the .cdx, the PTX and the SPIR-V)
# does not ship: only these two directories are copied.
$fwSrc  = Join-Path $Repo 'apps\fireworks'
$fwDst  = Join-Path $Web 'fireworks'
$fwKern = Join-Path $fwSrc 'kernels'
# The skyline is FireworksShow.codex through the wasm plug. Built here rather
# than tracked, for the reason .p4ignore gives beside its entry: fishtank
# tracked its module and shipped one four days stale.
& pwsh -NoProfile -File (Join-Path $fwSrc 'build-wasm.ps1')
if ($LASTEXITCODE -ne 0) { Write-Host '[landing] FAIL: fireworks skyline module'; exit 9 }
$fwWasm = Join-Path $fwSrc 'web\fireworks-show.wasm'
if (-not (Test-Path -PathType Leaf $fwWasm)) { Write-Host '[landing] FAIL: no fireworks-show.wasm'; exit 9 }
$fwNoWgsl = @(Get-ChildItem $fwKern -Filter *.codex -File |
              Where-Object { -not (Test-Path -PathType Leaf (Join-Path $fwKern ($_.BaseName + '.wgsl'))) })
if ($fwNoWgsl.Count -gt 0) {
    Write-Host ('[landing] FAIL: fireworks kernel(s) with no .wgsl: ' + (($fwNoWgsl | ForEach-Object BaseName) -join ', '))
    exit 9
}
Remove-BuiltDir $fwDst
foreach ($d in 'web', 'kernels') {
    $to = Join-Path $fwDst $d
    New-Item -ItemType Directory -Force -Path $to | Out-Null
    Copy-Item (Join-Path $fwSrc ($d + '\*')) $to -Force
}
$fwRooted = @(Get-ChildItem (Join-Path $fwDst 'web') -File |
              Select-String -Pattern "['""(]/(kernels|web)/")
if ($fwRooted.Count -gt 0) {
    Write-Host ('[landing] FAIL: ' + $fwRooted.Count + ' fireworks page ref(s) are server-root absolute')
    exit 9
}
# @() around both: StrictMode Latest refuses .Count on a SCALAR, and fireworks
# ships exactly one page and one kernel, so this line failed on its own app
# while the same shape in the gpushow and starmap sections above survives only
# because those directories happen to hold more than one file.
Write-Host ('[landing] fireworks: {0} page(s), {1} kernel(s)' -f
    @(Get-ChildItem (Join-Path $fwDst 'web') -File).Count,
    @(Get-ChildItem (Join-Path $fwDst 'kernels') -Filter *.wgsl -File).Count)

# --- 6. fishtank/ : the WASM aquarium ---------------------------------
# Build output like the sections above. The page is the ONLY fishtank
# surface that runs Codex: it calls init_aquarium and tick and reads fish
# and particle state out of WASM linear memory, so the two files below are
# the whole demo and none of the 30 MB of assets and models is reached.
# Published as index.html so the URL is <site>/fishtank/, beside the module
# the page fetches by a relative name.
$ftSrc = Join-Path $Repo 'apps\fishtank'
$ftDst = Join-Path $Web 'fishtank'
$ftWasm = Join-Path $ftSrc 'web\fishtank.wasm'
$ftPage = Join-Path $ftSrc 'web\fishtank-wasm.html'
foreach ($f in $ftWasm, $ftPage) {
    if (-not (Test-Path -PathType Leaf $f)) {
        Write-Host "[landing] FAIL: missing $f (run apps\fishtank\build-wasm.ps1)"
        exit 10
    }
}
# The module shipped 4 days behind its source once, beside a freshly
# assembled page, because build-wasm.ps1 only warned when wat2wasm refused.
if ((Get-Item $ftWasm).LastWriteTime -lt (Get-Item (Join-Path $ftSrc 'FishTankWasm.codex')).LastWriteTime) {
    Write-Host '[landing] FAIL: fishtank.wasm is older than FishTankWasm.codex; rebuild it'
    exit 10
}
New-Item -ItemType Directory -Force -Path $ftDst | Out-Null
Copy-Item $ftPage (Join-Path $ftDst 'index.html') -Force
Copy-Item $ftWasm (Join-Path $ftDst 'fishtank.wasm') -Force
# The page builds its sprite atlas from assets/<name>.png at RUNTIME, and it
# builds that path by CONCATENATION, so a census keyed on fetch( or src=" finds
# nothing and concludes the page needs no assets. It does. Worse, a missing one
# is silent: img.onerror resolves null, the atlas comes up empty, and every fish
# falls back to a 0.01 atlas patch -- full-size quads with nothing drawn on them,
# which reads as "the fish are tiny" rather than as a packaging fault. Shipped
# 2026-09-02 that way, and Damian is the one who noticed.
# Only the names the page actually loads: web/assets holds 35 images, 16 MB.
$ftHtml = [System.IO.File]::ReadAllText($ftPage)
$ftNames = @([regex]::Matches($ftHtml, "tex:'([a-z0-9-]+)'") |
             ForEach-Object { $_.Groups[1].Value }) + 'reef-backdrop' | Sort-Object -Unique
if ($ftNames.Count -lt 2) { Write-Host '[landing] FAIL: found no fishtank texture names in the page'; exit 10 }
$ftAssets = Join-Path $ftDst 'assets'
New-Item -ItemType Directory -Force -Path $ftAssets | Out-Null
foreach ($n in $ftNames) {
    $from = Join-Path $ftSrc "web\assets\$n.png"
    if (-not (Test-Path -PathType Leaf $from)) {
        Write-Host "[landing] FAIL: the fishtank page loads $n.png and it is not in web/assets"
        exit 10
    }
    Copy-Item $from (Join-Path $ftAssets "$n.png") -Force
}
Write-Host ('[landing] fishtank: page {0:N0} B, module {1:N0} B, {2} textures {3:N0} B' -f (Get-Item $ftPage).Length, (Get-Item $ftWasm).Length, $ftNames.Count, ((Get-ChildItem $ftAssets -File | Measure-Object Length -Sum).Sum))
# --- 6b. globe/ : the ray-traced Earth and the lensed black hole ---------
# Laid out like gpushow rather than like fishtank, because the page fetches
# ../kernels/GlobeKernels.wgsl and ../earth-texture.raw and those relative
# paths are what makes it run unchanged from the depot and from the site.
# The .wgsl is the wgsl plug's output, tracked beside its .codex; the .codex
# ships too, on gpushow's terms, so the shader can be read against its source.
$glSrc  = Join-Path $Repo 'apps\globe'
$glDst  = Join-Path $Web 'globe'
$glWgsl = Join-Path $glSrc 'kernels\GlobeKernels.wgsl'
$glPage = Join-Path $glSrc 'web\globe-codex.html'
$glTex  = Join-Path $glSrc 'earth-texture.raw'
foreach ($f in $glWgsl, $glPage, $glTex) {
    if (-not (Test-Path -PathType Leaf $f)) { Write-Host "[landing] FAIL: missing $f"; exit 12 }
}
# A refusal header means the plug did not emit something the module calls, so
# the page would load a shader that fails to compile and paint nothing.
if (Select-String -Path $glWgsl -Pattern 'WGSL PLUG REFUSAL' -Quiet) {
    Write-Host '[landing] FAIL: GlobeKernels.wgsl carries a plug refusal; regenerate it'
    exit 12
}
if ((Get-Item $glWgsl).LastWriteTime -lt (Get-Item (Join-Path $glSrc 'kernels\GlobeKernels.codex')).LastWriteTime) {
    Write-Host '[landing] FAIL: GlobeKernels.wgsl is older than GlobeKernels.codex; regenerate it'
    exit 12
}
Remove-BuiltDir $glDst
New-Item -ItemType Directory -Force -Path (Join-Path $glDst 'web') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $glDst 'kernels') | Out-Null
Copy-Item $glPage (Join-Path $glDst 'web\index.html') -Force
Copy-Item $glWgsl (Join-Path $glDst 'kernels\GlobeKernels.wgsl') -Force
Copy-Item (Join-Path $glSrc 'kernels\GlobeKernels.codex') (Join-Path $glDst 'kernels\GlobeKernels.codex') -Force
Copy-Item $glTex (Join-Path $glDst 'earth-texture.raw') -Force
Write-Host ('[landing] globe: page {0:N0} B, shader {1:N0} B, texture {2:N0} B' -f `
    (Get-Item $glPage).Length, (Get-Item $glWgsl).Length, (Get-Item $glTex).Length)

# --- 7. spark/ : the software 3D renderer --------------------------------
# Built here rather than copied, like games/ and compile/: the module is
# .p4ignore'd build output, so the bundle is reproducible from the depot
# rather than from whoever last ran the spark build. The page's pixels are
# all computed by Codex; the JS instantiates, calls spark_render once and
# blits the framebuffer, so a module that failed to build is a blank canvas
# and the build stops instead.
Write-Host '[landing] building the spark module ...'
& pwsh -NoProfile -File (Join-Path $Repo 'apps\spark\build-wasm.ps1') -Kernel $Kernel
if ($LASTEXITCODE -ne 0) { Write-Host '[landing] FAIL: spark wasm build'; exit 11 }

$spSrc = Join-Path $Repo 'apps\spark\web'
$spDst = Join-Path $Web 'spark'
New-Item -ItemType Directory -Force -Path $spDst | Out-Null
Copy-Item (Join-Path $spSrc 'spark.html') (Join-Path $spDst 'index.html') -Force
Copy-Item (Join-Path $spSrc 'spark.wasm') (Join-Path $spDst 'spark.wasm') -Force
Write-Host ('[landing] spark: page {0:N0} B, module {1:N0} B' -f `
    (Get-Item (Join-Path $spDst 'index.html')).Length,
    (Get-Item (Join-Path $spDst 'spark.wasm')).Length)
# --- 8. starmap/ : the 3D star map ---------------------------------------
# Built here for the same reason as spark: the module is .p4ignore'd build
# output. The driver check is a GATE and not a courtesy. A module that builds
# and then answers garbage is the failure this app is most exposed to, because
# every coordinate crosses a 32-bit word boundary and peek-32 zero-extends, so
# a sign or split defect reads as a plausible sky rather than as a crash.
Write-Host '[landing] building the starmap module ...'
& pwsh -NoProfile -File (Join-Path $Repo 'apps\starmap\build-wasm.ps1') -Kernel $Kernel
if ($LASTEXITCODE -ne 0) { Write-Host '[landing] FAIL: starmap wasm build'; exit 12 }

$smSrc  = Join-Path $Repo 'apps\starmap'
$smDst  = Join-Path $Web 'starmap'
$smWasm = Join-Path $smSrc 'web\starmap.wasm'
$smPage = Join-Path $smSrc 'web\starmap-codex.html'
foreach ($f in @($smWasm, $smPage)) {
    if (-not (Test-Path -PathType Leaf $f)) { Write-Host "[landing] FAIL: missing $f"; exit 12 }
}
if ((Get-Item $smWasm).LastWriteTime -lt (Get-Item (Join-Path $smSrc 'StarMapWasm.codex')).LastWriteTime) {
    Write-Host '[landing] FAIL: starmap.wasm is older than StarMapWasm.codex; rebuild it'
    exit 12
}

# The module's entry act builds the catalog and prints its counts, so running
# it is a behavioural gate and not a load test: a trapped builtin, a catalog
# that did not build, or a wrong object count all show up in that one line.
# wasmtime rather than node because the wasm plug already requires it
# (codex/plugs/wasm/hosted-wasm-test.ps1 checks for wat2wasm and wasmtime
# together), so this adds no dependency the module's own build did not have.
# The deeper arms, the ones that read linear memory back, need a driver that
# can poke at exports; that is apps/starmap/sm-verify.mjs, run by hand like
# the other forty-four *-verify.mjs graders in this tree, none of which any
# script invokes.
if (-not (Get-Command 'wasmtime' -ErrorAction SilentlyContinue)) {
    Write-Host '[landing] FAIL: wasmtime is not on the Path; the starmap module cannot be graded'
    exit 12
}
$smSay = (& wasmtime $smWasm 2>&1 | Out-String).Trim()
if ($LASTEXITCODE -ne 0) {
    Write-Host "[landing] FAIL: the starmap module trapped on its own entry: $smSay"
    exit 12
}
if ($smSay -notmatch 'StarMap WASM: (\d+) objects, (\d+) labels, (\d+) visible') {
    Write-Host "[landing] FAIL: the starmap module said '$smSay', which is not its entry line"
    exit 12
}
$smObjects = [int]$Matches[1]; $smLabels = [int]$Matches[2]; $smVisible = [int]$Matches[3]
if ($smObjects -ne 80 -or $smLabels -le 0 -or $smVisible -le 0 -or $smVisible -gt $smObjects) {
    Write-Host "[landing] FAIL: starmap built $smObjects objects, $smLabels labels, $smVisible visible"
    exit 12
}
Write-Host "[landing] starmap module: $smObjects objects, $smLabels labels, $smVisible visible"

New-Item -ItemType Directory -Force -Path $smDst | Out-Null
Copy-Item $smPage (Join-Path $smDst 'index.html') -Force
Copy-Item $smWasm (Join-Path $smDst 'starmap.wasm') -Force
# The page fetches its module by a relative name and nothing else, so a
# server-root reference here would resolve only under the dev server.
$smRooted = @(Select-String -Path (Join-Path $smDst 'index.html') -Pattern "(src|href|fetch\()\s*=?\s*['`"]/")
if ($smRooted.Count -gt 0) {
    Write-Host ('[landing] FAIL: ' + $smRooted.Count + ' starmap page ref(s) are server-root absolute')
    exit 12
}
Write-Host ('[landing] starmap: page {0:N0} B, module {1:N0} B' -f `
    (Get-Item (Join-Path $smDst 'index.html')).Length,
    (Get-Item (Join-Path $smDst 'starmap.wasm')).Length)

Write-Host ''
Write-Host '[landing] assembled:'
foreach ($f in (Get-ChildItem $Web -File | Sort-Object Name)) {
    '  {0,-22} {1,10:N0}' -f $f.Name, $f.Length
}
foreach ($f in (Get-ChildItem (Join-Path $Web 'games') -File -ErrorAction SilentlyContinue | Sort-Object Name)) {
    '  games/{0,-15} {1,10:N0}' -f $f.Name, $f.Length
}
foreach ($f in (Get-ChildItem $dst -File | Sort-Object Name)) {
    '  compile/{0,-13} {1,10:N0}' -f $f.Name, $f.Length
}
foreach ($d in 'web', 'kernels', 'screenshots') {
    $g = Join-Path $gpuDst $d
    '  gpushow/{0,-12} {1,6} files {2,10:N0}' -f $d, (Get-ChildItem $g -File).Count, ((Get-ChildItem $g -File | Measure-Object Length -Sum).Sum)
}
'  {0,-22} {1,10:N0}' -f 'TOTAL', ((Get-ChildItem $Web -File -Recurse | Measure-Object Length -Sum).Sum)