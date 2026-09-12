param(
    [Parameter(Mandatory)][string]$Wat,
    [Parameter(Mandatory)][string]$WorkDir
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$Wat = (Resolve-Path $Wat).Path
$WorkDir = [IO.Path]::GetFullPath($WorkDir)
if (Test-Path $WorkDir) { throw 'WorkDir must be new; preserve existing proof artifacts.' }
$null = Get-Command wat2wasm, wasmtime -ErrorAction Stop
$source = [IO.File]::ReadAllText($Wat)
if ($source -notmatch '\(func \$text_to_double ' -or $source -notmatch '\(global \$heap_ptr ') {
    throw 'Expected an emitted wasm module containing text_to_double and heap_ptr.'
}
$end = $source.LastIndexOf(')')
if ($end -lt 0 -or $source.Substring($end + 1).Trim().Length -ne 0) { throw 'Malformed module ending.' }
New-Item -ItemType Directory $WorkDir | Out-Null
# A fresh page lies beyond every existing data segment. CCE: 0=3, 1=4,
# decimal point=65, minus=73. One wasm page is 65536 bytes.
$wrapper = @'
  (func $__parse_boundary (export "__parse_boundary")
    (param $digits i32) (param $negative i32) (param $nonzero i32) (result i64)
    (local $page i32) (local $p i32) (local $i i32) (local $length i32)
    (local $heap i32) (local $bits i64)
    (local.set $page (memory.grow (i32.const 1)))
    (if (i32.eq (local.get $page) (i32.const -1)) (then (return (i64.const 9223372036854775807))))
    (local.set $p (i32.mul (local.get $page) (i32.const 65536)))
    (local.set $length (i32.add (i32.add (local.get $digits) (local.get $negative)) (i32.const 2)))
    (i32.store (local.get $p) (local.get $length))
    (local.set $i (i32.const 0))
    (loop $fill
      (i32.store8 (i32.add (i32.add (local.get $p) (i32.const 4)) (local.get $i)) (i32.const 3))
      (local.set $i (i32.add (local.get $i) (i32.const 1)))
      (br_if $fill (i32.lt_u (local.get $i) (local.get $length))))
    (if (local.get $negative) (then (i32.store8 (i32.add (local.get $p) (i32.const 4)) (i32.const 73))))
    (i32.store8 (i32.add (i32.add (local.get $p) (i32.const 5)) (local.get $negative)) (i32.const 65))
    (if (local.get $nonzero) (then
      (i32.store8 (i32.add (i32.add (local.get $p) (i32.const 3)) (local.get $length)) (i32.const 4))))
    (local.set $heap (global.get $heap_ptr))
    (local.set $bits (call $text_to_double (i64.extend_i32_u (local.get $p))))
    (if (i32.ne (global.get $heap_ptr) (local.get $heap)) (then (return (i64.const 9223372036854775807))))
    (local.get $bits))
'@
$module = $source.Substring(0, $end) + $wrapper + "`n)"
[IO.File]::WriteAllText("$WorkDir/subject.wat", $module, [Text.UTF8Encoding]::new($false))
@{ wat=$Wat; sha256=(Get-FileHash $Wat).Hash; measuredAt=(Get-Date).ToString('o') } |
    ConvertTo-Json | Set-Content "$WorkDir/provenance.json"
& wat2wasm --enable-tail-call "$WorkDir/subject.wat" -o "$WorkDir/subject.wasm" *> "$WorkDir/assemble.log"
if ($LASTEXITCODE -ne 0) { throw "WAT assembly failed: $WorkDir/assemble.log" }
$results = [Collections.Generic.List[object]]::new()
$cases = @()
foreach ($digits in @(1, 27, 28, 55, 56, 64, 82, 83, 107, 108, 109, 111)) {
    foreach ($negative in @(0, 1)) { $cases += @{ digits=$digits; negative=$negative; nonzero=1 } }
}
foreach ($digits in @(1, 108, 109)) {
    foreach ($negative in @(0, 1)) { $cases += @{ digits=$digits; negative=$negative; nonzero=0 } }
}
foreach ($case in $cases) {
    $name = "f$($case.digits)-neg$($case.negative)-nz$($case.nonzero)"
    $arguments = @('run', '--invoke', '__parse_boundary', "$WorkDir/subject.wasm",
        "$($case.digits)", "$($case.negative)", "$($case.nonzero)")
    $process = Start-Process wasmtime -ArgumentList $arguments -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput "$WorkDir/$name.out" -RedirectStandardError "$WorkDir/$name.err"
    $completed = $process.WaitForExit(10000)
    if (-not $completed) { $process.Kill(); $process.WaitForExit() }
    $rc = $process.ExitCode
    $output = [IO.File]::ReadAllText("$WorkDir/$name.out").Trim()
    $errorText = [IO.File]::ReadAllText("$WorkDir/$name.err")
    $refuse = $case.digits -gt 108 -and $case.nonzero -eq 1
    $decimal = $(if ($case.negative) { '-' } else { '' }) + '0.' + ('0' * ($case.digits - 1)) + $case.nonzero
    $expected = [BitConverter]::DoubleToInt64Bits([double]::Parse($decimal, [Globalization.CultureInfo]::InvariantCulture)).ToString()
    $ok = if ($refuse) {
        $completed -and $rc -eq 3 -and $output -eq '' -and $errorText.Contains('wasm `unreachable` instruction executed')
    } else { $completed -and $rc -eq 0 -and $output -ceq $expected }
    $results.Add([pscustomobject]@{ name=$name; fractionDigits=$case.digits; input=$decimal;
        expected=$(if ($refuse) { 'unreachable' } else { $expected }); actual=$output; exit=$rc; pass=$ok })
    Write-Host "$name $(if ($ok) { 'PASS' } else { 'FAIL' }) expected=$(if ($refuse) { 'unreachable' } else { $expected }) actual=$output exit=$rc"
}
$results | ConvertTo-Json -Depth 4 | Set-Content "$WorkDir/results.json"
$failed = @($results | Where-Object { -not $_.pass }).Count
Write-Host "wasm parse boundary: $($results.Count - $failed)/$($results.Count) pass, $failed fail"
if ($failed) { exit 1 }
