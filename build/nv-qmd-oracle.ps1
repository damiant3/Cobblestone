[CmdletBinding()]
param(
    [string]$MesaDir = (Join-Path $env:TEMP 'nv-qmd-oracle\mesa')
)

# The oracle for codex/test/nv-qmd-maxwell: NVIDIA's cla0c0qmd.h (as vendored
# in Mesa, unmodified) supplies every field position, and Mesa's own drf.h
# multi-word setter packs it, compiled by MSVC. drf.h's variadic dispatch
# macros are GNU C and MSVC cannot parse them, so only its DRF_ helpers and
# NVVAL_MW_SET_X are taken, line for line. The values set are the union of
# Mesa's two V00_06 fillers, as codex/os/kernel/NvQmd.codex records.
#
#   pwsh build/nv-qmd-oracle.ps1 > codex/test/nv-qmd-maxwell.expected

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$tag = 'mesa-24.2.0'
if (-not (Test-Path -PathType Container $MesaDir)) {
    & git clone --quiet --depth 1 --filter=blob:none --sparse --branch $tag https://gitlab.freedesktop.org/mesa/mesa.git $MesaDir 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Host 'mesa clone failed'; exit 2 }
    & git -C $MesaDir sparse-checkout set src/nouveau/headers/nvidia/classes src/gallium/drivers/nouveau/nvc0 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Host 'mesa sparse checkout failed'; exit 2 }
}
$header = Join-Path $MesaDir 'src\nouveau\headers\nvidia\classes\cla0c0qmd.h'
$drf = Join-Path $MesaDir 'src\gallium\drivers\nouveau\nvc0\drf.h'
foreach ($p in $header, $drf) { if (-not (Test-Path -PathType Leaf $p)) { Write-Host "MISSING: $p"; exit 2 } }

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { Write-Host 'MISSING: MSVC (Visual Studio C++ tools)'; exit 2 }
$vcvars = Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat'

$work = Join-Path $env:TEMP 'nv-qmd-oracle\work'
New-Item -ItemType Directory -Force $work | Out-Null
$drfLines = [Collections.Generic.List[string]]::new()
$inSet = $false
foreach ($l in [IO.File]::ReadAllLines($drf)) {
    if ($l -match '^#define DRF_') { $drfLines.Add($l) }
    elseif ($l -match '^#define NVVAL_MW_SET_X\(') { $inSet = $true; $drfLines.Add($l) }
    elseif ($inSet) { $drfLines.Add($l); if ($l -match 'while\(0\)') { $inSet = $false } }
}
[IO.File]::WriteAllLines((Join-Path $work 'drf_mw.h'), $drfLines)
Copy-Item -Force $header (Join-Path $work 'cla0c0qmd.h')

[IO.File]::WriteAllText((Join-Path $work 'oracle.c'), @'
#include <stdio.h>
#include <stdint.h>
#include <string.h>
#include "drf_mw.h"
#include "cla0c0qmd.h"

#define SET(f, v) NVVAL_MW_SET_X(q, NVA0C0_QMDV00_06_##f, (v))
#define SETI(f, i, v) NVVAL_MW_SET_X(q, NVA0C0_QMDV00_06_##f(i), (v))
#define DEF(f, e) NVVAL_MW_SET_X(q, NVA0C0_QMDV00_06_##f, NVA0C0_QMDV00_06_##f##_##e)

typedef struct { uint64_t po, gx, gy, gz, bx, by, bz, sm, lm, rc, bc, ca, cs; } launch;

static const launch vectors[] = {
    { 0x1000, 4, 1, 1, 128, 1, 1, 0, 0, 28, 0, 0x200001000ull, 0x160 },
    { 0x12345678, 70000, 3, 2, 32, 8, 4, 20000, 100, 255, 1, 0xFFFFFFFF00ull, 65536 },
    { 0, 0xFFFFFFFFull, 65535, 65535, 65535, 1, 1, 40000, 0xFFFFF0, 64, 31, 0, 0 },
    { 0x40, 1, 1, 1, 1, 1, 1, 16384, 16, 8, 0, 0x100, 256 },
    { 0x80, 2, 2, 2, 2, 2, 2, 16385, 17, 9, 2, 0x200, 4 },
    { 0xC0, 3, 1, 1, 3, 1, 1, 32768, 0, 10, 3, 0x300, 8 },
    { 0x100, 5, 1, 1, 5, 1, 1, 32769, 0, 11, 4, 0x400, 12 },
};

int main(void) {
    for (int j = 0; j < (int)(sizeof vectors / sizeof vectors[0]); j++) {
        const launch *v = &vectors[j];
        uint32_t q[64];
        memset(q, 0, sizeof q);
        /* NAK Qmd0_6::new */
        SET(QMD_MAJOR_VERSION, 0);
        SET(QMD_VERSION, 6);
        DEF(API_VISIBLE_CALL_LIMIT, NO_CHECK);
        DEF(SAMPLER_INDEX, INDEPENDENTLY);
        SET(SASS_VERSION, 0x30);
        /* gallium nve4_compute_setup_launch_desc */
        DEF(INVALIDATE_TEXTURE_HEADER_CACHE, TRUE);
        DEF(INVALIDATE_TEXTURE_SAMPLER_CACHE, TRUE);
        DEF(INVALIDATE_TEXTURE_DATA_CACHE, TRUE);
        DEF(INVALIDATE_SHADER_DATA_CACHE, TRUE);
        DEF(INVALIDATE_SHADER_CONSTANT_CACHE, TRUE);
        DEF(RELEASE_MEMBAR_TYPE, FE_SYSMEMBAR);
        DEF(CWD_MEMBAR_TYPE, L1_SYSMEMBAR);
        SET(SHADER_LOCAL_MEMORY_CRS_SIZE, 0x800);
        /* both */
        SET(PROGRAM_OFFSET, v->po);
        SET(CTA_RASTER_WIDTH, v->gx);
        SET(CTA_RASTER_HEIGHT, v->gy);
        SET(CTA_RASTER_DEPTH, v->gz);
        SET(CTA_THREAD_DIMENSION0, v->bx);
        SET(CTA_THREAD_DIMENSION1, v->by);
        SET(CTA_THREAD_DIMENSION2, v->bz);
        uint64_t smem = (v->sm + 0xff) & ~0xffull;
        SET(SHARED_MEMORY_SIZE, smem);
        if (smem <= (16 << 10)) DEF(L1_CONFIGURATION, DIRECTLY_ADDRESSABLE_MEMORY_SIZE_16KB);
        else if (smem <= (32 << 10)) DEF(L1_CONFIGURATION, DIRECTLY_ADDRESSABLE_MEMORY_SIZE_32KB);
        else DEF(L1_CONFIGURATION, DIRECTLY_ADDRESSABLE_MEMORY_SIZE_48KB);
        SET(SHADER_LOCAL_MEMORY_LOW_SIZE, (v->lm + 0xf) & ~0xfull);
        SET(SHADER_LOCAL_MEMORY_HIGH_SIZE, 0);
        SET(REGISTER_COUNT, v->rc);
        SET(BARRIER_COUNT, v->bc);
        if (v->cs > 0) {
            SETI(CONSTANT_BUFFER_ADDR_LOWER, 0, v->ca);
            SETI(CONSTANT_BUFFER_ADDR_UPPER, 0, v->ca >> 32);
            SETI(CONSTANT_BUFFER_SIZE, 0, v->cs);
            SETI(CONSTANT_BUFFER_VALID, 0, NVA0C0_QMDV00_06_CONSTANT_BUFFER_VALID_TRUE);
        }
        printf("Q %d\n", j);
        for (int k = 0; k < 64; k += 8)
            printf("%08x %08x %08x %08x %08x %08x %08x %08x\n", q[k], q[k+1], q[k+2], q[k+3], q[k+4], q[k+5], q[k+6], q[k+7]);
    }
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
