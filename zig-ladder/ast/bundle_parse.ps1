# Bundle the parse-milestone subject: the lex chapters plus the parser's
# own three files and the CST node definitions, with a harness that dumps
# the parsed Document. Output: parse-subject.codex next to this script.
param(
    [string]$Harness = 'ParseHarness.codex',
    [string]$OutName = 'parse-subject.codex'
)
$ErrorActionPreference = 'Stop'
$ladder = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path  # ladder-root-bootstrap: reaches the LADDER only; the checkout comes from ladder_root
$repo = (& python3 (Join-Path $ladder 'ladder_root.py') codex).Trim()
$here = $PSScriptRoot

. "$repo/codex/plugs/common/plug-build-lib.ps1"

$lines = [System.Collections.Generic.List[string]]::new()
# ListUtils is here for a name that appears nowhere in the sources: the
# desugarer turns every `for` expression into a call to map-list
# (Ast/Desugarer.codex, `is ForExpr`), so a chapter using `for` needs
# ListUtils in the unit or codegen reports an undefined name with no
# grep-able reference to explain it.
foreach ($ch in @('codex/foreword/core/ListUtils.codex',
                  'codex/compiler/Core/BuildSettings.codex',
                  'codex/compiler/Core/Phase.codex',
                  'codex/compiler/Core/TextFormat.codex',
                  'codex/compiler/Core/CdxCodes.codex',
                  'codex/compiler/Core/Severity.codex',
                  'codex/compiler/Core/SourceText.codex',
                  'codex/compiler/Core/Name.codex',
                  'codex/compiler/Core/Diagnostic.codex',
                  'codex/compiler/Core/DiagnosticBag.codex',
                  'codex/compiler/Types/CodexType.codex',
                  'codex/compiler/Types/CodexTypeHelpers.codex',
                  'codex/compiler/IR/IRChapter.codex',
                  'codex/compiler/Syntax/Token.codex',
                  'codex/compiler/Syntax/Lexer.codex',
                  'codex/compiler/Syntax/SyntaxNodes.codex',
                  'codex/compiler/Syntax/ParserCore.codex',
                  'codex/compiler/Syntax/ParserExpressions.codex',
                  'codex/compiler/Syntax/Parser.codex')) {
    Add-PlugChapter -Lines $lines -Path (Join-Path $repo $ch) -Quire 'Parsmi'
}
Add-PlugChapter -Lines $lines -Path (Join-Path $here 'LexStubs.codex') -Quire 'Parsmi'
Add-PlugChapter -Lines $lines -Path (Join-Path $here $Harness) -Quire 'Parsmi'

# The seed's emitter hijacks any 1-arg call literally named deck-record
# (X86_64Compound.codex emit-apply) into __deck-enter/__deck-exit region
# machinery, which corrupts memory in subjects that lack the compiler
# opening's phase-allocator runtime. Renaming keeps the identity semantics
# and sidesteps the intercept; LexStubs defines the renamed identity.
for ($i = 0; $i -lt $lines.Count; $i++) {
    $lines[$i] = $lines[$i].Replace('deck-record', 'subj-deck-record')
}

$preLines = Resolve-PlugForewords $lines
Bundle-PlugSource -PreLines $preLines -Lines $lines -BundleSrc (Join-Path $here $OutName) -PlugName 'parse-subject'
