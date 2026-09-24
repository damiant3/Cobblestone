param(
    [Parameter(Mandatory)][string]$Kernel,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$kernelPath = (Resolve-Path -LiteralPath $Kernel).Path
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputPath) { throw 'inspector: output directory must be new' }
[void][IO.Directory]::CreateDirectory($outputPath)
. (Join-Path $repo 'codex/plugs/common/plug-build-lib.ps1')
$lines = [Collections.Generic.List[string]]::new()
$paths = @('codex/compiler/Core/BuildSettings.codex','codex/compiler/Core/OffsetTable.codex','codex/compiler/Core/Name.codex','codex/compiler/Core/SourceText.codex','codex/compiler/Types/CodexType.codex','codex/compiler/Types/CodexTypeHelpers.codex','codex/compiler/Ast/AstNodes.codex','codex/compiler/IR/IRChapter.codex','codex/plugs/common/PlugTypes.codex','codex/plugs/common/IRTextParser.codex','codex/compiler/Emit/IRTextEmitter.codex','codex/test/method-template-contract/InspectionSender.codex')
$inputs = [Collections.Generic.List[object]]::new()
foreach ($relative in $paths) {
    $path = Join-Path $repo $relative
    $inputs.Add(@{Path=$path;Sha256=(Get-FileHash -LiteralPath $path).Hash})
    $drop = if ($relative -like '*AstNodes.codex') { @('Deck Copies') } else { @() }
    Add-PlugChapter -Lines $lines -Path $path -Quire Inspect -DropSections $drop
}
$transportPath = Join-Path $repo 'codex/plugs/zig/ZigPlug.codex'
$transport = [IO.File]::ReadAllText($transportPath)
foreach ($needle in @('Chapter: ZigPlug','49152 9145 host-ip','emit-zig-chapter (parsed.chapter) (parsed.type-defs)','net-io-send-text-checked')) {
    if ([regex]::Matches($transport,[regex]::Escape($needle)).Count -ne 1) { throw "inspector: transport contract changed: $needle" }
}
$inputs.Add(@{Path=$transportPath;Sha256=(Get-FileHash -LiteralPath $transportPath).Hash})
$transport = $transport.Replace('Chapter: ZigPlug','Chapter: Inspect--Transport').Replace('49152 9145 host-ip','49152 9196 host-ip').Replace('emit-zig-chapter (parsed.chapter) (parsed.type-defs)','emit-ir-chapter (parsed.chapter) (parsed.meta) (parsed.type-defs)').Replace('net-io-send-text-checked','inspection-send')
foreach ($line in ($transport -split '\r?\n')) { $lines.Add($line) }
$pre = Resolve-PlugForewords $lines
$source = Join-Path $outputPath 'inspection.codex'
[IO.File]::WriteAllText($source,(($pre+$lines)-join "`n")+"`n",[Text.UTF8Encoding]::new($false))
$hash = (Get-FileHash -LiteralPath $kernelPath).Hash
$proof = @{Kernel=$kernelPath;KernelSha256=$hash;Inputs=$inputs.ToArray();SourceSha256=(Get-FileHash $source).Hash}
$proof | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $outputPath 'provenance.json')
$free = (Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1MB
if ($free -le 1.5) { throw "inspector: RAM admission refused ($free GiB)" }
& pwsh -NoProfile -File (Join-Path $repo 'build/compile.ps1') -Src $source -Out (Join-Path $outputPath 'inspection.cdx') -Log (Join-Path $outputPath 'inspection.diag') -Kernel $kernelPath
if ($LASTEXITCODE -ne 0) { throw 'inspector: compilation failed' }
foreach ($inputFile in $inputs) { if ((Get-FileHash -LiteralPath $inputFile.Path).Hash -cne $inputFile.Sha256) { throw 'inspector: input changed during build' } }
if ((Get-FileHash -LiteralPath $kernelPath).Hash -cne $hash) { throw 'inspector: kernel changed during build' }
if ((Get-FileHash -LiteralPath $source).Hash -cne $proof.SourceSha256) { throw 'inspector: generated source changed during build' }
$proof.OutputSha256 = (Get-FileHash (Join-Path $outputPath 'inspection.cdx')).Hash
$proof | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $outputPath 'provenance.json')
Set-Content (Join-Path $outputPath 'exit.txt') 0
