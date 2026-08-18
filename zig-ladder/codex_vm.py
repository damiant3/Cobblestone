# Minimal Linux driver for the Codex bare-metal compiler under QEMU.
# compile: boot the seed, wait READY (ctrl serial), stream the unit blob
#          (data serial), parse log lines / SIZE:<n> / binary payload.
# run:     boot a compiled CDX, capture data serial until exit/quiet.
import pathlib
import socket
import subprocess
import sys
import time

from ladder_root import CODEX

REPO = str(CODEX)
ACCEL = __import__("os").environ.get("CODEX_ACCEL", "tcg")
BASE_PORT = 56400

def _free_port():
    s = socket.socket()
    s.bind(("127.0.0.1", 0))
    p = s.getsockname()[1]
    s.close()
    return p

def launch(kernel, mem_mb=3072, nic=False):
    # nic: only for plug kernels that drive the NE2K (the seed has no NIC
    # driver). Ports are dynamic: fixed ports collide with leftover VMs
    # and TIME_WAIT across rapid relaunches — bit us three times on
    # 2026-08-14.
    data_port, ctrl_port = _free_port(), _free_port()
    ram_bytes = mem_mb * 1024 * 1024
    # kernel-irqchip=off is what the author's WHPX fallback needs; under KVM
    # the userspace APIC path is deprecated and the guest dies pre-READY.
    # The in-kernel irqchip boots clean, so only disable it off-KVM.
    machine = ["-machine", "kernel-irqchip=off"] if ACCEL != "kvm" else []
    proc = subprocess.Popen([
        "qemu-system-x86_64", "-accel", ACCEL, *machine, "-kernel", kernel,
        "-chardev", f"socket,id=ch0,host=127.0.0.1,port={data_port},server=on,wait=on,nodelay=on",
        "-chardev", f"socket,id=ch1,host=127.0.0.1,port={ctrl_port},server=on,wait=on,nodelay=on",
        "-serial", "chardev:ch0", "-serial", "chardev:ch1",
        "-device", "isa-debug-exit,iobase=0xf4,iosize=0x04",
        *(["-netdev", "user,id=net0",
           "-device", "ne2k_isa,netdev=net0,irq=9,iobase=0x300,mac=52:54:00:12:34:56"]
          if nic else []),
        *(["-object", f"filter-dump,id=fd0,netdev=net0,file={__import__('os').environ['CODEX_PCAP']}"]
          if nic and __import__('os').environ.get("CODEX_PCAP") else []),
        # codex-vm writes the guest RAM size at phys 0xFE8 pre-boot ("dynamic
        # RSP"); QEMU does not, and the boot stub then sets RSP from 0 and
        # triple-faults. Seed the cell the same way. (QEMU 6.2's generic
        # loader asserts data_len < 8, so write the low 4 bytes; RAM sizes
        # here stay under 4GB.)
        "-device", f"loader,addr=0xfe8,data={hex(ram_bytes)},data-len=4",
        "-cpu", "max",
        "-display", "none", "-no-reboot", "-m", str(mem_mb),
    ], stdout=subprocess.DEVNULL, stderr=subprocess.PIPE)

    def connect(port):
        for _ in range(120):
            if proc.poll() is not None:
                raise RuntimeError("qemu exited: " + proc.stderr.read().decode()[-800:])
            try:
                return socket.create_connection(("127.0.0.1", port), timeout=1)
            except OSError:
                time.sleep(0.25)
        raise RuntimeError(f"no connection on {port}")

    data, ctrl = connect(data_port), connect(ctrl_port)
    return proc, data, ctrl

def wait_ready(ctrl, timeout=180):
    ctrl.settimeout(timeout)
    buf = b""
    while b"READY\n" not in buf:
        chunk = ctrl.recv(4096)
        if not chunk:
            raise RuntimeError(f"ctrl closed pre-READY: {buf!r}")
        buf += chunk
    return buf

def recv_all(sock, idle_timeout, overall_timeout):
    sock.settimeout(idle_timeout)
    buf = b""
    last_byte_at = [time.time()]
    deadline = time.time() + overall_timeout
    while time.time() < deadline:
        try:
            chunk = sock.recv(65536)
        except socket.timeout:
            break
        except OSError:
            break
        if not chunk:
            break
        buf += chunk
        last_byte_at[0] = time.time()
    recv_all.last_gap = time.time() - last_byte_at[0]
    return buf

def compile_blob(blob_path, out_cdx, mem_mb=3072, timeout=1800):
    proc, data, ctrl = launch(f"{REPO}/seed/Codex.cdx", mem_mb)
    try:
        t0 = time.time()
        pre = wait_ready(ctrl, 300)
        print(f"READY in {time.time()-t0:.0f}s  ({pre.decode(errors='replace').strip().replace(chr(10), ' | ')})")
        blob = open(blob_path, "rb").read()
        # Paced send (same as the vm-config fallback): a single large burst
        # intermittently loses bytes into the guest, and a lost EOT hangs
        # both sides.
        sent = 0
        while sent < len(blob):
            n = min(8192, len(blob) - sent)
            data.sendall(blob[sent:sent + n])
            sent += n
            time.sleep(0.005)
        t1 = time.time()
        # Read until the SIZE:<n> payload is fully here (plus a short trailer
        # drain), rather than waiting out an idle timeout: the guest stays
        # running after a compile, so idle-based reads always pay full price.
        data.settimeout(5)
        out = b""
        deadline = time.time() + timeout
        needed = None
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
                    needed = nl0 + 1 + int(out[idx0+5:nl0].split()[0])
                elif b"CODEGEN-HALTED" in out or b"CODEGEN-ERRORS" in out:
                    needed = len(out)
            if needed is not None and len(out) >= needed:
                data.settimeout(2)  # brief trailer drain (HEAP:/STACK: lines)
        print(f"compile stream: {len(out)} bytes in {time.time()-t1:.0f}s")
        # Parse: lines until SIZE:<n>, then n binary bytes, then trailer lines.
        idx = out.find(b"SIZE:")
        header = out[:idx].decode(errors="replace") if idx >= 0 else out.decode(errors="replace")
        for line in header.splitlines():
            if line.strip() and not line.startswith(("WD:",)):
                print("  |", line)
        if idx < 0:
            print("NO SIZE MARKER -- compile failed")
            return False
        nl = out.index(b"\n", idx)
        size = int(out[idx+5:nl].split()[0])
        binary = out[nl+1:nl+1+size]
        trailer = out[nl+1+size:].decode(errors="replace")
        print(f"  SIZE: {size}, got {len(binary)} binary bytes")
        for line in trailer.splitlines():
            if line.startswith(("HEAP:", "STACK:", "WD:")):
                print("  |", line)
        if len(binary) != size:
            print("SHORT BINARY READ")
            return False
        with open(f"/proc/{proc.pid}/status") as f:
            for line in f:
                if line.startswith("VmHWM"):
                    print("  qemu peak RSS:", line.split(":")[1].strip())
        open(out_cdx, "wb").write(binary)
        print(f"wrote {out_cdx}")
        return True
    finally:
        proc.kill()

def run_cdx(kernel, mem_mb=1024, timeout=300, idle_timeout=60):
    # idle_timeout is silence tolerance, not total time: a program that
    # computes for minutes before its first print needs it raised.
    proc, data, ctrl = launch(kernel, mem_mb)
    try:
        t0 = time.time()
        out = recv_all(data, idle_timeout=idle_timeout, overall_timeout=timeout)
        print(f"program output ({time.time()-t0:.0f}s):")
        for line in out.decode(errors="replace").splitlines():
            if not line.startswith(("WD:", "HEAP:", "STACK:")):
                print("  >", line)
        return out
    finally:
        proc.kill()

if __name__ == "__main__":
    cmd = sys.argv[1]
    if cmd == "compile":
        ok = compile_blob(sys.argv[2], sys.argv[3])
        sys.exit(0 if ok else 1)
    elif cmd == "run":
        run_cdx(sys.argv[2])
