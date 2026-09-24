param(
    [Parameter(Mandatory)][string]$Kernel,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$kernelPath = (Resolve-Path -LiteralPath $Kernel).Path
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputPath) { throw 'control: output directory must be new' }
[void][IO.Directory]::CreateDirectory($outputPath)
$original = Join-Path $outputPath 'producer.codex'
$control = Join-Path $outputPath 'control.codex'
& pwsh -NoProfile -File (Join-Path $repo 'build/concat-codex-self.ps1') -OutFile $original
if ($LASTEXITCODE -ne 0) { throw 'control: source assembly failed' }
$source = [IO.File]::ReadAllText($original)
$needle = 'in let result = deck-record (method-materialize (fe.method-inputs) ir (fe.type-defs) ceiling)'
if ([regex]::Matches($source,[regex]::Escape($needle)).Count -ne 1) { throw 'control: materialization boundary changed' }
$replacement = 'in let result = MethodMaterialization { type-defs = fe.type-defs, wire = "", bag = empty-bag, plans = [], byte-bound = 0 }'
[IO.File]::WriteAllText($control,$source.Replace($needle,$replacement),[Text.UTF8Encoding]::new($false))
$kernelHash = (Get-FileHash -LiteralPath $kernelPath).Hash
$proof = @{Kernel=$kernelPath;KernelSha256=$kernelHash;ProducerSourceSha256=(Get-FileHash $original).Hash;ControlSourceSha256=(Get-FileHash $control).Hash;Removed=$needle;Replacement=$replacement}
$proof | ConvertTo-Json | Set-Content (Join-Path $outputPath 'provenance.json')
$free = (Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1MB
if ($free -le 1.5) { throw "control: RAM admission refused ($free GiB)" }
& pwsh -NoProfile -File (Join-Path $repo 'build/compile.ps1') -Src $control -Out (Join-Path $outputPath 'control.cdx') -Log (Join-Path $outputPath 'control.diag') -Kernel $kernelPath -Repl
if ($LASTEXITCODE -ne 0) { throw 'control: compilation failed' }
if ((Get-FileHash -LiteralPath $kernelPath).Hash -cne $kernelHash) { throw 'control: kernel changed during build' }
if ((Get-FileHash -LiteralPath $original).Hash -cne $proof.ProducerSourceSha256 -or (Get-FileHash -LiteralPath $control).Hash -cne $proof.ControlSourceSha256) { throw 'control: generated source changed during build' }
$proof.OutputSha256 = (Get-FileHash (Join-Path $outputPath 'control.cdx')).Hash
$proof | ConvertTo-Json | Set-Content (Join-Path $outputPath 'provenance.json')
Set-Content (Join-Path $outputPath 'exit.txt') 0
