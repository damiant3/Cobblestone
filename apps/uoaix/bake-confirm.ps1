[CmdletBinding()]
param([Parameter(Mandatory=$true)][int]$Port,[Parameter(Mandatory=$true)][string]$KeyFile,[string]$HostName='127.0.0.1',[string]$Pending='')
# The decorator bake's comeback (UoaixDecorator.md): sends decor-baked with the pending serials bake-decor.ps1 wrote,
# so the server removes the baked items' world copies and drops their marks, then deletes the pending file. The
# handshake is admin-read.ps1's (UOAIX1: HMAC-derived keys, AES-GCM envelopes).
# panel-open closes an open browser session and writes a session-open row to the action log.
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$token=([IO.File]::ReadAllText((Resolve-Path -LiteralPath $KeyFile).Path)).Trim()
$parts=$token.Split(':')
$role=4
if($parts.Length -eq 3 -and $parts[0] -ceq 'UOAIXGM1'){$role=[int]$parts[1];$token=$parts[2]}
if($role -lt 4 -or $role -gt 132 -or $token -notmatch '^[0-9a-fA-F]{64}$'){throw 'Key file holds no admin key (64 hex, or UOAIXGM1:<role>:<64 hex>)'}
$raw=[Convert]::FromHexString($token)
$http=[Net.Http.HttpClient]::new()
$http.Timeout=[TimeSpan]::FromSeconds(10)
$http.DefaultRequestHeaders.ConnectionClose=$true
function Send([string]$Method,[string]$Path,[string]$Body=''){
    $request=[Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::new($Method),"http://${HostName}:$Port$Path")
    if($Method -eq 'POST'){$request.Content=[Net.Http.StringContent]::new($Body,[Text.Encoding]::ASCII,'text/plain')}
    try{$response=$http.Send($request);try{@{status=[int]$response.StatusCode;body=$response.Content.ReadAsStringAsync().GetAwaiter().GetResult()}}finally{$response.Dispose()}}
    finally{$request.Dispose()}
}
function Mac([byte[]]$Key,[string]$Label){[Convert]::ToHexString([Security.Cryptography.HMACSHA256]::HashData($Key,[Text.Encoding]::UTF8.GetBytes($Label))).ToLowerInvariant()}
try{
    $hello=Send GET '/handshake'
    $fields=$hello.body.Split("`n")
    if($hello.status -ne 200 -or $fields.Length -lt 2){throw "Handshake greeting refused: $($hello.status)"}
    $epoch=$fields[1]
    $incoming=[Security.Cryptography.HMACSHA256]::HashData($raw,[Text.Encoding]::UTF8.GetBytes("UOAIX1/request/$role/$epoch"))
    $outgoing=[Security.Cryptography.HMACSHA256]::HashData($raw,[Text.Encoding]::UTF8.GetBytes("UOAIX1/response/$role/$epoch"))
    $challenge=[Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)).ToLowerInvariant()
    $label="UOAIX1/sequence/$role/$challenge"
    $probe=Send POST '/handshake' "$role`n$challenge`n$(Mac $incoming $label)"
    $p=$probe.body.Split("`n")
    if($probe.status -ne 200 -or $p.Length -ne 2 -or $p[0] -notmatch '^\d{1,9}$'){throw "Sequence probe refused: $($probe.status) (wrong key or role?)"}
    if($p[1] -cne (Mac $outgoing "$label/$($p[0])")){throw 'Sequence probe authentication failed'}
    $floor=[long]$p[0]
    $label="UOAIX1/reserve/$role/$challenge/$floor"
    $reserve=Send POST '/handshake' "$role`n$challenge`n$floor`n$(Mac $incoming $label)"
    $p=$reserve.body.Split("`n")
    if($reserve.status -ne 200 -or $p.Length -ne 2 -or $p[0] -cne [string]$floor -or $p[1] -cne (Mac $outgoing $label)){throw 'Sequence reservation failed'}
    $script:sequence=$floor
    function Ask([hashtable]$Body){
        $script:sequence++
        $sequence=$script:sequence
        $nonce=[byte[]]::new(12);[BitConverter]::GetBytes($sequence).CopyTo($nonce,4)
        $plain=[Text.Encoding]::UTF8.GetBytes(($Body|ConvertTo-Json -Compress))
        $cipher=[byte[]]::new($plain.Length);$tag=[byte[]]::new(16)
        $aes=[Security.Cryptography.AesGcm]::new($incoming,16)
        try{$aes.Encrypt($nonce,$plain,$cipher,$tag,[Text.Encoding]::UTF8.GetBytes("UOAIX1/request/$role/$sequence"))}finally{$aes.Dispose()}
        $reply=Send POST '/admin' "$role`n$sequence`n$([Convert]::ToHexString($cipher).ToLowerInvariant())`n$([Convert]::ToHexString($tag).ToLowerInvariant())"
        if($reply.status -ne 200){throw "Authenticated request refused: $($reply.status)"}
        $r=$reply.body.Split("`n")
        if($r.Length -ne 2){throw 'Wrong encrypted reply shape'}
        $rc=[Convert]::FromHexString($r[0]);$rt=[Convert]::FromHexString($r[1]);$rp=[byte[]]::new($rc.Length)
        $aes=[Security.Cryptography.AesGcm]::new($outgoing,16)
        try{$aes.Decrypt($nonce,$rc,$rt,$rp,[Text.Encoding]::UTF8.GetBytes("UOAIX1/response/$role/$sequence"))}finally{$aes.Dispose()}
        return [Text.Encoding]::UTF8.GetString($rp)
    }
    $repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
    if(-not $Pending){$Pending=Join-Path $repo 'build-output/uoaix/decor-pending.json'}
    if(-not (Test-Path -LiteralPath $Pending)){Write-Output 'BAKE nothing pending';return}
    $p=Get-Content -LiteralPath $Pending -Raw|ConvertFrom-Json
    $serials=@($p.serials)
    $tiles=@(if($p.PSObject.Properties.Name -contains 'tiles'){$p.tiles})
    if($serials.Count -eq 0 -and $tiles.Count -eq 0){Remove-Item -LiteralPath $Pending;Write-Output 'BAKE pending held nothing';return}
    $opened=Ask @{command='panel-open';session=0}|ConvertFrom-Json
    if(-not ($opened.PSObject.Properties.Name -contains 'session')){throw "panel-open refused: $(($opened|ConvertTo-Json -Compress))"}
    if($serials.Count -gt 0){
        $reply=Ask @{command='panel-action';session=$opened.session;action='decor-baked';serials=($serials -join ',')}
        if(($reply|ConvertFrom-Json).PSObject.Properties.Name -contains 'error'){throw "decor-baked refused: $reply; $Pending kept"}
        [ordered]@{serials=@();tiles=$tiles;exported=$p.exported}|ConvertTo-Json|Set-Content -LiteralPath $Pending
        Write-Output "BAKE confirmed $($serials.Count) items: $reply"
    }
    if($tiles.Count -gt 0){
        # decor-land-baked takes the tiles as "x,y;x,y" (GmLand.codex) and answers {"ok":true,...} when it drops them.
        $reply=Ask @{command='decor-land-baked';session=$opened.session;tiles=($tiles -join ';')}
        if(-not $reply.StartsWith('{"ok":true')){throw "decor-land-baked refused: $reply; $Pending keeps the tiles"}
        Write-Output "BAKE confirmed $($tiles.Count) land tiles: $reply"
    }
    Remove-Item -LiteralPath $Pending
}finally{$http.Dispose();[Array]::Clear($raw,0,$raw.Length)}
