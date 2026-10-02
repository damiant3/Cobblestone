[CmdletBinding()]
param(
    [string]$MaxasDir = (Join-Path $env:TEMP 'sass-ctrl-oracle\maxas'),
    [string]$Perl = 'C:\Program Files\Git\usr\bin\perl.exe'
)

# Grades the sass plug's listing of test/first-slice.codex with maxas
# (54eda7af): every instruction line through maxas's own processAsmLine,
# readCtrl and genCode, and every control word against maxas's Assemble
# packing of the three control codes after it (reuse flags 0). It also
# requires the three in-slice kernels to be listed and the two out-of-slice
# kernels to be refused for their reasons. What a kernel computes is not
# graded here. The plug is rebuilt from source first.
#
#   pwsh codex/plugs/sass/test-maxas.ps1

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

$out = Join-Path $PSScriptRoot 'build-output'
& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'build.ps1') | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host 'FAIL: plug build'; exit 1 }
$listing = Join-Path $out 'first-slice.sass'
if (Test-Path $listing) { [IO.File]::Delete($listing) }
& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'run.ps1') -Src (Join-Path $PSScriptRoot 'test\first-slice.codex') -Out $listing | Out-Null
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $listing)) { Write-Host 'FAIL: plug run'; exit 1 }

$text = [IO.File]::ReadAllText($listing)
$failed = 0
foreach ($k in 'gpu-scale-add', 'gpu-global-index', 'gpu-nested') {
    if (-not $text.Contains(".kernel $k`n")) { Write-Host "FAIL: kernel $k not listed"; $failed++ }
}
foreach ($r in 'REFUSED gpu-refuse-recursion: recursion count-down', 'REFUSED gpu-refuse-multiply: multiply') {
    if (-not $text.Contains($r)) { Write-Host "FAIL: missing '$r'"; $failed++ }
}

$work = Join-Path $env:TEMP 'sass-plug-oracle'
New-Item -ItemType Directory -Force $work | Out-Null
$pl = Join-Path $work 'grade.pl'
[IO.File]::WriteAllText($pl, @'
use strict;
use MaxAs::MaxAsGrammar;

my ($insts, $words, $bad) = (0, 0, 0);
my ($want, @ctrl);
open my $fh, '<', $ARGV[0] or die "cannot read $ARGV[0]";
while (my $line = <$fh>) {
    chomp $line;
    if ($line =~ /^C ([0-9a-f]{16})$/) {
        $want = $1;
        @ctrl = ();
    } elsif ($line =~ /^I (\S+) ([0-9a-f]{16}) (.*)$/) {
        my ($c, $hex, $t) = ($1, $2, $3);
        my $inst = processAsmLine("$c      $t", 1) or die "maxas cannot parse: $line";
        my $code;
        foreach my $gram (@{$grammar{$inst->{op}}}) {
            my $cap = parseInstruct($inst->{inst}, $gram) or next;
            ($code) = genCode($inst->{op}, $gram, $cap);
            last;
        }
        die "no maxas grammar row matches: $line" unless defined $code;
        $insts++;
        if (sprintf('%016x', $code) ne $hex) { printf "WORD %016x plug %s: %s\n", $code, $hex, $t; $bad++ }
        push @ctrl, $inst->{ctrl};
        if (@ctrl == 3) {
            my $w = sprintf('%016x', ($ctrl[0] << 0) | ($ctrl[1] << 21) | ($ctrl[2] << 42));
            $words++;
            if ($w ne $want) { print "CTRL $w plug $want\n"; $bad++ }
        }
    }
}
print "maxas: $insts instructions, $words control words, $bad mismatches\n";
exit($bad ? 1 : 0);
'@.Replace("`r", ''), [Text.UTF8Encoding]::new($false))
& $Perl -I (Join-Path $MaxasDir 'lib') $pl $listing
if ($LASTEXITCODE -ne 0) { $failed++ }

$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$bundle = [IO.File]::ReadAllText((Join-Path $out 'plug-source.codex'))
$cut = $bundle.IndexOf('Chapter: Sass--SassPlug')
if ($cut -lt 0) { throw 'SassPlug chapter boundary missing' }
$probe = Join-Path $out 'emit-cost.codex'
[IO.File]::WriteAllText($probe, $bundle.Substring(0, $cut) + [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'test/emit-cost.codex')))
$probeCdx = Join-Path $out 'emit-cost.cdx'
$probeLog = Join-Path $out 'emit-cost.log'
$probeOut = Join-Path $out 'emit-cost.out'
if ((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory -lt 1572864) { throw 'emit-cost: RAM admission failed' }
& pwsh -NoProfile -File (Join-Path $repo 'build/compile.ps1') -Src $probe -Out $probeCdx -Log $probeLog -Kernel (Join-Path $repo 'seed/Codex.cdx')
if ($LASTEXITCODE -ne 0) { Get-Content $probeLog; throw 'emit-cost compile failed' }
if ((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory -lt 1572864) { throw 'emit-cost: RAM admission failed' }
& pwsh -NoProfile -File (Join-Path $repo 'build/test-run.ps1') -Kernel $probeCdx -OutFile $probeOut
if ($LASTEXITCODE -ne 0) { throw 'emit-cost run failed' }
$cost = [IO.File]::ReadAllLines($probeOut)
$cost | ForEach-Object { Write-Host $_ }
if ($cost -notcontains 'FORK True') { throw 'lowering state snapshots changed' }
$sizes = @(1024, 2048, 4096, 8192)
$index = 0
$previous = 0L
foreach ($line in $cost) {
    if ($line -notmatch '^COST (\d+) (\d+) (\d+) (\d+) True$') { continue }
    $n = [long]$matches[1]; $bytes = [long]$matches[2]; $count = [long]$matches[4]
    if ($index -ge $sizes.Count -or $n -ne $sizes[$index] -or $count -ne $n) { throw 'emit-cost output coverage failed' }
    if ($bytes -le 0 -or $bytes -gt 512 * $n -or ($previous -gt 0 -and $bytes -gt 3 * $previous)) { throw 'emit-cost heap is not linear' }
    $previous = $bytes
    $index++
}
if ($index -ne $sizes.Count) { throw 'emit-cost missing or incorrect instructions' }
if ($failed) { Write-Host "FAIL: $failed"; exit 1 }
Write-Host 'PASS'
