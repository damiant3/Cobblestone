# random-get-refusal-test.ps1 -- the wasm lowering of __hardware-random-word refuses
# when the host's random_get fails, and draws when it succeeds.
#
# wasmtime has no switch that makes WASI random_get fail, and it refuses a preload
# named wasi_snapshot_preview1. The failing arm therefore re-imports random_get from
# "env" (one textual change, match count checked) and preloads an env module whose
# random_get answers errno 29 (EIO); everything else in the module is unchanged.
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Kernel, [string]$OutDir = '')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
if (-not $OutDir) { $OutDir = Join-Path $env:TEMP ('random-get-refusal-' + [guid]::NewGuid().ToString('N').Substring(0,8)) }
if (Test-Path -LiteralPath $OutDir) { throw 'OutDir must be new' }
[void](New-Item -ItemType Directory -Path $OutDir)
$src = Join-Path $PSScriptRoot 'test/random-get-refusal-rt.codex'
$wat = Join-Path $OutDir 'program.wat'
& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'run.ps1') -Src $src -Out $wat -Kernel $Kernel | Out-Null
if (-not (Test-Path -LiteralPath $wat)) { throw 'wasm plug produced no WAT' }
$text = [IO.File]::ReadAllText($wat)
$from = '(import "wasi_snapshot_preview1" "random_get"'
$count = ([regex]::Matches($text, [regex]::Escape($from))).Count
if ($count -ne 1) { throw "random_get import found $count times, want 1" }
[IO.File]::WriteAllText((Join-Path $OutDir 'failing.wat'), $text.Replace($from, '(import "env" "random_get"'))
[IO.File]::WriteAllText((Join-Path $OutDir 'host.wat'), '(module (func (export "random_get") (param i32 i32) (result i32) (i32.const 29)))')
foreach ($n in 'program', 'failing', 'host') {
    & wat2wasm --enable-tail-call (Join-Path $OutDir "$n.wat") -o (Join-Path $OutDir "$n.wasm")
    if ($LASTEXITCODE -ne 0) { throw "wat2wasm $n failed" }
}
$real = (& wasmtime run (Join-Path $OutDir 'program.wasm') 2>&1 | Out-String).Trim()
$fail = (& wasmtime run --preload ('env=' + (Join-Path $OutDir 'host.wasm')) (Join-Path $OutDir 'failing.wasm') 2>&1 | Out-String).Trim()
$bad = 0
if ($real -ceq 'hardware-random-words: drawn 2') { 'PASS a host random_get that succeeds draws two words' } else { "FAIL real host answered: $real"; $bad++ }
if ($fail -ceq 'hardware-random-words: refused') { 'PASS a host random_get that fails refuses, no value' } else { "FAIL failing host answered: $fail"; $bad++ }
if ($bad -ne 0) { exit 1 }