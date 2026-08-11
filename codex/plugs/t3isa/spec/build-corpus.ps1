# Compile the external project's example programs to T3ISA, producing the
# matched (.t3s assembly, .t3b words, .t3d sidecar) triples that
# derive-opcodes.ps1 and validate.ps1 read.
#
# Requires the external toolchain, which lives on this machine only. Nothing in
# build/build.ps1 reaches this script.
param(
  [string]$Manitc  = 'D:\Toolchain-Ternary\target\release\manitc.exe',
  [string]$Corpus  = 'D:\Projects\maniTC-main',
  [string]$Work    = (Join-Path $PSScriptRoot 'build-output')
)
if (-not (Test-Path $Manitc)) { throw "manitc not found at $Manitc. See docs/Designs/Active/Compiler/T3IsaPlug.md 'Cost' for how it is built." }
if (-not (Test-Path $Corpus)) { throw "example corpus not found at $Corpus" }
New-Item -ItemType Directory -Force -Path $Work | Out-Null

$srcs = @()
$srcs += Get-ChildItem (Join-Path $Corpus 'benchmarks') -Filter *.mt -ErrorAction SilentlyContinue
$srcs += Get-ChildItem (Join-Path $Corpus 'examples')   -Filter *.mt -ErrorAction SilentlyContinue

$ok = 0; $skip = 0
foreach ($s in $srcs) {
  $stem = [IO.Path]::GetFileNameWithoutExtension($s.Name)
  $out = Join-Path $Work "$stem.t3b"
  if (Test-Path $out) { Remove-Item $out -Force }
  $log = & $Manitc compile $s.FullName --target t3 -o $out 2>&1 | Out-String

  # The exit code is not the test. manitc exits 0 when its own assembler fails:
  # oop.mt prints "[T3ISA] assembler error: Cannot resolve: float_Point::zero_0",
  # writes the .t3s listing, writes no .t3b, and returns 0. Trusting the code
  # left a listing with no binary in the corpus, which inflated every sabotage
  # arm's score by one. Check for the artifact.
  if ($LASTEXITCODE -eq 0 -and (Test-Path $out)) { $ok++; Write-Output ("OK   {0}" -f $stem); continue }

  # Several of their examples do not compile with their own current compiler,
  # and one assembles no binary. Skipped rather than treated as our failure,
  # but the .t3s must go too or it becomes a phantom corpus entry.
  $orphan = Join-Path $Work "$stem.t3s"
  if (Test-Path $orphan) { Remove-Item $orphan -Force }
  $skip++
  $why = @($log -split "`n" | Where-Object { $_ -match 'error' } | Select-Object -First 1)
  $reason = if ($why.Count -gt 0) { $why[0].Trim() } else { 'no .t3b produced' }
  Write-Output ("SKIP {0} : {1}" -f $stem, $reason)
}
Write-Output ""
Write-Output "compiled: $ok   skipped: $skip   work dir: $Work"
