# Common plug build library. Source this from plug build scripts.
# Provides: Resolve-PlugForewords, Build-PlugCdx, Add-PlugChapter,
#           Bundle-PlugSource, Build-TranspilerPlug

$script:PlugBuildRepo = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..')).Path
. (Join-Path $script:PlugBuildRepo 'build' 'vm-config.ps1')

$script:QuireDirs = @{}
$script:QuireOverrides = @{ 'codex\foreword\core' = 'Foreword'; 'codex\os\core' = 'OS'; 'apps\erp' = 'ERP' }
foreach ($root in @('codex\foreword', 'codex\os', 'apps', 'apps\games')) {
    $rootPath = Join-Path $script:PlugBuildRepo $root
    if (-not (Test-Path $rootPath)) { continue }
    foreach ($d in Get-ChildItem $rootPath -Directory) {
        $rel = $d.FullName.Substring($script:PlugBuildRepo.Length + 1)
        if (-not (Get-ChildItem $d.FullName -Filter '*.codex' -File -ErrorAction SilentlyContinue | Select-Object -First 1)) { continue }
        if ($script:QuireOverrides[$rel]) {
            $qname = $script:QuireOverrides[$rel]
        } else {
            $seg = $d.Name
            $qname = $seg.Substring(0,1).ToUpper() + $seg.Substring(1)
        }
        if (-not $script:QuireDirs[$qname]) { $script:QuireDirs[$qname] = $rel }
    }
}
$script:CitePat = '^\s*cites\s+(' + (($script:QuireDirs.Keys | Sort-Object) -join '|') + ')\s+chapter\s+([A-Za-z_][A-Za-z0-9_-]*)'

function Add-PlugChapter {
    param(
        [System.Collections.Generic.List[string]]$Lines,
        [string]$Path,
        [string[]]$StripCites = @(),
        [string]$Quire = ''
    )
    # Prefix the chapter name with the plug's quire (e.g. Riscv--RiscVPlug) so the
    # compiler's quire-based effect-exemption (TypeChecker quire-effect-exempt)
    # recognizes plug code as trusted. Without the prefix slug-quire cannot map the
    # chapter to its quire and CDX2031 (Device.Port effect) wrongly fires on the
    # plug's own port-I/O serial output. Mirrors Resolve-PlugForewords' prefixing.
    $renamed = $false
    foreach ($l in [System.IO.File]::ReadAllLines($Path)) {
        $skip = $false
        foreach ($sc in $StripCites) { if ($l -match "cites.*$sc") { $skip = $true } }
        if ($skip) { continue }
        if ($Quire -and (-not $renamed) -and $l -match '^Chapter:\s*(.+?)\s*$') {
            $Lines.Add("Chapter: $Quire--$($matches[1])")
            $renamed = $true
        } else {
            $Lines.Add($l)
        }
    }
    $Lines.Add(''); $Lines.Add('')
}

function Resolve-PlugForewords {
    param([System.Collections.Generic.List[string]]$Lines)
    $visiting = @{}
    $visited  = @{}
    $ordered  = [System.Collections.Generic.List[hashtable]]::new()
    function Resolve-PlugCite([string]$quire, [string]$name) {
        $key = "${quire}::${name}"
        if ($visited[$key]) { return }
        if ($visiting[$key]) { return }
        $visiting[$key] = $true
        $fwPath = Join-Path $script:PlugBuildRepo (Join-Path $script:QuireDirs[$quire] "$name.codex")
        if (-not (Test-Path -PathType Leaf $fwPath)) {
            [Console]::Error.WriteLine("MISSING: cited $quire chapter '$name' (expected $fwPath)")
            exit 3
        }
        foreach ($l in [System.IO.File]::ReadAllLines($fwPath)) {
            if ($l -match $script:CitePat) { Resolve-PlugCite $matches[1] $matches[2] }
        }
        [void]$visiting.Remove($key)
        $visited[$key] = $true
        [void]$ordered.Add(@{ Quire = $quire; Name = $name; Path = $fwPath })
    }
    foreach ($l in $Lines) {
        if ($l -match $script:CitePat) { Resolve-PlugCite $matches[1] $matches[2] }
    }
    $preLines = [System.Collections.Generic.List[string]]::new()
    foreach ($entry in $ordered) {
        $renamed = $false
        foreach ($l in [System.IO.File]::ReadAllLines($entry.Path)) {
            if ((-not $renamed) -and $l -match '^Chapter:\s*(.+?)\s*$') {
                [void]$preLines.Add("Chapter: $($entry.Quire)--$($matches[1])")
                $renamed = $true
            } else { [void]$preLines.Add($l) }
        }
        [void]$preLines.Add(''); [void]$preLines.Add('')
    }
    return ,$preLines
}

function Bundle-PlugSource {
    param(
        [System.Collections.Generic.List[string]]$PreLines,
        [System.Collections.Generic.List[string]]$Lines,
        [string]$BundleSrc,
        [string]$PlugName
    )
    $body = (($PreLines + $Lines) -join "`n") + "`n"
    [System.IO.File]::WriteAllText($BundleSrc, $body, [System.Text.UTF8Encoding]::new($false))
    Write-Host "[$PlugName] bundled $($PreLines.Count + $Lines.Count) lines, $($body.Length) bytes"
}

function Build-PlugCdx {
    param(
        [string]$BundleSrc,
        [string]$OutFile,
        [string]$LogFile,
        [string]$PlugName,
        [string]$Survey = ''
    )
    $compileScript = Join-Path $script:PlugBuildRepo 'build' 'compile.ps1'
    $compileArgs = @('-NoProfile', '-File', $compileScript, '-Src', $BundleSrc, '-Out', $OutFile, '-Log', $LogFile)
    if ($Survey) { $compileArgs += @('-Survey', $Survey) }
    & pwsh @compileArgs 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        [Console]::Error.WriteLine("FAIL: compile errors; see $LogFile")
        Get-Content $LogFile -ErrorAction SilentlyContinue | Select-Object -First 10 | ForEach-Object { [Console]::Error.WriteLine("  $_") }
        exit 5
    }
    $sz = (Get-Item $OutFile).Length
    Write-Host "[$PlugName] OK: $OutFile ($sz bytes)"
}

function Build-TranspilerPlug {
    param(
        [string]$PlugDir,
        [string]$PlugName,
        [string[]]$Chapters,
        [string]$Survey = ''
    )
    $outDir    = Join-Path $PlugDir 'build-output'
    $outFile   = Join-Path $outDir "$PlugName-plug.cdx"
    $bundleSrc = Join-Path $outDir 'plug-source.codex'
    $logFile   = Join-Path $outDir 'build.log'
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null

    # The plug's own chapters are trusted low-level code (they do port-I/O for
    # their serial wire protocol). Tag them with the plug's quire so the compiler
    # exempts them from the effect-declaration check, like the compiler/kernel/os
    # quires. Native-backend plug names map to the quires already in the exempt
    # list (riscv->Riscv, arm64->Arm64, pe->Pe, elf->Elf, img->Img).
    $plugQuire = $PlugName.Substring(0,1).ToUpper() + $PlugName.Substring(1)

    $lines = [System.Collections.Generic.List[string]]::new()
    Add-PlugChapter -Lines $lines -Path (Join-Path $script:PlugBuildRepo 'codex\plugs\common\PlugTypes.codex') -Quire $plugQuire
    Add-PlugChapter -Lines $lines -Path (Join-Path $script:PlugBuildRepo 'codex\plugs\common\IRTextParser.codex') -Quire $plugQuire
    foreach ($ch in $Chapters) {
        Add-PlugChapter -Lines $lines -Path (Join-Path $PlugDir "$ch.codex") -Quire $plugQuire
    }

    $preLines = Resolve-PlugForewords $lines
    Bundle-PlugSource -PreLines $preLines -Lines $lines -BundleSrc $bundleSrc -PlugName "$PlugName-plug"
    Build-PlugCdx -BundleSrc $bundleSrc -OutFile $outFile -LogFile $logFile -PlugName "$PlugName-plug" -Survey $Survey
}
