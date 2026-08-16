# Bundle the IRTextParser memory probe: the parser, the type chapters it
# builds into, and a harness that prints per-stage heap deltas. No lexer,
# no checker, no emitter, no transport -- the sample IR is an embedded
# literal, so the unit is the parser's own dependency closure and
# nothing else.
param(
    [string]$Harness = 'IrmemHarness.codex',
    [string]$OutName = 'irmem-subject.codex'
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$here = $PSScriptRoot

. "$repo/codex/plugs/common/plug-build-lib.ps1"

$lines = [System.Collections.Generic.List[string]]::new()
foreach ($ch in @('codex/foreword/core/ListUtils.codex',
                  'codex/compiler/Core/Name.codex',
                  'codex/compiler/Core/SourceText.codex',
                  'codex/compiler/Types/CodexType.codex',
                  'codex/compiler/IR/IRChapter.codex',
                  'codex/compiler/Syntax/Token.codex',
                  'codex/compiler/Syntax/SyntaxNodes.codex',
                  'codex/compiler/Ast/AstNodes.codex',
                  'codex/plugs/common/PlugTypes.codex',
                  'codex/plugs/common/IRTextParser.codex')) {
    Add-PlugChapter -Lines $lines -Path (Join-Path $repo $ch) -Quire 'Parsmi'
}
Add-PlugChapter -Lines $lines -Path (Join-Path $here $Harness) -Quire 'Parsmi'

$preLines = Resolve-PlugForewords $lines
Bundle-PlugSource -PreLines $preLines -Lines $lines -BundleSrc (Join-Path $here $OutName) -PlugName 'irmem-subject'
