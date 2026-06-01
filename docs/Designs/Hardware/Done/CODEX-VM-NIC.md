# codex-vm NE2000 NIC Emulation

## Goal

Give the guest real TCP/IP networking over a virtual NE2000 ISA NIC,
bridging to the host's network via a user-mode NAT (no TAP driver, no
admin rights). The guest already has a full network stack
(Ethernet/IP/TCP in `codex/os/net/`) and NE2000 driver code in the boot
sequence and `net-send-raw`/`net-recv-raw` builtins.

## What the guest expects

QEMU provides: `-device ne2k_isa,netdev=net0,irq=9,iobase=0x300,mac=52:54:00:12:34:56`

### I/O ports (base 0x300)

| Offset | Port  | Name     | Write              | Read               |
|--------|-------|----------|--------------------|--------------------|
| 0x00   | 0x300 | CR       | Command register   | Command register   |
| 0x01   | 0x301 | PSTART   | Page start         | CLDA0              |
| 0x02   | 0x302 | PSTOP    | Page stop          | CLDA1              |
| 0x03   | 0x303 | BNRY     | Boundary pointer   | Boundary pointer   |
| 0x04   | 0x304 | TPSR     | TX page start      | TSR (TX status)    |
| 0x05   | 0x305 | TBCR0    | TX byte count low  | NCR                |
| 0x06   | 0x306 | TBCR1    | TX byte count high | FIFO               |
| 0x07   | 0x307 | ISR      | Interrupt status   | Interrupt status   |
| 0x08   | 0x308 | RSAR0    | Remote start low   | CRDA0              |
| 0x09   | 0x309 | RSAR1    | Remote start high  | CRDA1              |
| 0x0A   | 0x30A | RBCR0    | Remote count low   | —                  |
| 0x0B   | 0x30B | RBCR1    | Remote count high  | —                  |
| 0x0C   | 0x30C | RCR      | RX config          | RSR (RX status)    |
| 0x0D   | 0x30D | TCR      | TX config          | —                  |
| 0x0E   | 0x30E | DCR      | Data config        | —                  |
| 0x0F   | 0x30F | IMR      | Interrupt mask     | —                  |
| 0x10   | 0x310 | DATA     | Remote DMA data    | Remote DMA data    |
| 0x1F   | 0x31F | RESET    | Reset (write any)  | Reset (read any)   |

Page 1 (CR bits 7:6 = 01): offsets 0x01-0x06 are PAR0-5 (MAC), 0x07 is
CURR (current RX page), 0x08-0x0F are MAR0-7 (multicast filter).

### NIC init sequence (from `emit-nic-init`)

1. Read+write reset port (0x31F) to reset
2. Poll ISR bit 7 (RST) until set, with 1M iteration timeout
3. Write 0xFF to ISR to clear all
4. Phantom NIC check: if ISR reads back 0xFF, no NIC present
5. CR=0x21 (stop, page 0, no DMA)
6. DCR=0x49 (word mode, FIFO 8 bytes, normal)
7. Clear RBCR0/1 to 0
8. RCR=0x04 (accept broadcast)
9. TCR=0x02 (internal loopback for init)
10. PSTART=0x46, PSTOP=0x80, BNRY=0x46
11. Clear IMR
12. DMA read 32 bytes at address 0 (the PROM — 6-byte MAC doubled)
13. Read 6 words from DATA port → extract MAC bytes
14. Clear RDC in ISR
15. CR=0x61 (stop, page 1) → write CURR=PSTART via page 1 offset 7
16. CR=0x22 (start, page 0, no DMA)
17. TCR=0x00 (normal TX)
18. Store nic-present=1

### TX path (`net-send-raw`)

1. CR=0x22 (start, page 0)
2. Set RSAR to TX page address (0x40 << 8 = 0x4000)
3. Set RBCR to frame length
4. CR=0x12 (remote write DMA)
5. REP OUTSW to DATA port (bulk word-write)
6. Set TPSR=0x40, TBCR=length
7. CR=0x26 (start, TX)

### RX path (`net-recv-raw`)

1. CR=0x62 (start, page 1) → read CURR
2. CR=0x22 (start, page 0) → read BNRY
3. If BNRY == CURR: no data
4. Set RSAR to BNRY page, RBCR=4
5. CR=0x0A (remote read DMA) → read 4-byte NE2000 header
6. Header: status, next_page, length_lo, length_hi
7. Set RSAR/RBCR for full frame, CR=0x0A, REP INSW from DATA port
8. Update BNRY to next_page
9. Return frame bytes + length

### IRQ 9

The ISR in the common interrupt handler (vector 41 = 32+9) doesn't
currently handle IRQ 9. The guest uses polling for NIC RX. IRQ-driven
RX is a future enhancement.

## Implementation plan

### Phase 1: NE2000 register emulation (no network)

Add to `codex-vm.c`:

```c
typedef struct {
    unsigned char mem[32768];  /* 32KB internal SRAM (NE2000 has 16KB, but use 32 for safety) */
    unsigned char cr;          /* command register */
    unsigned char isr;         /* interrupt status */
    unsigned char imr;         /* interrupt mask */
    unsigned char dcr;         /* data config */
    unsigned char tcr, rcr;    /* TX/RX config */
    unsigned char tpsr;        /* TX page start */
    int tbcr;                  /* TX byte count */
    unsigned char pstart, pstop, bnry, curr; /* ring buffer pointers */
    int rsar;                  /* remote start address (16-bit) */
    int rbcr;                  /* remote byte count */
    unsigned char par[6];      /* MAC address */
    unsigned char mar[8];      /* multicast filter */
    int page;                  /* current register page (0, 1, or 2) */
} Ne2kState;
```

Handle ports 0x300-0x31F in `handle_io`:
- CR writes update `page` (bits 7:6), start/stop, DMA mode
- DATA port reads/writes transfer from/to `mem[]` at `rsar`, decrement `rbcr`
- ISR writes clear bits (write-1-to-clear)
- Page 1 reads/writes access PAR and CURR
- Reset port sets ISR bit 7

This is enough for `emit-nic-init` to succeed and `nic-present=1`.

### Phase 2: TX — guest-to-host frames

When the guest issues TX (CR bit 2):
1. Read the frame from `ne2k.mem[tpsr * 256]`, length = `tbcr`
2. Parse the Ethernet frame (dest MAC, src MAC, ethertype, payload)
3. If it's an ARP request for the gateway → reply with a fake gateway MAC
4. If it's an IP packet → extract the TCP/UDP payload
5. For TCP: maintain a host-side socket per guest TCP connection
   - SYN → `connect()` to the real destination
   - Data → `send()` on the host socket
   - FIN → `closesocket()`
6. Set ISR bit 1 (TX complete)

### Phase 3: RX — host-to-guest frames

Background thread polls host sockets for incoming data:
1. `recv()` on host sockets
2. Build an Ethernet frame (with fake MACs) containing the IP/TCP reply
3. Write frame into NE2000 ring buffer at `curr` page
4. Advance `curr`
5. Set ISR bit 0 (RX ready) and raise IRQ 9 if IMR allows

### Phase 4: User-mode NAT

Implement minimal user-mode NAT (like QEMU's SLIRP but simpler):
- Gateway IP: 10.0.2.2 (responds to ARP, acts as default gateway)
- Guest IP: 10.0.2.15 (or DHCP)
- DNS: forward to host's DNS resolver
- TCP: 1:1 NAT — each guest TCP connection maps to a host socket
- UDP: same pattern for DNS queries
- ICMP: optional (ping), can stub initially

### Complexity estimate

| Phase | Lines of C | Effort |
|-------|-----------|--------|
| 1: Registers | ~200 | Small — mechanical port handling |
| 2: TX | ~300 | Medium — frame parsing, socket management |
| 3: RX | ~250 | Medium — background thread, ring buffer writes |
| 4: NAT | ~400 | Medium — ARP replies, IP routing, connection table |
| **Total** | **~1150** | |

### What we skip

- Multicast filtering (accept all or none)
- Error counters (NCR, CRC errors)
- Page 2 registers (read-only config mirror)
- DHCP server (guest can use static IP 10.0.2.15)
- UDP beyond DNS
- ICMP

### Dependencies

- `codex/os/net/` already has: Ethernet framing, IP, ARP, TCP, TLS
- Guest uses `net-send-raw` (pointer + length → NE2000 TX) and
  `net-recv-raw` (length → NE2000 RX → pointer)
- No changes needed to the compiler or guest OS code

### Testing

1. Phase 1: boot guest, verify `nic-present=1` in guest memory
2. Phase 2: guest sends ARP → VM replies; guest sends TCP SYN to
   a real host → VM forwards, gets SYN-ACK
3. Phase 3: guest receives SYN-ACK, completes handshake
4. End-to-end: guest HTTP GET to a known URL, verify response
