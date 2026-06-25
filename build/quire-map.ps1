# The single quire registry and cite resolver. Dot-source this file;
# never copy the map. Every script that resolves
# `cites <Quire> chapter <Name>` (bundlers, compile, test batch,
# plug builds, linters) must take $QuireDirs and $CitePat from here.
# Add new quires HERE and nowhere else. Paths are repo-relative.

$QuireDirs = @{
    'Foreword' = 'codex\foreword\core'; 'Kernel' = 'codex\os\kernel'; 'OS' = 'codex\os\core'
    'Works' = 'apps\works'; 'Trust' = 'codex\os\trust'; 'Net' = 'codex\os\net'
    'Verify' = 'codex\os\verify'; 'Replay' = 'codex\os\replay'; 'Sched' = 'codex\os\sched'
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
    'Explorer' = 'apps\explorer'
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
    'WebApp' = 'apps\webapp'
    'ERP' = 'apps\erp'
    'Vision' = 'apps\vision'
    'Collab' = 'apps\collab'
    'Services' = 'apps\services'
    'Market' = 'apps\market'
    'Workflow' = 'apps\workflow'
    'Site' = 'apps\site'
    'Boards' = 'codex\boards'
    'Guios' = 'apps\guios'
}

function New-CitePattern {
    # Alternation sorted longest-first so 'Games' wins over 'Game'.
    param([string[]]$ExtraQuires = @())
    $names = @($QuireDirs.Keys) + $ExtraQuires
    $alt = ($names | Sort-Object -Descending { $_.Length }) -join '|'
    return "^\s*cites\s+($alt)\s+chapter\s+([A-Za-z_][A-Za-z0-9_-]*)"
}

$CitePat = New-CitePattern

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
        [ValidateSet('throw','skip')] [string]$OnMissing = 'throw'
    )
    if (-not $Pattern) { $Pattern = $CitePat }
    $visited = @{}
    $visiting = @{}
    if ($SeedSeen) { foreach ($k in $SeedSeen.Keys) { $visited[$k] = $true } }
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
            if ($OnMissing -eq 'skip') { $visited[$key] = $true; return }
            throw "Cited $quire chapter '$name' not found (expected $path)"
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
