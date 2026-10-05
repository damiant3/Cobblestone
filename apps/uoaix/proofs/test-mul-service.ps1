[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ClientRoot,
    [ValidateRange(1024,65535)][int]$Port = 2595
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $ClientRoot).Path
$names = @('MAP0.MUL','STAIDX0.MUL','STATICS0.MUL','TILEDATA.MUL','MULTI.IDX','MULTI.MUL')
$http = [Net.Http.HttpClient]::new()
$http.Timeout = [TimeSpan]::FromSeconds(10)
$checks = 0
function Request([string]$Path, [int]$Status) {
    $reply = $http.GetAsync("http://127.0.0.1:$Port$Path").GetAwaiter().GetResult()
    try {
        if ([int]$reply.StatusCode -ne $Status) { throw "HTTP status mismatch: $Path" }
        $bytes = $reply.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
        if ($reply.Content.Headers.ContentLength -ne $bytes.Length) { throw "Short body: $Path" }
        $script:checks++
        return ,$bytes
    } finally { $reply.Dispose() }
}
function Compare-Range([int]$Id, [long]$Offset, [int]$Count) {
    $actual = Request "/mul/$Id/$Offset/$Count" 200
    $file = [IO.File]::OpenRead((Join-Path $root $names[$Id]))
    try {
        $expected = [byte[]]::new($Count)
        [void]$file.Seek($Offset,[IO.SeekOrigin]::Begin)
        $file.ReadExactly($expected)
        if ($actual.Length -ne $Count) { throw 'Range length mismatch' }
        for ($i=0; $i -lt $Count; $i++) {
            if ($actual[$i] -ne $expected[$i]) { throw "Range mismatch: file $Id offset $Offset byte $i" }
        }
    } finally { $file.Dispose() }
}
try {
    for ($id=0; $id -lt $names.Length; $id++) {
        $size = (Get-Item -LiteralPath (Join-Path $root $names[$id])).Length
        $reply = Request "/size/$id" 200
        if ($reply.Length -ne 8 -or [BitConverter]::ToInt64($reply,0) -ne $size) { throw "Size mismatch: file $id" }
        Compare-Range $id 0 32
        Compare-Range $id ($size-32) 32
        Compare-Range $id $size 0
        $null = Request "/mul/$id/$size/1" 416
    }
    Compare-Range 0 0 65536
    $null = Request '/mul/0/0/65537' 416
    $null = Request '/mul/0/9999999999/1' 416
    $null = Request '/mul/6/0/1' 400
    $null = Request '/mul/0/-1/1' 400
    $null = Request '/mul/0/0/100000' 400
    $null = Request '/size/6' 400
    $null = Request '/LOGIN.CFG' 400
    for ($i=0; $i -lt 20; $i++) {
        $x = 1400 + $i*7
        $y = 1600 + $i*11
        $block = [long]([Math]::Floor($x/8)*512+[Math]::Floor($y/8))
        Compare-Range 0 ($block*196) 196
        Compare-Range 1 ($block*12) 12
    }
    if ($checks -ne 78) { throw "Incomplete MUL service proof: $checks checks, expected 78" }
    Write-Output "PASS MUL service: $checks exact-size/status checks; six original files; twenty Britain map/index ranges; no data written"
} finally { $http.Dispose() }
