param(
    [Parameter(Mandatory)][string]$Kernel,
    [Parameter(Mandatory)][string]$BaselineKernel,
    [Parameter(Mandatory)][string]$PlugCdx,
    [Parameter(Mandatory)][string]$BaselinePlugCdx,
    [Parameter(Mandatory)][string]$WorkDir
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot/../../..").Path
. "$repo/build/vm-config.ps1"
$WorkDir = [IO.Path]::GetFullPath($WorkDir)
if (Test-Path $WorkDir) { throw 'WorkDir must be new.' }
New-Item -ItemType Directory $WorkDir | Out-Null
$Kernel = (Resolve-Path $Kernel).Path
$BaselineKernel = (Resolve-Path $BaselineKernel).Path
$PlugCdx = (Resolve-Path $PlugCdx).Path
$BaselinePlugCdx = (Resolve-Path $BaselinePlugCdx).Path
Get-FileHash $Kernel, $BaselineKernel, $PlugCdx, $BaselinePlugCdx |
    Select-Object Path, Hash | ConvertTo-Json | Set-Content "$WorkDir/provenance.json"
function Admit {
    $free = (Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1MB
    Write-Host "RAM: $free GiB; one guest"
    if ($free -le 1.5) { throw 'RAM admission refused.' }
}
function Compile-Ir([string]$name, [string]$src, [string]$compiler) {
    Admit
    & pwsh -NoProfile -File "$repo/build/compile.ps1" -Src $src -Out "$WorkDir/$name.ir" -Log "$WorkDir/$name.compile.log" -Kernel $compiler -IrCce -Passes text-plug *> "$WorkDir/$name.compile.console"
    if ($LASTEXITCODE -ne 0) { throw "IR compile failed: $name" }
}
function Emit-Wat([string]$name, [string]$irName, [string]$plug) {
    $header = ConvertTo-CceBytes "IR-CCE`n"
    $ir = [IO.File]::ReadAllBytes("$WorkDir/$irName.ir")
    $inputBytes = [byte[]]::new($header.Length + $ir.Length + 1)
    [Array]::Copy($header, 0, $inputBytes, 0, $header.Length)
    [Array]::Copy($ir, 0, $inputBytes, $header.Length, $ir.Length)
    [IO.File]::WriteAllBytes("$WorkDir/$name.input", $inputBytes)
    Admit
    $ok = Invoke-PlugVmFileSerial -Kernel $plug -InputFile "$WorkDir/$name.input" -OutputFile "$WorkDir/$name.raw" -StderrFile "$WorkDir/$name.emit.log" -TimeoutSec 60
    if (-not $ok) { throw "Plug timed out: $name" }
    $raw = [IO.File]::ReadAllText("$WorkDir/$name.raw")
    $diag = [IO.File]::ReadAllText("$WorkDir/$name.emit.log")
    if (($raw + $diag) -match 'DROPPED|!EXC|OUT OF MEMORY') { throw "Plug failed: $name" }
    $wat = (($raw -replace '^\x01', '') -split "`n" | Where-Object { $_ -notmatch '^(HEAP|WD|STACK|PM):' }) -join "`n"
    if (-not $wat.TrimStart().StartsWith('(module')) { throw "Missing module: $name" }
    [IO.File]::WriteAllText("$WorkDir/$name.wat", $wat)
}
function Assemble([string]$name, [string]$refusal = '') {
    & wat2wasm --enable-tail-call "$WorkDir/$name.wat" -o "$WorkDir/$name.wasm" *> "$WorkDir/$name.assemble.log"
    $rc = $LASTEXITCODE
    if ($refusal) {
        if ($rc -eq 0 -or -not ([IO.File]::ReadAllText("$WorkDir/$name.assemble.log").Contains($refusal))) { throw "Expected refusal ${refusal}: $name" }
    } elseif ($rc -ne 0) { throw "Assembly failed: $name" }
}
function Exports([string]$name) {
    & wasm-objdump -x "$WorkDir/$name.wasm" > "$WorkDir/$name.sections" 2> "$WorkDir/$name.sections.err"
    if ($LASTEXITCODE -ne 0) { throw "Cannot read binary export section: $name" }
    return @([regex]::Matches([IO.File]::ReadAllText("$WorkDir/$name.sections"), '(?m)^ - func\[\d+\].* -> "([^"]+)"\r?$') | ForEach-Object { $_.Groups[1].Value } | Sort-Object)
}
function Require-Exports([string]$name, [string[]]$expected) {
    $actual = Exports $name
    if (($actual -join '|') -cne (($expected | Sort-Object) -join '|')) { throw "Wrong exports ${name}: $($actual -join ',')" }
    Write-Host "$name exports PASS: $($actual -join ',')"
}
function Invoke-Value([string]$name, [string]$export, [string[]]$arguments, [string]$expected) {
    & wasmtime run --invoke $export "$WorkDir/$name.wasm" @arguments > "$WorkDir/$name.$export.out" 2> "$WorkDir/$name.$export.err"
    if ($LASTEXITCODE -ne 0 -or [IO.File]::ReadAllText("$WorkDir/$name.$export.out").Trim() -cne $expected) { throw "Wrong runtime value: $name/$export" }
    Write-Host "$name/$export PASS: $expected"
}
$fixture = "$repo/codex/test/wasm-exports-root.codex"
Compile-Ir 'declared' $fixture $Kernel
Admit
& pwsh -NoProfile -File "$repo/build/compile.ps1" -Src $fixture -Out "$WorkDir/declared-uni.unused" -Log "$WorkDir/declared-uni.log" -Kernel $Kernel -IrUni -Passes text-plug *> "$WorkDir/declared-uni.console"
if ($LASTEXITCODE -ne 4) { throw 'Unexpected IR-UNI capture convention.' }
$uniLog = [IO.File]::ReadAllText("$WorkDir/declared-uni.log").Replace("`r", '')
$uni = [regex]::Match($uniLog, '(?s)IR-BEGIN\n(.*?)\nIR-END')
$cceDecoded = ConvertFrom-CceBytes ([IO.File]::ReadAllBytes("$WorkDir/declared.ir"))
if (-not $uni.Success -or $uni.Groups[1].Value.Trim() -cne $cceDecoded.Trim()) { throw 'IR-UNI and IR-CCE declarations differ.' }
Write-Host 'IR-UNI/IR-CCE declared roots PASS'
Emit-Wat 'declared' 'declared' $PlugCdx
Assemble 'declared'
Require-Exports 'declared' @('__heap_reset', '_start', 'disk_reserve', 'api_add', 'api_twice')
Invoke-Value 'declared' 'api_add' @('20', '22') '42'
Invoke-Value 'declared' 'api_twice' @('21') '42'
$declaredWat = [IO.File]::ReadAllText("$WorkDir/declared.wat")
if ($declaredWat.Contains('(func $api_absent ')) { throw 'Unreferenced negative control survived DCE.' }
Compile-Ir 'old-kernel' $fixture $BaselineKernel
Emit-Wat 'old-kernel' 'old-kernel' $PlugCdx
Assemble 'old-kernel'
Require-Exports 'old-kernel' @('__heap_reset', '_start', 'disk_reserve')
$basic = @'
Chapter: ExportsSelection
  cites Foreword chapter Console
Section: Selection
DECLARATION
  species-count : Integer = 8
  opening : [Console] Nothing = print-line-uni (show species-count)
'@
foreach ($mode in @('absent', 'empty', 'missing', 'nonliteral')) {
    $declaration = switch ($mode) {
        'absent' { '' }
        'empty' { '  wasm-exports : Text = ""' }
        'missing' { '  wasm-exports : Text = "|missing-export|"' }
        'nonliteral' { "  wasm-exports : Integer -> Text`n  wasm-exports (x) = show x" }
    }
    [IO.File]::WriteAllText("$WorkDir/$mode.codex", $basic.Replace('DECLARATION', $declaration))
    Compile-Ir $mode "$WorkDir/$mode.codex" $Kernel
    Emit-Wat $mode $mode $PlugCdx
    if ($mode -eq 'missing') { Assemble $mode 'missing_export' }
    elseif ($mode -eq 'nonliteral') { Assemble $mode 'wasm-exports-requires-text-literal' }
    else {
        Assemble $mode
        $wanted = @('__heap_reset', '_start', 'disk_reserve')
        if ($mode -eq 'absent') { $wanted += 'species_count' }
        Require-Exports $mode $wanted
    }
}
$fish = "$repo/apps/fishtank/FishTankWasm.codex"
Compile-Ir 'fish-old-kernel' $fish $BaselineKernel
Compile-Ir 'fish-new-kernel' $fish $Kernel
if ((Get-FileHash "$WorkDir/fish-old-kernel.ir").Hash -ne (Get-FileHash "$WorkDir/fish-new-kernel.ir").Hash) { throw 'Undeclared fishtank IR changed.' }
Emit-Wat 'fish-old' 'fish-old-kernel' $BaselinePlugCdx
Emit-Wat 'fish-new' 'fish-new-kernel' $PlugCdx
Assemble 'fish-old'
Assemble 'fish-new'
$oldExports = Exports 'fish-old'
if ($oldExports.Count -le 3) { throw 'Fishtank does not exercise fallback exports.' }
Require-Exports 'fish-new' $oldExports
Invoke-Value 'fish-old' 'species_count' @() '8'
Invoke-Value 'fish-new' 'species_count' @() '8'
Write-Host 'PASS declared exports, DCE control, empty override, invalid declarations and fishtank fallback'
Set-Content "$WorkDir/result.txt" 'PASS'
