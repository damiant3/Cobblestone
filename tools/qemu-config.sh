#!/bin/bash
# qemu-config.sh — selects QEMU binary, accelerator, and chardev based on
# QEMU_ACCEL env. Sourced by pingpong-self.sh, sample-compile-selfhost.sh,
# and run-for-sweep.sh.
#
# Two-channel serial: COM1 (ch0, FD 3) carries data — source bytes inbound,
# compiled bytes outbound. COM2 (ch1, FD 4) is reserved for control —
# the future SUT writes its READY greeting on COM2 so the data stream
# stays clean. The current depot seed is single-chardev (READY on COM1):
# tools open both sockets and accept READY on either FD 3 or FD 4, which
# keeps existing seeds working while a future seed activates the COM2 path.
#
# Defaults to WHPX (Windows-native QEMU + Windows Hypervisor Platform)
# because each QEMU instance is its own Hyper-V partition with no shared
# accelerator state — concurrent jobs scale cleanly. KVM-in-WSL2 funnels
# every guest through one /dev/kvm, and IRQ delivery / KVM_RUN scheduling
# under load are non-deterministic enough to flake sweep --jobs=N
# (see CL 398's diagnosis). Set QEMU_ACCEL=kvm to opt back into Linux QEMU
# inside WSL — fine for single-job runs, not recommended for concurrent.
#
# Honored env vars:
#   QEMU_ACCEL          kvm | whpx                    (default: whpx)
#   QEMU_BIN_KVM        path to Linux QEMU            (default: /usr/bin/qemu-system-x86_64)
#   QEMU_BIN_WHPX       path to Windows QEMU          (default: /mnt/c/Program Files/qemu/qemu-system-x86_64.exe
#                                                      from WSL, or /c/Program Files/qemu/qemu-system-x86_64.exe
#                                                      from Git Bash on Windows)
#
# Provides:
#   QEMU                  binary to invoke
#   QEMU_ACCEL_FLAGS      array, e.g. ("-enable-kvm") or ("-accel" "whpx")
#   qemu_path <path>      echoes a kernel/path the chosen QEMU can open
#                         (wslpath -m for WHPX from WSL; identity otherwise)
#   qemu_chardev <port>       echoes the -chardev arg for the data socket (ch0)
#   qemu_chardev_ctrl <port>  echoes the -chardev arg for the control socket (ch1)
#   qemu_alloc_port           PID-deterministic port in 50200-57799; pair the
#                             control port as ctrl_port=$((data_port + 1))
#   qemu_wait_listen <data_port> [<ctrl_port>]
#                             waits for QEMU to bind, opens FD 3 (data) and
#                             — if ctrl_port given — FD 4 (control)
#   qemu_read_ready           after qemu_wait_listen, reads READY from FD 3
#                             (single-chardev seed) or FD 4 (dual-chardev seed)
#                             whichever fires first within the timeout
#
# WSL networking note: when QEMU_ACCEL=whpx and the harness runs from WSL,
# WSL's localhost must mirror Windows's so the harness can reach the
# Windows-side QEMU socket. Add to %USERPROFILE%\.wslconfig:
#     [wsl2]
#     networkingMode=mirrored
# then `wsl --shutdown` to apply. KVM-only users don't need this.

QEMU_ACCEL=${QEMU_ACCEL:-whpx}

if [ "$QEMU_ACCEL" = "whpx" ]; then
    if [ -z "${QEMU_BIN_WHPX:-}" ]; then
        if [ -x "/mnt/c/Program Files/qemu/qemu-system-x86_64.exe" ]; then
            QEMU_BIN_WHPX="/mnt/c/Program Files/qemu/qemu-system-x86_64.exe"
        elif [ -x "/c/Program Files/qemu/qemu-system-x86_64.exe" ]; then
            QEMU_BIN_WHPX="/c/Program Files/qemu/qemu-system-x86_64.exe"
        else
            echo "FAIL: QEMU_ACCEL=whpx but Windows qemu-system-x86_64.exe not found." >&2
            echo "  Install via: winget install --id SoftwareFreedomConservancy.QEMU" >&2
            exit 1
        fi
    fi
    QEMU="$QEMU_BIN_WHPX"
    QEMU_ACCEL_FLAGS=(-accel whpx)
    qemu_path() {
        if command -v wslpath >/dev/null 2>&1; then
            wslpath -m "$1"
        else
            echo "$1"
        fi
    }
else
    QEMU="${QEMU_BIN_KVM:-/usr/bin/qemu-system-x86_64}"
    QEMU_ACCEL_FLAGS=(-enable-kvm)
    qemu_path() { echo "$1"; }
fi

qemu_alloc_port() {
    # Random EVEN port in the Windows-safe range 50200-64998.
    # Stride-2: caller pairs with ctrl_port = data_port + 1, so both
    # slots in the pair are guaranteed unique for one allocation.
    #
    # Optional first arg is an attempt number; each attempt draws a
    # fresh random offset (RANDOM is re-seeded per-subshell).
    #
    # Range avoids Hyper-V/WHPX silent reservation 45000-49797 and
    # `netsh excludedportrange` slots 50000-50059, 55095-55194,
    # 57888-57987.
    local attempt=${1:-0}
    local range=7400
    local offset=$(( (RANDOM + attempt * 991) % range ))
    echo $(( 50200 + offset * 2 ))
}

qemu_chardev() {
    # wait=on: QEMU blocks until the harness connects, then boots the guest.
    # Required so the guest's first byte lands after the harness has
    # opened the FD; otherwise the host-side bytes can be dropped or
    # buffer-discarded and the harness sees no greeting.
    local port=$1
    echo "socket,id=ch0,host=127.0.0.1,port=$port,server=on,wait=on"
}

qemu_chardev_ctrl() {
    local port=$1
    echo "socket,id=ch1,host=127.0.0.1,port=$port,server=on,wait=on"
}

qemu_wait_listen() {
    # Open FD 3 on the data port. If a control port is given, also open
    # FD 4 on it. Bash's /dev/tcp connect can hang on WSL2 mirrored
    # networking when the port isn't yet bound; per-attempt timeout
    # bounds the wait at ~30s per FD.
    local data_port=$1
    local ctrl_port=${2:-}
    local i j
    for i in $(seq 1 30); do
        if timeout 1 bash -c "exec 3<>/dev/tcp/127.0.0.1/$data_port" 2>/dev/null; then
            if exec 3<>/dev/tcp/127.0.0.1/$data_port 2>/dev/null; then
                if [ -z "$ctrl_port" ]; then
                    return 0
                fi
                for j in $(seq 1 30); do
                    if timeout 1 bash -c "exec 3<>/dev/tcp/127.0.0.1/$ctrl_port" 2>/dev/null; then
                        if exec 4<>/dev/tcp/127.0.0.1/$ctrl_port 2>/dev/null; then
                            return 0
                        fi
                    fi
                    sleep 1
                done
                exec 3>&- 2>/dev/null || true
                return 1
            fi
        fi
        sleep 1
    done
    return 1
}

qemu_read_ready() {
    # Read READY from FD 4. Post-CL-397 the depot seed and every ELF
    # produced from current source write READY exclusively on COM2
    # (ch1, FD 4); FD 3 is data-only. We must NOT poll FD 3 here:
    # output from the guest's main path arrives on FD 3 in the same
    # window as READY arrives on FD 4, and any read-and-discard on
    # FD 3 swallows the first output line. That bug hit greeting,
    # tco-stress, and three other samples on the first ref-sweep of
    # the dual-chardev harness.
    local timeout_s=${1:-60}
    READY_FD=4
    if IFS= read -r -t "$timeout_s" line <&4 2>/dev/null; then
        if [[ "$line" == READY* ]]; then
            return 0
        fi
    fi
    return 1
}

export QEMU QEMU_ACCEL
