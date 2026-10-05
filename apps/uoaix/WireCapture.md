# Stage 0 capture procedure

**Acceptance pending (2026-10-04).** Evidence is local under
`build-output/uoaix/wire/`: `exploratory-both.log` connections 1 and 3 are
the original real-client login attempts. `capture-v2.log` connection 0 is
a synthetic replay. Root's timing correlation identifies connection 1 as
the likely subsequent real-client attempt. The capture recorded `ACCEPT`
and `END ... 0` despite a data frame in the VM's host log. That empty
receive remains an open finding. The arena-lifetime
repair and repeated synthetic connections do not close that finding.
`capture-v3.log` connections 0-7 are synthetic replay/refusal probes;
`v3-synthetic-receipts.json` identifies the six successful replays.
Fresh real-client runs and their screens wait for Damian's GO through root
because the client takes keyboard focus. The later real game capture
in `build-output/uoaix/game-trace35144-v2/server.log`, connection 1, establishes
rolling XOR for the game stream. [ProtocolFacts.md](ProtocolFacts.md) records
the complete-frame evidence and password transform. Stage 1 proceeds under root's direction;
[GameServer.md](GameServer.md) owns the implementation and remaining client
acceptance.

`WireCapture.codex` is an experimental listener, not an account server.
Use only the synthetic account `uoaixtest` with password `uoaixtest` in
the separate install at `D:\Projects\uoaix-client`. The original client
install is not modified. The listener records credentials as wire bytes;
the capture stays under the ignored `build-output/uoaix/wire/` directory.
The separate install's `LOGIN.CFG` must contain
`LoginServer=127.0.0.1,2593`; change only that install's configuration.

Compile from the workspace root after the required proof isolation:

```powershell
pwsh -File build/compile.ps1 -Src apps/uoaix/WireCapture.codex -Out build-output/uoaix/WireCapture.cdx -Log build-output/uoaix/capture-compile.log -Kernel seed/Codex.cdx
```

After RAM admission, launch a detached guest with the following arguments.
Record the process id and output path in the lane's status; redirect host
stderr to a unique file under `$env:TEMP`.

```text
tools/codex-vm.exe -kernel build-output/uoaix/WireCapture.cdx -headless -mem 3072 -portfwd 2593:2593 -output build-output/uoaix/wire/capture.log
```

First, wait for `LISTEN <connection> 2593`. Then log in with the test
account, save the shard-list screen, and select UOAIX. The diagnostic sends
a relay to the same port with key `12345678`; the next connection with
that seed is capture-only. No character screen or world is implemented
by the diagnostic. Close the test client after the game-login bytes have
arrived, wait for the next `LISTEN`, and repeat with a fresh client
process. Save both complete raw streams and the client executable hash.
The measured 1.25.32 `CLIENT.EXE` SHA-256 is
`F78A864042E3762B896CCDF1423E96D3023A969270C4EB6ACBD207CC71D80D07`
(separate shard install, rechecked 2026-10-04).

`RX <connection> <hex>` records TCP application bytes in receive order;
concatenate rows with the same connection id before interpreting packets.
TCP chunk boundaries are not packet boundaries. `TX-ATTEMPT` records
intended output; `TX-COMPLETE ... shard-list` and `RELAY ... True` report
completed sends. `REFUSE` and incomplete sends invalidate acceptance.
Synthetic TCP smoke traffic is kept separate by connection id and never
counts as a real-client run. Stop only the recorded guest process after
capturing, and retain the host stderr beside the evidence.

Run `.\apps\uoaix\CheckWire.ps1 -Capture <capture.log> -LoginConnections <first>,<second>`
from PowerShell with the two real-client login ids. The checker tries
plaintext and the 1.25.32 old-login XOR hypothesis from
[POL's crypt sources](https://github.com/polserver/polserver/tree/master/pol-core/pol/crypt).
The check compares every login and shard-selection byte with the synthetic
credentials, removes the seed-derived keystream rather than hiding payload
fields, and requires both completed responses. The checker alone does not
prove that a client rendered the shard list, nor identify the game cipher.
Those claims require the saved client screens and game-stream evidence.

The 2026-10-04 exploratory captures establish the login stream's old XOR
recurrence with keys `389DE58C` and `026950C6`. The 62-byte login is opcode
`80`, a zero-padded 30-byte account, a zero-padded 30-byte password with
13 subtracted from each nonzero ASCII byte, and one trailer byte. The
exploratory runs observed `5D` and `64` trailers; the checker requires an
explicit expected value (`-LoginFlag`, default 100 for `64`) and does not
mask that byte. Its meaning is not established by the captures. The client
can send `A4` hardware information (149 bytes) before the three-byte `A0`
shard selection. `LoginWire.codex` decodes incrementally across receive
boundaries and permits a relay only after the complete selection.
The first exploratory listener sent a premature relay on `A4`; those
post-list bytes do not establish a successful game handshake. The later
game-stream evidence is the separate real capture identified above.

`proofs/LoginWireProof.codex` grades a fabricated encrypted stream at every
split, incomplete login, completed hardware information, invalid shard,
unknown packet and relay-seed capture handling. Compare the complete native
output with `proofs/LoginWireProof.expected`. The stream is generated by an
independent host recurrence, not by the Codex decoder. A fixture is not a
replacement for the two actual client runs or their screens. Neither proof
is added to the battery.

The diagnostic refuses a connection once received input exceeds 4096 bytes and
uses a 4096-byte transport buffer. One `UoLoginWire` record per connection
holds ten Integer fields and one Boolean, padded to 88 bytes by the native
record layout. The decoder updates that single-owner record in place;
decoding takes constant work per byte without per-byte record allocation.
Log formatting is linear in received bytes, with one small text and one
list entry per byte. Only one connection
is active. The connection heap is restored before the next accept, which
bounds retained state across repeated runs. The NetCompact arena set is
allocated and returned to the process pool before that restoration mark;
the pool's retained pointer must never target reclaimed connection memory.
The accept-timeout branch also closes and releases the transport. The
runtime receive loop owns its transient polling allocations. The limit is
an experimental capture budget, not a production shard capacity.

