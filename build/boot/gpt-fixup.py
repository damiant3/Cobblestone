# Generate GPT fixup blobs so a small GPT image becomes spec-correct on a
# larger physical disk. The image carries its backup GPT header at the
# IMAGE's last sector; the spec says the backup header lives at the DISK's
# last sector, and some firmware (Dell) validates that and silently drops
# the disk from the boot list. This produces sector blobs the flasher
# writes after the image:
#   blob-0.bin            patched protective MBR (0xEE partition spans the disk)
#   blob-1.bin            patched primary header (AlternateLBA/LastUsable -> disk end)
#   blob-<last-33>.bin    backup partition entry array (copy of primary's 32 sectors)
#   blob-<last>.bin       backup header at the disk's true last sector
#
# Usage: python gpt-fixup.py <image> <writable-sectors> <outdir> [reported-sectors]
#
# writable-sectors: the highest sector count that reads back after a write --
# the GPT backup lands just below this. Old U3-era sticks reserve a hidden
# tail (CD-ROM image area) inside their REPORTED capacity: writes there are
# silently dropped, so a backup header at the reported end is a pretend one.
# The primary header's AlternateLBA points at wherever we actually put it;
# firmware follows the pointer. reported-sectors (default: writable) sizes
# the protective MBR, which firmware compares against READ CAPACITY.
import struct, sys, zlib, os

img_path, total_sectors, outdir = sys.argv[1], int(sys.argv[2]), sys.argv[3]
reported_sectors = int(sys.argv[4]) if len(sys.argv) > 4 else total_sectors
os.makedirs(outdir, exist_ok=True)
img = bytearray(open(img_path, 'rb').read())

SEC = 512
last = total_sectors - 1

mbr = bytearray(img[0:SEC])
assert mbr[510] == 0x55 and mbr[511] == 0xAA, 'no MBR boot sig'
assert mbr[446 + 4] == 0xEE, 'partition 0 is not GPT protective'
prot_size = min(0xFFFFFFFF, reported_sectors - 1)
struct.pack_into('<I', mbr, 446 + 12, prot_size)

hdr = bytearray(img[SEC:2 * SEC])
assert hdr[0:8] == b'EFI PART', 'no GPT header at LBA 1'
hdr_size = struct.unpack_from('<I', hdr, 12)[0]
ent_lba = struct.unpack_from('<Q', hdr, 72)[0]
ent_count = struct.unpack_from('<I', hdr, 80)[0]
ent_size = struct.unpack_from('<I', hdr, 84)[0]
arr_bytes = ent_count * ent_size
arr_sectors = max(32, (arr_bytes + SEC - 1) // SEC)
arr = bytes(img[ent_lba * SEC: (ent_lba + arr_sectors) * SEC])

backup_arr_lba = last - arr_sectors
last_usable = backup_arr_lba - 1

def with_crc(h):
    h = bytearray(h)
    struct.pack_into('<I', h, 16, 0)
    struct.pack_into('<I', h, 16, zlib.crc32(bytes(h[0:hdr_size])) & 0xFFFFFFFF)
    return h

primary = bytearray(hdr)
struct.pack_into('<Q', primary, 32, last)          # AlternateLBA
struct.pack_into('<Q', primary, 48, last_usable)   # LastUsableLBA
primary = with_crc(primary)

backup = bytearray(primary)
struct.pack_into('<Q', backup, 24, last)           # MyLBA
struct.pack_into('<Q', backup, 32, 1)              # AlternateLBA
struct.pack_into('<Q', backup, 72, backup_arr_lba) # PartitionEntryLBA
backup = with_crc(backup)

def emit(lba, data):
    with open(os.path.join(outdir, f'blob-{lba}.bin'), 'wb') as f:
        f.write(data)

emit(0, bytes(mbr))
emit(1, bytes(primary))
emit(backup_arr_lba, arr)
emit(last, bytes(backup))
print(f'gpt-fixup: disk={total_sectors} sectors, backup header @ {last}, '
      f'entries @ {backup_arr_lba} ({arr_sectors} sectors), last usable {last_usable}')
