[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ClientRoot,
    [string]$ReferenceRoot='D:/Projects/uo-reference',
    [string]$OutDir='build-output/uoaix/decoration-host'
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$output=[IO.Path]::GetFullPath($OutDir)
[void][IO.Directory]::CreateDirectory($output)
$import=Join-Path $repo 'apps/uoaix/import-decoration.ps1'
$fixture=Join-Path $output ('fixture-'+[guid]::NewGuid().ToString('N'))
foreach($relative in @('ServUO/Scripts/Items/Functional/Doors.cs','ServUO/Scripts/Items/Functional/SecretDoors.cs','ServUO/Server/Item.cs')) {
    $dest=Join-Path $fixture $relative
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($dest))
    Copy-Item -LiteralPath (Join-Path $ReferenceRoot $relative) -Destination $dest
}
foreach($relative in @('ServUO/Data/Decoration/Old/Britannia','ServUO/Data/Decoration/Britannia','UOX3/data/js/jsdata/worldtemplates')) {
    [void][IO.Directory]::CreateDirectory((Join-Path $fixture $relative))
}
$old=@'
# sign
Static 0x0B98 (Name=Test Shop)
1421 1698 0

# door
MetalDoor 0x0675 (Facing=WestCW)
1422 1698 0
'@
[IO.File]::WriteAllText((Join-Path $fixture 'ServUO/Data/Decoration/Old/Britannia/britain.cfg'),$old)
[IO.File]::WriteAllText((Join-Path $fixture 'ServUO/Data/Decoration/Britannia/britain.cfg'),$old)
[IO.File]::WriteAllText((Join-Path $fixture 'ServUO/Data/Decoration/Britannia/patches.cfg'),"Static 0xFFFF`n1423 1698 0`n`nStatic 0x0B98 (Name=Patched Sign)`n1424 1698 0`n")
[IO.File]::WriteAllText((Join-Path $fixture 'UOX3/data/js/jsdata/worldtemplates/felucca_signs.jsdata'),"3`n")
$doorRows=@('3',
    '1701|#|0|12|1422|1698|0|0|0|2|0|0|0|400000|125|0|0|0|0|0|0|0|0|0',
    '1701|#|0|12|1425|1698|0|0|0|2|0|0|0|400000|125|0|0|0|0|0|0|0|0|0',
    '1703|#|0|13|1426|1698|0|0|0|2|0|0|0|400000|125|0|0|0|0|0|0|0|0|0')
[IO.File]::WriteAllLines((Join-Path $fixture 'UOX3/data/js/jsdata/worldtemplates/felucca_doors.jsdata'),$doorRows)
$facings=@('westcw','EastCCW','WestCCW','EastCW','SouthCW','NorthCCW','SouthCCW','NorthCW')
$faceText=[Text.StringBuilder]::new()
for($i=0;$i -lt 8;$i++) {
    [void]$faceText.AppendLine("DarkWoodDoor $((0x6A5 + $i*2)) (Facing=$($facings[$i]))")
    [void]$faceText.AppendLine("$(1430 + $i*3) 1705 0")
    [void]$faceText.AppendLine()
}
[IO.File]::WriteAllText((Join-Path $fixture 'ServUO/Data/Decoration/Britannia/facings.cfg'),$faceText.ToString())
$first=Join-Path $output 'fixture.dwd'
& $import -ClientRoot $ClientRoot -ReferenceRoot $fixture -OutFile $first
$bytes=[IO.File]::ReadAllBytes($first)
if([BitConverter]::ToInt64($bytes,8) -ne 13) {throw 'duplicate old/current placement was not collapsed or door/patch missing'}
$report=Get-Content -LiteralPath "$first.json" -Raw|ConvertFrom-Json
if(@($report.skipped|Where-Object {$_.Art -eq 65535 -and $_.Reason -eq 'absent legacy tiledata'}).Length -ne 1) {throw 'unsupported art was not reported'}
$byPosition=@{}
for($i=0;$i -lt [BitConverter]::ToInt64($bytes,8);$i++) {
    $at=64+$i*176;$px=[BitConverter]::ToInt64($bytes,$at);$py=[BitConverter]::ToInt64($bytes,$at+8)
    $byPosition["$px,$py"]=$at
}
$dx=@(-1,1,-1,1,1,1,0,0);$dy=@(1,1,0,-1,1,-1,0,-1)
for($i=0;$i -lt 8;$i++) {
    $at=$byPosition["$(1430 + $i*3),1705"]
    if([BitConverter]::ToInt64($bytes,$at+24) -ne 0x6A5+$i*2 -or [BitConverter]::ToInt64($bytes,$at+64) -ne 0x6A6+$i*2 -or
       [BitConverter]::ToInt64($bytes,$at+56) -ne 1 -or [BitConverter]::ToInt64($bytes,$at+72) -ne $dx[$i] -or [BitConverter]::ToInt64($bytes,$at+80) -ne $dy[$i]) {throw "Typed door facing $i differs from BaseDoor"}
}
if([BitConverter]::ToInt64($bytes,$byPosition['1422,1698']+24) -ne 0x675 -or
   [BitConverter]::ToInt64($bytes,$byPosition['1425,1698']+64) -ne 0x6A6 -or
   [BitConverter]::ToInt64($bytes,$byPosition['1426,1698']+56) -ne 4 -or $report.templateDoors -ne 2) {throw 'Supplemental door, locked type or CFG priority failed'}
Write-Output 'PASS typed doors: eight facings, missing placement supplement, locked type and CFG priority'
$second=Join-Path $output 'repeat.dwd'
& $import -ClientRoot $ClientRoot -ReferenceRoot $fixture -OutFile $second
if((Get-FileHash $first).Hash -ne (Get-FileHash $second).Hash) {throw 'import not deterministic'}
Write-Output 'PASS decoration import dedup, patches, legacy art filter and deterministic identity'
$reservation=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0)
$reservation.Start();$port=$reservation.LocalEndpoint.Port;$reservation.Stop()
$stdout=Join-Path $output 'adapter.stdout'
$stderr=Join-Path $env:TEMP ('decoration-adapter-'+[guid]::NewGuid().ToString('N')+'.stderr')
$adapter=Start-Process pwsh -ArgumentList @('-NoProfile','-File',('"'+(Join-Path $repo 'apps/uoaix/serve-mul.ps1')+'"'),'-ClientRoot',('"'+$ClientRoot+'"'),'-Port',$port,'-DecorationFile',('"'+$first+'"')) -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
$http=[Net.Http.HttpClient]::new()
$http.Timeout=[TimeSpan]::FromSeconds(5)
try {
    $ready=$false
    for($i=0;$i -lt 50 -and -not $ready;$i++) {
        if($adapter.HasExited) {throw "adapter exited; see $stderr"}
        try {$head=$http.GetByteArrayAsync("http://127.0.0.1:$port/decoration/0/64").GetAwaiter().GetResult();$ready=$true}
        catch {[Threading.Thread]::Sleep(100)}
    }
    if(-not $ready -or $head.Length -ne 64 -or [BitConverter]::ToInt64($head,0) -ne 0x31445744) {throw 'adapter header mismatch'}
    $body=$http.GetByteArrayAsync("http://127.0.0.1:$port/decoration/64/$($bytes.Length-64)").GetAwaiter().GetResult()
    for($i=0;$i -lt $body.Length;$i++) {if($body[$i] -ne $bytes[$i+64]) {throw 'adapter byte mismatch'}}
    $refused=$http.GetAsync("http://127.0.0.1:$port/decoration/$($bytes.Length-1)/10").GetAwaiter().GetResult()
    if([int]$refused.StatusCode -ne 416) {throw 'adapter accepted out-of-bounds range'}
    $size=$http.GetByteArrayAsync("http://127.0.0.1:$port/size/3").GetAwaiter().GetResult()
    if([BitConverter]::ToInt64($size,0) -ne 1036288) {throw 'existing MUL route regressed'}
    Write-Output 'PASS decoration adapter bytes, range refusal and existing MUL route'
} finally {
    $http.Dispose()
    if(-not $adapter.HasExited) {Stop-Process -Id $adapter.Id}
    Write-Output "Adapter evidence: $stdout ; $stderr"
}
