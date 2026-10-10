[CmdletBinding()]
param([Parameter(Mandatory)][int]$Port,[Parameter(Mandatory)][string]$KeyFile,[string]$Pending='',[string]$ClientRoot='')
# The decorator bake's export, run against the live shard before a downtime (UoaixDecorator.md): reads live.decor's
# marked items (marked: serial, art, hue, x, y, z, account) and land edits (land: x, y, art, z, account; absent before
# the land tools), appends them to decor.cfg and land.cfg, and writes the serials and tiles to the pending file, which
# the comeback's decor-baked and decor-land-baked commands consume. The stopped-server install folds both files in
# (install-map-cache.ps1); decor-statics.ps1 and decor-land.ps1 patch the players' clients.
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if(-not $Pending){$Pending=Join-Path $repo 'build-output/uoaix/decor-pending.json'}
$cfg=Join-Path $PSScriptRoot 'decor.cfg'
$landCfg=Join-Path $PSScriptRoot 'land.cfg'
if(Test-Path -LiteralPath $Pending){throw "A bake is pending ($Pending): confirm it with decor-baked before exporting again"}
$text=(& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'admin-read.ps1') -Port $Port -KeyFile $KeyFile -Section 'live.decor' | Out-String).Trim()
if($LASTEXITCODE -ne 0){throw "admin-read failed: $text"}
$decor=$text|ConvertFrom-Json
$marked=@(if($decor.PSObject.Properties.Name -contains 'marked'){$decor.marked})
if(-not ($decor.PSObject.Properties.Name -contains 'marked') -and -not ($decor.PSObject.Properties.Name -contains 'land')){throw 'live.decor has neither a marked list nor land edits'}
$land=@(if($decor.PSObject.Properties.Name -contains 'land'){$decor.land})
$removed=@(if($decor.PSObject.Properties.Name -contains 'removed'){$decor.removed})
foreach($m in $marked){
    foreach($field in 'serial','art','hue','x','y','z','account'){if($null -eq $m.$field){throw "Marked item lacks $field"}}
    if([int]$m.art -lt 0 -or [int]$m.art -gt 0x7FFF -or [int]$m.hue -lt 0 -or [int]$m.hue -gt 65535 -or [int]$m.z -lt -128 -or [int]$m.z -gt 127 -or [int]$m.x -ge 6144 -or [int]$m.y -ge 4096){throw "Marked item $($m.serial) outside the legacy client's fields"}
    if([string]$m.account -notmatch '^[A-Za-z0-9 _.-]{1,30}$'){throw "Marked item $($m.serial) account name is not plain ASCII"}
}
foreach($r in $removed){
    foreach($field in 'serial','art','x','y','z','account'){if($null -eq $r.$field){throw "Removal mark lacks $field"}}
    if([int]$r.art -lt 1 -or [int]$r.art -gt 0x3FFF -or [int]$r.z -lt -128 -or [int]$r.z -gt 127 -or [int]$r.x -lt 0 -or [int]$r.x -ge 6144 -or [int]$r.y -lt 0 -or [int]$r.y -ge 4096){throw "Removal mark $($r.serial) outside the legacy client's fields"}
    if([string]$r.account -notmatch '^[A-Za-z0-9 _.-]{1,30}$'){throw "Removal mark $($r.serial) account name is not plain ASCII"}
}
foreach($l in $land){
    foreach($field in 'x','y','art','z','account'){if($null -eq $l.$field){throw "Land edit lacks $field"}}
    if([int]$l.art -lt -1 -or [int]$l.art -gt 0x3FFF -or [int]$l.z -lt -128 -or [int]$l.z -gt 127 -or [int]$l.x -lt 0 -or [int]$l.x -ge 6144 -or [int]$l.y -lt 0 -or [int]$l.y -ge 4096){throw "Land edit $($l.x),$($l.y) outside the legacy map"}
    if([string]$l.account -notmatch '^[A-Za-z0-9 _.-]{1,30}$'){throw "Land edit $($l.x),$($l.y) account name is not plain ASCII"}
}
if($marked.Count -eq 0 -and $land.Count -eq 0 -and $removed.Count -eq 0){Write-Output 'BAKE nothing marked';return}
# A marked building piece is one item of art 0x4000 + multi; it bakes as the multi's drawn components from the client's
# MULTI.IDX (12 bytes a multi: offset, length, extra) and MULTI.MUL (12 bytes a component: art, x, y, z as 16-bit
# offsets, then flags; flags 0 is a component the client does not draw: placeholder art 0x0001, doors, boat parts).
$multiIdx=$null;$multiMul=$null
if(@($marked|Where-Object {[int]$_.art -ge 0x4000}).Count -gt 0){
    if(-not $ClientRoot){throw 'A marked building piece needs -ClientRoot for MULTI.IDX and MULTI.MUL'}
    $multiIdx=[IO.File]::ReadAllBytes((Join-Path $ClientRoot 'MULTI.IDX'));$multiMul=[IO.File]::ReadAllBytes((Join-Path $ClientRoot 'MULTI.MUL'))
}
function Get-Parts($m){
    $list=[Collections.Generic.List[int[]]]::new()
    if([int]$m.art -lt 0x4000){$list.Add([int[]]@([int]$m.art,0,0,0));return ,$list}
    $id=[int]$m.art-0x4000;if(($id+1)*12 -gt $multiIdx.Length){throw "Marked item $($m.serial) names multi $id past MULTI.IDX"}
    $off=[BitConverter]::ToInt32($multiIdx,$id*12);$len=[BitConverter]::ToInt32($multiIdx,$id*12+4)
    if($off -lt 0 -or $len -le 0 -or $len % 12 -ne 0 -or $off+$len -gt $multiMul.Length){throw "Marked item $($m.serial) names multi $id with no components"}
    for($a=$off;$a -lt $off+$len;$a+=12){if([BitConverter]::ToInt32($multiMul,$a+8) -ne 0){$list.Add([int[]]@([BitConverter]::ToUInt16($multiMul,$a),[BitConverter]::ToInt16($multiMul,$a+2),[BitConverter]::ToInt16($multiMul,$a+4),[BitConverter]::ToInt16($multiMul,$a+6)))}}
    return ,$list
}
if($marked.Count -gt 0 -or $removed.Count -gt 0){
    if((Get-Item -LiteralPath $cfg).IsReadOnly){& p4 edit $cfg | Out-Null}
    # A removal mark bakes as a Remove line: the client static of that art at that place leaves both clients and the
    # server's colliders (decor-statics.ps1, install-map-cache.ps1).
    $lines=@(foreach($m in $marked){
        foreach($p in (Get-Parts $m)){
            $x=[int]$m.x+$p[1];$y=[int]$m.y+$p[2];$z=[int]$m.z+$p[3]
            if($p[0] -gt 0x3FFF -or $x -lt 0 -or $x -ge 6144 -or $y -lt 0 -or $y -ge 4096 -or $z -lt -128 -or $z -gt 127){throw "Marked item $($m.serial) has a part outside the legacy map"}
            'Static 0x{0:X4} (Hue=0x{1:X})' -f $p[0],[int]$m.hue
            '{0} {1} {2} {3} serial {4}' -f $x,$y,$z,$m.account,[long]$m.serial
        }
    }) + @(foreach($r in $removed){'Remove 0x{0:X4} {1} {2} {3} {4} serial {5}' -f [int]$r.art,[int]$r.x,[int]$r.y,[int]$r.z,$r.account,[long]$r.serial})
    [IO.File]::AppendAllText($cfg,(($lines -join "`r`n")+"`r`n"))
}
if($land.Count -gt 0){
    if((Get-Item -LiteralPath $landCfg).IsReadOnly){& p4 edit $landCfg | Out-Null}
    # Art -1 keeps the client's land art and changes only the height (a raise or lower; the server holds no land art).
    $lines=foreach($l in $land){'{0} {1} {2} {3} {4}' -f [int]$l.x,[int]$l.y,$(if([int]$l.art -lt 0){'keep'}else{'0x{0:X4}' -f [int]$l.art}),[int]$l.z,$l.account}
    [IO.File]::AppendAllText($landCfg,(($lines -join "`r`n")+"`r`n"))
}
New-Item -ItemType Directory -Force -Path (Split-Path $Pending) | Out-Null
[ordered]@{serials=@(@($marked)+@($removed)|ForEach-Object {[long]$_.serial});tiles=@($land|ForEach-Object {'{0},{1}' -f [int]$_.x,[int]$_.y}|Select-Object -Unique);exported=[DateTime]::UtcNow.ToString('o')}|ConvertTo-Json|Set-Content -LiteralPath $Pending
Write-Output "BAKE exported $($marked.Count) marked items and $($removed.Count) static removals to $cfg and $($land.Count) land edits to $landCfg; pending $Pending"
