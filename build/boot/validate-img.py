import struct, sys

# Image path comes from argv. The old hardcoded path silently validated
# build/boot/optiona.img no matter what was passed -- which once made
# three different depot images read as "identical".
img_path = sys.argv[1] if len(sys.argv) > 1 else r"D:\Projects\NewRepository-fester\build\boot\optiona.img"
img = open(img_path, "rb").read()
print(f"validating: {img_path}")
SS = 512
def u16(b,o): return struct.unpack_from("<H",b,o)[0]
def u32(b,o): return struct.unpack_from("<I",b,o)[0]
def u64(b,o): return struct.unpack_from("<Q",b,o)[0]

print(f"image size: {len(img)} bytes = {len(img)//SS} sectors")

# --- Protective MBR ---
print("\n== MBR (sector 0) ==")
print("  boot sig 0xAA55:", hex(u16(img,510)))
pt = 446
print("  part0 type:", hex(img[pt+4]), "(0xEE = GPT protective)")
print("  part0 start LBA:", u32(img,pt+8), " size:", u32(img,pt+12))

# --- GPT header (sector 1) ---
g = SS
print("\n== GPT header (sector 1) ==")
sig = img[g:g+8]
print("  signature:", sig, "(b'EFI PART')")
print("  revision:", hex(u32(img,g+8)))
print("  header size:", u32(img,g+12))
print("  my LBA:", u64(img,g+24), " alt LBA:", u64(img,g+32))
print("  first usable:", u64(img,g+40), " last usable:", u64(img,g+48))
part_lba = u64(img,g+72)
nparts = u32(img,g+80)
psize = u32(img,g+84)
print(f"  part entry LBA: {part_lba}  count: {nparts}  entrysize: {psize}")
hdr_crc = u32(img,g+16)
# verify header CRC
import binascii
hdr = bytearray(img[g:g+92]); hdr[16:20]=b"\0\0\0\0"
calc = binascii.crc32(bytes(hdr)) & 0xffffffff
print(f"  header CRC: stored={hex(hdr_crc)} calc={hex(calc)} {'OK' if hdr_crc==calc else 'MISMATCH!!'}")
# partition array CRC
pe_crc = u32(img,g+88)
parr = img[part_lba*SS : part_lba*SS + nparts*psize]
calc2 = binascii.crc32(parr) & 0xffffffff
print(f"  part-array CRC: stored={hex(pe_crc)} calc={hex(calc2)} {'OK' if pe_crc==calc2 else 'MISMATCH!!'}")

# --- Partition entry 0 ---
p = part_lba*SS
tguid = img[p:p+16]
ESP = bytes([0x28,0x73,0x2A,0xC1,0x1F,0xF8,0xD2,0x11,0xBA,0x4B,0x00,0xA0,0xC9,0x3E,0xC9,0x3B])
print("\n== Partition entry 0 ==")
print("  type GUID:", tguid.hex())
print("  is ESP:", tguid==ESP)
first_lba = u64(img,p+32); last_lba = u64(img,p+40)
print(f"  first LBA: {first_lba}  last LBA: {last_lba}  ({last_lba-first_lba+1} sectors)")
name = img[p+56:p+56+22].decode("utf-16le","replace")
print("  name:", repr(name))

# --- FAT BPB at partition start ---
f = first_lba*SS
print("\n== FAT BPB (partition start) ==")
print("  jump:", img[f:f+3].hex())
print("  OEM:", img[f+3:f+11])
bps = u16(img,f+11); spc = img[f+13]; rsvd = u16(img,f+14)
nfat = img[f+16]; rootent = u16(img,f+17); tot16 = u16(img,f+19)
media = img[f+21]; fatsz16 = u16(img,f+22); tot32 = u32(img,f+32)
print(f"  bytes/sector: {bps}")
print(f"  sectors/cluster: {spc}")
print(f"  reserved sectors: {rsvd}")
print(f"  num FATs: {nfat}")
print(f"  root entries: {rootent}")
print(f"  total sectors 16: {tot16}   32: {tot32}")
print(f"  media: {hex(media)}")
print(f"  FAT size sectors: {fatsz16}")
print(f"  boot sig 0xAA55:", hex(u16(img,f+510)))
fstype = img[f+54:f+62]
print("  fs type label:", fstype)
# compute FAT type per spec (by cluster count)
totsec = tot16 if tot16 else tot32
root_dir_sectors = (rootent*32 + bps-1)//bps
data_sec = totsec - (rsvd + nfat*fatsz16 + root_dir_sectors)
n_clusters = data_sec // spc
print(f"  computed: total={totsec} rootdirsec={root_dir_sectors} datasec={data_sec} clusters={n_clusters}")
if n_clusters < 4085: realtype="FAT12"
elif n_clusters < 65525: realtype="FAT16"
else: realtype="FAT32"
print(f"  >>> SPEC FAT TYPE by cluster count = {realtype}  (label says {fstype.strip()})")
if realtype!="FAT16":
    print(f"  *** MISMATCH: firmware determines FAT type by cluster count, not label. {realtype} != FAT16 label ***")

# --- Walk root dir for EFI/BOOT/BOOTX64.EFI ---
fat1 = f + rsvd*bps
root_off = f + (rsvd + nfat*fatsz16)*bps
data_off = f + (rsvd + nfat*fatsz16 + root_dir_sectors)*bps
def cluster_off(cl): return data_off + (cl-2)*spc*bps
def read_dir(off, count):
    ents=[]
    for i in range(count):
        e = img[off+i*32:off+i*32+32]
        if e[0]==0: break
        if e[0]==0xE5: continue
        attr=e[11]
        nm=e[0:11].decode("ascii","replace")
        cl=u16(e,26); sz=u32(e,28)
        ents.append((nm,attr,cl,sz))
    return ents
print("\n== Root directory ==")
root=read_dir(root_off, rootent)
for nm,attr,cl,sz in root: print(f"  {nm!r} attr={hex(attr)} cluster={cl} size={sz}")
def find(ents,name83):
    for nm,attr,cl,sz in ents:
        if nm==name83: return (nm,attr,cl,sz)
    return None
efi=find(root,"EFI        ")
if not efi: print("  *** EFI dir NOT FOUND in root ***"); sys.exit()
efidir=read_dir(cluster_off(efi[2]), spc*bps//32)
print("\n== EFI/ ==")
for e in efidir: print(f"  {e[0]!r} attr={hex(e[1])} cluster={e[2]} size={e[3]}")
boot=find(efidir,"BOOT       ")
if not boot: print("  *** BOOT dir NOT FOUND ***"); sys.exit()
bootdir=read_dir(cluster_off(boot[2]), spc*bps//32)
print("\n== EFI/BOOT/ ==")
for e in bootdir: print(f"  {e[0]!r} attr={hex(e[1])} cluster={e[2]} size={e[3]}")
b64=find(bootdir,"BOOTX64 EFI")
if not b64: print("  *** BOOTX64.EFI NOT FOUND ***"); sys.exit()
print(f"\n== BOOTX64.EFI == cluster={b64[2]} size={b64[3]}")
peoff=cluster_off(b64[2])
pe=img[peoff:peoff+b64[3]]
print("  MZ:", pe[0:2], hex(u16(pe,0)))
lfanew=u32(pe,60); print("  e_lfanew:", lfanew)
print("  PE sig:", pe[lfanew:lfanew+4])
mach=u16(pe,lfanew+4); print("  machine:", hex(mach), "(0x8664=amd64)")
nsec=u16(pe,lfanew+6); print("  sections:", nsec)
optmagic=u16(pe,lfanew+24); print("  opt magic:", hex(optmagic),"(0x20b=PE32+)")
subsys=u16(pe,lfanew+24+68); print("  subsystem:", subsys, "(10=EFI app)")
dllc=u16(pe,lfanew+24+70); print("  dllchars:", hex(dllc))
entry=u32(pe,lfanew+24+16); print("  entry RVA:", hex(entry))
imgbase=u64(pe,lfanew+24+24); print("  image base:", hex(imgbase))
sizeimg=u32(pe,lfanew+24+56); print("  size of image:", hex(sizeimg))
sizehdr=u32(pe,lfanew+24+60); print("  size of headers:", hex(sizehdr))
secalign=u32(pe,lfanew+24+32); filealign=u32(pe,lfanew+24+36)
print("  section align:", hex(secalign), " file align:", hex(filealign))
print("  sections:")
sectab=lfanew+24+ u16(pe,lfanew+20)
for s in range(nsec):
    so=sectab+s*40
    snm=pe[so:so+8]; vsz=u32(pe,so+8); vad=u32(pe,so+12); rsz=u32(pe,so+16); rof=u32(pe,so+20); ch=u32(pe,so+36)
    print(f"    {snm} vsz={hex(vsz)} vaddr={hex(vad)} rawsz={hex(rsz)} rawoff={hex(rof)} chars={hex(ch)}")
