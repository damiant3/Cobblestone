[CmdletBinding()]
param(
    [string]$OutDir='',
    # The instrument's own control (coverage/test-cover.ps1) measures its fixture in place of the Assist.
    [string[]]$Sources=@(),
    [string[]]$Tests=@(),
    [string]$Entry='Uoaix.Assist.Tests.AllTests'
)
# The Assist's coverage (docs/Designs/Active/Apps/AssistTesting.md): instruments a copy of the sources assist.ps1
# compiles (coverage/Instrumenter.cs), runs every suite in tests/ over it, prints each file's line and branch coverage,
# and writes every unhit probe to coverage.txt in OutDir. Exits 1 when a case fails or the copy does not compile. Run it
# in a fresh pwsh: Add-Type cannot load an assembly twice in one process.
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
if(-not $OutDir){$OutDir=Join-Path ([IO.Path]::GetTempPath()) "assist-cover-$PID"}
$line=Get-Content (Join-Path $PSScriptRoot 'assist.ps1')|Where-Object{$_ -match '^Add-Type -Path \(@\('}
if(@($line).Count -ne 1){throw "assist.ps1 holds $(@($line).Count) Add-Type -Path lines, not 1"}
$files=@([regex]::Matches($line.Substring(0,$line.IndexOf(')|ForEach-Object')),"'([^']+\.cs)'")|ForEach-Object{Join-Path $PSScriptRoot $_.Groups[1].Value})
$refs=($line -split '-ReferencedAssemblies ')[1].Trim() -split ','
$suiteFiles=@(Get-ChildItem (Join-Path $PSScriptRoot 'tests') -Filter *.cs|ForEach-Object FullName)
if($Sources.Count -gt 0){$files=@($Sources|ForEach-Object{[IO.Path]::GetFullPath($_)})}
if($Tests.Count -gt 0){$suiteFiles=@($Tests|ForEach-Object{[IO.Path]::GetFullPath($_)})}
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
$roslyn=@('Microsoft.CodeAnalysis.dll','Microsoft.CodeAnalysis.CSharp.dll')|ForEach-Object{Join-Path $PSHOME $_}
Add-Type -Path (Join-Path $PSScriptRoot 'coverage/Instrumenter.cs') -ReferencedAssemblies ($roslyn+@('System.Runtime','System.Collections','System.Collections.Immutable','System.Linq','System.IO.FileSystem','System.Text.Encoding.Extensions')) -IgnoreWarnings -WarningAction SilentlyContinue
# The semantic model needs every assembly the sources can name: the runtime's own and PowerShell's (WinForms ships there).
$platform=@(([AppContext]::GetData('TRUSTED_PLATFORM_ASSEMBLIES')) -split [IO.Path]::PathSeparator|Where-Object{
    $n=[IO.Path]::GetFileNameWithoutExtension($_); $n -like 'System.*' -or $n -in @('netstandard','mscorlib','Microsoft.Win32.Primitives','Microsoft.Win32.Registry','Accessibility')})
$errors=[Uoaix.Assist.Coverage.Instrumenter]::Run([string[]]$files,[string[]]$platform,$OutDir)
if($errors.Count -gt 0){Write-Output "SOURCES DO NOT COMPILE FOR THE SEMANTIC MODEL ($($errors.Count) errors):";$errors|Select-Object -First 20|ForEach-Object{Write-Output "  $_"};exit 1}
$probes=@(Get-Content (Join-Path $OutDir 'probes.tsv')|ForEach-Object{$f=$_ -split "`t";[pscustomobject]@{Id=[int]$f[0];File=$f[1];Line=[int]$f[2];Kind=$f[3]}})
$copies=@($files|ForEach-Object{Join-Path $OutDir (Split-Path $_ -Leaf)})
try{Add-Type -Path ($copies+@((Join-Path $PSScriptRoot 'coverage/Cov.cs'))+$suiteFiles) -ReferencedAssemblies $refs}
catch{Write-Output "INSTRUMENTED COPY DOES NOT COMPILE: $($_.Exception.Message)";exit 1}
[Uoaix.Assist.Coverage.Cov]::Size($probes.Count)
$env:UOAIX_DIR=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
# The instrumented copy runs 5 to 8 times slower than the plain one (tests/Check.cs, HangMs).
$env:ASSIST_HANG_MS='60000'
$failed=([type]$Entry)::Run()
$hit=@{};foreach($p in $probes){$hit[$p.Id]=[Uoaix.Assist.Coverage.Cov]::Hits($p.Id)}
$report=New-Object Collections.Generic.List[string]
$rows=foreach($g in ($probes|Group-Object File|Sort-Object Name)){
    $lines=@($g.Group|Where-Object{$_.Kind -like 'line*'});$arms=@($g.Group|Where-Object{$_.Kind -like 'arm*' -or $_.Kind -like 'cond*'});$unm=@($g.Group|Where-Object{$_.Kind -like 'unmeasured*'})
    $lh=@($lines|Where-Object{$hit[$_.Id] -gt 0}).Count;$ah=@($arms|Where-Object{$hit[$_.Id] -gt 0}).Count
    foreach($p in ($g.Group|Where-Object{$_.Kind -notlike 'unmeasured*' -and $hit[$_.Id] -eq 0}|Sort-Object Line)){$report.Add("$($p.File):$($p.Line) $($p.Kind)")}
    foreach($p in $unm){$report.Add("$($p.File):$($p.Line) $($p.Kind)")}
    [pscustomobject]@{File=$g.Name;Lines="$lh/$($lines.Count)";LinePct=if($lines.Count){[math]::Round(100*$lh/$lines.Count,1)}else{100};Branches="$ah/$($arms.Count)";BranchPct=if($arms.Count){[math]::Round(100*$ah/$arms.Count,1)}else{100};Unmeasured=$unm.Count}
}
$rows|Format-Table -AutoSize|Out-String -Width 200|Write-Output
$lt=@($probes|Where-Object{$_.Kind -like 'line*'});$at=@($probes|Where-Object{$_.Kind -like 'arm*' -or $_.Kind -like 'cond*'})
Write-Output ("total: lines {0}/{1}, branches {2}/{3}, unmeasured {4}" -f @($lt|Where-Object{$hit[$_.Id] -gt 0}).Count,$lt.Count,@($at|Where-Object{$hit[$_.Id] -gt 0}).Count,$at.Count,@($probes|Where-Object{$_.Kind -like 'unmeasured*'}).Count)
$report|Set-Content (Join-Path $OutDir 'coverage.txt')
Write-Output "unhit and unmeasured probes: $(Join-Path $OutDir 'coverage.txt')"
exit ([int]($failed -gt 0))
