# Creature database helpers. Stores CreatureRecords as JSON files
# keyed by species name. Near-term file-based; migrates to Data quire
# tables when the Codex DB runtime is wired.
#
# Usage: . apps/fishtank/pipeline/creature-db.ps1

$script:DbRoot = Join-Path $PSScriptRoot '..' 'creature-db'

function Ensure-CreatureDb {
    if (-not (Test-Path $script:DbRoot)) { New-Item -ItemType Directory -Force $script:DbRoot | Out-Null }
    $idx = Join-Path $script:DbRoot 'index.json'
    if (-not (Test-Path $idx)) { '{}' | Set-Content $idx -Encoding utf8 }
}

function Get-CreatureKey([string]$Name) {
    ($Name -replace '[^a-zA-Z0-9]', '-').ToLower()
}

function Get-CreaturePath([string]$Name) {
    Join-Path $script:DbRoot "$(Get-CreatureKey $Name).json"
}

function Save-Creature($Record) {
    Ensure-CreatureDb
    $key = Get-CreatureKey $Record.name
    $path = Join-Path $script:DbRoot "$key.json"
    $Record | ConvertTo-Json -Depth 10 | Set-Content $path -Encoding utf8
    $idx = Get-Content (Join-Path $script:DbRoot 'index.json') -Raw | ConvertFrom-Json
    $idx | Add-Member -NotePropertyName $key -NotePropertyValue @{
        name = $Record.name; kind = $Record.kind; status = $Record.status
        family = $Record.family
    } -Force
    $idx | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $script:DbRoot 'index.json') -Encoding utf8
    $path
}

function Load-Creature([string]$Name) {
    $path = Get-CreaturePath $Name
    if (-not (Test-Path $path)) { return $null }
    Get-Content $path -Raw | ConvertFrom-Json
}

function Find-Creatures([string]$Query) {
    Ensure-CreatureDb
    $idx = Get-Content (Join-Path $script:DbRoot 'index.json') -Raw | ConvertFrom-Json
    $results = @()
    foreach ($prop in $idx.PSObject.Properties) {
        $entry = $prop.Value
        if ($entry.name -match $Query -or $entry.family -match $Query) {
            $results += $entry
        }
    }
    $results
}

function Update-CreatureStatus([string]$Name, [string]$Status) {
    $rec = Load-Creature $Name
    if (-not $rec) { return }
    $rec.status = $Status
    $rec | ConvertTo-Json -Depth 10 | Set-Content (Get-CreaturePath $Name) -Encoding utf8
    $idx = Get-Content (Join-Path $script:DbRoot 'index.json') -Raw | ConvertFrom-Json
    $key = Get-CreatureKey $Name
    if ($idx.$key) { $idx.$key.status = $Status }
    $idx | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $script:DbRoot 'index.json') -Encoding utf8
}

function New-CreatureRecord {
    param(
        [string]$Name,
        [string]$Scientific = '',
        [string]$Kind = 'fish',
        [string]$Family = '',
        [string]$Description = '',
        [string]$RefPrompt = '',
        [string]$RefNegative = 'blurry, watermark, text, cartoon, illustration, painting',
        [string]$RefModel = 'realisticVisionV60B1_v51HyperVAE.safetensors',
        [hashtable]$Boids = @{ speed=100; turnRate=300; schoolSize=4; separation=200; alignment=400; cohesion=500; solo=$false },
        [hashtable[]]$SubUnits = @()
    )
    if (-not $RefPrompt) {
        $RefPrompt = "$Name, side view, white background, product photography, centered, clean, isolated specimen, no environment, fins spread, high detail"
    }
    if (-not $SubUnits) {
        $SubUnits = @(
            @{ kind='body'; label='Body'; transparent=$false; doubleSided=$false; boneName='spine'; animType='spine-deform'
               textures=@(@{slot='diffuse';prompt="seamless tileable texture of $Name fish skin, flat UV unwrapped, orange white bands, scale pattern, top-down macro photograph of fish scales, no perspective, no fish shape, texture map only, 8k";width=2048;height=2048;model=$RefModel;path='';status='planned'},
                          @{slot='normal';prompt="seamless tileable normal map texture, fish scales bump detail, tangent space normal map, blue-purple tones, flat lighting, no perspective, PBR material, 8k";width=2048;height=2048;model=$RefModel;path='';status='planned'}) },
            @{ kind='dorsalFin'; label='Dorsal Fin'; transparent=$true; doubleSided=$true; boneName='dorsal'; animType='fin-wave'
               textures=@(@{slot='diffuse';prompt="fish fin membrane texture, translucent, visible rays and veins, flat scan on black background, no perspective, texture map, macro photo";width=1024;height=1024;model=$RefModel;path='';status='planned'},
                          @{slot='alpha';prompt="white fin silhouette shape on pure black background, sharp clean edges, fin membrane outline, mask texture";width=1024;height=1024;model=$RefModel;path='';status='planned'}) },
            @{ kind='tailFin'; label='Tail Fin'; transparent=$true; doubleSided=$true; boneName='tail'; animType='tail-oscillate'
               textures=@(@{slot='diffuse';prompt="fish tail fin membrane texture, translucent orange, visible fin rays, flat scan on black background, texture map, macro";width=1024;height=1024;model=$RefModel;path='';status='planned'}) },
            @{ kind='eye'; label='Eye'; transparent=$false; doubleSided=$false; boneName='eye'; animType='eye-drift'
               textures=@(@{slot='diffuse';prompt="fish eye extreme close-up, round golden iris with black pupil, wet reflective surface, aquatic eye, macro photograph, centered, no eyelids";width=512;height=512;model=$RefModel;path='';status='planned'}) }
        )
    }
    @{
        name = $Name; scientific = $Scientific; kind = $Kind; family = $Family
        description = $Description
        refPrompt = $RefPrompt; refNegative = $RefNegative; refModel = $RefModel
        subUnits = $SubUnits
        boids = $Boids
        animations = @(
            @{ name='swim'; bone='spine'; axis='y'; amplitude=15; frequency=200; phase=0 },
            @{ name='dorsalWave'; bone='dorsal'; axis='z'; amplitude=8; frequency=150; phase=50 },
            @{ name='tailOscillate'; bone='tail'; axis='y'; amplitude=25; frequency=300; phase=0 },
            @{ name='mouthOpen'; bone='jaw'; axis='x'; amplitude=5; frequency=30; phase=0 },
            @{ name='gillPulse'; bone='gill'; axis='x'; amplitude=3; frequency=60; phase=25 }
        )
        meshPath = ''; textureAtlas = ''; glbPath = ''
        width = 300; height = 200
        status = 'planned'
    }
}
