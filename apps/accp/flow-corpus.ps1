[CmdletBinding()]
param([string]$Bundle = (Join-Path $PSScriptRoot 'build-output'), [string]$Out = '')
# AccpInformationFlow section 6. Compiles each corpus program with the bundle's
# codex-compiler.wasm in the bridge's IR-UNI mode and writes one "label|IR" line per
# program to codex/test/apps/accp-flow-rung1.stdin, the IR joined onto one line.
# Every program reads net-status (Network.Read) and writes print-line-uni
# (Console.Write), so its row holds both ends of the corpus rule.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if (-not $Out) { $Out = Join-Path $repo 'codex/test/apps/accp-flow-rung1.stdin' }
$compiler = Join-Path $Bundle 'codex-compiler.wasm'
if (-not (Test-Path -LiteralPath $compiler)) { throw "missing ${compiler}: run apps/accp/build.ps1" }
$wasmtime = (Get-Command wasmtime -ErrorAction Stop).Source
$utf8 = [Text.UTF8Encoding]::new($false)
$head = "  opening : [Console, Network.Read] Nothing = act`n    value <- net-status`n"
$cases = [ordered]@{
    'direct'                 = "$head    print-line-uni (show value)`n  end"
    'direct, untagged'       = "$head    print-line-uni (show 7)`n  end"
    'record field'           = "  P = record { a : Integer, b : Integer }`n$head    let r = P { a = value, b = 1 }`n    in print-line-uni (show r.b)`n  end"
    'record field, untagged' = "  P = record { a : Integer, b : Integer }`n$head    let r = P { a = 0, b = 1 }`n    in print-line-uni (show r.b)`n  end"
    'list element'           = "$head    print-line-uni (show (list-at [1, value] 0))`n  end"
    'list element, untagged' = "$head    print-line-uni (show (list-at [1, 2] 0))`n  end"
    'concatenation'          = "$head    print-line-uni (`"v`" & show value)`n  end"
    'concatenation, untagged' = "$head    print-line-uni (`"v`" & show 2)`n  end"
    'closure capture'        = "$head    let f = \x -> x + value`n    in print-line-uni (show (f 1))`n  end"
    'closure capture, untagged' = "$head    let f = \x -> x + 3`n    in print-line-uni (show (f 1))`n  end"
    'callee result'          = "  bump : Integer -> Integer`n  bump (n) = n + 1`n$head    print-line-uni (show (bump value))`n  end"
    'callee result, untagged' = "  bump : Integer -> Integer`n  bump (n) = n + 1`n$head    print-line-uni (show (bump 4))`n  end"
    'recursion accumulator'  = "  count : Integer, Integer -> Integer`n  count (n) (acc) = if n <= 0 then acc else count (n - 1) (acc + 1)`n$head    print-line-uni (show (count value 0))`n  end"
    'recursion accumulator, untagged' = "  count : Integer, Integer -> Integer`n  count (n) (acc) = if n <= 0 then acc else count (n - 1) (acc + 1)`n$head    print-line-uni (show (count 3 0))`n  end"
    'in-place write'         = "$head    let ys = [0, 0]`n    in let zs = list-set-at ys 0 value`n    in print-line-uni (show (list-at ys 1))`n  end"
    'in-place write, untagged' = "$head    let ys = [0, 0]`n    in let zs = list-set-at ys 0 5`n    in print-line-uni (show (list-at ys 1))`n  end"
    'implicit if'            = "$head    print-line-uni (if value == 0 then `"a`" else `"b`")`n  end"
    'implicit if, untagged'  = "$head    print-line-uni (if 1 == 0 then `"a`" else `"b`")`n  end"
    'implicit if, sink in the branches' = "$head    if value == 0 then print-line-uni `"a`" else print-line-uni `"b`"`n  end"
    'implicit if, sink in the branches, untagged' = "$head    if 7 == 0 then print-line-uni `"a`" else print-line-uni `"b`"`n  end"
    'implicit when'          = "$head    when value`n      is 0 -> print-line-uni `"a`"`n      is otherwise -> print-line-uni `"b`"`n  end"
    'implicit when, untagged' = "$head    when 3`n      is 0 -> print-line-uni `"a`"`n      is otherwise -> print-line-uni `"b`"`n  end"
    'sink under a tagged branch in a callee' = "  say : Integer -> [Console] Nothing`n  say (n) = print-line-uni `"said`"`n$head    if value == 0 then say 1 else say 2`n  end"
    'sink under a tagged branch in a callee, untagged' = "  say : Integer -> [Console] Nothing`n  say (n) = print-line-uni `"said`"`n$head    if 1 == 0 then say 1 else say 2`n  end"
    'function value from a tagged branch' = "  say-a : Integer -> [Console] Nothing`n  say-a (n) = print-line-uni `"a`"`n  say-b : Integer -> [Console] Nothing`n  say-b (n) = print-line-uni `"b`"`n$head    let f = if value == 0 then say-a else say-b`n    in f 0`n  end"
    'function value from a tagged branch, untagged' = "  say-a : Integer -> [Console] Nothing`n  say-a (n) = print-line-uni `"a`"`n  say-b : Integer -> [Console] Nothing`n  say-b (n) = print-line-uni `"b`"`n$head    let f = if 1 == 0 then say-a else say-b`n    in f 0`n  end"
    'sink builtin as a function value' = "  apply-to : (Text -> [Console] Nothing), Text -> [Console] Nothing`n  apply-to (g) (t) = g t`n$head    apply-to print-line-uni (show value)`n  end"
    'sink builtin as a function value, untagged' = "  apply-to : (Text -> [Console] Nothing), Text -> [Console] Nothing`n  apply-to (g) (t) = g t`n$head    apply-to print-line-uni (show 6)`n  end"
    'function value returning a source' = "  reader : Integer -> [Network.Read] Integer`n  reader (n) = act`n    v <- net-status`n    v + n`n  end`n$head    let g = reader`n    in print-line-uni (show (g 0))`n  end"
    'function value returning a source, untagged' = "  echo : Integer -> Integer`n  echo (n) = n + 1`n$head    let g = echo`n    in print-line-uni (show (g 0))`n  end"
    'global state cell'      = "$head    let u = set-state value`n    in print-line-uni (show get-state)`n  end"
    'raw memory'             = "$head    let p = alloc-bytes 8`n    in let q = poke-byte p 0 value`n    in print-line-uni (show (peek-byte p 0))`n  end"
}
$lines = [Collections.Generic.List[string]]::new()
foreach ($label in $cases.Keys) {
    $src = "Chapter: FlowCorpus`nSection: Entry`n$($cases[$label])`nPage 1`n"
    $bytes = $utf8.GetBytes("IR-UNI decks=12`n" + $src + [char]0)
    $psi = [Diagnostics.ProcessStartInfo]::new($wasmtime)
    foreach ($a in @('run', '-W', 'max-wasm-stack=16777216,max-memory-size=536870912', $compiler)) { $psi.ArgumentList.Add($a) }
    $psi.RedirectStandardInput = $true; $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true; $psi.UseShellExecute = $false
    $p = [Diagnostics.Process]::Start($psi)
    $p.StandardInput.BaseStream.Write($bytes, 0, $bytes.Length); $p.StandardInput.Close()
    $text = $p.StandardOutput.ReadToEnd(); $null = $p.StandardError.ReadToEnd()
    if (-not $p.WaitForExit(60000)) { $p.Kill($true); throw "compile timeout: $label" }
    if ($text -match 'error CDX[^\r\n]*') { throw "${label}: $($Matches[0])" }
    $m = [regex]::Match($text, '(?s)IR-BEGIN\r?\n(.*?)\r?\nIR-END')
    if (-not $m.Success) { throw "${label}: no IR frame" }
    $ir = ($m.Groups[1].Value -split '\r?\n' | ForEach-Object { $_.Trim() }) -join ' '
    $lines.Add("$label|$ir")
}
[IO.File]::WriteAllText($Out, (($lines -join "`r`n") + "`r`n"), $utf8)
"flow corpus: $($lines.Count) programs written to $Out"
