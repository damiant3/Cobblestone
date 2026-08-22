# GitHub Update 49

**Scope: main CLs after the Update 48 release push commit, opened 2026-08-20.**
Update 48 covered main 17237 to the release head plus the release's own map,
img, diag, README and report CLs. Accumulate this cycle's themes here as they
land; every number in the final report gets re-measured at the release head,
not carried forward (L-COUNT).

## Open from Update 48

- ~~**The batch stream can lose bytes, and the harness cannot tell that from a
  miscompile.**~~ LANDED: blu's census counts what `blit_guest_output` drops
  and both harnesses read codex-vm's stderr instead of deleting it (17910);
  reek's runner compares LENGTH before rendering a codegen verdict (18992);
  L-SHORT's runner column reads yes. The hazard it named stands as a lesson,
  not as an open item.
- ~~**plugs 1.34, the ARM64 MMIO boundary**~~ LANDED as (a): the ARM64 MMIO
  window gated on `cap-device`, VirtioBlk `-auto` roots carry `Device.Mmio`,
  both halves armed (18038, root).
- **B4 step 6** waited on the B3 metal sitting; **B3 flew at sitting 11
  (18980), the exchange completed on the real I219**, so the wait is over and
  step 6 itself is still open.
- **NIC-4, NIC-5, WORKS-9, the native GOP metal half and A8's metal arm**
  rode sittings 6 through 11: NIC-4's ring half answered and its successor
  question stays open (18279, 18283); WORKS-9 returned a threshold on metal
  (18276, 18273); gopmode honoured at 1920x1080 on the ASUS (18276); NIC-5
  and A8's metal arm are still open.
- ~~**Rulings still queued for Damian**~~: the queue became Decisions and red
  made the technical calls per Damian's direction (17898); 4 ingested (18656);
  10 landed (18038); 19 closed with ThreatModel (18706); 20 ruled (17885).
  **16 (ProductBuilder stage 6 host) is customer-gated and is the only one
  left.**
- ~~**`flash-open-bank` Board threading**~~ LANDED 17839 and 17841 (root), on
  2026-08-20 before the push commit, so it is already in the public Update 48
  tree.

## Landed this cycle

**Measured at head 19029 (2026-08-22), from the depot, not carried.** The
Update 48 push commit is main 17857 (red, 2026-08-20), so this cycle is main
17858 through 19029: 308 CLs on main, by copy-up client reek 62, fester 51,
red 50, val 50, root 44, blu 51 (32 from his own client and 19 copied up for
him through the main client). Seed at head `29C23385` (2,877,350 bytes, main
19025); `build/boot/diag.img` at head `2C7030D7` (sitting 11's image, 43 arms
rehearsed 2026-08-21). **One outside pull request landed after that
measurement and before the pin: PR 76 (Steve Howell), main 19045 (reek).**

### The release head, measured after the pin at 19045 (red)
- **PR 76 (Steve Howell), main 19045:** the zig plug's integer arithmetic
  wraps (`+%` `-%` `*%` on the integer rows, `-%` for non-real negation,
  `cx_ipow` mirroring `__ipow` with a negative exponent answering 0, shift
  counts masked `& 63`), a shadowed-builtin yield fix, and `Char` as a CCE
  code. Main carried none of it: the apparent hits were prose. His corpus
  went 0/5 to 5/5 against the `.expected` oracles with a control run. The
  oracle cannot catch a `+` put back (49/49 either way; no overflow row),
  recorded as plugs 1.56 with his verified C# XOR aside. The PR stays open
  until this push, then closes with the verifying commit named.
- **The image that ships, `build/boot/diag.img`, is the DEFAULT image, not
  a sitting's (19047, 19048, red).** Two runners landed the same day by two
  lanes had made that image impossible: `check-shipping-images` refused any
  `DIAG.CFG` (18237) while `build-diag` refused to build without one (18645)
  and bakes `diag-default.cfg`. The check now accepts exactly the checked-in
  default, byte for byte after normalisation, and refuses anything else
  naming the first differing line; falsified both ways (sitting 11's image
  refused, the default accepted). The sitting configs themselves,
  `build/boot/diag-sitting*.cfg`, name the box and never ship (PublicPush,
  19035).
- The shipping image is rehearsed on every arm in both beds; its hash is
  in the README beside the seed digest and is re-measured below at the
  release head, not carried.

### What the release proofs found (red, 2026-08-21 evening)
- **The full gate was green on the first run** (2,017 s in `red-main`):
  hard fixed point in one pass, the text leg ran (26.6 / 43.5 / 22.2 / 0.2 s),
  `test-compile` compiled all 1,436 chapters in 1,326 s, the 270-unit app
  sweep read 265 clean, 5 known-dirty, 0 regressions, 57 generators 0 drift,
  202 refusals as declared, doc counts 63 of 63. `Sut === seed`
  (`A01C1547`), so no seed rebuild was due.
- **The DDC witness holds**: the C# arm, emitted from this seed and built by
  Roslyn with 0 warnings, reproduced 2,877,350 bytes with 0 differing outside
  the signature region and 95 inside it.
- **The first battery run was RED: 1,585 pass, 3 fail, 48 skip** (every
  oracle green: scalar 2013/2013, vector 130/130, CCE 1485/1516 with 31 in
  documented gaps). The three were `browser-chrome-layout`,
  `browser-viewport-fit` and `badge-tags`, all `FAIL_OUTPUT`, none
  truncated, and all three had been red at head for most of the day:
  BROWSER-8 (18453) replaced the newtab panel with `scroll-view[viewport-row]`
  and its indicator takes 6 px of the viewport; val's antialiased coverage and
  ramp shadow (18827, 18893) enlarged the badge's pixel counts (hollow 44 to
  52, filled 60 to 70) while the test's own invariants still answered yes.
  Every stream held the same stale expectation; **nobody re-ran them because
  the battery is not a lane's gate and `-Internal` does not carry them.**
  Re-baselined with those CLs cited (19056, 19057) and the battery re-run.
- **The battery on head 19057: 1,636 total, 1,588 pass, 0 fail, 48 skip,
  672 s**; the three re-baselined tests `PASS_EXPECTED`, nothing newly red.
  The separate app sweep and the poison build ran after it, and both are
  green: **the app sweep 270 units, 265 clean, 5 known-dirty, 0
  regressions (127 s); the poison battery against the 0xCD-fill seed
  `69077E8A` (compiled by the release seed) 1,636 total, 1,588 pass, 0 fail,
  48 skip, nothing newly red (661 s), the working kernel restored to
  `A01C1547` afterwards.** Four proofs, four greens, on the bytes that ship.
- **The README gained the capability that moved this cycle** (19058): the
  network is proven on real hardware, the I219 on the same consumer board,
  b3's TCP exchange with the development machine with the driver as
  shipped.

### The artifacts, measured at the release head
| artifact | SHA-256 | bytes | CL |
|---|---|---|---|
| `seed/Codex.cdx` | `A01C1547E92EB0D074B80EA3AF3957580307BF68D819D57BB880DB5294CE65D7` | 2,877,350 | unchanged this release (Sut === seed) |
| `seed/Codex.img` | `B5D3863B8AE683A56BEADBD009EA091EE00B86A0CA9B2418803D746C12F1128C` | 16,777,216 | 19053 |
| `seed/Codex.map` | from `Sut.map`, 5,334 of 5,334 symbols match the embedded MAP1 | | 19053 |
| `build/boot/diag.img` | `1190FD4C54BCEEA54416566AF3BB1E6C241247D79ECC83BA33335B1CDB55103B` | 16,777,216 | 19048, rehearsed 46 arms both beds, default cfg, shipping check OK |

**Release head: main 19057, plus the release's own CLs after it** (19058
README; this report's copy-up; the push commit syncs main at the last of
them). This cycle is main 17858 through 19057.
- **Two runners had made the shipping image impossible**, and one pre-push
  scan found the sitting configs untracked; both are in
  `SomethingSeenDuringRelease.md` under this cycle.

### The I219 campaign: six sittings, and the ASUS talked to the dev box (red, blu, reek, root)

- **Opened 18319 (red):** 8086:15b8 is a PCH part, MAC in the PCH and the
  I219 as the PHY, with eight datasheet requirements the e1000 driver never
  met. **The bed lied about its identity** (18321, 18324): codex-vm presented
  15B8 with 82574 semantics; reek's I219 model landed 18335 and the FLY GATE
  rule (18327, red) requires one falsifiable pair on the model before any
  image is composed; a flight carries what main carries (18344), and a
  measurement flight proves its own instrument (18346).
- **Sitting 6 flew (18276, red):** the sink ladder returned a THRESHOLD on
  metal (16 sectors, done=4) after four sittings of one bit; gopmode
  honoured at 1920x1080 on the ASUS; NIC-4's ring half answered
  `rdh-writable=y` (18279, blu); the "arrived-but-invisible" reading is NOT
  eliminated (18283, correction). WORKS-9 banked the number that had existed
  only on glass (18273, reek); the pre-ladder band floor measured 516..519,
  so the shipped 500 was out of band, not on its edge (18059, 18061,
  L-ADJECTIVE 18069).
- **Sitting 7 flew and lost its payload at stage 9 (18462, red);** the
  pch-state stage that reads the part (18373, root; constants and citation
  status 18349, reek; the three model levers 18365) rode sitting 8, which
  **answered the K1 question: K1 is ENABLED on the board** (18546), so blu's
  layer is needed. K1 lands through the cited register 770.17 (18468), and
  its PLACEMENT is the finding, after bring-up because a PHY reset restores
  the NVM value (18484); the 0x0034 offset cannot be sourced and 770.17 can
  (18444); the mechanism arm (18441, reek); the kmrn row stops claiming a
  hole from a zero (18500, 18550, 18553, 18556).
- **The driver table closed (18898, red):** K1 required by default with
  kumeran deleted (18562), the MDIO/NVM semaphore built (18505) and shipping
  on, coupled to K1 (18565, 18567, 18569); ULP entry disabled by a cited
  write, gate off (18819), its reaching/skipping pair (18850), the 779.16 ULP
  control model (18837, reek); the LCD reload obligation paid on the ASDE
  path, gate still off (18632, 18794); the SMBus Control register read, no
  write (18883); ULP and Force SMBus are cited and modellable while a cited
  register is not a cited obligation (18420, 18426, reek); **LTR is
  uncitable from what we hold, measured and recorded (18888)**; what the
  datasheet says about resetting a live receiver (18862).
- **Sitting 9 flew (18720):** firmware holds MDIO ownership; the RING
  successor answered; the bed cannot express the nicring reading, GPRC
  placement (18729). **Sitting 10 flew (18856): the hang is inside
  `e1000-reset`, the first name it has had; K1 exonerated.** **Sitting 11
  flew (18980): all seven reset operations ran and banked, the sitting-10
  hang did not reproduce, and b3's whole TCP exchange completed over the real
  I219 with the dev box echoing 13 bytes; the medium died INSIDE b3's second
  bring-up**, narrowed by reek to the SWFLAG acquire or the CTRL|SLU write
  (19003, 19016). The K1 control that holds the part fixed (18874, reek) and
  `pchk1` listening after the K1 write (18948, root) ride sitting 12.

### The checked send and the NETIO ruling (blu)

- **The NETIO ceiling RULED (18406, 18663):** "tcp correctness is a working
  nic, not adherence to a standard"; cut the drain, the NIC comes first. The
  send drain gets its own budget below the give-up ladder and **b3 sends
  checked** (18685): the claim that b3 was not blind was false (18673,
  red's correction), and every record sender now stops on a record it could
  not finish: WebServer (18752), HttpFetch (18759), the repository wire
  (18763), the four app senders (18767), the tools (18782).
- The poll cell is clamped at both ends with a derived ceiling and a
  three-state arm (18389); b3 gets a send-repeat knob and `b3=short` its arm
  (18716, 18701); `ip=dhcp` is a named opt-in with a no-lease state and B3
  requires `ip=`, refusing with `no-address` instead of inventing a bed
  address (18186, 18166, L-BEDTRUE); the NIC-6 leased-gateway card (18190).
- **B3 as a diagnostic stage, with the codex-vm e1000 RDH fix and the ARP
  narrowing (18011); ASDE as stage 14 (18104), quiescing before it resets
  (18138)**; nicring's during-window GPRC rides the answer row (18307); the
  RDH discriminator gets its NO branch, `-e1000-rdh-ro` (18245).

### The bed models the part (reek, fester, blu)

- I219 (18335) and its second pair, the MDIO/NVM semaphore (18356); the
  NIC advertised an id it does not implement (18324); **GPRC counts where
  the MAC accepts the frame and the K1 stall rides STATUS.LU** (18747,
  fester); `-e1000-inject-armed` and the nicring-invisible/nic-armed pair
  (18822); `-e1000-rdh-ro` (18245); the 779.16 ULP model (18837).
- **Medium death, keyed to the thing under test:** `-usb-bot-die-len`
  (18853), the BOT census and `-usb-bot-die-lba` (18869), `-census` as a
  FILE beside each arm's output (18891), sink says `died` when the target
  stops answering (18932), `-usb-bot-revive-on-reset` and `sink-revived`,
  the arm where WORKS-9 recovery succeeds (18955, 18966), and
  `-usb-bot-die-on-nic`, keyed on the OTHER device (19018).
- PCI: `-pci-bridge-levels`, `-pci-bridge-backward` (18775),
  `-pci-bridge-deep` (18756), `PciScanResult` carries `truncated` (18791);
  the `xhci-two` arm and the two-controller bed's BAR default moved out of
  the RAM arena (18844); `-hpet-frozen`, the undecoded-window shape (18799,
  root); REP INSW batched from the IDE data port and `blit_guest_output`'s
  drops counted (17910, blu).

### The diag ladder: the stick banks as it goes (root, red, fester, blu)

- The ladder renumbered to 13 stages, gopmode 6, xhci 8, b3 13 (18048,
  18050, red); slot grant so non-picture stages can paint (18332); **bank
  loss painted when it is set, not at a summary a hang never reaches
  (18341)**; sink deferred last so a wedge stops eating the bank (18474);
  `pchk1` at 15 reads 770.17 back after the K1 write (18643); the build
  refuses a cfg leaving a risky stage unnamed and a key named twice (18574,
  18645); the hardware-returned stick images and their identity records
  ingested (18656, rulings 4); a cfg-off composition is rehearsable (18679).
- **Stages bank mid-run and b3 steps its bring-up through the driver's own
  functions (18799)**; a note APPENDS as bytes, never Text (18901); the
  reset step is seven banked operations (18928); **a step paints its own
  refused note and b3's row carries the ordinal of the first (19021)**;
  `rings-link` is the six parts of `e1000-init-after-reset` (19029); the
  SIZE payload leaves through the 0xE9 debug console (18871).
- `diag-arm` watches for END (35 minutes to 518 s, 18234), learns the
  subject's baseline and refuses when the subject moves under a run (18209),
  checks the pch stage (18396); `flash-usb` refuses an unrehearsed hash BY
  DEFAULT (18218, 18229); `check-shipping-images` refuses a diag image
  carrying a baked sitting config (18237); run build-diag AFTER a gate
  (18924). Sitting images rehearsed flash-ready: 18511, 18693, 18816, 18834,
  18936, 18946.
- **L-SHORT has a runner (17874 the lesson; 17910 blu, 18992 reek):**
  both harnesses read codex-vm's stderr instead of deleting it, and
  `test.ps1` / `bvt.ps1` compare LENGTH before rendering a codegen verdict
  (TRUNCATED, LENGTHS DIFFER).

### Compiler and gate (fester, root, blu)

- **`negate` on a Real was an integer negation at both widths on every lane
  (18612, 18629); closed and lifted to LESSONS as L-CONSTRUCT (18635).**
- The boot processor sets EFER.NXE instead of inheriting it (18577); the
  manufactured NX item closed (18593).
- **The gate compiles `codex/test` at last** (18302, 18262; the NOTHING
  COMPILES section deleted 18304); **`-Internal` is THE gate**, the text leg
  and app sweep conditional (18195); the gate says which fixed-point path it
  took (18083, 18091); a converged seed replaced one that was not a fixed
  point of its own source (18019, P-STAGE2); the ATA data phase uses REP
  OUTSW/INSW builtins (17970); trace-alloc reconnected (17934); the
  guard-page WALK arm (17869); check-doc-counts unconditional in the gate
  (18639); `check-errors` reports a dead slot as a moved diagnostic (18471);
  `check-test-compile` says why a chapter is dirty (18508); the xdiag cell
  checker admits the diag cells (19021).

### The CostModel registry, measured with the seed (blu)

- Fifteen builtin allocation classes measured and `bounded none` widened by
  three (18659); the predicate, conversion and raw-memory families (18921);
  the buffer family, and rule 8's 8x claim with it (18951); the proof terms
  are erased and the pointer family measured (18969); the real conversion
  family (18985); SIMD, every vector we produce boxed (18995); atomics, with
  **five broken sized-vector builtins recorded as unowned** (18999); the CPU
  reads and the rest of raw memory, the registry never consulted for a
  nullary builtin (19012); the effectful arm shape, port and MMIO (19025).
  Eight seed moves in the cycle; `fixed` stays unshipped, 143 of 264 rows
  still `unknown` at the head.

### Plugs close-out (reek, fester)

- **1.46, the match-guard sweep:** 33 plugs measured, 12 keep it (17985);
  then lua/ruby/php/d/scala/groovy (18057), clojure/julia/perl (18074),
  rust/haskell/go (18102), thirteen more (18115), ada (18161), pascal
  (18163), fortran (18224), the keep count corrected (18226); what is left
  is one blocker plus four closures, not eight plugs (18121, 18123,
  L-ADJECTIVE); recast as blocked with the runtime census (18804).
- **1.7 fortran closed (18725)** through function values, closures and
  partial application (18291), the lambda residue (18257), the
  value-position `when` payload binding (18492, 18517), literal patterns
  (18713) and `let` in expression position (18689). **1.36 winforms closed
  (18786).** 1.48 arm64 mov-elim peephole guarded (17980, 17982); 1.29
  (17987); 1.50/1.49/1.45/1.7 zig (17880); 1.45 re-measured stale (18607);
  1.52 csharp heap base (17953); 1.51/1.52 marked closed (18465); 1.56 wgsl
  and babbage let bindings (18744); babbage shelved to its own backlog
  (18812).
- **The zig heap (1.53, 1.53a, 1.54):** a reservation is not an allocation
  (18590, 18596), the reservation fix's peak-memory trade bounded (18598),
  the arena never freed (18600), `cx_heap` grows through `page_allocator`
  (18606). The plugs register swept to open items only, 4266 lines to 104
  (18531).

### Shell Refinement (val, fester)

- Campaign opened (18044), ownership settled (18064). **Stage 1**, the desk
  gets a typeface and the widget layer draws with it (18077, 18098, 18118);
  **stage 2**, icons on the launcher, sidebar, taskbar and the Files pane's
  type column, StatusBadge's three tags (18180, 18200, 18299, 18362);
  **stage 3**, all four adornments on by default (18402); **stage 4**, the
  desk click plays through the HDA controller, a caller-owned BDL (18559,
  18696); **stage 5**, settings that refuse instead of clamping and an
  appearance that survives a reboot (18437, 18478); **stage 6**,
  Accessibility armed with a census over all sixteen roles (18772). Every
  icon the desk names is a drawing, rasterized at the size it is shown
  (18916, 18943, 18975, 19008); the drop shadow is a ramp (18893).
- fester's text metrics under it: comp-text measures before it draws
  (18192), centres the cap band (18258), owns the fit (18285), the caret by
  the measured metric (18294); GopFont carries vertical metrics (18251); the
  glyph cell is the face's box (18241); a glyph bounded by its own advance
  (18379); paragraph leading measured off the glass, the recorded hypothesis
  refuted (18514, 18520); desk goldens get a runner and an arm that can fail
  (18127, 18134, 18175). Browser: the page scrolls and the chrome does not
  (17968); BROWSER-8's scroll indicator (18312, reverted 18381 for two red
  tests, relanded 18453); the parallel-block throw class closed (18314).

### The rest, by lane

- **HAL hardware crypto dispatch, step 1 (root):** designed (18958), built
  (18963): VirtioRng on VirtioBlk's transport, the `[Rng]` row, arms on the
  arm64 bed; rule 2's compile-time refusal arm (18990); steps 2 and 3
  blocked on a board crypto manual the tree does not hold.
- **ThreatModel closed and moved to Done (18706, fester)**; hardware crypto
  rehomed to the HAL design. **CrossLaneFilesystem complete (18310).** A8:
  the VT-x refusal fires at the launch (18370), the two bed arms get a runner
  (18486), **compare the compiled result against CODEX.CDX, gated and
  ablated (18680)**.
- **docs/Reference/LeanAndCodex.md, the Lean comparison (18910, red)**; the
  DevelopersGuide proof section (18913); the rulings queue became Decisions
  with eight technical calls made by red (17898), ruling 20 ruled (17885);
  Damian's standing rule on reports (18496).
- WORKS-44 closed, supersession is a fact kind (17919, val); WORKS-32
  closed (17962); Track D items 10 and 20 (17913, 17928, 17895, reek); the
  kernel metadata cell table completed (17918, red); BatteryReorg step 6 is a
  queue (18905); README gains a measured lines-of-code section (18584) and
  R-PROSE re-measured at 52,393 lines (18582); the plugs and val, fester,
  blu, root and diag registers swept of landed accounts (18531, 18543,
  18587, 18528, 18534).
