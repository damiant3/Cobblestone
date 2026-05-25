 1. The Verifier

  THE-LAST-PEAK calls this "the hardest sub-problem on this face" and then gives it exactly zero design work. The entire
   security model — everything in RuntimeTrust.txt, every "software cannot hurt you" claim — depends on a program that
  type-checks untrusted code at install time. What subset of the type system is decidable for untrusted inputs? What's
  the fuel limit strategy? How does the verifier handle effect polymorphism from code it hasn't seen before? How do you
  prove the verifier itself is correct?

  This isn't just another component. It's the single point of failure for the whole thesis. A bug in the verifier IS a
  zero-day. Every other design implicitly assumes it exists and works. It needs a design doc before anything that
  depends on it (which is: everything in Codex.OS).

  2. Cryptographic Primitives

  RuntimeTrust.txt puts Signature fields on every message. V3 federation puts PublicKey in vouch records. The forensics
  layer hashes every fact. The policy contract signs every policy. But there is no design for where crypto comes from.
  On .NET you have System.Security.Cryptography. On bare metal you have nothing.

  Questions that need answers: Which algorithms (Ed25519? SHA-256? Both are implicit in the docs but never stated)?
  Implemented in Codex or as builtins? Side-channel resistance on bare metal? Key storage? This isn't a nice-to-have —
  it's the foundation of every trust operation in every doc you just wrote.

  3. Identity and Authentication

  DistributedAgentOS.txt says "the agent is bound to a person, not a device." The policy contract compiles rules about
  "Jake." The agent protocol has AgentIdentity in every message. But identity is never designed. How does Jake's phone
  know it's Jake? Biometrics? Key pair? Hardware token? How does a Codex.OS instance authenticate a remote agent's
  identity claim? How do you bootstrap identity on a new device?

  The capability system assumes identities are solved. The trust lattice assumes identities are solved. The forensic
  chain signs everything with an identity. None of them design identity. This is the "who are you" problem that gates
  every "are you allowed to" question.

  4. The Networking Stack

  THE-LAST-PEAK says "HTTP sync works, need raw sockets." The agent protocol needs sub-second message exchange.
  Federation needs peer-to-peer sync. But there's no TCP/IP stack design, no socket abstraction, no DNS, no transport
  layer for bare metal. "Cross-device transport (serial, TCP, BLE)" is Step 6 in the agent protocol's implementation
  order and is marked "Large" — but it's an implementation order entry, not a design.

  The networking stack is the circulatory system for everything above Ring 4. Agent negotiation, federation sync, lease
  renewal, forensic record propagation — all of it dies without a network. And on bare metal, you're writing the stack
  from scratch.

  5. Filesystem / Persistent Storage

  THE-LAST-PEAK mentions "facts on disk" as a possibility and marks the filesystem as "Not started." But the fact store
  is in-memory today. Policies are facts. Forensic records are facts. The trust lattice is facts. When the machine
  reboots, where did all the facts go?

  This needs a design: how does the content-addressed fact store map to a block device? Is there a journal? What about
  wear leveling on flash? Boot sequence — how does the OS know which facts to load first? This is the "persistence" gap
  that sits under everything stateful.

  6. Process Lifecycle and IPC

  Ring 2 has a 16-slot process table with preemptive context switching. But how do processes talk to each other? The
  agent protocol is for agents — but two Codex programs running on the same kernel need to communicate. Message passing?
   Shared capability grants? Typed channels? The structured concurrency doc designs in-process fork/await, but
  inter-process communication is undesigned.

  Also missing: how does the kernel decide what to run? Round-robin? Priority? Real-time scheduling for [HardRealtime]
  paths? The policy system compiles CPU quotas but the scheduler that enforces them doesn't exist in any doc.

  ---
  The Medium Four: Partially Addressed, Needs Depth

  7. Boot Sequence / Init System

  What happens when Codex.OS boots? Which processes start? How is the initial capability set distributed? The kernel
  boots and runs the compiler today — but a real OS needs an init system that starts the agent, loads the fact store
  from disk, opens the network, and establishes identity. The boot sequence is the capability root — whoever runs first
  can grant everything. Getting this wrong is a privilege escalation vulnerability at boot time.

  8. The AI / Inference Boundary

  The forensics layer carefully distinguishes Deterministic | Statistical | Heuristic inference methods. But the
  "Statistical" path is undesigned. Where does the model live? How does it run on bare metal (no Python, no CUDA, no
  ONNX runtime)? What's the boundary between compiled deterministic code and inference? The entire Codex.OS UX premise
  is "the agent runs inference" — but the inference engine has no design.

  This might be intentionally deferred (the agent is just a Codex program that calls an inference API), but if so, the
  design should say that explicitly and define the API boundary.

  9. Upgrade / Live Migration

  The self-hosting compiler can rebuild itself. But how does a running Codex.OS instance upgrade? The local agent
  "accepts new source, compiles it locally, and extends itself" — but the mechanism isn't designed. Hot code reload?
  Stop-the-world recompile? How do you upgrade the kernel? The verifier? If the verifier has a bug and you ship a fix,
  how does that fix propagate to devices that are offline?

  Policy versioning is also in this bucket — RuntimeTrust.txt's policy templates can change between versions. A policy
  written against V2 templates might not parse the same under V3. Need a policy_version field or a migration strategy.

  10. Error Recovery

  The forensics layer records what happened. The anomaly protocol escalates and falls back to safe mode. But what
  happens after safe mode? The agent is restricted to compiled-in safe capabilities — then what? Does it stay in safe
  mode forever? Who restarts it? Is there a supervisor process? Watchdog? The design has a good "enter safe mode" story
  but no "exit safe mode" story.

  ---
  The Connective Tissue: Cross-Cutting Concerns

  11. Testing / Verification Strategy for Safety-Critical Components

  900+ tests for the compiler. But the verifier, capability enforcer, and agent protocol handler are safety-critical — a
   bug in any of them breaks the security model. What's the strategy? Formal verification of the verifier?
  Property-based testing of the capability checker? Fuzzing of the policy parser? The project principle is "test what
  matters" — these are the things that matter most and have no testing strategy.

  12. Performance Model

  MM3-REALITY-CHECK measures 220MB peak heap for self-compilation. But the OS will run multiple processes, each with
  capabilities, each with forensic recording. What's the memory budget per process? What's the interrupt latency budget?
   What's the context switch cost target? Without a performance model, you'll build something that works but can't run
  on the phone sitting on the desk.

  ---
  What's Solid

  For balance: the capability refinement, the agent protocol, the policy language, the forensics layer, federation,
  narration, structured concurrency, the stdlib, and the memory model are all well-designed. The implementation orders
  are realistic. The dependency chains are clear. The V1→V7 roadmap makes sense.

  The gaps are almost all in the infrastructure layer — the stuff below the abstractions. The abstractions are
  beautiful. The plumbing to make them real on bare metal is where the design work is missing. That's normal at this
  stage — you designed top-down from the vision and built bottom-up from the compiler, and the two are about to meet in
  the middle. The middle is where the gaps live.
