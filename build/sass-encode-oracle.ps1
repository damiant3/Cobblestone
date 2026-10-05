[CmdletBinding()]
param(
    [string]$MaxasDir = (Join-Path $env:TEMP 'sass-ctrl-oracle\maxas'),
    [string]$Perl = 'C:\Program Files\Git\usr\bin\perl.exe'
)

# The oracle for codex/test/sass-encode-maxas: maxas's own processAsmLine,
# parseInstruct and genCode (lib/MaxAs/MaxAsGrammar.pm, unmodified) over the
# instructions the subject encodes, printed as the 64-bit word then the text,
# and maxas's XMAD.LO expansion (replaceXMADs) of the subject's multiplies.
#
#   pwsh build/sass-encode-oracle.ps1 > codex/test/sass-encode-maxas.expected

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$commit = '54eda7af086a46c9dae1688b691968235d560164'
if (-not (Test-Path -PathType Leaf $Perl)) { Write-Host "MISSING: $Perl (Git for Windows perl)"; exit 2 }
if (-not (Test-Path -PathType Container $MaxasDir)) {
    & git clone --quiet https://github.com/NervanaSystems/maxas.git $MaxasDir
    if ($LASTEXITCODE -ne 0) { Write-Host 'maxas clone failed'; exit 2 }
}
& git -C $MaxasDir checkout --quiet $commit
if ($LASTEXITCODE -ne 0) { Write-Host "maxas checkout of $commit failed"; exit 2 }
$head = (& git -C $MaxasDir rev-parse HEAD).Trim()
if ($head -ne $commit) { Write-Host "maxas is at $head, not $commit"; exit 2 }

$work = Join-Path $env:TEMP 'sass-encode-oracle'
New-Item -ItemType Directory -Force $work | Out-Null
$pl = Join-Path $work 'oracle.pl'
[IO.File]::WriteAllText($pl, @'
use strict;
use MaxAs::MaxAsGrammar;

my @vectors = (
    'IADD R0, R1, R2;',
    'IADD R200, R17, RZ;',
    '@P3 IADD R5, R6, R7;',
    '@!P0 IADD R9, R10, R11;',
    'FADD R3, R4, R5;',
    'FMUL R12, R34, R56;',
    'FFMA R1, R2, R3, R4;',
    'FFMA R100, R101, R102, R103;',
    '@!P6 FFMA R250, R251, R252, R253;',
    'MOV R7, R8;',
    'MOV R254, RZ;',
    'MOV32I R1, 0x3f800000;',
    'MOV32I R2, 0xffffffff;',
    'MOV32I R3, 0x0;',
    'S2R R0, SR_TID.X;',
    'S2R R3, SR_CTAID.Y;',
    'S2R R21, SR_LANEID;',
    'S2R R22, SR_TID.Z;',
    'NOP;',
    'EXIT;',
    '@P1 EXIT;',
    '@!P5 FADD R64, R128, R192;',
    'IADD R0, R1, c[0x0][0x140];',
    'IADD R0, R1, 0x1;',
    'IADD R0, R1, -0x1;',
    'IADD R0, R1, c[0x3][0xfffc];',
    'FADD R0, R1, c[0x0][0x144];',
    'FMUL R0, R1, c[0x2][0x8];',
    'FFMA R0, R1, c[0x0][0x148], R3;',
    'MOV R0, c[0x0][0x140];',
    'MOV R0, 0x7ffff;',
    'MOV R0, -0x80000;',
    'ISETP.LT.AND P0, PT, R1, R2, PT;',
    'ISETP.GE.U32.AND P3, PT, R1, c[0x0][0x150], PT;',
    'ISETP.NE.AND P1, PT, R4, 0x0, PT;',
    '@P4 ISETP.LE.U32.AND P6, PT, R17, -0x80000, PT;',
    'ISETP.EQ.AND P2, PT, R9, R10, PT;',
    'ISETP.GT.U32.AND P5, PT, R11, R12, PT;',
    'FSETP.GT.AND P2, PT, R1, R2, PT;',
    'FSETP.EQ.AND P0, PT, R5, c[0x0][0x10], PT;',
    'FSETP.LT.AND P4, PT, R6, R7, PT;',
    'FSETP.GE.AND P1, PT, R8, RZ, PT;',
    'SHL R0, R1, R2;',
    'SHL R0, R1, 0x2;',
    'SHR R0, R1, 0x3;',
    'SHR.U32 R0, R1, 0x3;',
    'SHR.U32 R0, R1, c[0x0][0x8];',
    'LOP.AND R0, R1, R2;',
    'LOP.OR R0, R1, 0xff;',
    'LOP.XOR R0, R1, c[0x0][0x4];',
    'ISCADD R0, R1, R2, 0x2;',
    'ISCADD R0, R1, c[0x0][0x140], 0x3;',
    'ISCADD R3, R4, 0x7, 0x1f;',
    'LDG.E R0, [R2];',
    'LDG.E R0, [R2+0x10];',
    'LDG.E R0, [R2+-0x10];',
    'LDG.E.64 R4, [R2];',
    '@!P2 LDG.E R9, [R200+0x7ffffc];',
    'STG.E [R2], R0;',
    'STG.E [R2+0x4], R7;',
    'STG.E.64 [R2+0x8], R4;',
    'BRA 0x20;',
    'BRA -0x20;',
    'BRA -0x800000;',
    'XMAD R2, R0, R1, RZ;',
    'XMAD.MRG R3, R0, R1.H1, RZ;',
    'XMAD.PSL.CBCC R0, R0.H1, R3.H1, R2;',
    'XMAD.PSL R4, R5.H1, R6, R7;',
    'XMAD.CHI R4, R5, R6, R7;',
    'XMAD.CLO R8, R9, R10, R11;',
    'XMAD.CSFU R8, R9, R10, R11;',
    'XMAD.PSL.CLO R12, R13, R14, R15;',
    'XMAD.MRG.CBCC R12, R13, R14, R15;',
    'XMAD.CBCC R1, R2, R3, R4;',
    'XMAD R0, R1, c[0x0][0x140], R3;',
    'XMAD.MRG R0, R1, c[0x0][0x140], R3;',
    'XMAD R0, R1, c[0x0][0x140].H1, R3;',
    'XMAD.PSL R5, R6.H1, c[0x2][0x10], R7;',
    'XMAD.CHI R5, R6, c[0x1][0x8], R7;',
    'XMAD R0, R1, 0xffff, R3;',
    'XMAD.PSL R0, R1.H1, 0x1234, R0;',
    '@P2 XMAD R200, R201, R202, R203;',
    'IADD R0.CC, R2, R4;',
    'IADD.X R1, R3, R5;',
    'IADD R0.CC, R2, -R4;',
    'IADD.X R1, R3, -R5;',
    'IADD RZ.CC, -R2, R4;',
    'IADD.X R7.CC, R8, c[0x0][0x144];',
    'IADD R0.CC, R2, -c[0x0][0x140];',
    'IADD.X R1, R3, 0x0;',
    'ISETP.LT.U32.AND P0, PT, R0, R2, PT;',
    'ISETP.LT.X.AND P0, PT, R1, R3, P0;',
    'ISETP.NE.U32.X.OR P1, PT, R1, R3, !P0;',
    'ISETP.EQ.XOR P2, PT, R4, c[0x0][0x8], P3;',
    'SHF.L.W R0, R1, R2, R3;',
    'SHF.R.U64 R0, R1, R2, R3;',
    'SHF.R.S64.HI R1, R1, 0x3, R3;',
    'SHF.L.U64.HI R5, R4, 0x1f, R5;',
    'SHF.R.W.U64.HI R6, R7, R8, R9;',
);
my @macros = (
    'XMAD.LO R0, R1, R2, RZ, R3;',
    'XMAD.LO R10, R11, c[0x0][0x144], R12, R13;',
    'XMAD.LO R100, R7, R8, R9, R250;',
);
for my $text (@vectors) {
    my $inst = processAsmLine("--:-:-:-:1      $text", 1) or die "maxas cannot parse: $text";
    my $code;
    foreach my $gram (@{$grammar{$inst->{op}}}) {
        my $cap = parseInstruct($inst->{inst}, $gram) or next;
        ($code) = genCode($inst->{op}, $gram, $cap);
        last;
    }
    die "no maxas grammar row matches: $text" unless defined $code;
    printf "%016x %s\n", $code, $text;
}
# maxas's own XMAD.LO expansion, then each line through genCode.
for my $text (@macros) {
    my $file = replaceXMADs("\n--:-:-:-:1      $text");
    my $n = 0;
    for my $line (split /\n/, $file) {
        next unless $line =~ /\S/;
        my $inst = processAsmLine($line, 1) or die "maxas cannot parse its own expansion: $line";
        my $code;
        foreach my $gram (@{$grammar{$inst->{op}}}) {
            my $cap = parseInstruct($inst->{inst}, $gram) or next;
            ($code) = genCode($inst->{op}, $gram, $cap);
            last;
        }
        die "no maxas grammar row matches: $line" unless defined $code;
        printf "%016x %s\n", $code, $inst->{inst};
        $n++;
    }
    die "XMAD.LO expanded to $n lines: $text" unless $n == 3;
}
'@.Replace("`r", ''), [Text.UTF8Encoding]::new($false))

[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$out = & $Perl -I (Join-Path $MaxasDir 'lib') $pl
if ($LASTEXITCODE -ne 0) { Write-Host 'maxas oracle failed'; exit 2 }
[Console]::Out.Write((($out -join "`n") + "`n"))
