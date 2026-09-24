[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$work=Join-Path $PSScriptRoot ('build-output/mcp-test-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work|Out-Null
$journal=Join-Path $work 'journal.jsonl'
$start=[Diagnostics.ProcessStartInfo]::new((Get-Command pwsh).Source)
$start.UseShellExecute=$false;$start.CreateNoWindow=$true
$start.RedirectStandardInput=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
foreach($arg in @('-NoProfile','-File',(Join-Path $PSScriptRoot 'serve.ps1'),'-Mcp','-Journal',$journal)){$start.ArgumentList.Add($arg)}
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
    Assert 'four tools discoverable' ($names.Count -eq 4 -and @('codex_run','codex_check','codex_lens','codex_describe'|Where-Object{$_ -notin $names}).Count -eq 0)
    foreach($tool in $r.result.tools){Assert "schema $($tool.name)" ($tool.inputSchema.type -eq 'object' -and $tool.inputSchema.additionalProperties -eq $false -and $tool.outputSchema.required -contains 'status')}
    $r=Call 3 'resources/list'
    Assert 'language card discoverable' ($r.result.resources[0].uri -eq 'codex://accp/language')
    $r=Call 4 'resources/read' @{uri='codex://accp/language'}
    $card=$r.result.contents[0].text
    Assert 'language card contains complete source and failure rules' ($card -match 'Chapter: Count' -and $card -match 'print-line-uni' -and $card -match 'never computed answers')
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
    $before=Count-Records
    $r=Tool 12 'unknown_tool'
    Assert 'unknown tool is a protocol error' ($r.error.code -eq -32602)
    $r=Tool 13 'codex_run' @{source=$source;lease='forged'}
    Assert 'caller cannot add grant parameters' ($r.error.code -eq -32602)
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
} finally {
    if(-not $p.HasExited){$p.Kill($true)}
    $p.Dispose()
}
Write-Host "MCP focused proof: $pass pass, $fail fail. Artifacts: $work"
if($fail -gt 0){exit 1}
