# Bundle the fib-milestone subject: the lower milestone's chapter set plus
# the IR Text Emitter (and OffsetTable, which it cites), so the chain ends
# in the compiler's own serialization instead of a structural dump. The
# harness compiles fib end to end and prints one (def ...) line per
# definition -- the same grammar the IR-CCE wire carries.
param(
    [string]$Harness = 'FibHarness.codex',
    [string]$OutName = 'fib-subject.codex'
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$here = $PSScriptRoot

. "$repo/codex/plugs/common/plug-build-lib.ps1"

$lines = [System.Collections.Generic.List[string]]::new()
# ListUtils is named here even though Core/Collections.codex cites Foreword
# chapter ListUtils and Resolve-PlugForewords therefore pulls a copy in
# already. The two copies are NOT redundant and the CDX3006 warning they
# raise is not noise.
#
# The desugarer turns every `for` into a call to map-list
# (Ast/Desugarer.codex, `is ForExpr`), a name that appears nowhere in the
# sources and that no compiler chapter cites. With a Parsmi--ListUtils in
# the unit those synthesized calls resolve inside their own quire. Drop it
# and they resolve across quires instead, which CDX3006 warns about in so
# many words: "in a chapter that defines neither it depends on the order
# the build globs files".
#
# Measured, not theorised. Removing this line left ten map-list calls in
# Desugarer and ChapterScoper with an unresolvable result type -- the plug
# reported ten "unresolved type variable T27 of map-list" markers -- while
# the check and lower subjects, whose chapter sets glob differently,
# resolved the same calls and passed. Passing by glob order is not passing.
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
                  'codex/compiler/Core/Collections.codex',
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
                  'codex/compiler/Ast/Desugarer.codex',
                  'codex/compiler/Core/SkipListText.codex',
                  'codex/compiler/Semantics/ChapterScoper.codex',
                  'codex/compiler/Semantics/NameResolver.codex',
                  'codex/compiler/Types/CodexTypeTree.codex',
                  'codex/compiler/Types/TypeEnv.codex',
                  'codex/compiler/Types/Unifier.codex',
                  'codex/compiler/Types/TypeChecker.codex',
                  'codex/compiler/Types/TypeCheckerInference.codex',
                  'codex/compiler/IR/LoweringTypes.codex',
                  'codex/compiler/IR/Lowering.codex',
                  'codex/compiler/Core/OffsetTable.codex',
                  'codex/compiler/Emit/IRTextEmitter.codex')) {
    Add-PlugChapter -Lines $lines -Path (Join-Path $repo $ch) -Quire 'Parsmi'
}
# Update 42 gave PhaseAllocator a cite of Codex chapter BootPaint, and a cite
# names a chapter, so the unit has to carry one. See BootPaintStubs.codex for
# why it is a stub and not the real 341-line screen painter.
Add-PlugChapter -Lines $lines -Path (Join-Path $here 'BootPaintStubs.codex') -Quire 'Parsmi'
Add-PlugChapter -Lines $lines -Path (Join-Path $here 'LowerStubs.codex') -Quire 'Parsmi'
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
Bundle-PlugSource -PreLines $preLines -Lines $lines -BundleSrc (Join-Path $here $OutName) -PlugName 'fib-subject'
