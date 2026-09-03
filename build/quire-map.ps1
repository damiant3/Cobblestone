# quire-map.ps1 -- Single quire registry and cite resolver
# GENERATED FROM THE CODEX SHELL DSL. Do not edit by hand.
# A hand edit here must NOT be submitted. Change the generator under
# codex/build/, regenerate, and submit the generator and this file
# together. Until then build/check-generated-scripts.ps1 reports this
# file as drifted, and the next regeneration discards the edit.
[CmdletBinding()]
param(
)

# The single quire registry and cite resolver. Dot-source this file;
# never copy the map. Every script that resolves
#   cites <Quire> chapter <Name>
# (bundlers, compile, test batch, plug builds, linters) must take
# $QuireDirs and $CitePat from here.
# Add new quires HERE and nowhere else. Paths are repo-relative.

$QuireDirs = @{
    'Foreword' = 'codex\foreword\core'; 'Kernel' = 'codex\os\kernel'; 'OS' = 'codex\os\core'
    'Works' = 'apps\works'; 'Trust' = 'codex\os\trust'; 'Net' = 'codex\os\net'
    'Verify' = 'codex\os\verify'; 'Replay' = 'codex\os\replay'; 'Sched' = 'codex\os\sched'
    # Wflow, not Workflow: 'Workflow' is apps\workflow, a different quire. The
    # cites have always said Wflow; what was missing was this line, so every
    # chapter they name was silently left out of the unit and the author got
    # CDX3002 at the use sites instead.
    'Wflow' = 'codex\workflow'; 'Tracker' = 'codex\tracker'
    'Observe' = 'codex\os\observe'; 'Game' = 'codex\foreword\game'
    'Signal' = 'codex\foreword\signal'; 'Compress' = 'codex\foreword\compress'
    'Encode' = 'codex\foreword\encode'; 'Math' = 'codex\foreword\math'
    'Sim' = 'codex\foreword\sim'; 'AI' = 'codex\foreword\ai'
    'Engine' = 'codex\foreword\engine'
    'Punctual' = 'codex\foreword\punctual'
    'Gpu' = 'codex\foreword\gpu'; 'Shell' = 'codex\foreword\shell'
    'UI' = 'codex\foreword\ui'; 'Dev' = 'codex\os\dev'
    'Magic' = 'apps\games\magic'; 'Games' = 'apps\games\classic'
    'Spark' = 'apps\spark'; 'Data' = 'apps\data'
    'WaDemo' = 'apps\wademo'
    'Explorer' = 'apps\explorer'; 'FontExplorer' = 'apps\fontexplorer'
    'CodexMagic' = 'apps\games\codexmagic'
    'Mathbook' = 'apps\mathbook'
    'Cvmm' = 'apps\cvmm'
    'Nettool' = 'apps\nettool'
    'Browser' = 'apps\browser'
    'FileShare' = 'apps\fileshare'
    'Chat' = 'apps\chat'
    'Designer' = 'apps\designer'
    'Diagram' = 'apps\diagram'
    'Secrets' = 'apps\secrets'
    'MobileApp' = 'apps\codexmagic-mobile'
    'Starmap' = 'apps\starmap'
    'Globe' = 'apps\globe'
    'Safari' = 'apps\safari\port'
    'Judge' = 'apps\safari\judge'
    'Gold' = 'apps\safari\gold'
    'Radio' = 'apps\radio'
    'Mail' = 'apps\mail'
    'Music' = 'apps\music'
    'Photos' = 'apps\photos'
    'Notes' = 'apps\notes'
    'Tasks' = 'apps\tasks'
    'Weather' = 'apps\weather'
    'Calendar' = 'apps\calendar'
    'Maps' = 'apps\maps'
    'News' = 'apps\news'
    'Podcasts' = 'apps\podcasts'
    'Books' = 'apps\books'
    'Recorder' = 'apps\recorder'
    'Capture' = 'apps\capture'
    'Publisher' = 'apps\publisher'
    'ImageTools' = 'apps\imagetools'
    'Fitness' = 'apps\fitness'
    'Pomodoro' = 'apps\pomodoro'
    'Piano' = 'apps\piano'
    'Markets' = 'apps\markets'
    'FishTank' = 'apps\fishtank'
    'Helm' = 'apps\helm'
    'Colophon' = 'apps\colophon'
    'WebApp' = 'apps\webapp'
    'ERP' = 'apps\erp'
    'Vision' = 'apps\vision'
    'Collab' = 'apps\collab'
    'Services' = 'apps\services'; 'Accounts' = 'apps\services\accounts'
    'Market' = 'apps\market'
    'Workflow' = 'apps\workflow'
    'Site' = 'apps\site'
    'Lens' = 'apps\lens'
    'Boards' = 'codex\boards'
    'Product' = 'codex\product'
    'Mesh' = 'apps\edgemesh'
    'Water' = 'shaders\water'
    'Clouds' = 'shaders\clouds'
    'Terrain' = 'shaders\terrain'
    'Reflect' = 'shaders\reflect'
    'Guios' = 'apps\guios'
    'C64' = 'apps\c64'
    'Ideas' = 'apps\ideas'
    # 'Circuits' is the Core sub-quire. The chapters at the circuits ROOT
    # (CanvasModel, CircuitsTheme, CircuitsUI, ViewState, ...) are the app
    # shell and had no quire at all, so opening.codex could not cite them
    # and every name they define reported CDX3002 at the use site.
    'CircuitsApp' = 'apps\circuits'
    'Circuits' = 'apps\circuits\Core'
    'CircuitsSch' = 'apps\circuits\SchematicEditor'
    'CircuitsSym' = 'apps\circuits\SymbolEditor'
    'CircuitsSim' = 'apps\circuits\Simulator'
    'CircuitsPcb' = 'apps\circuits\PcbEditor'
    'CircuitsFp' = 'apps\circuits\FootprintEditor'
    'CircuitsBv' = 'apps\circuits\BoardViewer'
    'CircuitsMfg' = 'apps\circuits\Manufacturing'
    # The diagnostic ladder and its stage chapters (DiagnosticStick.md):
    # a stage is a chapter under build\boot\diag cited as Diag chapter X.
    'Diag' = 'build\boot\diag'
}


function New-CitePattern {
    # Alternation sorted longest-first so 'Games' wins over 'Game'.
    #
    # This pattern is generated from the KEYS of $QuireDirs, so a cite to an
    # unregistered quire does not match it, is never walked, and is never
    # resolved: the chapter is silently left out of the unit and the author gets
    # a cascade of CDX3002 at every USE SITE and never once at the cite. That is
    # what $StrictCitePat below exists to stop, and it is what Resolve-CiteOrder
    # now uses by default.
    #
    # The two bundlers this was kept for no longer carry their own copies:
    # concat-codex-self.ps1 and plug-build-lib.ps1 both take $StrictCitePat
    # and $QuireDirs from here, so NOTHING in the tree calls
    # this any more. It is kept only as the explicit way to ask for the old
    # blind-to-unregistered behaviour, and there is no good reason to.
    # Where a bundler must admit fewer quires than the registry holds, that
    # is policy and belongs at its own call site as a filter over these keys
    # -- concat-codex-self's $libQuireNames is the worked example -- not as a
    # second pattern that reads the same text a different way.
    param([string[]]$ExtraQuires = @())
    $names = @($QuireDirs.Keys) + $ExtraQuires
    $alt = ($names | Sort-Object -Descending { $_.Length }) -join '|'
    return "^\s*cites\s+($alt)\s+chapter\s+([A-Za-z_][A-Za-z0-9_-]*)"
}

# The strict pattern takes the quire as any identifier rather than an
# alternation of the registered keys, which is the whole point: a cite naming a
# quire nobody registered now MATCHES, is walked, and fails loudly instead of
# vanishing. Three shapes it has to accept that the lenient one cannot:
# 
#   cites Foreword chapter Sha256                     plain
#   cites Codex chapter Build Settings                a name with a SPACE
#   cites Codex chapter Build Settings (max-depth)    ...and a trailing note
# 
# Real headers include 'AST Nodes', 'Build Settings' and 'Diagnostic Bag', and
# the convention annotates a cite with the definition it wanted. The lenient
# name capture stops at the space and reads that last line as name 'Build'.
$StrictCitePat = '^\s*cites\s+([A-Za-z_][A-Za-z0-9_]*)\s+chapter\s+([A-Za-z_][A-Za-z0-9_ -]*?)\s*(?:\(.*)?$'

$CitePat = New-CitePattern


function Get-CiteKey {
    # Cites name a chapter two different ways and both are in the tree:
    # 'cites Common chapter ByteHelpers' is the FILE's name, while
    # 'cites Codex chapter Build Settings' is the HEADER's. The file is
    # ByteHelpers.codex holding 'Chapter: Byte Helpers', so neither form is
    # wrong and no lookup keyed on one of them finds the other. Comparing with
    # the spaces removed is what makes the two forms the same key.
    param([string]$Name)
    return ($Name -replace '\s', '').ToLowerInvariant()
}

function Get-PresentChapterNames {
    # The presence seed, keyed on the bare chapter name.
    #
    # A cite is satisfied two ways, not one: the chapter is RESOLVED through the
    # registry, or it is already PRESENT in the unit because a bundler globbed
    # the directory it lives in and renamed its header to <Dir>--<Name>. The
    # compiler's own source is assembled that way -- concat-codex-self.ps1 globs
    # codex/compiler -- so 'cites Codex chapter Build Settings' is answered by
    # 'Chapter: Core--Build Settings' sitting in the same unit, and the registry
    # has no entry for 'Codex' at all and should not.
    #
    # This keys on the NAME and not on quire::name, which is the reason the
    # existing SeedSeen cannot do the job: concat renames by DIRECTORY (Core--)
    # while the cite names a QUIRE (Codex), so the two never share a key.
    param([string[]]$Lines)
    $present = @{}
    foreach ($l in $Lines) {
        if ($l -match '^Chapter:\s*(.+?)\s*$') {
            $full = $matches[1]
            $present[(Get-CiteKey $full)] = $true
            # 'Core--Build Settings' also answers to 'Build Settings'.
            $sep = $full.IndexOf('--')
            if ($sep -ge 0) { $present[(Get-CiteKey $full.Substring($sep + 2))] = $true }
        }
    }
    return $present
}


function Resolve-CiteOrder {
    # DFS post-order over the cite graph: a chapter's dependencies always
    # precede it in the returned list. BFS+Reverse is NOT a topological
    # order (CL 3644) -- do not reintroduce it.
    # Returns a list of @{ Quire; Name; Path; Lines }.
    param(
        [Parameter(Mandatory=$true)] [AllowEmptyString()] [AllowEmptyCollection()] [string[]]$RootLines,
        [string]$Repo = '.',
        [string]$Pattern = '',
        [hashtable]$SeedSeen = $null,
        [string[]]$ExcludeQuires = @(),
        [scriptblock]$PathOverride = $null,
        [hashtable]$PresentNames = @{},
        [ValidateSet('throw','skip')] [string]$OnMissing = 'throw'
    )
    if (-not $Pattern) { $Pattern = $StrictCitePat }
    $visited = @{}
    $visiting = @{}
    if ($SeedSeen) { foreach ($k in $SeedSeen.Keys) { $visited[$k] = $true } }
    # Chapters already in the unit satisfy a cite by being there. Seeded from
    # the root lines themselves, so a caller that bundles before it resolves
    # (the plugs, concat) needs no new argument and cannot forget to pass one.
    $present = Get-PresentChapterNames -Lines $RootLines
    foreach ($k in @($PresentNames.Keys)) { $present[(Get-CiteKey $k)] = $true }
    $excluded = @{}
    foreach ($q in $ExcludeQuires) { $excluded[$q] = $true }
    $ordered = [System.Collections.Generic.List[hashtable]]::new()
    $walk = $null
    $walk = {
        param([string]$quire, [string]$name)
        if ($excluded[$quire]) { return }
        $key = "${quire}::${name}"
        if ($visited[$key] -or $visiting[$key]) { return }
        $visiting[$key] = $true
        $path = $null
        if ($PathOverride) { $path = & $PathOverride $quire $name }
        if (-not $path) {
            $dir = $QuireDirs[$quire]
            if ($dir) { $path = Join-Path $Repo (Join-Path $dir "$name.codex") }
        }
        if (-not $path -or -not (Test-Path -PathType Leaf $path)) {
            $visiting.Remove($key)
            # Already in the unit: satisfied, and there is nothing to walk.
            if ($present[(Get-CiteKey $name)]) { $visited[$key] = $true; return }
            if ($OnMissing -eq 'skip') { $visited[$key] = $true; return }
            $why = if ($QuireDirs[$quire]) {
                "quire '$quire' is registered as '$($QuireDirs[$quire])' but has no chapter '$name' (expected $path)"
            } else {
                "quire '$quire' is not registered in build/quire-map.ps1, and no chapter '$name' is present in the unit"
            }
            throw "Unresolvable cite: $quire chapter '$name' -- $why"
        }
        $chLines = [System.IO.File]::ReadAllLines($path)
        foreach ($l in $chLines) {
            if ($l -match $Pattern) { & $walk $matches[1] $matches[2] }
        }
        $visiting.Remove($key)
        $visited[$key] = $true
        $ordered.Add(@{ Quire = $quire; Name = $name; Path = $path; Lines = $chLines })
    }
    # The sugars' own dependencies, walked whether or not the source cites
    # them. `for x in xs -> ...` desugars to a call to `map-list`
    # (Foreword ListUtils) and a tuple literal or pattern to `MkTup<N>`
    # (Foreword Tuple). Both are names the DESUGARER writes, not the author,
    # so requiring the author to cite them made the language's own syntax
    # conditional on a line nobody could know to write: `for` without the
    # cite failed with `CDX3002: Undefined name: map-list`, and a tuple with
    # `CDX2002: Unknown name: MkTup2`.
    #
    # Unconditional rather than "only if the source uses the sugar" because
    # detecting use means parsing sugar with a regex, and getting that wrong
    # fails the same way this did. The two chapters are 141 lines and cite
    # nothing, so there is no cascade -- the cost is a fixed 5 KB per unit.
    #
    # A chapter that defines its own `map-list` still wins inside itself:
    # ChapterScoper mangles both sides of a collision per chapter, so a
    # mention resolves to its own chapter's. That is the same rule the register
    # 2.15 settled for builtins, and it is why this is safe to do for every
    # unit rather than only for units that would otherwise fail. It does
    # raise CDX3006 (a warning) where a unit now carries two definitions of
    # a name; `$present` and `$excluded` are both honoured, so a unit that
    # already bundles these chapters, or excludes the Foreword quire, is
    # untouched.
    foreach ($impl in @(@('Foreword','ListUtils'), @('Foreword','Tuple'))) {
        & $walk $impl[0] $impl[1]
    }
    foreach ($l in $RootLines) {
        if ($l -match $Pattern) { & $walk $matches[1] $matches[2] }
    }
    return ,$ordered
}


function Get-DiagRegions {
    # Maps a unit line number back to the file it came from.
    #
    # The compiler numbers diagnostics against the assembled UNIT -- every
    # cited chapter, then the source -- so a source line is reported at
    # (prelude + its own line). Every position a user sees in a file that
    # cites anything is wrong by however many lines its dependencies run to;
    # a cite-less file was right only by accident, because its unit was
    # itself. Measured: an error on line 11 of a file citing one foreword
    # chapter reported as 214:5.
    #
    # Both assemblers must map back or only one of them tells the truth --
    # compile.ps1 for a single compile and test-compile-batch.ps1 for the
    # battery, which builds its own unit and writes its own build.log. That
    # is why this lives here rather than in either of them.
    #
    # Format-CiteChapters replaces a chapter's `Chapter:` line one-for-one
    # and appends two blank lines, so an entry occupies Lines.Count + 2.
    param([Parameter(Mandatory=$true)] $Ordered, [Parameter(Mandatory=$true)] [string]$SrcPath)
    $regions = [System.Collections.Generic.List[hashtable]]::new()
    $cum = 0
    foreach ($entry in $Ordered) {
        $n = $entry.Lines.Count + 2
        [void]$regions.Add(@{ Start = $cum + 1; End = $cum + $n; File = $entry.Path })
        $cum += $n
    }
    [void]$regions.Add(@{ Start = $cum + 1; End = [int]::MaxValue; File = $SrcPath })
    return ,$regions
}

function Convert-DiagLine {
    # `<unit-line>:<col>: ...` becomes `<file>:<file-line>:<col>: ...`.
    # Anything without a position prefix passes through untouched, so
    # protocol lines (SIZE:, HEAP:, !EXC, MAP:) are unaffected.
    param([string]$Line, $Regions)
    if ($Regions -and $Line -match '^(\d+):(\d+):(.*)$') {
        $ul = [int]$matches[1]; $col = $matches[2]; $rest = $matches[3]
        foreach ($r in $Regions) {
            if ($ul -ge $r.Start -and $ul -le $r.End) {
                return "$($r.File):$($ul - $r.Start + 1):${col}:$rest"
            }
        }
    }
    return $Line
}


function Format-CiteChapters {
    # Renders resolved chapters as concatenation-ready lines, renaming
    # each header to `Chapter: Quire--Name` to avoid collisions.
    param([Parameter(Mandatory=$true)] $Ordered)
    $out = [System.Collections.Generic.List[string]]::new()
    foreach ($entry in $Ordered) {
        $renamed = $false
        foreach ($l in $entry.Lines) {
            if ((-not $renamed) -and $l -match '^Chapter:\s*(.+?)\s*$') {
                $out.Add("Chapter: $($entry.Quire)--$($matches[1])")
                $renamed = $true
            } else { $out.Add($l) }
        }
        $out.Add(''); $out.Add('')
    }
    return ,$out
}
