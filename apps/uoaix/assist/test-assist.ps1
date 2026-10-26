[CmdletBinding()]
param()
# The Assist's suites (docs/Designs/Active/Apps/AssistTesting.md): compiles the sources assist.ps1 compiles, read from
# its Add-Type line so the two lists cannot drift, with tests/*.cs; runs every case; exits 1 on any failure or on a
# compile error. Run it in a fresh pwsh: Add-Type cannot load the assembly twice in one process.
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$line=Get-Content (Join-Path $PSScriptRoot 'assist.ps1')|Where-Object{$_ -match '^Add-Type -Path \(@\('}
if(@($line).Count -ne 1){throw "assist.ps1 holds $(@($line).Count) Add-Type -Path lines, not 1"}
$files=@([regex]::Matches($line.Substring(0,$line.IndexOf(')|ForEach-Object')),"'([^']+\.cs)'")|ForEach-Object{Join-Path $PSScriptRoot $_.Groups[1].Value})
$refs=($line -split '-ReferencedAssemblies ')[1].Trim() -split ','
$tests=@(Get-ChildItem (Join-Path $PSScriptRoot 'tests') -Filter *.cs|ForEach-Object FullName)
if($files.Count -eq 0 -or $tests.Count -eq 0){throw "nothing to compile: $($files.Count) sources, $($tests.Count) suites"}
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
# The fake shard reads the wire tables from the UOAIX sources (WireTables.Load).
$env:UOAIX_DIR=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
try{Add-Type -Path ($files+$tests) -ReferencedAssemblies $refs}
catch{Write-Output "COMPILE FAILED: $($_.Exception.Message)";exit 1}
$failed=[Uoaix.Assist.Tests.AllTests]::Run()
exit ([int]($failed -gt 0))
