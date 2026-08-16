# The scale rung's chapter set IS the fibx rung's: same front end, same whole
# x86-64 back end, same finalize. Only the subject differs -- 526 lines of
# CCE instead of eighteen lines of fib -- so this swaps the harness and
# changes nothing else. A second copy of the chapter list would be a second
# thing to keep in step, and the two rungs exist precisely to be identical
# apart from size.
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'bundle_fibx.ps1') `
    -Harness 'ScaleHarness.codex' `
    -OutName 'scale-subject.codex' `
    -PlugName 'scale-subject'
