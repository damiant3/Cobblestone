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
Copy-Item (Join-Path $PSScriptRoot 'page\examples.json') (Join-Path $OutDir 'examples.json') -Force

# 3d. The target plugs, each a wasm module reading IR on stdin. Built by
# codex/plugs/common/build-plug-wasm.ps1; a missing one leaves its lens dark
# rather than failing the page build, since the page fetches them on demand.
foreach ($p in @(@{ plug = 'javascript'; file = 'javascript-stdio.wasm' },
                 @{ plug = 'csharp';     file = 'csharp-stdio.wasm' },
                 @{ plug = 'evidence';   file = 'evidence-stdio.wasm' })) {
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
foreach ($f in @('codex-compiler.wasm', 'javascript-stdio.wasm', 'csharp-stdio.wasm', 'evidence-stdio.wasm')) {
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

# 5. Inject the hash into the page.
$html = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'page\index.html'))
$html = $html.Replace('__X86_HASH__', $hash)
[IO.File]::WriteAllText((Join-Path $OutDir 'index.html'), $html, [Text.UTF8Encoding]::new($false))

Write-Host "[page] $OutDir"
Write-Host "[page] module  : $((Get-Item $wasm).Length) bytes"
Write-Host "[page] source  : $($srcBytes.Length) bytes"
Write-Host "[page] anchor  : $hash ($($clean.Length) chars cleaned)"
