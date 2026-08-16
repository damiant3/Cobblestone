# Bundle the lir-milestone subject: the first back-half rung, and the
# first bundle with NO front end at all -- no lexer, parser or checker.
# The harness hand-builds LIR, so the set is the selector (X86_64Lir),
# its state and encoder, the LIR types and allocator (IR/Lir), and the
# Core chapters CodegenState's fields reach. Output: lir-subject.codex.
#
# Cites that force chapters in: X86_64State cites VM Profile and Build
# Settings (plus Foreword MemoryMap, which the resolver pulls); IR/Lir
# cites Build Settings and IR Chapter.
param(
    [string]$Harness = 'LirHarness.codex',
    [string]$OutName = 'lir-subject.codex'
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$here = $PSScriptRoot

. "$repo/codex/plugs/common/plug-build-lib.ps1"

$lines = [System.Collections.Generic.List[string]]::new()
# CCE is named outright because X86_64State reads cce-to-unicode-table
# without citing the chapter -- the same uncited-foreword-dependency class
# as finding 4 on PR 64, in a page his subset-cites gate does not cover.
foreach ($ch in @('codex/foreword/core/ListUtils.codex',
                  'codex/foreword/core/CCE.codex',
                  'codex/compiler/Core/OffsetTable.codex',
                  'codex/compiler/Syntax/Token.codex',
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
                  'codex/compiler/Core/SkipListText.codex',
                  'codex/compiler/Core/VmProfile.codex',
                  'codex/compiler/Types/CodexType.codex',
                  'codex/compiler/Types/TypeEnv.codex',
                  'codex/compiler/Types/CodexTypeHelpers.codex',
                  'codex/compiler/IR/IRChapter.codex',
                  'codex/compiler/IR/Lir.codex',
                  'codex/compiler/Emit/EmitAllocator.codex',
                  'codex/compiler/Emit/X86_64Encoder.codex',
                  'codex/compiler/Emit/X86_64State.codex',
                  'codex/compiler/Emit/X86_64Lir.codex')) {
    Add-PlugChapter -Lines $lines -Path (Join-Path $repo $ch) -Quire 'Parsmi'
}
# PhaseAllocator cites Codex chapter BootPaint since Update 42; the stub
# stands in for the 341-line screen painter, same as every other bundle.
Add-PlugChapter -Lines $lines -Path (Join-Path $here 'BootPaintStubs.codex') -Quire 'Parsmi'
# TypeEnv reads the builtin-spec table; CheckStubs carries it with bs-emit
# stripped, the same stand-in the check and lower bundles use.
Add-PlugChapter -Lines $lines -Path (Join-Path $here 'CheckStubs.codex') -Quire 'Parsmi'
Add-PlugChapter -Lines $lines -Path (Join-Path $here 'LirStubs.codex') -Quire 'Parsmi'
Add-PlugChapter -Lines $lines -Path (Join-Path $here $Harness) -Quire 'Parsmi'

# The Emit/X86_64*.codex files are pages of ONE chapter spanning 14 files,
# and each carries a 'Page N of 14' trailer; CDX3004 requires the declared
# count to equal the pages present. Three ride in this bundle.
$pageCount = ($lines | Where-Object { $_ -match '^Page \d+ of 14$' }).Count
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^Page \d+ of 14$') {
        $lines[$i] = $lines[$i] -replace 'of 14$', "of $pageCount"
    }
}

# Same rename every bundle applies: the seed's emitter intercepts a 1-arg
# call literally named deck-record (X86_64Compound.codex emit-apply) into
# region machinery that corrupts subjects lacking the compiler opening's
# runtime. LirStubs defines the renamed identity.
for ($i = 0; $i -lt $lines.Count; $i++) {
    $lines[$i] = $lines[$i].Replace('deck-record', 'subj-deck-record')
}

$preLines = Resolve-PlugForewords $lines
Bundle-PlugSource -PreLines $preLines -Lines $lines -BundleSrc (Join-Path $here $OutName) -PlugName 'lir-subject'
