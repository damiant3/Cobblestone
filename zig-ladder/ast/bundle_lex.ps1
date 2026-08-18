# Bundle the lex-milestone subject: compiler front-half chapters (lexer
# slice) + a harness chapter, using the plug build library's own chapter
# machinery. Output: <name>.codex next to this script.
param(
    [string]$Harness = 'LexHarness.codex',
    [string]$OutName = 'lex-subject.codex'
)
$ErrorActionPreference = 'Stop'
$ladder = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path  # ladder-root-bootstrap: reaches the LADDER only; the checkout comes from ladder_root
$repo = (& python3 (Join-Path $ladder 'ladder_root.py') codex).Trim()
$here = $PSScriptRoot

. "$repo/codex/plugs/common/plug-build-lib.ps1"

$lines = [System.Collections.Generic.List[string]]::new()
foreach ($ch in @('codex/compiler/Core/BuildSettings.codex',
                  'codex/compiler/Core/Phase.codex',
                  'codex/compiler/Core/TextFormat.codex',
                  'codex/compiler/Core/CdxCodes.codex',
                  'codex/compiler/Core/Severity.codex',
                  'codex/compiler/Core/SourceText.codex',
                  'codex/compiler/Core/Diagnostic.codex',
                  'codex/compiler/Syntax/Token.codex',
                  'codex/compiler/Syntax/Lexer.codex')) {
    Add-PlugChapter -Lines $lines -Path (Join-Path $repo $ch) -Quire 'Lexmi'
}
Add-PlugChapter -Lines $lines -Path (Join-Path $here 'LexStubs.codex') -Quire 'Lexmi'
Add-PlugChapter -Lines $lines -Path (Join-Path $here $Harness) -Quire 'Lexmi'

# The seed's emitter hijacks any 1-arg call literally named deck-record
# (X86_64Compound.codex emit-apply) into __deck-enter/__deck-exit region
# machinery, which corrupts memory in subjects that lack the compiler
# opening's phase-allocator runtime. Renaming keeps the identity semantics
# and sidesteps the intercept; LexStubs defines the renamed identity.
for ($i = 0; $i -lt $lines.Count; $i++) {
    $lines[$i] = $lines[$i].Replace('deck-record', 'subj-deck-record')
}

$preLines = Resolve-PlugForewords $lines
Bundle-PlugSource -PreLines $preLines -Lines $lines -BundleSrc (Join-Path $here $OutName) -PlugName 'lex-subject'
