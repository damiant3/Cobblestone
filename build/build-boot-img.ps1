# build-boot-img.ps1 -- Build seed/Codex.img, the bootable UEFI dev-console image
# GENERATED FROM THE CODEX SHELL DSL. Do not edit by hand.
# A hand edit here must NOT be submitted. Change the generator under
# codex/build/, regenerate, and submit the generator and this file
# together. Until then build/check-generated-scripts.ps1 reports this
# file as drifted, and the next regeneration discards the edit.
[CmdletBinding()]
param(
    # -Agent additionally generates a bundled agent (build/make-agent-bundle.ps1)
    # and writes AGENT.GGU + AGENT.MAN into the ESP root, which is what makes a
    # booted stick carry an agent rather than an offer to go and find one. It is
    # opt-in because it signs, so it boots VMs, and because it changes the depot
    # artifact. `build/agent-bundle-test.ps1` is what proves the guest verifies
    # what this writes.
    [switch]$Agent,
    # Where to write. Defaults to the depot artifact; point it elsewhere to
    # exercise this script without touching seed/Codex.img.
    [string]$Out = '',
    # Which compiler to build the payload WITH. Defaults to the depot seed.
    # 
    # This was not passed at all, so compile.ps1 fell back to whatever
    # build.ps1 had last left in build-output/bare-metal -- and it prints a NOTE
    # saying so, which nothing was reading. That is the same defect the comment
    # about -Source below describes, one input over: two runs of this script at
    # the same commit could produce different depot images and neither would say
    # so, and a digest recorded against "the tree at CL N" would not be
    # reproducible from CL N. The seed is the reproducible choice and the one
    # -Seed already uses for the payload it embeds.
    [string]$Kernel = '',
    # Which payload the image boots into. B5.4 step 3 made GopBoot the default,
    # gated: it passes validate-img and paints its menu under OVMF, where the
    # dev console reaches a black screen and OUT OF MEMORY. UefiBoot stays
    # reachable by name, and install-boot-test.ps1 asks for it explicitly
    # because it asserts that payload's banner (L-FALLBACK).
    [string]$BootSource = 'apps\works\GopBoot.codex',
    # GopBoot is an interactive poll loop that makes no heap progress, so the
    # watchdog has to be petted rather than inferred from progress. This is the
    # one compile-mode difference between the two payloads, and it follows the
    # payload: -Pet is now the default and -Uefi selects the dev console's mode.
    # -Pet is still accepted so the invocation B5.4 was gated with keeps working.
    [switch]$Pet,
    [switch]$Uefi
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Seed = Join-Path $Repo 'seed\Codex.cdx'
$ImgOut = if ($Out) { $Out } else { Join-Path $Repo 'seed\Codex.img' }

if ((-not (Test-Path -PathType Leaf $Seed))) {
    Write-Host "FAIL: $Seed missing"
    exit 1
}

$BuildOut = Join-Path $Repo 'build-output'
if ((-not (Test-Path -PathType Container $BuildOut))) {
    New-Item -ItemType Directory -Force $BuildOut | Out-Null
}

Write-Host '=== Build Boot IMG ==='


# Step 1: Bundle boot source with all dependencies
$bundleScript = Join-Path $PSScriptRoot 'bundle-app.ps1'
$compileScript = Join-Path $PSScriptRoot 'compile.ps1'
$BootSourcePath = if ([System.IO.Path]::IsPathRooted($BootSource)) { $BootSource } else { Join-Path $Repo $BootSource }
if ((-not (Test-Path -PathType Leaf $BootSourcePath))) {
    Write-Host "FAIL: boot source $BootSourcePath missing"
    exit 1
}
$BundledSource = Join-Path $BuildOut 'boot-bundled.codex'
$BootCdx = Join-Path $BuildOut 'boot.cdx'
$BootLog = Join-Path $BuildOut 'img-compile.log'
Write-Host "  Bundling $BootSourcePath..."
& pwsh -NoProfile -File $bundleScript -Src $BootSourcePath -Out $BundledSource
if (((-not ($LASTEXITCODE -eq 0)) -or (-not (Test-Path -PathType Leaf $BundledSource)))) {
    Write-Host 'FAIL: bundle failed'
    exit 1
}


# Step 2: Compile bundled source to CDX
$KernelArg = if ($Kernel) { $Kernel } else { $Seed }
if ((-not (Test-Path -PathType Leaf $KernelArg))) {
    Write-Host "FAIL: kernel $KernelArg missing"
    exit 1
}
$ModeFlag = if ($Uefi) { '-Uefi' } else { '-Pet' }
Write-Host "  Compiling bundled source -> CDX ($ModeFlag), kernel $KernelArg..."
& pwsh -NoProfile -File $compileScript -Src $BundledSource -Out $BootCdx -Log $BootLog $ModeFlag -Kernel $KernelArg
if (((-not ($LASTEXITCODE -eq 0)) -or (-not (Test-Path -PathType Leaf $BootCdx)))) {
    Write-Host 'FAIL: CDX compile failed'
    Get-Content $BootLog -ErrorAction SilentlyContinue | Select-Object -First 15 | ForEach-Object { Write-Host "  $_" }
    exit 1
}


# Step 3: Convert CDX to PE
$PeScript = Join-Path $PSScriptRoot 'cdx-to-pe.ps1'
$BootPe = Join-Path $BuildOut 'boot.efi'
Write-Host '  Converting CDX -> PE...'
& pwsh -NoProfile -File $PeScript -CdxInput $BootCdx -Out $BootPe -HeapPages 131072
if (((-not ($LASTEXITCODE -eq 0)) -or (-not (Test-Path -PathType Leaf $BootPe)))) {
    Write-Host 'FAIL: PE conversion failed'
    exit 1
}


# Step 4 (optional): the bundled agent
$AgentArgs = @()
if ($Agent) {
    $AgentDir = Join-Path $BuildOut 'agent'
    Write-Host '  Building the bundled agent (signs, so this boots VMs)...'
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'make-agent-bundle.ps1') -OutDir $AgentDir
    if ((-not ($LASTEXITCODE -eq 0))) {
        Write-Host 'FAIL: agent bundle failed'
        exit 1
    }
    $AgentArgs = @('-Agent', (Join-Path $AgentDir 'AGENT.GGU'), '-AgentManifest', (Join-Path $AgentDir 'AGENT.MAN'))
}


# Step 5: Build the GPT disk image.
# 
# The seed and the source both go on it, unconditionally. They did not before,
# and the way they did not is the instructive part:
# 
#   -Seed was never passed at all, so CODEX.CDX was never on the image, while
#   docs/UsersHandbook.md said the image contained the CDX seed.
# 
#   -Source was passed only `if (Test-Path $SourceFile)`, and $SourceFile is
#   build-output/Codex.codex -- a scratch artifact that build.ps1's concat
#   phase happens to leave behind. So whether the stick a person is handed
#   carried its own source depended on whether somebody had run a build in
#   that workspace recently and not cleaned it. Two runs of this script, same
#   commit, could produce different images and neither would say so.
# 
# A stick that cannot verify or rebuild itself is not what this project
# promises anyone, so both are now required inputs and the concat is generated
# when it is missing rather than silently skipped.
$ImgScript = Join-Path $PSScriptRoot 'build-img.ps1'
$ConcatScript = Join-Path $PSScriptRoot 'concat-codex-self.ps1'
$SourceFile = Join-Path $BuildOut 'Codex.codex'
if ((-not (Test-Path -PathType Leaf $SourceFile))) {
    Write-Host "  Concatenating compiler source (no $SourceFile present)..."
    & pwsh -NoProfile -File $ConcatScript -CodexDir (Join-Path $Repo 'codex\compiler') -OutFile $SourceFile
    if (((-not ($LASTEXITCODE -eq 0)) -or (-not (Test-Path -PathType Leaf $SourceFile)))) {
        Write-Host 'FAIL: could not produce the compiler source concat'
        exit 1
    }
}

# 32768 sectors (16 MB). The payload is now the PE plus a ~2.6 MB seed plus a
# ~2.8 MB source, and build-img refuses a payload over 90 per cent of the
# partition -- at the old 16384 it fitted with almost nothing to spare and
# -Agent would not have fitted at all.
Write-Host '  Building GPT disk image...'
& pwsh -NoProfile -File $ImgScript -PeInput $BootPe -Out $ImgOut -Seed $Seed -Source $SourceFile -SourceDir (Join-Path $Repo 'codex\compiler') -TotalSectors 32768 @AgentArgs
if (((-not ($LASTEXITCODE -eq 0)) -or (-not (Test-Path -PathType Leaf $ImgOut)))) {
    Write-Host 'FAIL: IMG build failed'
    exit 1
}


$totalSize = (Get-Item $ImgOut).Length
Write-Host "Done: $ImgOut ($([math]::Round($totalSize / 1MB, 1)) MB)"
