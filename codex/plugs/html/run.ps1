# Run HTML plug: source -> IR-CCE -> CCE-to-UTF8 -> plug CDX -> HTML
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$Src, [Parameter(Mandatory=$true)][string]$Out)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..' '..' '..' 'build' 'vm-config.ps1')
$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..')).Path
$PlugCdx = Join-Path $PSScriptRoot 'build-output\html-plug.cdx'
$IrFile = Join-Path $PSScriptRoot 'build-output\last-run.ir'
$LogFile = Join-Path $PSScriptRoot 'build-output\run.log'
if (-not (Test-Path $PlugCdx)) { [Console]::Error.WriteLine("MISSING: $PlugCdx"); exit 2 }

# Phase 1: source -> IR-CCE
& pwsh -NoProfile -File (Join-Path $Repo 'build\compile.ps1') -Src $Src -Out $IrFile -Log $LogFile -IrCce
if ($LASTEXITCODE -ne 0) { [Console]::Error.WriteLine("FAIL: IR; see $LogFile"); exit 3 }
$irCceBytes = [System.IO.File]::ReadAllBytes($IrFile)
Write-Host "[html-run] IR: $($irCceBytes.Length) bytes (CCE)"

# Phase 1b: CCE -> UTF-8 (avoids byte-4 EOT collision with CCE digit '1')
$sb = [System.Text.StringBuilder]::new($irCceBytes.Length)
foreach ($b in $irCceBytes) {
    if ($b -lt $script:CceToUnicode.Length) { [void]$sb.Append([char]$script:CceToUnicode[$b]) }
}
$utf8Ir = [System.Text.Encoding]::UTF8.GetBytes($sb.ToString())

# Phase 2: Build input: mode header + UTF-8 IR + EOT
$inputFile = [System.IO.Path]::GetTempFileName()
$header = [System.Text.Encoding]::UTF8.GetBytes("IR-CCE`n")
$combined = New-Object byte[] ($header.Length + $utf8Ir.Length + 1)
[Array]::Copy($header, 0, $combined, 0, $header.Length)
[Array]::Copy($utf8Ir, 0, $combined, $header.Length, $utf8Ir.Length)
$combined[$combined.Length - 1] = 4
[System.IO.File]::WriteAllBytes($inputFile, $combined)

# Phase 3: Run plug CDX with file I/O
$vmBin = Join-Path $Repo 'tools\codex-vm.exe'
$outFile = [System.IO.Path]::GetTempFileName()
$errFile = [System.IO.Path]::GetTempFileName()
$proc = Start-Process -FilePath $vmBin -ArgumentList @('-kernel',$PlugCdx,'-input',$inputFile,'-output',$outFile,'-mem','4096','-headless') -PassThru -WindowStyle Hidden -RedirectStandardError $errFile
$proc.WaitForExit(300000)
if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force; [Console]::Error.WriteLine("FAIL: timeout"); exit 4 }

if (-not (Test-Path $outFile) -or (Get-Item $outFile).Length -eq 0) {
    $err = if (Test-Path $errFile) { Get-Content $errFile -Raw } else { "" }
    [Console]::Error.WriteLine("FAIL: no output")
    if ($err -match 'EXC') { [Console]::Error.WriteLine($err.Substring(0, [Math]::Min(300, $err.Length))) }
    exit 5
}
$raw = [System.IO.File]::ReadAllText($outFile)
$lines = $raw -split "`n" | Where-Object { $_ -notmatch '^(HEAP|WD|STACK|PM):' -and $_.Trim().Length -gt 0 }
$html = ($lines -join "`n")
[System.IO.File]::WriteAllText($Out, $html, [System.Text.UTF8Encoding]::new($false))
Write-Host "[html-plug] OK: $Out ($($html.Length) chars)"
Remove-Item $inputFile,$outFile,$errFile -Force -ErrorAction SilentlyContinue