param(
    [Parameter(Mandatory)][string]$Kernel,
    [Parameter(Mandatory)][string]$Inspector,
    [Parameter(Mandatory)][string]$InputsDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string[]]$Subjects
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot '../../build/vm-config.ps1')
. (Join-Path $PSScriptRoot '../../build/ir-fidelity/ir-wire.ps1')
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$kernelPath = (Resolve-Path -LiteralPath $Kernel).Path
$inspectorPath = (Resolve-Path -LiteralPath $Inspector).Path
$inputPath = (Resolve-Path -LiteralPath $InputsDirectory).Path
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
if ($Subjects.Count -eq 0 -or (Test-Path -LiteralPath $outputPath)) { throw 'contract-inspect: nonempty selection and new output directory required' }
$selected = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($subject in $Subjects) {
    if ($subject -cnotmatch '^[a-z][a-z-]*$' -or -not $selected.Add($subject)) { throw 'contract-inspect: invalid or duplicate subject' }
}
[void][IO.Directory]::CreateDirectory($outputPath)
$kernelHash = (Get-FileHash -LiteralPath $kernelPath).Hash
$inspectorHash = (Get-FileHash -LiteralPath $inspectorPath).Hash
$provenance = Get-Content -LiteralPath (Join-Path $inputPath 'provenance.json') -Raw | ConvertFrom-Json
if ($provenance.KernelSha256 -cne $kernelHash) { throw 'contract-inspect: input IR producer differs from selected kernel' }
$results = [Collections.Generic.List[object]]::new()
function Invoke-InspectionGuest([string]$Tag, [string[]]$Arguments) {
    $free = (Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1MB
    if ($free -le 1.5) { throw "contract-inspect: RAM admission refused ($free GiB)" }
    Write-Host "$Tag RAM=$free GiB, one guest"
    & pwsh -NoProfile -File @Arguments *> (Join-Path $outputPath "$Tag.console")
    return $LASTEXITCODE
}
function Read-Cce([string]$Path) {
    $decoded = ConvertFrom-CceBytesDetailed ([IO.File]::ReadAllBytes($Path))
    if ($decoded.Unmapped + $decoded.Malformed + $decoded.Truncated) { throw "contract-inspect: CCE decode failed: $Path" }
    return $decoded.Text
}
function Read-Section([string]$Text) {
    $tree = ConvertFrom-IrWire $Text
    if ((Get-IrHead $tree) -cne 'chapter') { throw 'contract-inspect: output is not a chapter' }
    foreach ($required in @('defs','type-defs')) {
        $nodes = @($tree | Where-Object { (Get-IrHead $_) -ceq $required })
        if ($nodes.Count -ne 1) { throw "contract-inspect: missing or duplicate runtime section $required" }
    }
    $sections = @($tree | Where-Object { (Get-IrHead $_) -ceq 'method-templates' })
    if ($sections.Count -gt 1) { throw 'contract-inspect: duplicate metadata section' }
    if ($sections.Count -eq 0) { return '' }
    return Format-IrNode $sections[0]
}
try {
    foreach ($subject in $Subjects) {
        $source = Join-Path $inputPath "$subject.codex"
        $inputIr = Join-Path $inputPath "$subject.ir"
        $sourceHash = (Get-FileHash -LiteralPath $source).Hash
        $inputHash = (Get-FileHash -LiteralPath $inputIr).Hash
        $original = Read-Cce $inputIr
        $originalSection = Read-Section $original
        [IO.File]::WriteAllText((Join-Path $outputPath "$subject-cce.ir.txt"),$original,[Text.UTF8Encoding]::new($false))
        $unicodeLog = Join-Path $outputPath "$subject-unicode.diag"
        $rc = Invoke-InspectionGuest "$subject-unicode" @((Join-Path $repo 'build/compile.ps1'),'-Src',$source,'-Out',(Join-Path $outputPath "$subject-unused.out"),'-Log',$unicodeLog,'-Kernel',$kernelPath,'-IrUni','-Passes','text-plug')
        $unicodeText = Get-IrWireText -LogPath $unicodeLog
        $diagnostics = [IO.File]::ReadAllText($unicodeLog)
        if ($rc -ne 0 -or -not $unicodeText -or $diagnostics -match '(?m)^!EXC=|guest serial byte\(s\) DROPPED|^CODEGEN-HALTED') { throw "$subject IR-UNI failed" }
        if ((Read-Section $unicodeText) -cne $originalSection) { throw "$subject IR-UNI/IR-CCE metadata mismatch" }
        [IO.File]::WriteAllText((Join-Path $outputPath "$subject-uni.ir.txt"),$unicodeText,[Text.UTF8Encoding]::new($false))
        $roundtrip = Join-Path $outputPath "$subject-roundtrip.ir"
        $rc = Invoke-InspectionGuest "$subject-inspection" @((Join-Path $repo 'build/run-plug.ps1'),'-Plug',$inspectorPath,'-InFile',$inputIr,'-Output',$roundtrip,'-Port','9196','-MemMB','3072')
        if ($rc -ne 0) { throw "$subject inspection transport failed" }
        $roundText = Read-Cce $roundtrip
        if ((Read-Section $roundText) -cne $originalSection) { throw "$subject parser/emitter lost or changed metadata" }
        [IO.File]::WriteAllText((Join-Path $outputPath "$subject-roundtrip.ir.txt"),$roundText,[Text.UTF8Encoding]::new($false))
        if ((Get-FileHash -LiteralPath $source).Hash -cne $sourceHash -or (Get-FileHash -LiteralPath $inputIr).Hash -cne $inputHash) { throw 'contract-inspect: input changed during proof' }
        $results.Add(@{Subject=$subject;SourceSha256=$sourceHash;InputIrSha256=$inputHash;KernelSha256=$kernelHash;InspectorSha256=$inspectorHash;MetadataModes='PASS';MetadataInspection='PASS';CompleteDecodedIrEqual=($original -ceq $roundText)})
        Write-Host "$subject metadata modes/inspection PASS"
    }
    if ((Get-FileHash -LiteralPath $kernelPath).Hash -cne $kernelHash -or (Get-FileHash -LiteralPath $inspectorPath).Hash -cne $inspectorHash) { throw 'contract-inspect: executable changed during proof' }
    Set-Content -LiteralPath (Join-Path $outputPath 'exit.txt') -Value 0
} catch {
    $_ | Out-String | Set-Content -LiteralPath (Join-Path $outputPath 'failure.txt')
    Set-Content -LiteralPath (Join-Path $outputPath 'exit.txt') -Value 1
    throw
} finally {
    $results.ToArray() | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $outputPath 'results.json')
}
