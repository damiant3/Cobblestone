# Build the compliance evidence plug as a WebAssembly module.
#
# This plug needs none of codex/plugs/common/PlugStdio.codex: its opening is
# already stdin and stdout, reading a mode line then key lines until END and
# printing ==CDXE== / ==HTML== / ==SBOM== / ==END==. So it is bundled the way
# its own build.ps1 bundles it, and only the back end differs.
#
# ITS INPUT IS RAW CCE, NOT ASCII. `read-line-cce` is the raw half of the read
# family and does no conversion, and its line separator is the CCE unit for a
# newline, which is 1 rather than 10. A caller that feeds ASCII gets the whole
# stream back as one unreadable line; the page encodes through the same
# 128-entry from-unicode table the runtime carries.
[CmdletBinding()]
param([string]$Kernel, [string]$OutDir)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..')).Path
if (-not $Kernel) { $Kernel = Join-Path $Repo 'seed\Codex.cdx' }
if (-not $OutDir) { $OutDir = Join-Path $PSScriptRoot 'build-output' }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

if (-not (Get-Command wat2wasm -ErrorAction SilentlyContinue)) {
    Write-Host 'REFUSE: wat2wasm is not on PATH.'; exit 2
}

$root    = Join-Path $OutDir 'evidence-root.codex'
$bundled = Join-Path $OutDir 'plug-source.codex'
$wat     = Join-Path $OutDir 'evidence-stdio.wat'
$wasm    = Join-Path $OutDir 'evidence-stdio.wasm'

$body = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'EvidencePackage.codex')) + "`n" +
        [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'EvidencePlug.codex'))
[IO.File]::WriteAllText($root, $body, [Text.UTF8Encoding]::new($false))

& pwsh -NoProfile -File (Join-Path $Repo 'build\bundle-app.ps1') -Src $root -Out $bundled
if ($LASTEXITCODE -ne 0) { Write-Host '[evidence-wasm] FAIL: bundle'; exit 3 }

& pwsh -NoProfile -File (Join-Path $Repo 'codex\plugs\wasm\run.ps1') -Src $bundled -Out $wat -Kernel $Kernel
if ($LASTEXITCODE -ne 0) { Write-Host '[evidence-wasm] FAIL: wasm emission'; exit 4 }

& wat2wasm --enable-tail-call $wat -o $wasm
if ($LASTEXITCODE -ne 0) { Write-Host '[evidence-wasm] FAIL: wat2wasm'; exit 5 }

Write-Host ("[evidence-wasm] OK: {0} ({1:N0} bytes)" -f $wasm, (Get-Item $wasm).Length)
