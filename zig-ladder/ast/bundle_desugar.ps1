# Bundle the desugar-milestone subject: everything the parse milestone
# carries, plus the AST node definitions and the desugarer, with a harness
# that parses and then desugars and dumps the AChapter. Output:
# desugar-subject.codex next to this script.
param(
    [string]$Harness = 'DesugarHarness.codex',
    [string]$OutName = 'desugar-subject.codex'
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$here = $PSScriptRoot

. "$repo/codex/plugs/common/plug-build-lib.ps1"

$lines = [System.Collections.Generic.List[string]]::new()
# ListUtils is here for a name that appears nowhere in the sources: the
# desugarer turns every `for` expression into a call to map-list
# (Ast/Desugarer.codex, `is ForExpr`), so a chapter using `for` needs
# ListUtils in the unit or codegen reports an undefined name with no
# grep-able reference to explain it. That applies doubly here, where the
# desugarer itself is in the unit.
#
# This milestone swaps LexStubs for the real Core/PhaseAllocator.codex,
# and the reason is the cite rather than the functions. Desugarer.codex
# cites 'Codex chapter Phase Allocator', which LexStubs cannot satisfy
# whatever it defines -- a cite names a chapter, not a symbol. Carrying
# both would then define deck-record and deck-short-of twice, so the stub
# steps aside. Nothing else in the unit cites Phase Allocator, which is
# why lex and parse never needed it.
#
# The real deck-short-of reads __deck-pos where the stub did not, and that
# is harmless here: the harness passes a 0 ceiling, for which both answer
# False.
foreach ($ch in @('codex/foreword/core/ListUtils.codex',
                  'codex/compiler/Core/BuildSettings.codex',
                  'codex/compiler/Core/Phase.codex',
                  'codex/compiler/Core/PhaseAllocator.codex',
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
                  'codex/compiler/Syntax/Parser.codex',
                  'codex/compiler/Ast/AstNodes.codex',
                  'codex/compiler/Ast/Desugarer.codex')) {
    Add-PlugChapter -Lines $lines -Path (Join-Path $repo $ch) -Quire 'Parsmi'
}
# Update 42 gave PhaseAllocator a cite of Codex chapter BootPaint, and a cite
# names a chapter, so the unit has to carry one. See BootPaintStubs.codex for
# why it is a stub and not the real 341-line screen painter.
Add-PlugChapter -Lines $lines -Path (Join-Path $here 'BootPaintStubs.codex') -Quire 'Parsmi'
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
Bundle-PlugSource -PreLines $preLines -Lines $lines -BundleSrc (Join-Path $here $OutName) -PlugName 'desugar-subject'
