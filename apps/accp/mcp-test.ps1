[CmdletBinding()]
param([string]$Bundle='')
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$work=Join-Path $PSScriptRoot ('build-output/mcp-test-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work|Out-Null
$journal=Join-Path $work 'journal.jsonl'
$start=[Diagnostics.ProcessStartInfo]::new((Get-Command pwsh).Source)
$start.UseShellExecute=$false;$start.CreateNoWindow=$true
$start.RedirectStandardInput=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true;$start.StandardInputEncoding=[Text.UTF8Encoding]::new($false);$start.StandardOutputEncoding=[Text.UTF8Encoding]::new($false)
if(-not $Bundle){$Bundle=Join-Path $PSScriptRoot 'build-output'}
$Bundle=(Resolve-Path $Bundle).Path
$leases=(& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'mint-test-lease.ps1') -Work $work -Kernel (Join-Path $PSScriptRoot '../../seed/Codex.cdx') | Select-Object -Last 1) | ConvertFrom-Json
if($LASTEXITCODE -ne 0 -or -not $leases.allow){throw 'Lease mint failed'}
$binding=@('-Requester','3d4017c3e843895a92b70aa74d1b7ebc9c982ccf2ec4968cc0cd55f12af4660c','-Grantors','d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a','-Identity',('d4'*32),'-MaxLeaseSeconds','900','-Policy',$leases.allow_policy,'-NameBook',$leases.name_book)
foreach($arg in @('-NoProfile','-File',(Join-Path $PSScriptRoot 'serve.ps1'),'-Bundle',$Bundle,'-Mcp','-Journal',$journal)+$binding){$start.ArgumentList.Add($arg)}
$p=[Diagnostics.Process]::Start($start)
$p.StandardInput.AutoFlush=$true
$errors=$p.StandardError.ReadToEndAsync()
$pass=0;$fail=0;$utf8=[Text.UTF8Encoding]::new($false)
function Assert([string]$Name,[bool]$Ok,[string]$Detail=''){
    if($Ok){$script:pass++;Write-Host "PASS $Name"}else{$script:fail++;Write-Host "FAIL $Name $Detail"}
}
function Send($Object){$p.StandardInput.WriteLine(($Object|ConvertTo-Json -Depth 15 -Compress))}
function Receive {
    $read=$p.StandardOutput.ReadLineAsync()
    if(-not $read.Wait(35000)){throw 'MCP response timeout'}
    $line=$read.GetAwaiter().GetResult()
    if(-not $line){throw "MCP server exited: $($errors.GetAwaiter().GetResult())"}
    [IO.File]::AppendAllText((Join-Path $work 'wire.jsonl'),$line+"`n",$utf8)
    $line|ConvertFrom-Json
}
function Call($Id,[string]$Method,$Params=@{}){
    Send @{jsonrpc='2.0';id=$Id;method=$Method;params=$Params}
    Receive
}
function Tool($Id,[string]$Name,$Arguments=@{}){Call $Id 'tools/call' @{name=$Name;arguments=$Arguments}}
function Read-Journal {
    if(-not(Test-Path $journal)){return ''}
    $stream=[IO.File]::Open($journal,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite)
    $reader=[IO.StreamReader]::new($stream)
    try{$reader.ReadToEnd()}finally{$reader.Dispose()}
}
function Count-Records {@((Read-Journal)-split "`n"|Where-Object{$_}).Count}
try {
    $r=Call 0 'tools/list'
    Assert 'tools unavailable before initialization' ($r.error.code -eq -32002)
    $r=Call 'init' 'initialize' @{protocolVersion='2025-11-25';clientInfo=@{name='accp-proof';version='1'};capabilities=@{}}
    Assert 'initialize negotiates protocol' ($r.id -ceq 'init' -and $r.result.protocolVersion -ceq '2025-11-25')
    Assert 'initialize supplies computation guidance' ($r.result.instructions -match 'exact arithmetic' -and $r.result.instructions -match 'codex_run')
    Send @{jsonrpc='2.0';method='notifications/initialized'}
    $r=Call 1 'ping'
    Assert 'notification is silent and ping preserves numeric id' ($r.id -eq 1 -and $null -ne $r.result)
    $r=Call 2 'tools/list'
    $names=@($r.result.tools|ForEach-Object name)
    Assert 'five tools discoverable' ($names.Count -eq 5 -and @('codex_run','codex_check','codex_lens','codex_describe','codex_clarify'|Where-Object{$_ -notin $names}).Count -eq 0)
    foreach($tool in $r.result.tools){Assert "schema $($tool.name)" ($tool.inputSchema.type -eq 'object' -and $tool.inputSchema.additionalProperties -eq $false -and $tool.outputSchema.required -contains 'status')}
    $r=Call 3 'resources/list'
    Assert 'language card discoverable' ($r.result.resources[0].uri -eq 'codex://accp/language')
    $r=Call 4 'resources/read' @{uri='codex://accp/language'}
    $card=$r.result.contents[0].text
    Assert 'language card contains complete source and failure rules' ($card -match 'Chapter: Count' -and $card -match 'print-line-uni' -and $card -match 'never computed answers')
    Assert 'language card names truncating division and both remainders' ($card -match 'truncates toward zero' -and $card -match 'int-rem a b' -and $card -match 'int-mod a b')
    Assert 'discovery does not execute a program' ((Count-Records) -eq 0)
    $source="Chapter: McpAnswer`nSection: Entry`n  opening : Integer = 6 * 7`nPage 1`n"
    $r=Tool 'compute' 'codex_run' @{source=$source}
    Assert 'tool computes and returns structured result' ($r.id -ceq 'compute' -and $r.result.isError -eq $false -and $r.result.structuredContent.stdout -ceq "42`n") ($r|ConvertTo-Json -Depth 10 -Compress)
    $content=$r.result.content[0].text|ConvertFrom-Json
    Assert 'text and structured results agree' ($content.fact -ceq $r.result.structuredContent.fact -and $content.stdout -ceq "42`n")
    $original=[Convert]::FromBase64String($r.result._meta.accp_response_base64)
    $raw=$utf8.GetString($original)
    Assert 'exact ACCP response bytes are available' (($raw|ConvertFrom-Json).fact -ceq $content.fact)
    Assert 'tool response follows journal flush' ((Count-Records) -eq 1 -and (Read-Journal).Contains($content.fact))
    $r=Call 'sci' 'resources/read' @{uri='codex://accp/science'}
    $sci=$r.result.contents[0].text
    $exStart=$sci.IndexOf('Chapter: Exact'); $exEnd=$sci.IndexOf('Page 1',$exStart)
    Assert 'science card documents the exact library' ($exStart -ge 0 -and $exEnd -gt $exStart -and $sci -match 'library: exact')
    $exactSource=$sci.Substring($exStart,$exEnd+6-$exStart)+"`n"
    $r=Tool 'exact' 'codex_run' @{source=$exactSource;library='exact';limits=@{ms=10000}}
    Assert 'card exact example computes past 64 bits' ($r.result.isError -eq $false -and $r.result.structuredContent.stdout -ceq "156500072693749876333549759454926973536814597484617284976640`n3.33`n" -and $r.result.structuredContent.library -eq 'exact') ($r|ConvertTo-Json -Depth 10 -Compress)
    $spec="Chapter: Spec`n`n We say:`n the result should be fast and some users want it.`n`nSection: Entry`n  opening : Integer = 6 * 7`nPage 1`n"
    $r=Tool 'prose' 'codex_check' @{source=$spec;prose=$true}
    $pf=@($r.result.structuredContent.prose_findings)
    Assert 'check prose returns CPL findings' ($r.result.isError -eq $false -and ($pf -join ' ') -match "CDX1110: Banned word ' some '" -and ($pf -join ' ') -match "' should '") ($r|ConvertTo-Json -Depth 10 -Compress)
    $r=Tool 'noprose' 'codex_check' @{source=$spec}
    Assert 'check without prose reports no findings' ($r.result.isError -eq $false -and $null -eq $r.result.structuredContent.PSObject.Properties['prose_findings'])
    $r=Tool 'runprose' 'codex_run' @{source=$spec;prose=$true}
    Assert 'prose is refused outside check' ($r.error.code -eq -32602)
    $r=Tool 6 'codex_check' @{source=$source}
    Assert 'check compiles without executing' ($r.result.isError -eq $false -and $r.result.structuredContent.status -eq 'ok' -and $r.result.structuredContent.ms.run -eq 0)
    $broken=$source.Replace('6 * 7','unknown-value')
    $r=Tool 'broken' 'codex_run' @{source=$broken}
    Assert 'compiler diagnostics are tool errors' ($r.result.isError -eq $true -and $r.result.structuredContent.status -eq 'diagnostics' -and ($r.result.structuredContent.diagnostics -join ' ') -match 'CDX3002')
    $r=Tool 'fixed' 'codex_run' @{source=$source}
    Assert 'corrected source succeeds after diagnostics' ($r.result.isError -eq $false -and $r.result.structuredContent.stdout -ceq "42`n")
    $denied="Chapter: Denied`nSection: Entry`n  opening : [Console, Network.Read] Nothing = act`n    print-line-uni `"no network`"`n  end`nPage 1`n"
    $r=Tool 9 'codex_run' @{source=$denied}
    Assert 'effect refusal is visible as tool error' ($r.result.isError -eq $true -and $r.result.structuredContent.needs -contains 'Network.Read')
    $r=Tool 10 'codex_lens' @{source=$source}
    Assert 'lens returns WAT' ($r.result.isError -eq $false -and $r.result.structuredContent.wat.Contains('(module'))
    $r=Tool 11 'codex_describe'
    Assert 'describe identifies the MCP conduit' ($r.result.isError -eq $false -and $r.result.structuredContent.wire -eq 'mcp-jsonrpc')
    Assert 'describe reports the bound instance and its held policy' ($r.result.structuredContent.binding.requester -ceq '3d4017c3e843895a92b70aa74d1b7ebc9c982ccf2ec4968cc0cd55f12af4660c' -and $r.result.structuredContent.binding.policy_sha256 -ceq $leases.allow_sha256)
    $r=Tool 'l1' 'codex_run' @{source=$denied;lease=$leases.allow}
    Assert 'a current lease under the held policy admits the run over MCP' ($r.result.isError -eq $false -and $r.result.structuredContent.stdout -ceq "no network`n" -and $r.result.structuredContent.lease.policy_sha256 -ceq $leases.allow_sha256) ($r|ConvertTo-Json -Depth 8 -Compress)
    $r=Tool 'l2' 'codex_run' @{source=$denied;lease='zz'}
    Assert 'a malformed lease is refused naming the check' ($r.result.isError -eq $true -and $r.result.structuredContent.stage -eq 'admission' -and $r.result.structuredContent.reason -ceq 'lease is not lowercase hex') ($r|ConvertTo-Json -Depth 8 -Compress)
    $r=Tool 'l3' 'codex_run' @{source=$denied;lease=$leases.deny}
    Assert 'a lease drawn against another policy is refused naming the check' ($r.result.isError -eq $true -and $r.result.structuredContent.reason -ceq 'lease names a policy this conduit does not hold') ($r|ConvertTo-Json -Depth 8 -Compress)
    $r=Tool 'l4' 'codex_check' @{source=$denied;lease=$leases.allow}
    Assert 'codex_check takes no lease' ($r.error.code -eq -32602)
    $r2=Call 'schema' 'tools/list'
    $runTool=@($r2.result.tools|Where-Object name -eq 'codex_run')[0]
    Assert 'codex_run schema offers lease' ($null -ne $runTool.inputSchema.properties.lease -and $runTool.inputSchema.properties.lease.type -eq 'string')
    $before=Count-Records
    $r=Tool 'c1' 'codex_clarify' @{text="I want the thing but not the other thing and it's not fair because he always gets more."}
    $c=$r.result.structuredContent
    Assert 'clarify detects the child register' ($r.result.isError -eq $false -and $c.register -ceq 'child' -and $c.register_source -ceq 'detected') ($r|ConvertTo-Json -Depth 10 -Compress)
    Assert 'clarify asks the referent question first' ($c.next_question -ceq "Which one do you mean by 'thing'?")
    Assert 'clarify orders referents before cardinality before threshold' ((@($c.unresolved|ForEach-Object{$_.kind+':'+$_.word})-join ' ') -ceq 'referent:thing referent:it referent:he cardinality:always threshold:more') (@($c.unresolved|ForEach-Object{$_.kind+':'+$_.word})-join ' ')
    Assert 'clarify text and structured results agree' (($r.result.content[0].text|ConvertFrom-Json).next_question -ceq $c.next_question)
    $r=Tool 'c8' 'codex_clarify' @{text='Most people want more, and they always ask.';register='casual'}
    $c=$r.result.structuredContent
    Assert 'clarify reorders a later referent ahead of earlier words' ((@($c.unresolved|ForEach-Object{$_.kind+':'+$_.word})-join ' ') -ceq 'referent:they cardinality:most cardinality:always threshold:more') (@($c.unresolved|ForEach-Object{$_.kind+':'+$_.word})-join ' ')
    Assert 'clarify next question is the reordered referent' ($c.next_question -ceq "When you say 'they', which one specifically?") $c.next_question
    $r=Tool 'c2' 'codex_clarify' @{text="A meeting is at noon.`nHe said yes.";register='formal'}
    $c=$r.result.structuredContent
    Assert 'clarify keeps a solid sentence and names the unresolved one' ($c.register_source -ceq 'requested' -and $c.sentences -eq 2 -and @($c.solid).Count -eq 1 -and $c.solid[0].text -ceq 'A meeting is at noon.' -and $c.unresolved[0].sentence -eq 2) ($r|ConvertTo-Json -Depth 10 -Compress)
    Assert 'clarify phrases the formal register' ($c.next_question -ceq "NP-1: referent of 'he' is unresolved. Sentence 2 names no antecedent")
    $r=Tool 'c3' 'codex_clarify' @{text='Add two to three.'}
    Assert 'clarify reports a fully resolved text' ($r.result.structuredContent.fully_resolved -eq $true -and $r.result.structuredContent.next_question -ceq '' -and @($r.result.structuredContent.unresolved).Count -eq 0)
    $r=Tool 'c4' 'codex_clarify' @{text='It is late.';register='shouting'}
    Assert 'clarify refuses an unknown register' ($r.error.code -eq -32602)
    $r=Tool 'c5' 'codex_clarify' @{text='It is late.';source='x'}
    Assert 'clarify refuses unknown arguments' ($r.error.code -eq -32602)
    $r=Tool 'c6' 'codex_clarify' @{text=('a'*16385)}
    Assert 'clarify refuses text past its ceiling' ($r.error.code -eq -32602)
    $r=Tool 'c7' 'codex_clarify' @{text=7}
    Assert 'clarify refuses non-string text' ($r.error.code -eq -32602)
    Send @{jsonrpc='2.0';id='c9';method='tools/call';params=@{name='codex_clarify';arguments=@{text="A`0B is here."}}}
    $read=$p.StandardOutput.ReadLineAsync(); if(-not $read.Wait(35000)){throw 'MCP response timeout'}; $rawLine=$read.GetAwaiter().GetResult()
    Assert 'clarify escapes NUL in an echoed sentence' ($rawLine.IndexOf([char]0) -lt 0 -and $rawLine.Contains('A\u0000B is here.')) ($rawLine.Replace([string][char]0,'<NUL>'))
    Assert 'clarify NUL round-trips through a JSON parser' ((($rawLine|ConvertFrom-Json).result.structuredContent.solid[0].text) -ceq "A`0B is here.")
    $r=Tool 'c10' 'codex_clarify' @{text="A café serves naïve crème brûlée. Garçon, ça va."}
    $c=$r.result.structuredContent
    Assert 'clarify round-trips non-ASCII sentences' ($c.sentences -eq 2 -and $c.solid[0].text -ceq 'A café serves naïve crème brûlée.' -and $c.solid[1].text -ceq 'Garçon, ça va.') ($r|ConvertTo-Json -Depth 10 -Compress)
    Assert 'clarify executes no program' ((Count-Records) -eq $before)
    $r=Tool 12 'unknown_tool'
    Assert 'unknown tool is a protocol error' ($r.error.code -eq -32602)
    $r=Tool 13 'codex_run' @{source=$source;grant='forged'}
    Assert 'caller cannot add grant parameters' ($r.error.code -eq -32602)
    $r=Tool '13b' 'codex_run' @{source=$denied;lease='forged'}
    Assert 'a forged lease is refused at admission, not granted' ($r.result.isError -eq $true -and $r.result.structuredContent.reason -ceq 'lease is not lowercase hex') ($r|ConvertTo-Json -Depth 8 -Compress)
    $before=Count-Records
    Send @{jsonrpc='2.0';method='tools/call';params=@{name='codex_run';arguments=@{source=$source}}}
    Send @{jsonrpc='2.0';method='notifications/cancelled';params=@{requestId=14}}
    $r=Call 15 'ping'
    Assert 'tool-call notification does not execute or reply' ($r.id -eq 15 -and (Count-Records) -eq $before)
    $p.StandardInput.WriteLine('{"jsonrpc":"2.0","id":1,"id":2,"method":"ping"}')
    $r=Receive
    Assert 'duplicate envelope properties refused' ($r.error.code -eq -32600)
    $p.StandardInput.WriteLine('{')
    $r=Receive
    Assert 'malformed JSON gets parse error' ($r.error.code -eq -32700)
    $r=Call 16 'resources/read' @{uri='file:///private'}
    Assert 'resource reads are restricted to language card' ($r.error.code -eq -32002)
    $r=Call 17 'resources/templates/list'
    Assert 'resource templates report empty' ($r.result.resourceTemplates.Count -eq 0)
    $r=Call 18 'initialize' @{protocolVersion='2025-11-25';clientInfo=@{};capabilities=@{}}
    Assert 'duplicate initialization refused' ($r.error.code -eq -32600)
    for($i=0;$i -lt 8;$i++){$r=Tool "repeat-$i" 'codex_run' @{source=$source};Assert "repeat $i" ($r.result.structuredContent.stdout -ceq "42`n")}
    $p.StandardInput.Close()
    if(-not $p.WaitForExit(10000)){throw 'MCP shutdown timed out'}
    $err=$errors.GetAwaiter().GetResult()
    [IO.File]::WriteAllText((Join-Path $work 'stderr.log'),$err,$utf8)
    Assert 'clean shutdown' ($p.ExitCode -eq 0) $err
    $starts=@([regex]::Matches($err,'heap_start=(\d+)')|ForEach-Object{$_.Groups[1].Value}|Sort-Object -Unique)
    Assert 'MCP heap checkpoint is restored' ($starts.Count -eq 1) ($starts-join ',')
    $verify=Join-Path $PSScriptRoot 'verify-fact.ps1'
    $good=[Convert]::ToBase64String($original)
    $v=& pwsh -NoProfile -File $verify -Journal $journal -ResponseBase64 $good 2>&1
    Assert 'verify-fact accepts a retained response and its journal' ($LASTEXITCODE -eq 0 -and ($v -join ' ') -match 'VERIFIED') ($v -join ' ')
    $tampered=$utf8.GetBytes($utf8.GetString($original).Replace('"42\n"','"43\n"'))
    $v=& pwsh -NoProfile -File $verify -Journal $journal -ResponseBase64 ([Convert]::ToBase64String($tampered)) 2>&1
    Assert 'verify-fact refuses a tampered response' ($LASTEXITCODE -eq 1 -and ($v -join ' ') -match 'REFUSED: response bytes') ($v -join ' ')
    $bad=Join-Path $work 'journal-edited.jsonl'
    [IO.File]::WriteAllText($bad,(Read-Journal).Replace('"status":"ok"','"status":"refused"'),$utf8)
    $v=& pwsh -NoProfile -File $verify -Journal $bad 2>&1
    Assert 'verify-fact refuses an edited journal record' ($LASTEXITCODE -eq 1 -and ($v -join ' ') -match 'fact does not hash its record') ($v -join ' ')
} finally {
    if(-not $p.HasExited){$p.Kill($true)}
    $p.Dispose()
}
Write-Host "MCP focused proof: $pass pass, $fail fail. Artifacts: $work"
if($fail -gt 0){exit 1}
