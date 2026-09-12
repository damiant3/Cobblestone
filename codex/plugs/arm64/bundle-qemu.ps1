# Bundle the ARM64 plug into $SANDBOX for a cobblestone-qemu compile.
param(
    [string]$OutName = 'arm64plug-source.codex'
)
$ErrorActionPreference = 'Stop'
$repo = $env:CODEX_ROOT
if (-not $repo) { throw "CODEX_ROOT is not set" }
$out = $env:SANDBOX
if (-not $out) { throw "no SANDBOX: nowhere to write" }
$PlugDir = $PSScriptRoot

. (Join-Path $repo 'codex/plugs/common/plug-build-lib.ps1')

$lines = [System.Collections.Generic.List[string]]::new()
foreach ($decl in @('codex/compiler/Core/Name.codex',
                    'codex/compiler/Core/SourceText.codex',
                    'codex/compiler/Types/CodexType.codex',
                    'codex/compiler/Ast/AstNodes.codex',
                    'codex/compiler/IR/IRChapter.codex')) {
    $drop = if ($decl -like '*AstNodes.codex') { @('Deck Copies') } else { @() }
    Add-PlugChapter -Lines $lines -Path (Join-Path $repo $decl) -Quire 'Arm64' -DropSections $drop
}
foreach ($lir in @('codex/compiler/Core/BuildSettings.codex',
                   'codex/compiler/Types/CodexTypeHelpers.codex',
                   'codex/compiler/Syntax/Token.codex',
                   'codex/compiler/IR/Lir.codex',
                   'codex/compiler/IR/LirTargets.codex')) {
    Add-PlugChapter -Lines $lines -Path (Join-Path $repo $lir) -Quire 'Arm64' -StripCites @('Build Settings', 'IR Chapter', 'chapter Lir')
}
Add-PlugChapter -Lines $lines -Path (Join-Path $repo 'codex/plugs/common/PlugTypes.codex') -Quire 'Arm64'
Add-PlugChapter -Lines $lines -Path (Join-Path $repo 'codex/plugs/common/IRTextParser.codex') -Quire 'Arm64'
Add-PlugChapter -Lines $lines -Path (Join-Path $repo 'codex/plugs/common/PlugManifest.codex') -Quire 'Arm64'
foreach ($ch in @('Arm64Runtime', 'Arm64CodeGen', 'Arm64CodeGen2', 'Arm64Lir', 'Arm64CodeGen3', 'Arm64Disasm', 'Arm64Plug')) {
    Add-PlugChapter -Lines $lines -Path (Join-Path $PlugDir "$ch.codex") -Quire 'Arm64'
}
$preLines = Resolve-PlugForewords $lines
Bundle-PlugSource -PreLines $preLines -Lines $lines -BundleSrc (Join-Path $out $OutName) -PlugName 'arm64-plug'
Write-Host (Join-Path $out $OutName)
