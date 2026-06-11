# Plan a creature: create its database record with species-appropriate
# defaults. This is the entry point — run this first, then the pipeline
# stages in order.
#
# Usage: pwsh apps/fishtank/pipeline/plan-creature.ps1 -Species "Clownfish" -Family "Pomacentridae"
#        pwsh apps/fishtank/pipeline/plan-creature.ps1 -Species "Brain Coral" -Kind coral
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Species,
    [string]$Scientific = '',
    [string]$Kind = 'fish',
    [string]$Family = '',
    [string]$Description = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'creature-db.ps1')

$existing = Load-Creature $Species
if ($existing) {
    Write-Host "[$Species] already exists (status: $($existing.status))"
    exit 0
}

$subUnits = switch ($Kind) {
    'coral' {
        @(
            @{ kind='base'; label='Base'; transparent=$false; doubleSided=$false; boneName='root'; animType='none'
               textures=@(
                   @{slot='diffuse';prompt="$Species texture, photorealistic, organic surface detail, 8k";width=2048;height=2048;model='cyberrealisticXL_v4.safetensors';path='';status='planned'},
                   @{slot='normal';prompt="coral surface normal map, bumps, ridges, organic";width=2048;height=2048;model='cyberrealisticXL_v4.safetensors';path='';status='planned'}) },
            @{ kind='tentacle'; label='Polyps'; transparent=$true; doubleSided=$true; boneName='polyp'; animType='sway'
               textures=@(
                   @{slot='diffuse';prompt="coral polyp tentacles, translucent tips, organic";width=1024;height=1024;model='cyberrealisticXL_v4.safetensors';path='';status='planned'},
                   @{slot='alpha';prompt="coral polyp alpha mask, feathered edges";width=1024;height=1024;model='cyberrealisticXL_v4.safetensors';path='';status='planned'}) }
        )
    }
    'rock' {
        @(
            @{ kind='base'; label='Rock'; transparent=$false; doubleSided=$false; boneName='root'; animType='none'
               textures=@(
                   @{slot='diffuse';prompt="underwater rock texture, mossy, realistic, 8k";width=2048;height=2048;model='cyberrealisticXL_v4.safetensors';path='';status='planned'},
                   @{slot='normal';prompt="rock surface normal map, crevices, rough";width=2048;height=2048;model='cyberrealisticXL_v4.safetensors';path='';status='planned'}) }
        )
    }
    'plant' {
        @(
            @{ kind='base'; label='Stem'; transparent=$false; doubleSided=$false; boneName='root'; animType='sway'
               textures=@(
                   @{slot='diffuse';prompt="underwater plant texture, kelp, seagrass, green, 8k";width=1024;height=2048;model='cyberrealisticXL_v4.safetensors';path='';status='planned'},
                   @{slot='alpha';prompt="plant leaf alpha mask, organic edges";width=1024;height=2048;model='cyberrealisticXL_v4.safetensors';path='';status='planned'}) }
        )
    }
    default {
        $null
    }
}

$boids = switch ($Kind) {
    'fish' { @{ speed=100; turnRate=300; schoolSize=4; separation=200; alignment=400; cohesion=500; solo=$false } }
    'invertebrate' { @{ speed=50; turnRate=150; schoolSize=1; separation=100; alignment=0; cohesion=0; solo=$true } }
    default { @{ speed=0; turnRate=0; schoolSize=0; separation=0; alignment=0; cohesion=0; solo=$true } }
}

$rec = New-CreatureRecord -Name $Species -Scientific $Scientific -Kind $Kind -Family $Family -Description $Description -Boids $boids
if ($subUnits) { $rec.subUnits = $subUnits }

$path = Save-Creature $rec
Write-Host "[$Species] planned -> $path"
