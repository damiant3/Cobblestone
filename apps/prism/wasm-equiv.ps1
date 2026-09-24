# PRISM-1: the wasm compiler (apps/accp/build.ps1, from the depot seed) against
# the native seed through build/compile.ps1, on IR-CCE text-plug and IR-UNI.
# The hosted compiler writes IR-CCE as UTF-8 text, so it is CCE-encoded and cut
# at SIZE: before comparing. Usage: wasm-equiv.ps1 -Sources <files>
param([string]$Wasm = (Join-Path $PSScriptRoot '..\accp\build-output\codex-compiler.wasm'), [string[]]$Sources)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
. (Join-Path $repo 'build\quire-map.ps1')
. (Join-Path $repo 'build\vm-config.ps1')
$work = Join-Path ([IO.Path]::GetTempPath()) 'prism-wasm-equiv'
New-Item -ItemType Directory -Force $work | Out-Null
$cwasm = Join-Path $work 'compiler.cwasm'
if (-not (Test-Path $cwasm) -or (Get-Item $cwasm).LastWriteTime -lt (Get-Item $Wasm).LastWriteTime) {
    & wasmtime compile -W max-wasm-stack=16777216 $Wasm -o $cwasm
}
function Unit([string]$src, [string]$mode) {
    $lines = [IO.File]::ReadAllLines($src)
    $seen = @{}
    foreach ($l in $lines) { if ($l -match '^Chapter:\s*(\w+)--(.+?)\s*$') { $seen["$($matches[1])::$($matches[2])"] = $true } }
    $ordered = Resolve-CiteOrder -RootLines $lines -Repo $repo -SeedSeen $seen
    $sb = [Text.StringBuilder]::new()
    [void]$sb.Append($mode).Append("`n")
    foreach ($l in (Format-CiteChapters -Ordered $ordered)) { [void]$sb.Append($l).Append("`n") }
    foreach ($l in $lines) { [void]$sb.Append($l).Append("`n") }
    [void]$sb.Append([char]4)
    return $sb.ToString()
}
function WasmRun([string]$text, [string]$tag) {
    $in = Join-Path $work 'in.txt'; $out = Join-Path $work ($tag + '.wasm-out')
    [IO.File]::WriteAllText($in, $text + [char]0, [Text.UTF8Encoding]::new($false))
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $p = Start-Process wasmtime -ArgumentList @('run', '--allow-precompiled', '-W', 'max-wasm-stack=16777216,max-memory-size=1073741824', $cwasm) -RedirectStandardInput $in -RedirectStandardOutput $out -RedirectStandardError ($out + '.err') -NoNewWindow -PassThru -Wait
    return @{ bytes = [IO.File]::ReadAllBytes($out); ms = [int]$sw.Elapsed.TotalMilliseconds; code = $p.ExitCode }
}
function IrCceFromStream([byte[]]$b) {
    # SIZE: N, a newline, then exactly N bytes
    # The hosted compiler writes the IR as UTF-8 text; the native one writes
    # CCE bytes. SIZE counts CCE bytes, and diagnostics follow the IR.
    $t = [Text.Encoding]::UTF8.GetString($b)
    $m = [regex]::Match($t, '(?m)^SIZE:\s*(\d+)\r?\n')
    if (-not $m.Success) { return $null }
    $n = [int]$m.Groups[1].Value
    $enc = [byte[]](ConvertTo-CceBytes $t.Substring($m.Index + $m.Length))
    if ($enc.Length -lt $n) { return $null }
    return $enc[0..($n - 1)]
}
function IrUniFromStream([byte[]]$b) {
    $lines = ([Text.Encoding]::UTF8.GetString($b)) -split "`n"
    $from = -1; $to = -1
    for ($i = 0; $i -lt $lines.Count; $i++) { $t = $lines[$i].TrimEnd("`r"); if ($from -lt 0) { if ($t -eq 'IR-BEGIN') { $from = $i } } elseif ($t -eq 'IR-END') { $to = $i; break } }
    if ($from -lt 0 -or $to -le $from) { return $null }
    $sb = [Text.StringBuilder]::new(); for ($i = $from + 1; $i -lt $to; $i++) { [void]$sb.Append($lines[$i].TrimEnd("`r")).Append("`n") }
    return $sb.ToString()
}
$same = 0; $diff = @(); $wms = @(); $nms = @()
foreach ($s in $Sources) {
    $n = [IO.Path]::GetFileNameWithoutExtension($s)
    foreach ($kind in 'cce', 'uni') {
        $native = Join-Path $work "$n.$kind.native"
        $cargs = @(if ($kind -eq 'cce') { '-IrCce', '-Passes', 'text-plug' } else { '-IrUni' })
        $sw = [Diagnostics.Stopwatch]::StartNew()
        pwsh -NoProfile -File (Join-Path $repo 'build\compile.ps1') -Src $s -Out $native -Log "$native.log" -Kernel (Join-Path $repo 'seed\Codex.cdx') @cargs *> $null
        $nms += [int]$sw.Elapsed.TotalMilliseconds
        $mode = if ($kind -eq 'cce') { 'IR-CCE passes=text-plug' } else { 'IR-UNI' }
        try { $unitText = Unit $s 'MODE' } catch { $diff += "$n skipped: $($_.Exception.Message)"; break }
        $w = $null
        foreach ($d in 12, 48, 125) {
            $w = WasmRun ($unitText -replace '^MODE', "$mode decks=$d") "$n.$kind"
            $txt = [Text.Encoding]::UTF8.GetString($w.bytes)
            if ($w.code -eq 0 -and $txt -notmatch 'CDX9002') { break }
        }
        $wms += $w.ms
        if (-not (Test-Path $native)) { $diff += "$n/$kind native produced nothing"; continue }
        if ($kind -eq 'cce') {
            $got = IrCceFromStream $w.bytes; $want = [IO.File]::ReadAllBytes($native)
            if ($null -eq $got) { $diff += "$n/cce wasm produced no SIZE-framed IR (exit $($w.code))"; continue }
            if ([Convert]::ToBase64String([byte[]]$got) -eq [Convert]::ToBase64String($want)) { $same++ } else { $diff += "$n/cce differs ($($got.Count) vs $($want.Length) bytes)" }
        } else {
            $got = IrUniFromStream $w.bytes; $want = [IO.File]::ReadAllText($native)
            if ($null -eq $got) { $diff += "$n/uni wasm produced no IR (exit $($w.code))"; continue }
            if ($got -ceq $want) { $same++ } else { $diff += "$n/uni differs ($($got.Length) vs $($want.Length) chars)" }
        }
    }
}
"$same identical of $($Sources.Count * 2); wasm median $(($wms | Sort-Object)[[int]($wms.Count/2)]) ms, native median $(($nms | Sort-Object)[[int]($nms.Count/2)]) ms"
$diff | Select-Object -First 12
