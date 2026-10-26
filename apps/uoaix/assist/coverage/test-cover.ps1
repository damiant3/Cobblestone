[CmdletBinding()]
param()
# The coverage instrument's control: measures fixture/Fixture.cs driven by fixture/FixtureDriver.cs and compares the
# unhit probes with fixture/expected.txt, both ways: a probe reported unhit that the driver took fails, and so does an
# untaken arm the report misses. Exits 1 on any difference.
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$out=Join-Path ([IO.Path]::GetTempPath()) "assist-cover-control-$PID"
$fixture=Join-Path $PSScriptRoot 'fixture'
$run=pwsh -NoProfile -File (Join-Path $PSScriptRoot '..\cover-assist.ps1') -OutDir $out -Sources (Join-Path $fixture 'Fixture.cs') -Tests (Join-Path $fixture 'FixtureDriver.cs') -Entry 'Uoaix.Assist.CoverFixture.FixtureDriver' 2>&1
$code=$LASTEXITCODE
$report=Join-Path $out 'coverage.txt'
$got=@(if(Test-Path $report){Get-Content $report|Sort-Object})
$want=@(Get-Content (Join-Path $fixture 'expected.txt')|Where-Object{$_}|Sort-Object)
if($code -ne 0 -or $got.Count -eq 0){Write-Output "FAIL the instrument did not run (exit $code):";$run|Where-Object{$_ -notmatch 'CS1701|runtime policy'}|ForEach-Object{Write-Output "  $_"};exit 1}
$diff=@(Compare-Object $want $got)
if($diff.Count -eq 0){Write-Output "PASS coverage control: $($want.Count) untaken probes reported, no taken probe reported";exit 0}
foreach($d in $diff){Write-Output ("FAIL {0} {1}" -f $(if($d.SideIndicator -eq '<='){'missed untaken probe:'}else{'reported a probe not expected unhit:'}),$d.InputObject)}
exit 1
