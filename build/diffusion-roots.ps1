[CmdletBinding()]
param(
    [switch]$Provision,
    [string]$ModelsRoot = 'D:\AI\DiffusionForge\webui\models',
    [string[]]$VmArgsFile = @(),
    [string[]]$Subjects = @()
)

# The diffusion tests' model roots: build-output\diffusion-<name>, each a junction to
# one folder under Diffusion Forge's models directory (OperatorsManual, -gpu-files).
#
#   pwsh build/diffusion-roots.ps1                          # check every root a test names
#   pwsh build/diffusion-roots.ps1 -VmArgsFile a.vmargs,... # check only the roots these name
#   & build/diffusion-roots.ps1 -Subjects $tests            # the same, over each X.codex's X.vmargs
#   pwsh build/diffusion-roots.ps1 -Provision               # make the missing junctions
#
# Exit 0 when every root checked is a junction to an existing directory, 1 when one is
# not (each named with the command that makes it), 2 when -Provision cannot.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

# Forge's folder for each root, as its webui lays out models\.
$ForgeFolder = [ordered]@{
    'diffusion-models'        = 'Stable-diffusion'
    'diffusion-text-encoders' = 'text_encoder'
    'diffusion-vae'           = 'VAE'
    'diffusion-upscalers'     = 'RealESRGAN'
    'diffusion-swinir'        = 'SwinIR'
    'diffusion-dat'           = 'DAT'
}

function Get-NamedRoots([string[]]$files) {
    $names = [System.Collections.Generic.SortedSet[string]]::new()
    foreach ($f in $files) {
        foreach ($line in (Get-Content -LiteralPath $f)) {
            $line = $line.Trim()
            if (-not $line -or $line.StartsWith('#')) { continue }
            foreach ($m in [regex]::Matches($line, '(?:^|\s)build-output[\\/](diffusion-[A-Za-z0-9-]+)(?=\s|$)')) { [void]$names.Add($m.Groups[1].Value) }
        }
    }
    return @($names)
}

$VmArgsFile += @($Subjects | ForEach-Object { $_ -replace '\.codex$', '.vmargs' })
$files = if ($VmArgsFile.Count -gt 0) { @($VmArgsFile | Where-Object { $_ -and (Test-Path -PathType Leaf $_) }) }
         else { @(Get-ChildItem -Path (Join-Path $Repo 'codex\test') -Recurse -Filter '*.vmargs' -File | ForEach-Object FullName) }
$roots = Get-NamedRoots $files

$unknown = @($roots | Where-Object { -not $ForgeFolder.Contains($_) })
if ($unknown.Count -gt 0) {
    foreach ($u in $unknown) { Write-Host "diffusion-roots: a test names build-output\$u, which this tool has no Forge folder for; add it to `$ForgeFolder" }
    exit 1
}

$buildOut = Join-Path $Repo 'build-output'
$missing = @()
foreach ($r in $roots) {
    $path = Join-Path $buildOut $r
    $item = Get-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
    if ($null -eq $item) { $missing += [pscustomobject]@{ Root = $r; Why = 'absent' }; continue }
    if ($item.LinkType -ne 'Junction') { $missing += [pscustomobject]@{ Root = $r; Why = 'not a junction' }; continue }
    # Test-Path on the junction itself answers True after its target is gone; test the target.
    $target = @($item.Target)[0]
    if (-not $target -or -not (Test-Path -PathType Container -LiteralPath $target)) { $missing += [pscustomobject]@{ Root = $r; Why = "a junction to $target, which does not exist" } }
}

if ($Provision) {
    if (-not (Test-Path -PathType Container $ModelsRoot)) { Write-Host "diffusion-roots: models root $ModelsRoot does not exist; pass -ModelsRoot"; exit 2 }
    if (-not (Test-Path $buildOut)) { New-Item -ItemType Directory -Force $buildOut | Out-Null }
    $refused = 0
    foreach ($x in $missing) {
        $target = Join-Path $ModelsRoot $ForgeFolder[$x.Root]
        if ($x.Why -ne 'absent') { Write-Host "diffusion-roots: build-output\$($x.Root) is $($x.Why); remove it by hand, then provision"; $refused++; continue }
        if (-not (Test-Path -PathType Container $target)) { Write-Host "diffusion-roots: $target does not exist, so build-output\$($x.Root) is not made"; $refused++; continue }
        New-Item -ItemType Junction -Path (Join-Path $buildOut $x.Root) -Target $target | Out-Null
        Write-Host "diffusion-roots: build-output\$($x.Root) -> $target"
    }
    if ($refused -gt 0) { exit 2 }
    Write-Host "diffusion-roots: $($roots.Count) root(s) in place"
    exit 0
}

if ($missing.Count -gt 0) {
    foreach ($x in $missing) { Write-Host "MISSING MODEL ROOT: build-output\$($x.Root) is $($x.Why)" }
    Write-Host "  make them: pwsh build/diffusion-roots.ps1 -Provision [-ModelsRoot <Forge's models folder>]"
    exit 1
}
exit 0
