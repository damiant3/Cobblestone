# Assemble the self-compile page: the compiler as a wasm module, its own
# source, and index.html with the bare-metal reference hash injected.
#
# The hash is computed HERE, at page build, by running the same source
# through the x86-64 kernel and cleaning both sides the same way the page
# does. Hard-coding it would go stale with the next compiler change; a page
# that asserts byte-identity must carry the truth measured from the exact
# bytes it serves.
[CmdletBinding()]
param(
    [string]$Kernel,
    [string]$Source,
    [string]$OutDir
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..')).Path
if (-not $Kernel) { $Kernel = Join-Path $Repo 'seed\Codex.cdx' }
if (-not $Source) { $Source = Join-Path $Repo 'build\output\Codex.codex' }
if (-not $OutDir) { $OutDir = Join-Path $PSScriptRoot 'build-output\page' }

foreach ($tool in @('wat2wasm')) {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        Write-Host "REFUSE: $tool is not on PATH."; exit 2
    }
}
if (-not (Test-Path -PathType Leaf $Source)) {
    Write-Host "REFUSE: no concatenated compiler source at $Source (a gate's source-concat phase produces it)."; exit 2
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$wat = Join-Path $OutDir 'codex-compiler.wat'
$wasm = Join-Path $OutDir 'codex-compiler.wasm'

# 1. Source -> IR -> WAT through the plug, against the named kernel.
& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'run.ps1') -Src $Source -Out $wat -Kernel $Kernel
if ($LASTEXITCODE -ne 0) { Write-Host 'FAIL: plug emission'; exit 1 }

# 2. Assemble. --enable-tail-call: the emitter uses return_call so the
# module survives a browser's 1 MB stack; the binary runs unflagged.
& wat2wasm --enable-tail-call $wat -o $wasm
if ($LASTEXITCODE -ne 0) { Write-Host 'FAIL: wat2wasm'; exit 1 }

# 3. The source the page feeds back to the module.
Copy-Item $Source (Join-Path $OutDir 'Codex.codex') -Force

# 3b. The backdrop: a painted cobblestone roundabout at a crossroads.
Copy-Item (Join-Path $PSScriptRoot 'page\roundabout.jpg') (Join-Path $OutDir 'roundabout.jpg') -Force

# 3c. Prism rides beside the self-compile page so it can fetch the SAME
# module from its own directory rather than shipping a second copy.
Copy-Item (Join-Path $PSScriptRoot 'page\prism.html')    (Join-Path $OutDir 'prism.html') -Force
# An example is only good if it compiles in THIS module, and step 4c below
# grades every one of them. build/compile.ps1 is a MORE GENEROUS bed and will
# pass programs the page refuses: it bundles the whole foreword, where the
# page's unit is flat, so `cites Foreword chapter MathLib` resolves there and
# fails here CDX3007, and dropping the cite only moves it to CDX3002 because
# math-mod is a foreword function and not a builtin. That is what the prelude
# field is for, and gcd and collatz use it (reek, 2026-08-27, caught by Damian
# after compile.ps1 had reported both green).
Copy-Item (Join-Path $PSScriptRoot 'page\examples.json') (Join-Path $OutDir 'examples.json') -Force
# A copy of a Perforce file inherits its read-only bit, and step 3e writes the
# embedded page back over this one. Without this the build dies at the write
# with an access error naming the output rather than the cause.
Set-ItemProperty (Join-Path $OutDir 'prism.html') -Name IsReadOnly -Value $false

# 3d. The target plugs, each a wasm module the page fetches. The list is the
# one manifest (page-lenses.ps1, PRISM-7 stage 0), built by
# build-page-modules.ps1; a missing one leaves its lens dark rather than
# failing the page build, since the page fetches them on demand.
. (Join-Path $PSScriptRoot 'page-lenses.ps1')
$shippedModules = @($PageModules | Where-Object { -not ($_.ContainsKey('ship')) -or $_.ship })
foreach ($p in $shippedModules) {
    $from = Join-Path $Repo ("codex\plugs\{0}\build-output\{1}" -f $p.plug, $p.file)
    if (Test-Path -PathType Leaf $from) {
        Copy-Item $from (Join-Path $OutDir $p.file) -Force
        Write-Host ("[page] plug    : {0} ({1} bytes)" -f $p.file, (Get-Item $from).Length)
    } else {
        Write-Host ("[page] plug    : {0} ABSENT; its lens stays dark" -f $p.file)
    }
}

# 3e. Prism is ONE self-contained file. A browser refuses fetch on a file:
# origin, so a fetching page cannot work from disk however it is written, and
# a separate offline twin only moved the confusion: the copy people open is
# still the one that does not work. Embedding costs less than it sounds,
# because the fetching page already pulled 1.35 MB of modules and 215 KB of
# examples; what is added is base64 overhead, against three saved round trips.
$offlineSrc = Join-Path $OutDir 'prism.html'
# The marker must appear EXACTLY once. It first appeared twice, the second time
# inside a comment describing it, so the replace pasted two megabytes into the
# middle of the main script and cut it in half: the page then loaded with no
# syntax error, half its functions defined, and nothing to say why.
$markerCount = ([regex]::Matches([IO.File]::ReadAllText($offlineSrc), '<!--EMBED-->')).Count
if ($markerCount -ne 1) {
    Write-Host "FAIL: prism.html holds $markerCount EMBED markers; it must hold exactly one."; exit 1
}
$embed = [System.Text.StringBuilder]::new()
[void]$embed.AppendLine('<script>')
[void]$embed.AppendLine('window.__EMBED = {')
# Codex.codex rides in the embed too: it is the compiler's own concatenated
# source, and the page offers it as a preset so the self-compile happens HERE
# (the index.html purpose, being subsumed). It is text, not a module, and the
# page decodes it back to text on selection.
foreach ($f in (@('codex-compiler.wasm', 'Codex.codex') + @($shippedModules | ForEach-Object { $_.file }))) {
    $p = Join-Path $OutDir $f
    if (-not (Test-Path -PathType Leaf $p)) { continue }
    $b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($p))
    [void]$embed.AppendLine(('  "{0}": "{1}",' -f $f, $b64))
}
[void]$embed.AppendLine('};')
$exJson = Join-Path $OutDir 'examples.json'
if (Test-Path -PathType Leaf $exJson) {
    [void]$embed.AppendLine('window.__EXAMPLES = ' + ([IO.File]::ReadAllText($exJson)) + ';')
}
[void]$embed.AppendLine('</script>')
$offline = ([IO.File]::ReadAllText($offlineSrc)).Replace('<!--EMBED-->', $embed.ToString())
# The backdrop goes in too, or a downloaded prism.html is a working compiler
# with no picture behind it. That makes this ONE file the whole thing: hand
# somebody the file and it opens.
$bg = Join-Path $OutDir 'roundabout.jpg'
if (Test-Path -PathType Leaf $bg) {
    $bgB64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($bg))
    $offline = $offline.Replace("url('roundabout.jpg')", "url('data:image/jpeg;base64,$bgB64')")
}
[IO.File]::WriteAllText($offlineSrc, $offline, [Text.UTF8Encoding]::new($false))
Write-Host ("[page] prism   : self-contained, {0:N0} bytes" -f (Get-Item $offlineSrc).Length)
# A twin was shipped for one build and is gone; clear a stale one so a rebuilt
# tree does not keep serving the file that could not work.
$stale = Join-Path $OutDir 'prism-offline.html'
if (Test-Path -PathType Leaf $stale) { Remove-Item $stale -Force }

# 4. The bare-metal truth for the SAME source and mode line the page uses.
$modeHeader = [Text.Encoding]::ASCII.GetBytes("TEXT decks=125`n")
$srcBytes = [IO.File]::ReadAllBytes($Source)
$stdin = New-Object byte[] ($modeHeader.Length + $srcBytes.Length + 1)
[Buffer]::BlockCopy($modeHeader, 0, $stdin, 0, $modeHeader.Length)
[Buffer]::BlockCopy($srcBytes, 0, $stdin, $modeHeader.Length, $srcBytes.Length)
$stdinFile = Join-Path $OutDir 'x86-truth.stdin'
$truthFile = Join-Path $OutDir 'x86-truth.out'
[IO.File]::WriteAllBytes($stdinFile, $stdin)
& (Join-Path $Repo 'tools\codex-vm.exe') -kernel $Kernel -headless -mem 3072 -input $stdinFile -output $truthFile 2>&1 | Out-Null
if (-not (Test-Path $truthFile) -or (Get-Item $truthFile).Length -lt 1000000) {
    Write-Host 'FAIL: the x86 truth run produced no plausible output; no hash to anchor the page to.'; exit 1
}
$raw = [IO.File]::ReadAllBytes($truthFile)
if ($raw[0] -eq 1) { $raw = $raw[1..($raw.Length - 1)] }
# -cnotmatch: the filter must be CASE-SENSITIVE or it swallows emitted
# definitions that merely begin with Stack or Heap -- measured, four lines
# and 90 characters of `heap-hwm-addr` and `stack-min-rsp-addr`. The page's
# JavaScript startsWith is exact, and the anchor must be computed with the
# page's own filter or the page refutes a truth it was handed pre-broken.
$clean = ((([Text.Encoding]::UTF8.GetString($raw)) -replace "`r`n", "`n") -split "`n" |
    Where-Object { $_ -cnotmatch '^(WD:|PM:|HEAP|STACK)' }) -join "`n"
$sha = [Security.Cryptography.SHA256]::Create()
$hash = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($clean))).Replace('-', '')
Remove-Item $stdinFile, $truthFile -Force

# 4b. CDX mode must WORK in the module, not merely not trap, because the tab's
# save/download buttons hand the user a binary and a wrong binary is worse
# than a refused one (PRISM-6 (a)). The module used to trap here: emit_cdx ->
# compile_frontend_cdx -> pmap_self_test -> __self-type-defs, which exists
# only for the x86 pointer-map machinery and has no wasm form. That is fixed
# by the self-test standing down where there is no pointer map to walk, and
# this arm is what keeps it fixed: one small program, both targets, payloads
# compared byte for byte. Cheap (a couple of seconds) against a page build
# measured in minutes, and it fails the build rather than shipping a page
# whose download button produces rubbish.
function Get-CdxPayload([string]$path) {
    if (-not (Test-Path -PathType Leaf $path)) { return $null }
    $b = [IO.File]::ReadAllBytes($path)
    if ($b.Length -eq 0) { return $null }
    if ($b[0] -eq 1) { $b = $b[1..($b.Length - 1)] }
    $idx = ([Text.Encoding]::ASCII.GetString($b)).IndexOf('SIZE:')
    if ($idx -lt 0) { return $null }
    $nl = [Array]::IndexOf($b, [byte]10, $idx)
    if ($nl -lt 0) { return $null }
    $n = 0
    if (-not [int]::TryParse((([Text.Encoding]::ASCII.GetString($b, $idx + 5, $nl - ($idx + 5))).Trim() -replace '[^0-9].*$', ''), [ref]$n)) { return $null }
    if (($n -le 0) -or (($nl + 1 + $n) -gt $b.Length)) { return $null }
    $out = New-Object byte[] $n
    [Array]::Copy($b, $nl + 1, $out, 0, $n)
    return $out
}
$cdxProg = "Chapter: PageCdxArm`n`n  opening : [Console] Nothing = act`n   print-line-uni `"cdx`"`n  end`n"
$cdxHdr = [Text.Encoding]::ASCII.GetBytes("CDX`n")
$cdxSrc = [Text.Encoding]::UTF8.GetBytes($cdxProg)
$cdxIn = New-Object byte[] ($cdxHdr.Length + $cdxSrc.Length + 1)
[Buffer]::BlockCopy($cdxHdr, 0, $cdxIn, 0, $cdxHdr.Length)
[Buffer]::BlockCopy($cdxSrc, 0, $cdxIn, $cdxHdr.Length, $cdxSrc.Length)
$cdxStdin = Join-Path $OutDir 'cdx-arm.stdin'
$cdxWasmOut = Join-Path $OutDir 'cdx-arm.wasm.out'
$cdxX86Out = Join-Path $OutDir 'cdx-arm.x86.out'
[IO.File]::WriteAllBytes($cdxStdin, $cdxIn)
$wp = Start-Process -FilePath 'wasmtime' `
      -ArgumentList @('-W', 'max-wasm-stack=16777216', $wasm) -NoNewWindow -PassThru `
      -RedirectStandardInput $cdxStdin -RedirectStandardOutput $cdxWasmOut -RedirectStandardError "$cdxWasmOut.err"
$wp.WaitForExit(300000) | Out-Null
& (Join-Path $Repo 'tools\codex-vm.exe') -kernel $Kernel -headless -mem 3072 -input $cdxStdin -output $cdxX86Out 2>&1 | Out-Null
$cdxW = Get-CdxPayload $cdxWasmOut
$cdxX = Get-CdxPayload $cdxX86Out
if ($null -eq $cdxX) {
    Write-Host 'FAIL: the x86 CDX arm produced no payload, so the arm has no truth to grade against.'; exit 1
}
if ($null -eq $cdxW) {
    $trap = if (Test-Path "$cdxWasmOut.err") { (Get-Content "$cdxWasmOut.err" -Raw).Trim() } else { '' }
    Write-Host "FAIL: the module produced no CDX payload. $trap"; exit 1
}
if ($cdxW.Length -ne $cdxX.Length) {
    Write-Host "FAIL: CDX payload sizes differ, wasm $($cdxW.Length) vs x86-64 $($cdxX.Length)."; exit 1
}
$cdxDiff = -1
for ($ci = 0; $ci -lt $cdxW.Length; $ci++) { if ($cdxW[$ci] -ne $cdxX[$ci]) { $cdxDiff = $ci; break } }
if ($cdxDiff -ge 0) {
    Write-Host "FAIL: CDX payloads differ at byte $cdxDiff of $($cdxW.Length)."; exit 1
}
Remove-Item $cdxStdin, $cdxWasmOut, $cdxX86Out, "$cdxWasmOut.err" -Force -ErrorAction SilentlyContinue
Write-Host ("[page] cdx arm : {0:N0} payload bytes, byte-identical to x86-64" -f $cdxW.Length)

# 4c. Every example in the dropdown must compile in THIS module. Same argument
# as 4b: a visitor picks an example and presses Compile, and one that refuses is
# worse than one that is missing. The trap this closes is that build/compile.ps1
# bundles the whole foreword where the page's unit is flat, so it passes
# programs the page refuses; two examples shipped that way and Damian found them
# rather than a runner. `-Calibrate` runs first and is not ceremony -- it mangles
# each subject's Chapter header and requires every example to refuse, because a
# harness that reports 57 green without having been shown able to report a red
# is a screen that cannot fail. Both arms together measured 14 s and 71 s in
# two runs an hour apart on 2026-08-28, the spread being other lanes' VMs on a
# shared box rather than anything about the arm; either way it is seconds
# against a page build measured in minutes. Re-measure before quoting (L-COUNT).
$exArm = Join-Path $PSScriptRoot 'page-example-test.ps1'
foreach ($arm in @(@{ label = 'calibrate'; args = @('-Calibrate') }, @{ label = 'compile'; args = @() })) {
    # Keep the whole output and print the summary on a pass, all of it on a
    # failure: the per-example rows ARE the diagnosis, and a build that fails
    # here having thrown them away sends the reader back to run the arm by hand.
    $exOut = @(& pwsh -NoProfile -File $exArm -Module $wasm -Examples (Join-Path $PSScriptRoot 'page\examples.json') @($arm.args))
    $failed = ($LASTEXITCODE -ne 0)
    foreach ($line in $(if ($failed) { $exOut } else { $exOut | Select-Object -Last 2 })) {
        Write-Host ("[page] ex {0,-9}: {1}" -f $arm.label, $line)
    }
    if ($failed) {
        Write-Host "FAIL: the page's own examples did not pass their $($arm.label) arm."
        exit 1
    }
}

# 5. Inject the hash into the page.
$html = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'page\index.html'))
$html = $html.Replace('__X86_HASH__', $hash)
[IO.File]::WriteAllText((Join-Path $OutDir 'index.html'), $html, [Text.UTF8Encoding]::new($false))

Write-Host "[page] $OutDir"
Write-Host "[page] module  : $((Get-Item $wasm).Length) bytes"
Write-Host "[page] source  : $($srcBytes.Length) bytes"
Write-Host "[page] anchor  : $hash ($($clean.Length) chars cleaned)"
