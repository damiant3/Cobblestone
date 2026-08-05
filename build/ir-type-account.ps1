# Byte accountant for IR text: how much of an emitted unit is inline type
# structure that the chapter's own (type-defs ...) section already carries.
#
# Produce input with:
#   build/compile.ps1 -Src <app>.codex -Out <x>.out -Log <x>.log -IrUni -Kernel seed/Codex.cdx
# The IR lands in the -Log capture between IR-BEGIN and IR-END.
#
# -Coverage additionally answers the question that decides whether the inline
# form can be dropped without losing elaborated integer bounds: for every
# distinct record field emitted inline with explicit bounds, is that field
# also carried as (a-bounded ...) in type-defs?
#
# Calibrated against a hand-computed synthetic before first use. The paren
# walk skips string literals, so a quoted ")" in a name cannot desynchronise
# it; that case is in the self-test.

param(
    [string[]]$Path,
    [switch]$Coverage,
    [switch]$SelfTest
)

function Get-IrPayload([string]$file) {
    $raw = [IO.File]::ReadAllText($file)
    $b = $raw.IndexOf('IR-BEGIN')
    $e = $raw.LastIndexOf('IR-END')
    if ($b -lt 0 -or $e -lt 0) { throw "no IR-BEGIN/IR-END in $file" }
    $raw.Substring($b + 8, $e - $b - 8)
}

# Byte span of the balanced group starting at $from, string-literal aware.
function Get-GroupLength([string]$s, [int]$from) {
    $d = 0; $j = $from; $inS = $false; $esc = $false
    while ($j -lt $s.Length) {
        $c = $s[$j]
        if ($inS) {
            if ($esc) { $esc = $false }
            elseif ($c -eq '\') { $esc = $true }
            elseif ($c -eq '"') { $inS = $false }
        }
        elseif ($c -eq '"') { $inS = $true }
        elseif ($c -eq '(') { $d++ }
        elseif ($c -eq ')') { $d--; if ($d -eq 0) { return $j - $from + 1 } }
        $j++
    }
    throw "unbalanced group at $from"
}

function Measure-Ir([string]$s, [string]$name) {
    $n = $s.Length
    $inS = $false; $esc = $false; $depth = 0; $elideDepth = -1
    $recB = 0; $recG = 0; $sumB = 0; $sumG = 0; $start = 0; $kind = ''
    for ($i = 0; $i -lt $n; $i++) {
        $c = $s[$i]
        if ($inS) {
            if ($esc) { $esc = $false }
            elseif ($c -eq '\') { $esc = $true }
            elseif ($c -eq '"') { $inS = $false }
            continue
        }
        if ($c -eq '"') { $inS = $true; continue }
        if ($c -eq '(') {
            $depth++
            if ($elideDepth -lt 0) {
                if ($n -ge $i + 14 -and $s.Substring($i + 1, 13) -eq 'record-fields') {
                    $elideDepth = $depth; $start = $i; $kind = 'rec'
                }
                elseif ($n -ge $i + 10 -and $s.Substring($i + 1, 9) -eq 'sum-ctors') {
                    $elideDepth = $depth; $start = $i; $kind = 'sum'
                }
            }
            continue
        }
        if ($c -eq ')') {
            if ($elideDepth -eq $depth) {
                $len = $i - $start + 1
                if ($kind -eq 'rec') { $recB += $len; $recG++ } else { $sumB += $len; $sumG++ }
                $elideDepth = -1
            }
            $depth--
        }
    }
    $ti = $s.IndexOf('(type-defs')
    $tdLen = if ($ti -ge 0) { Get-GroupLength $s $ti } else { 0 }
    $elided = $recB + $sumB
    [pscustomobject]@{
        Unit         = $name
        IrBytes      = $n
        InlineStruct = $elided
        SharePct     = [math]::Round(100.0 * $elided / $n, 2)
        RecordGroups = $recG
        SumGroups    = $sumG
        TypeDefs     = $tdLen
        TypeDefsPct  = [math]::Round(100.0 * $tdLen / $n, 2)
        Remainder    = $n - $elided
        Shrink       = [math]::Round(1.0 * $n / [math]::Max(1, $n - $elided), 2)
    }
}

function Measure-Coverage([string]$s, [string]$name) {
    $ti = $s.IndexOf('(type-defs')
    if ($ti -lt 0) { throw "no type-defs section in $name" }
    $td = $s.Substring($ti, (Get-GroupLength $s $ti))

    $tdField = [Collections.Generic.HashSet[string]]::new()
    foreach ($m in [regex]::Matches($td, '\(rec-field "([^"]+)" \(a-bounded ')) { [void]$tdField.Add($m.Groups[1].Value) }
    $inField = [Collections.Generic.HashSet[string]]::new()
    foreach ($m in [regex]::Matches($s, '\(record-field "([^"]+)" \(int ')) { [void]$inField.Add($m.Groups[1].Value) }

    $tdCtor = [Collections.Generic.HashSet[string]]::new()
    foreach ($m in [regex]::Matches($td, '\(var-ctor "([^"]+)" \(fields[^)]*\(a-bounded')) { [void]$tdCtor.Add($m.Groups[1].Value) }
    $inCtor = [Collections.Generic.HashSet[string]]::new()
    foreach ($m in [regex]::Matches($s, '\(sum-ctor "([^"]+)" \(fields \(int ')) { [void]$inCtor.Add($m.Groups[1].Value) }

    $missF = @(); foreach ($x in $inField) { if (-not $tdField.Contains($x)) { $missF += $x } }
    $missC = @(); foreach ($x in $inCtor) { if (-not $tdCtor.Contains($x)) { $missC += $x } }

    [pscustomobject]@{
        Unit           = $name
        FieldsInline   = $inField.Count
        FieldsInDefs   = $tdField.Count
        FieldsUncov    = $missF.Count
        FieldsMissing  = ($missF | Select-Object -First 5) -join ','
        CtorsInline    = $inCtor.Count
        CtorsInDefs    = $tdCtor.Count
        CtorsUncov     = $missC.Count
        CtorsMissing   = ($missC | Select-Object -First 5) -join ','
    }
}

if ($SelfTest) {
    # Three axes, and each must be able to fail on its own:
    #  - a nested record-fields is counted ONCE, as part of its outer group
    #  - a quoted ")" INSIDE a counted group must not close it early
    #  - sum-ctors is counted separately from record-fields
    # The quoted paren has to sit inside a counted group or the test is blind
    # to the string-literal handling: the first version of this self-test put
    # it after both groups had closed, and it passed with that handling
    # spliced out.
    $rec = '(record-fields (record-field "a)b" (record-ty "S" (args) (record-fields (record-field "g" int)))) (record-field "c" int))'
    $sum = '(sum-ctors (sum-ctor "C(" (fields int)))'
    $syn = "(def `"d`" (record-ty `"R`" (args) $rec) $sum (type-defs))"
    $want = $rec.Length + $sum.Length
    $r = Measure-Ir $syn 'selftest'
    $ok = ($r.InlineStruct -eq $want) -and ($r.RecordGroups -eq 1) -and ($r.SumGroups -eq 1)
    if ($ok) { Write-Host "SELFTEST PASS  inline=$want rec=1 sum=1"; exit 0 }
    Write-Host "SELFTEST FAIL  inline=$($r.InlineStruct) rec=$($r.RecordGroups) sum=$($r.SumGroups) (want $want/1/1)"
    exit 1
}

if (-not $Path -or $Path.Count -eq 0) {
    Write-Host 'usage: ir-type-account.ps1 -Path <ir.log>... [-Coverage] | -SelfTest'
    exit 2
}

$out = @()
foreach ($p in $Path) {
    $s = Get-IrPayload $p
    $name = [IO.Path]::GetFileNameWithoutExtension($p)
    $out += if ($Coverage) { Measure-Coverage $s $name } else { Measure-Ir $s $name }
}
$out | Format-Table -AutoSize
