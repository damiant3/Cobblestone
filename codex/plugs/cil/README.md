# CIL plug

Direct Codex IR to managed PE assembly emission. The implementation ports
the stack-machine lowering approach from `old/src/Codex.Emit.IL/`; the retired
project is reference material and is not built or invoked. Metadata heaps,
table indices, method bodies and the PE/CLI container are written in Codex.
No Roslyn, Reflection.Emit or System.Reflection.Metadata library is used by
the emitter. System.Reflection.Metadata is an independent test reader only.

The current unit supports scalar functions, direct calls and recursion,
branches, locals, signed 64-bit arithmetic, default 64-bit reals, booleans,
CCE text literals, concrete records, generic lists, static function values
and partial applications as curried CLR delegates. Self tail calls beneath
conditionals and local bindings use loops. Record and list
updates preserve object identity; list append constructs a new list.
The output is a DLL with public static methods on
`PrismGenerated.Codex_Program`, referencing Mono-compatible `mscorlib` v4.
Metadata names are UTF-8; language text remains CCE inside CLR strings.
The module identifier is derived deterministically from SHA-256 with its
identifier bytes initially zero. Hash scratch is reclaimed before return.

Unsupported expressions and signatures refuse before any binary output.
Unlifted lambda expressions, generic user types, sum types, general framework
calls and bounded wrapping/clamping remain implementation work. Void values
in fields, locals and generic arguments refuse. The ModBuilder page uses this
plug in its Build mod DLL card and saves a managed `PrismMod.dll`.

Unity mode accepts `CIL-UNITY`, a newline, the bundled runtime DLL as base64,
another newline and the IR chapter. It embeds the adapter as a manifest
resource and emits a `PrismUnityEntry.Initialize` bootstrap. The adapter is
loaded before the model entry runs. Known Valheim effects call flat adapter
methods; storage and arc records reference the adapter's immutable CLR types.
Changes to those record layouts or effect signatures refuse against the fixed
adapter ABI. Unity models require a zero-argument `opening` returning `Nothing`.
Integer formatting crosses into the adapter's CCE conversion; string append
and equality operate on the internal text representation.

The fixed Unity adapter is built by the publisher from generated interop
source with Roslyn. Players receive the adapter inside the page and inside
their generated DLL; their edited model compiles through the Codex/WASM plug.
Players need no C# compiler, SDK, Git or source checkout. The page includes
the compiler plug and generated adapter source for review and source export.
The native game loader and helper deployment remain separate unfinished work.

Build with `pwsh codex/plugs/cil/build.ps1 -Kernel seed/Codex.cdx` after
reading the called build scripts and checking guest RAM admission. The build
prints the kernel digest and writes `build-output/cil-stdio.wasm` here.
Run `node codex/plugs/cil/test.mjs` to compile subjects with the embedded
browser compiler and emit assemblies through the actual WASM plug. Then run
`pwsh codex/plugs/cil/test-managed.ps1` to inspect metadata and execute the
methods through an independent CLR. The test tools are developer-only;
players will use the embedded compiler/plug and the game's existing runtime.
The developer checks need 64-bit PowerShell 7, Node with global WebSocket support,
Edge at its standard Program Files (x86) path, and the generated ModBuilder
workspace with its embedded compiler. They currently run on Windows.

The managed test covers calls, recursion, shadowing, signed argument ABI,
overflow, real arithmetic, unordered comparison, text storage, record field
ordering, mutable collections, captured arguments, nested/returned delegates,
lexical function shadowing, deep tail recursion, callbacks into the actual
Valheim storage model, deposit, withdrawal and commit guard. Browser
checks also cover deterministic output and unsupported-input refusal.
`pwsh codex/plugs/cil/test-mono.ps1 -GameDir <Valheim-folder>` runs integer
subjects in isolated processes against that game's existing Mono runtime and
managed core library. It prints the tested runtime's SHA-256. The script loads
the runtime but does not launch the game or load Unity scenes. These checks
do not establish playable-mod integration or complete feature acceptance.

Prepare the bundled adapter on a developer machine with the installed game:

1. Build `codex/plugs/unity/build.ps1 -Kernel seed/Codex.cdx` and this plug.
2. Run `node codex/plugs/cil/test.mjs --export-unity`. The export uses the
   locally built Unity module and writes combined adapter source and IR.
3. Run `pwsh codex/plugs/cil/build-unity-runtime.ps1 -GameDir <Valheim-folder>`.
   This publisher step needs a .NET SDK and the game's managed references.
4. Run `node codex/plugs/cil/test.mjs --unity-runtime
   codex/plugs/cil/build-output/runtime/PrismRuntime.dll`, then
   `pwsh codex/plugs/cil/test-unity-managed.ps1`. These check each preset and
   the combined DLL, resource identity, module hashes, all generated methods
   through the CLR JIT and selected real adapter text calculations.
5. Run `pwsh codex/plugs/cil/test-unity-runtime.ps1 -GameDir <Valheim-folder>`
   for embedded startup and HUD/storage callback execution through a stand-in
   adapter on the CLR and Mono. The stand-in does not establish Unity behavior.
6. For each generated `unity-<feature>` subject, `test-mono.ps1 -GameDir
   <Valheim-folder> -Subject unity-<feature> -Runtime <PrismRuntime.dll>
   -Prepare` prepares every method against the real adapter without invoking
   game-facing entry points. `unity-all` covers the combined pack.
7. Generate the site with `pwsh apps/modbuilder/build-site.ps1`; run
   `node apps/modbuilder/test-managed-build.mjs` for standalone file-mode
   compilation, source edits, stale-output rejection and Save As state checks.
   The file picker is a stand-in in this automated check.

The adapter build certifies the installed game assembly and Unity player
hashes. An updated game needs a rebuilt adapter. Actual startup, storage,
world and plant acceptance in the game remain required by ModBuilder MB-8.
