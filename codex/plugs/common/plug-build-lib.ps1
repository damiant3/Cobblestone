# plug-build-lib.ps1 -- Common plug build library -- shared functions for plug build scripts
# Generated from Codex Shell DSL. Do not edit by hand.
[CmdletBinding()]
param(
)

# Common plug build library. Source this from plug build scripts.
# Provides: Resolve-PlugForewords, Build-PlugCdx, Add-PlugChapter,
#           Bundle-PlugSource, Build-TranspilerPlug

$script:PlugBuildRepo = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..')).Path
. (Join-Path $script:PlugBuildRepo 'build' 'vm-config.ps1')

# The registry and the pattern come from build/quire-map.ps1. This used to
# DERIVE a quire table by globbing codex\foreword, codex\os, apps and
# apps\games and capitalising each directory name, which is worse than a
# copy: a copy can be compared, a derivation quietly answers a different
# question. It could not see any quire outside those four roots, so
# Wflow (codex\workflow), Boards (codex\boards) and Tracker
# (codex\tracker) did not exist here at all, and it invented names the
# registry does not use for anything whose directory name happened to
# differ. A plug citing one of those got MISSING and exit 3 for a chapter
# that is registered and present.
. (Join-Path $script:PlugBuildRepo 'build' 'quire-map.ps1')
$script:QuireDirs = $QuireDirs
$script:CitePat = $StrictCitePat


function Add-PlugChapter {
    param(
        [System.Collections.Generic.List[string]]$Lines,
        [string]$Path,
        [string[]]$StripCites = @(),
        [string]$Quire = '',
        [string[]]$DropSections = @()
    )
    # DropSections lets a compiler chapter be bundled for its DECLARATIONS while
    # leaving behind a section the plug cannot compile. Only one section needs it
    # today: AST Nodes' "Deck Copies", whose leaf copiers (copy-sx-text and
    # friends) live in SyntaxNodes, a chapter of 66 CST declarations the plug
    # never touches. Dropping that one section is what keeps SyntaxNodes out of
    # the bundle. A renamed section fails the build loudly (the declarations it
    # guarded go missing); it cannot rot silently.
    # Prefix the chapter name with the plug's quire (e.g. Riscv--RiscVPlug).
    # Mirrors Resolve-PlugForewords' prefixing.
    $renamed = $false
    $dropping = $false
    foreach ($l in [System.IO.File]::ReadAllLines($Path)) {
        if ($l -match '^Section:\s*(.+?)\s*$') {
            $dropping = $DropSections -contains $matches[1]
        }
        if ($dropping) { continue }
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
    # A cite is satisfied two ways, and the second one is why the strict
    # pattern needs this: the chapter is RESOLVED through the registry, or it
    # is already PRESENT because Add-PlugChapter bundled it. Every plug
    # codegen chapter says `cites Plug chapter Plug Types`, and there is no
    # quire named Plug -- PlugTypes.codex is added directly. The old lenient
    # pattern was built from a derived key list that had no Plug either, so
    # that cite never matched and was skipped by accident rather than by
    # rule. Same rule as Resolve-CiteOrder's, same helper.
    $present = Get-PresentChapterNames -Lines $Lines
    function Resolve-PlugCite([string]$quire, [string]$name) {
        $key = "${quire}::${name}"
        if ($visited[$key]) { return }
        if ($visiting[$key]) { return }
        $visiting[$key] = $true
        $dir = $script:QuireDirs[$quire]
        $fwPath = if ($dir) { Join-Path $script:PlugBuildRepo (Join-Path $dir "$name.codex") } else { $null }
        if ((-not $fwPath) -or (-not (Test-Path -PathType Leaf $fwPath))) {
            [void]$visiting.Remove($key)
            if ($present[(Get-CiteKey $name)]) { $visited[$key] = $true; return }
            $why = if ($dir) { "expected $fwPath" } else { "quire '$quire' is not registered in build/quire-map.ps1, and no chapter '$name' is present in the unit" }
            [Console]::Error.WriteLine("MISSING: cited $quire chapter '$name' ($why)")
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
    # Phase decks are fixed generous floors under demand paging; the old
    # type-density check-mul pin is gone with the survey system.
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
        [string]$Survey = '',
        # Bundle the compiler's LIR so a native backend can lower to it plug-side
        # (the retarget's Route A). Off by default: the transpiler plugs emit
        # high-level source and have no use for registers, so they should not
        # carry 147 KB of allocator. Lir cites Build Settings and IR Chapter and
        # LirTargets cites Lir; all three end up in this one unit, so those cites
        # are stripped rather than resolved.
        [switch]$WithLir
    )
    $outDir    = Join-Path $PlugDir 'build-output'
    $outFile   = Join-Path $outDir "$PlugName-plug.cdx"
    $bundleSrc = Join-Path $outDir 'plug-source.codex'
    $logFile   = Join-Path $outDir 'build.log'
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null

    # Namespace every chapter in the bundle under the plug's own quire.
    $plugQuire = $PlugName.Substring(0,1).ToUpper() + $PlugName.Substring(1)

    # The plug reads the compiler's own declaration chapters. There is one
    # declaration of Name, SourceSpan, CodexType, the AST nodes and the IR
    # nodes in the tree, and this is it -- the plug used to carry a second,
    # hand-maintained copy that nothing checked and no compile could see,
    # because the two never meet in one unit. Four bundle whole; AST Nodes
    # drops Deck Copies (see Add-PlugChapter).
    $lines = [System.Collections.Generic.List[string]]::new()
    foreach ($decl in @('codex\compiler\Core\Name.codex',
                        'codex\compiler\Core\SourceText.codex',
                        'codex\compiler\Types\CodexType.codex',
                        'codex\compiler\Ast\AstNodes.codex',
                        'codex\compiler\IR\IRChapter.codex')) {
        $drop = if ($decl -like '*AstNodes.codex') { @('Deck Copies') } else { @() }
        Add-PlugChapter -Lines $lines -Path (Join-Path $script:PlugBuildRepo $decl) -Quire $plugQuire -DropSections $drop
    }

    if ($WithLir) {
        # CodexTypeHelpers owns hw-width-bytes / -signed / bounds-to-hw-width and
        # Token owns pat-lit-to-integer; the lowering calls all four. Both cite
        # nothing, which is what makes Route A affordable -- they come in as
        # leaves rather than dragging the Types or Syntax quire behind them.
        foreach ($lir in @('codex\compiler\Core\BuildSettings.codex',
                           'codex\compiler\Types\CodexTypeHelpers.codex',
                           'codex\compiler\Syntax\Token.codex',
                           'codex\compiler\IR\Lir.codex',
                           'codex\compiler\IR\LirTargets.codex')) {
            Add-PlugChapter -Lines $lines -Path (Join-Path $script:PlugBuildRepo $lir) -Quire $plugQuire -StripCites @('Build Settings', 'IR Chapter', 'chapter Lir')
        }
    }
    Add-PlugChapter -Lines $lines -Path (Join-Path $script:PlugBuildRepo 'codex\plugs\common\PlugTypes.codex') -Quire $plugQuire
    Add-PlugChapter -Lines $lines -Path (Join-Path $script:PlugBuildRepo 'codex\plugs\common\IRTextParser.codex') -Quire $plugQuire
    foreach ($ch in $Chapters) {
        Add-PlugChapter -Lines $lines -Path (Join-Path $PlugDir "$ch.codex") -Quire $plugQuire
    }

    $preLines = Resolve-PlugForewords $lines
    Bundle-PlugSource -PreLines $preLines -Lines $lines -BundleSrc $bundleSrc -PlugName "$PlugName-plug"
    Build-PlugCdx -BundleSrc $bundleSrc -OutFile $outFile -LogFile $logFile -PlugName "$PlugName-plug" -Survey $Survey
}
