[CmdletBinding()]
param([string]$Exe = 'build-output/native-helper/ModBuilder.exe')
$ErrorActionPreference = 'Stop'
$exePath = (Resolve-Path -LiteralPath $Exe).Path
$probe = [Net.Sockets.TcpClient]::new()
try { $probe.Connect('127.0.0.1',8789); throw 'Port 8789 is occupied. Stop the existing helper before running the native fixture.' }
catch [Net.Sockets.SocketException] { if ($_.Exception.SocketErrorCode -ne 'ConnectionRefused') { throw } }
finally { $probe.Dispose() }
$scratch = Join-Path (Get-Location) 'build-output/native-helper/fixture'
[void](New-Item -ItemType Directory -Force -Path $scratch)
$savedLocal = $env:LOCALAPPDATA
$savedSteam = $env:MODBUILDER_TEST_STEAM
$process = $null
function Assert($pass, $name) { if (-not $pass) { throw $name }; Write-Host "PASS $name" }
function Wait-Ready($child, $log) {
    for ($i=0; $i -lt 80; $i++) {
        Start-Sleep -Milliseconds 100
        $stream = [IO.File]::Open($log, 'Open', 'Read', 'ReadWrite')
        $reader = [IO.StreamReader]::new($stream)
        try { $text = $reader.ReadToEnd() } finally { $reader.Dispose() }
        if ($text.StartsWith('{') -and $text.EndsWith("`n")) { return ($text.Trim() | ConvertFrom-Json) }
        if ($child.HasExited) { throw "Helper exited: $($child.ExitCode)" }
    }
    throw 'Native helper startup timeout'
}
try {
    $env:LOCALAPPDATA = Join-Path $scratch 'local'
    $env:MODBUILDER_TEST_STEAM = Join-Path $scratch 'steam'
    $library = Join-Path $scratch 'Library é'
    $game = Join-Path $library 'steamapps/common/Valheim'
    foreach ($dir in @($env:LOCALAPPDATA, "$env:MODBUILDER_TEST_STEAM/steamapps", "$game/valheim_Data/Managed", "$game/MonoBleedingEdge/EmbedRuntime")) { [void](New-Item -ItemType Directory -Force -Path $dir) }
    foreach ($file in @('valheim.exe','valheim_Data/Managed/assembly_valheim.dll','MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll')) { [IO.File]::WriteAllText((Join-Path $game $file), 'fixture remains unchanged') }
    [IO.File]::WriteAllText("$env:MODBUILDER_TEST_STEAM/steamapps/libraryfolders.vdf", ('"libraryfolders" { "1" { "path" "' + $library.Replace('\','\\') + '" } }'))
    $inventory = (& $exePath --probe | ConvertFrom-Json)
    Assert ($LASTEXITCODE -eq 0 -and $inventory.found -and $inventory.gamePath.Replace('\','/') -eq $game.Replace('\','/')) 'Steam secondary library and Unicode path discovery'
    $stdout = Join-Path $scratch 'stdout.txt'
    $process = Start-Process -FilePath $exePath -ArgumentList '--test' -PassThru -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError (Join-Path $scratch 'stderr.txt')
    $ready = Wait-Ready $process $stdout
    Assert ($null -ne $ready -and $ready.ready) 'Native listener starts'
    $base = 'http://127.0.0.1:8789'
    $headers = @{ 'X-Bridge-Token' = $ready.token; Origin = 'null' }
    $health = Invoke-RestMethod "$base/health"
    Assert (-not $health.tokenOk -and $null -eq $health.inventory) 'Anonymous health reveals no game path'
    $health = Invoke-RestMethod "$base/health" -Headers $headers
    Assert ($health.tokenOk -and $health.inventory.found -and -not $health.capabilities.build) 'Authenticated inventory and honest capability report'
    $denied = Invoke-WebRequest "$base/inventory" -SkipHttpErrorCheck
    Assert ($denied.StatusCode -eq 403) 'Inventory rejects absent token'
    $denied = Invoke-WebRequest "$base/inventory" -Headers @{ 'X-Bridge-Token'=$ready.token; Origin='https://unrelated.invalid' } -SkipHttpErrorCheck
    Assert ($denied.StatusCode -eq 403) 'Unexpected browser origin rejected'
    $preflight = Invoke-WebRequest "$base/target" -Method Options -Headers @{Origin='null'}
    Assert ($preflight.StatusCode -eq 200 -and $preflight.Headers['Access-Control-Allow-Origin'] -eq 'null') 'File-page preflight accepted'
    $unsupported = Invoke-WebRequest "$base/target" -Method Post -Body '{}' -ContentType 'application/json' -Headers $headers -SkipHttpErrorCheck
    Assert ($unsupported.StatusCode -eq 409) 'Unavailable mod operations refuse before any game write'
    for ($i=0; $i -lt 200; $i++) { $null = Invoke-RestMethod "$base/inventory" -Headers $headers }
    Assert (-not $process.HasExited) 'Repeated requests retain a working native heap'
    $previous = $process
    $restartLog = Join-Path $scratch 'restart.txt'
    $process = Start-Process -FilePath $exePath -ArgumentList '--test' -PassThru -WindowStyle Hidden -RedirectStandardOutput $restartLog -RedirectStandardError (Join-Path $scratch 'restart-error.txt')
    $next = Wait-Ready $process $restartLog
    Assert ($previous.WaitForExit(5000) -and $next.ready -and $next.token -ne $ready.token) 'Reopening replaces the running helper with a fresh pairing'
    $settings = Join-Path $env:LOCALAPPDATA 'Cobblestone/ModBuilder'
    [IO.File]::WriteAllText((Join-Path $settings 'keep.txt'),'unrelated')
    $locked = [IO.File]::Open((Join-Path $settings 'inventory.json'), 'Open', 'Read', 'None')
    try { $result = & $exePath --test --uninstall } finally { $locked.Dispose() }
    Assert ($result -match 'UNINSTALL INCOMPLETE' -and (Test-Path "$settings/owner.txt")) 'Locked inventory reports incomplete removal and preserves ownership for retry'
    $result = & $exePath --test --uninstall
    Assert ($LASTEXITCODE -eq 0 -and $result -match 'UNINSTALL COMPLETE') 'Native uninstall completes'
    Assert ($process.WaitForExit(5000)) 'Uninstaller stops the helper'
    Assert ((Test-Path "$settings/keep.txt") -and -not (Test-Path "$settings/owner.txt") -and -not (Test-Path "$settings/inventory.json")) 'Uninstaller preserves unknown files'
    Assert ([IO.File]::ReadAllText((Join-Path $game 'valheim.exe')) -eq 'fixture remains unchanged') 'Game files remain unchanged'
    [IO.File]::WriteAllText((Join-Path $settings 'owner.txt'),'another owner')
    [IO.File]::WriteAllText((Join-Path $settings 'inventory.json'),'preserve')
    $result = & $exePath --test --uninstall
    Assert ($result -match 'UNINSTALL REFUSED' -and [IO.File]::ReadAllText("$settings/inventory.json") -eq 'preserve') 'Ownership mismatch refuses deletion'
    Remove-Item -LiteralPath "$settings/owner.txt", "$settings/inventory.json", "$settings/keep.txt"
    Remove-Item -LiteralPath $settings
} finally {
    if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id }
    if ($previous -and -not $previous.HasExited) { Stop-Process -Id $previous.Id }
    $env:LOCALAPPDATA = $savedLocal
    $env:MODBUILDER_TEST_STEAM = $savedSteam
}
