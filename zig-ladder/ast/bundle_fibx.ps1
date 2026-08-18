# Bundle the fib-milestone subject: the lower milestone's chapter set plus
# the IR Text Emitter (and OffsetTable, which it cites), so the chain ends
# in the compiler's own serialization instead of a structural dump. The
# harness compiles fib end to end and prints one (def ...) line per
# definition -- the same grammar the IR-CCE wire carries.
param(
    [string]$Harness = 'FibxHarness.codex',
    [string]$OutName = 'fibx-subject.codex',
    [string]$PlugName = 'fibx-subject',
    # Chapters appended after the list below, and the BootPaint to carry.
    # The whole-compiler rung adds the middle end and swaps the stub for the
    # real painter; everything else about the unit is identical, so it stays
    # one list rather than two that have to be kept in step.
    [string[]]$ExtraChapters = @(),
    [string]$BootPaint = 'BootPaintStubs.codex'
)
$ErrorActionPreference = 'Stop'
$ladder = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path  # ladder-root-bootstrap: reaches the LADDER only; the checkout comes from ladder_root
$repo = (& python3 (Join-Path $ladder 'ladder_root.py') codex).Trim()
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
foreach ($ch in @('codex/foreword/core/CCE.codex',
                  'codex/compiler/Core/OffsetTable.codex',
                  'codex/compiler/Core/VmProfile.codex',
                  'codex/compiler/Types/Builtins.codex',
                  'codex/compiler/IR/Lir.codex',
                  'codex/compiler/Emit/EmitAllocator.codex',
                  'codex/compiler/Emit/CdxWriter.codex',
                  'codex/compiler/Emit/X86_64Boot.codex',
                  'codex/compiler/Emit/X86_64Encoder.codex',
                  'codex/compiler/Emit/X86_64State.codex',
                  'codex/compiler/Emit/X86_64.codex',
                  'codex/compiler/Emit/X86_64Builtins.codex',
                  'codex/compiler/Emit/X86_64Chapter.codex',
                  'codex/compiler/Emit/X86_64Compound.codex',
                  'codex/compiler/Emit/X86_64Helpers.codex',
                  'codex/compiler/Emit/X86_64IO.codex',
                  'codex/compiler/Emit/X86_64IPCHelpers.codex',
                  'codex/compiler/Emit/X86_64InsnCount.codex',
                  'codex/compiler/Emit/X86_64Lir.codex',
                  'codex/compiler/Emit/X86_64ListHelpers.codex',
                  'codex/compiler/Emit/X86_64ProcessHelpers.codex',
                  'codex/compiler/Emit/X86_64TextHelpers.codex',
                  'codex/foreword/core/ListUtils.codex',
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
                  # RESOLVE: rewrite-ir-defs. Every harness runs the driver's
                  # resolve phase now, so every subject needs this chapter --
                  # it used to arrive only via the whole rung's extras.
                  'codex/compiler/IR/ResolveTypes.codex',
                  'codex/compiler/Emit/IRTextEmitter.codex')) {
    Add-PlugChapter -Lines $lines -Path (Join-Path $repo $ch) -Quire 'Parsmi'
}
# Update 42 gave PhaseAllocator a cite of Codex chapter BootPaint, and a cite
# names a chapter, so the unit has to carry one. See BootPaintStubs.codex for
# why it is a stub and not the real 341-line screen painter.
foreach ($ch in $ExtraChapters) {
    Add-PlugChapter -Lines $lines -Path (Join-Path $repo $ch) -Quire 'Parsmi'
}
$bootPaintPath = if ($BootPaint -match '/') { Join-Path $repo $BootPaint } else { Join-Path $here $BootPaint }
Add-PlugChapter -Lines $lines -Path $bootPaintPath -Quire 'Parsmi'
Add-PlugChapter -Lines $lines -Path (Join-Path $here $Harness) -Quire 'Parsmi'

# There used to be a rename of deck-record to subj-deck-record here. It was
# right when it was written: the seed's emitter hijacked any 1-arg call
# literally named deck-record into __deck-enter/__deck-exit, which corrupts
# memory in a subject that lacks the phase-allocator runtime, and renaming
# sidestepped it.
#
# Update 43 fixed that properly, on our report: the intercept now fires only
# when deck-record and init-phase-allocator are defined in the SAME chapter,
# so a bundle without the Phase Allocator gets the plain identity it declared
# and needs no help from us.
#
# Leaving the rename in place then became the bug. The seed doing the
# compiling is upstream's, so the name it looks for is `deck-record`; we had
# renamed ours out from under it, dr-slug came back empty, and the flag was
# False for every bundle here. That switched the deck discipline off across
# the whole bundled compiler, hundreds of call sites, and stayed invisible for
# thirteen rungs because a clean compile never needs a value to outlive
# emit-all-defs's per-function __heap-restore. clamp does: bag-add parks the
# diagnostic bag on the deck, the bag was freed at the bracket instead, and
# the second diagnostic read a dangling spine.
#
# LexStubs declares deck-record unrenamed, so the stub bundles still resolve.

# All 14 pages of the X86-64 Code Generator chapter are present, so the
# 'Page N of 14' trailers stand as written; the rewrite below is inherited
# from the lir bundle and self-adjusts, a no-op here.
$pageCount = ($lines | Where-Object { $_ -match '^Page \d+ of 14$' }).Count
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^Page \d+ of 14$') {
        $lines[$i] = $lines[$i] -replace 'of 14$', "of $pageCount"
    }
}
$preLines = Resolve-PlugForewords $lines
Bundle-PlugSource -PreLines $preLines -Lines $lines -BundleSrc (Join-Path $here $OutName) -PlugName $PlugName
