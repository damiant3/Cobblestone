[CmdletBinding()]
param([string]$Kernel = '', [string]$OutDir = '', [switch]$Mcp, [switch]$ReuseRuntime)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if (-not $Kernel) { $Kernel = Join-Path $repo 'seed/Codex.cdx' }
if (-not $OutDir) { $OutDir = Join-Path $PSScriptRoot 'build-output' }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path
$runtimeArgs=@('-NoProfile','-File',(Join-Path $PSScriptRoot 'build-runtime.ps1'),'-Kernel',$Kernel,'-OutDir',$OutDir)
if($ReuseRuntime){$runtimeArgs+='-Reuse'}
& pwsh @runtimeArgs
if($LASTEXITCODE -ne 0){throw 'ACCP runtime build failed'}
. (Join-Path $repo 'build/quire-map.ps1')
foreach ($library in @(@('math','AccpMath'),@('physics','AccpPhysics'),@('experiment','AccpExperiment'))) {
    $ordered = Resolve-CiteOrder -RootLines @("  cites Accp chapter $($library[1])") -Repo $repo
    $source = (Format-CiteChapters -Ordered $ordered) -join "`n"
    $bytes = [Text.Encoding]::UTF8.GetBytes($source)
    if ($bytes.Length -gt 65536) { throw 'Library exceeds source_bytes' }
    [IO.File]::WriteAllBytes((Join-Path $OutDir ($library[0]+'.codex')), $bytes)
}
$entries=@('Accp')
if($Mcp){$entries+='AccpMcp'}
foreach($entry in $entries){
    $free = (Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1MB
    if ($free -le 1.5) { throw "Compile admission refused: $free GiB free" }
    Write-Host "ACCP $entry compile: one guest, free RAM $([Math]::Round($free,2)) GiB"
    & pwsh -NoProfile -File (Join-Path $repo 'build/compile.ps1') `
        -Src (Join-Path $repo "apps/accp/$entry.codex") -Out (Join-Path $OutDir "$entry.cdx") `
        -Log (Join-Path $OutDir "$entry.compile.log") -Kernel $Kernel -RawFlags hosted-windows
    if ($LASTEXITCODE -ne 0) { Get-Content (Join-Path $OutDir "$entry.compile.log"); throw "ACCP compile failed: $LASTEXITCODE" }
    & pwsh -NoProfile -File (Join-Path $repo 'codex/plugs/pe/cdx-to-pe-console.ps1') `
        -CdxInput (Join-Path $OutDir "$entry.cdx") -Out (Join-Path $OutDir "$entry.exe")
    if ($LASTEXITCODE -ne 0) { throw 'Console packaging failed' }
}
$manifest = [ordered]@{ version = 2; seed = (Get-FileHash $Kernel).Hash.ToLowerInvariant(); files = [ordered]@{} }
$artifacts=@('codex-compiler.wasm','wasm-stdio.wasm','runtime.json','math.codex','physics.codex','experiment.codex')
foreach($entry in $entries){$artifacts+=@("$entry.cdx","$entry.exe")}
foreach ($name in $artifacts) {
    $hash = (Get-FileHash (Join-Path $OutDir $name)).Hash.ToLowerInvariant()
    $manifest.files[$name] = $hash
    Write-Host "$name SHA256 $hash"
}
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $OutDir 'manifest.json') -Encoding utf8
Write-Host "ACCP built at $OutDir"
