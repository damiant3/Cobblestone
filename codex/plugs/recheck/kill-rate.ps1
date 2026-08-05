# The rechecker's kill-rate. This is the deliverable, not the rechecker.
#
# A rechecker that agrees with the compiler on every input in the tree is
# indistinguishable from a program that returns AGREE unconditionally.
# Agreement is not evidence; sensitivity is. So every mutation below is a
# single targeted corruption of VALID IR that the rechecker MUST report as
# DISAGREE, and the run reports caught over planted, per class.
#
# The mutations are applied to the IR TEXT rather than to the source, because
# the compiler would reject the source. That is also what makes the corpus
# independently useful: a mutation the COMPILER accepts is a soundness bug
# found directly, with the rechecker agreeing or disagreeing about nothing.
#
# THE CONTROL IS THE FIRST ROW AND IT IS NOT A FORMALITY. The unmutated IR
# must come back with zero DISAGREE. Without it a rechecker that answered
# DISAGREE unconditionally would score a perfect kill-rate.
#
#   pwsh codex/plugs/recheck/kill-rate.ps1
[CmdletBinding()]
param([string]$Subject = '', [string]$Kernel = '')

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '..' '..' '..' 'build' 'vm-config.ps1')

$Repo    = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..')).Path
$PlugDir = (Resolve-Path $PSScriptRoot).Path
$PlugCdx = Join-Path $PlugDir 'build-output\recheck-plug.cdx'
$WorkDir = Join-Path $PlugDir 'build-output\kill-rate'
if (-not $Subject) { $Subject = Join-Path $PlugDir 'corpus\subject.codex' }
# The effect row and the linear fact only appear on the wire from the compiler
# that publishes them. Until that seed is installed the corpus must be built
# with the SUT; afterwards the default is correct.
if (-not $Kernel) { $Kernel = Join-Path $Repo 'seed\Codex.cdx' }

if (-not (Test-Path -PathType Leaf $PlugCdx)) {
    [Console]::Error.WriteLine("MISSING: $PlugCdx -- run codex/plugs/recheck/build.ps1 first")
    exit 2
}
New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null

# -- Produce the valid IR once ----------------------------------------
$baseIr  = Join-Path $WorkDir 'base.ir'
$baseLog = Join-Path $WorkDir 'base.log'
& pwsh -NoProfile -File (Join-Path $Repo 'build\compile.ps1') -Src $Subject -Out $baseIr `
    -Log $baseLog -IrCce -Passes none -Kernel $Kernel 2>&1 | Out-Null
if (Select-String -Path $baseLog -Pattern 'error CDX' -Quiet) {
    [Console]::Error.WriteLine("FAIL: subject did not compile; see $baseLog")
    exit 4
}
$baseBytes = [System.IO.File]::ReadAllBytes($baseIr)

# The IR wire is CCE, not ASCII, so an anchor written in ASCII finds nothing.
# CCE shares no boundary with ASCII: '(' is 74 and 'a' is 15. This map is the
# compiler's own answer, produced by printing `from-unicode u` for u in
# 32..126 against the depot seed and pasted here.
#
# It is not trusted on its own: every anchor below must encode and then be
# found EXACTLY ONCE in the base IR. A wrong map finds nothing and the run
# reports NOT-APPLICABLE rather than quietly planting a mutation that was
# never planted.
$script:AsciiToCce = @{}
foreach ($p in ('32=2;33=67;34=72;35=83;36=95;37=96;38=84;39=71;40=74;41=75;42=78;43=76;44=66;45=73;46=65;47=81;' +
                '48=3;49=4;50=5;51=6;52=7;53=8;54=9;55=10;56=11;57=12;58=69;59=70;60=79;61=77;62=80;63=68;64=82;' +
                '65=41;66=58;67=50;68=48;69=39;70=54;71=55;72=46;73=43;74=61;75=60;76=49;77=52;78=44;79=42;80=57;' +
                '81=63;82=47;83=45;84=40;85=51;86=59;87=53;88=62;89=56;90=64;91=88;92=86;93=89;94=94;95=85;96=93;' +
                '97=15;98=32;99=24;100=22;101=13;102=28;103=29;104=20;105=17;106=35;107=34;108=23;109=26;110=18;' +
                '111=16;112=31;113=37;114=21;115=19;116=14;117=25;118=33;119=27;120=36;121=30;122=38;123=90;' +
                '124=87;125=91;126=92') -split ';') {
    $kv = $p -split '='
    $script:AsciiToCce[[int]$kv[0]] = [byte][int]$kv[1]
}

function ConvertTo-Cce([string]$s) {
    $out = New-Object byte[] $s.Length
    for ($i = 0; $i -lt $s.Length; $i++) {
        $c = [int][char]$s[$i]
        if (-not $script:AsciiToCce.ContainsKey($c)) { return $null }
        $out[$i] = $script:AsciiToCce[$c]
    }
    return ,$out
}

function Find-Bytes([byte[]]$hay, [byte[]]$needle, [int]$start) {
    $limit = $hay.Length - $needle.Length
    for ($i = $start; $i -le $limit; $i++) {
        $ok = $true
        for ($j = 0; $j -lt $needle.Length; $j++) {
            if ($hay[$i + $j] -ne $needle[$j]) { $ok = $false; break }
        }
        if ($ok) { return $i }
    }
    return -1
}

function Count-Bytes([byte[]]$hay, [byte[]]$needle) {
    $n = 0; $at = 0
    while ($true) {
        $at = Find-Bytes $hay $needle $at
        if ($at -lt 0) { break }
        $n++; $at++
    }
    return $n
}

# -- The mutations ----------------------------------------------------
# Each is one targeted edit with an anchor that must occur in the base IR.
# A mutation whose anchor is missing is reported as NOT-APPLICABLE rather
# than silently counting as caught, because a corpus that quietly plants
# nothing scores 0 of 0 and reads as success.
$mutations = @(
    @{ Class = 'unbound-name'
       From  = '(name "label" (fn int-default text))'
       To    = '(name "no-such-name-anywhere" (fn int-default text))' },

    @{ Class = 'apply-arg-type'
       From  = '(apply (name "show" (fn int-default text)) (name "n" int-default) text)'
       To    = '(apply (name "show" (fn int-default text)) (name "n" text) text)' },

    @{ Class = 'apply-result-type'
       From  = '(apply (name "show" (fn int-default text)) (name "n" int-default) text)'
       To    = '(apply (name "show" (fn int-default text)) (name "n" int-default) boolean)' },

    @{ Class = 'apply-non-function'
       From  = '(apply (name "label" (fn int-default text))'
       To    = '(apply (name "label" text)' },

    @{ Class = 'ctor-pat-field-type'
       From  = '(ctor-pat "Dim" (subs (var-pat "k" int-default))'
       To    = '(ctor-pat "Dim" (subs (var-pat "k" text))' },

    @{ Class = 'ctor-pat-unknown'
       From  = '(ctor-pat "Dim" (subs'
       To    = '(ctor-pat "NotAConstructor" (subs' },

    @{ Class = 'ctor-pat-arity'
       From  = '(ctor-pat "Bright" (subs (var-pat "t" text))'
       To    = '(ctor-pat "Bright" (subs (var-pat "t" text) (var-pat "zz" text))' },

    @{ Class = 'ctor-ref-payload-type'
       From  = '(name "Bright" (fn text (sum "Shade"'
       To    = '(name "Bright" (fn boolean (sum "Shade"' },

    # Fair is a real constructor and resolves as a name, but it belongs to
    # Weather, not Shade. Naming something that binds nowhere would be caught
    # by the unbound-name arm instead and leave this one unexercised, which is
    # exactly what the first version of this corpus did.
    @{ Class = 'ctor-ref-unknown'
       From  = '(name "Dim" (fn int-default (sum "Shade"'
       To    = '(name "Fair" (fn int-default (sum "Shade"' },

    # Stage 2. A literal too large for the bounded parameter it is handed to.
    @{ Class = 'bounds-exceeded-arg'; Kind = 'bounds-exceeded'
       From  = '(name "narrow" (fn (int 0 10 ov-error) (int 0 20 ov-error))) (int-lit 5)'
       To    = '(name "narrow" (fn (int 0 10 ov-error) (int 0 20 ov-error))) (int-lit 500)' },

    # The declared return narrowed below what the body can produce. 0..15 is
    # chosen so the two candidate readings DISAGREE, which 0..5 would not:
    # the body is n * 2 over 0..10, the binary node carries its LEFT OPERAND
    # type 0..10, and 0..10 fits inside 0..15 while the real product 0..20 does
    # not. A rechecker that read the node type would pass this and a rechecker
    # that derives the product fails it. With 0..5 both readings fail and the
    # mutation would score whether or not the derivation exists.
    @{ Class = 'bounds-exceeded-return'; Kind = 'bounds-exceeded'
       From  = '(params (param "n" (int 0 10 ov-error))) (fn (int 0 10 ov-error) (int 0 20 ov-error))'
       To    = '(params (param "n" (int 0 10 ov-error))) (fn (int 0 10 ov-error) (int 0 15 ov-error))' },

    # The design's own class: widen a bounded integer past its declared hi.
    # 0..15 for the same reason as above: it still fits the declared return of
    # 0..20 when read as the node type, and only the derived product 0..30
    # exceeds it. 0..100 would be caught either way and prove nothing about
    # whether the range is being derived at all.
    @{ Class = 'bounds-widened-operand'; Kind = 'bounds-exceeded'
       From  = '(binary mul-int (name "n" (int 0 10 ov-error))'
       To    = '(binary mul-int (name "n" (int 0 15 ov-error))' },

    # Stage 3. The declared effect removed from a signature whose body still
    # performs it. announce's body calls print-line-uni, which carries
    # Console.Write, and Console.Write is covered only by a declared Console.
    @{ Class = 'effect-undeclared'
       From  = '(fn text nothing (row (labels (label "Console" "")) "" -1))'
       To    = '(fn text nothing)' },

    # Stage 3, the grounds table. touch performs port-out-byte, a
    # [Device.Port] intrinsic, behind a pure signature, which is legal only
    # because the chapter grounds Device.Port. Delete the entry from the wire
    # and the discharge is gone, so the call must be reported.
    #
    # This row exists because publishing the table made a whole class of
    # finding go quiet -- 90 of the 97 in the first complete sweep -- and a
    # check that went quiet has to be shown to still fire. Dropping the effect
    # half alone would not do it: "Corpus\nDevice.Port" against a def whose
    # chapter-slug is RecheckSubject must fail to match, so this also pins
    # that the SLUG is compared rather than any entry discharging anything.
    @{ Class = 'grounds-dropped'; Kind = 'effect-undeclared'
       From  = '(grounds "RecheckSubject\nDevice.Port")'
       To    = '(grounds "Elsewhere\nDevice.Port")' },

    # A linear parameter used twice. Exactly-once is the whole discipline and
    # the front end refuses this with CDX2061.
    @{ Class = 'linear-reused'
       From  = '(binary add-int (name "q" int-default) (int-lit 1) int-default)'
       To    = '(binary add-int (name "q" int-default) (name "q" int-default) int-default)' },

    # A linear parameter named by the def but mentioned nowhere in its body,
    # which is the leak half of the same discipline (CDX2063).
    @{ Class = 'linear-unused'
       From  = '(unique "q")'
       To    = '(unique "zz")' }
)

function Invoke-Recheck([string]$IrPath) {
    $stderrFile = [System.IO.Path]::GetTempFileName()
    $proc = $null
    try {
        $proc = Start-Process -FilePath $script:CodexVmBin `
            -ArgumentList @('-kernel', $PlugCdx, '-mem', '3072', '-headless') `
            -PassThru -WindowStyle Hidden -RedirectStandardError $stderrFile
        $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 9100)
        $listener.Start()
        $deadline = (Get-Date).AddSeconds(30)
        while (-not $listener.Pending() -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 100 }
        if (-not $listener.Pending()) { $listener.Stop(); return '' }
        $client = $listener.AcceptTcpClient()
        $listener.Stop()
        $ns = $client.GetStream()
        $data = [System.IO.File]::ReadAllBytes($IrPath)
        $ns.Write([BitConverter]::GetBytes([int]($data.Length + 1)), 0, 4)
        $ns.WriteByte(1)
        $off = 0
        while ($off -lt $data.Length) {
            $n = [Math]::Min(4096, $data.Length - $off)
            $ns.Write($data, $off, $n); $ns.Flush(); $off += $n
            if ($off -lt $data.Length) { Start-Sleep -Milliseconds 20 }
        }
        $ns.ReadTimeout = 300000
        $resp = [System.Collections.Generic.List[byte]]::new()
        $buf = New-Object byte[] 65536
        while ($true) {
            try { $n = $ns.Read($buf, 0, $buf.Length) } catch { break }
            if ($n -le 0) { break }
            for ($i = 0; $i -lt $n; $i++) { $resp.Add($buf[$i]) }
        }
        $client.Close()
        return [System.Text.Encoding]::ASCII.GetString($resp.ToArray())
    } finally {
        if ($proc -and -not $proc.HasExited) { try { Stop-Process -Id $proc.Id -Force -ErrorAction Stop } catch {} }
        Remove-Item -Force $stderrFile -ErrorAction SilentlyContinue
    }
}

# -- The control ------------------------------------------------------
Write-Host ''
Write-Host '=== CONTROL: unmutated IR must produce zero DISAGREE ==='
$controlOut = Invoke-Recheck $baseIr
Write-Host $controlOut
# Two stages report two summary lines, so "DISAGREE 0" appearing anywhere is
# not the question: a clean stage 1 beside a disagreeing stage 2 would match it
# and pass a control that had failed. Finding lines begin with the verdict.
$controlOk = -not ($controlOut -match '(?m)^DISAGREE ')
if (-not $controlOk) {
    Write-Host 'CONTROL FAILED: the rechecker disagrees with valid IR. Every kill below is suspect.'
}

# -- The mutations ----------------------------------------------------
Write-Host '=== MUTATIONS ==='
$planted = 0; $caught = 0
$rows = [System.Collections.Generic.List[object]]::new()
foreach ($m in $mutations) {
    $class = $m.Class
    $fromB = ConvertTo-Cce $m.From
    $toB   = ConvertTo-Cce $m.To
    if ($null -eq $fromB -or $null -eq $toB) {
        $rows.Add([pscustomobject]@{ Class = $class; Result = 'NOT-APPLICABLE (unencodable)' })
        Write-Host ("  {0,-24} NOT-APPLICABLE -- anchor has a character outside the CCE map" -f $class)
        continue
    }
    $hits = Count-Bytes $baseBytes $fromB
    if ($hits -eq 0) {
        $rows.Add([pscustomobject]@{ Class = $class; Result = 'NOT-APPLICABLE (anchor absent)' })
        Write-Host ("  {0,-24} NOT-APPLICABLE -- anchor not present in base IR" -f $class)
        continue
    }
    $planted++
    $idx = Find-Bytes $baseBytes $fromB 0
    $tailStart = $idx + $fromB.Length
    $tailLen   = $baseBytes.Length - $tailStart
    $mut = New-Object byte[] ($idx + $toB.Length + $tailLen)
    [System.Array]::Copy($baseBytes, 0, $mut, 0, $idx)
    [System.Array]::Copy($toB, 0, $mut, $idx, $toB.Length)
    [System.Array]::Copy($baseBytes, $tailStart, $mut, $idx + $toB.Length, $tailLen)
    $mutPath = Join-Path $WorkDir "mut-$class.ir"
    [System.IO.File]::WriteAllBytes($mutPath, $mut)
    $out = Invoke-Recheck $mutPath
    [System.IO.File]::WriteAllText((Join-Path $WorkDir "mut-$class.report"), $out)

    # A mutation may name the finding kind it expects when that differs from
    # its own label: three separate bounds mutations all surface as one kind.
    $wantKind = if ($m.ContainsKey('Kind')) { $m.Kind } else { $class }
    $sawClass = $out -match ("DISAGREE .*\[" + [regex]::Escape($wantKind) + "\]")
    $sawAny   = $out -match '(?m)^DISAGREE '
    if ($sawClass) {
        $caught++
        $rows.Add([pscustomobject]@{ Class = $class; Result = 'CAUGHT' })
        Write-Host ("  {0,-24} CAUGHT" -f $class)
    } elseif ($sawAny) {
        $caught++
        $rows.Add([pscustomobject]@{ Class = $class; Result = 'CAUGHT (different class)' })
        Write-Host ("  {0,-24} CAUGHT but reported under another class" -f $class)
    } else {
        $rows.Add([pscustomobject]@{ Class = $class; Result = 'MISSED' })
        Write-Host ("  {0,-24} MISSED" -f $class)
    }
}

Write-Host ''
Write-Host '=== KILL RATE ==='
Write-Host ("  control (valid IR, zero disagreements) : {0}" -f $(if ($controlOk) { 'PASS' } else { 'FAIL' }))
Write-Host ("  mutations caught / planted             : {0} / {1}" -f $caught, $planted)
if ($planted -gt 0) {
    Write-Host ("  kill rate                              : {0}%" -f [math]::Round(100.0 * $caught / $planted, 1))
}
Write-Host ''
Write-Host 'A class below 100 per cent is a hole in the rechecker, stated here rather'
Write-Host 'than discovered later. Reports are in build-output/kill-rate/.'

if (-not $controlOk) { exit 1 }
if ($planted -eq 0) { exit 1 }
exit 0
