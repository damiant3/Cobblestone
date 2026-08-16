# Read a filter-dump pcap of a plug transfer and report, for every
# host->guest TCP segment, its payload length and the IR offset of its
# final byte. That turns "the corrupt byte was at offset N" into "the
# corrupt byte was the last byte of a segment of length L" without any
# guessing about how send() maps to frames.
#
# Also simulates the NE2000 receive ring (PSTART=0x46, PSTOP=0x80, 256-byte
# pages) so a corrupt offset can be checked against page and wrap geometry.
import struct
import sys

PSTART, PSTOP, PAGE = 0x46, 0x80, 256

def segments(path, guest_port=9145):
    """host->guest TCP segments in capture order: (offset, length, eth_len)."""
    d = open(path, "rb").read()
    magic = struct.unpack("<I", d[:4])[0]
    endian = "<" if magic in (0xa1b2c3d4, 0xa1b23c4d) else ">"
    nano = magic in (0xa1b23c4d, 0x4d3cb2a1)
    off, out = 24, []
    while off + 16 <= len(d):
        _, _, incl, _ = struct.unpack(endian + "IIII", d[off:off + 16])
        off += 16
        pkt, off = d[off:off + incl], off + incl
        if len(pkt) < 34 or struct.unpack(">H", pkt[12:14])[0] != 0x0800:
            continue
        ihl = (pkt[14] & 0x0F) * 4
        if pkt[23] != 6:                      # not TCP
            continue
        ip_total = struct.unpack(">H", pkt[16:18])[0]
        t = 14 + ihl
        sport = struct.unpack(">H", pkt[t:t + 2])[0]
        doff = (pkt[t + 12] >> 4) * 4
        plen = ip_total - ihl - doff
        if sport != guest_port or plen <= 0:  # only host->guest with data
            continue
        seq = struct.unpack(">I", pkt[t + 4:t + 8])[0]
        # eth_len is what the NE2000 stores: the frame as it arrived.
        out.append((seq, plen, 14 + ip_total))
    if not out:
        return []
    base = out[0][0]
    return [((s - base) & 0xFFFFFFFF, p, e) for s, p, e in out]

def odd_bodies(path, guest_port=9145):
    """host->guest frames the guest will truncate: odd NE2000 body length.

    The body the guest DMAs is the stored frame, which QEMU pads to a
    60-byte minimum and no further. `shr rcx, 1` rounds the word count down,
    so an odd body loses its final byte. An empty list is a proof that this
    defect did not fire on this transfer -- it says nothing about the stalls
    or the guest crashes, which are separate.
    """
    return [(seq, plen, eth) for seq, plen, eth in segments(path, guest_port)
            if max(eth, 60) % 2]

def ring(segs):
    """Walk the NE2000 ring the way the device fills it."""
    page, rows = PSTART, []
    for seq, plen, eth in segs:
        stored = max(eth, 60) + 4          # 4-byte NE2000 header, 60B minimum
        pages = (stored + PAGE - 1) // PAGE
        start = page
        wrapped = start + pages > PSTOP
        page = PSTART + (start + pages - PSTOP) if wrapped else start + pages
        rows.append((seq, plen, eth, stored, start, pages, wrapped))
    return rows

def main(path, marks):
    segs = segments(path)
    if not segs:
        print("no host->guest TCP payload frames in", path)
        return
    total = sum(p for _, p, _ in segs)
    print(f"{path}: {len(segs)} data segments, {total} payload bytes")
    lens = {}
    for _, p, _ in segs:
        lens[p] = lens.get(p, 0) + 1
    print("segment payload lengths:",
          ", ".join(f"{l}x{n}" for l, n in sorted(lens.items())))
    odd = [p for _, p, _ in segs if p % 2]
    print(f"odd-length payloads: {len(odd)}   "
          f"odd stored frames: {sum(1 for _,_,e in segs if max(e,60) % 2)}")

    rows = ring(segs)
    print(f"ring wraps: {sum(1 for r in rows if r[6])}")
    ends = {}
    for (seq, plen, eth, stored, start, pages, wrapped) in rows:
        ends[seq + plen - 1] = (plen, eth, stored, start, pages, wrapped)

    for m in marks:
        print(f"\n-- offset {m}")
        if m in ends:
            plen, eth, stored, start, pages, wrapped = ends[m]
            print(f"   IS the last byte of a segment: payload={plen} "
                  f"eth={eth} stored={stored} page={hex(start)} "
                  f"pages={pages} wrapped={wrapped}")
            print(f"   payload parity={'odd' if plen % 2 else 'even'}  "
                  f"stored parity={'odd' if stored % 2 else 'even'}  "
                  f"stored%256={stored % 256}")
        else:
            for (seq, plen, eth, stored, start, pages, wrapped) in rows:
                if seq <= m < seq + plen:
                    print(f"   inside a segment: seq={seq} payload={plen} "
                          f"pos_in_seg={m - seq} (last={plen - 1}) "
                          f"page={hex(start)} wrapped={wrapped}")
                    break
            else:
                print("   not covered by any captured segment")

if __name__ == "__main__":
    main(sys.argv[1], [int(a) for a in sys.argv[2:]])
