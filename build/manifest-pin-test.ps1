# The manifest-scope pin.
#
# The compiler emits each program's capability manifest into the CDX header
# (manifest-cap-bytes in X86_64Chapter.codex): per capability, le16 cap-id,
# le16 direction, le32 scope-length, the scope bytes (CCE), le64 zero. That
# emission was verified only by inspection, because nothing inside a guest can
# read its own manifest -- so a regression was caught by no test.
#
# This closes it. The subject is compiled FRESH by the current compiler (so a
# regression is in the bytes we check, not frozen away), its CDX is attached as
# the reader's disk, and the reader parses the header at offset 136 (manifest
# offset, le64) and 144 (size), walks the entries, and prints the id, direction,
# length and scope of each. The output is compared against the expected. If
# manifest-cap-bytes ever writes the wrong direction, drops a scope, or changes
# a cap-id, this fails.
#
# Headless, self-contained, no network, no real hardware.
#
#   pwsh build/manifest-pin-test.ps1                       # against seed/Codex.cdx
#   pwsh build/manifest-pin-test.ps1 -Kernel build/output/Sut.cdx   # against a fresh SUT

param(
  [string]$Kernel = "seed/Codex.cdx"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root "test-output/manifest-pin"
New-Item -ItemType Directory -Force $out | Out-Null

$subjectCdx = Join-Path $out "subject.cdx"
$readerCdx  = Join-Path $out "reader.cdx"
$actual     = Join-Path $out "actual.txt"
$compile    = Join-Path $root "build/compile.ps1"
$vm         = Join-Path $root "tools/codex-vm.exe"
$kernelPath = if ([System.IO.Path]::IsPathRooted($Kernel)) { $Kernel } else { Join-Path $root $Kernel }

Write-Host "[manifest-pin] compiling subject (known manifest) with $Kernel ..."
pwsh $compile -Src (Join-Path $root "codex/test/manifest-subject.codex") -Out $subjectCdx -Log (Join-Path $out "subject.log") -Kernel $kernelPath | Out-Null
if (-not (Test-Path $subjectCdx)) { Write-Host "[manifest-pin] FAIL: subject did not compile" -ForegroundColor Red; exit 1 }

Write-Host "[manifest-pin] compiling reader ..."
pwsh $compile -Src (Join-Path $root "codex/test/manifest-pin.codex") -Out $readerCdx -Log (Join-Path $out "reader.log") -Kernel $kernelPath | Out-Null
if (-not (Test-Path $readerCdx)) { Write-Host "[manifest-pin] FAIL: reader did not compile" -ForegroundColor Red; exit 1 }

Write-Host "[manifest-pin] running reader with the subject CDX as its disk ..."
& $vm -kernel $readerCdx -disk $subjectCdx -headless -output $actual -mem 3072 | Out-Null

$exp = (Get-Content (Join-Path $root "codex/test/manifest-pin.expected")) -replace "`r", ""
$act = if (Test-Path $actual) { (Get-Content $actual) -replace "`r", "" } else { @() }

if (Compare-Object $exp $act) {
  Write-Host "[manifest-pin] FAIL: emitted manifest does not match expected" -ForegroundColor Red
  Write-Host "--- expected ---"; $exp | ForEach-Object { Write-Host $_ }
  Write-Host "--- actual ---";   $act | ForEach-Object { Write-Host $_ }
  exit 1
} else {
  Write-Host "[manifest-pin] PASS: manifest matches expected" -ForegroundColor Green
  $act | ForEach-Object { Write-Host $_ }
  exit 0
}
