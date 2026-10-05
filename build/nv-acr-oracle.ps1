[CmdletBinding()]
param()

# Runs nouveau v6.12's original ACR setup and high-security descriptor writer
# with synthetic image data and a capture-only falcon. No firmware is loaded.
# REFUSE/PRESERVE express the Codex builder's local input and alias contracts.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot
$work = Join-Path $env:TEMP ('nv-acr-oracle-' + (Split-Path $repo -Leaf))
New-Item -ItemType Directory -Force $work | Out-Null
$base = 'https://raw.githubusercontent.com/torvalds/linux/v6.12/drivers/gpu/drm/nouveau'
$inputs = @(
    @('gm200.c','nvkm/subdev/acr/gm200.c','E3EB342D116930173C65DB020DA642ECA92E4953FD012976676B98C19AF34C62'),
    @('acr.h','include/nvfw/acr.h','8673936F9CBFF523F125A7FDDB1190E186559739FC35FAB5E7385FF6D4DA62AC'),
    @('flcn.h','include/nvfw/flcn.h','D3C894670485F624A0EFED951E2FE16546C976158B034ADA566DCF33EB0273FB'),
    @('falcon.h','include/nvkm/engine/falcon.h','F6BC13C54AD465439F6A52FC9960F2AA65A3A6CD24E772E1C612080CB4C1E4BD'),
    @('subdev-acr.h','include/nvkm/subdev/acr.h','D14C80E4DE3CAE0EF3BE2B9B233A85149F79D8B9B5B2B0687D45C1AAAE637AD0'),
    @('lsfw.c','nvkm/subdev/acr/lsfw.c','6003B059C360F223A726D6AD4D1F525A19D0407ECF812491081D0136F5C1A5AE'),
    @('gr-gm200.c','nvkm/engine/gr/gm200.c','45E36F384792C84078E47909927B9C77CCC0D48CF2B30B7B6CC936AB06D84E18'),
    @('fw.h','include/nvfw/fw.h','29524E3B1586A674924D5019A2F694B5EB47737032E75C91B4BA2C443CF5E27A')
)
foreach ($refInput in $inputs) {
    $path = Join-Path $work $refInput[0]
    if (-not (Test-Path -LiteralPath $path)) { Invoke-WebRequest "$base/$($refInput[1])" -OutFile $path }
    if ((Get-FileHash $path).Hash -ne $refInput[2]) { throw "Reference hash differs: $path" }
}
function Extract([string]$File, [string]$Pattern) {
    $text = [IO.File]::ReadAllText((Join-Path $work $File))
    $match = [regex]::Match($text, $Pattern, 'Multiline,Singleline')
    if (-not $match.Success) { throw "Cannot extract $Pattern from $File" }
    return $match.Value
}
$source = [IO.File]::ReadAllText((Join-Path $work 'gm200.c'))
$license = $source.Substring(0, $source.IndexOf('*/') + 2)
$grSource = [IO.File]::ReadAllText((Join-Path $work 'gr-gm200.c'))
$license += "`n" + $grSource.Substring(0, $grSource.IndexOf('*/') + 2)
$desc = (Extract 'acr.h' '^struct flcn_acr_desc \{.*?^\};').Replace('__aligned(8)', '')
$bld = (Extract 'flcn.h' '^struct flcn_bl_dmem_desc_v1 \{.*?^\} __packed;').Replace('__packed', '')
$dma = Extract 'falcon.h' '^enum nvkm_falcon_dmaidx \{.*?^\};'
$setup = Extract 'gm200.c' '^static int\r?\ngm200_acr_load_setup\(.*?^\}'
$load = Extract 'gm200.c' '^int\r?\ngm200_acr_hsfw_load_bld\(.*?^\}'
$wprHeader = Extract 'acr.h' '^struct wpr_header \{.*?^\};'
$signature = Extract 'acr.h' '^struct lsf_signature \{.*?^\};'
$tail = Extract 'acr.h' '^struct lsb_header_tail \{.*?^\};'
$lsb = Extract 'acr.h' '^struct lsb_header \{.*?^\};'
$layout = Extract 'gm200.c' '^u32\r?\ngm200_acr_wpr_layout\(.*?^\}'
$tailBuild = Extract 'gm200.c' '^void\r?\ngm200_acr_wpr_build_lsb_tail\(.*?^\}'
$lsbBuild = Extract 'gm200.c' '^static int\r?\ngm200_acr_wpr_build_lsb\(.*?^\}'
$wprBuild = Extract 'gm200.c' '^int\r?\ngm200_acr_wpr_build\(.*?^\}'
$ids = Extract 'subdev-acr.h' '^enum nvkm_acr_lsf_id \{.*?^\};'
$fwBin = Extract 'fw.h' '^struct nvfw_bin_hdr \{.*?^\};'
$fwBl = Extract 'fw.h' '^struct nvfw_bl_desc \{.*?^\};'
$lsLoad = Extract 'lsfw.c' '^int\r?\nnvkm_acr_lsfw_load_bl_inst_data_sig\(.*?^\}'
$blWrite = Extract 'gr-gm200.c' '^static void\r?\ngm200_gr_acr_bld_write\(.*?^\}'
$blPatch = Extract 'gr-gm200.c' '^static void\r?\ngm200_gr_acr_bld_patch\(.*?^\}'
$head = @'
#include <stdio.h>
#include <stdint.h>
#include <string.h>
#include <stdlib.h>
typedef uint8_t u8;
typedef uint32_t u32;
typedef uint64_t u64;
typedef int64_t s64;
struct nvkm_subdev { struct nvkm_device *device; };
struct nvkm_acr { struct nvkm_subdev subdev; u64 wpr_start, wpr_end; u8 *wpr; struct nvkm_acr_lsfw *lsfw; };
struct nvkm_device { struct nvkm_acr *acr; };
struct nvkm_falcon { struct nvkm_subdev *owner, *user; };
struct nvkm_vma { u64 addr; };
struct nvkm_falcon_fw {
    struct { u8 *img; } fw;
    struct nvkm_falcon *falcon;
    struct nvkm_vma *vma;
    u32 nmem_base, nmem_size, imem_base, imem_size, dmem_base_img, dmem_size;
};
#define flcn_acr_desc_dump(a,b) ((void)0)
#define flcn_bl_dmem_desc_v1_dump(a,b) ((void)0)
enum { DMEM = 1 };
#define ALIGN(n,a) (((n)+(a)-1) & ~((a)-1))
#define WARN_ON(x) (x)
#define EINVAL 22
struct nvkm_acr_lsf { u32 id; };
struct nvkm_acr_lsf_func { u32 flags, bld_size; void (*bld_write)(struct nvkm_acr *, u32, struct nvkm_acr_lsfw *); };
struct firmware { size_t size; const u8 *data; };
struct nvkm_acr_lsfw {
    struct nvkm_acr_lsfw *next;
    const struct nvkm_acr_lsf_func *func;
    u32 id;
    struct { u32 size; u8 *data; } img;
    const struct firmware *sig;
    struct { u32 lsb, img, bld; } offset;
    u32 bl_data_size, ucode_size, data_size, bootloader_size, bootloader_imem_offset;
    u32 app_start_offset, app_resident_code_offset, app_resident_code_size;
    u32 app_resident_data_offset, app_resident_data_size;
    u32 app_imem_entry, app_size;
};
#define list_for_each_entry(pos,head,member) for ((pos)=*(head); (pos); (pos)=(pos)->next)
static void nvkm_wobj(u8 *memory, u32 offset, const void *data, size_t size) { if ((u64)offset+size > 65536) abort(); memcpy(memory+offset,data,size); }
static void nvkm_wo32(u8 *memory, u32 offset, u32 value) { nvkm_wobj(memory,offset,&value,4); }
static void nvkm_robj(u8 *memory, u32 offset, void *data, size_t size) { if ((u64)offset+size > 65536) abort(); memcpy(data,memory+offset,size); }
static void skip_bld(struct nvkm_acr *acr, u32 offset, struct nvkm_acr_lsfw *lsfw) { }
static u8 captured[128];
static size_t captured_size;
static int nvkm_falcon_pio_wr(struct nvkm_falcon *f, u8 *bytes, int a, int b, int mem, int c, size_t size, int d, int e) {
    if (mem != DMEM || size > sizeof(captured)) abort();
    memcpy(captured, bytes, size); captured_size = size; return 0;
}
static void words(const char *tag, int n, const void *data, size_t size) {
    printf("%s %d %zu", tag, n, size / 4);
    for (size_t i = 0; i < size; i += 4) { u32 v; memcpy(&v, (const u8 *)data+i, 4); printf(" %08x", v); }
    puts("");
}
'@
$loadStubs = @'
/* Header accessors and firmware I/O are stubs over valid synthetic headers.
 * The original loader supplies all layout arithmetic, copies and padding. */
#define GFP_KERNEL 0
#define ENOMEM 12
#define IS_ERR(p) (!(p))
#define PTR_ERR(p) (-ENOMEM)
#define kzalloc(size,flags) calloc(1,size)
static struct firmware files[4];
static struct nvkm_acr_lsfw *nvkm_acr_lsfw_add(const struct nvkm_acr_lsf_func *func, struct nvkm_acr *acr, struct nvkm_falcon *falcon, enum nvkm_acr_lsf_id id) {
    struct nvkm_acr_lsfw *p=calloc(1,sizeof(*p)); if(p){p->func=func;p->id=id;acr->lsfw=p;} return p;
}
static void nvkm_acr_lsfw_del(struct nvkm_acr_lsfw *p) { free(p->img.data); free(p); }
static int nvkm_firmware_load_name(struct nvkm_subdev *s,const char *path,const char *name,int ver,const struct firmware **fw) {
    unsigned i= !strcmp(name,"bl") ? 0 : !strcmp(name,"inst") ? 1 : !strcmp(name,"data") ? 2 : 3;
    *fw=&files[i]; return 0;
}
static void nvkm_firmware_put(const struct firmware *fw) { }
static const struct nvfw_bin_hdr *nvfw_bin_hdr(struct nvkm_subdev *s,const void *data) { return data; }
static const struct nvfw_bl_desc *nvfw_bl_desc(struct nvkm_subdev *s,const void *data) { return data; }
'@
$main = @'
static void setup(int n, u32 pattern, u64 start, u64 limit) {
    struct flcn_acr_desc desc;
    for (size_t i = 0; i < sizeof(desc)/4; i++) ((u32 *)&desc)[i] = pattern + (u32)i;
    struct nvkm_acr acr = { { 0 }, start, limit };
    struct nvkm_device device = { &acr };
    struct nvkm_subdev subdev = { &device };
    struct nvkm_falcon falcon = { &subdev, &subdev };
    struct nvkm_falcon_fw fw = { 0 }; fw.falcon = &falcon; fw.fw.img = (u8 *)&desc;
    if (gm200_acr_load_setup(&fw)) abort();
    words("DESC", n, &desc, sizeof(desc));
}
static void load(int n, u64 base, u32 noff, u32 nsize, u32 soff, u32 ssize, u32 doff, u32 dsize) {
    struct nvkm_vma vma = { base };
    struct nvkm_falcon falcon = { 0 };
    struct nvkm_falcon_fw fw = { 0 };
    fw.vma = &vma; fw.falcon = &falcon;
    fw.nmem_base = noff; fw.nmem_size = nsize; fw.imem_base = soff; fw.imem_size = ssize;
    fw.dmem_base_img = doff; fw.dmem_size = dsize;
    if (gm200_acr_hsfw_load_bld(&fw)) abort();
    words("HS", n, captured, captured_size);
}
static void wpr(int rtos_id) {
    u8 memory[65536] = { 0 }, images[8193] = { 0 };
    u32 sig0[19], sig1[19];
    for (unsigned i=0;i<19;i++) { sig0[i]=0x12340000+i; sig1[i]=0xABCD0000+i; }
    struct firmware sigs[2] = { { sizeof(sig0),(u8 *)sig0 }, { sizeof(sig1),(u8 *)sig1 } };
    struct nvkm_acr_lsf_func funcs[2] = { { 0,76,skip_bld }, { 8,76,skip_bld } };
    struct nvkm_acr_lsfw items[2] = { 0 };
    for (unsigned i=0;i<2;i++) {
        struct nvkm_acr_lsfw *p=&items[i]; p->next=i ? NULL : &items[1];
        p->func=&funcs[i]; p->id=2+i; p->img.size=i ? 8193:4097; p->img.data=images; p->sig=&sigs[i];
        p->ucode_size=i ? 8704:4352; p->data_size=i ? 768:512; p->bootloader_size=i ? 512:256;
        p->bootloader_imem_offset=i ? 0x280:0x180; p->app_start_offset=i ? 512:256;
        p->app_resident_code_offset=i ? 35:17; p->app_resident_code_size=i ? 8193:4097;
        p->app_resident_data_offset=i ? 8195:4099; p->app_resident_data_size=i ? 654:321;
    }
    struct nvkm_acr acr = { 0 }; acr.wpr=memory; acr.lsfw=items;
    u32 size=gm200_acr_wpr_layout(&acr);
    struct nvkm_acr_lsf rtos = { (u32)rtos_id };
    if (gm200_acr_wpr_build(&acr,rtos_id<0 ? NULL:&rtos)) abort();
    for (unsigned i=0;i<2;i++) {
        struct nvkm_acr_lsfw *p=&items[i];
        u32 slot[]={p->offset.lsb,p->offset.img,p->offset.bld,p->bl_data_size,p->offset.bld+p->bl_data_size};
        if (i==1 && slot[4]!=size) abort();
        words("SLOT",i,slot,sizeof(slot));
    }
    words("WPR",0,memory,sizeof(struct wpr_header));
    words("WPR",1,memory+sizeof(struct wpr_header),sizeof(struct wpr_header));
    words("END",0,memory+2*sizeof(struct wpr_header),4);
    words("LSB",0,memory+items[0].offset.lsb,sizeof(struct lsb_header));
    words("LSB",1,memory+items[1].offset.lsb,sizeof(struct lsb_header));
}
static void load_image(int n,u32 code_size,u32 tag,u32 inst_size,u32 data_size,u64 wpr_base) {
    u32 boot_size=ALIGN(code_size,256);
    u8 *bl=calloc(1,128+boot_size), *inst=calloc(1,inst_size), *data=calloc(1,data_size);
    if(!bl || !inst || !data) abort();
    struct nvfw_bin_hdr *hdr=(void *)bl; hdr->header_offset=32;hdr->data_offset=128;
    struct nvfw_bl_desc *desc=(void *)(bl+32);desc->code_size=code_size;desc->start_tag=tag;
    for(u32 i=0;i<boot_size;i++) bl[128+i]=(u8)(16+n+i);
    for(u32 i=0;i<inst_size;i++) inst[i]=(u8)(64+n+i);
    for(u32 i=0;i<data_size;i++) data[i]=(u8)(160+n+i);
    files[0]=(struct firmware){128+boot_size,bl};files[1]=(struct firmware){inst_size,inst};
    files[2]=(struct firmware){data_size,data};files[3]=(struct firmware){0,NULL};
    u8 wpr_mem[65536]={0};
    struct nvkm_acr acr={0};acr.wpr=wpr_mem;
    struct nvkm_device device={&acr};struct nvkm_subdev subdev={&device};
    struct nvkm_falcon falcon={&subdev,&subdev};struct nvkm_acr_lsf_func func={0};
    if(nvkm_acr_lsfw_load_bl_inst_data_sig(&subdev,&falcon,(enum nvkm_acr_lsf_id)(2+n),"synthetic",0,&func)) abort();
    struct nvkm_acr_lsfw *p=acr.lsfw;
    u32 layout[]={p->bootloader_size,p->bootloader_imem_offset,p->app_start_offset,p->app_imem_entry,
        p->app_resident_code_offset,p->app_resident_code_size,p->app_resident_data_offset,p->app_resident_data_size,
        p->app_size,p->img.size,p->ucode_size,p->data_size};
    words("LOAD",n,layout,sizeof(layout));
    printf("IMAGE %d %u ",n,p->img.size);for(u32 i=0;i<p->img.size;i++) printf("%02x",p->img.data[i]);puts("");
    p->offset.img=4096+n*8192;p->app_resident_code_offset=n*17;p->app_imem_entry=n*19;
    gm200_gr_acr_bld_write(&acr,0,p);words("BLD",n,wpr_mem,sizeof(struct flcn_bl_dmem_desc_v1));
    gm200_gr_acr_bld_patch(&acr,0,(s64)wpr_base);words("RELOC",n,wpr_mem,sizeof(struct flcn_bl_dmem_desc_v1));
    nvkm_acr_lsfw_del(p);free(bl);free(inst);free(data);
}
int main(void) {
    setup(0, 0x12340000, 0x1234560000ull, 0x12345A0000ull);
    puts("PRESERVE True");
    setup(1, 0xA5A50000, 0xFFFFFF0000ull, 0xFFFFFFFF00ull);
    load(0, 0x123456789000ull, 0x100, 0x234, 0x567, 0x890, 0xABC, 0xDEF);
    load(1, 0xFFFFFFF0ull, 0, 1, 0xFFFFFFFF, 2, 0x1234, 3);
    puts("REFUSE 0 0 0");
    puts("HS-REFUSE 0");
    wpr(-1);
    wpr(3);
    puts("WPR-REFUSE 0 0");
    load_image(0,73,3,257,13,0x1234560000ull);
    load_image(1,257,0xABC,1,257,0xFFFFFF00ull);
    puts("LOAD-REFUSE 0 0 0");
    return 0;
}
'@
$unit = $license + "`n" + $head + "`n" + $desc + "`n#pragma pack(push,1)`n" + $bld + "`n#pragma pack(pop)`n" + $dma + "`n" + $ids + "`n" + $wprHeader + "`n" + $signature + "`n" + $tail + "`n" + $lsb + "`n" + $fwBin + "`n" + $fwBl + "`n" + $loadStubs + "`n" + $setup + "`n" + $load + "`n" + $layout + "`n" + $tailBuild + "`n" + $lsbBuild + "`n" + $wprBuild + "`n" + $lsLoad + "`n" + $blWrite + "`n" + $blPatch + "`n" + $main
[IO.File]::WriteAllText((Join-Path $work 'oracle.c'), $unit.Replace("`r", ''), [Text.UTF8Encoding]::new($false))
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw 'MSVC unavailable' }
$vcvars = Join-Path $vs 'VC/Auxiliary/Build/vcvars64.bat'
$exe = Join-Path $work 'oracle.exe'
if (Test-Path $exe) { [IO.File]::Delete($exe) }
$log = & cmd /c "call `"$vcvars`" >nul && cd /d `"$work`" && cl /nologo /W3 /O2 oracle.c /Fe:oracle.exe" 2>&1
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $exe)) { $log | ForEach-Object { Write-Host $_ }; throw 'ACR oracle compile failed' }
$out = & $exe
if ($LASTEXITCODE -ne 0) { throw 'ACR oracle run failed' }
[Console]::Out.Write(($out -join "`n") + "`n")
