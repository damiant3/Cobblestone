# The single quire registry and cite resolver. Dot-source this file;
# never copy the map. Every script that resolves
# `cites <Quire> chapter <Name>` (bundlers, compile, test batch,
# plug builds, linters) must take $QuireDirs and $CitePat from here.
# Add new quires HERE and nowhere else. Paths are repo-relative.

$QuireDirs = @{
    'Foreword' = 'codex\foreword\core'; 'Kernel' = 'codex\os\kernel'; 'OS' = 'codex\os\core'
    'Works' = 'apps\works'; 'Trust' = 'codex\os\trust'; 'Net' = 'codex\os\net'
    'Verify' = 'codex\os\verify'; 'Replay' = 'codex\os\replay'; 'Sched' = 'codex\os\sched'
    # Wflow, not Workflow: 'Workflow' is apps\workflow, a different quire. The
    # cites have always said Wflow; what was missing was this line, so every
    # chapter they name was silently left out of the unit and the author got
    # CDX3002 at the use sites instead. BACKLOG 2.24.
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
    'Guios' = 'apps\guios'
    'Ideas' = 'apps\ideas'
    'Circuits' = 'apps\circuits\Core'
    'CircuitsSch' = 'apps\circuits\SchematicEditor'
    'CircuitsSym' = 'apps\circuits\SymbolEditor'
    'CircuitsSim' = 'apps\circuits\Simulator'
    'CircuitsPcb' = 'apps\circuits\PcbEditor'
    'CircuitsFp' = 'apps\circuits\FootprintEditor'
    'CircuitsBv' = 'apps\circuits\BoardViewer'
    'CircuitsMfg' = 'apps\circuits\Manufacturing'
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
    # This lenient pattern is kept because two other bundlers carry their own
    # copies of it (concat-codex-self.ps1, plug-build-lib.ps1) and match its
    # shape. Pass it explicitly if you want the old blind-to-unregistered
    # behaviour; nothing in the tree should want it.
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
    foreach ($l in $RootLines) {
        if ($l -match $Pattern) { & $walk $matches[1] $matches[2] }
    }
    return ,$ordered
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
