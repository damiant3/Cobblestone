# GitHub Update 9 -- CL 986 to CL 1058 (2026-05-06)

Previous update: CL 985 (GitHubUpdate8).
This update: CL 1058.

## PE32+ Emitter + IMG Mode (CLs 999-1005, Cam)

The compiler now emits native UEFI PE32+ executables and GPT disk images
directly from Codex source. No external toolchain.

- **PeWriter** -- PE32+ binary builder. Reads CDX header metadata
  (text/rodata offsets and sizes), builds a UEFI stub that
  AllocatePages at 0x100000, copies code+rodata, patches jumps,
  and emits a complete PE32+ with optional DOS stub.
- **GptWriter** -- Protective MBR + GPT header + EFI System Partition
  entry. Content-addressed partition GUIDs from SHA-256.
- **Fat32Writer** -- Minimal FAT32 filesystem with EFI/BOOT/BOOTX64.EFI.
  BPB, FSInfo, FAT table, directory entries, cluster allocation.
- **IMG mode** -- `x86-64-emit-img` chains CDX → PE32+ → GPT+FAT32
  into a single 64 MB bootable disk image. One compile command
  produces a USB-flashable artifact.

Emit modes are now: CDX, ELF, EFI (PE32+), IMG (GPT disk image), TEXT.

## UEFI Boot Path (CLs 1023-1054, Nib)

End-to-end UEFI boot from PE32+ to interactive shell, debugged on
real hardware through a series of diagnostic CLs.

- **ExitBootServices** -- Fixed stack layout, memory map buffer sizing,
  retry loop for stale map keys (CLs 1023-1025).
- **Diagnostics** -- Serial output after ExitBootServices, VGA text
  buffer probes, ConOut validation, CR3 trampoline fix for page
  tables at high addresses (CLs 1027-1034).
- **UEFI Console emit** -- `x86-64-emit-uefi-app` routes print-line
  to UEFI ConOut→OutputString instead of COM1 serial. SystemTable
  pointer stored at boot (CL 1045).
- **UEFI app PE stub** -- Minimal entry that calls opening directly
  without bare-metal init. ConOut stays alive for FirstBoot (CL 1054).
- **Boot key detection** -- Hold Escape/F12 at boot to enter dev console
  vs. normal boot path (CL 1036).
- **`__uefi_print` runtime helper** -- UTF-16LE conversion for ConOut
  output on UEFI (CL 1050).

## Build Environment (CLs 1004-1019, Cam)

Nine Codex-native works chapters replacing PowerShell build scripts:

| Chapter | Replaces |
|---------|----------|
| SourceConcat | concat-codex-self.ps1 |
| SeedVerify | test-self-verify.ps1 |
| DigestCompute | Get-FileHash calls |
| SweepHarness | sweep.ps1 decision logic |
| BuildManifest | manual README updates |
| CompilerDriver | sample-compile-selfhost.ps1 + pingpong |
| TestRunner | run-for-sweep.ps1 |
| FilePath | path manipulation |
| HexFormat | hex/size formatting |

## Prose Grammar Pipeline (CLs 1037-1058, Cam)

Feature-flagged CPL (Codex Prose Language) integration into the
self-hosted compiler. Nine steps, all behind `CompileFlags { prose }`:

1. **CompileFlags** record + mode parsing (`TEXT prose` activates)
2. **Lexer** -- `ProseText` and `AtSign` token emission at column 2
3. **Parser** -- prose block collection into Document
4. **Template recognition** -- all 6 CPL sentence forms:
   record (`A X is a record containing:`), variant (`X is one of:`),
   function (`To V ...`), constraint (`such that`/`where`/`provided that`),
   proof (`claim:`/`therefore,`), procedure (`first,`/`then,`/`finally,`),
   quantified (`for every`/`there exists`)
5. **Annotation syntax** -- `@kind target body` parsed into AnnotationNode
6. **Consistency checking** -- prose function/record/variant names
   validated against notation definitions (CDX1101-1104)
7. **Pipeline wiring** -- checks invoked from compile-parse when flag on
8. **Banned words** -- 14 CPL-banned words enforced in `We say:` blocks
   (CDX1110)
9. **Sample** -- `prose-basic.codex` exercises all forms, in sweep

Prose flows through Document → AChapter → IRChapter. When flag is off,
zero behavioral change -- proven by 8 consecutive fixed points.

## Compiler Infrastructure (CLs 996-1020)

- **O(n^2) offset-table fix** -- linear scan replaced with parallel
  hash-table build. Was the dominant cost in emit for large chapters (CL 996).
- **list-push rename** -- `list-snoc` renamed to `list-push` across
  the entire codebase. Aliased for backward compat (CLs 1016-1020).
- **IrNegate constant folding** -- negative literals fold at IR level
  instead of emitting negate instructions (CL 1013).
- **Empty act block fix** -- streaming text emitter handled empty act
  blocks correctly (CL 1006).

## Text Emitter Fix + Pingpong Upgrade (CL 1053, Cam)

- **Negative literal parens** -- `IrIntLit` with negative value and
  `IrNegate` with compound operand now parenthesized in text emit.
  Fixed 21 round-trip failures in the selfhost.
- **Pingpong dual fixed point** -- `pingpong-self.ps1` rewritten:
  Phase 4 = text round-trip (soft fixed point, byte-identical .codex),
  Phase 5 = CDX fixed point (hard fixed point, byte-identical .cdx).
  Both must pass.

## Agent Lifecycle (CL 1057, Nib)

Works-level agent system:
- **AgentRuntime** -- GGUF loader, local+GPU inference, chat interface
- **AgentAcquisition** -- bundled/USB/network acquisition, verification
- **AgentCoordinator** -- local/upstream escalation, role dispatch
- **FirstBoot** -- welcome wizard, identity gen, agent setup, mode selection
- **DevAgent** -- updated for lifecycle integration

## Dev Console (CL 1048, Nib)

Interactive development environment accessible via UEFI boot key.
Menu-driven with code browser, text editor, compiler, sweep runner,
debugger, CDX inspector.

## Numbers

- **Compiler**: ~23,200 lines of Codex across 54 files (up from ~21,000/51)
- **Foreword**: 177 chapters across 18 quires
- **Works**: 40 chapters
- **OS**: 56 chapters
- **Samples**: 153 pass / 0 fail / 18 skip (up from 152, +prose-basic)
- **Seed**: 1,860,000 bytes (CDX), dual fixed point (text + CDX)
- **Emit modes**: CDX, ELF, EFI (PE32+), IMG (GPT), TEXT, UEFI
