# gdb + QEMU debugging: stage1.elf bare-metal

Proven recipe for debugging `build-output/bare-metal/stage1.elf` produced
by the MM4 pingpong (Bootstrap 2). Works post-CL-290 — stage1.elf has
DWARF and 1865 subprograms resolve by name.

Both tools run inside **WSL** (gdb + QEMU TCG/KVM are Linux-side).

## TL;DR

Two operations, one workflow:

1. **Trace** — run stage1.elf under QEMU **TCG** (no KVM) with
   `-d in_asm` to capture every translated block. Use this to find out
   which addresses are actually executed.
2. **Probe** — run stage1.elf under QEMU **KVM** with gdbstub, set a
   hardware breakpoint at your address, report register values when hit.

Rule: **never set a gdb hbreak at an address you have not first confirmed
is in the trace.** DWARF lists functions that may be dead code. A bp at
an unreached address looks like "gdb broken" — it is not.

## Workflow

### Step 1 — find a candidate address

```bash
# Get a function's low_pc from DWARF.
/usr/bin/readelf -wi build-output/bare-metal/stage1.elf | \
    grep -B1 -A2 'DW_AT_name.*parse-match-branches'
```
→ `DW_AT_low_pc : 0x1e0534`

### Step 2 — verify the address is actually executed

Run stage1.elf under QEMU TCG with `-d in_asm` (inside WSL):

```bash
ELF=build-output/bare-metal/stage1.elf
SAMPLE=build-output/bare-metal/bs3-mini.codex   # or samples/foo.codex
TRACE=/tmp/bs3-qemu-trace.log
SERIAL=/tmp/bs3-trace-serial.raw
PIPE=/tmp/bs3trace-$$
rm -f "$PIPE" "$SERIAL" "$TRACE"
mkfifo "$PIPE"

# Feeder: wait for READY, then send CDX protocol
(
    while ! grep -qa READY "$SERIAL" 2>/dev/null; do sleep 0.3; done
    printf 'CDX\n'; cat "$SAMPLE"; printf '\x04'
) > "$PIPE" &

# NO -enable-kvm — TCG required for -d in_asm
/usr/bin/qemu-system-x86_64 \
    -kernel "$ELF" \
    -serial stdio \
    -device isa-debug-exit,iobase=0xf4,iosize=0x04 \
    -display none -no-reboot -m 1024 \
    -d in_asm -D "$TRACE" \
    < "$PIPE" > "$SERIAL" 2>/dev/null &
QEMU=$!

# Wait up to 120s (TCG is slow)
for i in $(seq 120); do
    kill -0 $QEMU 2>/dev/null || break
    grep -qa 'CODEGEN-HALTED\|CODEGEN-ERRORS\|CODEGEN-EMITTED' "$SERIAL" 2>/dev/null && { sleep 1; kill $QEMU 2>/dev/null; break; }
    sleep 1
done
kill $QEMU 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE"

# Check whether your address was executed
grep -c '^0x001e0534:' "$TRACE"
```
→ non-zero ⇒ executed, go to Step 3. Zero ⇒ dead code; the bug is
upstream, and a gdb bp here will never fire.

### Step 3 — probe with gdb

Run stage1.elf under QEMU KVM with gdbstub (inside WSL):

```bash
ADDR=0x1e0534
ELF=build-output/bare-metal/stage1.elf
SAMPLE=build-output/bare-metal/bs3-mini.codex
SERIAL=/tmp/bs3-gdb-serial.raw
PIPE=/tmp/bs3gdb-$$
rm -f "$PIPE" "$SERIAL"
mkfifo "$PIPE"

(
    while ! grep -qa READY "$SERIAL" 2>/dev/null; do sleep 0.3; done
    printf 'CDX\n'; cat "$SAMPLE"; printf '\x04'
) > "$PIPE" &

/usr/bin/qemu-system-x86_64 \
    -enable-kvm -kernel "$ELF" \
    -serial stdio \
    -device isa-debug-exit,iobase=0xf4,iosize=0x04 \
    -display none -no-reboot -m 1024 \
    -gdb tcp::1234 -S \
    < "$PIPE" > "$SERIAL" 2>/dev/null &
QEMU=$!
sleep 1

cat > /tmp/bs3gdb.gdb << GEOF
set architecture i386:x86-64
file $ELF
target remote :1234
set pagination off
set confirm off
hbreak *$ADDR
continue
printf "HIT $ADDR: rip=%#lx rdi=%#lx rsi=%#lx rdx=%#lx rcx=%#lx r8=%#lx r9=%#lx\n", \$rip, \$rdi, \$rsi, \$rdx, \$rcx, \$r8, \$r9
kill
quit
GEOF

timeout 30 /usr/bin/gdb -batch -nx -x /tmp/bs3gdb.gdb 2>&1

kill $QEMU 2>/dev/null; wait 2>/dev/null; rm -f "$PIPE" /tmp/bs3gdb.gdb
```

Output includes register values at bp hit:
```
HIT 0x1e0534: rip=0x1e0534 rdi=0x403f80 rsi=0x404030 rdx=0x5 rcx=0x8 r8=0x403f90 r9=...
```

### Step 4 — interpret registers

x86-64 System V ABI: first 6 args go in `rdi, rsi, rdx, rcx, r8, r9`.
Remaining on stack. Return value in `rax`. So the args above, for a
function with signature `parse-match-branches (scrut) (acc) (col) (ln) (st)`,
decode as:

| arg | reg | value | meaning |
|-----|-----|-------|---------|
| scrut | rdi | 0x403f80 | Expr pointer |
| acc | rsi | 0x404030 | List MatchArm pointer |
| col | rdx | 0x5 | Integer |
| ln | rcx | 0x8 | Integer |
| st | r8 | 0x403f90 | ParseState pointer |

## QEMU flags explained

| flag | why |
|------|-----|
| `-kernel stage1.elf` | multiboot boot of ELF |
| `-serial stdio` | kernel's `CDX\n<src>\x04` input, binary output |
| `-device isa-debug-exit,iobase=0xf4,iosize=0x04` | `out 0xf4, 0` exits QEMU cleanly (used by `--exit-mode qemu-exit`) |
| `-gdb tcp::1234 -S` | gdbstub on port 1234, start halted |
| `-enable-kvm` | 10×+ faster — use for all debug runs |
| `-d in_asm -D file.log` | record every translated block — **NO KVM**; TCG only |
| `-display none -no-reboot -m 1024` | headless, 1 GB |

## gdb script skeleton

```
set architecture i386:x86-64
file build-output/bare-metal/stage1.elf
target remote :1234
set pagination off
set confirm off

hbreak *0xADDRESS
continue
printf "HIT rip=%#lx rdi=%#lx rsi=%#lx\n", $rip, $rdi, $rsi

kill
quit
```

**Must set architecture BEFORE `file`** — default is i386, gdb rejects
the x86-64 ELF otherwise.

## Known quirks / gotchas

### 1. HW breakpoint requires exact instruction boundary

`hbreak *0x100010` mid-`cli`-instruction: silently never fires. Always
set bp at the start of an instruction (DWARF addresses are safe;
arbitrary `+N` offsets may not be).

### 2. "target running" after first continue

gdb batch mode has a quirk where after a HW bp hits, a SECOND `continue`
in the same session fails with:

> Cannot execute this command while the target is running.

The `vCont;c` async wait exits without receiving a stop reply. `stepi`
(count=1) works after a bp hit; `stepi N` (N>1) fails the same way.

**Workaround:** drive gdb in one-continue-per-session style. If you need
multiple stop points, set ALL the HW bps up front before the first
continue — bps still fire, they just can't be chained via script
`continue` after the first hit. Alternatively, use the gdb Python API
with a `stop` event listener and explicit polling.

### 3. Only 4 HW breakpoints (DR0-DR3)

x86 has 4 debug registers. Setting a 5th hbreak fails silently. Use
software `break` (INT3) for overflow — QEMU's gdbstub intercepts INT3
before the guest IDT.

### 4. TCG is slow

`-d in_asm` forces TCG (no KVM). stage1.elf compiling the mini sample
takes ~20-60s under TCG vs. ~2s under KVM. Use TCG only for tracing;
use KVM for all iterative gdb work.

## Why this matters for MM4 bug hunting

Early in a BS3 bug investigation, you may form a hypothesis like "the
bug is in `continue-ctor-fields` at line 127." DWARF will dutifully
report `continue-ctor-fields` at address X, and you'll set hbreak there.
If X never appears in the `-d in_asm` trace, that function is **never
called in the failing run** — the hypothesis is wrong. The real bug is
upstream, e.g. in `parse-match-branches` which took an early-exit path
and never dispatched to `parse-one-match-branch`.

Always trace first, probe second.

## References

- DWARF reference: `readelf -wi stage1.elf`
- Disassembly: `objdump -d -M x86-64 -m i386:x86-64 stage1.elf --start-address=0x... --stop-address=0x...`
