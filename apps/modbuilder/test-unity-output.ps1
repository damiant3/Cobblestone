[CmdletBinding()]
param([string]$Profile = '', [string]$SourceDirectory = '', [switch]$UnicodeProbe)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (-not $Profile) { $Profile = Join-Path $repo 'build-output/prism-targets/valheim-local.prism-target.json' }
if (-not $SourceDirectory) { $SourceDirectory = Join-Path $repo 'build-output/modbuilder-site' }
. (Join-Path $PSScriptRoot 'target-toolchain.ps1')
$targetProfile = Get-Content -LiteralPath $Profile -Raw | ConvertFrom-Json
$work = Join-Path $repo 'build-output/modbuilder-runtime-proof'
[void](New-Item -ItemType Directory -Force -Path $work)
$features = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'mods/valheim/features.json') -Raw | ConvertFrom-Json).features
$subjects = @($features | ForEach-Object { 'mod-' + $_.id }) + @('control-text','control-heap','control-state')
if ($UnicodeProbe) { $subjects = @('control-unicode') }
foreach ($name in $subjects) {
    $code = [IO.File]::ReadAllText((Join-Path $SourceDirectory ($name + '.cs')))
    $receipt = Invoke-PrismTarget -Root $work -Request ([pscustomobject]@{operation='build'; profile=$targetProfile; code=$code})
    if (-not $receipt.ok) { throw "$name failed C# compilation: $($receipt.diagnostics)" }
    $assembly = [Reflection.Assembly]::LoadFile($receipt.artifact)
    $model = $assembly.GetType('PrismGenerated.Codex_Program', $true)
    if ($name -eq 'mod-hud') {
        if ($code -match '_Cce|_Buf|_State|_CxText|_ICodexVariant') { throw 'HUD still carries unused language runtime' }
        $row = $model.GetMethod('hud_row_y')
        if ($row.Invoke($null,@([long]0)) -ne 110 -or $row.Invoke($null,@([long]1)) -ne 62) { throw 'HUD layout behavior changed' }
    }
    if ($name -like 'control-*') {
        $value = $model.GetMethod('opening').Invoke($null,@())
        if ($name -in @('control-text','control-unicode')) {
            $decoded = $assembly.GetType('PrismGenerated._Cce',$true).GetMethod('ToUnicode').Invoke($null,@($value))
            $expected = if ($UnicodeProbe) { 'h' + [char]233 + 'llo' } else { 'hello' }
            if ($decoded -cne $expected) { throw "Text conversion failed: expected '$expected', observed '$decoded'" }
        }
        if ($name -eq 'control-heap' -and $value -le 0) { throw 'Heap runtime was lost' }
        if ($name -eq 'control-state' -and $value -ne 7) { throw 'State runtime was lost' }
    }
    Write-Host "PASS ${name}: native C# compile and applicable behavior checks"
}
