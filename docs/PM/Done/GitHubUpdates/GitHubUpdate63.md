# GitHub Update 63

**Release of 2026-09-25.**

Update 63 brings 256-bit vectors to x86-64, proven on real silicon; a
narrower language that refuses ambiguous forms; a typed IR graded against the
type checker; TLS 1.3 that Chrome completes; signed images verified from
memory; parsers that refuse hostile input and decode in linear time;
Ctrl-Alt-Del; and the Dev Console on the boot stick. The public history was
rewritten this cycle, so an older clone must be cloned again. Numbers in
parentheses are main changelists. The release proof follows at the end.

## Public history rewritten

- **A clone made before this update must be cloned again.** The phone
  project and the Commodore ROM bytes are purged from the public history of
  both mirrors, and old images and disks are no longer tracked (red 29018,
  29047).

## The zig plug (Steve Howell's)

- **The network entry streams its reply** (reek 27285): each definition is
  emitted above a heap mark and sent in ~16 KB batches, so a large reply no
  longer holds every definition's emission heap. The same 672,807-byte reply
  that ran the depot plug OUT OF MEMORY at 256 MB completes there
  byte-identically, in 19 s against 20 s.

## Keys reach the apps as CCE

- **Enter, Backspace and Escape work in the Secrets lock screen, the helm and
  collab compose boxes, the diagram label editor and the browser address
  bar** (blu 27601, 27604). Each handler compared ASCII 13, 27 and 8, but
  `poll-key` delivers those keys as named keys, so Enter inserted CCE unit 255
  instead of submitting, and printable keys went through a second Unicode
  conversion that dropped or garbled them. `poll-key` also answered raw
  Unicode on the UEFI path and CCE on the PS/2 path; both paths now answer
  CCE (`codex/test/apps/key-contract`).
- **No stored vault changes meaning.** The Secrets lock screen commits a
  passphrase only on Enter, and Enter never reached it, so no vault was ever
  keyed from the keyboard. No shipped image provisions a vault: no build
  script or image recipe references the Secrets app.

## 256-bit vectors on x86-64

- **x86-64 programs can compute on 256-bit vectors.** `Vector 4 Real` and
  `Vector 8` operators run on YMM registers through a new VEX encoder, with
  splat, extract, select, reduce-add and mask any/all/none/count. A program
  that uses them stops by name at start-up when AVX was not admitted, and
  hosted targets refuse at compile time (red 28961). The compile mode flag
  `avx-local` drops that start-up check for a unit that reads the admission
  cell itself before its 256-bit work (red 29012). The 256-bit types
  `vec8-` and `vec4d-` landed first, with a vector mask carrying its element
  type (val 28881). The lanes are proven on metal: at hardware sitting 16, on
  an i7-6700K (ASUS SABERTOOTH Z170), the UEFI boot admitted AVX and all ten
  256-bit lane rows answered right (`docs/Hardware/HardwareSitting.md`).
- **The kernel admits AVX at boot and keeps each process's vector state.**
  The boot and application processors set CR4.OSXSAVE and XSETBV, and the
  process switch saves and restores with XSAVE/XRSTOR (reek 28778). Each
  process keeps its own XMM state (val 28742), and the preemptive scheduler
  registers the unsaved XMM state (val 28648). A UEFI payload such as the
  diagnostic stick now admits AVX too, and the diagnostic image gains an
  `avx` stage (fester 28945) that runs ten 256-bit lane rows when AVX is
  admitted (fester 29025).
- **codex-vm exposes AVX to its guests.** CPUID leaves 0, 1 and 0Dh answer
  truthfully, application processors get CPUID answers, and `-no-avx` turns
  it off (reek 28762).
- **Targets without 256-bit support refuse instead of trapping.** The wasm
  plug refuses the 256-bit widths at assembly rather than at run time, and a
  test pins the ARM64 and RISC-V refusal (reek 28913).
- **Vector equality compares lanes.** `==` and `/=` on packed vectors
  compared addresses; they were first refused (val 28933) and now answer a
  VectorMask lane by lane on `Vector 2 Integer`, with `~` and `~0` doing the
  same on `Vector 2 Real` and `Vector 4 (Real approximate)` (val 28990).
  `==` on Real lanes stays refused (CDX2085), ARM64 and RISC-V refuse the
  lane-wise names at compile time, and `==` on a record with a Real, Vector
  or SizedVec field is refused with CDX2099 (val 28784).
- **`==` on a value typed by the definition's own type variable is refused
  (CDX2100).** The body cannot see the type, so the comparison compared two
  machine words: for Text, a list, a vector or a record, their addresses, and
  `same (glue "a") (glue "a")` answered False. No program in the tree reached
  such a comparison (blu 29046). Generic equality by dictionary passing
  follows this release.

## A narrower language: ambiguous forms are errors

- **A newline ends an application.** A token left in a definition body after
  its expression ends is refused (CDX1078), a bare literal statement that is
  not the last in an `act` block is refused (CDX1079), and CDX1070 no longer
  fires on a next line at or left of the application's start, so a new `act`
  statement needs no parentheses. A survey fixed 43 old sites, among them a
  MeshRoles function that was missing its `if` (red 28795).
- **`do` as a definition body is refused by name** with CDX1075, which
  points to `act ... end` (red 28916).
- **A column-3 token that top-level recovery used to drop silently is
  refused (CDX1077), closing issue 120.** `-Prose` mode now parses what
  normal mode parses; 185 sites were resolved (fester 28606).
- **A match on literals needs a catch-all.** A literal match with no
  catch-all compiled clean and answered its last arm; it is now refused with
  CDX2070. A Boolean match needs True and False, and a bounded Integer whose
  literals name every value is exhaustive (fester 28161).
- **A constructor field written `(A, B)` with no arrow is refused at the
  declaration** with CDX1076; it used to become a function type and fail
  later at a use (fester 27566).
- **A bounded unit is narrowing-checked** at fields, parameters and returns
  (blu 28587).
- **The tree compiles under the new rules.** Stray tokens and over-indented
  prose that CDX1078 refuses were fixed in accumulator-corpus, the diagnostic
  stick, the wgsl plug and apps/globe/BlackHoleSimd; BlackHoleSimd still does
  not compile, because it calls builtins that exist in no revision of the
  compiler (red 28807, fester 28873, red 28896, red 28925).

## The compiler's typed IR matches its type checker

- **A whole-IR fidelity check grades every typed IR node against the type
  the checker filed for it.** It runs under the compile flag `fidelity`; with
  the flag off it costs one Boolean test per inference. Its first corpus run
  found 20 disagreements (red 28434).
- **Those disagreements are fixed.** Comparison operands and act binds lower
  with no inherited expected type (red 28466), a field access carries the
  checker's instantiated type (red 28513), and the lazy thunk carries a real
  function type (red 28809).
- **A record with type arguments gets its instantiated equality helper**
  (COMPILER-91): 12 sites that failed with CDX2040 now compile (blu 27339).
- **Growing inference accepts a counter-guarded branch** (COMPILER-7): a
  branch reached only while a counter parameter is below an integer literal
  is accepted when every self call advances the counter by a positive
  literal. The same change adds a test of the x86-64 capability grant and
  revoke emitters at bits 31 and 40 (fester 28799).
- **x86-64 prints every CCE character correctly.** Tier-1 slices 0 and 1
  printed 64 code points high (reek 27543), the tier-2 delta is now
  sign-extended (red 28256), and two uncalled CCE helpers are gone from every
  x86-64 binary (reek 27607).
- **A file read or write in argument position no longer corrupts the
  stack.** The x86-64 default FileSystem servicer stubs left the closure
  count word on the stack, which was fatal in argument position; the
  "spawned child crashes inside write-file" report was the same defect
  (blu 27970).
- **Every compiler chapter compiles standalone** from only what it cites
  (val 28693), measured per chapter by check-subset-cites (val 28593, 28696),
  with the plug builders stripping two compiler cites (val 28638).
- **The native plugs share literal tables.** COMPILER-86 stage 3 lands for
  the arm64, riscv and wasm plugs (reek 27702, 27710, 27712).

## TLS 1.3 and the network stack

- **Chrome completes a TLS 1.3 handshake with the Codex server.** The server
  signs with ECDSA P-256 (RFC 6979) and honours signature_algorithms
  (red 27648), answers HelloRetryRequest when a client sends no X25519 share
  (red 27449), and aborts with handshake_failure when the ClientHello never
  lists X25519 (red 27451). The server finds the client's X25519 share by
  walking the key share list, so a client that leads with GREASE or
  X25519MLKEM768 is answered on its X25519 share (red 27303). Chrome 153 headless completed TLS 1.3 with the
  P-256 CertificateVerify accepted (red 27708).
- **A peer that abandons a connection releases it.** A raw receive returns
  at end of stream once the peer's FIN arrives, and an RST closes at the
  exact expected sequence (RFC 9293, RFC 5961); an abandoned handshake used
  to hold the server for 120 s, and the next client is now served in 0.1 s
  (red 27708). Framed receive loops return at the FIN on both architectures
  (val 27765), as does ARM64 raw receive (val 27763).
- **The multiplexed web server no longer loses client bytes.** A receive
  buffer minted inside the serve loop lay above the heap mark the loop
  restores to, so bytes a client sent during a reply were written into live
  heap and never parsed. The mux now mints one buffer before the mark, caps
  admission at 64 connections, and holds 546 bytes per connection on x86-64
  instead of 66,082 (val 29093). Unparsed request bytes are capped at 8,192
  across connections, and a connection past the cap is answered 413 and
  closed (val 29104).
- **An idle web server waits for the tick instead of polling the NIC.** Over
  60 s idle at `-smp 2`, VM exits fell from 13,954,806 to 37,287 (val 27721).
- **The ARM64 web server reclaims memory per connection.** Received frames
  are reused, 1276 -> 202 heap bytes per frame (blu 27236, with a
  backend-neutral test fix in 27337); Arm64NetIO compacts (blu 27488);
  virtio-net re-posts its rx descriptors (blu 27560); the server reclaims per
  connection (blu 27562), has a 64 KB receive buffer (blu 27628) and a
  checked send (blu 27782); and the ARM64 poll interval is calibrated against
  CNTVCT (blu 28051).

## Verifying signed images

- **A CDX image can be verified straight from memory.** verify-cdx-full-at
  and evaluate-load-at verify from an address beside the list path
  (blu 28001), and OTA Gate B verifies the staged bank in place (blu 28025).
- **Proof-bearing images verify.** Full verification now hashes the proof
  section as the encoder does; before, every proof-bearing image was refused
  "content hash mismatch" (blu 28012).
- **Capability and effect entries are bounded by their section.** A signed
  image declaring a capability scope length or an effect name length of 60000
  crashed the guest; both verification paths now refuse it with the same
  verdict (blu 29069).
- **The trust threshold comes from the caller's policy** (min-trust, default
  5000), never from image byte 216 (blu 28501).
- **Verification heap on the address path fell from 7,754,748 to 19,836
  bytes**: signature checking restores the heap after ed25519-verify
  (blu 28519).

## Hostile input: refuse, and stay linear

- **Parsers refuse what they used to accept or trap on.** Toml and Uri
  answer only inside a stated subset; Uri had answered host good.com for
  `http://good.com:x@evil.com/p` (fester 27110, 27269). Yaml, Smtp, Syslog,
  Tftp, Icmp and the Markdown encoder gained guards (fester 27123, 27281,
  27298, 27306, 27317, 27328); tftp-receive-data accepts only the next block
  (fester 27437); Jwt segments decode strictly (fester 27444); the persist
  chapters refuse malformed stored keys and signatures (fester 27474), as
  does SourceDefWire (fester 27698). Deflate stored-block headers, CDX
  section bounds and TrueType contour order no longer kill the guest
  (fester 27874, 27925, 27962). FAT16 and FAT32 file reads refuse a bad
  cluster, a short chain and a cycle (fester 27660, 27642); GPT LBAs are
  bounded before any addition (blu 28279); DiskFacts checks superblock
  credibility (blu 28396) and refuses a boot-config length past the sector
  (fester 28524).
- **Byte-to-text decoding is linear instead of quadratic.** json and csv
  parsing (fester 27524, 27497), the fact-store reader, with a test fixture
  for the 4 MB entry cap minted at test time (fester 28405, 28371),
  MessageFraming (fester 28431), HttpClient responses (fester 28440),
  CdxBinary and CdxVerifier names (fester 28448), WebServer requests
  (fester 28474), and Gguf and SafeTensors text (fester 28488). Before, a
  256 KB frame or body, a 64 KB name, a 2 MB request or a 1 MB GGUF string
  ran a 3 GB guest out of memory.
- **Integer overflow is refused, not wrapped.** ExprEval refuses overflowing
  literals and `+ - * /` instead of trapping (blu 28425); IdeaEngine,
  CellFormula, CellCsv, the mathbook Parser and BulkLoader refuse integer
  literals past i64 max (blu 28454); and the Sheets CellStore refuses a
  number outside i32, so a wider CSV field stays text (blu 28481).
- **The Brotli decoder conforms to RFC 7932.** A 24-stream conformance test
  graded by .NET found 6 red, two of them crashing the guest on valid
  streams; five decoder fixes make all 24 pass (blu 28225).

## Hardware debugging, timers and boot

- **x86-64 hardware watchpoints.** New builtins read and write debug
  registers DR0-DR3, DR6 and DR7 (reek 27585), a bare-metal #DB handler
  records a hit and resumes (val 27676), and the debugger's watch menu arms a
  hardware write watch (val 27689).
- **ARM64 generic-timer builtins.** The frontend declares them, so
  Arm64Timer compiles and runs on arm64; riscv and x86 refuse them by name
  (red 27913).
- **Hardware sitting 16 answered four more questions on the i7-6700K.** The
  Clock accessory's real-time-clock write works on that silicon; flushed
  writes to the stick survive power-off; one keystroke at the top of a 2.9 MB
  source file costs 30.3 ms there; and entering VMX hangs on that board
  before its first note, which is open (`docs/Hardware/HardwareSitting.md`).
- **Ctrl-Alt-Del resets the machine.** Every x86-64 Codex image resets on the
  chord from a PS/2 keyboard (the kernel's IRQ1 handler), and the diagnostic
  image also resets from a USB keyboard (reek 29077). The chord is rehearsed
  on the beds only: codex-vm over PS/2 and USB, and OVMF over USB. Under OVMF
  a PS/2 key never reaches the IRQ1 handler, an open defect (COMPILER-104),
  and no real hardware has been measured.
- **UEFI boot is sturdier.** cdx-to-pe with `-ExitBootServices` sizes the
  memory map from the firmware and its panics really halt (reek 27513), and
  build-boot-img passes `-ExitBootServices` for GopBoot, so the tested image
  is the one that is flown (val 28189). codex-vm's GetMemoryMap answers
  EFI_BUFFER_TOO_SMALL with the needed size, as UEFI specifies; answering
  success had halted every `-ExitBootServices` image in codex-vm (reek 28214).
- **The diagnostic stick for hardware sitting 16** adds edit and vmx stages
  and a may-wedge gate, rehearsed over 51 arms (fester 28331, 28359). USB
  mass-storage walk tests tell the two controllers apart (val 27355).

## The Dev Console on the boot stick

- **The Dev Console runs on the GOP framebuffer after ExitBootServices.**
  Output draws on an 80x25 cell grid (val 27396), keys arrive through a
  fixed ring (val 27406), the console runs as its own process (val 27413),
  GopBoot's menu carries a Dev Console row (val 27415), and under OVMF the
  console opens, draws and takes keys (val 27418, 27420), proven end to end
  (val 27422).
- **It browses the stick's own source.** StickSource indexes SOURCE.SRC off
  the ESP in a fixed 1.3 MB block (val 27441); Browse Source, the chapter
  viewer, search and the debugger read it, and under OVMF it indexed 6610
  definitions from SOURCE.SRC (66445 lines) (val 27455).

## Keys reach the apps as CCE (additions)

- **InputSource keys follow the poll-key contract** (blu 27616), and four
  desk byte sites treat DEL as a control (blu 27390).
- **Foreign bytes are cleaned before they reach the encoder.** Eleven sites
  route through cce-foreign-byte-text (tab to space, controls and DEL
  dropped) (blu 27376), and foreign bytes and JSON escapes no longer reach
  the encoder as -1 (blu 27576).

## Desk, 3D and apps

- **3D texturing and shadows are perspective-correct.** Both rasterizers
  interpolate UV and light-space position perspective-correct (reek 27464);
  the Aquarium floor is a subdivided mesh (val 27383); and software shadow
  acne on a lit cube face falls from 87 pixels to 0 (reek 27511).
- **Desk fixes.** The Browser and the 3D panes paint under an open start
  menu (val 27117); the start menu answers arrows and Enter (val 27256);
  syntax-highlight ink keeps a contrast gap to its background (val 27334); a
  transient notice shows in the taskbar clock cell (val 27369); and a test
  grades the desk icon box's vertical bound (val 27367).
- **UI library fixes.** Tables built from one base no longer share its list
  (fester 28541), and event-path-to returns the path to its target, not every
  widget the search visited (val 28354).
- **Secrets team sharing uses real X25519 key agreement**, a per-share salt
  and nonce, and replay refusal (blu 28076).
- **The C64 CIA block moves off the SID**, so a SID write no longer presses
  keys (fester 28165).
- **The C64 runs on the MEGA65 Open ROMs** (LGPL-3.0 or later), shipped with
  their licence and attribution; the C64 web page thanks MEGA65 (val 29034).
- **The services app compiles and runs** (fester 27531).
- **CVMM reads live system state.** ProcessManager reads the kernel process
  table, DriveManager the block devices and NetworkManager the NIC driver
  (blu 28118, 28143, 28152); CvmmPersist compiles and round-trips
  (blu 28092); and the cvmm page build fails when it produces no page
  (val 28345).
- **Prism composes subsets.** Per-feature compatibility receipts, the page
  sending the composed selection, and composed subset packages (red 27262,
  27289, 27295).

## Games

- **Spider is solved by search.** It deals from a fair shuffle
  (val 27654), and a depth-first search wins 9 of fair seeds 1-30 where the
  greedy player won none (val 27656).
- **Monopoly seats trade.** Self-playing seats trade for colour groups
  (val 27625) and offer trades to the person at seat 0 (val 28556).
- **Game rules completed.** a friendly-occupied HexWar hex is exempt from the enemy zone for supply (val 27577); Checkers ends a barren endgame as a draw and a blocked
  side as a loss (val 27535, 27551); Crazy Eights ends a dead position
  (val 27528); and poker wild cards substitute instead of counting
  (val 27521).
- **The Magic engine.** CodexMagic and CardGen draw from the Games Rng
  (val 27638, 27740, 27751), magic/Test runs to its end (val 27773), and
  CardEmitter targets the Magic quire (reek 28829).

## Plugs

- **Wasm modules declare their exports.** The export allowlist is deleted,
  so a module without a `wasm-exports` declaration exports no application
  definition; fishtank, the last module on the allowlist, and the other app modules declare theirs (reek 28846, 28853, 28857,
  28864, 28870, 28876, 28897, 28922).
- **Wasm gains effect handlers and more.** Effect handlers (reek 28582),
  handler clauses that capture locals (reek 28611), fork running its thunk
  (reek 28491), Text append in place (reek 28460), WAT name sanitizing and a
  stratified hosted sample (reek 28399), and one refusal spelling
  (reek 28673). The release gate builds the wasm bundles and runs the wasm
  plug (reek 27310, 28304), and the fishtank builder builds at head
  (reek 27245).
- **ARM64 and RISC-V handler clauses capture locals**, with the clause lift
  shared across plugs (reek 28709, 28713).
- **ARM64 fixes.** Heap-advance alignment, bounds guards that spare the
  guarded register, nested constructor patterns, full-width wrapping,
  text-compare sign, tail-call spilled arguments, `__buf-write-byte` cursor,
  `print-uni` decoding multi-byte CCE, `__text_to_double` correctly rounded,
  `show` allocating exactly, and bounded-integer record fields packed
  widest-first (reek 27113, 27875, 27892, 27939, 27837, 28112, 28130, 28084,
  28238, 28244, 28268; blu 28937); a prologue stack/heap guard
  (blu 27724); TCO gates that see top-level constant reads (reek 28253).
- **RISC-V fixes.** Stack arguments reserved above spills, far stack
  parameter loads, text-compare sign and bound, `__buf-write-byte` cursor,
  the runtime gaining char-encode, unsigned byte reads, `print-line-uni`
  decoding multi-byte CCE, and `print-uni` without a trailing newline
  (reek 27148, 27651, 27853, 28138, 28734, 28739, 28750; blu 28834, 28840).
  Bounded stack parameters are guarded (reek 27131).
- **Text plugs serve the sized-vector builtins.** The Python, JavaScript,
  TypeScript and zig plugs emit `vec-empty`, `vec-singleton`, `vec-cons`,
  `vec-head` and `vec-length`; `codex/test/ops/vec-sized` failed through each
  plug before and matches its `.expected` after (blu 29055, 29057, 29059,
  29061).
- **`__heap-base`** is a new builtin on every target, and wasm render
  targets are no longer refused (reek 28569).
- **HTML plug.** Accessibility, Charts/Vector, Scroll and Overlay routes
  (val 28417, 28402, 28389, 28386), widget shadow, gradient and accent border
  (val 28347), `__record-set` mutating its target (val 28343), Boolean shown
  as True/False (val 28368), zero-parameter definitions evaluated at every
  use (val 28450), Integer results from runtime builtins arriving as BigInt
  (val 28478), and every generated app page loading with 0 console errors,
  28 of 28 (val 28503).
- **TypeScript and C# plugs.** TypeScript refuses an unknown callee at the
  name site, wraps bounded integers and matches integer literal patterns
  (val 28341, 28313, 28315); C# gains vector builtins (reek 28599).
- **The img plug's FAT16 writer refuses a payload that does not fit**
  instead of writing past its buffer (val 28250), with a capacity test
  (val 28292).
- **Cross-architecture tests.** A match-in-record-field test (reek 27877),
  a desk-parse fixture that no longer reads a null registry (reek 28248),
  and arm64 pins for two heap tests (reek 28420).

## Tooling and tests

- **Test disk images are minted at run time.** All 100 codex/test disk
  images leave the depot for sha256-pinned recipes, and no disk image is
  tracked under codex/test (reek 28954, 28968, 28979). The 20 FAT16 volumes
  that carried an old compiler and EFI loader keep every structural byte and
  replace those two files' data with a fill pattern; no test's expected
  output reads either file.
- **Generated plug output is checked for unknown callees** for zig, C#,
  Python, JavaScript and TypeScript (blu 28686, val 28309).
- **Plug bundles know when they are stale.** The staleness manifest covers
  every input the assembler read (val 28301), and bundle-app writes its
  inputs list (fester 28635).
- **Harness improvements.** Per-architecture `.expected-<arch>` files and
  `.arch-only` honoured by test, BVT and both cross harnesses (reek 28287,
  28295); per-run plug host ports via codex-vm `-natmap` (reek 28324);
  `compile -Measure` reports the front end's errors (reek 28318); cite-gate
  names a selected library chapter instead of compiling it to CDX2040
  (red 27779); two more tests join the BVT (blu 28190); a product smoke
  runner with sidecars (val 28573, 28621); annotation targets checked by
  build/check-annotation-targets.ps1 and run by build.ps1 (val 27135,
  27265); a WorldProbe app-sweep fix (red 28860); and two shell-raw baseline
  fixes (reek 28306, val 28283).
- **ACCP.** The hosted Windows arena is committed on demand in 64 MiB chunks
  (reek 28228), and the runtime build bundles IR/ConstShare (reek 28236).
- **Prism's dead `server.ps1` is deleted** (fester 27192).

## Source cleanup

- **Prose about our own code was removed** across codex/os/net, the kernel,
  dev, trust, replay, verify and sched chapters, the wasm plug, apps/works and
  most app quires including games and safari, with code lines unchanged
  (blu 27180, 27400, 27734, 27737, 27743, 27750, 27754, 27756; reek 27183;
  val 27258, 27817, 27821, 27828, 27840, 27844, 27850, 27856, 27858, 27865,
  27936, 27997, 28019, 28040, 28047, 28056, 28070, 28088, 28110, 28124,
  28135).

## Release proof

The release gate and battery found nine defects that no lane gate reaches,
and each was fixed before the proof below:

- Under `-Prose`, the prose-to-notation check (CDX1101) had gone silent: a
  parser look-ahead moved the parse state it was only meant to inspect, so a
  prose line after a definition was swallowed into the definition and never
  checked. The look-ahead now reads by position, which moved the seed
  (main 29154).

- The circuits help overlay never drew its last row: the row had no
  `draw-gpu-text` head, so a stray `)` ended the expression, and the old
  parser dropped the rest silently. The narrower rule refuses that form
  (CDX1078), which is how the sweep found it (main 29152).
- `text-in-list` was defined in both the Desugarer and the Type Checker
  (CDX3006 twice); the Desugarer's copy is renamed, which moved the seed
  (main 29144).
- Four 256-bit vector builtins read `fixed` with no allocation site recorded;
  each allocates one 32-byte vector, now pinned (main 29142).
- The backlog-id checker threw on a one-line register (main 29135); an
  annotation named a function that had moved to another chapter, and this
  cycle's shell-generator growth was unrecorded (main 29142).
- Three arm64-only test chapters were compiled on the x86-64 bed, and a
  fourth ran there, where its heap never reaches the stack (main 29146,
  29148).
- A new build tool was missing from the tool catalog (main 29150).

The full gate passed across two runs on one build output, the second
resuming at the phase the first stopped on: a one-pass hard fixed point, the
text leg, 237 declared refusals, all 1,777 test chapters compiling (3
arm64-only chapters baselined to their cross runner), 3,419 test-run checks,
cross-architecture and plug smoke, 57 generators with no drift, both VM hosts
agreeing, UEFI console output, deck headroom (tightest margin 2.17), the app
sweep, nine wasm bundles and 59 hosted wasm programs. The final seed
`C74F10419BA0DB66` was then rebuilt from the final source with stages 2, 3
and 4 byte-identical, a 147-check BVT and a 162-chapter cite-gate, and it
verifies itself. IR fidelity graded its reader and verdict controls and
reported nine cases with no unexpected result.

The battery ran as three tier groups whose union is `-Tier all`. The poison
battery, built from the final source, passed 2,085 of 2,142 tests with no
failures and 57 declared skips. The normal battery's lib, fw, oracles, apps,
hardware, traps and slow tiers passed on the final seed (386, and 689 with
23 declared skips); its lang tier ran on the previous seed and found the
prose regression above, and `prose-anchor` passes on the final seed. The app
sweep on the final seed compiled 300 units clean, 1 known baseline failure
and no regressions over 301.

Roslyn built the freshly emitted C# compiler. Both DDC arms produced
3,760,588 bytes with zero differences from the release seed outside offsets
40..135; 96 signature bytes differed. The symbol map matched all 6,305
embedded MAP1 rows by name, address and size. TechnicalDetails records the
seed digest.

Both boot images were rebuilt on the release seed. All 57 diagnostic
rehearsal arms passed across Codex VM and QEMU/OVMF with a 180-second minimum
arm allowance, and the shipping check confirmed the default configuration.
The diagnostic image SHA-256 is
`FD3FA5D717E309CA8D075E70AE585ED8A727BC856B1F8244897D70818A961785`.

Five-second memory samples from 13:04 to 15:00 measured a free-memory floor
of 3.87 GiB at 13:33, during the gate's test-run phase: four guests with
4,242 MiB combined working set, about 1,060 MiB per guest. The sampled guest
peak was eight at 13:41, at 811 MiB combined because those guests had just
started. These are sampled host observations, not per-guest heap high-water
guarantees.
