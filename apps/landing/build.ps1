[CmdletBinding()]
param(
    [switch]$Page,
    [switch]$Repl,
    [switch]$KeepStudio,
    [string]$Kernel
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$AppDir = (Resolve-Path $PSScriptRoot).Path
$Repo   = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$Web    = Join-Path $AppDir 'web'

function Remove-BuiltDir {
    param([string]$Path)
    if (-not (Test-Path $Path)) { return }
    $builtRoot = [IO.Path]::GetFullPath($Web).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $target = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $Path).Path)
    if (-not $target.StartsWith($builtRoot, [StringComparison]::OrdinalIgnoreCase) -or (Get-Item -LiteralPath $target).LinkType) {
        throw "Refuse removal outside the assembled site: $target"
    }
    Get-ChildItem $Path -Recurse -File -Force | ForEach-Object { $_.IsReadOnly = $false }
    [IO.Directory]::Delete((Resolve-Path $Path), $true)
}

if (-not $Kernel) { $Kernel = Join-Path $Repo 'seed\Codex.cdx' }
if (-not (Test-Path -PathType Leaf $Kernel)) {
    Write-Host "REFUSE: no kernel at $Kernel"; exit 2
}

$depotSeed = Join-Path $Repo 'seed\Codex.cdx'
if ((Get-FileHash -Algorithm SHA256 $Kernel).Hash -ne (Get-FileHash -Algorithm SHA256 $depotSeed).Hash) {
    Write-Host 'REFUSE: the HTML and WGSL page builders use seed/Codex.cdx; -Kernel must match that compiler.'
    exit 2
}

Write-Host "[landing] generating landing.html ..."
& pwsh -NoProfile -File (Join-Path $Repo 'codex\plugs\html\run.ps1') `
    -Src (Join-Path $AppDir 'LandingPage.codex') `
    -Out (Join-Path $Web 'landing.html')
if ($LASTEXITCODE -ne 0) { Write-Host '[landing] FAIL: page generation'; exit 3 }
Write-Host "[landing] generating imagegen.html ..."
& pwsh -NoProfile -File (Join-Path $Repo 'codex\plugs\html\run.ps1') `
    -Src (Join-Path $AppDir 'ImageGenPage.codex') `
    -Out (Join-Path $Web 'imagegen.html')
if ($LASTEXITCODE -ne 0) { Write-Host '[landing] FAIL: imagegen page generation'; exit 3 }
Write-Host "[landing] generating imagegen-browser.html ..."
& node (Join-Path $Repo 'apps\diffusion\build-browser-page.mjs') --no-tokenizer --out (Join-Path $Web 'imagegen-browser.html')
if ($LASTEXITCODE -ne 0) { Write-Host '[landing] FAIL: imagegen-browser page generation'; exit 3 }
if ($KeepStudio) {
    if (-not (Test-Path -LiteralPath (Join-Path $Web 'sparkstudio.html') -PathType Leaf)) { throw 'No Spark Studio page to preserve' }
    Write-Host '[landing] preserving Spark Studio for coordinated package validation'
} else {
    Write-Host "[landing] generating sparkstudio.html ..."
    & node (Join-Path $Repo 'apps\spark\build-studio-page.mjs') --out (Join-Path $Web 'sparkstudio.html')
    if ($LASTEXITCODE -ne 0) { Write-Host '[landing] FAIL: Spark Studio page generation'; exit 3 }
}

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

$modBuilderDst = Join-Path $Web 'modbuilder'
New-Item -ItemType Directory -Force -Path $modBuilderDst | Out-Null
Copy-Item -LiteralPath (Join-Path $Repo 'apps/modbuilder/web/workspace.html') -Destination (Join-Path $modBuilderDst 'index.html') -Force

if ($Page) {
    Write-Host "[landing] -Page given; skipping games/ and compile/."
    exit 0
}

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

if (Get-Command 'node' -ErrorAction SilentlyContinue) {
    & node (Join-Path $Repo 'apps\games\page-verify.mjs')
    if ($LASTEXITCODE -ne 0) { Write-Host '[landing] FAIL: the arcade page does not start'; exit 8 }
} else {
    Write-Host '[landing] node is not on the Path; page-verify skipped'
}

Write-Host '[landing] building the c64 module ...'
& pwsh -NoProfile -File (Join-Path $Repo 'apps\c64\build-wasm.ps1') -Kernel $Kernel
if ($LASTEXITCODE -ne 0) { Write-Host '[landing] FAIL: c64 wasm build'; exit 8 }

Write-Host '[landing] building the mathbook module ...'
& pwsh -NoProfile -File (Join-Path $Repo 'apps\mathbook\build-wasm.ps1') -Kernel $Kernel
if ($LASTEXITCODE -ne 0) { Write-Host '[landing] FAIL: mathbook wasm build'; exit 8 }

Write-Host '[landing] building the data module ...'
& pwsh -NoProfile -File (Join-Path $Repo 'apps\data\build-wasm.ps1') -Kernel $Kernel
if ($LASTEXITCODE -ne 0) { Write-Host '[landing] FAIL: data wasm build'; exit 8 }

Write-Host '[landing] building the safari module ...'
& pwsh -NoProfile -File (Join-Path $Repo 'apps\safari\build-wasm.ps1') -Page -Wasm -Kernel $Kernel
if ($LASTEXITCODE -ne 0) { Write-Host '[landing] FAIL: safari wasm build'; exit 8 }
Copy-Item (Join-Path $Repo 'apps\safari\build-output\safari-page.wasm') `
          (Join-Path $Repo 'apps\landing\web\safari\safari.wasm') -Force

$pageSrc = Join-Path $Repo 'codex\plugs\wasm\build-output\page'
Write-Host "[landing] building the wasm self-compile page ..."
& pwsh -NoProfile -File (Join-Path $Repo 'codex\plugs\wasm\build-page.ps1') -Kernel $Kernel
if ($LASTEXITCODE -ne 0) { Write-Host '[landing] FAIL: wasm page build'; exit 4 }

$dst = Join-Path $Web 'compile'
New-Item -ItemType Directory -Force -Path $dst | Out-Null
foreach ($f in 'codex-compiler.wasm', 'Codex.codex', 'roundabout.jpg', 'prism.html', 'examples.json', 'library.img.gz') {
    $from = Join-Path $pageSrc $f
    if (-not (Test-Path -PathType Leaf $from)) { Write-Host "[landing] FAIL: missing $f"; exit 5 }
    Copy-Item $from (Join-Path $dst $f) -Force
}
foreach ($f in 'prism-offline.html', 'mosaic.svg', 'index.html') {
    $gone = Join-Path $dst $f
    if (Test-Path -PathType Leaf $gone) { Remove-Item $gone -Force; Write-Host "[landing] retired $f" }
}
$mods = @(Get-ChildItem $pageSrc -Filter '*.wasm' -File |
          Where-Object { $_.Name -ne 'codex-compiler.wasm' })
foreach ($m in $mods) { Copy-Item $m.FullName (Join-Path $dst $m.Name) -Force }
Write-Host ("[landing] target modules: {0}" -f $mods.Count)

$gpuSrc = Join-Path $Repo 'apps\gpushow'
$gpuDst = Join-Path $Web 'gpushow'
$gpuCites = @(Get-ChildItem (Join-Path $gpuSrc 'kernels') -Filter *.codex -File |
    Select-String -Pattern '^\s*cites\s+Gpu\s+chapter\s+([A-Za-z0-9_]+)\s*$' |
    ForEach-Object { $_.Matches[0].Groups[1].Value } | Sort-Object -Unique)
$noWgsl = @(Get-ChildItem (Join-Path $gpuSrc 'kernels') -Filter *.codex -File |
            Where-Object { $_.BaseName -notin $gpuCites -and -not (Test-Path -PathType Leaf (Join-Path $gpuSrc ('kernels\' + $_.BaseName + '.wgsl'))) })
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
foreach ($chapter in $gpuCites) {
    $from = Join-Path $Repo "codex/foreword/gpu/$chapter.codex"
    if (-not (Test-Path -LiteralPath $from -PathType Leaf)) { throw "Missing GPU chapter: $chapter" }
    Copy-Item -LiteralPath $from -Destination (Join-Path $gpuDst "kernels/$chapter.codex") -Force
}
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
$fwSrc  = Join-Path $Repo 'apps\fireworks'
$fwDst  = Join-Path $Web 'fireworks'
$fwKern = Join-Path $fwSrc 'kernels'
& pwsh -NoProfile -File (Join-Path $fwSrc 'build-wasm.ps1')
if ($LASTEXITCODE -ne 0) { Write-Host '[landing] FAIL: fireworks skyline module'; exit 9 }
& node (Join-Path $fwSrc 'fw-verify.mjs') (Join-Path $fwSrc 'web\fireworks-show.wasm')
if ($LASTEXITCODE -ne 0) { Write-Host '[landing] FAIL: the fireworks skyline module does not build its cities'; exit 9 }
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
Write-Host ('[landing] fireworks: {0} page(s), {1} kernel(s)' -f
    @(Get-ChildItem (Join-Path $fwDst 'web') -File).Count,
    @(Get-ChildItem (Join-Path $fwDst 'kernels') -Filter *.wgsl -File).Count)

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
if ((Get-Item $ftWasm).LastWriteTime -lt (Get-Item (Join-Path $ftSrc 'FishTankWasm.codex')).LastWriteTime) {
    Write-Host '[landing] FAIL: fishtank.wasm is older than FishTankWasm.codex; rebuild it'
    exit 10
}
New-Item -ItemType Directory -Force -Path $ftDst | Out-Null
Copy-Item $ftPage (Join-Path $ftDst 'index.html') -Force
Copy-Item $ftWasm (Join-Path $ftDst 'fishtank.wasm') -Force
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
$glSrc  = Join-Path $Repo 'apps\globe'
$glDst  = Join-Path $Web 'globe'
$glWgsl = Join-Path $glSrc 'kernels\GlobeKernels.wgsl'
$glPage = Join-Path $glSrc 'web\globe-codex.html'
$glTex  = Join-Path $glSrc 'earth-texture.raw'
foreach ($f in $glWgsl, $glPage, $glTex) {
    if (-not (Test-Path -PathType Leaf $f)) { Write-Host "[landing] FAIL: missing $f"; exit 12 }
}
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

if (-not (Get-Command 'wasmtime' -ErrorAction SilentlyContinue)) {
    Write-Host '[landing] FAIL: wasmtime is not on the Path; the starmap module cannot be graded'
    exit 12
}
$smSay = (& wasmtime $smWasm 2>&1 | Out-String).Trim()
if ($LASTEXITCODE -ne 0) {
    Write-Host "[landing] FAIL: the starmap module trapped on its own entry: $smSay"
    exit 12
}
if ($smSay -notmatch 'StarMap WASM: ready for a catalogue, window (\d+) MB, cap (\d+)') {
    Write-Host "[landing] FAIL: the starmap module said '$smSay', which is not its entry line"
    exit 12
}
$smWindowMB = [int]$Matches[1]

$smDat = Join-Path $smSrc 'data\starmap.dat'
if (-not (Test-Path -PathType Leaf $smDat)) { Write-Host "[landing] FAIL: missing $smDat"; exit 12 }
$smHdr = [byte[]]::new(64)
$fs = [IO.File]::OpenRead($smDat)
try { $null = $fs.Read($smHdr, 0, 64) } finally { $fs.Close() }
$smLen = (Get-Item $smDat).Length
$smMagic = [Text.Encoding]::ASCII.GetString($smHdr, 0, 4)
$smVer = [BitConverter]::ToInt32($smHdr, 4)
$smStars = [BitConverter]::ToInt32($smHdr, 8)
if ($smMagic -ne 'STAR' -or $smVer -ne 2) {
    Write-Host "[landing] FAIL: starmap.dat magic '$smMagic' version $smVer"; exit 12
}
if ($smStars -lt 1 -or (64 + $smStars * 32) -gt $smLen) {
    Write-Host "[landing] FAIL: starmap.dat says $smStars stars, which does not fit its $smLen bytes"; exit 12
}
if ($smLen -gt $smWindowMB * 1048576) {
    Write-Host "[landing] FAIL: starmap.dat is $smLen bytes and the module reserves $smWindowMB MB"; exit 12
}
Write-Host ('[landing] starmap module: ready, {0} MB window; catalogue {1:N0} stars in {2:N0} B' -f $smWindowMB, $smStars, $smLen)

New-Item -ItemType Directory -Force -Path $smDst | Out-Null
Copy-Item $smPage (Join-Path $smDst 'index.html') -Force
Copy-Item $smWasm (Join-Path $smDst 'starmap.wasm') -Force
Copy-Item $smDat (Join-Path $smDst 'starmap.dat') -Force
$smRooted = @(Select-String -Path (Join-Path $smDst 'index.html') -Pattern "(src|href|fetch\()\s*=?\s*['`"]/")
if ($smRooted.Count -gt 0) {
    Write-Host ('[landing] FAIL: ' + $smRooted.Count + ' starmap page ref(s) are server-root absolute')
    exit 12
}
Write-Host ('[landing] starmap: page {0:N0} B, module {1:N0} B, catalogue {2:N0} B' -f `
    (Get-Item (Join-Path $smDst 'index.html')).Length,
    (Get-Item (Join-Path $smDst 'starmap.wasm')).Length,
    (Get-Item (Join-Path $smDst 'starmap.dat')).Length)

$exDst = Join-Path $Web 'experimental'
New-Item -ItemType Directory -Force -Path $exDst | Out-Null
Copy-Item (Join-Path $Repo 'codex\foreword\gpu\DeviceEffect.codex') (Join-Path $exDst 'DeviceEffect.codex') -Force
Set-ItemProperty (Join-Path $exDst 'DeviceEffect.codex') -Name IsReadOnly -Value $false
Write-Host ('[landing] experimental: page {0:N0} B, DeviceEffect {1:N0} B' -f `
    (Get-Item (Join-Path $exDst 'index.html')).Length, (Get-Item (Join-Path $exDst 'DeviceEffect.codex')).Length)
Write-Host ''
Write-Host '[landing] assembled:'
& node (Join-Path $AppDir 'pack-file-assets.mjs') --web $Web
if ($LASTEXITCODE -ne 0) { Write-Host '[landing] FAIL: file URL asset packaging'; exit 13 }
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
