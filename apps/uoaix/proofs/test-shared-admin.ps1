[CmdletBinding()]
param([Parameter(Mandatory)][int]$Port,[Parameter(Mandatory)][string]$Config,
    [Parameter(Mandatory)][string]$OutDir,[Parameter(Mandatory)][string]$StopFile)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
if(Test-Path $OutDir){throw 'OutDir must be new'}
[void](New-Item -ItemType Directory $OutDir)
$lines=[IO.File]::ReadAllLines($Config)
if($lines.Length -ne 5 -or @($lines|Where-Object {$_ -notmatch '^[0-9a-f]{64}$'}).Count){throw 'Fixture config invalid'}
$key=[Convert]::FromHexString($lines[0]);$epoch=$lines[4]
$requestKey=[Security.Cryptography.HMACSHA256]::HashData($key,[Text.Encoding]::UTF8.GetBytes("UOAIX1/request/1/$epoch"))
$responseKey=[Security.Cryptography.HMACSHA256]::HashData($key,[Text.Encoding]::UTF8.GetBytes("UOAIX1/response/1/$epoch"))
$http=[Net.Http.HttpClient]::new();$http.Timeout=[TimeSpan]::FromSeconds(10)
$http.DefaultRequestHeaders.ConnectionClose=$true
$receipt=[ordered]@{passed=$false;unauthenticatedRefused=$false;healthTimes=@()}
function Send([string]$Path,[string]$Body){
    $request=[Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Post,"http://127.0.0.1:$Port$Path")
    $request.Content=[Net.Http.StringContent]::new($Body,[Text.Encoding]::ASCII,'text/plain')
    try{$response=$http.Send($request);try{return @{status=[int]$response.StatusCode;body=$response.Content.ReadAsStringAsync().GetAwaiter().GetResult()}}finally{$response.Dispose()}}finally{$request.Dispose()}
}
function Tag([byte[]]$Secret,[string]$Text){[Convert]::ToHexString([Security.Cryptography.HMACSHA256]::HashData($Secret,[Text.Encoding]::UTF8.GetBytes($Text))).ToLowerInvariant()}
function Health([long]$Sequence){
    $nonce=[byte[]]::new(12);[BitConverter]::GetBytes($Sequence).CopyTo($nonce,4)
    $plain=[Text.Encoding]::UTF8.GetBytes('{"command":"health"}')
    $cipher=[byte[]]::new($plain.Length);$tag=[byte[]]::new(16)
    $aes=[Security.Cryptography.AesGcm]::new($requestKey,16)
    try{$aes.Encrypt($nonce,$plain,$cipher,$tag,[Text.Encoding]::UTF8.GetBytes("UOAIX1/request/1/$Sequence"))}finally{$aes.Dispose()}
    $response=Send '/admin' "1`n$Sequence`n$([Convert]::ToHexString($cipher).ToLowerInvariant())`n$([Convert]::ToHexString($tag).ToLowerInvariant())"
    $parts=$response.body.Split("`n")
    if($response.status -ne 200 -or $parts.Length -ne 2){throw 'Authenticated health refused'}
    $cipher=[Convert]::FromHexString($parts[0]);$plain=[byte[]]::new($cipher.Length)
    $aes=[Security.Cryptography.AesGcm]::new($responseKey,16)
    try{$aes.Decrypt($nonce,$cipher,[Convert]::FromHexString($parts[1]),$plain,[Text.Encoding]::UTF8.GetBytes("UOAIX1/response/1/$Sequence"))}finally{$aes.Dispose()}
    $health=[Text.Encoding]::UTF8.GetString($plain)|ConvertFrom-Json
    if($health.population -ne 1){throw 'Wrong shared health fixture'}
    $receipt.healthTimes+=([datetimeoffset]::UtcNow.ToString('o'))
}
try{
    $denied=Send '/admin' '{"command":"health"}'
    if($denied.status -ne 401 -or $denied.body -cne ''){throw 'Unauthenticated admin exposed a response'}
    $receipt.unauthenticatedRefused=$true
    $challenge=[Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)).ToLowerInvariant()
    $label="UOAIX1/sequence/1/$challenge"
    $probe=Send '/handshake' "1`n$challenge`n$(Tag $requestKey $label)"
    $parts=$probe.body.Split("`n")
    if($probe.status -ne 200 -or $parts.Length -ne 2 -or $parts[0] -notmatch '^\d{1,9}$'){throw 'Sequence probe refused'}
    if($parts[1] -cne (Tag $responseKey "$label/$($parts[0])")){throw 'Sequence probe authentication failed'}
    $floor=[long]$parts[0];$label="UOAIX1/reserve/1/$challenge/$floor"
    $reserve=Send '/handshake' "1`n$challenge`n$floor`n$(Tag $requestKey $label)"
    $parts=$reserve.body.Split("`n")
    if($reserve.status -ne 200 -or $parts.Length -ne 2 -or $parts[0] -cne [string]$floor -or $parts[1] -cne (Tag $responseKey $label)){throw 'Sequence reservation failed'}
    $sequence=$floor+1;Health $sequence
    [IO.File]::WriteAllText((Join-Path $OutDir 'ready'),'authenticated')
    $deadline=[datetime]::UtcNow.AddSeconds(180)
    while(-not (Test-Path $StopFile)){
        if([datetime]::UtcNow -gt $deadline -or $sequence -ge $floor+1024){throw 'Shared admin proof deadline or lease exhausted'}
        $sequence++;Health $sequence
        Start-Sleep -Milliseconds 100
    }
    if($receipt.healthTimes.Count -lt 2){throw 'No repeated authenticated health'}
    $receipt.passed=$true
    Write-Output 'PASS unauthenticated refusal and repeated authenticated health'
}finally{
    $http.Dispose()
    [IO.File]::WriteAllText((Join-Path $OutDir 'result.json'),($receipt|ConvertTo-Json -Depth 5))
}
