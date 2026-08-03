# Test the bare-metal exception handler output.
# Compiles crashing samples, boots them with a short timeout,
# and verifies the serial output contains the expected dump format.
[CmdletBinding()]
param(
    [string]$CodexCdx = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'vm-config.ps1')

$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $CodexCdx) { $CodexCdx = Join-Path $Repo 'seed\Codex.cdx' }
$compile = Join-Path $PSScriptRoot 'compile.ps1'
$outDir = Join-Path $Repo 'build-output\exc-test'
if (Test-Path $outDir) { Remove-Item -Recurse -Force $outDir }
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$stage0Dir = Join-Path $Repo 'build-output\bare-metal'
New-Item -ItemType Directory -Force -Path $stage0Dir | Out-Null
Copy-Item -Force $CodexCdx (Join-Path $stage0Dir 'Codex.cdx')

# NeedFrames asserts the guest's RBP frame walk actually produced frames.
# exc-deep-frames faults six non-tail recursions down, so a working walk
# reports at least three; a walk that silently stopped reports none, and
# without this the sample would pass on the !EXC= line alone and the
# capability could rot unnoticed. The shallower samples fault at the top of
# the stack, which the handler's own RSP switch overwrites before the walk
# runs, so they are not expected to yield a chain.
$samples = @(
    @{ Name = 'exc-div-zero';    Pattern = '!EXC=';          NeedStack = $true },
    @{ Name = 'exc-null-read';   Pattern = '!EXC=';          NeedStack = $true },
    @{ Name = 'exc-gpf';         Pattern = '!EXC=';          NeedStack = $true },
    @{ Name = 'exc-deep-frames'; Pattern = '!EXC=';          NeedStack = $true; NeedFrames = 3 },
    @{ Name = 'exc-stack-heap';  Pattern = 'OUT OF MEMORY';  NeedStack = $false; TimeoutSec = 90 }
)

$pass = 0
$fail = 0

foreach ($s in $samples) {
    $src = Join-Path $Repo "codex\test\$($s.Name).codex"
    $cdx = Join-Path $outDir "$($s.Name).cdx"
    $log = Join-Path $outDir "$($s.Name).log"

    Write-Host -NoNewline "$($s.Name): "

    & pwsh -NoProfile -File $compile -Src $src -Out $cdx -Log $log 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAIL (compile)"
        $fail++
        continue
    }

    # These samples exist to crash the guest, and they crash in well under a
    # second. Start-VmRun cannot observe that: it sleeps 500 ms, treats an
    # already-exited process as a failed launch, and retries four times before
    # returning null. So every sample reported "FAIL (vm start)" and the file
    # asserted nothing. Run the VM directly with -serial stdio instead and read
    # what it captured after it halts; a fast exit is the expected outcome here,
    # not a launch failure.
    #
    # That logic was not wrong when it was written, and it is worth knowing why,
    # because nothing in this file changed. It was written against QEMU, whose
    # chardev is "server=on,wait=on": QEMU blocks before running the guest until
    # the harness connects, so the process was still alive at the 500 ms check
    # and the read loop got its output. codex-vm has no such wait. And the
    # choice between them is not a flag but
    #     $script:UseCodexVm = Test-Path -PathType Leaf $script:CodexVmBin
    # so the day codex-vm.exe landed in the depot this test broke in every
    # workspace at once, with no changelist touching it and nothing to announce
    # it. A liveness check that assumes the VM waits is the coupling to see.
    $so = Join-Path $outDir "$($s.Name).stdout"
    $se = Join-Path $outDir "$($s.Name).stderr"
    # exc-stack-heap has to recurse until the stack collides with a 2.1 GB heap
    # advance, so it needs a bigger budget than the fault samples, which finish
    # in under a second.
    #
    # KNOWN FAILING, and pre-existing: it does not merely need longer, it never
    # terminates. Measured 2026-07-26 on the depot seed, a run left going for
    # over twelve hours never printed OUT OF MEMORY and never exited, and
    # codex-vm's own -timeout did not stop it either. This was invisible while
    # the harness reported every sample as "FAIL (vm start)". The budget below
    # is therefore sized to fail fast rather than to let it finish; raising it
    # will not help until the hang itself is understood.
    $budget = if ($s.ContainsKey('TimeoutSec')) { [int]$s.TimeoutSec } else { 45 }
    $vmArgs = @('-kernel', $cdx, '-serial', 'stdio', '-mem', '3072', '-timeout', "$budget")
    $proc = Start-Process -FilePath $script:CodexVmBin -ArgumentList $vmArgs -PassThru `
        -WindowStyle Hidden -RedirectStandardOutput $so -RedirectStandardError $se
    $proc | Wait-Process -Timeout ($budget + 30) -ErrorAction SilentlyContinue
    if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
    $output = ''
    foreach ($f in @($so, $se)) {
        $t = Get-Content $f -Raw -ErrorAction SilentlyContinue
        if ($t) { $output += $t }
    }
    Remove-Item -Force $so, $se -ErrorAction SilentlyContinue

    [System.IO.File]::WriteAllText((Join-Path $outDir "$($s.Name).out"), $output)

    $hasPattern = $output.Contains($s.Pattern)
    $dumpLines = @($output -split "`n" | Where-Object { $_ -match '^S\[' }).Count
    $stackOk = if ($s.NeedStack) { $dumpLines -ge 14 } else { $true }
    $frameLines = @($output -split "`n" | Where-Object { $_ -match '^F\[' }).Count
    $needFrames = if ($s.ContainsKey('NeedFrames')) { [int]$s.NeedFrames } else { 0 }
    $framesOk = $frameLines -ge $needFrames

    if ($hasPattern -and $stackOk -and $framesOk) {
        Write-Host "PASS (pattern=$hasPattern stack=$dumpLines frames=$frameLines)"
        $pass++
    } else {
        Write-Host "FAIL (pattern=$hasPattern stack=$dumpLines frames=$frameLines need=$needFrames)"
        Write-Host "  output: $($output.Substring(0, [math]::Min(200, $output.Length)))"
        $fail++
    }
}

Write-Host ""
Write-Host "Exception handler tests: pass=$pass fail=$fail"
if ($fail -gt 0) { exit 1 }
exit 0
