[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$scratch = Join-Path $repo 'build-output/native-helper/launch fixture'
[void](New-Item -ItemType Directory -Force -Path $scratch)
$hostSource = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'WindowsHost.codex'))
$captureSource = @'
Chapter: Capture Browser Argument
  opening : [Console] Nothing =
   let h = mb-host 0
   in let command = mb-from-wide (mb-call (h.command) 0 0 0 0 0 0 0 0) 32768
   in let saved = mb-write h (mb-env h "MODBUILDER_TEST_CAPTURE_LOG") command
   in print-line-uni (if saved then "captured" else "capture failed")
'@
$launchSource = @'
Chapter: Native Browser Launch Probe
  opening : [Console] Nothing =
   let h = mb-host 0
   in let browser = mb-browser h
   in let target = mb-env h "MODBUILDER_TEST_CAPTURE_APP"
   in let url = mb-env h "MODBUILDER_TEST_CAPTURE_URL"
   in act
    print-line-uni (if browser /= "" & mb-exists h browser then "BROWSER FOUND" else "BROWSER MISSING")
    print-line-uni (show (mb-open-browser h target url))
    print-line-uni (show (mb-open-browser h target (url & "\"")))
   end
'@
foreach ($item in @(@{Name='Capture';Source=$captureSource},@{Name='Launch';Source=$launchSource})) {
    $source = Join-Path $scratch ($item.Name + '.codex')
    [IO.File]::WriteAllText($source, $hostSource + [Environment]::NewLine + $item.Source)
    $options = @()
    if ($item.Name -eq 'Launch') { $options += '--console' }
    & node (Join-Path $PSScriptRoot 'build-helper.mjs') $source (Join-Path $scratch ($item.Name + '.exe')) @options
    if ($LASTEXITCODE -ne 0) { throw "Native $($item.Name) build failed" }
}
$names = @('MODBUILDER_TEST_CAPTURE_LOG','MODBUILDER_TEST_CAPTURE_APP','MODBUILDER_TEST_CAPTURE_URL')
$saved = @{}
foreach($name in $names) { $saved[$name] = [Environment]::GetEnvironmentVariable($name) }
try {
    $env:MODBUILDER_TEST_CAPTURE_LOG = Join-Path $scratch ('argument-' + [guid]::NewGuid().ToString('N') + '.txt')
    $env:MODBUILDER_TEST_CAPTURE_APP = Join-Path $scratch 'Capture.exe'
    $env:MODBUILDER_TEST_CAPTURE_URL = 'file:///C:/Pairing%20Probe/workspace.html#bridge=fixture-token&bridgeUrl=http%3A%2F%2F127.0.0.1%3A8789'
    $output = & (Join-Path $scratch 'Launch.exe')
    if ($LASTEXITCODE -ne 0 -or $output[0] -ne 'BROWSER FOUND' -or [long]$output[1] -le 32 -or $output[2] -ne '0') { throw "Native launch probe failed: $output" }
    $command = ''
    for($i=0;$i -lt 100 -and -not $command;$i++) { Start-Sleep -Milliseconds 50; if(Test-Path -LiteralPath $env:MODBUILDER_TEST_CAPTURE_LOG) { try { $command=[IO.File]::ReadAllText($env:MODBUILDER_TEST_CAPTURE_LOG) } catch [IO.IOException] {} } }
    if (-not $command.Contains('"' + $env:MODBUILDER_TEST_CAPTURE_URL + '"')) { throw 'Windows launch lost or split the pairing URL' }
    Write-Host 'PASS default browser resolves and native ShellExecute preserves the complete quoted file URL, fragment and parameters'
    Write-Host 'PASS ambiguous quoted URL refuses without launching'
} finally {
    foreach($name in $names) { [Environment]::SetEnvironmentVariable($name,$saved[$name]) }
}
