# pe-descsize-arm.ps1 -- the cdx-to-pe stub refuses a firmware memory map whose
# DescriptorSize is below 40 (sizeof EFI_MEMORY_DESCRIPTOR).
#
# Compiles one chapter with -Uefi, wraps it with -Script (default
# build/cdx-to-pe.ps1), and boots the PE under codex-vm -uefi three times:
#   control   DescriptorSize 48 (codex-vm's own): the payload prints its output
#   zero      CODEX_VM_UEFI_DESC_SIZE=0: COM1 carries 'D' and the payload never runs
#   short     CODEX_VM_UEFI_DESC_SIZE=32: the same
# A stub without the bound spins on 0 with COM1 silent after its marks, and on 32
# walks 32-byte steps through 48-byte descriptors and refuses a free range ('R').
# The arm reaches the memory-map guard's walk only: the heap-floor walk reads
# the same DescriptorSize after the guard has passed it.
#
#   pwsh build/boot/pe-descsize-arm.ps1 [-Kernel seed\Codex.cdx] [-Script build\cdx-to-pe.ps1]
#
# Exit 0 when every arm passes, 1 on a red, 2 when the arm cannot run.
[CmdletBinding()]
param(
    [string]$Kernel = 'seed\Codex.cdx',
    [string]$Script = 'build\cdx-to-pe.ps1',
    [string]$Test = 'ops/tier0-cyrillic-print',
    [int]$TimeoutSec = 30
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Set-Location $Repo
[Environment]::CurrentDirectory = $Repo
$Vm = Join-Path $Repo 'tools\codex-vm.exe'
$KernelAbs = (Resolve-Path -LiteralPath $Kernel).Path
$ScriptAbs = (Resolve-Path -LiteralPath $Script).Path
Write-Host ("kernel: $Kernel [" + (Get-FileHash -Algorithm SHA256 $KernelAbs).Hash.Substring(0, 16) + "]")

$Work = Join-Path ([IO.Path]::GetTempPath()) ("pe-descsize-arm-" + (Split-Path $Repo -Leaf))
New-Item -ItemType Directory -Force $Work | Out-Null
$src = Join-Path $Repo "codex\test\$Test.codex"
$exp = Join-Path $Repo "codex\test\$Test.expected"
$cdx = Join-Path $Work 'arm.cdx'; $efi = Join-Path $Work 'arm.efi'
Remove-Item $cdx, $efi -ErrorAction SilentlyContinue

& pwsh -NoProfile -File (Join-Path $Repo 'build\compile.ps1') -Src $src -Out $cdx -Log (Join-Path $Work 'compile.log') -Kernel $KernelAbs -Uefi *> (Join-Path $Work 'compile.out')
if (-not (Test-Path -PathType Leaf $cdx)) { Write-Host "pe-descsize-arm: compile failed, see $Work\compile.log"; exit 2 }
& pwsh -NoProfile -File $ScriptAbs -CdxInput $cdx -Out $efi *> (Join-Path $Work 'pe.log')
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -PathType Leaf $efi)) { Write-Host "pe-descsize-arm: cdx-to-pe failed, see $Work\pe.log"; exit 2 }
$want = ([IO.File]::ReadAllText($exp) -replace "`r`n", "`n").TrimEnd("`n")

function Invoke-Arm([string]$Name, [string]$DescSize) {
    $con = Join-Path $Work "$Name.conout"; $ser = Join-Path $Work "$Name.serial"; $err = Join-Path $Work "$Name.err"
    Remove-Item $con, $ser, $err -ErrorAction SilentlyContinue
    $old = $env:CODEX_VM_UEFI_DESC_SIZE
    if ($DescSize) { $env:CODEX_VM_UEFI_DESC_SIZE = $DescSize } else { Remove-Item Env:CODEX_VM_UEFI_DESC_SIZE -ErrorAction SilentlyContinue }
    try {
        $p = Start-Process -FilePath $Vm -ArgumentList @('-kernel', $efi, '-uefi', '-headless', '-conout', $con, '-output', $ser) `
            -WindowStyle Hidden -PassThru -RedirectStandardError $err -RedirectStandardOutput (Join-Path $Work "$Name.stdout")
    } finally {
        if ($null -eq $old) { Remove-Item Env:CODEX_VM_UEFI_DESC_SIZE -ErrorAction SilentlyContinue } else { $env:CODEX_VM_UEFI_DESC_SIZE = $old }
    }
    if (-not $p.WaitForExit($TimeoutSec * 1000)) { Stop-Process -Id $p.Id -Force; [void]$p.WaitForExit(5000) }
    $conText = if (Test-Path $con) { ([IO.File]::ReadAllText($con) -replace "`r`n", "`n").TrimEnd("`n") } else { '' }
    $serText = if (Test-Path $ser) { [IO.File]::ReadAllText($ser) } else { '' }
    $errText = if (Test-Path $err) { [IO.File]::ReadAllText($err) } else { '' }
    return [pscustomobject]@{ Con = $conText; Ser = $serText; Err = $errText }
}

$bad = 0
$r = Invoke-Arm 'control' ''
$ok = $r.Con.Contains($want) -and -not $r.Ser.TrimEnd().EndsWith('D') -and -not $r.Err.Contains('HOST CRASH')
if (-not $ok) { $bad++ }
Write-Host ("{0} control: conout {1} expected" -f $(if ($ok) { 'PASS' } else { 'FAIL' }), $(if ($r.Con.Contains($want)) { 'carries' } else { 'lacks' }))
foreach ($arm in @(@('zero', '0'), @('short', '32'))) {
    $r = Invoke-Arm $arm[0] $arm[1]
    $ok = $r.Ser.TrimEnd().EndsWith('D') -and -not $r.Con.Contains($want) -and -not $r.Err.Contains('HOST CRASH')
    if (-not $ok) { $bad++ }
    $serShown = ($r.Ser -replace '[^\x20-\x7E]', '.')
    if ($serShown.Length -gt 40) { $serShown = $serShown.Substring(0, 40) }
    Write-Host ("{0} {1} (DescriptorSize {2}): COM1 '{3}', payload {4}" -f $(if ($ok) { 'PASS' } else { 'FAIL' }), $arm[0], $arm[1], $serShown, $(if ($r.Con.Contains($want)) { 'ran' } else { 'did not run' }))
}
if ($bad) { Write-Host "pe-descsize-arm: $bad red"; exit 1 }
Write-Host 'pe-descsize-arm: 3 of 3 arms passed'
exit 0
