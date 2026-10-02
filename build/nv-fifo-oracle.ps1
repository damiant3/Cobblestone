[CmdletBinding()]
param(
    [string]$MesaDir = (Join-Path $env:TEMP 'nv-qmd-oracle\mesa')
)

# The oracle for codex/test/nv-fifo-maxwell: NVIDIA's clb06f.h and nvtypes.h (as vendored in
# Mesa, unmodified) supplies every field range, and Mesa's drf.h multi-word
# setter packs it, compiled by MSVC. Only drf.h's DRF_ helpers and
# NVVAL_MW_SET_X are taken, because its variadic dispatch macros are GNU C.
# Runlist entries execute nouveau's gm107_runl_insert_chan from hash-pinned
# Linux v6.12 source. REFUSE is our input contract, not nouveau behaviour.
#
#   pwsh build/nv-fifo-oracle.ps1 > codex/test/nv-fifo-maxwell.expected

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -PathType Container $MesaDir)) {
    & git clone --quiet --depth 1 --filter=blob:none --sparse --branch mesa-24.2.0 https://gitlab.freedesktop.org/mesa/mesa.git $MesaDir 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Host 'mesa clone failed'; exit 2 }
}
& git -C $MesaDir sparse-checkout add src/nouveau/headers/nvidia/classes src/gallium/drivers/nouveau/nvc0 2>&1 | Out-Null
& git -C $MesaDir checkout HEAD -- src/nouveau/headers/nvtypes.h 2>&1 | Out-Null
$header = Join-Path $MesaDir 'src\nouveau\headers\nvidia\classes\clb06f.h'
$types = Join-Path $MesaDir 'src\nouveau\headers\nvtypes.h'
$drf = Join-Path $MesaDir 'src\gallium\drivers\nouveau\nvc0\drf.h'
foreach ($p in $header, $types, $drf) { if (-not (Test-Path -PathType Leaf $p)) { Write-Host "MISSING: $p"; exit 2 } }

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { Write-Host 'MISSING: MSVC (Visual Studio C++ tools)'; exit 2 }
$vcvars = Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat'

$work = Join-Path $env:TEMP 'nv-fifo-oracle\work'
New-Item -ItemType Directory -Force $work | Out-Null
$nouveau = Join-Path $work 'gm107.c'
Invoke-WebRequest 'https://raw.githubusercontent.com/torvalds/linux/v6.12/drivers/gpu/drm/nouveau/nvkm/engine/fifo/gm107.c' -OutFile $nouveau
if ((Get-FileHash $nouveau).Hash -ne '5F931394BBF314D65119713936468C5E5D9BD7181F6EA622721014A4FFA4E621') { throw 'nouveau reference differs from the pinned bytes' }
$foreign = [IO.File]::ReadAllText($nouveau)
$entry = [regex]::Match($foreign, '(?ms)^static void\r?\ngm107_runl_insert_chan\(.*?^\}')
if (-not $entry.Success) { throw 'nouveau runlist writer not found' }
$license = $foreign.Substring(0, $foreign.IndexOf('*/') + 2)
[IO.File]::WriteAllText((Join-Path $work 'runlist.h'), $license + "`n" + $entry.Value)
$ramfcSource = Join-Path $work 'gk104.c'
Invoke-WebRequest 'https://raw.githubusercontent.com/torvalds/linux/v6.12/drivers/gpu/drm/nouveau/nvkm/engine/fifo/gk104.c' -OutFile $ramfcSource
if ((Get-FileHash $ramfcSource).Hash -ne 'D9CE2A7495358C75E5CEFAE2CEAE099D3E161EE960CBA2415BA24B5C08374DAE') { throw 'nouveau RAMFC reference differs from the pinned bytes' }
$foreign = [IO.File]::ReadAllText($ramfcSource)
$entry = [regex]::Match($foreign, '(?ms)^static int\r?\ngk104_chan_ramfc_write\(.*?^\}')
if (-not $entry.Success) { throw 'nouveau RAMFC writer not found' }
$license = $foreign.Substring(0, $foreign.IndexOf('*/') + 2)
[IO.File]::WriteAllText((Join-Path $work 'ramfc.h'), $license + "`n" + $entry.Value)
$drfLines = [Collections.Generic.List[string]]::new()
$inSet = $false
foreach ($l in [IO.File]::ReadAllLines($drf)) {
    if ($l -match '^#define DRF_') { $drfLines.Add($l) }
    elseif ($l -match '^#define NVVAL_MW_SET_X\(') { $inSet = $true; $drfLines.Add($l) }
    elseif ($inSet) { $drfLines.Add($l); if ($l -match 'while\(0\)') { $inSet = $false } }
}
[IO.File]::WriteAllLines((Join-Path $work 'drf_mw.h'), $drfLines)
Copy-Item -Force $header (Join-Path $work 'clb06f.h')
Copy-Item -Force $types (Join-Path $work 'nvtypes.h')

[IO.File]::WriteAllText((Join-Path $work 'oracle.c'), @'
#include <stdio.h>
#include <stdint.h>
#include <stdbool.h>
#include "drf_mw.h"
#include "clb06f.h"

typedef uint64_t u64;
typedef uint32_t u32;
struct nvkm_memory { uint64_t addr; uint32_t words[64]; unsigned writes; };
#define nvkm_gpuobj nvkm_memory
struct nvkm_chan { uint32_t id; struct nvkm_gpuobj *inst; struct { struct nvkm_memory *mem; uint64_t base; } userd; };
static void nvkm_wo32(struct nvkm_memory *memory, uint64_t offset, uint32_t value) { memory->words[offset / 4] = value; memory->writes++; }
static uint64_t nvkm_memory_addr(struct nvkm_memory *memory) { return memory->addr; }
#define nvkm_kmap(memory) ((void)0)
#define nvkm_done(memory) ((void)0)
#define lower_32_bits(value) ((uint32_t)(value))
#define upper_32_bits(value) ((uint32_t)((value) >> 32))
static unsigned ilog2(uint64_t value) { unsigned bits = 0; while (value >>= 1) bits++; return bits; }
#include "runlist.h"
#include "ramfc.h"

static void rl(int n, uint32_t chid, uint64_t addr) {
    struct nvkm_gpuobj inst = { addr };
    struct nvkm_chan chan = { chid, &inst };
    struct nvkm_memory memory = { 0, { 0xdeadbeef, 0xdeadbeef }, 0 };
    gm107_runl_insert_chan(&chan, &memory, 0);
    printf("RL %d %u %08x %08x\n", n, memory.writes, memory.words[0], memory.words[1]);
}

static void ramfc(int n, uint64_t userd_addr, uint64_t offset, uint64_t length, uint32_t devm, bool priv, uint32_t chid) {
    struct nvkm_memory inst = { 0 }, userd = { userd_addr };
    struct nvkm_chan chan = { chid, &inst, { &userd, 0 } };
    gk104_chan_ramfc_write(&chan, offset, length, devm, priv);
    printf("RAMFC %d %zu", n, sizeof(inst.words) / sizeof(inst.words[0]));
    for (unsigned i = 0; i < sizeof(inst.words) / sizeof(inst.words[0]); i++) printf(" %08x", inst.words[i]);
    puts("");
}

#define SET(o, f, v) NVVAL_MW_SET_X(o, MW(f), (v))

static void gp(int n, uint64_t addr, uint32_t dwords, uint32_t priv, uint32_t sync) {
    uint32_t w0[1] = { 0 }, w1[1] = { 0 };
    SET(w0, NVB06F_GP_ENTRY0_FETCH, NVB06F_GP_ENTRY0_FETCH_UNCONDITIONAL);
    SET(w0, NVB06F_GP_ENTRY0_GET, addr >> 2);
    SET(w1, NVB06F_GP_ENTRY1_GET_HI, addr >> 32);
    SET(w1, NVB06F_GP_ENTRY1_PRIV, priv);
    SET(w1, NVB06F_GP_ENTRY1_LEVEL, NVB06F_GP_ENTRY1_LEVEL_MAIN);
    SET(w1, NVB06F_GP_ENTRY1_LENGTH, dwords);
    SET(w1, NVB06F_GP_ENTRY1_SYNC, sync);
    printf("GP %d %08x %08x\n", n, w0[0], w1[0]);
}

static void mh(int n, uint32_t op, uint32_t subc, uint32_t method, uint32_t count) {
    uint32_t w[1] = { 0 };
    SET(w, NVB06F_DMA_METHOD_ADDRESS, method >> 2);
    SET(w, NVB06F_DMA_METHOD_SUBCHANNEL, subc);
    SET(w, NVB06F_DMA_METHOD_COUNT, count);
    SET(w, NVB06F_DMA_SEC_OP, op);
    printf("MH %d %08x\n", n, w[0]);
}

int main(void) {
    gp(0, 0x12345678ull, 16, 0, 0);
    gp(1, 0xFFFFFFFFFCull, 0x1FFFFF, NVB06F_GP_ENTRY1_PRIV_KERNEL, NVB06F_GP_ENTRY1_SYNC_WAIT);
    gp(2, 0x1000ull, 1, 0, 1);
    gp(3, 0xAB00000004ull, 0xABCDE, 1, 0);
    mh(0, NVB06F_DMA_SEC_OP_INC_METHOD, 1, 0x2B4, 1);
    mh(1, NVB06F_DMA_SEC_OP_INC_METHOD, 1, 0x2BC, 1);
    mh(2, NVB06F_DMA_SEC_OP_NON_INC_METHOD, 7, 0x3FFC, 0x1FFF);
    mh(3, NVB06F_DMA_SEC_OP_IMMD_DATA_METHOD, 2, 0x110, 0xABC);
    mh(4, NVB06F_DMA_SEC_OP_ONE_INC, 5, 0x4, 0);
    rl(0, 0, 0);
    rl(1, 3, 0x12345678000ull);
    rl(2, 2047, 0xABCDE000ull);
    rl(3, 0xFFFFFFFF, 0xFFFFFFFF000ull);
    puts("REFUSE 0 0 0 0 0");
    ramfc(0, 0x12345678900ull, 0xAB12345000ull, 256, 0x123, false, 3);
    ramfc(1, 0xABCDEF00100ull, 0xFFFFFFFFF8ull, 65536, 0xFFF, true, 2047);
    ramfc(2, 0, 0, 8, 0, false, 0);
    puts("RAMFC-REFUSE 0 0 0 0 0");
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
