[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('codex','claude')][string]$Client,
    [ValidateSet('count','recovery')][string]$Scenario='count',
    [string]$Executable='',
    [string]$ServerScript='',
    [string]$Model='',
    [switch]$WithoutInstructions,
    [string]$VerifyDirectory='',
    [int]$TimeoutSec=300
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
function Verify-Harness([string]$Directory){
    function Hash-Text([string]$Value){
        [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($Value))).ToLowerInvariant()
    }
    if([IO.File]::ReadAllText((Join-Path $Directory 'exit.txt')).Trim() -cne '0'){throw 'Client did not exit successfully'}
    $facts=@{};$previous='0'*64
    foreach($line in [IO.File]::ReadLines((Join-Path $Directory 'journal.jsonl'))){
        $j=$line|ConvertFrom-Json -AsHashtable
        $record=$line.Substring($line.IndexOf(',"record":')+10)
        $record=$record.Substring(0,$record.Length-1)
        if((Hash-Text $record) -cne $j.fact -or $j.record.previous -cne $previous){throw 'Journal hash chain mismatch'}
        $facts[$j.fact]=$j.record;$previous=$j.fact
    }
    $pending=@{};$runs=[Collections.Generic.List[object]]::new()
    foreach($line in [IO.File]::ReadLines((Join-Path $Directory 'protocol.jsonl'))){
        $w=$line|ConvertFrom-Json -AsHashtable;$m=$w.message|ConvertFrom-Json -AsHashtable
        if($w.direction -eq 'in' -and $m.method -eq 'tools/call' -and $m.params.name -eq 'codex_run'){
            $pending[[string]$m.id]=$m.params.arguments
        }elseif($w.direction -eq 'out' -and $m.ContainsKey('id') -and $pending.ContainsKey([string]$m.id)){
            $args=$pending[[string]$m.id];$pending.Remove([string]$m.id)
            $r=$m.result.structuredContent
            if(-not $r -or -not $facts.ContainsKey($r.fact)){throw 'Run result has no matching journal fact'}
            $record=$facts[$r.fact]
            $raw=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($m.result._meta.accp_response_base64))
            $signed=$raw|ConvertFrom-Json -AsHashtable
            if(($r|ConvertTo-Json -Depth 30 -Compress) -cne ($signed|ConvertTo-Json -Depth 30 -Compress)){throw 'Structured result differs from recorded response bytes'}
            $suffix=',"fact":"'+$r.fact+'"}'
            if(-not $raw.EndsWith($suffix,[StringComparison]::Ordinal)){throw 'Run response fact suffix mismatch'}
            $body=$raw.Substring(0,$raw.Length-$suffix.Length)+'}'
            if((Hash-Text $body) -cne $record.response_sha256 -or (Hash-Text $args.source) -cne $record.source_sha256){throw 'Run evidence hash mismatch'}
            $runs.Add(@{source=$args.source;result=$r})
        }
    }
    if($pending.Count -or -not $runs.Count){throw 'No completed codex_run evidence'}
    $eventSources=[Collections.Generic.List[string]]::new()
    foreach($line in [IO.File]::ReadLines((Join-Path $Directory 'events.jsonl'))){
        $e=$line|ConvertFrom-Json -AsHashtable
        if($Client -eq 'codex' -and $e.type -eq 'item.completed' -and $e.item.type -eq 'mcp_tool_call' -and $e.item.server -eq 'cobblestone' -and $e.item.tool -eq 'codex_run'){
            $eventSources.Add($e.item.arguments.source)
        }elseif($Client -eq 'claude' -and $e.type -eq 'assistant'){
            foreach($c in $e.message.content){if($c.type -eq 'tool_use' -and $c.name -eq 'mcp__cobblestone__codex_run'){$eventSources.Add($c.input.source)}}
        }
    }
    foreach($run in $runs){if(-not $eventSources.Contains($run.source)){throw 'Protocol call missing from client events'}}
    if($Scenario -eq 'count'){
        $counted=@($runs | Where-Object {$_.result.status -ceq 'ok' -and $_.result.stdout -ceq "3`n" -and $_.source.Contains('"strawberry"') -and $_.source.Contains('char-code-at') -and $_.source.Contains('text-length')})
        if(-not $counted.Count){throw 'No successful character-count computation; inspect source for input-derived work'}
    }else{
        $prompt=[IO.File]::ReadAllText((Join-Path $Directory 'prompt.txt')).Replace("`r",'')
        $original=$prompt.Substring($prompt.IndexOf('Chapter: Recovery')).TrimEnd()
        $first=$runs[0]
        if($first.source.Replace("`r",'').TrimEnd() -cne $original -or $first.result.status -cne 'diagnostics' -or ($first.result.diagnostics -join "`n") -notmatch 'CDX3002.*print-lien-uni'){throw 'Missing unchanged-source diagnostic'}
        $fixed=$original.Replace('print-lien-uni','print-line-uni')
        $recovered=@($runs | Select-Object -Skip 1 | Where-Object {$_.source.Replace("`r",'').TrimEnd() -ceq $fixed -and $_.result.status -ceq 'ok' -and $_.result.stdout -ceq "42`n"})
        if(-not $recovered.Count){throw 'Missing corrected execution returning 42'}
    }
    Write-Host "PASS $Client ${Scenario}: $($runs.Count) runs matched client events, source/response hashes and journal chain. Artifacts $Directory"
}
if($VerifyDirectory){Verify-Harness (Resolve-Path $VerifyDirectory).Path;return}
if(-not $ServerScript){$ServerScript=Join-Path $PSScriptRoot 'serve.ps1'}
$ServerScript=(Resolve-Path $ServerScript).Path
if(-not $Executable){$Executable=(Get-Command $Client -ErrorAction Stop).Source}
$name='accp-harness-'+[Guid]::NewGuid().ToString('N')
$work=Join-Path $PSScriptRoot ('build-output/'+$name)
$cwd=Join-Path ([IO.Path]::GetTempPath()) $name
New-Item -ItemType Directory -Path $work,$cwd|Out-Null
if(-not $WithoutInstructions){
    $guidance=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'agent-instructions.md'))
    [IO.File]::WriteAllText((Join-Path $cwd 'AGENTS.md'),$guidance,[Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $cwd 'CLAUDE.md'),$guidance,[Text.UTF8Encoding]::new($false))
}
$utf8=[Text.UTF8Encoding]::new($false)
$pwsh=(Get-Command pwsh).Source
$serverArgs=@('-NoProfile','-File',$ServerScript,'-Mcp','-Journal',(Join-Path $work 'journal.jsonl'),'-ProtocolLog',(Join-Path $work 'protocol.jsonl'))
$config=@{mcpServers=@{cobblestone=@{command=$pwsh;args=$serverArgs}}}
$configPath=Join-Path $work 'mcp.json'
$config|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $configPath -Encoding utf8
$prompt=if($Scenario -eq 'count'){
    'How many lowercase r characters occur in the exact text "strawberry"? Verify the answer and state the evidence.'
}else{
@'
Run the following program unchanged once. Use the resulting compiler diagnostic to correct the misspelled print routine, run the corrected program, and report its result. Do not edit files.

Chapter: Recovery
Section: Entry
  opening : [Console] Nothing = act
    print-lien-uni (show (6 * 7))
  end
Page 1
'@
}
[IO.File]::WriteAllText((Join-Path $work 'prompt.txt'),$prompt,$utf8)
$start=[Diagnostics.ProcessStartInfo]::new($Executable)
$start.WorkingDirectory=$cwd;$start.UseShellExecute=$false;$start.CreateNoWindow=$true
$start.RedirectStandardInput=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
if($Client -eq 'codex'){
    $clientArgs=@('exec','--json','--ephemeral','--skip-git-repo-check','--sandbox','read-only','-C',$cwd,
        '-c','approval_policy="never"',
        '-c',('mcp_servers.cobblestone.command='+($pwsh|ConvertTo-Json -Compress)),
        '-c',('mcp_servers.cobblestone.args='+($serverArgs|ConvertTo-Json -Compress)),
        '-c','mcp_servers.cobblestone.startup_timeout_sec=30',
        '-c','mcp_servers.cobblestone.tool_timeout_sec=30',
        '-c','mcp_servers.cobblestone.default_tools_approval_mode="approve"','-')
}else{
    $clientArgs=@('-p','--output-format','stream-json','--verbose','--no-session-persistence',
        '--strict-mcp-config','--mcp-config',$configPath,
        '--allowedTools','mcp__cobblestone__codex_run,mcp__cobblestone__codex_check,mcp__cobblestone__codex_lens,mcp__cobblestone__codex_describe',
        '--permission-prompts','none')
}
if($Model){$clientArgs+=@('--model',$Model)}
foreach($arg in $clientArgs){$start.ArgumentList.Add($arg)}
$process=[Diagnostics.Process]::Start($start)
Write-Host "$Client $Scenario PID $($process.Id); artifacts $work"
$out=[IO.File]::Create((Join-Path $work 'events.jsonl'))
$err=[IO.File]::Create((Join-Path $work 'stderr.log'))
try {
    $outTask=$process.StandardOutput.BaseStream.CopyToAsync($out)
    $errTask=$process.StandardError.BaseStream.CopyToAsync($err)
    $process.StandardInput.WriteLine($prompt);$process.StandardInput.Close()
    if(-not $process.WaitForExit($TimeoutSec*1000)){$process.Kill($true);throw "$Client acceptance timed out"}
    [void]$outTask.GetAwaiter().GetResult();[void]$errTask.GetAwaiter().GetResult()
    $code=$process.ExitCode
    $code|Set-Content (Join-Path $work 'exit.txt')
    Write-Host "$Client $Scenario exit $code; artifacts $work"
    if($code -ne 0){exit $code}
}finally{
    if(-not $process.HasExited){$process.Kill($true)}
    $process.Dispose();$out.Dispose();$err.Dispose()
    $full=[IO.Path]::GetFullPath($cwd);$parent=[IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if($full.StartsWith($parent,[StringComparison]::OrdinalIgnoreCase) -and (Split-Path $full -Leaf) -ceq $name){Remove-Item -LiteralPath $full -Recurse -Force}
}
Verify-Harness $work
