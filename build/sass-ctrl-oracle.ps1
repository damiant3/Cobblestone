[CmdletBinding()]
param(
    [string]$MaxasDir = (Join-Path $env:TEMP 'sass-ctrl-oracle\maxas'),
    [string]$Perl = 'C:\Program Files\Git\usr\bin\perl.exe'
)

# The oracle for codex/test/sass-ctrl-maxas: maxas's own readCtrl, printCtrl
# and processSassCtrlLine (lib/MaxAs/MaxAsGrammar.pm, unmodified) over the
# vectors the subject builds. The control word is assembled with the
# expression from MaxAs.pm's Assemble and decoded by processSassCtrlLine.
#
#   pwsh build/sass-ctrl-oracle.ps1 > codex/test/sass-ctrl-maxas.expected

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

$work = Join-Path $env:TEMP 'sass-ctrl-oracle'
New-Item -ItemType Directory -Force $work | Out-Null
$pl = Join-Path $work 'oracle.pl'
[IO.File]::WriteAllText($pl, @'
use strict;
use MaxAs::MaxAsGrammar;

sub vec_at {
    my $j = shift;
    return (15, 1, 7, 7, 63, 15) if $j == 0;
    return (0, 0, 0, 0, 0, 0) if $j == 1;
    my $i = $j - 2;
    return (($i * 7) % 16, $i % 2, ($i * 5) % 8, ($i * 3 + 2) % 8, ($i * 13) % 64, ($i * 11) % 16);
}

sub notation {
    my ($s, $y, $w, $r, $m) = @_;
    my $wait = $m ? sprintf('%02x', $m) : '--';
    my $rd = $r == 7 ? '-' : $r + 1;
    my $wr = $w == 7 ? '-' : $w + 1;
    my $yd = $y ? '-' : 'Y';
    return sprintf('%s:%s:%s:%s:%x', $wait, $rd, $wr, $yd, $s);
}

my $count = 27;
my (@ctrl, @reuse);
for my $j (0 .. $count - 1) {
    my @f = vec_at($j);
    my $code = readCtrl(notation(@f), "vector $j");
    push @ctrl, $code;
    push @reuse, $f[5];
    printf "S %d %s %05x %s\n", $j, join(' ', @f), $code, printCtrl($code);
}
for (my $k = 0; 3 * $k < $count; $k++) {
    my @c = @ctrl[3 * $k .. 3 * $k + 2];
    my @u = @reuse[3 * $k .. 3 * $k + 2];
    my $word = ($c[0] <<  0) | ($c[1] << 21) | ($c[2] << 42) |
               ($u[0] << 17) | ($u[1] << 38) | ($u[2] << 59);
    my (@dc, @du);
    processSassCtrlLine(sprintf('        /* 0x%016x */', $word), \@dc, \@du) or die "no decode of word $k";
    printf "W %d %016x %s %d %s %d %s %d\n", $k, $word,
        printCtrl($dc[0]), $du[0], printCtrl($dc[1]), $du[1], printCtrl($dc[2]), $du[2];
}
'@.Replace("`r", ''), [Text.UTF8Encoding]::new($false))

[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$out = & $Perl -I (Join-Path $MaxasDir 'lib') $pl
if ($LASTEXITCODE -ne 0) { Write-Host 'maxas oracle failed'; exit 2 }
[Console]::Out.Write((($out -join "`n") + "`n"))
