[CmdletBinding()]
param([Parameter(Mandatory=$true)][int]$Port,[Parameter(Mandatory=$true)][string]$KeyFile,
    [ValidateSet('panel-data','gm-list')][string]$Command='panel-data',[string]$HostName='127.0.0.1',[string]$Section='')
# Reads the live admin state the way the login page (AdminLoginPage) does (UOAIX1 handshake: HMAC-derived keys, AES-GCM envelopes) and
# prints the reply JSON, whole or one top-level section (-Section live, or a dotted path such as live.law.jail).
# Read commands only; the protocol is the one apps/uoaix/test-admin.ps1 grades. The panel holds one owner session:
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
    $opened=Ask @{command='panel-open';session=0}|ConvertFrom-Json
    if(-not ($opened.PSObject.Properties.Name -contains 'session')){throw "panel-open refused: $(($opened|ConvertTo-Json -Compress))"}
    $json=Ask @{command=$Command;session=$opened.session}
    if(-not $Section){$json;return}
    $node=$json|ConvertFrom-Json
    foreach($name in $Section.Split('.')){if($null -eq $node -or -not ($node.PSObject.Properties.Name -contains $name)){throw "No section '$name' in the reply"};$node=$node.$name}
    $node|ConvertTo-Json -Depth 12
}finally{$http.Dispose();[Array]::Clear($raw,0,$raw.Length)}
