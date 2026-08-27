# Build a plug as a WebAssembly module that reads IR on stdin and writes the
# target on stdout.
#
# The plug openings in the tree take their IR over NE2K and answer over TCP,
# which is all a plug ever needed while a plug only ever ran on bare metal
# behind a socket. A wasm build has neither a NIC nor a socket. This assembles
# the SAME emitter against codex/plugs/common/PlugStdio.codex instead of the
# plug's network entry, so both transports exist and neither replaces the
# other: <plug>/build.ps1 still produces the network CDX and is untouched.
#
# The plug supplies one function, in <PlugDir>/<Chapter>.codex:
#     plug-emit-ir : Text -> Text
# PlugStdio supplies the opening that calls it.
#
# Usage:
#   codex/plugs/common/build-plug-wasm.ps1 -Plug javascript `
#       -Chapters JavaScriptEmitter,JavaScriptStdio
#
# -Transport bytes builds the other kind of plug: elf, pe and img take a
# COMPILED PAYLOAD rather than IR text, so they get PlugBytes instead of
# PlugStdio and none of the IR declaration chapters, which they have never
# needed -- codex/plugs/elf/build.ps1 bundles no IR chapter either. The plug
# supplies, in <PlugDir>/<Chapter>.codex:
#     plug-emit-bytes : Integer, Integer -> [Console] Nothing
# taking a buffer address and a byte count.
#
#   codex/plugs/common/build-plug-wasm.ps1 -Plug elf -Transport bytes `
#       -Chapters ByteHelpers,ElfWriter,DwarfWriter,'ElfPlug:Network Config|Drain|Body',ElfStdio
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Plug,
    [Parameter(Mandatory)][string[]]$Chapters,
    [ValidateSet('ir', 'bytes')][string]$Transport = 'ir',
    [string]$Kernel,
    [string]$OutDir
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'plug-build-lib.ps1')

$Repo    = $script:PlugBuildRepo
$PlugDir = Join-Path $Repo "codex\plugs\$Plug"
if (-not (Test-Path -PathType Container $PlugDir)) {
    Write-Host "REFUSE: no plug at $PlugDir"; exit 2
}
if (-not $Kernel) { $Kernel = Join-Path $Repo 'seed\Codex.cdx' }
if (-not $OutDir) { $OutDir = Join-Path $PlugDir 'build-output' }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

foreach ($tool in @('wat2wasm')) {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        Write-Host "REFUSE: $tool is not on PATH."; exit 2
    }
}

# A name of its own. Build-TranspilerPlug hardcodes plug-source.codex, and
# overwriting that would leave the network build's bundle looking like this
# one.
$suffix    = if ($Transport -eq 'bytes') { 'bytes' } else { 'stdio' }
$bundleSrc = Join-Path $OutDir "plug-source-$suffix.codex"
$wat       = Join-Path $OutDir "$Plug-$suffix.wat"
$wasm      = Join-Path $OutDir "$Plug-$suffix.wasm"

$plugQuire = $Plug.Substring(0,1).ToUpper() + $Plug.Substring(1)

# pwsh -File hands every argument over as a string, so -Chapters A,B arrives
# as the single element "A,B" rather than two. Split rather than depend on
# how the caller invoked this.
$Chapters = @($Chapters | ForEach-Object { $_ -split ',' } | Where-Object { $_ -ne '' })

# Same declaration chapters Build-TranspilerPlug bundles: one declaration of
# Name, SourceSpan, CodexType, the AST nodes and the IR nodes in the tree.
$lines = [System.Collections.Generic.List[string]]::new()
if ($Transport -eq 'ir') {
    foreach ($decl in @('codex\compiler\Core\Name.codex',
                        'codex\compiler\Core\SourceText.codex',
                        'codex\compiler\Types\CodexType.codex',
                        'codex\compiler\Ast\AstNodes.codex',
                        'codex\compiler\IR\IRChapter.codex')) {
        $drop = if ($decl -like '*AstNodes.codex') { @('Deck Copies') } else { @() }
        Add-PlugChapter -Lines $lines -Path (Join-Path $Repo $decl) -Quire $plugQuire -DropSections $drop
    }
    Add-PlugChapter -Lines $lines -Path (Join-Path $Repo 'codex\plugs\common\PlugTypes.codex')   -Quire $plugQuire
    Add-PlugChapter -Lines $lines -Path (Join-Path $Repo 'codex\plugs\common\IRTextParser.codex') -Quire $plugQuire
    Add-PlugChapter -Lines $lines -Path (Join-Path $Repo 'codex\plugs\common\PlugStdio.codex')    -Quire $plugQuire
} else {
    Add-PlugChapter -Lines $lines -Path (Join-Path $Repo 'codex\plugs\common\PlugBytes.codex')    -Quire $plugQuire
}
# A chapter may be written Name:Sec1|Sec2 to drop those sections from it.
# That is how a plug whose shared helpers live in its NETWORK entry chapter
# rides this without duplicating them: bundle the chapter, drop the sections
# that ARE the transport (its constants, its drain, its opening), and keep the
# single definition of everything else. csharp needs exactly that, because
# stream-defs-sexp and collect-mut-names sit in CSharpPlug.codex.
foreach ($ch in $Chapters) {
    $name = $ch; $drop = @()
    if ($ch -match '^([^:]+):(.+)$') {
        $name = $Matches[1]
        $drop = @($Matches[2] -split '\|' | Where-Object { $_ -ne '' })
    }
    # A bytes plug's chapter list names shared helpers (ByteHelpers, PlugChain)
    # that live in common/ rather than in the plug's own directory. Look there
    # second, and REFUSE rather than let ReadAllLines throw a path at the reader.
    $chPath = Join-Path $PlugDir "$name.codex"
    if (-not (Test-Path -PathType Leaf $chPath)) { $chPath = Join-Path $Repo "codex\plugs\common\$name.codex" }
    if (-not (Test-Path -PathType Leaf $chPath)) {
        Write-Host "REFUSE: no chapter '$name' in $PlugDir or codex\plugs\common"; exit 2
    }
    Add-PlugChapter -Lines $lines -Path $chPath -Quire $plugQuire -DropSections $drop
}

$preLines = Resolve-PlugForewords $lines
Bundle-PlugSource -PreLines $preLines -Lines $lines -BundleSrc $bundleSrc -PlugName "$Plug-$suffix"

Write-Host "[$Plug-$suffix] emitting wat through the wasm plug ..."
& pwsh -NoProfile -File (Join-Path $Repo 'codex\plugs\wasm\run.ps1') -Src $bundleSrc -Out $wat -Kernel $Kernel
if ($LASTEXITCODE -ne 0) { Write-Host "[$Plug-$suffix] FAIL: wasm emission"; exit 3 }

& wat2wasm --enable-tail-call $wat -o $wasm
if ($LASTEXITCODE -ne 0) { Write-Host "[$Plug-$suffix] FAIL: wat2wasm"; exit 4 }

Write-Host "[$Plug-$suffix] OK: $wasm ($((Get-Item $wasm).Length) bytes)"
