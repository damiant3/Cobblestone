# Build Codex Designer: source -> concat -> IR-CCE -> WASM plug -> WAT -> HTML
# Usage: pwsh codex/plugs/wasm/build-designer.ps1
[CmdletBinding()]
param([switch]$WatOnly)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo      = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..')).Path
$PlugDir   = (Resolve-Path $PSScriptRoot).Path
$OutDir    = Join-Path $PlugDir 'build-output'
$PlugCdx   = Join-Path $OutDir 'wasm-plug.cdx'
$LogFile   = Join-Path $OutDir 'designer-build.log'
$DesignerRoot = Join-Path $Repo 'apps' 'designer'

if (-not (Test-Path $PlugCdx)) { Write-Error "MISSING: $PlugCdx (run build.ps1 first)"; exit 2 }

. (Join-Path $Repo 'build' 'vm-config.ps1')

# -- Concatenate designer source files --
$lines = [System.Collections.Generic.List[string]]::new()
$srcFiles = @(
    (Join-Path $DesignerRoot 'DesignerWidgets.codex'),
    (Join-Path $DesignerRoot 'DesignerRender.codex'),
    (Join-Path $DesignerRoot 'DesignerExport.codex'),
    (Join-Path $DesignerRoot 'DesignerApp.codex')
)
foreach ($f in $srcFiles) {
    if (-not (Test-Path $f)) { Write-Error "MISSING: $f"; exit 3 }
    foreach ($l in [System.IO.File]::ReadAllLines($f)) {
        # Strip cross-chapter cites (all in same bundle)
        if ($l -match '^\s*cites\s+Designer\s+chapter') { continue }
        $lines.Add($l)
    }
    $lines.Add(''); $lines.Add('')
}

# -- Cite resolution for foreword deps --
# Designer intra-quire cites are stripped during concat above, so the
# resolver never sees them; everything it finds is a real dependency.
. (Join-Path $Repo 'codex\plugs\common\plug-build-lib.ps1')
$preLines = Resolve-PlugForewords -Lines $lines

$bundleSrc = Join-Path $OutDir 'designer-bundle.codex'
$body = (($preLines + $lines) -join "`n") + "`n"
[System.IO.File]::WriteAllText($bundleSrc, $body, [System.Text.UTF8Encoding]::new($false))
Write-Host "[designer] bundled $($preLines.Count + $lines.Count) lines ($($body.Length) bytes)"

# -- Phase 1: source -> IR-CCE --
$IrFile = Join-Path $OutDir 'designer.ir'
# text-plug: this plug resolves a Codex call by its NAME, so the inline passes
# must not substitute a body and delete the call. See text-plug-ir-pipeline in
# codex/compiler/IR/Passes.codex, and run.ps1, which passes the same flag.
& pwsh -NoProfile -File (Join-Path $Repo 'build\compile.ps1') -Src $bundleSrc -Out $IrFile -Log $LogFile -IrCce -Passes 'text-plug' -Survey "check-mul:400,lower-mul:300,headroom:120" -MemMB 2048
if ($LASTEXITCODE -ne 0) {
    Write-Error "FAIL: IR compile; see $LogFile"
    Get-Content $LogFile -ErrorAction SilentlyContinue | Select-Object -Last 20 | ForEach-Object { Write-Host "  $_" }
    exit 4
}
Write-Host "[designer] IR: $((Get-Item $IrFile).Length) bytes (CCE)"

# -- Phase 2: IR -> WAT via WASM plug --
$irBytes = [System.IO.File]::ReadAllBytes($IrFile)
$inputFile = [System.IO.Path]::GetTempFileName()
$hdrList = [System.Collections.Generic.List[byte]]::new()
foreach ($ch in "IR-CCE".ToCharArray()) {
    $u = [int]$ch
    if ($u -lt 256) { $hdrList.Add([byte]$script:UnicodeToCce[$u]) }
}
$hdrList.Add([byte]1)
$modeHeader = $hdrList.ToArray()
$combined = New-Object byte[] ($modeHeader.Length + $irBytes.Length + 1)
[Buffer]::BlockCopy($modeHeader, 0, $combined, 0, $modeHeader.Length)
[Buffer]::BlockCopy($irBytes, 0, $combined, $modeHeader.Length, $irBytes.Length)
$combined[$combined.Length - 1] = 0
[System.IO.File]::WriteAllBytes($inputFile, $combined)

$vmBin = Join-Path $Repo 'tools\codex-vm.exe'
$outFile = [System.IO.Path]::GetTempFileName()
$errFile = [System.IO.Path]::GetTempFileName()
$proc = Start-Process -FilePath $vmBin -ArgumentList @('-kernel',$PlugCdx,'-input',$inputFile,'-output',$outFile,'-mem','2048','-headless') -PassThru -WindowStyle Hidden -RedirectStandardError $errFile
$proc.WaitForExit(300000)
if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force; Write-Error "FAIL: plug timeout"; exit 5 }

if (-not (Test-Path $outFile) -or (Get-Item $outFile).Length -eq 0) {
    $err = if (Test-Path $errFile) { Get-Content $errFile -Raw } else { "" }
    Write-Error "FAIL: plug produced no output"
    if ($err -match 'EXC') { Write-Host $err.Substring(0, [Math]::Min(500, $err.Length)) }
    exit 6
}

$raw = [System.IO.File]::ReadAllText($outFile)
$watLines = $raw -split "`n" | Where-Object { $_ -notmatch '^(HEAP|WD|STACK|PM):' -and $_.Trim().Length -gt 0 }
$wat = ($watLines -join "`n") -replace '^[\x00-\x1f]+', ''
$watFile = Join-Path $OutDir 'designer.wat'
[System.IO.File]::WriteAllText($watFile, $wat, [System.Text.UTF8Encoding]::new($false))
Write-Host "[designer] WAT: $watFile ($($wat.Length) chars)"

Remove-Item $inputFile,$outFile,$errFile -Force -ErrorAction SilentlyContinue

# -- Phase 3: WAT -> WASM --
if (-not $WatOnly) {
    $wat2wasm = Get-Command 'wat2wasm' -ErrorAction SilentlyContinue
    if ($wat2wasm) {
        $wasmFile = Join-Path $OutDir 'designer.wasm'
        & wat2wasm $watFile -o $wasmFile
        if ($LASTEXITCODE -eq 0) {
            Write-Host "[designer] WASM: $wasmFile ($((Get-Item $wasmFile).Length) bytes)"
        } else { Write-Warning "wat2wasm failed; WAT is still available at $watFile" }
    } else {
        Write-Host "[designer] wat2wasm not found; WAT ready at $watFile"
    }
}

# -- Phase 4: Assemble HTML --
$templateFile = Join-Path $DesignerRoot 'designer-page.template'
$jsFile = Join-Path $DesignerRoot 'designer-app.js'
$cssFile = Join-Path $DesignerRoot 'designer-studio.css'
$htmlOut = Join-Path $OutDir 'designer.html'

if ((Test-Path $templateFile) -and (Test-Path $jsFile) -and (Test-Path $cssFile)) {
    $template = [System.IO.File]::ReadAllText($templateFile)
    $css = [System.IO.File]::ReadAllText($cssFile)
    $js = [System.IO.File]::ReadAllText($jsFile)
    $html = $template.Replace('{{CSS}}', $css).Replace('{{JS}}', $js)
    if (Test-Path $htmlOut) { try { Set-ItemProperty $htmlOut -Name IsReadOnly -Value $false } catch {} }
    [System.IO.File]::WriteAllText($htmlOut, $html, [System.Text.UTF8Encoding]::new($false))
    Write-Host "[designer] HTML: $htmlOut"
} else {
    Write-Host "[designer] HTML: skipped (missing template, CSS, or JS source)"
}

Write-Host "[designer] done"
