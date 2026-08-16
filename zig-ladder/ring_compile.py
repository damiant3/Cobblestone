# Compile via codex-vm's ring-preload contract under QEMU: the input blob
# (mode line + unit + EOT) is placed at guest phys 0x500000 by the generic
# loader pre-boot, and the write-pos cell (zeroed by the boot stub) is
# re-injected post-READY through the gdbstub — the same re-injection
# codex-vm does on the guest's first LSR read. No streamed serial input,
# so the QEMU 6.2 chardev stall has nothing to race against.
# Serial stays for output only (READY, logs, SIZE:<n>, binary).
#
# Blobs larger than the 1 MB ring stream through it: both ring positions
# are unbounded counters masked at access (X86_64Boot.codex, cells 28704/
# 28712), so the host refills from behind the guest's read cursor and the
# ring becomes a window. ring_refill_test.sh is the oracle for that path.
import os
import socket
import subprocess
import sys
import time

REPO = str(__import__("pathlib").Path(__file__).resolve().parent.parent)
RING_ADDR = 0x500000
RING_SIZE = 0x100000          # 1 MB, must match seed's serial-ring-buf-size
WPOS_ADDR = 28704             # 0x7020
RPOS_ADDR = 28712             # 0x7028

def free_port():
    s = socket.socket()
    s.bind(("127.0.0.1", 0))
    p = s.getsockname()[1]
    s.close()
    return p

class Gdb:
    def __init__(self, port):
        self.s = socket.create_connection(("127.0.0.1", port), timeout=10)
        self.s.settimeout(5)
        # QEMU halts the VM on connect and may emit a spontaneous stop
        # packet; drain and ack anything queued before the first command.
        self.s.settimeout(0.8)
        try:
            while True:
                junk = self.s.recv(4096)
                if not junk:
                    break
                if b"$" in junk:
                    self.s.sendall(b"+")
        except (TimeoutError, OSError):
            pass
        self.s.settimeout(5)

    def _cks(self, payload):
        return sum(payload) % 256

    def cmd(self, payload):
        pkt = b"$" + payload + b"#" + b"%02x" % self._cks(payload)
        self.s.sendall(pkt)
        buf = b""
        while True:
            c = self.s.recv(4096)
            if not c:
                raise RuntimeError("gdb closed")
            buf += c
            if b"#" in buf[1:]:
                start = buf.index(b"$")
                end = buf.index(b"#", start)
                self.s.sendall(b"+")
                return buf[start + 1:end]

    def write_mem(self, addr, data):
        payload = b"M%x,%x:" % (addr, len(data)) + data.hex().encode()
        r = self.cmd(payload)
        if r != b"OK":
            raise RuntimeError(f"gdb write @{addr:#x} failed: {r!r}")

    def read_mem(self, addr, n):
        r = self.cmd(b"m%x,%x" % (addr, n))
        if r.startswith(b"E") or len(r) != n * 2:
            raise RuntimeError(f"gdb read @{addr:#x} unexpected reply: {r!r}")
        return bytes.fromhex(r.decode())

    def cont_nowait(self):
        # 'c' replies only at the next stop, so consume just the '+' ack.
        payload = b"c"
        pkt = b"$" + payload + b"#" + b"%02x" % self._cks(payload)
        self.s.sendall(pkt)
        self.s.recv(1)

    def interrupt(self):
        # Raw 0x03 stops a running target; QEMU answers with a stop packet.
        self.s.sendall(b"\x03")
        buf = b""
        while True:
            c = self.s.recv(4096)
            if not c:
                raise RuntimeError("gdb closed during interrupt")
            buf += c
            if b"#" in buf[1:]:
                self.s.sendall(b"+")
                return

    def detach(self):
        try:
            self.cmd(b"D")
        except Exception:
            pass
        self.s.close()

def compile_ring(blob_path, out_path, mem_mb=3072, timeout=1800, seed=None):
    blob = open(blob_path, "rb").read()
    # Both ring positions are unbounded counters masked at access on both
    # the ISR write side and the __bare_metal_read_serial read side, so
    # 1 MB is a WINDOW, not a ceiling: a blob larger than the ring stages
    # its first megabyte through the loader and the rest is refilled from
    # behind the guest's read cursor (see the refill loop below). The
    # single-shot path for blobs that fit is unchanged.
    staged = min(len(blob), RING_SIZE)
    stage_path = blob_path
    if len(blob) > RING_SIZE:
        stage_path = blob_path + ".stage1"
        with open(stage_path, "wb") as f:
            f.write(blob[:RING_SIZE])
    dp, cp, gp = free_port(), free_port(), free_port()
    proc = subprocess.Popen([
        "qemu-system-x86_64", "-accel", "tcg",
        "-machine", "kernel-irqchip=off", "-cpu", "max",
        "-kernel", seed or f"{REPO}/seed/Codex.cdx",
        "-device", f"loader,file={stage_path},addr={hex(RING_ADDR)},force-raw=on",
        "-chardev", f"socket,id=ch0,host=127.0.0.1,port={dp},server=on,wait=on,nodelay=on",
        "-chardev", f"socket,id=ch1,host=127.0.0.1,port={cp},server=on,wait=on,nodelay=on",
        "-serial", "chardev:ch0", "-serial", "chardev:ch1",
        "-device", "isa-debug-exit,iobase=0xf4,iosize=0x04",
        "-device", f"loader,addr=0xfe8,data={hex(mem_mb << 20)},data-len=4",
        "-gdb", f"tcp:127.0.0.1:{gp}",
        "-display", "none", "-no-reboot", "-m", str(mem_mb),
    ], stdout=subprocess.DEVNULL, stderr=subprocess.PIPE)
    try:
        def connect(port):
            for _ in range(120):
                if proc.poll() is not None:
                    raise RuntimeError("qemu died: " + proc.stderr.read().decode()[-400:])
                try:
                    return socket.create_connection(("127.0.0.1", port), timeout=1)
                except OSError:
                    time.sleep(0.25)
            raise RuntimeError(f"no connect {port}")
        data, ctrl = connect(dp), connect(cp)
        ctrl.settimeout(120)
        t0 = time.time()
        buf = b""
        while b"READY\n" not in buf:
            buf += ctrl.recv(4096)
        print(f"READY at {time.time()-t0:.1f}s; injecting wpos={staged} via gdbstub", flush=True)

        gdb = Gdb(gp)
        gdb.cmd(b"?")                       # attach/halt handshake
        ring_head = gdb.read_mem(RING_ADDR, 8)
        if ring_head != blob[:8]:
            raise RuntimeError(f"ring preload mismatch: {ring_head!r} vs {blob[:8]!r}")
        gdb.write_mem(WPOS_ADDR, staged.to_bytes(8, "little"))
        gdb.write_mem(RPOS_ADDR, (0).to_bytes(8, "little"))
        back = gdb.read_mem(WPOS_ADDR, 8)
        print(f"wpos cell now {int.from_bytes(back, 'little')}; ring head verified", flush=True)

        if staged < len(blob):
            # Refill from behind the read cursor. The VM is stopped for
            # every cell access: the gdbstub writes memory byte-wise, so a
            # wpos update racing the guest's own load could tear and send
            # the reader past real data.
            #
            # The loop runs until the guest has CONSUMED everything, not
            # merely until everything is delivered. A reader that finds
            # the ring dry mid-stream parks in hlt, and nothing on this
            # path ever wakes it -- streamed serial wakes it through UART
            # interrupts, and the preload path has no serial input by
            # design. Each round's stop/resume doubles as that missing
            # wake, so detaching before rpos catches wpos would leave the
            # guest asleep beside a full ring.
            wpos = staged
            stalled = 0
            last_rpos = -1
            t_fill = time.time()
            gdb.cont_nowait()
            while True:
                time.sleep(0.15)
                gdb.interrupt()
                rpos = int.from_bytes(gdb.read_mem(RPOS_ADDR, 8), "little")
                if rpos >= len(blob):
                    break
                room = RING_SIZE - (wpos - rpos)
                if room > 0 and wpos < len(blob):
                    chunk = blob[wpos:wpos + room]
                    off = 0
                    # 1 KB per M packet: hex doubles the payload and QEMU's
                    # gdbstub buffer is 4096 bytes, packet framing included.
                    while off < len(chunk):
                        piece = chunk[off:off + 1024]
                        pos = (wpos + off) & (RING_SIZE - 1)
                        head = min(len(piece), RING_SIZE - pos)
                        gdb.write_mem(RING_ADDR + pos, piece[:head])
                        if head < len(piece):
                            gdb.write_mem(RING_ADDR, piece[head:])
                        off += len(piece)
                    wpos += len(chunk)
                    gdb.write_mem(WPOS_ADDR, wpos.to_bytes(8, "little"))
                    print(f"  refill: wpos {wpos}/{len(blob)} rpos {rpos}", flush=True)
                stalled = 0 if rpos != last_rpos else stalled + 1
                last_rpos = rpos
                if stalled > 400:
                    raise RuntimeError(f"guest stopped consuming at rpos {rpos} of {len(blob)}")
                gdb.cont_nowait()
            print(f"ring refill consumed: {len(blob)} bytes in {time.time()-t_fill:.1f}s", flush=True)
        print("detaching", flush=True)
        gdb.detach()

        # From here: output-only serial, same SIZE-aware read as compile_blob.
        data.settimeout(5)
        out = b""
        deadline = time.time() + timeout
        needed = None
        t1 = time.time()
        while time.time() < deadline:
            try:
                chunk = data.recv(65536)
            except socket.timeout:
                if needed is not None and len(out) >= needed:
                    break
                continue
            except OSError:
                break
            if not chunk:
                break
            out += chunk
            if needed is None:
                idx0 = out.find(b"SIZE:")
                if idx0 >= 0 and b"\n" in out[idx0:]:
                    nl0 = out.index(b"\n", idx0)
                    needed = nl0 + 1 + int(out[idx0 + 5:nl0].split()[0])
                elif b"CODEGEN-HALTED" in out or b"CODEGEN-ERRORS" in out:
                    needed = len(out)
            if needed is not None and len(out) >= needed:
                data.settimeout(2)
        print(f"stream: {len(out)} bytes in {time.time()-t1:.0f}s", flush=True)
        idx = out.find(b"SIZE:")
        header = out[:idx if idx >= 0 else len(out)].decode(errors="replace")
        shown = 0
        for line in header.splitlines():
            if line.strip() and not line.startswith("WD:"):
                print("  |", line, flush=True)
                shown += 1
                if shown > 12:
                    print("  | ...", flush=True)
                    break
        if idx < 0:
            print("NO SIZE MARKER — compile failed or errored", flush=True)
            return False
        nl = out.index(b"\n", idx)
        size = int(out[idx + 5:nl].split()[0])
        binary = out[nl + 1:nl + 1 + size]
        print(f"  SIZE: {size}, got {len(binary)}", flush=True)
        if len(binary) != size:
            print("SHORT BINARY", flush=True)
            return False
        open(out_path, "wb").write(binary)
        print(f"wrote {out_path}", flush=True)
        trailer = out[nl + 1 + size:]
        if trailer.strip():
            open(out_path + ".map", "wb").write(trailer)
            print(f"wrote {out_path}.map ({len(trailer)} bytes)", flush=True)
        return True
    finally:
        proc.kill()
        proc.wait()

if __name__ == "__main__":
    ok = compile_ring(sys.argv[1], sys.argv[2])
    sys.exit(0 if ok else 1)
