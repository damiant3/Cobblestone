# The compile page's module manifest: every wasm module the page ships, with
# the chapter list each is built from. THIS IS THE ONLY COPY. Before this file
# the list existed three times -- the buildable half only as prose in
# `page-lens-test.ps1`'s table, the copy list again in `build-page.ps1`, and
# the bytes plugs' lists nowhere at all -- so a fresh workspace could not
# rebuild the 48 shipped modules (PRISM-7 stage 0; the gap is recorded in
# `plugs-backlog.md` "no script calls build-plug-wasm.ps1 at all").
#
# Dot-source this file; it defines $PageModules. Consumers:
#   build-page-modules.ps1  builds every row
#   page-lens-test.ps1      grades the ir rows
#   page-bytes-test.ps1     grades the bytes rows
#   build-page.ps1          copies every row's module into the page
#
# Row fields:
#   plug      directory under codex/plugs/
#   file      the module the page fetches
#   transport 'ir' (PlugStdio, IR text on stdin), 'bytes' (PlugBytes, a
#             compiled payload), 'irbytes' (PlugIrBytes, IR text in and a
#             binary wire out -- the native backends), or 'self' (the plug
#             carries its own build-wasm.ps1 and this manifest only names the
#             artifact)
#   withLir   bundle the compiler's LIR, as the native backends' network build
#             does; absent means no
#   common    one codex/plugs/common chapter bundled by name (PlugManifest)
#   decks     reservation for the bundle's IR compile; absent means the default
#   chapters  build-plug-wasm.ps1 -Chapters value; Name:Sec1|Sec2 drops
#             sections (the transport half of a network entry chapter)
#   ship      $false keeps a module out of the page (built and graded, not
#             shipped); absent means shipped. Only elf today: its lens is dark
#             because nothing emits the payload it reads (plugs 1.92).

$PageModules = @(
    # -- text and UI lenses, IR transport -------------------------------------
    @{ plug = 'javascript'; file = 'javascript-stdio.wasm'; transport = 'ir'; chapters = 'JavaScriptEmitter,JavaScriptStdio' }
    @{ plug = 'csharp';     file = 'csharp-stdio.wasm';     transport = 'ir'; chapters = 'CsAst,CSharpEmitter,CSharpEmitterExpressions,CSharpPlug:Network Config|Drain|Body,CSharpStdio' }
    @{ plug = 'python';     file = 'python-stdio.wasm';     transport = 'ir'; chapters = 'PythonEmitter,PythonStdio' }
    @{ plug = 'typescript'; file = 'typescript-stdio.wasm'; transport = 'ir'; chapters = 'TypeScriptEmitter,TypeScriptStdio' }
    @{ plug = 'zig';        file = 'zig-stdio.wasm';        transport = 'ir'; chapters = 'ZigEmitter,ZigStdio' }
    @{ plug = 'rust';       file = 'rust-stdio.wasm';       transport = 'ir'; chapters = 'RustEmitter,RustStdio' }
    @{ plug = 'go';         file = 'go-stdio.wasm';         transport = 'ir'; chapters = 'GoEmitter,GoStdio' }
    @{ plug = 'java';       file = 'java-stdio.wasm';       transport = 'ir'; chapters = 'JavaEmitter,JavaStdio' }
    @{ plug = 'kotlin';     file = 'kotlin-stdio.wasm';     transport = 'ir'; chapters = 'KotlinEmitter,KotlinStdio' }
    @{ plug = 'swift';      file = 'swift-stdio.wasm';      transport = 'ir'; chapters = 'SwiftEmitter,SwiftStdio' }
    @{ plug = 'haskell';    file = 'haskell-stdio.wasm';    transport = 'ir'; chapters = 'HaskellEmitter,HaskellStdio' }
    @{ plug = 'ruby';       file = 'ruby-stdio.wasm';       transport = 'ir'; chapters = 'RubyEmitter,RubyStdio' }
    @{ plug = 'ocaml';      file = 'ocaml-stdio.wasm';      transport = 'ir'; chapters = 'OCamlEmitter,OCamlStdio' }
    @{ plug = 'lua';        file = 'lua-stdio.wasm';        transport = 'ir'; chapters = 'LuaEmitter,LuaStdio' }
    @{ plug = 'php';        file = 'php-stdio.wasm';        transport = 'ir'; chapters = 'PhpEmitter,PhpStdio' }
    @{ plug = 'scala';      file = 'scala-stdio.wasm';      transport = 'ir'; chapters = 'ScalaEmitter,ScalaStdio' }
    @{ plug = 'elixir';     file = 'elixir-stdio.wasm';     transport = 'ir'; chapters = 'ElixirEmitter,ElixirStdio' }
    @{ plug = 'cobol';      file = 'cobol-stdio.wasm';      transport = 'ir'; chapters = 'CobolEmitter,CobolStdio' }
    @{ plug = 'fortran';    file = 'fortran-stdio.wasm';    transport = 'ir'; chapters = 'FortranEmitter,FortranStdio' }
    @{ plug = 'html';       file = 'html-stdio.wasm';       transport = 'ir'; chapters = 'HtmlEmitter,HtmlStdio' }
    @{ plug = 'react';      file = 'react-stdio.wasm';      transport = 'ir'; chapters = 'ReactEmitter,ReactStdio' }
    @{ plug = 'vue';        file = 'vue-stdio.wasm';        transport = 'ir'; chapters = 'VueEmitter,VueStdio' }
    @{ plug = 'swiftui';    file = 'swiftui-stdio.wasm';    transport = 'ir'; chapters = 'SwiftUIEmitter,SwiftUIStdio' }
    @{ plug = 'winforms';   file = 'winforms-stdio.wasm';   transport = 'ir'; chapters = 'WinFormsEmitter,WinFormsStdio' }
    @{ plug = 'angular';    file = 'angular-stdio.wasm';    transport = 'ir'; chapters = 'AngularEmitter,AngularStdio' }
    @{ plug = 'svelte';     file = 'svelte-stdio.wasm';     transport = 'ir'; chapters = 'SvelteEmitter,SvelteStdio' }
    @{ plug = 'wpf';        file = 'wpf-stdio.wasm';        transport = 'ir'; chapters = 'CsAst,WpfEmitter,WpfStdio' }
    @{ plug = 'qt';         file = 'qt-stdio.wasm';         transport = 'ir'; chapters = 'QtEmitter,QtStdio' }
    @{ plug = 'gtk';        file = 'gtk-stdio.wasm';        transport = 'ir'; chapters = 'GtkEmitter,GtkStdio' }
    @{ plug = 'compose';    file = 'compose-stdio.wasm';    transport = 'ir'; chapters = 'ComposeEmitter,ComposeStdio' }
    @{ plug = 'flutter';    file = 'flutter-stdio.wasm';    transport = 'ir'; chapters = 'FlutterEmitter,FlutterStdio' }
    @{ plug = 'electron';   file = 'electron-stdio.wasm';   transport = 'ir'; chapters = 'ElectronEmitter,ElectronStdio' }
    @{ plug = 'maui';       file = 'maui-stdio.wasm';       transport = 'ir'; chapters = 'MauiEmitter,MauiStdio' }
    @{ plug = 'ada';        file = 'ada-stdio.wasm';        transport = 'ir'; chapters = 'AdaEmitter,AdaStdio' }
    @{ plug = 'clojure';    file = 'clojure-stdio.wasm';    transport = 'ir'; chapters = 'ClojureEmitter,ClojureStdio' }
    @{ plug = 'd';          file = 'd-stdio.wasm';          transport = 'ir'; chapters = 'DEmitter,DStdio' }
    @{ plug = 'groovy';     file = 'groovy-stdio.wasm';     transport = 'ir'; chapters = 'GroovyEmitter,GroovyStdio' }
    @{ plug = 'julia';      file = 'julia-stdio.wasm';      transport = 'ir'; chapters = 'JuliaEmitter,JuliaStdio' }
    @{ plug = 'nim';        file = 'nim-stdio.wasm';        transport = 'ir'; chapters = 'NimEmitter,NimStdio' }
    @{ plug = 'objc';       file = 'objc-stdio.wasm';       transport = 'ir'; chapters = 'ObjCEmitter,ObjCStdio' }
    @{ plug = 'pascal';     file = 'pascal-stdio.wasm';     transport = 'ir'; chapters = 'PascalEmitter,PascalStdio' }
    @{ plug = 'perl';       file = 'perl-stdio.wasm';       transport = 'ir'; chapters = 'PerlEmitter,PerlStdio' }
    @{ plug = 'scheme';     file = 'scheme-stdio.wasm';     transport = 'ir'; chapters = 'SchemeEmitter,SchemeStdio' }
    @{ plug = 'ptx';        file = 'ptx-stdio.wasm';        transport = 'ir'; chapters = 'PtxEmitter,PtxStdio' }
    @{ plug = 'wgsl';       file = 'wgsl-stdio.wasm';       transport = 'ir'; chapters = 'WgslEmitter,WgslStdio' }

    # -- binary plugs, bytes transport (a compiled payload, not IR) -----------
    # elf's chapter list is the one build-plug-wasm.ps1's own header documents;
    # pe and img follow the same shape: writers whole, the network entry minus
    # its transport sections, then the Stdio chapter. PePlug keeps its ARM64
    # Wire Parser and Byte Slicing sections (PeStdio calls both); ImgPlug keeps
    # Read Helpers.
    @{ plug = 'pe';  file = 'pe-bytes.wasm';  transport = 'bytes'; chapters = 'ByteHelpers,PeWriter,Arm64PeWriter,PePlug:Network Config|Spin|Drain|Body,PeStdio' }
    @{ plug = 'img'; file = 'img-bytes.wasm'; transport = 'bytes'; chapters = 'ByteHelpers,PlugChain,Fat16Writer,Fat32Writer,GptWriter,ImgPlug:Network Config|Streaming Send|Drain|Body,ImgStdio' }
    @{ plug = 'elf'; file = 'elf-bytes.wasm'; transport = 'bytes'; chapters = 'ByteHelpers,PlugChain,ElfWriter,DwarfWriter,ElfPlug:Network Config|Drain|Body,ElfStdio'; ship = $false }

    # -- native backends, IR in and a binary WIRE out -------------------------
    # The board lanes -- riscv for boards, arm64 for boards AND phones. These
    # read IR text like a lens and answer the wire the
    # bytes plugs consume -- `[4B code-len][4B data-len][4B func-count][code]
    # [data][func table]` -- which is what `elf-bytes.wasm` has been waiting
    # for: its lens was dark only because nothing in a browser could emit its
    # payload. Chained, the page turns source into a board kernel without a
    # host: compiler -> riscv wire -> ELF.
    #
    # They need what their NETWORK build needs and no lens does: the compiler's
    # LIR (withLir), the boot-grant helper (common), and a bigger reservation
    # for the IR compile (decks) -- without it that compile dies in __alloc at
    # about 542 MB, the same way the network build would without its -Decks 160.
    @{ plug = 'riscv'; file = 'riscv-stdio.wasm'; transport = 'irbytes'
       chapters = 'RiscVRuntime,RiscVCodeGen,RiscVCodeGen2,RiscVLir,RiscVCodeGen3,RiscVDisasm,RiscVStdio'
       withLir = $true; common = 'PlugManifest'; decks = 160; ship = $false }
    @{ plug = 'arm64'; file = 'arm64-stdio.wasm'; transport = 'irbytes'
       chapters = 'Arm64Runtime,Arm64CodeGen,Arm64CodeGen2,Arm64Lir,Arm64CodeGen3,Arm64Disasm,Arm64Stdio'
       withLir = $true; common = 'PlugManifest'; decks = 160; ship = $false }

    # -- plugs that carry their own wasm builder ------------------------------
    # evidence reads raw CCE lines on stdin (no PlugStdio); its builder is
    # codex/plugs/evidence/build-wasm.ps1 and the chapter list lives there.
    @{ plug = 'evidence'; file = 'evidence-stdio.wasm'; transport = 'self'; chapters = '' }
)
