# Grade every lens the compile page ships: run each plug's wasm module over one
# subject's IR and require it to answer.
#
# The page fetches a module per target and leaves a lens dark when the file is
# absent, so a lens that assembles but traps on real IR looks exactly like a
# lens that works until somebody presses the pill. Nothing else in the tree runs
# these modules; `build-page.ps1` only copies whatever it finds.
#
# -Calibrate is the half that makes a green mean anything. It feeds each module
# a subject that is not IR at all and requires the module to FAIL. A lens that
# answers there is not reading its input, and its pass on the real subject was
# free.
[CmdletBinding()]
param(
    [string]$Subject,
    [string]$Kernel,
    [string[]]$Only,
    [switch]$Calibrate
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..')).Path
if (-not $Kernel) { $Kernel = Join-Path $Repo 'seed\Codex.cdx' }

foreach ($tool in @('wasmtime')) {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        Write-Host "REFUSE: $tool is not on PATH."; exit 2
    }
}

# The page's lens list. Plug name, the module it fetches, and the chapters the
# module is built from -- the chapter list is here because it lives nowhere
# else: build-plug-wasm.ps1 takes it as an argument and no script calls it.
$LENSES = @(
    @{ plug = 'javascript'; file = 'javascript-stdio.wasm'; chapters = 'JavaScriptEmitter,JavaScriptStdio' }
    @{ plug = 'csharp';     file = 'csharp-stdio.wasm';     chapters = 'CSharpEmitter,CSharpPlug:Network Config|Drain|Body,CSharpStdio' }
    @{ plug = 'python';     file = 'python-stdio.wasm';     chapters = 'PythonEmitter,PythonStdio' }
    @{ plug = 'typescript'; file = 'typescript-stdio.wasm'; chapters = 'TypeScriptEmitter,TypeScriptStdio' }
    @{ plug = 'zig';        file = 'zig-stdio.wasm';        chapters = 'ZigEmitter,ZigStdio' }
    @{ plug = 'rust';       file = 'rust-stdio.wasm';       chapters = 'RustEmitter,RustStdio' }
    @{ plug = 'go';         file = 'go-stdio.wasm';         chapters = 'GoEmitter,GoStdio' }
    @{ plug = 'java';       file = 'java-stdio.wasm';       chapters = 'JavaEmitter,JavaStdio' }
    @{ plug = 'kotlin';     file = 'kotlin-stdio.wasm';     chapters = 'KotlinEmitter,KotlinStdio' }
    @{ plug = 'swift';      file = 'swift-stdio.wasm';      chapters = 'SwiftEmitter,SwiftStdio' }
    @{ plug = 'haskell';    file = 'haskell-stdio.wasm';    chapters = 'HaskellEmitter,HaskellStdio' }
    @{ plug = 'ruby';       file = 'ruby-stdio.wasm';       chapters = 'RubyEmitter,RubyStdio' }
    @{ plug = 'ocaml';      file = 'ocaml-stdio.wasm';      chapters = 'OCamlEmitter,OCamlStdio' }
    @{ plug = 'lua';        file = 'lua-stdio.wasm';        chapters = 'LuaEmitter,LuaStdio' }
    @{ plug = 'php';        file = 'php-stdio.wasm';        chapters = 'PhpEmitter,PhpStdio' }
    @{ plug = 'scala';      file = 'scala-stdio.wasm';      chapters = 'ScalaEmitter,ScalaStdio' }
    @{ plug = 'elixir';     file = 'elixir-stdio.wasm';     chapters = 'ElixirEmitter,ElixirStdio' }
    @{ plug = 'cobol';      file = 'cobol-stdio.wasm';      chapters = 'CobolEmitter,CobolStdio' }
    @{ plug = 'fortran';    file = 'fortran-stdio.wasm';    chapters = 'FortranEmitter,FortranStdio' }
    @{ plug = 'html';       file = 'html-stdio.wasm';       chapters = 'HtmlEmitter,HtmlStdio' }
    @{ plug = 'react';      file = 'react-stdio.wasm';      chapters = 'ReactEmitter,ReactStdio' }
    @{ plug = 'vue';        file = 'vue-stdio.wasm';        chapters = 'VueEmitter,VueStdio' }
    @{ plug = 'swiftui';    file = 'swiftui-stdio.wasm';    chapters = 'SwiftUIEmitter,SwiftUIStdio' }
    @{ plug = 'winforms';   file = 'winforms-stdio.wasm';   chapters = 'WinFormsEmitter,WinFormsStdio' }
    @{ plug = 'angular';    file = 'angular-stdio.wasm';    chapters = 'AngularEmitter,AngularStdio' }
    @{ plug = 'svelte';     file = 'svelte-stdio.wasm';     chapters = 'SvelteEmitter,SvelteStdio' }
    @{ plug = 'wpf';        file = 'wpf-stdio.wasm';        chapters = 'CsAst,WpfEmitter,WpfStdio' }
    @{ plug = 'qt';         file = 'qt-stdio.wasm';         chapters = 'QtEmitter,QtStdio' }
    @{ plug = 'gtk';        file = 'gtk-stdio.wasm';        chapters = 'GtkEmitter,GtkStdio' }
    @{ plug = 'compose';    file = 'compose-stdio.wasm';    chapters = 'ComposeEmitter,ComposeStdio' }
    @{ plug = 'flutter';    file = 'flutter-stdio.wasm';    chapters = 'FlutterEmitter,FlutterStdio' }
    @{ plug = 'electron';   file = 'electron-stdio.wasm';   chapters = 'ElectronEmitter,ElectronStdio' }
    @{ plug = 'maui';       file = 'maui-stdio.wasm';       chapters = 'MauiEmitter,MauiStdio' }
)

# pwsh -File hands an array argument over as ONE string, so -Only a,b arrives
# as the single element 'a,b' and matches no plug. Split it, and REFUSE on a
# name that is in no lens: the filter selecting nothing used to run zero
# lenses and still print '0 failed' and exit 0, which is a screen that cannot
# fail dressed as a pass.
$Only = @($Only | ForEach-Object { $_ -split ',' } | Where-Object { $_ -ne '' })
if ($Only) {
    $known = @($LENSES | ForEach-Object { $_.plug })
    $unknown = @($Only | Where-Object { $known -notcontains $_ })
    if ($unknown) { Write-Host ("REFUSE: -Only names no lens: {0}" -f ($unknown -join ', ')); exit 2 }
}
$work = Join-Path $PSScriptRoot 'build-output\lens-test'
New-Item -ItemType Directory -Force -Path $work | Out-Null

# The subject. Default is the page's own richest example, so the grading input
# is one a visitor can actually paste into the box.
if (-not $Subject) {
    $ex = (Get-Content (Join-Path $PSScriptRoot 'page\examples.json') -Raw | ConvertFrom-Json) |
          Where-Object { $_.name -eq 'accumulator-corpus' } | Select-Object -First 1
    if (-not $ex) { Write-Host 'REFUSE: no accumulator-corpus in examples.json and no -Subject given.'; exit 2 }
    $Subject = Join-Path $work 'subject.codex'
    [IO.File]::WriteAllText($Subject, $ex.source, [Text.UTF8Encoding]::new($false))
}

$irFile = Join-Path $work 'subject.ir'
$logFile = Join-Path $work 'subject.log'
& pwsh -NoProfile -File (Join-Path $Repo 'build\compile.ps1') `
    -Src $Subject -Out (Join-Path $work 'subject.out') -Log $logFile -IrUni -Kernel $Kernel | Out-Null
$log = [IO.File]::ReadAllText($logFile)
$a = $log.IndexOf('IR-BEGIN'); $b = $log.IndexOf('IR-END')
if ($a -lt 0 -or $b -lt $a) {
    Write-Host "REFUSE: the subject produced no IR, so there is nothing to hand a lens. See $logFile"
    exit 2
}
$ir = $log.Substring($a + 9, $b - ($a + 9)).Trim()
[IO.File]::WriteAllText($irFile, $ir, [Text.UTF8Encoding]::new($false))

# The calibration input is deliberately not IR. A lens that answers on this is
# not parsing what it was handed, so its pass on the real subject proves
# nothing about the lens.
$badFile = Join-Path $work 'not-ir.txt'
[IO.File]::WriteAllText($badFile, "this is not an IR chapter`nand it never was`n", [Text.UTF8Encoding]::new($false))

# NOT $input: that name is a PowerShell automatic variable holding the pipeline
# enumerator, and assigning to it hands Start-Process a null path.
$inputFile = if ($Calibrate) { $badFile } else { $irFile }

# The subject's own definition names are the evidence a lens has to produce.
# They come out of the IR rather than out of the source, so what is asked for
# is exactly what the lens was handed.
$script:SubjectNames = @([regex]::Matches($ir, '\(def "([^"]+)"') |
                         ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
# A floor rather than all of them: an emitter is entitled to inline, rename or
# drop a definition, and demanding every name would make the harness fail on
# correct output. A quarter is far above what boilerplate can reach (measured:
# zero) and far below what a real emission produces.
$script:NameFloor = [Math]::Max(2, [int]($script:SubjectNames.Count / 4))
if ($script:SubjectNames.Count -eq 0) {
    Write-Host 'REFUSE: the subject IR carries no definition names, so there is no evidence to ask a lens for.'
    exit 2
}
Write-Host ("[lens] subject : {0}" -f (Split-Path $Subject -Leaf))
Write-Host ("[lens] IR      : {0:N0} bytes, {1} definition names, floor {2}" -f `
            (Get-Item $irFile).Length, $script:SubjectNames.Count, $script:NameFloor)
if ($Calibrate) { Write-Host '[lens] CALIBRATION: every lens must FAIL on input that is not IR.' }

$rows = @()
foreach ($L in $LENSES) {
    if ($Only -and ($Only -notcontains $L.plug)) { continue }
    $wasm = Join-Path $Repo ("codex\plugs\{0}\build-output\{1}" -f $L.plug, $L.file)
    if (-not (Test-Path -PathType Leaf $wasm)) {
        $rows += [pscustomobject]@{ plug = $L.plug; verdict = 'ABSENT'; bytes = 0; note = 'no module; its lens is dark on the page' }
        continue
    }
    # A module is only as fresh as its last build. A merge-down updates the
    # source and rebuilds nothing, so an emitter measured through a stale
    # module reports the previous revision -- as a false green just as readily
    # as a false red.
    #
    # Only the chapters this module is BUILT FROM count. The plug's network
    # entry chapter sits in the same directory and is not bundled here, so a
    # sweep of the directory reports every lens stale the moment anyone edits
    # the TCP half, which is a guard firing on something it does not guard.
    $sources = @()
    foreach ($ch in ($L.chapters -split ',')) {
        $name = ($ch -split ':')[0]
        foreach ($cand in @((Join-Path $Repo ("codex\plugs\{0}\{1}.codex" -f $L.plug, $name)),
                            (Join-Path $Repo ("codex\plugs\common\{0}.codex" -f $name)))) {
            if (Test-Path -PathType Leaf $cand) { $sources += (Get-Item $cand); break }
        }
    }
    foreach ($shared in @('PlugStdio.codex', 'PlugTypes.codex', 'IRTextParser.codex')) {
        $sp2 = Join-Path $Repo "codex\plugs\common\$shared"
        if (Test-Path -PathType Leaf $sp2) { $sources += (Get-Item $sp2) }
    }
    $newest = $sources | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($newest -and $newest.LastWriteTime -gt (Get-Item $wasm).LastWriteTime) {
        $rows += [pscustomobject]@{ plug = $L.plug; verdict = 'STALE'; bytes = 0
                                    note = "$($newest.Name) is newer than the module; rebuild before grading" }
        continue
    }

    $out = Join-Path $work ("{0}.out" -f $L.plug)
    $err = Join-Path $work ("{0}.err" -f $L.plug)
    $p = Start-Process -FilePath 'wasmtime' `
         -ArgumentList @('-W', 'max-wasm-stack=16777216', $wasm) -NoNewWindow -PassThru `
         -RedirectStandardInput $inputFile -RedirectStandardOutput $out -RedirectStandardError $err
    if (-not $p) {
        $rows += [pscustomobject]@{ plug = $L.plug; verdict = 'NOSTART'; bytes = 0; note = 'wasmtime did not start' }
        continue
    }
    if (-not $p.WaitForExit(300000)) {
        try { $p.Kill() } catch {}
        $rows += [pscustomobject]@{ plug = $L.plug; verdict = 'TIMEOUT'; bytes = 0; note = 'no answer in 300 s' }
        continue
    }
    $len = if (Test-Path $out) { (Get-Item $out).Length } else { 0 }
    # -Raw on an empty file answers $null, not '', and a clean run leaves stderr
    # empty, so the common case is the one that throws.
    $trap = ''
    if (Test-Path $err) {
        $rawErr = Get-Content $err -Raw
        if ($null -ne $rawErr) { $trap = ([string]$rawErr).Trim() }
    }
    $text = if ($len -gt 0) { [IO.File]::ReadAllText($out) } else { '' }

    # Exit 0 with bytes on stdout is NOT the test, and the calibration run is
    # what says so: handed a file that is not IR at all, every lens still exits
    # 0 and still prints its prelude, because an empty parse is not an error.
    # Twenty-three lenses "passed" a screen that could not fail. So the verdict
    # asks for evidence of THIS SUBJECT in the output: how many of the subject's
    # own definition names survive into the emitted text. Names are matched with
    # '-' normalised to '_', which is the one rewrite every target here shares.
    $hits = 0
    $flat = $text -replace '-', '_'
    foreach ($n in $script:SubjectNames) { if ($flat.Contains(($n -replace '-', '_'))) { $hits++ } }

    $verdict = 'OK'; $note = ''
    if ($p.ExitCode -ne 0) { $verdict = 'TRAP'; $note = ($trap -split "`n")[0] }
    elseif ($len -eq 0) { $verdict = 'EMPTY'; $note = 'exit 0 and nothing on stdout' }
    elseif ($text -match '(?m)^REFUSED') { $verdict = 'REFUSED'; $note = (($text -split "`n") | Where-Object { $_ -match '^REFUSED' })[0] }
    elseif ($hits -lt $script:NameFloor) {
        $verdict = 'PRELUDE'
        $note = "only $hits of $($script:SubjectNames.Count) subject names in the output; the lens emitted boilerplate, not this program"
    } else { $note = "$hits/$($script:SubjectNames.Count) names" }
    $rows += [pscustomobject]@{ plug = $L.plug; verdict = $verdict; bytes = $len; note = $note }
}

Write-Host ''
foreach ($r in $rows) {
    Write-Host ("  {0,-12} {1,-8} {2,10:N0}  {3}" -f $r.plug, $r.verdict, $r.bytes, $r.note)
}

$ok = @($rows | Where-Object { $_.verdict -eq 'OK' }).Count
$bad = @($rows | Where-Object { $_.verdict -notin @('OK', 'ABSENT') }).Count
$absent = @($rows | Where-Object { $_.verdict -eq 'ABSENT' }).Count
Write-Host ''
Write-Host ("[lens] {0} answered, {1} failed, {2} absent, of {3} graded" -f $ok, $bad, $absent, $rows.Count)

if ($Calibrate) {
    # The arms are inverted here: answering on input that is not IR is the
    # failure, and every lens failing is the pass.
    if ($ok -gt 0) {
        Write-Host "[lens] CALIBRATION FAILED: $ok lens(es) answered on input that is not IR, so this harness cannot tell a working lens from a blind one."
        exit 1
    }
    Write-Host '[lens] CALIBRATION PASSED: no lens answered on input that is not IR.'
    exit 0
}
if ($bad -gt 0) { exit 1 }
exit 0
