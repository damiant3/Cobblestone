# Works — Codex Developer Environment & Repository Protocol

Works is the application layer of Codex OS: a UEFI-bootable developer console, a content-addressed repository protocol (replacing Git), and the tooling that ties the compiler, AI agents, test harness, and network services together into a self-contained bare-metal development environment.

## Modules

### UEFI Boot & Console Shell
- **UefiBoot** — UEFI PE entry point: reads SOURCE.SRC, indexes chapters, enters the dev console
- **UefiConsole** — Typed wrapper over UEFI ConOut/ConIn
- **DevConsole** — Top-level state machine with 16 modes (browse, edit, compile, debug, agent, hypervisor, etc.)
- **DevConsoleMenu** — Generic keyboard-navigable menu system
- **VgaShell** — VGA-text fallback shell for non-UEFI environments
- **FirstBoot** — First-boot wizard: identity generation, agent acquisition, upstream config

### Source Navigation & Editing
- **CodeBrowser** — Prefix-trie definition index over all source chapters
- **ConsoleEditor** — Line-oriented multi-buffer text editor
- **ShellParser, ShellClarifier, ShellDispatch** — Command tokenizer, ambiguity resolver, dispatcher
- **ShellPersistence** — Disk serialization of shell history, aliases, and environment

### Compiler & Build System
- **CompilerDriver** — Self-hosted compiler driver replacing PowerShell build scripts
- **BuildManifest, BuildRecord, BuildTrace** — Structured seed-rebuild manifest, per-compilation records, artifact provenance
- **SweepHarness** — Sample-battery classification and pass/fail evaluation
- **SeedVerify** — CDX seed self-verification: magic, Ed25519 signature, author key
- **DigestCompute** — SHA-256 digest utilities

### VMX Hypervisor & Testing
- **DevHypervisor** — Bare-metal Intel VT-x hypervisor with I/O port exit dispatch to Codex device models
- **VmCompile, VmPingpong, VmSweep, VmRunner** — Compile, fixed-point, and sample battery tests inside VMX guests

### Debugger
- **DevDebugger** — Interactive bare-metal debugger: memory inspector, port I/O probe, breakpoints, stack walker
- **CdxInspector** — CDX binary header inspector

### Repository Protocol (replacing Git)
- **RepoProtocol** — Facts, proposals, verdicts, supersession, and wire codec
- **Annotation, AnnotationStore, SignedAnnotation** — Typed annotations with Ed25519 signatures
- **AnnotationDriver, AnnotationTransport** — Surface coordinator and transport pump
- **MutationLog** — Append-only, CRC-framed mutation log
- **Discussion** — Threaded discussion store anchored to annotation targets
- **Historian** — Supersession-chain walker for attribution and verdict history
- **Narrator** — Narration collapse: annotations to plain-language summary
- **TrustExplorer** — Trust-lattice browser: publisher list, trust threshold dial, blocklist
- **KeyManager** — Ed25519 keypair generation, keyring, fingerprinting

### AI Agent System
- **DevAgent** — Local AI assistant: loads GGUF model, runs inference, chat interface
- **AgentRuntime** — CPU-only fixed-point inference or GPU offload
- **AgentCoordinator** — Local/upstream dispatch with role configs
- **AgentAcquisition** — Three-path agent acquisition: bundled, USB, or network with signature verification

### Network & Web
- **WebServer** — HTTP server with route dispatch
- **Http** — HTTP/1.0 request parsing and response formatting
- **Middleware** — Logging, metrics, rate limiting, session gating

## Completeness

60% — Core data models, type hierarchies, wire codecs, and pure functions are fully specified across all 54 modules. Boot, menu, UEFI I/O, repo protocol, annotation pipeline, key management, build records, CDX inspector, seed verify, and the VM compile/pingpong/sweep replacements are structurally complete. Major stubs: DevConsoleBoot source-tree indexing returns placeholders; hypervisor VMX device-model dispatch is incomplete; AgentRuntime inference forward-pass references NeuralNet/Tensor chapters not present in Works; ShellDispatch has many stub command handlers.

## Codex Conformance

Full — Every module is written in Codex. Container formats (ELF, EFI, GPT/FAT) are produced by plug CDX binaries. Network and serial I/O route through effect types. No foreign tooling dependencies.
