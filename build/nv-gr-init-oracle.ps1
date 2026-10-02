[CmdletBinding()]
param()

# Execute nouveau v6.12's GR firmware-list converters on synthetic records.
# Malformed-input refusal fixtures are local contracts, not nouveau claims.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot
$work = Join-Path $env:TEMP ('nv-gr-init-oracle-' + (Split-Path $repo -Leaf))
New-Item -ItemType Directory -Force $work | Out-Null
$base = 'https://raw.githubusercontent.com/torvalds/linux/v6.12/drivers/gpu/drm/nouveau/nvkm/engine/gr'
foreach ($ref in @(
    @('gk20a.c','CA22A0AE7652FC0DE509075401B4F7AC567D7F14AAC4638C6AA189B1F966A5FC'),
    @('gf100.h','513D4FC9B4B63B7B6CF9B6780A5135CD8D757BD1339755C58E548054FFC353C0')
)) {
    $path = Join-Path $work $ref[0]
    if (-not (Test-Path $path)) { Invoke-WebRequest "$base/$($ref[0])" -OutFile $path }
    if ((Get-FileHash $path).Hash -ne $ref[1]) { throw "Reference hash differs: $path" }
}
function Extract([string]$File, [string]$Pattern) {
    $text = [IO.File]::ReadAllText((Join-Path $work $File))
    $match = [regex]::Match($text, $Pattern, 'Multiline,Singleline')
    if (-not $match.Success) { throw "Cannot extract $Pattern from $File" }
    return $match.Value
}
$source = [IO.File]::ReadAllText((Join-Path $work 'gk20a.c'))
$license = $source.Substring(0, $source.IndexOf('*/') + 2)
$structs = @(
    (Extract 'gf100.h' '^struct gf100_gr_init \{.*?^\};'),
    (Extract 'gf100.h' '^struct gf100_gr_pack \{.*?^\};'),
    (Extract 'gk20a.c' '^struct gk20a_fw_av\s*\{.*?^\};'),
    (Extract 'gk20a.c' '^struct gk20a_fw_aiv\s*\{.*?^\};')
) -join "`n"
$functions = @('gk20a_gr_av_to_init_', 'gk20a_gr_av_to_init', 'gk20a_gr_aiv_to_init', 'gk20a_gr_av_to_method') | ForEach-Object {
    Extract 'gk20a.c' ('^int\r?\n' + $_ + '\(.*?^\}')
}
$head = @'
#include <stdio.h>
#include <stdint.h>
#include <stdlib.h>
typedef uint8_t u8;
typedef uint32_t u32;
typedef uint64_t u64;
struct nvkm_blob { size_t size; const void *data; };
#define vzalloc(size) calloc(1,size)
#define vfree(ptr) free(ptr)
#define ENOMEM 12
#define ENOSPC 28
'@
$main = @'
static void grade(const char *tag, const u32 *ws, size_t count, int mode) {
    struct nvkm_blob blob={count*4,ws};
    struct gf100_gr_pack *packs=NULL;
    int ret= mode==0 ? gk20a_gr_av_to_init(&blob,&packs) : mode==1 ? gk20a_gr_aiv_to_init(&blob,&packs) : gk20a_gr_av_to_method(&blob,&packs);
    if (ret) { if(ret!=-ENOSPC) abort(); printf("%s REFUSED\n",tag);return; }
    unsigned total=0;
    for(unsigned g=0;packs[g].init;g++) for(const struct gf100_gr_init *e=packs[g].init;e->count;e++) total++;
    printf("%s COUNT %u\n",tag,total);
    unsigned i=0;
    for(unsigned g=0;packs[g].init;g++) for(const struct gf100_gr_init *e=packs[g].init;e->count;e++,i++)
        printf("%s %u %u %08x %08x %u %u %016llx\n",tag,i,g,packs[g].type,e->addr,e->count,e->pitch,(unsigned long long)e->data);
    free(packs);
}
int main(void) {
    const u32 av[]={0x419E44,0x1FFFFE,0x41E100,3,0xFFFFFFFF,0x80000000};
    const u32 aiv[]={0x405800,0xDEADBEEF,0x10203040,0x405804,7,0xFFFFFFFF};
    const u32 method[]={0x0110B1C0,17,0x0111B1C0,18,0x0020B097,19,0x0030B1C0,20,0xFFFEB1C0,0xFFFFFFFF};
    u32 many[32]; for(unsigned i=0;i<16;i++){many[2*i]=0x1000000|(256+i);many[2*i+1]=1000+i;}
    grade("AV",av,sizeof(av)/4,0);
    grade("AIV",aiv,sizeof(aiv)/4,1);
    grade("METHOD",method,sizeof(method)/4,2);
    grade("METHOD15",many,30,2);
    grade("METHOD16",many,32,2);
    grade("EMPTY",av,0,0);
    puts("SHORT REFUSED\nBYTE REFUSED\nCLASS0 REFUSED");
    puts("AIVSHORT REFUSED\nMETHODSHORT REFUSED\nNEGATIVE REFUSED\nLATECLASS0 REFUSED");
    return 0;
}
'@
$unit = $license + "`n" + $head + "`n" + $structs + "`n" + ($functions -join "`n") + "`n" + $main
[IO.File]::WriteAllText((Join-Path $work 'oracle.c'), $unit.Replace("`r", ''), [Text.UTF8Encoding]::new($false))
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw 'MSVC unavailable' }
$vcvars = Join-Path $vs 'VC/Auxiliary/Build/vcvars64.bat'
$exe = Join-Path $work 'oracle.exe'
if (Test-Path $exe) { [IO.File]::Delete($exe) }
$log = & cmd /c "call `"$vcvars`" >nul && cd /d `"$work`" && cl /nologo /W3 /O2 oracle.c /Fe:oracle.exe" 2>&1
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $exe)) { $log | ForEach-Object { Write-Host $_ }; throw 'GR init oracle compile failed' }
$out = & $exe
if ($LASTEXITCODE -ne 0) { throw 'GR init oracle run failed' }
[Console]::Out.Write(($out -join "`n") + "`n")
