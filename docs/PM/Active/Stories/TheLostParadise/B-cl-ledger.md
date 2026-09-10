# Appendix B. The changelist ledger

*Owner: red. Appendix to `TheLostParadise.md`. Compiled 2026-09-09 from
`//Codex/main`. Charter rules 1 to 7 bind this file.*

## What the ledger holds

Every changelist on the main stream that touched one of the five metal paths,
in changelist order, from the first on 2026-05-07 to the last on 2026-09-09.
The set is the union of the five paths and holds **675 changelists**.

| tag | path | changelists on that path alone |
|---|---|---|
| K | `codex/os/kernel/...` | 134 |
| N | `codex/os/net/...` | 149 |
| B | `build/boot/...` | 196 |
| V | `tools/codex-vm.c` | 240 |
| H | `docs/Hardware/...` | 91 |

The five per-path figures sum to 810 and the union holds 675, therefore 135
changelist-path pairs are changelists that touched more than one metal path.
The tag column of each row names every metal path the changelist touched.

`seed/...` is deliberately absent from this ledger. The seed path carries 906
changelists, more than the five metal paths together, because every
seed-affecting compiler change lands there whether or not the change concerns
metal. A ledger including the seed path would be a ledger of the whole
project.

## How to read a row

Each row is a date, a changelist number, the metal paths the changelist
touched, and the opening of the changelist description truncated to 104
characters. Perforce holds the full description: `p4 describe -s <CL>`.

**Two alterations are made to the quoted descriptions and are declared here
rather than left for a reader to discover.** First, each description is
flattened to a single line, because a Perforce description carries newlines
and a table row cannot. Second, four descriptions contained an em-dash or an
en-dash and each such character is written `--`, under charter rule 6. No
other character is changed, and no row is omitted.

## The ledger by month

| month | metal changelists |
|---|---|
| 2026-05 | 48 |
| 2026-06 | 58 |
| 2026-07 | 178 |
| 2026-08 | 320 |
| 2026-09 | 71 |

August 2026 carries 320 of the 675, which is 47 percent of all metal work in
one month of the six the project ran.

## The ledger

| date | CL | paths | description (truncated to 104 characters) |
|---|---|---|---|
| 2026-05-07 | 1154 | V | feat: codex-vm -- WHP-based VM host, replaces QEMU for dev. Serial TCP + IDE + multiboot loader. |
| 2026-05-07 | 1160 | V | codex-vm serial fixes + harness integration |
| 2026-05-07 | 1162 | V | codex-vm PIT interrupt + DISK compile test |
| 2026-05-17 | 1592 | V | Switch default VM from QEMU to codex-vm across all build/test scripts. Rename qemu-config.ps1 to vm-conf |
| 2026-05-17 | 1597 | V | codex-vm: detect and fix WHP R10 register corruption across VM exits |
| 2026-05-17 | 1606 | V | codex-vm: shadow register file to protect against WHP GPR corruption on VM exits |
| 2026-05-18 | 1630 | KN | Repository restructure: 31 top-level dirs to 8. codex/ (compiler) -> codex/compiler/ codex.foreword + co |
| 2026-05-18 | 1632 | V | Fix codex-vm GPF on large serial transfers (self-compilation). The serial IRQ injection path in the main |
| 2026-05-18 | 1654 | V | Copying //Codex/RESTRUCTURE to main (//Codex/main) |
| 2026-05-18 | 1666 | V | Copying //Codex/RESTRUCTURE to main (//Codex/main) |
| 2026-05-18 | 1696 | V | Copy RESTRUCTURE to main: codex-vm ring buffer fix, NE2000 NIC, VGA display, keyboard/mouse, UEFI emulat |
| 2026-05-18 | 1788 | V | codex-vm: implement GOP (Graphics Output Protocol) for UEFI framebuffer display |
| 2026-05-18 | 1822 | V | Spark: fix keyboard input -- read port 0x60 directly instead of kernel buffer |
| 2026-05-18 | 1828 | K | Copying //Codex/CodexMagic to main (//Codex/main) |
| 2026-05-19 | 1838 | K | Copy up from MutableRecords: I/O rename, IR-CCE, symbol map, seed 2,165,888 bytes |
| 2026-05-21 | 1924 | KNV | Copy up from CodexMagic: TCP plug I/O, NE2000 driver, VM string I/O, plug build citePat fix, copy-up doc |
| 2026-05-21 | 1957 | K | Copy up from MutableRecords: Gap 4 USB install - block-select builtin, IdentityManager tuple fix, XHCI s |
| 2026-05-21 | 1964 | V | Copy up from CodexMagic: C# plug end-to-end (27/29), NE2000 ring fix, NAT shutdown, seed rebuild |
| 2026-05-22 | 1997 | K | copy up from //Codex/MutableRecords: UEFI boot fixes (GPT UTF-16, pre-zeroing, EfiLoaderCode), print-uni |
| 2026-05-23 | 2032 | NV | Copy up from CodexMagic CL 2029: Emitter Exodus plug chaining + ELF/DWARF plug + NE2000 word-DMA fix + c |
| 2026-05-23 | 2043 | V | Copy up from CodexMagic CL 2042: PE plug + IMG plug (GPT/FAT16/FAT32) + common build lib + codex-vm e_ph |
| 2026-05-23 | 2053 | KV | Copy up from MutableRecords: codex-vm hardware emulation, UVC driver, tests, docs codex-vm.c: UEFI firmw |
| 2026-05-23 | 2061 | KV | Copy up from MutableRecords: xHCI transfers, PC speaker, SMBIOS Xhci.codex: transfer ring management (wr |
| 2026-05-23 | 2070 | KV | Copy up from MutableRecords: docs refresh, codex-vm polish, tests, digests codex-vm.c: PIT mode register |
| 2026-05-23 | 2110 | V | Copy up from MutableRecords: plug fixes, debugger, editor undo, docs Plug emitters: Rust+JS Unicode esca |
| 2026-05-24 | 2283 | NV | copy up from CodexMagic: is-page-marker fix, Page->DbPage rename, TCP transport raw buffer, NAT backpres |
| 2026-05-24 | 2292 | V | copy up from CodexMagic: NAT TCP half-close, plug run.ps1 framed protocol (CL 2291) |
| 2026-05-25 | 2312 | V | copy-up from CodexMagic: vm shadow buffers (CL 2310) + plug emitter edits |
| 2026-05-25 | 2323 | V | copy-up from CodexMagic: vm graceful shutdown (CL 2321) + vm-config.ps1 |
| 2026-05-25 | 2328 | V | copy-up: vm shutdown watchdog (CL 2327) |
| 2026-05-25 | 2332 | V | copy-up: WHP mutex (CL 2331) |
| 2026-05-25 | 2337 | V | copy-up: remove watchdog (CL 2336) |
| 2026-05-25 | 2346 | V | copy-up: fix serial socket blocking (CL 2345) |
| 2026-05-25 | 2384 | V | Copying //Codex/MutableRecords to main (//Codex/main) |
| 2026-05-25 | 2402 | V | copy up from MutableRecords: fix codex-vm socket disconnect detection to prevent vid.sys heap corruption |
| 2026-05-25 | 2406 | V | copy up from RESTRUCTURE: VM serial removal + memory-mapped I/O (CL 2405) |
| 2026-05-25 | 2410 | V | copy up: codex-vm.exe + LSR input injection fix (CL 2409) |
| 2026-05-26 | 2428 | V | copy up from CodexMagic: codex-vm mmio output ring buffer + doorbell port |
| 2026-05-26 | 2453 | V | serial-removal: replace UART with MMIO output ring buffer |
| 2026-05-27 | 2542 | V | VM: defer HEAP: exit when ring buffer has unread data (REPL batch support) |
| 2026-05-27 | 2560 | NV | Copying //Codex/CodexMagic to main (//Codex/main) |
| 2026-05-29 | 2645 | V | Copying //Codex/CodexMagic to main (//Codex/main) |
| 2026-05-29 | 2741 | N | Copy up from CodexMagic: explorer DB web server (bare-metal, TCP, reads .db off disk) + pip-tree designe |
| 2026-05-30 | 2785 | N | Copy up from CodexMagic (CL 2781/2782): Accounts std-include (reusable user accounts/sessions; salted SH |
| 2026-05-30 | 2792 | N | Copy up from Mountain (CLs 2726, 2732, 2790): full-compiler C# emit + IRTextEmitter fix. 2726: IR text e |
| 2026-05-30 | 2810 | V | Copy up from CodexMagic (CL 2809): codex-vm durable disk writes (IDE WRITE SECTORS 0x30 + REP OUTSW data |
| 2026-05-30 | 2813 | N | Copy up from CodexMagic (CL 2812): Accounts durable persistence (save/load account table to disk sector; |
| 2026-05-31 | 2885 | V | copy-up from MutableRecords: interactive debugger for codex-vm (-debug/-break/-map, command shell, guest |
| 2026-06-01 | 2964 | V | Copy up from MutableRecords: close Bug 1 investigation (lambda-body-def miscompile, fixed by CL 2937), r |
| 2026-06-01 | 2981 | KN | cleanup: remove 609 unused cites across 246 files. Lint tool false positives (6 Tuple cites in compiler) |
| 2026-06-04 | 3114 | V | copy-up from CodexMagic: bench/ codegen battery, CodegenAnalysis.md, codex-vm crash diagnostics + ring b |
| 2026-06-08 | 3277 | N | copy-up from MutableRecords: Prismatic mana system, deck tester, CDX HTTP server, rules API, data migrat |
| 2026-06-08 | 3296 | N | copy-up: compile fixes -- remove stack field, light-cost in MagicServer JSON, net-io-send-raw, CodexMagi |
| 2026-06-08 | 3355 | V | copy-up from RESTRUCTURE: audio engine (synth, ADSR, delay, distortion, waveform, sheet music, music the |
| 2026-06-08 | 3456 | N | copy-up from Mountain: MathBook CAS (symbolic math, calculus, number theory, circuits, proofs, stats), N |
| 2026-06-08 | 3459 | N | copy-up from MutableRecords: 8 new web apps (chat, mail, music, notes, weather, tasks, photos, maps) + C |
| 2026-06-08 | 3463 | N | copy-up from Mountain: restore MathBook, NetTool, and mesh OS chapters deleted by CL 3459 |
| 2026-06-08 | 3584 | K | Copy up from //Codex/Browser: persistence layer (AppPersist, AppLog), all app persist modules, Vision ap |
| 2026-06-10 | 3657 | K | Copying //Codex/MutableRecords to main (//Codex/main) |
| 2026-06-10 | 3743 | V | copy-up from MutableRecords: ERP buildout Phases 0-3 + memory ceiling raise ERP (CLs 3706-3731): quire r |
| 2026-06-11 | 3767 | N | copy-up from RESTRUCTURE: whitespace cleanup CLs 3764 + 3766 -- collapse doubled blank lines (46 .codex) |
| 2026-06-13 | 4142 | V | copy-up blu: codex-vm whp_lock fix + backlog update |
| 2026-06-13 | 4154 | N | copy-up blu: text-slice fix + backlog cleanup |
| 2026-06-13 | 4186 | KN | copy-up from CodexMagic CL 4184: fancy types -- unit types (Frequency, Mass, Length, Angle, Speed, Force |
| 2026-06-14 | 4239 | KN | copy-up from CodexMagic CL 4237: bounded integers on game (TileMap, Inventory, SaveSlot, Netcode), AI (T |
| 2026-06-14 | 4280 | V | copy-up reek: ARM64 alloc-local __record-set mutation fix, serial I/O plug, spill slot bounds fix, NE2K  |
| 2026-06-16 | 4568 | V | copy-up blu: GPU-accelerated 3D engine demo, host-side triangle rasterizer, codex-vm GPU command ports |
| 2026-06-17 | 4645 | V | Copy up from Mountain: Globe demo (NASA texture, 16-thread GPU rasterizer, per-pixel lighting, atmospher |
| 2026-06-17 | 4657 | V | GPU texture upload from guest RAM + procedural terrain generator (Earth/Mars/random) with runtime planet |
| 2026-06-17 | 4660 | V | codex-vm: remove -texture CLI flag -- textures are now uploaded from guest RAM at runtime |
| 2026-06-17 | 4662 | V | GPU asset-load port: guest requests file read from host into guest RAM, GlobeDemo loads earth-texture.ra |
| 2026-06-17 | 4670 | V | copy-up fester: SMP Phase 1-2 (atomic builtins, LAPIC emulation, -smp flag, SIPI AP boot, per-core stack |
| 2026-06-17 | 4675 | V | GPU texture sampler: wrap U coordinate modularly instead of clamping -- fixes Pacific seam smear |
| 2026-06-18 | 4713 | KN | Copy up from CodexMagic: ARM64 UEFI boot pipeline -- boot-arm64.ps1, PE plug mode 2, Arm64PeWriter, kern |
| 2026-06-18 | 4786 | KN | copy-up from CodexMagic: IR emitter serializes record-field and sum-ctor data in inline type annotations |
| 2026-06-18 | 4794 | K | copy-up from Mountain: DiskFacts log compaction |
| 2026-06-18 | 4807 | V | copy-up from Mountain: codex-vm MMIO hole memory map split, BACKLOG kernel plan, log compaction |
| 2026-06-18 | 4816 | V | copy-up from MutableRecords: SMP Phase 2 -- opt-in per-core bootstrap (-smp N), boot guard, codex-vm con |
| 2026-06-19 | 4906 | V | copy-up: codex-vm graceful shutdown + demand-paged memory (BSOD fix) |
| 2026-06-20 | 4923 | V | copy-up: dynamic RSP from VM RAM size (CL 4921) -- programs run at any -mem |
| 2026-06-20 | 4933 | V | copy-up: demand-commit VM memory (CL 4931) |
| 2026-06-20 | 4984 | KN | copy-up from MutableRecords: bounds guards for raw byte parsing in OS networking (Ethernet, TCP, DHCP, D |
| 2026-06-20 | 4996 | K | copy-up from RESTRUCTURE: Sovereignty Phase 1 -- repo protocol persistence (kinds 30-38), source scanner |
| 2026-06-20 | 5002 | K | copy-up from CodexMagic: ARM64 codegen 17 bug fixes -- timeout root cause, local recycling, epilogue res |
| 2026-06-20 | 5019 | K | copy-up from CodexMagic: ARM64 runtime fixes -- list name mismatch, itoa zero, STP-pre print, 64-bit BAR |
| 2026-06-20 | 5035 | K | copy-up from CodexMagic: disable retarget optimization (corrupts MOVZ immediates), VirtioPci 64-bit BAR, |
| 2026-06-20 | 5049 | V | copy-up from MutableRecords: GUI OS M1 -- GuiShell app, GopBuf, codex-vm GOP fix, Render text wiring, de |
| 2026-06-20 | 5085 | V | copy-up from Mountain: test-cross.ps1 harness, RISC-V register clobber fix, VM mouse tracking fix |
| 2026-06-20 | 5113 | K | copy-up from CodexMagic: VirtIO buffer region + mmio-barrier fix for ARM64 UEFI |
| 2026-06-20 | 5143 | V | copy-up from MutableRecords: mouse fix (kbd qword stomp), MutWheel zero-alloc, notepad app, sidebar clic |
| 2026-06-20 | 5163 | V | copy-up from MutableRecords: working mouse (absolute coords via I/O ports), WHP coherency fix, all GUI O |
| 2026-06-20 | 5227 | K | copy-up from CodexMagic: international keyboard layouts (23 layouts), kernel Keyboard.codex rewrite with |
| 2026-06-20 | 5230 | K | copy-up from CodexMagic: Cyrillic (RU/UA/BG) and Greek (EL) keyboard layouts, caps lock for non-Latin sc |
| 2026-06-21 | 5510 | V | copy-up from MutableRecords: SMP boot for codex-vm (codegen + VM) |
| 2026-06-21 | 5532 | V | copy-up from MutableRecords: guios multi-resolution stride fix, calc/ImgTools layout, VM GOP page-align |
| 2026-06-22 | 5640 | N | copy-up from MutableRecords: External Auth Bridge -- JWT decoder, OAuth2+PKCE client, Google/Microsoft p |
| 2026-06-22 | 5646 | K | copy-up from MutableRecords: Keyboard RGB -- USB HID class driver, VIA/QMK protocol (13 RGB modes), Keyb |
| 2026-06-22 | 5735 | N | copy-up from //Codex/MutableRecords: cvmm build fix + type errors + compile.ps1 diagnostic + INT64_MIN i |
| 2026-06-22 | 5753 | V | remove Consolas font -- proprietary Microsoft font removed from font-disk.img, FontLoad.codex catalog (1 |
| 2026-06-24 | 5972 | K | copy-up from Mountain: ARM64 kernel drivers (Arm64Pci, VirtioNet, VirtioPci), Arm64PeWriter, arm64-web-s |
| 2026-06-27 | 6183 | KN | copy-up: ARM64 QEMU/OCI web server -- heap alignment, FPU enable, peek-16/poke-16, PCI BAR assignment, T |
| 2026-06-27 | 6255 | V | copy-up from reek: Codex Circuits EDA suite (62 chapters) + interactive Phase 1 + codex-vm GPU fixes Cir |
| 2026-06-28 | 6293 | V | copy-up: circuits v2 (CL 6291) - CanvasModel, menus, IO port mouse, 4-tier GPU depth, codex-vm debug fpr |
| 2026-06-28 | 6300 | V | codex-vm: fix serial EOF for no-input boots, fix UEFI PE load crash (from RESTRUCTURE CL 6299) |
| 2026-06-29 | 6371 | V | copy-up from val (CodexMagic): guios shell polish + codex-vm GUI before stream rename |
| 2026-06-30 | 6420 | V | copy up from val: codex-vm fault info on IF=0 halts Tooling only (codex-vm.c + .exe), no seed/compiler c |
| 2026-07-03 | 6765 | KN | Copy up from blu: BoundedSignatures campaign COMPLETE - CDX2051 66 -> 0, promoted warning -> error. Regi |
| 2026-07-04 | 7059 | V | copy up from val: fireworks USA 250 app (cinematic bare-metal demo over 5 US city skylines + [Device] PT |
| 2026-07-05 | 7162 | V | Copy up from blu: two independent wins. (1) CL 7130 - lower survey-check-mul default 200 to 40. Self-com |
| 2026-07-05 | 7172 | V | copy-up: codex-vm guest-armable watchpoints + demand-debug docs Standalone tooling improvement. codex-vm |
| 2026-07-05 | 7174 | V | copy-up: codex-vm TF single-step fix for the guest-armed page-watch |
| 2026-07-06 | 7176 | V | copy-up from blu 7175: codex-vm debug diagnostics (-r10dump, -watchall, -dumpmem; all flag-gated, defaul |
| 2026-07-06 | 7181 | V | copy up from val: codex-vm fireworks render features (gpu_fade_clear on I/O port 0x40E for long-exposure |
| 2026-07-07 | 7216 | V | Copy up from val: demand-paging hardening series (review of CL 7202). #PF handler only grows not-present |
| 2026-07-07 | 7218 | V | Copy up from val: sampling profiler + self-compile measurement (CL 7217). codex-vm now delivers timer in |
| 2026-07-07 | 7282 | V | Copy up from blu: WI-3 WCET slice - codex-vm -wcet observation mode (DR0-DR3 + TF stepping, no guest byt |
| 2026-07-08 | 7292 | BV | Copy up from fester: UEFI Option A boot - Codex boots on real UEFI hardware The keystone fix for the who |
| 2026-07-08 | 7313 | V | Copy up from blu: WCET plug audit + batch output path (7300-7312) - 7300: RISC-V plug WCET true accounti |
| 2026-07-08 | 7336 | K | Copy up from blu: capability stage 6 - identity key syscalls gated on Identity (CL 7335) key-load/key-ze |
| 2026-07-08 | 7346 | KBV | Copy up from fester: UEFI boot arc B1-B3.5 + capability-stage-7 process-test effect fixes. Boot: real PS |
| 2026-07-08 | 7362 | V | Copy up from blu: retire codex-vm legacy output-ring drain + doc roundup codex-vm no longer drains the r |
| 2026-07-09 | 7409 | KV | Copy up from blu: NIC TX batching (codex-vm batched REP OUTSW for the NE2K data port + Ne2k.codex send v |
| 2026-07-09 | 7419 | KN | Copy up from blu: dig 1 -Apps CDX2051 sweep + dig 12 closure (blu 7415, 7418) 24 CDX2051 silent-narrowin |
| 2026-07-13 | 7473 | V | copy-up from reek (CLs 7469, 7470): mouse click/drag fix + full-battery rule. InputSource: port 0xE1 (bu |
| 2026-07-13 | 7479 | V | copy-up from reek (CL 7478): headless UI test driver. codex-vm gains -mouse / -mouse-file (pointer timel |
| 2026-07-13 | 7481 | V | copy-up from reek (CL 7480): three bugs the new headless UI harness found on its first sweep of circuits |
| 2026-07-13 | 7497 | V | copy-up from reek (CL 7496): spark repaired, plus the codex-vm framebuffer-sync fix and spark's GUI batt |
| 2026-07-13 | 7532 | V | copy-up from reek (CLs 7505, 7511, 7514, 7517): the 3D stack comes back to life engine-demo and globe co |
| 2026-07-13 | 7545 | BV | copy-up from fester (CLs 7538, 7540, 7543, 7544): the boot and hardware arc The fester stream has been b |
| 2026-07-13 | 7598 | V | copy-up from blu (CL 7596): map the device gigabyte; recover the three dead board batteries; add build/b |
| 2026-07-13 | 7668 | V | copy-up from fester (CLs 7659, 7665, 7666): crypto AEADs, honest compliance evidence, boot arc CRYPTO (C |
| 2026-07-13 | 7676 | V | copy-up from blu (CLs 7649, 7656, 7675): SMP 4.3 CLOSED -- real AP bring-up. No application processor ha |
| 2026-07-13 | 7683 | K | copy-up from reek (CLs 7637-7681): apps/works goes from 56 to 77 of 83 chapters compiling apps/works had |
| 2026-07-13 | 7705 | V | copy-up from blu (CLs 7703, 7704): BACKLOG 4.4 CLOSED -- an AP fault is diagnosable. Two fixes: per-core |
| 2026-07-13 | 7770 | N | copy-up from fester (CL 7769): DTLS D3 endpoint - a full handshake completes over datagrams. Adds codex/ |
| 2026-07-13 | 7777 | V | copy-up from reek (CLs 7750-7775): apps/works compile campaign, BACKLOG 2.13 and 2.21 closed, seed 895FD |
| 2026-07-13 | 7785 | KN | copy-up from val (CLs 7721, 7773, 7784): the network capability stops being a decoration 7721 -- the pha |
| 2026-07-13 | 7812 | N | copy-up from fester (CL 7810): x509 A5 - wire peer auth into DtlsEndpoint. BACKLOG 5.9 CLOSED. The DTLS  |
| 2026-07-14 | 7847 | NV | copy-up from val (CL 7846): BACKLOG 1.7 closed - the Network effect has an implementation. fetch/post/re |
| 2026-07-14 | 7853 | N | copy-up from val (CL 7852): network follow-ons. (1) NetIO's net-io-send-chunk rebuilt TcpTransportState  |
| 2026-07-14 | 7866 | N | copy-up from val (CL 7865): BACKLOG 2.0 closed - a record literal must name every field. CDX2006 (missin |
| 2026-07-14 | 7884 | N | copy-up from blu (CLs 7881, 7882): fix two CRITICAL security findings from the 24h review (BigLog.md). 6 |
| 2026-07-14 | 7953 | K | copy-up from val (CL 7952): the fact-log replay truncated the store at the first fact bigger than one se |
| 2026-07-14 | 7970 | V | copy-up from blu (CL 7969): BACKLOG 4.11(a) - an application processor is preempted. Per-core LAPIC time |
| 2026-07-14 | 7983 | K | copy-up from val (CL 7979): compaction kept one fact per KIND. It would have eaten the repository. disk- |
| 2026-07-14 | 8036 | V | copy-up from blu (CL 8035): BACKLOG 4.11(d) idle cores halt -- hlt in __idle_dispatch + idle-stack ISR g |
| 2026-07-14 | 8065 | K | copy-up from val (CL 8058): BACKLOG 6.1(b1) the on-disk fact-log layout has one definition. Foreword Fac |
| 2026-07-14 | 8067 | K | copy-up from val (CL 8066): BACKLOG 6.1(c) removal via tombstones. repo-persist-save-tombstone + tombsto |
| 2026-07-14 | 8089 | N | copy-up: BACKLOG 6.1(g) the fact-sync wire verb -- SyncOffer/SyncReply AgentMessages over TrustTransport |
| 2026-07-14 | 8100 | N | copy up from fester (CL 8099): BACKLOG 1.14 kernel filesystem servicer + battery test-fix sweep. Seed re |
| 2026-07-14 | 8105 | K | copy-up: BACKLOG 6.1(f) persist the WorkIndex -- kind-40 index snapshot carries the whole materialized i |
| 2026-07-15 | 8202 | N | BACKLOG 5.10: DTLS handshake Random is caller entropy, not the endpoint public key (copy up fester 8201) |
| 2026-07-15 | 8232 | N | copy-up from val: 1.5 network scope runtime ring (network authority enforced at the fetch chokepoint; bo |
| 2026-07-15 | 8318 | K | copy-up: BACKLOG 2.21 Display ops performable - DisplayOps kernel chapter paints GOP framebuffer, draw-t |
| 2026-07-15 | 8329 | K | copy-up: BACKLOG 2.21 Display full-colour palette - fill/fill-rect/write-text/write-text-font over hoist |
| 2026-07-15 | 8348 | K | copy-up: audio output - HdaAudio driver plays PCM to the speakers via Intel HDA (the machine's first sou |
| 2026-07-15 | 8357 | K | copy-up: 1.8 migration de-exempt Kernel quire (grounds Device.Port/Block on 8 drivers, Ne2k act-convert) |
| 2026-07-15 | 8360 | N | copy-up from blu (BACKLOG 1.9): TLS 1.3 client+server (TlsEndpoint) over the record layer, Ed25519 X.509 |
| 2026-07-15 | 8367 | KV | copy-up: microphone input - codex-vm waveIn capture + HDA input stream, Microphone effect (listen/is-qui |
| 2026-07-15 | 8374 | N | copy-up: 1.8 migration FINAL - de-exempt Net quire (last OS quire). codex/os fully effect-enforced. Fixe |
| 2026-07-16 | 8405 | N | copy-up from blu (BACKLOG 5.2(3)): wire the LwM2M Object 5 flow. Net chapter Lwm2mFirmware joins OtaUpda |
| 2026-07-16 | 8419 | N | copy-up from blu: BACKLOG 5.13 + 5.14 CLOSED (fixed, not filed). 5.13 -- OtaUpdate.gate-a-verify-block h |
| 2026-07-16 | 8427 | K | copy-up from fester (BACKLOG 7.15 + 7.7): a program can write into a directory, and make one. fat16-crea |
| 2026-07-16 | 8452 | NBV | p4 filetype: the 216 files that contain non-ASCII but were typed text. Perforce types a file by its cont |
| 2026-07-16 | 8519 | K | 5.16: Audio is a least-authority capability - HdaAudio grounds Device.Mmio and publishes [Audio]; seed |
| 2026-07-16 | 8539 | K | 2.24 step 1: delete 518 ghost 'cites Codex chapter X' lines - documentation written with a load-bearing  |
| 2026-07-16 | 8581 | K | copy-up from val (CL 8580): BACKLOG 6.1 ingest polish -- pack-fact-entry reads FactLog's offsets instead |
| 2026-07-16 | 8613 | K | copy-up from val (CL 8612): BACKLOG 6.5 first cut - the archive base. A signed, compressed copy of a ran |
| 2026-07-16 | 8629 | K | copy-up from val (CL 8628): AppPersist asks FactLog where the fields are. It carried its own copy of the |
| 2026-07-16 | 8633 | K | copy-up from fester (CL 8632): BACKLOG 4.14 - the Lapic chapter exists vga-terminal-demo cited Kernel ch |
| 2026-07-16 | 8643 | KV | copy-up from fester (CL 8642): BACKLOG 4.15 - codex-vm decodes the MMIO operand instead of guessing it T |
| 2026-07-16 | 8681 | N | copy-up from val (CL 8680): BACKLOG 6.2 server - a peer can ask for one work by hash. Nothing in the tre |
| 2026-07-16 | 8710 | K | copy-up from fester (CL 8709): BACKLOG 7.14 - run the identity path, and find two bugs in it IdentityMan |
| 2026-07-16 | 8741 | K | copy-up: 7.14 the ceremony reads a passphrase; 7.14b the .keys sidecar; four orphaned test sidecars remo |
| 2026-07-17 | 8778 | KNB | Perforce filetype standardization: text/utf8 -> unicode. The depot had four filetypes doing one job: 426 |
| 2026-07-17 | 8801 | V | copy-up from fester (CL 8800): codex-vm audit -- crash/hang/UAF fixes (triple-fault reason-4 spin now CR |
| 2026-07-17 | 8840 | N | copy-up: 7.2 residual defects fixed and un-skipped. classics/task-queue/http-status-page had fabricated  |
| 2026-07-17 | 8884 | K | BACKLOG 4.15: xHCI command ring delivers events (Xhci.codex event-ring window at RTSOFF, CRCR RCS matche |
| 2026-07-18 | 8951 | V | copy-up from fester (CLs 8897, 8949): BACKLOG 4.16/4.17/4.18 codex-vm bundle -- COM3 compute bridge, NAT |
| 2026-07-18 | 8972 | V | copy-up from fester (CL 8971): BACKLOG 4.18 finished -- __interrupt_common records every non-timer vecto |
| 2026-07-18 | 8991 | KV | copy-up from fester (CL 8990): BACKLOG 4.15 xHCI endpoint-zero data path. Driver builds the input contex |
| 2026-07-18 | 9011 | KV | copy-up from fester (CL 9010): BACKLOG 4.15 xHCI bulk endpoints. CONFIGURE_ENDPOINT copies contexts; dri |
| 2026-07-18 | 9027 | KV | copy-up from fester (CL 9026): BACKLOG 4.15 closed. Interrupt endpoints, EVALUATE_CONTEXT context copy,  |
| 2026-07-18 | 9034 | KV | copy-up from fester (CL 9033): BACKLOG 4.13 attacked from the emulator side. codex-vm refuses to service |
| 2026-07-18 | 9080 | KV | copy-up from fester (CL 9079): BACKLOG 4.20 closed. The kernel USB mass-storage driver reads real sector |
| 2026-07-18 | 9126 | V | copy-up from fester (CL 9124): BACKLOG 4.13 -- model a high-speed hub and its transaction translator in  |
| 2026-07-18 | 9147 | KV | copy-up: BACKLOG 4.21 closed (two-tier hub transaction translator, codex-vm second hub tier, usb-kbd-hub |
| 2026-07-18 | 9164 | V | copy-up: BACKLOG 4.10 isochronous endpoint transfers -- GopUsbCam reads a UVC frame over Isoch IN, codex |
| 2026-07-18 | 9172 | K | Copy up from blu: ASCII character codes used where CCE was meant. CCE space is 2 not 32, digits are 3..1 |
| 2026-07-18 | 9180 | K | copy-up: retire the duplicate kernel USB stack -- Xhci/UsbMassStorage/UsbVideo and their 11 tests delete |
| 2026-07-18 | 9245 | N | BACKLOG 2.2, 2.3 and 2.24's dead cite-loader 2.3 -- the plugs share one transport instead of shadowing i |
| 2026-07-19 | 9498 | N | BACKLOG 4.12: three codex/os/net chapters compile. ImapClient had twelve CDX0006 carriage returns in com |
| 2026-07-20 | 9552 | N | copy-up: WebServer/Accounts unblock and 18 units closed (fester CL 9551). Sweep 213 to 228 clean entry u |
| 2026-07-20 | 9573 | N | copy-up: two cross-chapter name collisions, Accounts disk effects, class-D bound (fester CL 9572). Sweep |
| 2026-07-20 | 9588 | N | copy-up: ExplorerServer, AuthDemo, ChatServer, BridgeWebPage closed (fester CL 9587). Sweep 239 to 243 c |
| 2026-07-20 | 9592 | N | copy up from blu: 2.32 codex/os cluster -- every counted scope now at zero collisions |
| 2026-07-20 | 9710 | N | Copy up from val: BACKLOG 4.20(d) -- a listener forgets its peer, so a server can take a second caller.  |
| 2026-07-20 | 9743 | N | Copy up from val: BACKLOG 4.20(c) parked-frame pool (mechanism close). New codex/os/net/ParkedFrames + n |
| 2026-07-20 | 9755 | N | Copy up from val: BACKLOG 4.20 concurrent event-loop WebServer (web-serve-concurrent + web-mux-concurren |
| 2026-07-20 | 9759 | N | Copy up from val: BACKLOG 4.20 webserver request reassembly (Content-Length bodies, split requests) + TC |
| 2026-07-20 | 9775 | N | Copy up from val: BACKLOG 4.20 webserver connection lifecycle -- keep-alive plus reap-on-close (web-conn |
| 2026-07-20 | 9796 | N | BACKLOG 4.16 + 4.20 closed: TCP retransmission/TIME_WAIT aging/receive-window validation and WebServer i |
| 2026-07-20 | 9817 | KV | copy-up CLs 9814/9815/9816: 7.8 C64 SID (resonant filter, ring mod, hard sync, register routing, in-loop |
| 2026-07-20 | 9864 | V | copy-up: codex-vm proper TCP on the port-forward (retransmit un-ACKed SYN/data, exponential SYN backoff, |
| 2026-07-20 | 9912 | V | copy-up: codex-vm resolves the MMIO device by gpa before decoding the instruction |
| 2026-07-20 | 9921 | NV | copy-up: BACKLOG 6.2 -- a quotation resolves from a peer nobody named. Registry tier (cdx-registry, cdx- |
| 2026-07-21 | 9993 | V | copy-up: deterministic clock. codex-vm -rtc freezes the emulated RTC, test-gui.ps1 gains a uiscript vmar |
| 2026-07-21 | 10054 | V | copy-up: BACKLOG 4.8 closed -- NIC RX batched (one VM exit per frame, was one per word), plus nic-recv c |
| 2026-07-21 | 10117 | KN | copy-up: BACKLOG 2.32 closed. Nine names across five codex/os chapters take a chapter-specific prefix so |
| 2026-07-21 | 10148 | KN | copy-up: source stops citing the backlog and the design docs by name (230 files), and BACKLOG 3.24 is de |
| 2026-07-22 | 10174 | KN | copy-up: NIC driver seam in NetIO, PCI bus-master enable, pci-bus-master test. |
| 2026-07-22 | 10200 | KV | copy-up: BACKLOG 4.6 GPU compute doorbell -- 24,640 VM exits to 4 for a 32x32 matmul. |
| 2026-07-22 | 10213 | KV | copy-up: BACKLOG 4.6 closed -- GPU bridge callers on the doorbell, serial path deleted. |
| 2026-07-22 | 10216 | N | copy-up: BACKLOG 5.8 partial. TLS and DTLS clients fold Certificate and CertificateVerify into the trans |
| 2026-07-22 | 10227 | KV | copy-up: BACKLOG 4.17 -- compute bridge serves 13 of 17 operations. |
| 2026-07-22 | 10230 | N | copy-up: BACKLOG 5.8. The generic TLS 1.3 transcript and Finished move out of Encode chapter DtlsMessage |
| 2026-07-22 | 10243 | KV | copy-up: BACKLOG 4.17 -- the compute bridge reaches a real GPU. gpu-op-launch-ptx JITs guest PTX through |
| 2026-07-22 | 10256 | NV | copy-up: BACKLOG 5.8. A real TLS 1.3 handshake against OpenSSL 3.0.13 -- tools/tls-serve.codex (a TLS se |
| 2026-07-22 | 10259 | V | copy-up: BACKLOG 4.17 -- matmul runs on the GPU via an embedded PTX kernel, DEFAULT OFF because it was m |
| 2026-07-22 | 10266 | V | copy-up: BACKLOG 4.18/4.19 real-mode AP trampoline + proc 0 pinned to boot processor (fester CL 10265).  |
| 2026-07-22 | 10287 | N | copy-up: BACKLOG 5.8 relocate TlsEndpoint to foreword/encode (foreword-accessible TLS 1.3 handshake). No |
| 2026-07-22 | 10296 | KV | copy-up: BACKLOG 4.17 -- conv1d, max-pool, clamp served by the compute bridge (no seed) |
| 2026-07-22 | 10314 | KV | copy-up: BACKLOG 4.17 -- conv2d served via its own extended header (no seed) |
| 2026-07-22 | 10323 | N | copy-up: DTLS 1.3 application data in DtlsEndpoint + dtls-app-loopback test. No seed. |
| 2026-07-22 | 10338 | KV | copy-up: BACKLOG 4.17 -- device matmul pays and auto-selects; COM3 region committed, cap raised (no seed |
| 2026-07-22 | 10341 | N | copy-up: DTLS 1.3 epoch-2 handshake protection + test. No seed. |
| 2026-07-22 | 10384 | NV | copy-up: BACKLOG 5.4 CLOSED -- the codecs are live endpoints. General UDP forwarding in codex-vm (gatewa |
| 2026-07-22 | 10409 | NV | copy-up: BACKLOG 5.15 -- MQTT QoS 2 end to end against mosquitto; mqtts (MQTT over TLS 1.3) with the mis |
| 2026-07-26 | 10520 | N | copy-up: blu 10519 to main -- the https refusal names fetch-tls instead of claiming TLS is unimplemented |
| 2026-07-26 | 10543 | N | copy-up: blu 10542 to main -- TLS/DTLS peer identity. subjectAltName parsing, RFC 6125 matching with no  |
| 2026-07-26 | 10546 | K | copy-up: fester 10545 to main -- keyboard: poll-key speaks CCE, named keys leave the character space The |
| 2026-07-26 | 10569 | N | copy-up: blu 10568 to main -- X509 reads RSA keys and the real signatureAlgorithm OID. codex/test/real-c |
| 2026-07-26 | 10603 | V | copy-up: codex-vm commits guest RAM before a host-side store; foreign GGUF read closed (fester 10602) |
| 2026-07-27 | 10656 | N | Copy up from blu 10655: fetch-tls meets a server we did not write, and the FIN it could not see |
| 2026-07-27 | 10756 | V | copy-up: block-select actually selects a drive, and codex-vm has a second one. The three cells block-sel |
| 2026-07-27 | 10761 | K | Copy up from blu 10760: removes the 22 dangling BACKLOG references from source prose. BACKLOG.md was del |
| 2026-07-27 | 10782 | K | copy-up: Codex installs Codex onto a second drive. Boot image, seed and source text all cross intact and |
| 2026-07-27 | 10909 | V | Copy up from fester 10908: the UEFI dev console boots. LSTAR never programmed in the PE stub, codex-vm's |
| 2026-07-27 | 10944 | K | Copy up from fester 10942: DiskFacts refuses to eat a partition table. One disk-write-fact turned a boot |
| 2026-07-27 | 10989 | K | Copy up from fester 10986: the fact store gets its own partition, and addresses inside it. Relative sect |
| 2026-07-27 | 11040 | B | Copy up from fester 11038: gap 7, syntax highlighting in GUI mode. Works chapter SyntaxHighlight classif |
| 2026-07-27 | 11210 | V | copy-up: codex-vm -hbreak, a conditional breakpoint through the debug registers that can actually discri |
| 2026-07-28 | 11490 | K | copy-up: IdentityManager salt/IV derivation fix with its test, and the CRA/IEC mapping docs reconciled w |
| 2026-07-28 | 11500 | V | copy-up: emit-ir-cce streams def-by-def via print-text + VM capture/input caps (+ seed; red 11499). CL 2 |
| 2026-07-28 | 11508 | BV | copy-up: rule 11 dash removal, non-.codex half (blu 11506). 559 files swept, 8870 em-dashes and 109 en-d |
| 2026-07-28 | 11522 | N | copy-up: rule 11 dash removal, .codex half (blu 11521). 264 files, 745 em-dashes in prose, comments, Sec |
| 2026-07-28 | 11779 | B | copy-up from //Codex/fester: the QR telemetry decoder was broken and now has a runner; the R6 hardware s |
| 2026-07-29 | 11949 | N | copy-up: net -- heap guard in net-io-poll-one, and finish the NetDriver seam in HttpFetch and WebServer  |
| 2026-07-29 | 11952 | K | copy-up: B2 e1000e driver (hardware-untested) + Damian's two rulings |
| 2026-07-29 | 11975 | K | copy-up: B2 -- refuse a BAR outside the mapped device window; raw-integer poll entry for the stack |
| 2026-07-29 | 11977 | B | copy-up: PCI probe (PciProbe.codex) -- walks bus 0 and every bridge below it, reads out vendor/device ID |
| 2026-07-29 | 11983 | B | copy-up: PCI probe MAP= severity correction (BELOW3G is the dangerous one, it aliases the heap arena), O |
| 2026-07-29 | 12005 | B | copy-up from //Codex/val: SceneProbe, real-firmware 3D verdict, corrected OVMF input finding (12002, 120 |
| 2026-07-29 | 12013 | N | NetDriver: carry either NIC, defaulting to the NE2000. B3 bind-up. The seam can now reach red's e1000e.  |
| 2026-07-29 | 12032 | B | copy-up: xHCI BAR verdict readable on real firmware (verdict 1/2/0, high dword at diag 32), truth probe  |
| 2026-07-29 | 12056 | B | copy-up: test-ovmf workspace isolation (the gate could boot another agent's image) + PciProbe adaptive l |
| 2026-07-29 | 12058 | B | copy-up: diag README -- pass -Source '', and gate the file you are about to flash (fester CL 12057). |
| 2026-07-29 | 12067 | B | copy-up: MscAlignProbe -- a calibrated probe for the 64 KB TRB boundary, plus its README section and the |
| 2026-07-29 | 12070 | N | NetDriver: answer the bound card's own station address. B4. net-driver-mac answers the MAC of whatever c |
| 2026-07-29 | 12073 | B | copy-up: option_a_stub liveness marks + HardwareSitting colour-first boot-1 table (fester CL 12071). |
| 2026-07-29 | 12075 | B | copy-up: diag README -- layout prose corrected for the adaptive QR/list split, liveness marks documented |
| 2026-07-29 | 12078 | K | copy-up: B2 -- the chapter claimed the empty poll allocates nothing, and it allocates 32 bytes blu measu |
| 2026-07-29 | 12081 | KV | copy-up: Device Emulation Catalog entry 1 -- the e1000 model, its two tests, and the defect it found Car |
| 2026-07-29 | 12115 | B | Inventory.codex: rung 1 of the attempt-2 ladder, built and OVMF-gated Red's spec, docs/HardwareSitting.m |
| 2026-07-29 | 12134 | N | NetDriver: the e1000 bring-up call, card-independent and untuned. B3. net-driver-bring-up is the one cal |
| 2026-07-29 | 12142 | B | Inventory: the codes now carry the PS/2 answer, and top and budget are derived Item 2a, red's review of  |
| 2026-07-29 | 12147 | KN | Pci: pci-scan-all walks the bus tree, and net-driver-bring-up uses it. Fixes red's defect in CL 12134. R |
| 2026-07-29 | 12193 | V | codex-vm: -gop-stride padded-scanline bed, plus codex/test/gop-padded-stride proving the option reaches  |
| 2026-07-29 | 12201 | KB | copy-up: xhci-connect enumerates EVERY xHCI controller; a controller nobody opened now says NEVER-OPENED |
| 2026-07-29 | 12209 | B | copy-up: boot 3 liveness colours in cdx-to-pe stub, -MonCmds in test-ovmf (fester 12200) |
| 2026-07-29 | 12216 | V | copy-up: codex-vm padded-stride deltas on val's base, plus the catalog gap-table corrections Two defects |
| 2026-07-29 | 12219 | B | copy-up: seed/Codex.img with liveness colours, pinned kernel, validate-img can fail (fester 12217) |
| 2026-07-29 | 12236 | B | copy-up: HID endpoint reading -- descriptor asked beside programmed, on the glass and in the QR body. re |
| 2026-07-30 | 12283 | B | copy-up: xhci-diag moves off the PML4 page, 36200 -> 118784 (0x1D000). Red 12281. Closes reek's R-e alia |
| 2026-07-30 | 12299 | V | copy-up: codex-vm -e1000-nat wires the e1000 model to the NAT; cdx-serve converses over the e1000 branch |
| 2026-07-30 | 12319 | NV | copy-up: DHCP acquisition -- codex-vm NAT answers DHCP (it never did, the manual said it did), DhcpIO pu |
| 2026-07-30 | 12326 | KV | copy-up: MDIC, the PHY path the model did not emulate. Red 12322 + 12325. The I219 is a PCH-integrated M |
| 2026-07-30 | 12329 | KNV | copy-up: DHCP lease renewal, the HPET monotonic clock it needed, and -dhcp-lease so the renewal is obser |
| 2026-07-30 | 12335 | B | copy-up: Python out of the OVMF gate (ppm2png.ps1), stub and 0x8000 ruled in BootRoadmap (fester 12333) |
| 2026-07-30 | 12371 | KV | copy-up: xHCI periodic values by VALUE, XUSB2PR routing model, and the pci-read-config sign extension th |
| 2026-07-30 | 12375 | V | copy-up: xHCI Protocol Speed IDs -- the speed-class assumption is a defect, and under PSI dwords a hub-a |
| 2026-07-30 | 12392 | KV | copy-up: CORRECTION -- codex-vm sign-extended every 32-bit port read; the Codex side was right, my Pci.c |
| 2026-07-31 | 12509 | B | copy-up: reek takes the keyboard -- the second-xHCI bed and xhci-reloc-base. One fixed relocation consta |
| 2026-07-31 | 12519 | V | copy-up: second-xHCI bed and the keyboard fix. Two xHCIs relocating onto one xhci-reloc-base is the defe |
| 2026-08-01 | 12522 | B | copy-up: KbdDiagProbe v10 -- HOST id, ATTR attribution, BAR verdict, derived QR scale (red 12521) |
| 2026-08-01 | 12553 | B | copy-up: B5.4 step 4 part one -- dead A1 toolchain deleted; the asm is HELD because six diag probes stil |
| 2026-08-01 | 12560 | B | copy-up: all six diag probes read the 0x1F000 handoff block with a 0x8000 fallback; step 4's prerequisit |
| 2026-08-01 | 12562 | B | copy-up: B5.4 step 4 -- option_a_stub.asm deleted, build-option-a.ps1 delegates to cdx-to-pe.ps1; MSVC o |
| 2026-08-02 | 12580 | B | copy-up: CurrentPlan/HardwareSitting reconciled against source after B5.4 step 4; the release step table |
| 2026-08-02 | 12588 | BV | copy-up: codex-vm GOP_MAX_H 768 -> 1200 so the padded-stride bed can run the ASUS panel it was built for |
| 2026-08-02 | 12590 | B | copy-up: map the device gigabyte cache-disabled (PCD). MMIO and the UEFI framebuffer were write-back cac |
| 2026-08-02 | 12593 | BV | copy-up: TheKeyboardWasNeverSilent -- the keyboard conclusion. uefi-read-key-ex returns a NEGATIVE value |
| 2026-08-02 | 12600 | B | copy-up: the keyboard. uefi-read-key-ex encoded SUCCESS as a negative number (EFI_SHIFT_STATE_VALID at b |
| 2026-08-02 | 12616 | BV | copy-up: display cause found (stub read GOP geometry before ConOut activation re-mode) + fix; -ExitBootS |
| 2026-08-02 | 12618 | BV | copy-up: v11 flight recorded (display fix confirmed on metal; est=1 signature; PS/2 fallback proven; fir |
| 2026-08-03 | 12622 | BV | Copy up from red 12621: kbd-diag v13 (STOPX Stop Endpoint experiment), codex-vm endpoint-command arms +  |
| 2026-08-03 | 12624 | BV | Copy up from red 12623: xHCI spec + ServiceModel notes land in docs/Reference; four spec violations fixe |
| 2026-08-03 | 12626 | V | Copy up from red 12625: codex-vm -hid-nak arm (reproduces the ASUS silent-keyboard machine + the v14 met |
| 2026-08-03 | 12628 | BV | Copy up from red 12627: kbd-diag v15 (OOM fix + SET_IDLE removal + DEVX), USB2/HID specs land, codex-vm  |
| 2026-08-03 | 12630 | B | Copy up from red 12629: kbd-diag v16 (experiments gated on dead pipe, QR clear, short phases). v15 fligh |
| 2026-08-03 | 12632 | B | Copy up from red 12631: HardwareBringUpPlaybook + victory photos; kbd-diag-v16.img distributed with dige |
| 2026-08-03 | 12634 | V | Copy up from red 12633: codex-vm never raises a modal crash dialog (unattended battery hang fix). |
| 2026-08-03 | 12640 | V | Copy up from red 12639: codex-vm decodes page-straddling MMIO instructions. This was the smp-affinity fa |
| 2026-08-03 | 12642 | B | Release artifacts for the cycle: seed/Codex.map refreshed against the release seed (the -Repl seed build |
| 2026-08-03 | 12659 | B | Copy up from red 12658: option_a_stub.asm is retired, not deleted. Five docs corrected. Kept because the |
| 2026-08-03 | 12683 | B | copy-up: A4a -- the MSC bring-up says WHICH of six ways it failed. Ladder in xdiag 70-73 (furthest rung, |
| 2026-08-03 | 12692 | B | copy-up: the console re-mode cure gets a runner (build/boot/test-conout-remode.ps1 + GeoTruth), and thre |
| 2026-08-03 | 12708 | V | copy-up: fester C1.0 -- DDC pipeline audits the wrong binary and cannot run; codex-vm RAM cap makes comp |
| 2026-08-03 | 12723 | B | copy-up: retire the legacy asm stub (option_a_stub.asm + build-option-a-legacy.ps1 deleted, GopHandoff l |
| 2026-08-03 | 12730 | B | copy-up: A4a -- mass storage sent a hardcoded SET_CONFIGURATION(1). Found on the ASUS at rung=2 SET-CONF |
| 2026-08-03 | 12783 | B | copy-up: A4a -- rung 2 now says WHICH refusal and where the device was. The ASUS answered cfgv=1 rung=2, |
| 2026-08-03 | 12832 | V | copy-up: codex-vm gains three USB device-model gaps so our own bed can refuse. -usb-cfgval N makes the s |
| 2026-08-03 | 12858 | BV | copy-up: a wide bus, seen. Index 46 is a bitmask of which root ports report a device (a count named none |
| 2026-08-03 | 12865 | V | copy-up: codex-vm BOT gains the power-on UNIT ATTENTION, closing the gap reek opened 2026-07-29. A recog |
| 2026-08-03 | 12877 | BV | copy-up: would a retry have worked? A refused SET_CONFIGURATION is asked a second time and the answer re |
| 2026-08-04 | 12920 | KV | copy-up: the MDIO window arm and the 10 ms settle the I219 requires (B2b). -e1000-mdio-window in codex-v |
| 2026-08-04 | 12935 | BV | copy-up: retry cell renders the no-event state, codex-vm halts EP0 on a transaction error, boot 4 readin |
| 2026-08-04 | 12963 | K | copy-up: E1000e prose moved to an annotation sidecar, three false blocks corrected there. 107 prose line |
| 2026-08-04 | 12980 | KV | copy-up: B2b findings 2 and 3, PHY paging and MDIO slow mode. -e1000-mdio-slow in codex-vm (off by defau |
| 2026-08-04 | 13019 | B | copy-up: ASDE stage integrated onto msc-align.img, rebuilt and re-gated (reek 13017) |
| 2026-08-04 | 13029 | B | copy-up: A4a blocker closed on metal, liveness arm fixed, ASDE breadcrumbs (reek 13027) |
| 2026-08-04 | 13096 | V | copy-up: the keyboard fix (bind every boot keyboard + raw sibling listeners), the -hid-root-silent / -hi |
| 2026-08-04 | 13103 | V | copy-up: per-interface HID classification -- every boot mouse bound (combo devices reachable), um-pos sh |
| 2026-08-04 | 13111 | V | copy-up: F12 screenshots to the stick (GopShot + multi-cluster FAT16 writer), pane hotkeys, fat-write-bi |
| 2026-08-05 | 13124 | K | copy-up: annotation extraction, Os quire part 3 kernel (193 blocks audited, 447 prose lines cut, 62 side |
| 2026-08-05 | 13126 | N | copy-up: annotation extraction, Os quire part 4 net (229 blocks audited, 756 prose lines cut, 125 sideca |
| 2026-08-05 | 13133 | V | copy-up: xHCI completion latch keyed by (slot, DCI); every waiter passes its endpoint. The flight-2 defe |
| 2026-08-05 | 13238 | BV | copy-up: codex-vm SCI_INT is a GSI (9) not 0x2000, gated green; -IntelNic dropped as vacuous with the me |
| 2026-08-05 | 13352 | B | copy-up: F12 screenshots for the diagnostic probes (GopShot shot-wait, probes, README, test-ovmf f12 key |
| 2026-08-05 | 13378 | B | copy-up: msc-align F12 window before the ASDE arm, wedge finding to blu, run sheet from //Codex/red 1337 |
| 2026-08-05 | 13436 | B | copy-up: B2 read-only first touch before the I219 reset; reject row names the BAR verdict. Bed arms adde |
| 2026-08-05 | 13451 | V | copy-up: codex-vm TRB data-buffer alignment (latent, measured unreached); red's FAT-write displacement d |
| 2026-08-06 | 13611 | B | copy-up: reek -- xHCI controller census, a never-opened ordinal now names its controller (13602) |
| 2026-08-06 | 13643 | V | fester: codex-vm -natmap, sweep N-wide, and the first full sweep's findings (copy-up of 13640,13641) |
| 2026-08-06 | 13672 | B | copy-up: reek -- annotation campaign orphan quires (shaders, build/boot, bench: 300 blocks) + KbdDiagPro |
| 2026-08-06 | 13889 | V | copy-up: reek -- the NAK model becomes the codex-vm HID default, -hid-instant-complete opts out; four HI |
| 2026-08-07 | 13974 | K | copy-up from blu: annotation run 2, the last unaudited chapters in blu's lane (21 chapters, 24 blocks, 2 |
| 2026-08-07 | 13995 | B | copy-up: annotation run 3 slice A, generators boards and diag payloads (blu 13993) |
| 2026-08-07 | 14008 | KN | copy-up: annotation run 3 slice C, codex/os and codex/workflow, closes blu's annotation lane (blu 14007) |
| 2026-08-07 | 14083 | B | Copy-up from red: gop-draw-text-wrap primitive, MscAlignProbe wrapped rows, shot-window hint fits 1024 ( |
| 2026-08-08 | 14192 | V | copy-up: bound the boot stub's firmware allocations to bare-metal-ram-size; CODEX_VM_ALLOC_TRACE; clean- |
| 2026-08-08 | 14197 | V | copy-up: codex-vm's UEFI identity map tracks -mem instead of a fixed 4 GB. The emulator advertised RAM a |
| 2026-08-08 | 14230 | N | copy-up: bound the TCP retransmit timer (backoff, 5-retry cap with give-up) and arm it for SYN and FIN;  |
| 2026-08-08 | 14243 | N | copy-up: TCP sequence numbers wrap and comparisons use RFC 793 serial arithmetic; new codex/test/tcp-seq |
| 2026-08-08 | 14247 | N | copy-up: TCP retransmit queue is multi-segment (8 deep, oldest-first, bounded and refusing past the boun |
| 2026-08-08 | 14256 | N | copy-up: DtlsEndpoint reassembles fragmented peer flights; closes a pre-auth fault (EXC=06) on a non-zer |
| 2026-08-09 | 14267 | N | copy-up: DTLS send-side fragmentation at a 1200-byte MTU, with the control run that discriminates it (bl |
| 2026-08-09 | 14271 | N | copy-up: DTLS reassembly gated against real OpenSSL fragments, including a refragmented retransmission o |
| 2026-08-09 | 14273 | N | copy-up: TCP retransmit interval derived from measured RTT, RFC 6298 with Karn's algorithm (blu 14272) |
| 2026-08-09 | 14283 | N | copy-up: the general LwM2M client, and the CoAP multi-segment Uri-Path defect building it exposed (blu 1 |
| 2026-08-09 | 14317 | N | NetIO: a full send queue silently discarded every chunk past the eighth. net-send refuses a chunk when t |
| 2026-08-09 | 14340 | B | copy-up: A5 flight image built and bed-verified; sitting is two sticks |
| 2026-08-09 | 14353 | B | copy-up: A5 flight image rebuilt virgin (the flashed one could not fail) plus the calibrated raw stick r |
| 2026-08-09 | 14395 | N | copy-up: NetIO poll loops turn the clock, so a dead peer ends the loop (blu 14392) |
| 2026-08-09 | 14398 | V | copy-up: the A5 UEFI block path works end to end. Systab cell off the PML4, block write helper, and the  |
| 2026-08-09 | 14452 | V | copy-up: USB MSC timed-out transfers recover and retry (reek 14447). Three defects: no recovery on timeo |
| 2026-08-10 | 14478 | B | copy-up: A5 flight payload rebuilt as a5flight2.img -- -Uefi, bed-verified on a copy against a host comp |
| 2026-08-10 | 14481 | KBV | copy-up: blu -- B2 Finding 4 closed in the bed (-e1000-asde, driver clears ASDE), and AsdeStageProbe no  |
| 2026-08-10 | 14491 | B | copy-up: A5 self-compile arm -- a5bigflight.img, the compiler reproduces itself in the bed byte-identica |
| 2026-08-10 | 14494 | V | copy-up: blu -- codex-vm host crash on disk images above 31 MB: load_kernel memcpy ran past the 32 MB pr |
| 2026-08-10 | 14508 | B | copy-up: the metal ladder -- a probe that reports as a screen colour, every rung forced in the bed. a5fl |
| 2026-08-10 | 14514 | B | Copy up: ladder paints before it prints; content-checked read rungs; -Uefi in build-option-a.ps1 The lad |
| 2026-08-10 | 14548 | N | copy-up: NetIO connect now retransmits a lost SYN, and the ARM64 send path stops at a refused chunk inst |
| 2026-08-10 | 14552 | V | copy-up: codex-vm host crash on VBE mode set -- framebuffer activated at runtime without committing the  |
| 2026-08-10 | 14558 | K | copy-up: BROWSER-2 display -- VBE pixel path wrote to a banked window nothing scans out, two VM exits pe |
| 2026-08-11 | 14615 | B | copy-up: A5 sink arm (SinkLadderProbe + sink-arm calibration), reek 14613 |
| 2026-08-11 | 14646 | B | copy-up: A5 stick images rebuilt with the painted payload, bed-verified, ready to fly. |
| 2026-08-11 | 14660 | B | copy-up: A5 flight 3 wrote nothing and the ladder could not see it; retract the -screenshot finding (dec |
| 2026-08-11 | 14691 | V | copy-up: GPU desk render stages 1-2 -- rasterizer viewport (scissor) in codex-vm, and the desk 3D View p |
| 2026-08-11 | 14703 | V | copy-up: GPU desk render stages 3-4 -- shadow mapping on the host rasterizer. A light-space depth pass p |
| 2026-08-11 | 14721 | V | copy-up: shadow acne removed from BOTH renderers, and the GPU pane paced. The depth map is now filled fr |
| 2026-08-11 | 14732 | V | copy-up: shadow edges -- a finer map for the host path (1024, software keeps 256 since it builds its map |
| 2026-08-13 | 14778 | BH | Docs filing: move misfiled docs to the tree that owns them. Reference is external documentation and our  |
| 2026-08-13 | 14793 | H | copy-up: HardwareSitting A5 recipe drops the obsolete LF-normalising step (defect fixed at main 14789) |
| 2026-08-13 | 14796 | V | copy-up: shadows attach to their objects again -- front-face casting plus a slope-scaled bias, in both r |
| 2026-08-13 | 14856 | H | copy-up: HardwareSitting -- a freshly built image boots to the first-boot wizard, not the desk |
| 2026-08-13 | 14859 | V | GPU ground texture for the desk 3D pane, and the texture wire made explicit. The blue ground artifact wa |
| 2026-08-13 | 14864 | H | HardwareSitting: vmxprobe.img is on disk 2, pre-flash dump archived, and the flight is three keystrokes. |
| 2026-08-13 | 14866 | H | VT-x is available on the ASUS (IA32_FEATURE_CONTROL=5). A8 unblocked, Road A is the road; returned stick |
| 2026-08-13 | 14868 | H | build-img/build-boot-img: -Identity puts an existing IDENTITY.DAT on the image, so a fresh stick need no |
| 2026-08-13 | 14872 | H | GopWizard: a returning stick unlocks itself with the development passphrase; run sheet corrected on lock |
| 2026-08-13 | 14875 | V | Desk 3D pane: four measured defects. (1) The ground texture re-uploaded every frame from gsc-frame -- 30 |
| 2026-08-13 | 14888 | V | Desk 3D pane: the animation ran at one frame a second because the ORBIT CLOCK did, not the renderer. Mea |
| 2026-08-13 | 14913 | H | The timezone persists to the stick as TIMEZONE.DAT, and first boot asks for it. |
| 2026-08-13 | 14921 | V | codex-vm: pointer grab on Ctrl+Alt+G, the chord QEMU uses. Hides and pins the host cursor to the client  |
| 2026-08-13 | 14924 | BH | copy-up: B2 -- the arms fly before the reset, and the run sheet earns a flight. The 2026-08-11 flight di |
| 2026-08-13 | 14932 | V | codex-vm: the window title carries the grab state and the hotkey, from one place. A GOP SetMode was rewr |
| 2026-08-13 | 14937 | H | copy-up: B2 FLOWN -- the link comes up on the real I219. Every row painted and the machine did not wedge |
| 2026-08-13 | 14944 | V | WORKS-26: the pointer report rate is measurable, and the obvious cause is refuted. codex-vm prints DIAG  |
| 2026-08-13 | 14952 | BVH | copy-up: B2 -- CTRL is read-only on the I219, and the desk now reproduces it. Two boots on 2026-08-13. B |
| 2026-08-13 | 14957 | V | codex-vm: count transfer doorbells for the mouse endpoint and print arms and arm-rate beside the report  |
| 2026-08-13 | 14981 | V | copy-up: WORKS-26 -- the pointer in a pane was starved of VM exits, not of repaints; hid_kick_thread in  |
| 2026-08-13 | 14985 | KV | copy-up: B2c -- quiesce the receiver before programming its ring, and the e1000-ctrl-ro red fix. RED FIX |
| 2026-08-14 | 15001 | V | copy-up: WORKS-26 CLOSED -- the pointer in a desk pane now saturates the input The recorded root cause ( |
| 2026-08-14 | 15013 | NV | copy-up: calibrate NetIO's poll clock against the driver (a tick was 100000 polls, 1.55 s on the NE2000  |
| 2026-08-14 | 15016 | H | copy-up: the sitting queue for the NIC, five metal questions in argued flight order (HardwareSitting top |
| 2026-08-14 | 15028 | KN | copy-up: the poll-count-as-duration class -- the e1000 transmit wait was a 605us budget against a 1200us |
| 2026-08-14 | 15041 | BVH | copy-up: A5 SHIPPED -- the compiler compiled itself on the ASUS, OUT.CDX byte-identical. The >4GB-heap U |
| 2026-08-14 | 15054 | K | copy-up: DiskFacts unpack-text built the stored source one concatenation per byte, so the repository cou |
| 2026-08-14 | 15102 | BH | copy-up: wademo census app over the Codex DB engine, the -Ebs boot regression fix, and the NIC sitting r |
| 2026-08-15 | 15114 | H | Fold red's fleet-common traps out of agent memory into the docs that own them, and empty the memory inde |
| 2026-08-15 | 15158 | KB | copy-up: one source for the x86-64 identity memory map. New foreword chapter MemoryMap holds bare-metal- |
| 2026-08-15 | 15184 | N | copy-up: GitHub PR 64 absorbed (Steve Howell) -- the DMA truncation that corrupted every odd-length rece |
| 2026-08-15 | 15245 | N | copy-up: B5, verify the TCP and IP checksums on receive and refuse a frame that claims more bytes than i |
| 2026-08-15 | 15266 | N | copy-up: B5-UDP. The UDP receive path read a frame's IP header without checking it and a short frame kil |
| 2026-08-15 | 15287 | N | copy-up: ip-checksum silently dropped the last byte of an odd-length range. It stepped by two and read-b |
| 2026-08-15 | 15310 | N | copy-up: the ARP cache was a remote guest kill at 256 frames. arp-cache-add appended unconditionally int |
| 2026-08-15 | 15329 | N | copy-up: a 12-byte DNS response killed the guest. dns-parse-response stored the wire's ancount verbatim  |
| 2026-08-15 | 15345 | N | copy-up: four bytes on the wire killed the guest via MessageFraming. A wire-supplied length drove an unb |
| 2026-08-15 | 15356 | H | copy-up: two finished campaigns moved out of Active/ (MetalOutputSink.md to Designs/Done/Compiler, DECK- |
| 2026-08-15 | 15375 | N | copy-up: MessageFraming gets the refusal channel. FrameTextResult and FrameBytesResult carry valid, set  |
| 2026-08-15 | 15399 | BH | copy-up: NIC-3 second attempt. NicInitProbe performs e1000-init's own sequence with a row painted BEFORE |
| 2026-08-15 | 15403 | N | copy-up: HttpClient refuses the two wire-supplied numbers it believed (Track D census 10.1 item 4). A st |
| 2026-08-15 | 15416 | H | copy-up: nicinit.img flashed to disk 2 and verified. The dump displaced by it is the 2026-08-14 nicsitti |
| 2026-08-15 | 15418 | H | copy-up: record which seed nicinit.img was built against. The image embeds the seed, main's seed moved t |
| 2026-08-15 | 15426 | BH | copy-up: WORKS-9's heartbeat, bed-verified end to end. The sink ladder reports progress inside the fill  |
| 2026-08-15 | 15431 | H | copy-up: NIC-3 ANSWERED on metal. e1000-init does not hang -- it takes 93 seconds, 92.9 of them e1000-aw |
| 2026-08-15 | 15452 | K | copy-up: hid-scan-loop walked off the end of the buffer a device handed it -- it bounded against the cal |
| 2026-08-15 | 15461 | N | copy-up: Track D item 1, three untrusted lengths in TcpTransport now refuse. transport-feed-raw wrote pa |
| 2026-08-15 | 15465 | KH | copy-up: e1000-await-aneg is bounded by a 3-second HPET budget instead of a million-iteration count. On  |
| 2026-08-15 | 15469 | V | copy-up: the UEFI stub picks the largest GOP mode the firmware enumerates (native GOP resolution, bed ha |
| 2026-08-15 | 15477 | H | copy-up: re-measure two of my records against red's GOP rewrite. The -screenshot-delay defect still repr |
| 2026-08-15 | 15480 | H | copy-up: RepoProtocol reads blu's refusal channel -- every decode result folds valid, the two raw-span r |
| 2026-08-15 | 15510 | BH | copy-up: NIC-4 arm. NicRingProbe paints the RX descriptor DD map, which is what separates 'frames moved' |
| 2026-08-15 | 15514 | H | copy-up: HardwareSitting names the stub the queued sink image carries. The depot file is the one whose e |
| 2026-08-16 | 15529 | H | copy-up: sink-arm watches the payload's cite closure; CurrentPlan row closed. |
| 2026-08-16 | 15611 | KH | copy-up: e1000-await-link is bounded by time now. It is what the NIC-4 flight hung in, and the defect is |
| 2026-08-16 | 15763 | N | copy-up: the send path can drop the tail of a message and report success. It says so now. blu 15761. Not |
| 2026-08-16 | 15786 | N | copy-up: CLOSE_WAIT refused to send, so a plug whose peer half-closes lost the tail of its output. blu 1 |
| 2026-08-16 | 16065 | K | copy-up: OracleCloudArm64 Phase 4 MEASURED AND PASSING -- VirtioBlk now has a caller and reads sector 0  |
| 2026-08-16 | 16092 | K | copy-up: peek-16/poke-16 are compiler builtins with real 16-bit accesses on every lane (root 16091). The |
| 2026-08-16 | 16123 | K | copy-up: the ARM64 site serves 200. GET / returns 948 bytes of text/html titled Codex; GET /api/health r |
| 2026-08-17 | 16221 | K | copy-up: ARM64 DMA arithmetic done -- stack-top is a64pe-kernel-base + text-pages*4096 in Arm64PeWriter, |
| 2026-08-17 | 16259 | K | copy-up: delete the dead vnet-state-addr (one occurrence, its own declaration; #80100 is flash not RAM o |
| 2026-08-17 | 16291 | N | copy-up: tcp re-listen -- a listener is CREATED, not transitioned, so a server takes a second caller. ne |
| 2026-08-18 | 16668 | V | copy-up: codex-vm counts and reports dropped serial bytes; a failed demand-commit is named as the host's |
| 2026-08-18 | 16697 | K | copy-up: ARM64 VirtIO DMA regions derive from a stub-published floor at #40004000 instead of fixed #4400 |
| 2026-08-18 | 16701 | N | copy-up: Track D item 20 -- MessageFraming.frame-fits subtractive; the census result recorded on the row |
| 2026-08-18 | 16726 | BH | copy-up: DiagnosticStick.md design (proposal, approved campaign), HardwareSitting QUICKREF ruling of 202 |
| 2026-08-18 | 16728 | H | copy-up: DiagnosticStick.md output-channel section, HardwareSitting rig ruling. Docs only. |
| 2026-08-18 | 16731 | H | copy-up: retire the eject/reinsert warning as a live hazard (README, UsersHandbook, HardwareSitting reci |
| 2026-08-18 | 16746 | V | copy-up: codex-vm PCI-to-PCI bridge model (-pci-bridge) so pci-scan-all's descent branch runs; bus-aware |
| 2026-08-18 | 16751 | BH | copy-up: WORKS-9 -- the queued sinkladder image was stale against Fat16's release-blocker fix; rebuilt,  |
| 2026-08-18 | 16757 | H | copy-up: A8 -- a refused heap allocation now paints DARK RED, so the sitting can read the answer off the |
| 2026-08-18 | 16788 | N | copy-up: the sixth-handshake defect. net-listen called tcp-fresh-listener whatever the state, so a re-li |
| 2026-08-18 | 16793 | N | copy-up: OTA socket wiring -- the download runs over UDP against aiocoap, and the first real exchange ca |
| 2026-08-18 | 16822 | B | copy-up: DiagnosticStick.md step 1 (root 16819/16821): the diagnostic ladder build/boot/diag/Diag.codex  |
| 2026-08-18 | 16851 | BV | copy-up: DiagnosticStick.md step 3 (root 16848/16850): SMBIOS, EDID and CPU passive rows (DiagSmbios/Dia |
| 2026-08-18 | 16889 | B | copy-up: DiagScene banks a frame time (WORKS-36, root approved). Passive third row, no new state word, c |
| 2026-08-18 | 16900 | K | copy-up: ComplianceEvidence FactStore ingestion (root 16898): codex/plugs/evidence/FactIngest.codex reco |
| 2026-08-18 | 16941 | V | copy-up: TCP byte loss fixed. codex-vm discarded queued bytes on the guest's FIN. A 16 MB guest send int |
| 2026-08-18 | 17098 | H | copy-up: HardwareSitting diag.img flight 2026-08-18 |
| 2026-08-18 | 17105 | BH | copy-up: DiagPci judges the first MEMORY BAR (I/O BARs skipped); forced arm codex/test/diag-pci-map-judg |
| 2026-08-18 | 17117 | BH | copy-up: diag ladder EDID size per spec (ded-size) + cpu max-apic-ids row; flight item 4 FIXED with the  |
| 2026-08-18 | 17203 | B | copy-up: DiagnosticStick step 4 (DIAG.RCP in the image and bank; diag.rehearsed record; flash-usb -Rehea |
| 2026-08-18 | 17207 | B | copy-up: DiagnosticStick step 2 first lift, the block ladder as stage 6 with the block-oob forced arm; e |
| 2026-08-18 | 17210 | B | copy-up: DiagnosticStick step 2 sink ladder as stage 7 with the sink-shift forced arm; twelve arms green |
| 2026-08-18 | 17213 | V | copy-up: QEMU bulk-output path -- rep outsb FIFO bursts + the 2s trailer drain. plug-oracle-arith 4.84s  |
| 2026-08-18 | 17225 | B | copy-up: DiagnosticStick step 2 NIC three as stages 8-10 (nicsit/nicinit/nicring) with arms nic-pass/nic |
| 2026-08-18 | 17228 | B | copy-up: diag NIC stages, blu's review taken (BAR verdict gate on nicinit/nicring, s5 entering line); 15 |
| 2026-08-19 | 17310 | BH | copy-up: diag.img 601103D9 rehearsed 15/15 and flashed for the second grouped sitting; HardwareSitting f |
| 2026-08-19 | 17330 | H | copy-up: HardwareSitting, the second grouped diag sitting flown 2026-08-19 |
| 2026-08-19 | 17339 | H | copy-up: HardwareSitting, the 2026-08-19 sitting's glass transcribed |
| 2026-08-19 | 17345 | H | copy-up: HardwareSitting, A8 flown and GRANTED 2026-08-19 |
| 2026-08-19 | 17351 | H | copy-up: HardwareSitting, A8 keyboard bed arm |
| 2026-08-19 | 17357 | B | copy-up: nicring banks the RDH answer first and stops re-initialising the part (HardwareSitting 08-19 it |
| 2026-08-19 | 17359 | H | copy-up: HardwareSitting item 4 corrected, the count fallbacks need a dead clock |
| 2026-08-19 | 17371 | BVH | copy-up: WORKS-9 sink instrument, the -usb-bot-drops bed arm, and the finding Reads out what the sink re |
| 2026-08-19 | 17419 | BVH | copy-up: codex-vm -no-hpet and the nic-nohpet arm; test-ovmf read-only copy fix |
| 2026-08-19 | 17423 | H | Copy up from red: HardwareSitting A8 second flight, keyboard live. |
| 2026-08-19 | 17426 | B | copy-up: DIAG.RCP source stamp says when it does not know; diag.img 625235BF rehearsed 17/17 |
| 2026-08-19 | 17441 | H | Copy up from red: HardwareSitting third diag sitting. |
| 2026-08-19 | 17472 | BH | copy-up: WORKS-9, the bank's truth is the FILE, and the ladder refuses an image older than the seed Sitt |
| 2026-08-19 | 17492 | KBV | copy-up: nicring banks GPRC/RNBC/MPC/CRCERRS and an RDBA read-back; codex-vm models the receive counters |
| 2026-08-19 | 17557 | N | copy-up: GroupMembership service count counted a list-push twice (fester's find); battery-run regression |
| 2026-08-19 | 17589 | KN | copy-up: codex/os/net five chapters that had never compiled (fester's EdgeRouter find swept out); 39 of  |
| 2026-08-19 | 17665 | H | Copy up from red: HardwareSitting fourth diag sitting. |
| 2026-08-19 | 17693 | B | Copy up from red: BEDIDENT.DAT and its pinning test. |
| 2026-08-19 | 17708 | BH | copy-up: WORKS-9, the wedge is on the FIRST chunk and a flight can now vary the transfer size Sitting 4' |
| 2026-08-19 | 17742 | B | copy-up: nicring stage 2 -- the answer row rides the QR, GPRC read twice, and the descriptor discriminat |
| 2026-08-20 | 17796 | V | copy-up: the disk-write cost is the per-sector reopen, not the PIO exits -- ide_flush holds its handle ( |
| 2026-08-20 | 17804 | B | copy-up: identity stage 4 (IDENTITY.DAT v3 self-vouch trust root, verified at parse, v2 refused; passphr |
| 2026-08-20 | 17839 | N | copy-up: HAL flash joins the linear Board (root 17838, rulings 15 follow-on, Damian-directed). flash-ope |
| 2026-08-20 | 17857 | B | copy-up: Update 48 release -- proofs green at seed 930FF7F1 (battery 1541/0, sweep 0, poison 1541/0, DDC |
| 2026-08-20 | 17910 | V | copy-up: batch REP INSW from the IDE data port; count what blit_guest_output drops; both test harnesses  |
| 2026-08-20 | 17928 | N | copy-up: Track D item 10 -- the LwM2M staging bank is bounded (bank size enforces, CoAP Size2 refuses ea |
| 2026-08-20 | 18011 | NBV | copy-up: B3 as a diagnostic stage, the codex-vm e1000 RDH fix, the ARP narrowing (Decisions 2), and the  |
| 2026-08-20 | 18048 | BH | copy-up: diag ladder to 13 stages -- gopmode 6, xhci 8, b3 13; the GOP mode bank and its ASUS answer; ch |
| 2026-08-20 | 18050 | B | copy-up: the 13-stage renumber itself -- 18048 carried the chapters but not the table that dispatches to |
| 2026-08-20 | 18057 | BVH | copy-up: WORKS-9 sink rung ladder with a size-keyed bed lever and its answer-key arms; plugs 1.46 lua/ru |
| 2026-08-20 | 18059 | B | copy-up: WORKS-9 sink-drop reconciliation -- both readings correct for their tree, 500 sat on the pre-la |
| 2026-08-20 | 18061 | B | copy-up: WORKS-9 -- the pre-ladder band floor measured (516..519), so 500 was sixteen to nineteen events |
| 2026-08-20 | 18069 | B | copy-up: L-ADJECTIVE (an adjective standing in for a number survives review because it is unfalsifiable) |
| 2026-08-20 | 18104 | B | copy-up: ASDE as diagnostic stage 14, and the probe chapter that contradicted itself (blu 18101). Findin |
| 2026-08-20 | 18138 | KB | copy-up: ASDE quiesces before it resets, and the hazard is recorded at e1000-reset where it lives (blu 1 |
| 2026-08-20 | 18166 | BH | copy-up: B3 requires ip= and refuses with no-address instead of inventing a bed address; five control ch |
| 2026-08-20 | 18186 | B | copy-up: ip=dhcp as a NAMED opt-in for B3, with the no-lease state, a positive arm that requires the lea |
| 2026-08-20 | 18190 | H | copy-up: NIC-6 sitting card -- the leased-gateway hop, and the measurement showing codex-vm cannot separ |
| 2026-08-20 | 18209 | BH | copy-up: diag-arm learns the baseline the subject carries, and refuses when the subject moves under a ru |
| 2026-08-20 | 18218 | H | copy-up: Track A item 5 -- ten stale stub images measured, and flash-usb's rehearsal guard is opt-in (ro |
| 2026-08-20 | 18229 | H | copy-up: flash-usb refuses an unrehearsed hash by DEFAULT; override split from -Force as -UnrehearsedAny |
| 2026-08-20 | 18234 | B | copy-up: diag-arm watches for END instead of sleeping to the deadline. 35 minutes to 518 seconds on the  |
| 2026-08-20 | 18245 | BV | copy-up: the RDH discriminator gets its NO branch (-e1000-rdh-ro, nic-rdhro, nic-nolink asserts the y si |
| 2026-08-20 | 18276 | H | copy-up: SITTING 6 FLEW 2026-08-21. The sink ladder returned a THRESHOLD on metal, 16 sectors done=4, af |
| 2026-08-20 | 18279 | H | copy-up: sitting 6 -- NIC-4's ring half is answered (rdh-writable=y, RDH is ours to write); the successo |
| 2026-08-20 | 18283 | H | copy-up: CORRECTION, the sitting 6 NIC reading does NOT eliminate arrived-but-invisible. blu is right th |
| 2026-08-20 | 18307 | B | copy-up: nicring's during-window GPRC rides the answer row, so a dead bank no longer costs the discrimin |
| 2026-08-20 | 18324 | V | copy-up: codex-vm -- the NIC advertised an id it does not implement. |
| 2026-08-20 | 18332 | B | copy-up: diag slot grant -- non-picture stages could not paint at any width; b3 now names its step on th |
| 2026-08-20 | 18335 | V | copy-up: codex-vm -- an I219 model, and the K1 fly gate is met. |
| 2026-08-20 | 18341 | B | copy-up: diag paints bank loss when it is set, not at the summary a hang never reaches (root 18339) |
| 2026-08-20 | 18353 | KB | copy-up: the PCH K1 layer, shipping OFF until the fly gate proves it -- the board is a 15b8 part and sit |
| 2026-08-20 | 18356 | V | copy-up: codex-vm -- a second I219 pair, the MDIO/NVM semaphore. |
| 2026-08-20 | 18365 | V | copy-up: codex-vm -- the three the pch-state stage asked the model for. |
| 2026-08-20 | 18373 | B | copy-up: pch-state, the whole payload of sitting 7; every reading passes red's reading pair (root 18371) |
| 2026-08-20 | 18386 | B | copy-up: b3 absolute fuel cap, and it reports the ceiling it used (root 18384) |
| 2026-08-20 | 18389 | N | copy-up: the poll cell is clamped at both ends with a derived ceiling, and net-poll-clamped is the three |
| 2026-08-20 | 18396 | B | copy-up: diag-arm now checks the pch stage, and the sitting 7 rehearsal record. The harness checked a 14 |
| 2026-08-20 | 18399 | B | copy-up: b3 cap explanation corrected on blu's two corrections (root 18397) |
| 2026-08-20 | 18409 | B | copy-up: b3 poll cost is two numbers, both now labelled (root 18407) |
| 2026-08-21 | 18444 | K | copy-up: nic: 0x0034 offset 7 bit 1 cannot be sourced, and 770.17 can |
| 2026-08-21 | 18462 | H | copy-up: sitting 7 flew and lost its payload at stage 9, and the K1 rulings. Sitting 7s nine I219 readin |
| 2026-08-21 | 18468 | K | copy-up: nic: K1 through the cited register 770.17, with reek's re-aimed mechanism arm |
| 2026-08-21 | 18474 | B | copy-up: diag defers sink to last so a medium wedge stops eating the bank; banklost off-by-one; both sin |
| 2026-08-21 | 18500 | B | copy-up: pch reads KMRNCTRLSTA 0x00034 raw with its FCT/VET bracket; all-ones treated as unclaimed; bed  |
| 2026-08-21 | 18505 | K | copy-up: nic: requirement 2, the MDIO/NVM semaphore, built and shipping off |
| 2026-08-21 | 18534 | B | copy-up: diag docs sweep -- landed record deleted, live rules kept; one buried open item surfaced. |
| 2026-08-21 | 18546 | H | copy-up: SITTING 8 ANSWERED IT -- K1 IS ENABLED ON THE BOARD. pch 770.17=d104, giga-k1-dis=n, k1-en=y, s |
| 2026-08-21 | 18550 | B | copy-up: kmrn row no longer claims a hole from a zero; ZERO-ON-LIVE-PATH replaces HOLE/INERT. |
| 2026-08-21 | 18556 | B | copy-up: record red's ruling against the write+readback at 0x00034. |
| 2026-08-21 | 18559 | K | copy-up: ShellRefinement stage 4, a caller-owned BDL and the gated-play arm. |
| 2026-08-21 | 18562 | K | copy-up: nic: K1 required by default, kumeran deleted, with reek's arm |
| 2026-08-21 | 18567 | K | copy-up: nic: the semaphore ships on, coupled to the K1 flip |
| 2026-08-21 | 18574 | B | copy-up: build refuses a cfg leaving a risky stage unnamed; checked-in default cfg; DARK wording replace |
| 2026-08-21 | 18579 | H | copy-up: the word DARK is dead (root, main 18574) and the sitting-5 paragraph telling composers to read  |
| 2026-08-21 | 18615 | K | copy-up: name the BDL lifetime failure at hda-play-pcm-at. |
| 2026-08-21 | 18632 | K | copy-up: nic: requirement 3, the LCD registers after a PHY soft reset. e1000-lcd-reload ships OFF, pair  |
| 2026-08-21 | 18643 | B | copy-up: diag stage 15 pchk1 reads 770.17 back after the K1 write; asde to 16. The write is in e1000-ini |
| 2026-08-21 | 18645 | B | copy-up: the diag build refuses a config key named twice, and the composition report now asks the runtim |
| 2026-08-21 | 18652 | N | copy-up: collapse icmp-checksum into ip-checksum; the witness moves into the test chapter as witness-che |
| 2026-08-21 | 18656 | B | copy-up: ingest the hardware-returned stick images and the identity records recovered from them (rulings |
| 2026-08-21 | 18663 | B | copy-up: sitting 9 rehearsed (image 45239937, 36 arms, asde ON, pchk1 at 15) and the NETIO ceiling RULED |
| 2026-08-21 | 18679 | B | copy-up: diag-arm.ps1 rehearses a composition that turns a stage OFF (general arms expect skipped from t |
| 2026-08-21 | 18685 | NB | net: the send drain gets its own budget, below the give-up ladder. b3 sends checked. Copy up from blu 18 |
| 2026-08-21 | 18693 | B | copy-up: sitting 9 rebuilt on 18685, image ECC60AF4 rehearsed 36/36, flash-ready. |
| 2026-08-21 | 18696 | K | copy-up: ShellRefinement stage 4, the desk click plays through the HDA controller |
| 2026-08-21 | 18701 | B | diag: name the b3=short arm gap in the arm table. Copy up from blu 18699. |
| 2026-08-21 | 18716 | B | diag: b3 gets a send repeat knob, and b3=short gets its arm. Copy up from blu 18714. |
| 2026-08-21 | 18720 | H | copy-up: sitting 9 flown and recorded; firmware holds MDIO ownership; RING successor answered; sitting 1 |
| 2026-08-21 | 18729 | H | copy-up: sitting 9 card, the bed cannot express the nicring reading (GPRC placement in codex-vm). |
| 2026-08-21 | 18736 | KNB | nic: the K1 step reports refused ownership instead of dropping it. Copy up from blu 18734. |
| 2026-08-21 | 18747 | V | copy-up: GPRC counts where the MAC accepts the frame, so the bed can express DiagNicRing's gp>0 ddset=0  |
| 2026-08-21 | 18752 | N | net: WebServer stops sending HTTP responses it cannot finish, plus the re-measured test count. Copy up f |
| 2026-08-21 | 18756 | V | copy-up: -pci-bridge-deep, a bridge behind the bridge, so pci-collect's descent runs past one level for  |
| 2026-08-21 | 18759 | N | net: HttpFetch stops on a request or TLS record it could not finish. Copy up from blu 18757. |
| 2026-08-21 | 18775 | V | copy-up: -pci-bridge-levels N and -pci-bridge-backward, so pci-collect's depth cap and pci-bridge-one's  |
| 2026-08-21 | 18791 | K | copy-up: PciScanResult carries truncated, so a walk stopped by the depth cap says so instead of returnin |
| 2026-08-21 | 18794 | B | nic: the ASDE path pays the LCD reload obligation, gate still off. Copy up from blu 18792. |
| 2026-08-21 | 18799 | BV | copy-up: diag stages bank mid-run (DiagStage owns the bank write; ctx carries dc-vol/dc-lines; diag-bank |
| 2026-08-21 | 18816 | B | copy-up: sitting 10 rehearsed, image 5494AEA4 on 18799, 38/38, flash-ready. |
| 2026-08-21 | 18819 | KNB | nic: ULP entry is disabled by a cited write, gate off. Copy up from blu 18813. |
| 2026-08-21 | 18822 | BV | copy-up: -e1000-inject-armed and the nicring-invisible/nic-armed arm pair. Sitting 9's row was unreachab |
| 2026-08-21 | 18834 | B | copy-up: sitting 10 on 18825, image C6B1CEAC, 40/40, flash-ready. |
| 2026-08-21 | 18837 | V | copy-up: i219 ULP control model, PHY page 779 register 16 |
| 2026-08-21 | 18840 | H | copy-up: sitting 9 card carries the full pch row incl. ulp. |
| 2026-08-21 | 18844 | BV | copy-up: the xhci-two arm, plus the two-controller bed's BAR default moved out of the RAM arena and two  |
| 2026-08-21 | 18853 | V | copy-up: -usb-bot-die-len, a target that stops answering after a bulk write. Two premises corrected in t |
| 2026-08-21 | 18856 | H | copy-up: sitting 10 flown and recorded; the hang is inside e1000-reset; K1 exonerated. |
| 2026-08-21 | 18862 | K | nic: what the datasheet says about resetting a live receiver, measured. Copy up from blu 18860. |
| 2026-08-21 | 18869 | BV | copy-up: -usb-bot-census and -usb-bot-die-lba. The census says where the sink writes (3548..7324) versus |
| 2026-08-21 | 18874 | B | copy-up: nic-k1-off, the K1 control that holds the part fixed |
| 2026-08-21 | 18878 | B | copy-up: sink-dies names sink-ladder as its control. The lever and the arm pair themselves landed at 188 |
| 2026-08-21 | 18883 | B | diag: the pch stage reads the SMBus Control register, no write. Copy up from blu 18882. |
| 2026-08-21 | 18891 | BV | copy-up: -census FILE, so a rehearsal keeps the bed's BOT trace beside each arm's output for the board's |
| 2026-08-21 | 18901 | B | copy-up: a diag bank note APPENDS to the medium (bytes, never Text: gfat-text's per-character accumulato |
| 2026-08-21 | 18924 | B | copy-up: run build-diag AFTER a gate (the clean phase empties build-output and variant arms then SKIP),  |
| 2026-08-21 | 18928 | B | copy-up: b3's reset step is seven banked operations (imc, ctrl-read, rst-write, await-reset, settle-mdio |
| 2026-08-21 | 18932 | B | copy-up: sink says died when the target stops answering |
| 2026-08-21 | 18936 | B | copy-up: sitting 11 rehearsed, image 93FFCDA3, 43/43, flash-ready. |
| 2026-08-21 | 18946 | B | copy-up: sitting 11 on 18932, image 2C7030D7, 43/43, flash-ready. |
| 2026-08-21 | 18948 | B | copy-up: pchk1 LISTENS after the K1 write (1.2 s on the production ring, GPRC fenced and counted, DD cou |
| 2026-08-21 | 18955 | V | copy-up: -usb-bot-revive-on-reset, a bed for a recovery that succeeds |
| 2026-08-21 | 18966 | B | copy-up: sink-revived, the arm where WORKS-9 recovery succeeds |
| 2026-08-21 | 18980 | H | copy-up: sitting 11 flown and recorded; the ASUS talked to the dev box. |
| 2026-08-21 | 19003 | H | copy-up: sitting 11 medium-death candidate narrowed to b3's second bring-up (swflag or SLU). |
| 2026-08-21 | 19018 | BV | copy-up: codex-vm -usb-bot-die-on-nic, the bed for the I219-kills-MSC candidate, plus diag-arm nic-kills |
| 2026-08-21 | 19021 | B | copy-up: diag b3 step paints its own refused note (BANK LOST AT <step>, serial 'b3 bank lost at'), b3's  |
| 2026-08-21 | 19029 | B | copy-up: diag b3 rings-link is the six parts of e1000-init-after-reset, each banked (rings-quiesce, setu |
| 2026-08-21 | 19048 | B | copy-up: shipping diag.img (default cfg) for Update 49 and the shipping check that accepts exactly the d |
| 2026-08-21 | 19069 | H | copy-up: sitting 12 recipe in the sitting queue; red-workplan empty. |
| 2026-08-22 | 19092 | V | copy-up: codex-vm -run-list, a batch supervisor for the battery's phase 2 (CurrentPlan 'The battery chor |
| 2026-08-24 | 19166 | B | copy-up: sitting 12 mastered, and diag-arm's b3-banklost re-derived. diag-arm b3-banklost was RED ON MAI |
| 2026-08-24 | 19188 | H | copy-up: sitting 12 flew and eliminated both named candidates. Flown 2026-08-24, mastered by blu at Dami |
| 2026-08-24 | 19212 | KV | copy-up: the i219 acquire-loop defect is fixed, with a falsifier. Closes the registered defect in I219Is |
| 2026-08-25 | 19257 | H | copy-up: the Cobblestone directory-side rename docs |
| 2026-08-25 | 19288 | K | copy-up: Cobblestone rename, the on-device brand. Desk chrome (top bar, sidebar, taskbar menu, welcome t |
| 2026-08-25 | 19672 | N | copy-up: COMPILER-23 defect C step 3 completed -- the remaining 27 encode-meaning callers migrated to ch |
| 2026-08-25 | 19779 | B | copy-up: Update 50 release artifacts. Map refreshed from gate Sut.map (5,351/5,351 vs embedded MAP1), im |
| 2026-08-26 | 19955 | B | Copy-up: Update 51 release artifacts and docs (map, img, diag + rehearsal, TechnicalDetails, GitHubUpdat |
| 2026-08-27 | 20108 | V | copy-up: codex-vm refuses an unrecognised argument instead of dropping it in silence (plugs 1.41, the un |
| 2026-08-27 | 20353 | B | Update 52 release artifacts: seed/Codex.map refreshed from the gate's Sut.map and validated row-for-row  |
| 2026-08-28 | 20461 | V | copy-up: the batch-invalidation DROPPED arm gets a control, and codex-vm can produce the byte loss on pu |
| 2026-08-28 | 20487 | V | copy-up: compile.ps1's exit-4 no-SIZE path stops reading like a successful compile (fester 20484). Curre |
| 2026-08-28 | 20765 | B | The Update 53 release: artifacts and report. Map refreshed and validated 5,460/5,460 against the seed's  |
| 2026-09-01 | 21229 | B | Update 54 release: seed map and img refreshed against FCBABF07, diag.img rebuilt and fully rehearsed (46 |
| 2026-09-01 | 21429 | V | copy-up: codex-vm stops its application processors before teardown (red 21410): the partition was delete |
| 2026-09-02 | 21584 | V | copy-up: codex-vm's output writer checks its own write. Neither dump checked fwrite or fclose and Output |
| 2026-09-02 | 21676 | N | copy-up: COMPILER-36 multiply unit. Plain Integer multiply traps on overflow on x86-64 (jno; ud2 after i |
| 2026-09-02 | 22189 | KB | copy-up: Update 55 release artifacts (Hpet u64 assembly fix, map, img, diag image and rehearsal record,  |
| 2026-09-02 | 22312 | B | copy-up: Update 55 release, diag image and rehearsal record, digests, the note's proofs table, parking-l |
| 2026-09-07 | 22444 | V | copy-up: compile.ps1 refuses on a dropped capture instead of shipping a truncated .cdx at exit 0 (L-UNHE |
| 2026-09-07 | 22695 | B | copy-up: Track B diag stick rehearsed in evidence (AC7399ED at arms=46), and diag-arm.ps1 refuses a read |
| 2026-09-07 | 22724 | H | HardwareSitting: pre-flight card for AC7399ED, the default-cfg image now on the stick. b3 returns no-pee |
| 2026-09-07 | 22733 | H | HardwareSitting: the AC7399ED card names the in-place DIAG.CFG edit and refuses it. A naive reader (R-NA |
| 2026-09-07 | 22740 | H | HardwareSitting: the ASDE NOT-in-queue entry was the stale one, not CurrentPlan. The closure is CL 15015 |
| 2026-09-07 | 22750 | H | HardwareSitting: FLOWN 2026-09-07, AC7399ED. The card's headline prediction held: b3 returned no-peer be |
| 2026-09-07 | 22766 | B | copy-up: lift the MSC 64K-crossing probe into diag stage 9 (mscalign), placed in risk order, liveness co |
| 2026-09-07 | 22767 | H | HardwareSitting: the glass after pchk1, read back by Damian. asde stops at '-> RESET s2 warm reset', the |
| 2026-09-07 | 22788 | B | copy-up: keyboard publish block on GopUsbKbd plus diag stage 9 kbd; xdiag 112..119 registered. No seed,  |
| 2026-09-07 | 22806 | B | copy-up: b3-banklost re-aimed into the reset sequence and its suffix blindness fixed. Arm script only, n |
| 2026-09-07 | 22810 | V | From val 22808: codex-vm, the window pointer and the guest pointer now agree. Damian: "they currently do |
| 2026-09-07 | 22819 | B | copy-up: DiagMsc liveness probe uses msc-read10, so it issues no BOT reset and clobbers no cell. No seed |
| 2026-09-07 | 22832 | B | copy-up: asde banks a note before every step so a wedge names its own step (L-BANK, measured by the 2026 |
| 2026-09-07 | 22854 | H | HardwareSitting: FLOWN 2026-09-07 second, CB1AE335. The medium died at kbd, stage 9, while the ladder no |
| 2026-09-07 | 22859 | H | HardwareSitting: b3 is GREEN on sitting 13 and the peer log is the only record. echo-peer registered CON |
| 2026-09-07 | 22868 | H | HardwareSitting: retract the kbd classification-disagreement claim from the sitting 13 entry. build-diag |
| 2026-09-07 | 22881 | H | HardwareSitting: explain sitting 13's eight-stage gap. Stages 10 to 16 each returned success from dg-ban |
| 2026-09-07 | 22995 | B | copy-up: the CCE carriage-return guard in gfat-text with the esp-cfg-off arm that proves it, and DiagMsc |
| 2026-09-07 | 23029 | B | copy-up: diag-arm derives the subject baseline from the image ESP instead of build-output/diag-recipe.tx |
| 2026-09-07 | 23034 | B | copy-up: diag image rebuilt on the CCE guard, the DiagMsc restore and the derived baseline; cfg=off veri |
| 2026-09-07 | 23069 | B | copy-up: WORKS-62, the MSC driver issues SYNCHRONIZE CACHE and both diag bank writers call it between th |
| 2026-09-07 | 23111 | B | copy-up: diag.rehearsed carries AFC6AD65, rehearsed 46 of 46 on the flush-less control image, so flash-u |
| 2026-09-07 | 23138 | V | copy-up: codex-vm counts each lost byte once and the writer's loss at the final write; check-run-list ar |
| 2026-09-07 | 23166 | N | copy-up: listen transport takes serve-recv-buf-cap (val 23163): transport-new-with-cap, WebServer fresh- |
| 2026-09-07 | 23174 | N | copy-up: stage 2 bed accommodation (val 23171): service pinned to core 0, desk-loop yields per iteration |
| 2026-09-07 | 23193 | H | copy-up: sitting 14 card and the DiagnosticStick open row |
| 2026-09-07 | 23198 | H | copy-up: one sitting remains, the last sitting composed, fester on the network record channel |
| 2026-09-07 | 23204 | H | HardwareSitting: compose THE LAST SITTING's question set. Six questions no bed can answer, each with wha |
| 2026-09-07 | 23213 | B | copy-up: WORKS-62, a refused flush must never refuse the bank. 0x35 is optional in SCSI and the first ve |
| 2026-09-07 | 23233 | V | copy-up: plugs 2.46, codex-vm accepts SYNCHRONIZE CACHE and -usb-writeback models a write-back cache; 2. |
| 2026-09-07 | 23249 | B | copy-up: restore the DEFAULT shipping diag image. My 23213 put a sitting image on main via P-DEFAULT; ch |
| 2026-09-07 | 23259 | H | copy-up: the last sitting's ordering ruled, b3 early |
| 2026-09-07 | 23283 | V | copy-up: plugs 2.45 CLOSED, an application processor gets the same device models the boot processor gets |
| 2026-09-08 | 23327 | B | copy-up: WORKS-62 bed arms. The flush ablation now separates: dg-open writes the bank itself and ran bef |
| 2026-09-08 | 23332 | BH | copy-up: THE LAST SITTING cfg composed (diag-sitting15.cfg) plus the card. b3 early needs NO payload cha |
| 2026-09-08 | 23341 | H | copy-up: THE LAST SITTING, root's rulings in the card. Static ip for the channel, never re-addressed; NI |
| 2026-09-08 | 23477 | N | copy-up red 23475: PRISM-10, a hosted target serves HTTP over the host's TCP. HostedServe.codex is the s |
| 2026-09-08 | 23532 | BH | copy-up: diag ladder NETWORK RECORD CHANNEL (opens before the first bank write, ships every bank line li |
| 2026-09-08 | 23554 | N | copy-up: coaps:// composition (CoapsEndpoint + coaps-loopback gate), ProtocolStack and os.net count |
| 2026-09-08 | 23560 | N | copy-up: DtlsEndpoint anti-replay window on the application-data path, with the arm measured both ways |
| 2026-09-08 | 23569 | N | copy-up: DTLS handshake-epoch replay window, and the authenticated server flight's record sequence numbe |
| 2026-09-08 | 23601 | V | copy-up: plugs 2.51, per-processor port-exit accounting in codex-vm (IO BY VP), the io_lock convoy measu |
| 2026-09-08 | 23621 | N | copy-up: LwM2M over coaps (Lwm2mCoaps + lwm2m-coaps-loopback gate), app-epoch sequence census, handle-ec |
| 2026-09-08 | 23635 | N | copy-up: LwM2M echoes the registration handle; Deregister addresses it too |
| 2026-09-08 | 23637 | B | copy-up: bound the diag record-channel ship retry (drec-give-up=3), default re-rehearsed CA583E94; sitti |
| 2026-09-08 | 23650 | N | copy-up: boot-commit's production caller (fw-confirm-boot) and its gate; ota-fetch red at head recorded |
| 2026-09-08 | 23708 | N | copy-up: chr-hash-text lifts the duplicated djb2 fold out of LoadBalancer and MessageQueue; first arm fo |
| 2026-09-08 | 23725 | B | copy-up: diag ladder draw-API rows migration (val 23583/23592 clip param) + sitting applicability; ladde |
| 2026-09-08 | 23752 | N | copy-up: WORKS-48 residue, the standard endpoints reach the pane's log (val 23750). |
| 2026-09-08 | 23768 | BH | copy-up: sitting-15 flight image certified (47F29D50, lease/rtcw on, 50 arms) + pre-flight card; nic pre |
| 2026-09-08 | 23771 | H | copy-up: THE LAST SITTING signed off, root row |
| 2026-09-08 | 24131 | N | copy-up: the web mux keeps a pool of closed connections' transports and rebinds one on accept, so a conn |
| 2026-09-08 | 24214 | N | copy-up: TcpTransport builds one transport record per frame, not two (208 to 144) |
| 2026-09-08 | 24218 | N | copy-up: NetIO and Arm64NetIO build one transport record per frame on the drain path |
| 2026-09-08 | 24239 | N | copy-up: remove the vestigial recv-buf field from TcpTransportState (144 bytes a frame to 120) |
| 2026-09-08 | 24252 | B | copy-up: diag staleness guard keyed on the payload bundle closure; DiagnosticStick reading replaced with |
| 2026-09-08 | 24319 | H | copy-up red 24312: remove DeskBoot, a temporary that got half-officialised (Damian 2026-09-08). apps/wor |
| 2026-09-08 | 24435 | N | copy-up: TrustTransport identity copy removed, 14 hand-written transport rebuilds collapsed onto transpo |
| 2026-09-08 | 24791 | N | copy-up: tcp-checksum-valid sums the pseudo-header instead of concatenating it, 11,968 bytes off every a |
| 2026-09-08 | 24837 | N | copy-up: the IPv4 receive branch parses at an offset and copies nothing; an accepted 1,514-byte frame 49 |
| 2026-09-08 | 24841 | N | copy-up: payload-range campaign staged, and stage 1 (transport-feed-range, allocating nothing) landed |
| 2026-09-08 | 24844 | N | copy-up: payload-range stage 2, TcpSegment carries a range replacing the list; a parsed and checksum-val |
| 2026-09-08 | 24854 | N | copy-up: payload-range stages 3 and 4; nothing on the receive path copies the payload, an accepted 1,514 |
| 2026-09-08 | 24862 | N | copy-up: tcp-with-checksum sums the pseudo-header on the send path too, 12,272 to 304 and constant in se |
| 2026-09-09 | 25176 | H | copy-up root: the last sitting has flown, queue closed, Track A/B replaced |

## Coverage

```
p4 changes -l //Codex/main/codex/os/kernel/...   -> 134 changelists
p4 changes -l //Codex/main/codex/os/net/...      -> 149
p4 changes -l //Codex/main/build/boot/...        -> 196
p4 changes -l //Codex/main/tools/codex-vm.c      -> 240
p4 changes -l //Codex/main/docs/Hardware/...     ->  91
p4 changes -l //Codex/main/...                   -> 7,220 (the source of every date and description)
```

Measured 2026-09-09 between 03:05 and 03:40 from `D:\Projects\Cobblestone-red`.

**What this ledger does not carry.** The ledger carries no changelist that
touched only `seed/...`, for the reason given above. The ledger carries no
changelist from the pre-Perforce era of 2026-03-14 to 2026-04-16, because no
changelist exists for those 34 days. The ledger carries no flash tool or bed
script outside `tools/codex-vm.c`, and `build/flash-usb.ps1`,
`build/build-img.ps1` and the OVMF and Renode harnesses are part 05's and part
07's corpus rather than this appendix's.
