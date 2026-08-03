# Differential oracle for the scalar operators.
#
# Every answer here is checked against the HOST's arithmetic, never against
# another Codex answer. That is the whole design, and it is the lesson from
# BrotliBeatsOpus applied to arithmetic instead of a codec: a decoder checked
# only against its paired encoder is checked against nothing, and a compiler
# checked only against itself is the same shape. The self-compile and the
# fixed point cannot see an operator that is uniformly wrong, because they
# only require the compiler to agree with itself.
#
# It exists because Real `<` `>` `<=` `>=` were emitted as a SIGNED INTEGER
# compare of two IEEE-754 bit patterns from the initial import of 2026-04-17
# until 2026-07-27. That is correct for two non-negatives and for mixed signs
# and REVERSED for two negatives, so the defect lived on one axis -- the SIGN
# of both operands -- that no test enumerated. Of thirteen test files that
# declared a Real, exactly one contained any ordering comparison.
#
# So this enumerates a LATTICE rather than a handful of cases: every operator
# against every ordered pair drawn from a set that crosses zero. A hole along
# an axis nobody thought of is exactly what a cross product finds and a
# hand-written case list does not.
#
# Widened 2026-07-27 along the three axes it was missing, and the widening
# immediately found a second first-order defect (floor-div underflowing at
# i64-min, so a hex literal in a 255-wide band compiled to the wrong
# constant). The axes:
#
#   Real approximate.  Single precision is a SEPARATE emitter arm reaching a
#     different instruction (ucomiss, not ucomisd). Nothing exercised it. An
#     oracle that covers only f64 declares an untested f32 path correct.
#   The IEEE special values.  NaN, the infinities, and negative zero, built
#     exactly through bits-to-real rather than approximated by arithmetic.
#     The fix for the ordering defect emits `<` and `<=` with the operands
#     REVERSED, which is what makes all four operators answer False on NaN;
#     until these cases existed that reasoning was unverified.
#   Integer literals.  A literal's value is a compiler output like any other,
#     and it was the one output nothing adjudicated. This is the axis that
#     catches the floor-div class.
#
# Widened again 2026-07-27 along the OPERAND-TYPE axis, which is the axis all
# three of that day's defects lived on: an operator right for the operand
# types someone tested and wrong for one nobody did. Comparison was covered;
# arithmetic was not adjudicated at all. The added axes:
#
#   Real and Real-approximate ARITHMETIC.  `+ - * /` reach separate emitter
#     arms (emit-num-arith for f64, emit-real-approx-arith for f32) and
#     neither was ever checked against the host. Comparison having carried a
#     three-month silent inversion is a reason to doubt its neighbours rather
#     than to assume them.
#   The conversions.  real-from-int, real-to-int, to-real-approx and
#     real-approx-to-int. A float-to-integer conversion is the classic
#     silent-truncation site, and its out-of-range behaviour is a rule the
#     hardware states and nothing here had ever asked about.
#
# Results are adjudicated as BIT PATTERNS through real-to-bits, not as
# rendered decimals. A decimal render hides exactly the low-mantissa
# difference a wrong instruction produces, and it cannot express negative
# zero at all -- which is one of the operands.
#
# The one deliberate exception: when the host's answer is NaN the guest is
# required to be *a* NaN and not a specific one. IEEE-754 does not fix the
# payload of a computed NaN, so pinning the bits would be pinning an
# accident of which operand the hardware happened to propagate.
#
# ON DEMAND. Not in build.ps1, not in test.ps1. It boots a VM and takes a
# couple of minutes; it is for when you touch an operator, an emitter
# comparison path, a condition code, or an immediate encoder, and before a
# public push.
#
#   pwsh build/oracle-scalar.ps1
#   pwsh build/oracle-scalar.ps1 -Kernel build/output/Sut.cdx
#   pwsh build/oracle-scalar.ps1 -Keep        # keep the generated source
#
# A failure prints the operands, the answer, and what the host said.

param(
    [string]$Kernel = "seed/Codex.cdx",
    [switch]$Keep
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo
[Environment]::CurrentDirectory = $repo

$out = Join-Path $repo 'build-output'
New-Item -ItemType Directory -Force $out | Out-Null
$src = Join-Path $out 'oracle-scalar.codex'
$cdx = Join-Path $out 'oracle-scalar.cdx'
$log = Join-Path $out 'oracle-scalar.log'
$runOut = Join-Path $out 'oracle-scalar.out'

# ------------------------------------------------------------ host semantics
#
# PowerShell's -lt and -ge on [double] DO follow IEEE, but its own
# CompareTo does not: Double.CompareTo orders NaN below everything and
# negative zero below positive zero, and either would silently make this
# harness adjudicate the wrong rules. The op_ methods are named explicitly so
# the choice is deliberate and cannot drift.
function Host-Cmp([double]$a, [double]$b, [string]$op) {
    switch ($op) {
        '<'  { [int][double]::op_LessThan($a, $b) }
        '>'  { [int][double]::op_GreaterThan($a, $b) }
        '<=' { [int][double]::op_LessThanOrEqual($a, $b) }
        '>=' { [int][double]::op_GreaterThanOrEqual($a, $b) }
    }
}

function Host-CmpF([float]$a, [float]$b, [string]$op) {
    switch ($op) {
        '<'  { [int][single]::op_LessThan($a, $b) }
        '>'  { [int][single]::op_GreaterThan($a, $b) }
        '<=' { [int][single]::op_LessThanOrEqual($a, $b) }
        '>=' { [int][single]::op_GreaterThanOrEqual($a, $b) }
    }
}

# A 16-digit hex pattern is a raw bit pattern, so it exceeds [long] whenever
# the top bit is set. Go through UInt64 and reinterpret, which is what the
# hash form means.
function Hex-ToInt64([string]$h) {
    $u = [System.Convert]::ToUInt64($h, 16)
    [System.BitConverter]::ToInt64([System.BitConverter]::GetBytes($u), 0)
}

# `real-to-bits` is a bare register move: a Real is carried in a general
# register as its raw pattern, so the guest prints a SIGNED 64-bit decimal of
# the bits. `real-approx-to-bits` goes through movd, which zero-extends the
# 32-bit pattern, so an f32 prints as an UNSIGNED 32-bit value widened to 64.
# Those two widths are not interchangeable and getting one wrong would make
# every negative f32 look like a defect.
function Bits-D([double]$x) { [System.BitConverter]::DoubleToInt64Bits($x) }
# Reinterpret rather than convert. A PowerShell cast of a negative Int32 to
# [uint32] THROWS instead of wrapping, so the bytes are the only honest route.
function Bits-F([float]$x) {
    [int64][System.BitConverter]::ToUInt32([System.BitConverter]::GetBytes($x), 0)
}

function Is-NanD([double]$x) { [double]::IsNaN($x) }
function Is-NanF([float]$x)  { [single]::IsNaN($x) }

# Answers whether a printed guest bit pattern is *a* NaN. Used only where the
# host's own answer is NaN, because IEEE-754 leaves a computed NaN's payload
# unspecified and pinning it would pin an accident of operand propagation.
function Guest-IsNanD([string]$s) {
    $b = 0L
    if (-not [int64]::TryParse($s, [ref]$b)) { return $false }
    (($b -band 0x7FF0000000000000L) -eq 0x7FF0000000000000L) -and
    (($b -band 0x000FFFFFFFFFFFFFL) -ne 0L)
}
function Guest-IsNanF([string]$s) {
    $b = 0L
    if (-not [int64]::TryParse($s, [ref]$b)) { return $false }
    (($b -band 0x7F800000L) -eq 0x7F800000L) -and (($b -band 0x007FFFFFL) -ne 0L)
}

function Host-Arith([double]$a, [double]$b, [string]$op) {
    switch ($op) {
        '+' { $a + $b } '-' { $a - $b } '*' { $a * $b } '/' { $a / $b }
    }
}
function Host-ArithF([float]$a, [float]$b, [string]$op) {
    # Each step is rounded to single precision, which is what addss does and
    # what computing in double and narrowing at the end would NOT do.
    switch ($op) {
        '+' { [float]($a + $b) } '-' { [float]($a - $b) }
        '*' { [float]($a * $b) } '/' { [float]($a / $b) }
    }
}

# cvttsd2si truncates toward zero, and for NaN, either infinity, or a value
# outside the signed 64-bit range it returns the "integer indefinite" value,
# which is i64-min. That rule is stated here rather than delegated to a .NET
# cast: the language spec calls such a cast unspecified, so relying on it
# would make this harness adjudicate a coincidence of the host JIT instead of
# the rule the hardware documents.
$i64Min = [int64]::MinValue
function Host-RealToInt([double]$x) {
    if ([double]::IsNaN($x) -or [double]::IsInfinity($x)) { return $i64Min }
    $t = [Math]::Truncate($x)
    if ($t -ge 9223372036854775808.0 -or $t -lt -9223372036854775808.0) { return $i64Min }
    [int64]$t
}

# ---------------------------------------------------------------- the lattice
#
# Reals: two magnitudes either side of zero, plus zero, plus a pair that
# differ only far down the mantissa so the comparison cannot be decided by
# the exponent alone, plus the four IEEE values that are not ordinary
# numbers. Negative zero is built from its bit pattern rather than written
# as 0.0 - 0.0, which folds to positive zero and would test nothing.
$reals = @(
    @{ n = 'rNegBig';   lit = '0.0 - 2.9';                  v = -2.9 },
    @{ n = 'rNegSmall'; lit = '0.0 - 1.5';                  v = -1.5 },
    @{ n = 'rNegTiny';  lit = '0.0 - 0.0001';               v = -0.0001 },
    @{ n = 'rZero';     lit = '0.0';                        v = 0.0 },
    @{ n = 'rPosTiny';  lit = '0.0001';                     v = 0.0001 },
    @{ n = 'rPosSmall'; lit = '1.5';                        v = 1.5 },
    @{ n = 'rPosBig';   lit = '2.9';                        v = 2.9 },
    @{ n = 'rNegNear';  lit = '0.0 - 2.9000001';            v = -2.9000001 },
    @{ n = 'rNan';      lit = 'bits-to-real #7FF8000000000000'; v = [double]::NaN },
    @{ n = 'rPosInf';   lit = 'bits-to-real #7FF0000000000000'; v = [double]::PositiveInfinity },
    @{ n = 'rNegInf';   lit = 'bits-to-real #FFF0000000000000'; v = [double]::NegativeInfinity },
    @{ n = 'rNegZero';  lit = 'bits-to-real #8000000000000000'; v = [double]::Parse('-0.0', [Globalization.CultureInfo]::InvariantCulture) }
)

# Single precision. A separate emitter arm and a separate instruction, so a
# separate lattice; smaller, because the shared logic is already covered
# above and what is under test here is that f32 reaches ucomiss at all.
$approx = @(
    @{ n = 'aNegBig';   lit = 'to-real-approx (0.0 - 2.9)';     v = [float](-2.9) },
    @{ n = 'aNegSmall'; lit = 'to-real-approx (0.0 - 1.5)';     v = [float](-1.5) },
    @{ n = 'aZero';     lit = 'to-real-approx 0.0';             v = [float]0.0 },
    @{ n = 'aPosSmall'; lit = 'to-real-approx 1.5';             v = [float]1.5 },
    @{ n = 'aPosBig';   lit = 'to-real-approx 2.9';             v = [float]2.9 },
    @{ n = 'aNan';      lit = 'bits-to-real-approx #7FC00000';  v = [float]::NaN },
    @{ n = 'aPosInf';   lit = 'bits-to-real-approx #7F800000';  v = [float]::PositiveInfinity },
    @{ n = 'aNegInf';   lit = 'bits-to-real-approx #FF800000';  v = [float]::NegativeInfinity },
    @{ n = 'aNegZero';  lit = 'bits-to-real-approx #80000000';  v = [float]::Parse('-0.0', [Globalization.CultureInfo]::InvariantCulture) }
)

# Integers: the signs, and the boundary a truncating division and the two
# remainders disagree about.
$ints = @(
    @{ n = 'iNegBig';   lit = '0 - 7'; v = -7 },
    @{ n = 'iNegSmall'; lit = '0 - 3'; v = -3 },
    @{ n = 'iZero';     lit = '0';     v = 0 },
    @{ n = 'iPosSmall'; lit = '3';     v = 3 },
    @{ n = 'iPosBig';   lit = '7';     v = 7 }
)

# Integer literals. The immediate encoder splits a 64-bit constant into eight
# bytes, so the interesting values are the ones where that split is hardest:
# the extremes, and the band just above i64-min where a biased floor division
# underflows. i64-min+255 and i64-min+256 are here because they were always
# correct, and a test that showed only the failures would not have shown that
# the boundary sits where the arithmetic says.
$hexLits = @(
    '8000000000000000', '8000000000000001', '8000000000000002',
    '80000000000000FE', '80000000000000FF', '8000000000000100',
    '7FFFFFFFFFFFFFFF', '7FFFFFFFFFFFFF00', 'FFFFFFFFFFFFFFFF',
    'FFFFFFFF00000000', 'C000000000000000', 'BFF0000000000000',
    'FFF0000000000000', '0000000100000000', '00000000FFFFFFFF',
    '000000007FFFFFFF', '0000000080000000'
)

$ops = @(
    @{ sym = '<';  tag = 'lt' },
    @{ sym = '>';  tag = 'gt' },
    @{ sym = '<='; tag = 'le' },
    @{ sym = '>='; tag = 'ge' }
)

$arithOps = @(
    @{ sym = '+'; tag = 'add' },
    @{ sym = '-'; tag = 'sub' },
    @{ sym = '*'; tag = 'mul' },
    @{ sym = '/'; tag = 'div' }
)

# Integers worth converting to a Real: the signs, zero, a value past 2^53
# where a double can no longer represent every integer, and the two extremes
# where the round trip cannot be exact.
$convInts = @(
    @{ lit = '0 - 9007199254740993'; v = [int64]-9007199254740993 },
    @{ lit = '0 - 1000000';          v = [int64]-1000000 },
    @{ lit = '0 - 7';                v = [int64]-7 },
    @{ lit = '0 - 1';                v = [int64]-1 },
    @{ lit = '0';                    v = [int64]0 },
    @{ lit = '1';                    v = [int64]1 },
    @{ lit = '7';                    v = [int64]7 },
    @{ lit = '1000000';              v = [int64]1000000 },
    @{ lit = '9007199254740993';     v = [int64]9007199254740993 },
    @{ lit = '#7FFFFFFFFFFFFFFF';    v = [int64]::MaxValue },
    @{ lit = '#8000000000000000';    v = [int64]::MinValue }
)

# ------------------------------------------------------------ generate source
#
# Each case is asked twice. A comparison in condition position is FUSED into
# the branch; one whose result is handed on as a value is MATERIALISED. They
# are different code in the emitter and each carried the defect separately, so
# an oracle that asked only one shape would have declared the other correct.
$sb = [System.Text.StringBuilder]::new()
$null = $sb.AppendLine('Chapter: OracleScalar')
$null = $sb.AppendLine('')
$null = $sb.AppendLine(' Generated by build/oracle-scalar.ps1. Do not edit; regenerate.')
$null = $sb.AppendLine('')
$null = $sb.AppendLine(' Every comparison line prints the fused answer then the value answer.')
$null = $sb.AppendLine(' The host computes what both must be.')
$null = $sb.AppendLine('')
$null = $sb.AppendLine('Section: Operands')
$null = $sb.AppendLine('')
foreach ($r in $reals) {
    $null = $sb.AppendLine("  $($r.n) : Real")
    $null = $sb.AppendLine("  $($r.n) = $($r.lit)")
    $null = $sb.AppendLine('')
}
foreach ($a in $approx) {
    $null = $sb.AppendLine("  $($a.n) : Real approximate")
    $null = $sb.AppendLine("  $($a.n) = $($a.lit)")
    $null = $sb.AppendLine('')
}
foreach ($i in $ints) {
    $null = $sb.AppendLine("  $($i.n) : Integer")
    $null = $sb.AppendLine("  $($i.n) = $($i.lit)")
    $null = $sb.AppendLine('')
}

$null = $sb.AppendLine('Section: The Two Paths')
$null = $sb.AppendLine('')
$null = $sb.AppendLine('  b2i : Boolean -> Integer')
$null = $sb.AppendLine('  b2i (b) = if b then 1 else 0')
$null = $sb.AppendLine('')
foreach ($o in $ops) {
    $null = $sb.AppendLine("  rf-$($o.tag) : Real, Real -> Integer")
    $null = $sb.AppendLine("  rf-$($o.tag) (a) (b) = if a $($o.sym) b then 1 else 0")
    $null = $sb.AppendLine('')
    $null = $sb.AppendLine("  rv-$($o.tag) : Real, Real -> Integer")
    $null = $sb.AppendLine("  rv-$($o.tag) (a) (b) = b2i (a $($o.sym) b)")
    $null = $sb.AppendLine('')
    $null = $sb.AppendLine("  af-$($o.tag) : Real approximate, Real approximate -> Integer")
    $null = $sb.AppendLine("  af-$($o.tag) (a) (b) = if a $($o.sym) b then 1 else 0")
    $null = $sb.AppendLine('')
    $null = $sb.AppendLine("  av-$($o.tag) : Real approximate, Real approximate -> Integer")
    $null = $sb.AppendLine("  av-$($o.tag) (a) (b) = b2i (a $($o.sym) b)")
    $null = $sb.AppendLine('')
    $null = $sb.AppendLine("  if-$($o.tag) : Integer, Integer -> Integer")
    $null = $sb.AppendLine("  if-$($o.tag) (a) (b) = if a $($o.sym) b then 1 else 0")
    $null = $sb.AppendLine('')
    $null = $sb.AppendLine("  iv-$($o.tag) : Integer, Integer -> Integer")
    $null = $sb.AppendLine("  iv-$($o.tag) (a) (b) = b2i (a $($o.sym) b)")
    $null = $sb.AppendLine('')
}
$null = $sb.AppendLine('  idiv : Integer, Integer -> Integer')
$null = $sb.AppendLine('  idiv (a) (b) = a / b')
$null = $sb.AppendLine('')
$null = $sb.AppendLine('  irem : Integer, Integer -> Integer')
$null = $sb.AppendLine('  irem (a) (b) = int-rem a b')
$null = $sb.AppendLine('')
$null = $sb.AppendLine('  imod : Integer, Integer -> Integer')
$null = $sb.AppendLine('  imod (a) (b) = int-mod a b')
$null = $sb.AppendLine('')

# Arithmetic. The answer is handed back as its bit pattern, so the comparison
# with the host is exact and can express negative zero, the infinities, and a
# one-ulp difference. A rendered decimal can express none of the three.
$null = $sb.AppendLine('Section: Arithmetic')
$null = $sb.AppendLine('')
foreach ($o in $arithOps) {
    $null = $sb.AppendLine("  ra-$($o.tag) : Real, Real -> Integer")
    $null = $sb.AppendLine("  ra-$($o.tag) (a) (b) = real-to-bits (a $($o.sym) b)")
    $null = $sb.AppendLine('')
    $null = $sb.AppendLine("  aa-$($o.tag) : Real approximate, Real approximate -> Integer")
    $null = $sb.AppendLine("  aa-$($o.tag) (a) (b) = real-approx-to-bits (a $($o.sym) b)")
    $null = $sb.AppendLine('')
}

$null = $sb.AppendLine('Section: Conversions')
$null = $sb.AppendLine('')
$null = $sb.AppendLine('  ri-bits : Integer -> Integer')
$null = $sb.AppendLine('  ri-bits (n) = real-to-bits (real-from-int n)')
$null = $sb.AppendLine('')
$null = $sb.AppendLine('  ri-approx-bits : Integer -> Integer')
$null = $sb.AppendLine('  ri-approx-bits (n) = real-approx-to-bits (to-real-approx (real-from-int n))')
$null = $sb.AppendLine('')
$null = $sb.AppendLine('  ir-trunc : Real -> Integer')
$null = $sb.AppendLine('  ir-trunc (x) = real-to-int x')
$null = $sb.AppendLine('')
$null = $sb.AppendLine('  ir-approx-trunc : Real approximate -> Integer')
$null = $sb.AppendLine('  ir-approx-trunc (x) = real-approx-to-int x')
$null = $sb.AppendLine('')
$null = $sb.AppendLine('  narrow-bits : Real -> Integer')
$null = $sb.AppendLine('  narrow-bits (x) = real-approx-to-bits (to-real-approx x)')
$null = $sb.AppendLine('')
$null = $sb.AppendLine('  id-bits : Real -> Integer')
$null = $sb.AppendLine('  id-bits (x) = real-to-bits x')
$null = $sb.AppendLine('')
$null = $sb.AppendLine('  id-approx-bits : Real approximate -> Integer')
$null = $sb.AppendLine('  id-approx-bits (x) = real-approx-to-bits x')
$null = $sb.AppendLine('')

$expected = [System.Collections.Generic.List[object]]::new()
$null = $sb.AppendLine('Section: Report')
$null = $sb.AppendLine('')
$null = $sb.AppendLine('  opening : [Console] Nothing')
$null = $sb.AppendLine('  opening = act')

$id = 0
foreach ($o in $ops) {
    foreach ($a in $reals) {
        foreach ($b in $reals) {
            $id++
            $key = "R$id"
            $want = Host-Cmp $a.v $b.v $o.sym
            $null = $sb.AppendLine("    print-line-uni (`"$key `" & show (rf-$($o.tag) $($a.n) $($b.n)) & show (rv-$($o.tag) $($a.n) $($b.n)))")
            $expected.Add([pscustomobject]@{ Key = $key; Want = "$want$want"; What = "Real $($a.n) $($o.sym) $($b.n)" })
        }
    }
}
foreach ($o in $ops) {
    foreach ($a in $approx) {
        foreach ($b in $approx) {
            $id++
            $key = "A$id"
            $want = Host-CmpF $a.v $b.v $o.sym
            $null = $sb.AppendLine("    print-line-uni (`"$key `" & show (af-$($o.tag) $($a.n) $($b.n)) & show (av-$($o.tag) $($a.n) $($b.n)))")
            $expected.Add([pscustomobject]@{ Key = $key; Want = "$want$want"; What = "Real approximate $($a.n) $($o.sym) $($b.n)" })
        }
    }
}
foreach ($o in $ops) {
    foreach ($a in $ints) {
        foreach ($b in $ints) {
            $id++
            $key = "I$id"
            $want = switch ($o.sym) {
                '<'  { [int]($a.v -lt $b.v) }
                '>'  { [int]($a.v -gt $b.v) }
                '<=' { [int]($a.v -le $b.v) }
                '>=' { [int]($a.v -ge $b.v) }
            }
            $null = $sb.AppendLine("    print-line-uni (`"$key `" & show (if-$($o.tag) $($a.n) $($b.n)) & show (iv-$($o.tag) $($a.n) $($b.n)))")
            $expected.Add([pscustomobject]@{ Key = $key; Want = "$want$want"; What = "Integer $($a.v) $($o.sym) $($b.v)" })
        }
    }
}
# Truncating division and the two remainders, over every sign pair. The
# division identity a == (a / b) * b + int-rem a b must hold for all of them,
# and int-mod must never answer negative; those are the two things the guide
# says are silent to get wrong.
foreach ($a in $ints) {
    foreach ($b in $ints) {
        if ($b.v -eq 0) { continue }
        $id++
        $key = "D$id"
        $q = [Math]::Truncate([double]$a.v / [double]$b.v)
        $r = $a.v - ($q * $b.v)
        $m = $a.v % $b.v; if ($m -lt 0) { $m += [Math]::Abs($b.v) }
        $null = $sb.AppendLine("    print-line-uni (`"$key `" & show (idiv $($a.n) $($b.n)) & `" `" & show (irem $($a.n) $($b.n)) & `" `" & show (imod $($a.n) $($b.n)))")
        $expected.Add([pscustomobject]@{ Key = $key; Want = "$([int]$q) $([int]$r) $([int]$m)"; What = "Integer $($a.v) / $($b.v), int-rem, int-mod" })
    }
}
# Integer literals. The value a literal denotes is a compiler output, and it
# was the one output nothing here adjudicated.
foreach ($h in $hexLits) {
    $id++
    $key = "L$id"
    $want = Hex-ToInt64 $h
    $null = $sb.AppendLine("    print-line-uni (`"$key `" & show #$h)")
    $expected.Add([pscustomobject]@{ Key = $key; Want = "$want"; What = "literal #$h" })
}
# The operands themselves, before any operator touches them.
#
# This has to come first, and its absence was a real hole rather than a
# formality: if a decimal literal parses to a different double here than it
# does on the host, every arithmetic case built on that operand disagrees and
# reads exactly like a broken arithmetic instruction. The comparison lattice
# cannot see it either, because a one-ulp difference in an operand leaves
# every ordering in the lattice unchanged. An operator can only be
# adjudicated against an operand both sides agree on.
foreach ($r in $reals) {
    $id++
    $key = "O$id"
    $w = if (Is-NanD $r.v) { 'NAN' } else { "$(Bits-D $r.v)" }
    $null = $sb.AppendLine("    print-line-uni (`"$key `" & show (id-bits $($r.n)))")
    $expected.Add([pscustomobject]@{ Key = $key; Want = $w; Nan = 'd'; What = "operand $($r.n) = $($r.lit)" })
}
foreach ($a in $approx) {
    $id++
    $key = "O$id"
    $w = if (Is-NanF $a.v) { 'NAN' } else { "$(Bits-F $a.v)" }
    $null = $sb.AppendLine("    print-line-uni (`"$key `" & show (id-approx-bits $($a.n)))")
    $expected.Add([pscustomobject]@{ Key = $key; Want = $w; Nan = 'f'; What = "operand $($a.n) = $($a.lit)" })
}
# Arithmetic, f64 then f32, over the same lattices the comparisons use. The
# specials are the point: an arm that is right for two ordinary numbers can
# still mishandle an infinity, and no test in the tree adds one to anything.
foreach ($o in $arithOps) {
    foreach ($a in $reals) {
        foreach ($b in $reals) {
            $id++
            $key = "M$id"
            $r = Host-Arith $a.v $b.v $o.sym
            $want = if (Is-NanD $r) { 'NAN' } else { "$(Bits-D $r)" }
            $null = $sb.AppendLine("    print-line-uni (`"$key `" & show (ra-$($o.tag) $($a.n) $($b.n)))")
            $expected.Add([pscustomobject]@{ Key = $key; Want = $want; Nan = 'd'; What = "Real $($a.n) $($o.sym) $($b.n)" })
        }
    }
}
foreach ($o in $arithOps) {
    foreach ($a in $approx) {
        foreach ($b in $approx) {
            $id++
            $key = "N$id"
            $r = Host-ArithF $a.v $b.v $o.sym
            $want = if (Is-NanF $r) { 'NAN' } else { "$(Bits-F $r)" }
            $null = $sb.AppendLine("    print-line-uni (`"$key `" & show (aa-$($o.tag) $($a.n) $($b.n)))")
            $expected.Add([pscustomobject]@{ Key = $key; Want = $want; Nan = 'f'; What = "Real approximate $($a.n) $($o.sym) $($b.n)" })
        }
    }
}
# Conversions. Integer to Real is exact below 2^53 and rounds above it, which
# is why the lattice reaches past that boundary; Real to Integer truncates
# toward zero and answers i64-min for anything it cannot represent.
foreach ($c in $convInts) {
    $id++
    $key = "C$id"
    $null = $sb.AppendLine("    print-line-uni (`"$key `" & show (ri-bits ($($c.lit))))")
    $expected.Add([pscustomobject]@{ Key = $key; Want = "$(Bits-D ([double]$c.v))"; Nan = 'd'; What = "real-from-int $($c.v)" })

    $id++
    $key = "C$id"
    $null = $sb.AppendLine("    print-line-uni (`"$key `" & show (ri-approx-bits ($($c.lit))))")
    $expected.Add([pscustomobject]@{ Key = $key; Want = "$(Bits-F ([float][double]$c.v))"; Nan = 'f'; What = "to-real-approx (real-from-int $($c.v))" })
}
foreach ($r in $reals) {
    $id++
    $key = "C$id"
    $null = $sb.AppendLine("    print-line-uni (`"$key `" & show (ir-trunc $($r.n)))")
    $expected.Add([pscustomobject]@{ Key = $key; Want = "$(Host-RealToInt $r.v)"; Nan = 'd'; What = "real-to-int $($r.n)" })

    $id++
    $key = "C$id"
    $w = if (Is-NanD $r.v) { 'NAN' } else { "$(Bits-F ([float]$r.v))" }
    $null = $sb.AppendLine("    print-line-uni (`"$key `" & show (narrow-bits $($r.n)))")
    $expected.Add([pscustomobject]@{ Key = $key; Want = $w; Nan = 'f'; What = "to-real-approx $($r.n)" })
}
foreach ($a in $approx) {
    $id++
    $key = "C$id"
    $null = $sb.AppendLine("    print-line-uni (`"$key `" & show (ir-approx-trunc $($a.n)))")
    $expected.Add([pscustomobject]@{ Key = $key; Want = "$(Host-RealToInt ([double]$a.v))"; Nan = 'd'; What = "real-approx-to-int $($a.n)" })
}
$null = $sb.AppendLine('  end')

Set-Content -Path $src -Value $sb.ToString() -Encoding UTF8
Write-Host "generated $($expected.Count) cases -> $src"

# -------------------------------------------------------------- compile + run
& (Join-Path $repo 'build/compile.ps1') -Src $src -Out $cdx -Log $log -Kernel $Kernel | Out-Null
if (-not (Test-Path $cdx)) {
    Write-Host "COMPILE FAILED - see $log" -ForegroundColor Red
    Select-String -Path $log -Pattern 'error' | Select-Object -First 10 | ForEach-Object { $_.Line }
    exit 1
}
& (Join-Path $repo 'tools/codex-vm.exe') -kernel $cdx -headless -input NUL -output $runOut -mem 3072 | Out-Null

# ----------------------------------------------------------------- adjudicate
# The guest's serial stream carries a leading control byte before the first
# line, so the anchor has to survive it. Strip anything below space from both
# ends rather than matching unanchored: an unanchored match would also accept
# a key appearing in the middle of some other line.
$got = @{}
foreach ($line in (Get-Content $runOut)) {
    $t = ($line -replace '[\x00-\x1F]', '').Trim()
    if ($t -match '^([RIDALMNCO]\d+)\s+(.+)$') { $got[$matches[1]] = $matches[2].Trim() }
}

$fail = 0
foreach ($e in $expected) {
    if (-not $got.ContainsKey($e.Key)) {
        Write-Host "MISSING $($e.Key): $($e.What)" -ForegroundColor Red
        $fail++
        continue
    }
    # A host answer of NaN requires *a* NaN back, at the right width, rather
    # than a specific payload. Everything else is an exact string compare.
    if ($e.Want -eq 'NAN') {
        $ok = if ($e.Nan -eq 'f') { Guest-IsNanF $got[$e.Key] } else { Guest-IsNanD $got[$e.Key] }
        if (-not $ok) {
            Write-Host ("FAIL {0}: {1} -- got '{2}' want a NaN" -f $e.Key, $e.What, $got[$e.Key]) -ForegroundColor Red
            $fail++
        }
        continue
    }
    if ($got[$e.Key] -ne $e.Want) {
        Write-Host ("FAIL {0}: {1} -- got '{2}' want '{3}'" -f $e.Key, $e.What, $got[$e.Key], $e.Want) -ForegroundColor Red
        $fail++
    }
}

if (-not $Keep) { Remove-Item $src -ErrorAction SilentlyContinue }

$n = $expected.Count
if ($fail -eq 0) {
    Write-Host "oracle-scalar: $n/$n agree with the host" -ForegroundColor Green
    exit 0
}
Write-Host "oracle-scalar: $($n - $fail)/$n agree with the host, $fail DISAGREE" -ForegroundColor Red
exit 1
