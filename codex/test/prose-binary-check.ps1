[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Baseline,
    [Parameter(Mandatory)][string]$Candidate,
    [Parameter(Mandatory)][string]$OutDir
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Set-Location $repo
$Baseline = (Resolve-Path -LiteralPath $Baseline).Path
$Candidate = (Resolve-Path -LiteralPath $Candidate).Path
if (Test-Path -LiteralPath $OutDir) { throw 'Use a fresh output directory.' }
$out = (New-Item -ItemType Directory -Path $OutDir).FullName
foreach ($subject in @('prose-binary-continuation', 'prose-binary-control')) {
    $source = Join-Path $PSScriptRoot "$subject.codex"
    $expected = [IO.File]::ReadAllText((Join-Path $PSScriptRoot "$subject.expected")) -replace "`r", ''
    foreach ($arm in @('baseline', 'candidate')) {
        $kernel = if ($arm -eq 'baseline') { $Baseline } else { $Candidate }
        $stem = Join-Path $out "$subject-$arm"
        $free = (Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1MB
        "compile $subject $arm freeGiB=$free guests=1" | Add-Content (Join-Path $out 'runs.log')
        if ($free -le 1.5) { throw 'RAM below serial threshold.' }
        & pwsh -NoProfile -File build/compile.ps1 -Src $source -Out "$stem.cdx" -Log "$stem.log" -Kernel $kernel -TimeoutSec 30 *> "$stem.console"
        if ($LASTEXITCODE -ne 0) { Get-Content "$stem.log"; throw "$subject $arm compile failed." }
        $diagnostics = [IO.File]::ReadAllText("$stem.log")
        $warnings = @([regex]::Matches($diagnostics, '(?m)^.*warning CDX1074:.*$'))
        $wanted = if ($arm -eq 'candidate' -and $subject -eq 'prose-binary-continuation') { 3 } else { 0 }
        if ($warnings.Count -ne $wanted) { throw "$subject $arm expected $wanted warnings, found $($warnings.Count)." }
        if ($wanted -eq 3) {
            foreach ($pattern in @(
                ':10:4: warning CDX1074: Binary expression crosses prose 1 line\(s\) before the operator\.',
                ':16:6: warning CDX1074: Binary expression crosses prose 1 line\(s\) after the operator\.',
                ':29:4: warning CDX1074: Binary expression crosses prose 3 line\(s\) before the operator\.'
            )) {
                if ($diagnostics -notmatch $pattern) { throw "Missing warning location/text: $pattern" }
            }
        }
        $free = (Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1MB
        "run $subject $arm freeGiB=$free guests=1" | Add-Content (Join-Path $out 'runs.log')
        if ($free -le 1.5) { throw 'RAM below serial threshold.' }
        & pwsh -NoProfile -File build/test-run.ps1 -Kernel "$stem.cdx" -OutFile "$stem.out" *> "$stem.run.console"
        if ($LASTEXITCODE -ne 0) { throw "$subject $arm run failed." }
        if ([IO.File]::ReadAllText("$stem.out") -cne $expected) { throw "$subject $arm output changed." }
    }
    $baselineHash = (Get-FileHash (Join-Path $out "$subject-baseline.cdx")).Hash
    $candidateHash = (Get-FileHash (Join-Path $out "$subject-candidate.cdx")).Hash
    if ($baselineHash -ne $candidateHash) { throw "$subject emitted bytes changed." }
    "${subject}: warnings, locations, output and emitted bytes PASS"
}
'COMPILER-55 focused checks passed.'
