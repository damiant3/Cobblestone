# mint-test-lease.ps1 -- for the ACCP proofs (test.ps1, mcp-test.ps1) only: writes an allowing and a denying
# Jake policy and the grantor's signed name book into -Work, mints one sealed lease per policy that is current
# for ten minutes from now (RFC 8032 TEST 1 grantor, TEST 2 holder, identity d4 x 32), compiling one guest and
# running one. Prints one JSON object: allow, deny (lease hex), allow_policy, deny_policy, name_book (paths), allow_sha256.
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Work, [Parameter(Mandatory)][string]$Kernel)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$repo=(Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$work=$Work
$utf8=[Text.UTF8Encoding]::new($false)
$allowPolicy=Join-Path $work 'allow-policy.txt';[IO.File]::WriteAllText($allowPolicy,"We say:`n`"Jake's agent`" is permitted to use Network.Read.",$utf8)
$denyPolicy=Join-Path $work 'deny-policy.txt';[IO.File]::WriteAllText($denyPolicy,"We say:`n`"Jake's agent`" is permitted to use Network.Read.`n`"Jake's agent`" is never permitted to use Network.Read.",$utf8)
$quotaPolicy=Join-Path $work 'quota-policy.txt';[IO.File]::WriteAllText($quotaPolicy,"We say:`n`"Jake's agent`" is permitted to use Network.Read for no more than 1 hour per day.",$utf8)
$quotaHash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([IO.File]::ReadAllBytes($quotaPolicy))).ToLowerInvariant()
$policyBytes={param($p)'['+(([Security.Cryptography.SHA256]::HashData([IO.File]::ReadAllBytes($p))|ForEach-Object{[string]$_}) -join ', ')+']'}
$allowHash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([IO.File]::ReadAllBytes($allowPolicy))).ToLowerInvariant()
$nowSeconds=[DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$mint=@'
Chapter: AccpLeaseMint
  cites Accp chapter AccpGrant
  cites Trust chapter AgentProtocol
  cites Trust chapter GrantSeal
  cites Trust chapter NameBook
  cites Foreword chapter Ed25519
  cites Foreword chapter Console

Section: Entry

  grantor-secret : List Integer = [157, 97, 177, 157, 239, 253, 90, 96, 186, 132, 74, 244, 146, 236, 44, 196, 68, 73, 197, 105, 123, 50, 105, 25, 112, 59, 172, 3, 28, 174, 127, 96]

  holder-secret : List Integer = [76, 205, 8, 155, 40, 255, 150, 218, 157, 182, 195, 70, 236, 17, 78, 15, 91, 138, 49, 159, 53, 171, 166, 36, 218, 140, 246, 237, 79, 184, 166, 251]

  identity-key : List Integer = [212, 212, 212, 212, 212, 212, 212, 212, 212, 212, 212, 212, 212, 212, 212, 212, 212, 212, 212, 212, 212, 212, 212, 212, 212, 212, 212, 212, 212, 212, 212, 212]

  lease-for : List Integer -> GrantMsg
  lease-for (policy) = lease-flows policy []

  lease-flows : List Integer, List Text -> GrantMsg
  lease-flows (policy) (flows) = grant-seal (GrantMsg {
    id = [7, 2],
    proposal = [],
    from = AgentIdentity { public-key = ed25519-public-key holder-secret },
    grantor = AgentIdentity { public-key = ed25519-public-key grantor-secret },
    audience = AgentIdentity { public-key = identity-key },
    capability = Capability { name = "Network", direction = 2, scope = "" },
    scope = Scope { direction = 2, path = "", duration = 0 },
    not-before = NOT_BEFORE,
    expires = EXPIRES,
    policy-sha256 = policy,
    flow-rules = flows,
    gates = [],
    conditions = [],
    signature = []
  }) grantor-secret

  opening : [Console] Nothing = act
    print-line-uni ("allow " & ag-hex (encode-grant-body (lease-for ALLOW_HASH)))
    print-line-uni ("deny " & ag-hex (encode-grant-body (lease-for DENY_HASH)))
    print-line-uni ("quota " & ag-hex (encode-grant-body (lease-for QUOTA_HASH)))
    print-line-uni ("flow " & ag-hex (encode-grant-body (lease-flows ALLOW_HASH ["Network.Read -> Console.Write"])))
    print-line-uni ("book " & ag-hex (name-book-encode (name-book-sign grantor-secret (ed25519-public-key grantor-secret) [NameEntry { name = "Jake's agent", key = ed25519-public-key holder-secret }])))
  end

Page 1
'@
$mint=$mint.Replace('NOT_BEFORE',[string]($nowSeconds-60)).Replace('EXPIRES',[string]($nowSeconds+600)).Replace('ALLOW_HASH',(& $policyBytes $allowPolicy)).Replace('DENY_HASH',(& $policyBytes $denyPolicy)).Replace('QUOTA_HASH',(& $policyBytes $quotaPolicy))
$mintSrc=Join-Path $work 'lease-mint.codex';[IO.File]::WriteAllText($mintSrc,$mint.Replace("`r`n","`n").Replace("`n","`r`n"),$utf8)
& pwsh -NoProfile -File (Join-Path $repo 'build/compile.ps1') -Src $mintSrc -Out (Join-Path $work 'lease-mint.cdx') -Log (Join-Path $work 'lease-mint.log') -Kernel $Kernel
if($LASTEXITCODE -ne 0){Get-Content (Join-Path $work 'lease-mint.log') -Tail 20;throw 'Lease mint compile failed'}
& pwsh -NoProfile -File (Join-Path $repo 'build/test-run.ps1') -Kernel (Join-Path $work 'lease-mint.cdx') -OutFile (Join-Path $work 'lease-mint.txt')
$minted=@{};foreach($l in [IO.File]::ReadAllLines((Join-Path $work 'lease-mint.txt'))){$p=$l.Split(' ',2);if($p.Count -eq 2){$minted[$p[0]]=$p[1]}}
if(-not ($minted.allow -and $minted.deny -and $minted.flow -and $minted.quota -and $minted.book)){throw 'Lease mint printed no lease'}
$bookFile=Join-Path $work 'name-book.hex';[IO.File]::WriteAllText($bookFile,$minted.book,$utf8)
[ordered]@{allow=$minted.allow;deny=$minted.deny;flow=$minted.flow;quota=$minted.quota;quota_policy=$quotaPolicy;quota_sha256=$quotaHash;allow_policy=$allowPolicy;deny_policy=$denyPolicy;name_book=$bookFile;allow_sha256=$allowHash} | ConvertTo-Json -Compress
