# Build codex/plugs/evidence/build-output/evidence-plug.cdx: the compliance
# evidence plug (docs/Designs/Active/IoT/ComplianceEvidence.md). Not a
# transpiler plug: it reads no IR, so it is bundled with build/bundle-app.ps1
# from its two chapters (EvidencePackage, EvidencePlug) plus the foreword
# catalogs they cite, and compiled like any bare-metal program.
#
#   codex/plugs/evidence/build.ps1            # -> build-output/evidence-plug.cdx
#   codex/plugs/evidence/build.ps1 -Kernel seed/Codex.cdx
[CmdletBinding()]
param(
    # The compiler that builds the plug. Empty means whatever build.ps1 last
    # left in build-output (the dev loop); pass the seed for provenance.
    [string]$Kernel = ''
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..')).Path
$out = Join-Path $PSScriptRoot 'build-output'
New-Item -ItemType Directory -Force $out | Out-Null
$root = Join-Path $out 'evidence-root.codex'
$bundled = Join-Path $out 'plug-source.codex'
$cdx = Join-Path $out 'evidence-plug.cdx'
$log = Join-Path $out 'build.log'

# One root with both chapters: the plug chapter cites the package chapter as
# `Evidence chapter EvidencePackage`, which the bundler satisfies from the
# unit itself, so no quire registration is needed for a plug's own chapters.
$body = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'EvidencePackage.codex')) + "`n" + [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'EvidencePlug.codex'))
[IO.File]::WriteAllText($root, $body, [Text.UTF8Encoding]::new($false))

& pwsh -NoProfile -File (Join-Path $Repo 'build\bundle-app.ps1') -Src $root -Out $bundled
if ($LASTEXITCODE -ne 0) { throw "[evidence] bundle failed" }
$args = @('-Src', $bundled, '-Out', $cdx, '-Log', $log)
if ($Kernel) { $args += @('-Kernel', (Resolve-Path $Kernel).Path) }
& pwsh -NoProfile -File (Join-Path $Repo 'build\compile.ps1') @args
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $cdx)) {
    Get-Content $log -ErrorAction SilentlyContinue | Select-String 'error CDX' | Select-Object -First 20 | ForEach-Object { Write-Host "  $_" }
    throw "[evidence] compile failed; see $log"
}
Write-Host "[evidence] built $cdx ($((Get-Item $cdx).Length) bytes)"

# The fact-store writer: a second bare-metal program (FactIngest.codex, its own
# root: it cites the kernel's DiskFacts/AppPersist and declares Device.Block,
# which the plug does not), run by run.ps1 -FactImage against a disk image.
$ibundled = Join-Path $out 'ingest-source.codex'
$icdx = Join-Path $out 'fact-ingest.cdx'
$ilog = Join-Path $out 'ingest-build.log'
& pwsh -NoProfile -File (Join-Path $Repo 'build\bundle-app.ps1') -Src (Join-Path $PSScriptRoot 'FactIngest.codex') -Out $ibundled
if ($LASTEXITCODE -ne 0) { throw "[evidence] ingest bundle failed" }
$iargs = @('-Src', $ibundled, '-Out', $icdx, '-Log', $ilog)
if ($Kernel) { $iargs += @('-Kernel', (Resolve-Path $Kernel).Path) }
& pwsh -NoProfile -File (Join-Path $Repo 'build\compile.ps1') @iargs
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $icdx)) {
    Get-Content $ilog -ErrorAction SilentlyContinue | Select-String 'error CDX' | Select-Object -First 20 | ForEach-Object { Write-Host "  $_" }
    throw "[evidence] ingest compile failed; see $ilog"
}
Write-Host "[evidence] built $icdx ($((Get-Item $icdx).Length) bytes)"
