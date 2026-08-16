# QEMU-side equivalent of build/plug-run.ps1: boot a transpiler-plug CDX,
# accept its outbound TCP connection (guest -> 127.0.0.1:<port> via slirp),
# send length-framed IR text, capture the emitted source until close.
import os
import socket
import sys
import time

import codex_vm

def run_plug(plug_cdx, ir_path, out_path, port=9145, mem_mb=3072, timeout=180,
             chunk_size=4096, pcap=None, legacy=False):
    ir = open(ir_path, "rb").read()
    # The guest loses the last byte of any odd-length frame (see
    # PLUG_IR_TRANSPORT.md). Frame parity follows TCP payload parity because
    # the headers are even, so keeping every write even keeps every frame
    # even. pcap lets the caller prove it afterwards rather than trust it.
    if pcap:
        os.environ["CODEX_PCAP"] = pcap
    else:
        os.environ.pop("CODEX_PCAP", None)
    listener = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    listener.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    # The plug dials a port compiled into its kernel, so this side cannot pick
    # a free one. A previous run winding down still owns it for a while; wait
    # for it rather than dying on EADDRINUSE mid-experiment.
    deadline = time.time() + 90
    while True:
        try:
            listener.bind(("127.0.0.1", port))
            break
        except OSError:
            if time.time() > deadline:
                raise
            print(f"port {port} still busy, waiting...")
            time.sleep(3)
    listener.listen(1)
    listener.settimeout(1.0)
    print(f"listening on {port}; IR {len(ir)} bytes")

    proc, data, ctrl = codex_vm.launch(plug_cdx, mem_mb, nic=True)
    try:
        conn = None
        deadline = time.time() + 90
        while time.time() < deadline:
            if proc.poll() is not None:
                raise RuntimeError("qemu exited: " + proc.stderr.read().decode()[-800:])
            try:
                conn, addr = listener.accept()
                break
            except socket.timeout:
                continue
        if conn is None:
            raise RuntimeError("plug never connected (slirp loopback issue?)")
        print(f"plug connected from {addr}")

        # Protocol from plug-run.ps1: 4-byte LE length of (payload+1), one
        # tag byte 0x01, then the IR bytes. Built as one flat message so
        # every write can be sized deliberately.
        #
        # The trailing pad makes the wire total even. A sum of even segments
        # is even, and the 4 length bytes plus 1 tag byte make the real total
        # odd, so without it one segment has to come out odd and lose a byte.
        # The plug reads exactly msg_len and never sees the pad.
        if legacy:
            # The pre-2026-08-15 sender, kept to reproduce the stalls and
            # crashes that were characterised with it. Do not use it to
            # produce artifacts: it lets the kernel choose frame parity.
            conn.sendall((len(ir) + 1).to_bytes(4, "little"))
            conn.sendall(b"\x01")
            off = 0
            while off < len(ir):
                n = min(chunk_size, len(ir) - off)
                conn.sendall(ir[off:off + n])
                off += n
                time.sleep(0.02)
            print(f"sent {len(ir)} bytes in {chunk_size}-byte writes (LEGACY)")
        else:
            msg = (len(ir) + 1).to_bytes(4, "little") + b"\x01" + ir
            if len(msg) % 2:
                msg += b"\x00"
            # Nagle would recoalesce the writes and hand segmentation back to
            # the kernel, which is the thing we are trying to stop deciding
            # parity.
            conn.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
            step = chunk_size - (chunk_size % 2)
            off = 0
            while off < len(msg):
                conn.sendall(msg[off:off + step])
                off += step
                time.sleep(0.02)
            print(f"sent {len(msg)} bytes ({len(ir)} of IR) in {step}-byte writes")

        # The plug closing the socket is the ONLY end of stream. A quiet gap
        # is not: a big IR leaves the guest computing for a long time between
        # writes, and treating silence as completion writes a truncated
        # source file that looks like a successful run.
        conn.settimeout(10)
        out = b""
        closed = False
        crashed = False
        serial = b""
        data.setblocking(False)
        t0 = time.time()
        while time.time() - t0 < timeout:
            # A crashed guest prints !EXC and halts, so waiting out the full
            # timeout learns nothing. Watch the serial console while we wait.
            try:
                serial += data.recv(65536)
            except (BlockingIOError, socket.timeout, OSError):
                pass
            if b"!EXC" in serial:
                crashed = True
                break
            try:
                chunk = conn.recv(65536)
            except socket.timeout:
                continue
            except OSError:
                break
            if not chunk:
                closed = True
                break
            out += chunk
        conn.close()
        data.setblocking(True)

        if crashed:
            line = next((l for l in serial.decode(errors="replace").splitlines()
                         if "!EXC" in l), "")
            print(f"FAIL: guest crashed -- {line[:160]}")
            print("      resolve the RIP against <plug>.cdx.map for a stack trace")
            return False

        # Serial console carries the plug's own status line ("OK defs=N").
        status = codex_vm.recv_all(data, idle_timeout=3, overall_timeout=10)
        for line in status.decode(errors="replace").splitlines():
            if line.strip() and not line.startswith(("WD:", "HEAP:", "STACK:")):
                print("  serial |", line)

        if not out:
            print("FAIL: no bytes from plug")
            return False
        if not closed:
            print(f"FAIL: plug never closed the socket ({len(out)} bytes in "
                  f"{timeout}s); output would be truncated, discarding it")
            return False
        open(out_path, "wb").write(out)
        print(f"wrote {out_path} ({len(out)} bytes)")
        return True
    finally:
        proc.kill()
        listener.close()

if __name__ == "__main__":
    ok = run_plug(sys.argv[1], sys.argv[2], sys.argv[3],
                  port=int(sys.argv[4]) if len(sys.argv) > 4 else 9145,
                  chunk_size=int(sys.argv[5]) if len(sys.argv) > 5 else 4096)
    sys.exit(0 if ok else 1)
