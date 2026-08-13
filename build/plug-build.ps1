# plug-build.ps1 -- Build a transpiler plug CDX from name and chapter list
# GENERATED FROM THE CODEX SHELL DSL. Do not edit by hand.
# A hand edit here must NOT be submitted. Change the generator under
# codex/build/, regenerate, and submit the generator and this file
# together. Until then build/check-generated-scripts.ps1 reports this
# file as drifted, and the next regeneration discards the edit.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$PlugName,
    [Parameter(Mandatory=$true)]
    [string[]]$Chapters,
    [switch]$Force
)

# Build plugs/<PlugName>/build-output/<PlugName>-plug.cdx


Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'


. (Join-Path $PSScriptRoot '..' 'codex' 'plugs' 'common' 'plug-build-lib.ps1')


$plugDir = Join-Path $PSScriptRoot '..' 'codex' 'plugs' $PlugName
if ((-not (Test-Path -PathType Container $plugDir))) {
    [Console]::Error.WriteLine("MISSING plug directory: $plugDir")
    exit 2
}
Build-TranspilerPlug -PlugDir $plugDir -PlugName $PlugName -Chapters $Chapters
