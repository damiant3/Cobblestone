param(
    [Parameter(Mandatory)][string]$Kernel,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateSet('zero', 'demands', 'lookalike', 'runtime', 'escape', 'mono', 'bounds-small', 'bounds-large', 'unicode')]
    [string[]]$Only = @('zero', 'demands', 'lookalike', 'runtime', 'escape', 'mono', 'bounds-small', 'bounds-large', 'unicode'),
    [switch]$CaptureIr
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($Only.Count -eq 0) { throw 'contract-run: empty subject selection' }
$selected = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($name in $Only) {
    if (-not $selected.Add($name)) { throw 'contract-run: duplicate subject selection' }
}
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$kernelPath = (Resolve-Path -LiteralPath $Kernel).Path
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputPath) { throw 'contract-run: output directory must be new' }
[void][IO.Directory]::CreateDirectory($outputPath)
$kernelHash = (Get-FileHash -LiteralPath $kernelPath -Algorithm SHA256).Hash
$rows = [Collections.Generic.List[object]]::new()
function Invoke-ContractGuest([string]$Tag, [string[]]$Arguments) {
    $freeGiB = (Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1MB
    if ($freeGiB -le 1.5) { throw "contract-run: RAM admission refused ($freeGiB GiB)" }
    if ((Get-FileHash -LiteralPath $kernelPath -Algorithm SHA256).Hash -cne $kernelHash) {
        throw 'contract-run: producer changed during run'
    }
    $row = [ordered]@{ Tag = $Tag; FreeGiB = $freeGiB; Started = (Get-Date).ToString('o'); Exit = $null }
    & pwsh -NoProfile -File @Arguments *> (Join-Path $outputPath "$Tag.console")
    $row.Exit = $LASTEXITCODE
    $rows.Add($row)
    Write-Host "$Tag exit=$($row.Exit)"
    return $row.Exit
}
try {
    $inputs = [Collections.Generic.List[object]]::new()
    foreach ($name in $Only) {
        foreach ($suffix in @('codex', 'expected')) {
            $file = Join-Path $PSScriptRoot "method-template-contract-$name.$suffix"
            $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
            $copy = Join-Path $outputPath "$name.$suffix"
            Copy-Item -LiteralPath $file -Destination $copy
            if ((Get-FileHash -LiteralPath $copy).Hash -cne $hash) { throw 'contract-run: input changed during snapshot' }
            $inputs.Add(@{ Path = $file; Sha256 = $hash })
            $inputs.Add(@{ Path = $copy; Sha256 = $hash })
        }
    }
    foreach ($relative in @('build/compile.ps1', 'build/test-run.ps1', 'build/quire-map.ps1', 'build/vm-config.ps1', 'build/work-wire.ps1', 'tools/codex-vm.exe', 'codex/foreword/core/ListUtils.codex', 'codex/foreword/core/Tuple.codex')) {
        $file = Join-Path $repo $relative
        $inputs.Add(@{ Path = $file; Sha256 = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash })
    }
    @{ Kernel = $kernelPath; KernelSha256 = $kernelHash; Inputs = @($inputs.ToArray()) } |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $outputPath 'provenance.json') -Encoding utf8
    foreach ($name in $Only) {
        $source = Join-Path $outputPath "$name.codex"
        $binary = Join-Path $outputPath "$name.cdx"
        $diag = Join-Path $outputPath "$name.diag"
        $rc = Invoke-ContractGuest "$name-compile" @((Join-Path $repo 'build/compile.ps1'), '-Src', $source, '-Out', $binary, '-Log', $diag, '-Kernel', $kernelPath)
        if ($rc -ne 0) { Get-Content -LiteralPath $diag | Write-Host; throw "$name native compile failed" }
        $out = Join-Path $outputPath "$name.out"
        $rc = Invoke-ContractGuest "$name-run" @((Join-Path $repo 'build/test-run.ps1'), '-Kernel', $binary, '-OutFile', $out)
        if ($rc -ne 0) { throw "$name native run failed" }
        $expected = [IO.File]::ReadAllText((Join-Path $outputPath "$name.expected")).Replace("`r", '')
        $actual = [IO.File]::ReadAllText($out)
        if ($actual -cne $expected) { throw "$name output mismatch: $out" }
        Write-Host "$name exact native output PASS"
        if ($CaptureIr) {
            $ir = Join-Path $outputPath "$name.ir"
            $diag = Join-Path $outputPath "$name-ir.diag"
            $rc = Invoke-ContractGuest "$name-ir" @((Join-Path $repo 'build/compile.ps1'), '-Src', $source, '-Out', $ir, '-Log', $diag, '-Kernel', $kernelPath, '-IrCce', '-Passes', 'text-plug')
            if ($rc -ne 0) { Get-Content -LiteralPath $diag | Write-Host; throw "$name CCE IR compile failed" }
        }
    }
    foreach ($proofInput in $inputs) {
        if ((Get-FileHash -LiteralPath $proofInput.Path -Algorithm SHA256).Hash -cne $proofInput.Sha256) {
            throw "contract-run: input changed during run: $($proofInput.Path)"
        }
    }
    if ((Get-FileHash -LiteralPath $kernelPath).Hash -cne $kernelHash) { throw 'contract-run: producer changed during run' }
    Set-Content -LiteralPath (Join-Path $outputPath 'exit.txt') -Value 0
} catch {
    $_ | Out-String | Set-Content -LiteralPath (Join-Path $outputPath 'failure.txt')
    Set-Content -LiteralPath (Join-Path $outputPath 'exit.txt') -Value 1
    throw
} finally {
    $rows.ToArray() | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $outputPath 'runs.json')
}
