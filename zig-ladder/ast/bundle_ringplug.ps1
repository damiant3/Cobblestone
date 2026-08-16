# Bundle the ring-fed zig plug: the transpiler-plug chapter set (same
# declarations, parser, and emitter as plugs/zig/build.ps1) with
# ZigPlugRing as the body instead of ZigPlug -- no Net or Kernel
# chapters, because the intake is the serial ring the compiler itself
# reads from.
param(
    [string]$OutName = 'ringplug-source.codex'
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$here = $PSScriptRoot

. "$repo/codex/plugs/common/plug-build-lib.ps1"

$lines = [System.Collections.Generic.List[string]]::new()
foreach ($decl in @('codex/compiler/Core/Name.codex',
                    'codex/compiler/Core/SourceText.codex',
                    'codex/compiler/Types/CodexType.codex',
                    'codex/compiler/Ast/AstNodes.codex',
                    'codex/compiler/IR/IRChapter.codex')) {
    $drop = if ($decl -like '*AstNodes.codex') { @('Deck Copies') } else { @() }
    Add-PlugChapter -Lines $lines -Path (Join-Path $repo $decl) -Quire 'Zig' -DropSections $drop
}
Add-PlugChapter -Lines $lines -Path (Join-Path $repo 'codex/plugs/common/PlugTypes.codex') -Quire 'Zig'
Add-PlugChapter -Lines $lines -Path (Join-Path $repo 'codex/plugs/common/IRTextParser.codex') -Quire 'Zig'
Add-PlugChapter -Lines $lines -Path (Join-Path $repo 'codex/plugs/zig/ZigEmitter.codex') -Quire 'Zig'
Add-PlugChapter -Lines $lines -Path (Join-Path $here 'ZigPlugRing.codex') -Quire 'Zig'

$preLines = Resolve-PlugForewords $lines
Bundle-PlugSource -PreLines $preLines -Lines $lines -BundleSrc (Join-Path $here $OutName) -PlugName 'ringplug'
