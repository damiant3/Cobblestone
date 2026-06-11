# Batch-generate all default aquarium species.
# Usage: pwsh apps/fishtank/pipeline/batch-aquarium.ps1
[CmdletBinding()]
param(
    [string]$ForgeUrl = 'http://127.0.0.1:7860',
    [string]$TripoDir = 'D:\AI\TripoSR'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$pipeDir = $PSScriptRoot

$species = @(
    @{ Name='Clownfish';  Scientific='Amphiprion ocellaris'; Family='Pomacentridae'; Kind='fish'; Desc='Small orange fish with white bands and black edges, iconic reef dweller' },
    @{ Name='Angelfish';  Scientific='Pterophyllum scalare'; Family='Cichlidae';     Kind='fish'; Desc='Tall laterally compressed body with long trailing dorsal and anal fins' },
    @{ Name='Neon Tetra'; Scientific='Paracheirodon innesi'; Family='Characidae';    Kind='fish'; Desc='Tiny iridescent blue and red schooling fish' },
    @{ Name='Blue Tang';  Scientific='Paracanthurus hepatus'; Family='Acanthuridae'; Kind='fish'; Desc='Deep blue oval body with yellow tail and black palette marking' },
    @{ Name='Discus';     Scientific='Symphysodon discus';   Family='Cichlidae';     Kind='fish'; Desc='Round laterally flat body with vertical color bars, slow graceful movement' },
    @{ Name='Guppy';      Scientific='Poecilia reticulata';  Family='Poeciliidae';   Kind='fish'; Desc='Small colorful fish with large fanning tail, rapid swimmer' },
    @{ Name='Seahorse';   Scientific='Hippocampus kuda';     Family='Syngnathidae';  Kind='fish'; Desc='Upright swimmer with curled prehensile tail, armored body plates' },
    @{ Name='Shrimp';     Scientific='Lysmata amboinensis';  Family='Lysmatidae';    Kind='invertebrate'; Desc='Cleaner shrimp with red and white striped body, long antennae' },
    @{ Name='Brain Coral'; Scientific='Diploria labyrinthiformis'; Family='Mussidae'; Kind='coral'; Desc='Dome-shaped coral with meandering ridges resembling a brain' },
    @{ Name='Sea Fan';    Scientific='Gorgonia ventalina';   Family='Gorgoniidae';   Kind='coral'; Desc='Flat branching fan coral, purple-pink, sways in current' },
    @{ Name='Live Rock';  Scientific='';                     Family='';              Kind='rock';  Desc='Porous reef rock covered in coralline algae and micro-organisms' },
    @{ Name='Kelp';       Scientific='Macrocystis pyrifera'; Family='Laminariaceae'; Kind='plant'; Desc='Tall swaying brown-green seaweed fronds' }
)

$failed = 0
foreach ($sp in $species) {
    Write-Host "`n======================================" -ForegroundColor Cyan
    Write-Host " $($sp.Name)" -ForegroundColor Cyan
    Write-Host "======================================" -ForegroundColor Cyan
    try {
        & pwsh -NoProfile -File (Join-Path $pipeDir 'run-pipeline.ps1') `
            -Species $sp.Name -Kind $sp.Kind -Family $sp.Family `
            -Scientific $sp.Scientific -Description $sp.Desc `
            -ForgeUrl $ForgeUrl -TripoDir $TripoDir
    } catch {
        Write-Warning "$($sp.Name) failed: $_"
        $failed++
    }
}

Write-Host "`n=== Batch Complete ===" -ForegroundColor Green
Write-Host "$($species.Count - $failed)/$($species.Count) species processed"
if ($failed -gt 0) { Write-Host "$failed species had errors" -ForegroundColor Red }
