[CmdletBinding()]
param(
    [string]$HeadersDir = (Join-Path $env:TEMP 'nv-mmu-oracle\ogkm'),
    [string]$MesaDir = (Join-Path $env:TEMP 'nv-qmd-oracle\mesa')
)

# The oracle for codex/test/nv-mmu-maxwell: NVIDIA's published gm107
# dev_mmu.h and dev_ram.h (open-gpu-kernel-modules 570.144, unmodified)
# supply every field range, and Mesa's drf.h multi-word setter packs it,
# compiled by MSVC. Only drf.h's DRF_ helpers and NVVAL_MW_SET_X are taken,
# because its variadic dispatch macros are GNU C.
#
#   pwsh build/nv-mmu-oracle.ps1 > codex/test/nv-mmu-maxwell.expected

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -PathType Container $HeadersDir)) {
    & git clone --quiet --depth 1 --filter=blob:none --sparse --branch 570.144 https://github.com/NVIDIA/open-gpu-kernel-modules.git $HeadersDir 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Host 'open-gpu-kernel-modules clone failed'; exit 2 }
    & git -C $HeadersDir sparse-checkout set src/common/inc/swref/published/maxwell/gm107 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Host 'sparse checkout failed'; exit 2 }
}
if (-not (Test-Path -PathType Container $MesaDir)) {
    & git clone --quiet --depth 1 --filter=blob:none --sparse --branch mesa-24.2.0 https://gitlab.freedesktop.org/mesa/mesa.git $MesaDir 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Host 'mesa clone failed'; exit 2 }
    & git -C $MesaDir sparse-checkout set src/gallium/drivers/nouveau/nvc0 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Host 'mesa sparse checkout failed'; exit 2 }
}
$gm107 = Join-Path $HeadersDir 'src\common\inc\swref\published\maxwell\gm107'
$drf = Join-Path $MesaDir 'src\gallium\drivers\nouveau\nvc0\drf.h'
foreach ($p in (Join-Path $gm107 'dev_mmu.h'), (Join-Path $gm107 'dev_ram.h'), $drf) { if (-not (Test-Path -PathType Leaf $p)) { Write-Host "MISSING: $p"; exit 2 } }

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { Write-Host 'MISSING: MSVC (Visual Studio C++ tools)'; exit 2 }
$vcvars = Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat'

$work = Join-Path $env:TEMP 'nv-mmu-oracle\work'
New-Item -ItemType Directory -Force $work | Out-Null
$drfLines = [Collections.Generic.List[string]]::new()
$inSet = $false
foreach ($l in [IO.File]::ReadAllLines($drf)) {
    if ($l -match '^#define DRF_') { $drfLines.Add($l) }
    elseif ($l -match '^#define NVVAL_MW_SET_X\(') { $inSet = $true; $drfLines.Add($l) }
    elseif ($inSet) { $drfLines.Add($l); if ($l -match 'while\(0\)') { $inSet = $false } }
}
[IO.File]::WriteAllLines((Join-Path $work 'drf_mw.h'), $drfLines)
Copy-Item -Force (Join-Path $gm107 'dev_mmu.h') (Join-Path $work 'dev_mmu.h')
Copy-Item -Force (Join-Path $gm107 'dev_ram.h') (Join-Path $work 'dev_ram.h')

[IO.File]::WriteAllText((Join-Path $work 'oracle.c'), @'
#include <stdio.h>
#include <stdint.h>
#include <string.h>
#include "drf_mw.h"
#include "dev_mmu.h"
#include "dev_ram.h"

#define SET(o, f, v) NVVAL_MW_SET_X(o, MW(f), (v))

static void pte(int n, uint64_t addr, uint32_t ap, uint32_t vol, uint32_t kind, uint32_t ro, uint32_t priv) {
    uint32_t e[2] = { 0, 0 };
    SET(e, NV_MMU_PTE_VALID, NV_MMU_PTE_VALID_TRUE);
    SET(e, NV_MMU_PTE_PRIVILEGE, priv);
    SET(e, NV_MMU_PTE_READ_ONLY, ro);
    if (ap == NV_MMU_PTE_APERTURE_VIDEO_MEMORY) SET(e, NV_MMU_PTE_ADDRESS_VID, addr >> NV_MMU_PTE_ADDRESS_SHIFT);
    else SET(e, NV_MMU_PTE_ADDRESS_SYS, addr >> NV_MMU_PTE_ADDRESS_SHIFT);
    SET(e, NV_MMU_PTE_VOL, vol);
    SET(e, NV_MMU_PTE_APERTURE, ap);
    SET(e, NV_MMU_PTE_KIND, kind);
    printf("PTE %d %08x %08x\n", n, e[0], e[1]);
}

static void pde(int n, uint64_t big, uint32_t bap, uint64_t small, uint32_t sap, uint32_t bvol, uint32_t svol, uint32_t size) {
    uint32_t e[2] = { 0, 0 };
    SET(e, NV_MMU_PDE_APERTURE_BIG, bap);
    SET(e, NV_MMU_PDE_SIZE, size);
    if (bap == NV_MMU_PDE_APERTURE_BIG_VIDEO_MEMORY) SET(e, NV_MMU_PDE_ADDRESS_BIG_VID, big >> NV_MMU_PDE_ADDRESS_SHIFT);
    else if (bap != NV_MMU_PDE_APERTURE_BIG_INVALID) SET(e, NV_MMU_PDE_ADDRESS_BIG_SYS, big >> NV_MMU_PDE_ADDRESS_SHIFT);
    SET(e, NV_MMU_PDE_APERTURE_SMALL, sap);
    SET(e, NV_MMU_PDE_VOL_SMALL, svol);
    SET(e, NV_MMU_PDE_VOL_BIG, bvol);
    if (sap == NV_MMU_PDE_APERTURE_SMALL_VIDEO_MEMORY) SET(e, NV_MMU_PDE_ADDRESS_SMALL_VID, small >> NV_MMU_PDE_ADDRESS_SHIFT);
    else if (sap != NV_MMU_PDE_APERTURE_SMALL_INVALID) SET(e, NV_MMU_PDE_ADDRESS_SMALL_SYS, small >> NV_MMU_PDE_ADDRESS_SHIFT);
    printf("PDE %d %08x %08x\n", n, e[0], e[1]);
}

static void pdb(int n, uint64_t base, uint32_t target, uint32_t vol, uint64_t limit) {
    uint32_t r[132];
    memset(r, 0, sizeof r);
    SET(r, NV_RAMIN_PAGE_DIR_BASE_TARGET, target);
    SET(r, NV_RAMIN_PAGE_DIR_BASE_VOL, vol);
    SET(r, NV_RAMIN_PAGE_DIR_BASE_LO, base >> 12);
    SET(r, NV_RAMIN_PAGE_DIR_BASE_HI, base >> 32);
    SET(r, NV_RAMIN_ADR_LIMIT_LO, limit >> 12);
    SET(r, NV_RAMIN_ADR_LIMIT_HI, limit >> 32);
    printf("PDB %d %08x %08x %08x %08x\n", n, r[128], r[129], r[130], r[131]);
}

int main(void) {
    pte(0, 0x1000ull, 0, 0, 0, 0, 0);
    pte(1, 0x1FFFFFF000ull, 0, 1, 0xFE, 1, 1);
    pte(2, 0xFFFFFFF000ull, 2, 0, 0xDB, 0, 0);
    pte(3, 0x123456000ull, 3, 1, 0, 1, 0);
    pde(0, 0x2000ull, 1, 0, 0, 0, 0, 0);
    pde(1, 0, 0, 0x7000ull, 2, 0, 1, 2);
    pde(2, 0xABCDE000ull, 1, 0xFFFFFFF000ull, 3, 1, 1, 3);
    pde(3, 0x1FFFFFF000ull, 1, 0x1000ull, 1, 0, 0, 1);
    pdb(0, 0x20000000ull, 0, 0, 0xFFFFFFFFFFull);
    pdb(1, 0xFFFFFFF000ull, 3, 1, 0x1FFFFFFFFFull);
    pdb(2, 0xABC000ull, 2, 0, 0x123456789Aull);
    return 0;
}
'@.Replace("`r", ''))

$exe = Join-Path $work 'oracle.exe'
if (Test-Path $exe) { [IO.File]::Delete($exe) }
$log = & cmd /c "call `"$vcvars`" >nul && cd /d `"$work`" && cl /nologo /W3 /O2 oracle.c /Fe:oracle.exe" 2>&1
if (-not (Test-Path $exe)) { Write-Host 'oracle compile failed'; $log | ForEach-Object { Write-Host $_ }; exit 2 }
$out = & $exe
if ($LASTEXITCODE -ne 0) { Write-Host 'oracle run failed'; exit 2 }
[Console]::Out.Write((($out -join "`n") + "`n"))
