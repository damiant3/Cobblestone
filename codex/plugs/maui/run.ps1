# Run MAUI plug: source -> IR-CCE -> plug CDX -> MAUI C#
# Serial I/O pipeline matching the HTML plug pattern.
# Produces a full buildable .NET MAUI project.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string]$Src,
    [string]$Out,
    [string]$ProjectDir,
    [switch]$Build
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '..' '..' '..' 'build' 'vm-config.ps1')

$Repo     = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..')).Path
$PlugDir  = (Resolve-Path $PSScriptRoot).Path
$PlugCdx  = Join-Path $PlugDir 'build-output\maui-plug.cdx'
$IrDir    = Join-Path $PlugDir 'build-output'
$IrFile   = Join-Path $IrDir 'last-run.ir'
$LogFile  = Join-Path $IrDir 'run.log'
$Template = Join-Path $PlugDir 'template'

if (-not $ProjectDir) { $ProjectDir = Join-Path $IrDir 'CodexApp' }
if (-not $Out)        { $Out = Join-Path $ProjectDir 'MainPage.xaml.cs' }

if (-not (Test-Path -PathType Leaf $PlugCdx)) {
    [Console]::Error.WriteLine("MISSING: $PlugCdx -- run plugs/maui/build.ps1 first")
    exit 2
}

# -- Phase 1: Codex source -> IR-CCE ----------------------------------
$compileScript = Join-Path $Repo 'build' 'compile.ps1'
# text-plug: this plug resolves a Codex call by its NAME, so the inline passes
# must not substitute a body and delete the call. See text-plug-ir-pipeline
# in codex/compiler/IR/Passes.codex.
& pwsh -NoProfile -File $compileScript -Src $Src -Out $IrFile -Log $LogFile -IrCce -Passes 'text-plug'
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $IrFile)) {
    [Console]::Error.WriteLine("FAIL: IR compile failed; see $LogFile")
    exit 4
}
Write-Host "[maui-run] IR: $((Get-Item $IrFile).Length) bytes (CCE)"

# -- Phase 2: Build VM input (mode header + IR + null terminator) ------
$irBytes = [System.IO.File]::ReadAllBytes($IrFile)

$inputFile = [System.IO.Path]::GetTempFileName()
$hdrList = [System.Collections.Generic.List[byte]]::new()
foreach ($ch in "IR-CCE".ToCharArray()) {
    $u = [int]$ch
    if ($u -lt 256) { $hdrList.Add([byte]$script:UnicodeToCce[$u]) }
}
$hdrList.Add([byte]1)  # CCE newline
$modeHeader = $hdrList.ToArray()
$combined = New-Object byte[] ($modeHeader.Length + $irBytes.Length + 1)
[Buffer]::BlockCopy($modeHeader, 0, $combined, 0, $modeHeader.Length)
[Buffer]::BlockCopy($irBytes, 0, $combined, $modeHeader.Length, $irBytes.Length)
$combined[$combined.Length - 1] = 0  # null terminator for read-file
[System.IO.File]::WriteAllBytes($inputFile, $combined)

# -- Phase 3: Run plug CDX --------------------------------------------
$outFile = [System.IO.Path]::GetTempFileName()
$errFile = [System.IO.Path]::GetTempFileName()
$vmBin = $script:CodexVmBin
$proc = Start-Process -FilePath $vmBin -ArgumentList @('-kernel',$PlugCdx,'-input',$inputFile,'-output',$outFile,'-mem', '3072','-headless') -PassThru -WindowStyle Hidden -RedirectStandardError $errFile
$proc.WaitForExit(300000)
if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force; [Console]::Error.WriteLine("FAIL: timeout"); exit 5 }

if (-not (Test-Path $outFile) -or (Get-Item $outFile).Length -eq 0) {
    $err = if (Test-Path $errFile) { Get-Content $errFile -Raw } else { "" }
    [Console]::Error.WriteLine("FAIL: no output from plug")
    if ($err -match 'EXC') { [Console]::Error.WriteLine($err.Substring(0, [Math]::Min(500, $err.Length))) }
    exit 6
}

$raw = [System.IO.File]::ReadAllText($outFile)
$lines = $raw -split "`n" | Where-Object { $_ -notmatch '^(HEAP|WD|STACK|PM):' -and $_.Trim().Length -gt 0 }
$csOutput = ($lines -join "`n")
$csOutput = $csOutput -replace '^[\x00-\x1f]+', ''

# -- Phase 4: Copy template and write generated C# --------------------
if (-not (Test-Path (Join-Path $ProjectDir 'CodexApp.csproj'))) {
    New-Item -ItemType Directory -Force -Path $ProjectDir | Out-Null
    Get-ChildItem $Template -Recurse | ForEach-Object {
        $rel = $_.FullName.Substring($Template.Length + 1)
        $dest = Join-Path $ProjectDir $rel
        if ($_.PSIsContainer) { New-Item -ItemType Directory -Force -Path $dest | Out-Null }
        else { New-Item -ItemType Directory -Force -Path (Split-Path $dest) | Out-Null; Copy-Item $_.FullName $dest -Force }
    }
    Write-Host "[maui-plug] Template copied to $ProjectDir"
}
[System.IO.File]::WriteAllText($Out, $csOutput, [System.Text.UTF8Encoding]::new($false))
Write-Host "[maui-plug] OK: $Out ($($csOutput.Length) chars)"

# -- Phase 5: Optional dotnet build -----------------------------------
if ($Build) {
    Write-Host "[maui-plug] Building MAUI project..."
    $buildResult = & dotnet build (Join-Path $ProjectDir 'CodexApp.csproj') -f net8.0-windows10.0.19041.0 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Host "[maui-plug] BUILD OK"
    } else {
        Write-Host "[maui-plug] BUILD FAILED:"
        $buildResult | Select-Object -Last 20 | Write-Host
        exit 9
    }
}

Write-Host "[maui-plug] Project ready at: $ProjectDir"
Remove-Item $inputFile,$outFile,$errFile -Force -ErrorAction SilentlyContinue
