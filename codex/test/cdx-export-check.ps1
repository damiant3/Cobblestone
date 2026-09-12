param(
    [Parameter(Mandatory)][string]$Kernel,
    [Parameter(Mandatory)][string]$WorkDir,
    [switch]$ExpectUnfixed,
    [string]$ReferenceKernel = ''
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot/../..").Path
$Kernel = (Resolve-Path $Kernel).Path
if ($ReferenceKernel) { $ReferenceKernel = (Resolve-Path $ReferenceKernel).Path }
$WorkDir = [IO.Path]::GetFullPath($WorkDir)
if (Test-Path $WorkDir) { throw 'WorkDir must be new.' }
New-Item -ItemType Directory $WorkDir | Out-Null
$fixture = [IO.File]::ReadAllText("$PSScriptRoot/cdx-export-retention.codex")
$declaration = '  wasm-exports : Text = "|drive|export-only|"'
if (-not $fixture.Contains($declaration)) { throw 'Fixture declaration missing.' }
@{ kernel=$Kernel; sha256=(Get-FileHash $Kernel).Hash; expectUnfixed=[bool]$ExpectUnfixed;
    referenceKernel=$ReferenceKernel; referenceSha256=$(if ($ReferenceKernel) { (Get-FileHash $ReferenceKernel).Hash } else { '' });
    fixtureSha256=(Get-FileHash "$PSScriptRoot/cdx-export-retention.codex").Hash } |
    ConvertTo-Json | Set-Content "$WorkDir/provenance.json"
function Admit {
    $free = (Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1MB
    Write-Host "RAM $free GiB, one guest"
    if ($free -le 1.5) { throw 'RAM admission refused.' }
}
function Compile([string]$name, [string]$src, [string]$compiler, [string]$pipeline, [switch]$Ir) {
    Admit
    $compileArgs = @('-NoProfile', '-File', "$repo/build/compile.ps1", '-Src', $src,
        '-Out', "$WorkDir/$name.$(if ($Ir) { 'ir' } else { 'cdx' })", '-Log', "$WorkDir/$name.log", '-Kernel', $compiler)
    if ($pipeline -eq 'text-plug') { $compileArgs += @('-Passes', 'text-plug') }
    if ($Ir) { $compileArgs += '-IrCce' }
    & pwsh @compileArgs *> "$WorkDir/$name.console"
    if ($LASTEXITCODE -ne 0) { throw "Compile failed: $name" }
}
function Symbols([string]$name) {
    $map = "$WorkDir/$name.map"
    if (-not (Test-Path $map)) { throw "Missing symbol map: $name" }
    $binary = [IO.File]::ReadAllBytes("$WorkDir/$name.cdx")
    $names = @{}
    foreach ($line in (Get-Content $map)) {
        if ($line -match '^0x([0-9a-fA-F]+)\s+(\d+)\s+(.+)$') {
            $address = [Convert]::ToInt64($matches[1], 16)
            $size = [long]$matches[2]
            $symbol = $matches[3]
            # CDX code begins at virtual 0x100000 after the 224-byte header.
            $offset = $address - 0x100000 + 224
            if ($symbol -in @('drive', 'export-only', 'dead-code')) {
                if ($size -le 0 -or $offset -lt 224 -or $offset + $size -gt $binary.Length) { throw "Invalid retained symbol extent: $symbol" }
            }
            $names[$symbol] = $true
        }
    }
    if (-not $names.ContainsKey('opening')) { throw 'Map does not name opening.' }
    return $names
}
$results = @()
foreach ($pipeline in @('default', 'text-plug')) {
    foreach ($declared in @($true, $false)) {
        $name = "$pipeline-$(if ($declared) { 'declared' } else { 'undeclared' })"
        $src = "$WorkDir/$name.codex"
        [IO.File]::WriteAllText($src, $(if ($declared) { $fixture } else { $fixture.Replace($declaration, '') }))
        Compile $name $src $Kernel $pipeline
        $symbols = Symbols $name
        $wantDrive = $pipeline -eq 'text-plug' -or ($declared -and -not $ExpectUnfixed)
        $wantExport = $declared -and -not $ExpectUnfixed
        foreach ($entry in @(@('drive', $wantDrive), @('export-only', $wantExport), @('dead-code', $false))) {
            if ($symbols.ContainsKey($entry[0]) -ne $entry[1]) { throw "Wrong symbol retention: $name/$($entry[0])" }
        }
        Admit
        & pwsh -NoProfile -File "$repo/build/test-run.ps1" -Kernel "$WorkDir/$name.cdx" -OutFile "$WorkDir/$name.out" *> "$WorkDir/$name.run.console"
        if ($LASTEXITCODE -ne 0) { throw "Run failed: $name" }
        if ([IO.File]::ReadAllText("$WorkDir/$name.out").Replace("`r", '') -cne [IO.File]::ReadAllText("$PSScriptRoot/cdx-export-retention.expected").Replace("`r", '')) { throw "Wrong output: $name" }
        if ($ReferenceKernel) {
            Compile "$name-target-ir" $src $Kernel $pipeline -Ir
            Compile "$name-reference-ir" $src $ReferenceKernel $pipeline -Ir
            if ((Get-FileHash "$WorkDir/$name-target-ir.ir").Hash -ne (Get-FileHash "$WorkDir/$name-reference-ir.ir").Hash) { throw "IR contract changed: $name" }
        }
        $row = [pscustomobject]@{ pipeline=$pipeline; declared=$declared; drive=$symbols.ContainsKey('drive'); exportOnly=$symbols.ContainsKey('export-only'); dead=$symbols.ContainsKey('dead-code'); output=42 }
        $results += $row
        Write-Host "$name PASS drive=$($row.drive) export-only=$($row.exportOnly) dead=$($row.dead) output=42"
    }
}
$results | ConvertTo-Json | Set-Content "$WorkDir/results.json"
Set-Content "$WorkDir/result.txt" 'PASS'
Write-Host 'CDX export retention controls PASS'
