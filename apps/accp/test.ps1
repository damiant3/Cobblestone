[CmdletBinding()]
param([string]$Kernel='', [switch]$SkipProbeBuild)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$repo=(Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$bundle=Join-Path $PSScriptRoot 'build-output'
$work=Join-Path $bundle ('test-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
if(-not $Kernel){$Kernel=Join-Path $repo 'seed/Codex.cdx'}
$probe=Join-Path $bundle 'AccpProbe.exe'
if(-not $SkipProbeBuild){
    $free=(Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory/1MB
    if($free -le 1.5){throw 'Insufficient RAM for one compile guest'}
    & pwsh -NoProfile -File (Join-Path $repo 'build/compile.ps1') -Src (Join-Path $repo 'apps/accp/AccpProbe.codex') -Out (Join-Path $bundle 'AccpProbe.cdx') -Log (Join-Path $work 'probe-compile.log') -Kernel $Kernel -RawFlags hosted-windows
    if($LASTEXITCODE -ne 0){Get-Content (Join-Path $work 'probe-compile.log');throw 'Probe compile failed'}
    & pwsh -NoProfile -File (Join-Path $repo 'codex/plugs/pe/cdx-to-pe-console.ps1') -CdxInput (Join-Path $bundle 'AccpProbe.cdx') -Out $probe
    if($LASTEXITCODE -ne 0){throw 'Probe packaging failed'}
}
Add-Type -Path (Join-Path $PSScriptRoot 'HostIo.cs')
$utf8=[Text.UTF8Encoding]::new($false)
$pass=0; $fail=0
function Assert([string]$Name,[bool]$Ok,[string]$Detail='') {
    if($Ok){$script:pass++;Write-Host "PASS $Name"}
    else{$script:fail++;Write-Host "FAIL $Name $Detail"}
}
function Launch([string]$File,[string[]]$Arguments){
    $s=[Diagnostics.ProcessStartInfo]::new($File)
    $s.UseShellExecute=$false;$s.CreateNoWindow=$true
    $s.RedirectStandardInput=$true;$s.RedirectStandardOutput=$true;$s.RedirectStandardError=$true
    foreach($a in $Arguments){$s.ArgumentList.Add($a)}
    $p=[Diagnostics.Process]::Start($s);$p.StandardInput.AutoFlush=$true
    @{process=$p;errors=$p.StandardError.ReadToEndAsync()}
}
function AskRaw($Service,[string]$Line){
    $Service.process.StandardInput.WriteLine([AccpHost.Wire]::Ascii($Line))
    $read=$Service.process.StandardOutput.ReadLineAsync()
    if(-not $read.Wait(40000)){throw 'test response timeout'}
    $text=$read.GetAwaiter().GetResult()
    if(-not $text){throw "service closed its response pipe: $($Service.errors.GetAwaiter().GetResult())"}
    [IO.File]::AppendAllText((Join-Path $work 'responses.jsonl'),$text+"`n",$utf8)
    $text|ConvertFrom-Json
}
function Ask($Service,$Request){AskRaw $Service ($Request|ConvertTo-Json -Depth 10 -Compress)}
function Finish($Service,[string]$Name){
    $Service.process.StandardInput.Close()
    if(-not $Service.process.WaitForExit(10000)){$Service.process.Kill($true);throw "$Name did not stop"}
    $err=$Service.errors.GetAwaiter().GetResult()
    [IO.File]::WriteAllText((Join-Path $work ($Name+'.stderr')),$err,$utf8)
    Assert "$Name exits cleanly" ($Service.process.ExitCode -eq 0) $err
    $Service.process.Dispose()
    return $err
}
function Source([string]$Body){"Chapter: AccpFixture`nSection: Entry`n$Body`nPage 1`n"}

$service=$null;$unit=$null
try {
    $unit=Launch $probe @()
    $entry='(def "opening" "Fixture" (params) int-default (int 42 int-default) 0 0)'
    $ir='(chapter "Fixture" (defs '+$entry+'))'
    $irCases=@(
        @('pure entry',$ir,$true),
        @('missing entry','(chapter "X" (defs))',$false),
        @('duplicate entry',('(chapter "X" (defs '+$entry+' '+$entry+'))'),$false),
        @('forged entry in string','(chapter "X" (prose "(defs (def \"opening\"))") (defs))',$false),
        @('parameterized entry',$ir.Replace('(params)','(params ("x" int-default))'),$false),
        @('unresolved type',$ir.Replace('int-default (int','(tvar 9) (int'),$false),
        @('function return',$ir.Replace('int-default (int','(fn int-default int-default) (int'),$false),
        @('trailing form',$ir+' (chapter "Y" (defs))',$false),
        @('truncated IR',$ir.Substring(0,$ir.Length-1),$false),
        @('scope count mismatch',$ir.Replace('int-default (int','(effectful (effs "Console") (scopes) nothing) (int'),$false),
        @('duplicate effect',$ir.Replace('int-default (int','(effectful (effs "Console" "Console") (scopes "" "") nothing) (int'),$false)
    )
    foreach($case in $irCases){
        $r=Ask $unit @{op='ir';text=$case[1]};Assert $case[0] ($r.ok -eq $case[2]) ($r|ConvertTo-Json -Compress)
        if($case[0] -eq 'duplicate entry'){Assert 'duplicate fixture reaches entry guard' ($r.reason -eq 'duplicate opening')}
    }
    $watCases=@(
        @('no imports','(module (func (export "_start")))',$true),
        @('valid console','(module (import "wasi_snapshot_preview1" "fd_write" (func (param i32 i32 i32 i32) (result i32))) (func (export "_start")))',$true),
        @('forbidden network import','(module (import "env" "network" (func)) (func (export "_start")))',$false),
        @('wrong WASI signature','(module (import "wasi_snapshot_preview1" "fd_write" (func)) (func (export "_start")))',$false),
        @('forbidden memory import','(module (import "env" "memory" (memory 1)) (func (export "_start")))',$false),
        @('extra WASI capability','(module (import "wasi_snapshot_preview1" "random_get" (func (param i32 i32) (result i32))) (func (export "_start")))',$false)
    )
    foreach($case in $watCases){
        $wat=Join-Path $work 'import.wat';$wasm=Join-Path $work 'import.wasm'
        [IO.File]::WriteAllText($wat,$case[1],$utf8)
        & wat2wasm $wat -o $wasm
        if($LASTEXITCODE -ne 0){throw 'Import fixture assembly failed'}
        $r=Ask $unit @{op='wasm';text=[Convert]::ToBase64String([IO.File]::ReadAllBytes($wasm))}
        Assert $case[0] ($r -eq $case[2])
    }
    $unitErr=Finish $unit 'unit';$unit=$null

    $journal=Join-Path $work 'runs.jsonl'
    $service=Launch (Get-Command pwsh).Source @('-NoProfile','-File',(Join-Path $PSScriptRoot 'serve.ps1'),'-Journal',$journal)
    $d=Ask $service @{op='describe';id='describe'}
    Assert 'describe identifies Codex conduit and modules' ($d.status -eq 'ok' -and $d.conduit.Length -eq 64 -and $d.kernel.Length -eq 64 -and $d.evidence -eq 'artifact-digests-only')
    Write-Host "compiler SHA256 $($d.kernel)"
    Write-Host "lens SHA256 $($d.lens_module)"
    $strawberry=Source @'
  count-of : Text, Integer, Integer, Integer -> Integer
  count-of (s) (want) (i) (acc) =
    if i >= text-length s then acc
    else if char-code-at s i == want then count-of s want (i + 1) (acc + 1)
    else count-of s want (i + 1) acc
  opening : [Console] Nothing = act
    print-line-uni (show (count-of "strawberry" (char-code-at "r" 0) 0 0))
  end
'@
    $r=Ask $service @{op='run';source=$strawberry;id='strawberry'}
    Assert 'strawberry returns 3' ($r.status -eq 'ok' -and $r.stdout -ceq "3`n") ($r|ConvertTo-Json -Compress)
    $r=Ask $service @{op='run';source=$strawberry.Replace('"strawberry"','"raspberry refrigerator"');id='raspberry'}
    Assert 'raspberry refrigerator returns 7' ($r.status -eq 'ok' -and $r.stdout -ceq "7`n") ($r|ConvertTo-Json -Compress)
    $pure=Source '  opening : Integer = 42'
    $r=Ask $service @{op='run';source=$pure;id='pure'}
    Assert 'pure result' ($r.status -eq 'ok' -and $r.stdout -ceq "42`n")
    $write=Source "  opening : [Console.Write] Nothing = act`n    print-line-uni `"buffered`"`n  end"
    $r=Ask $service @{op='run';source=$write}
    Assert 'directional Console.Write' ($r.status -eq 'ok' -and $r.stdout -ceq "buffered`n") ($r|ConvertTo-Json -Compress)
    $read=Source @'
  Maybe a = | Just (a) | None
  opening : [Console.Read, Console.Write] Nothing = act
    line <- read-line
    print-line-uni (when line is Just (s) -> s is None -> "EOF")
  end
'@
    foreach($inputText in @("first`n","second`n",'')){
        $r=Ask $service @{op='run';source=$read;input=$inputText;id='input'}
        $expected=if($inputText){$inputText}else{"EOF`n"}
        Assert "input isolation [$($inputText.Trim())]" ($r.status -eq 'ok' -and $r.stdout -ceq $expected) ($r|ConvertTo-Json -Compress)
    }
    $network=Source "  opening : [Console, Network.Read] Nothing = act`n    value <- net-status`n    print-line-uni (show value)`n  end"
    $r=Ask $service @{op='check';source=$network}
    Assert 'check reports nonambient effect' ($r.status -eq 'ok' -and $r.outside_ambient -contains 'Network.Read')
    $r=Ask $service @{op='run';source=$network}
    Assert 'network refused before lens and assembly' ($r.status -eq 'refused' -and $r.needs -contains 'Network.Read' -and $r.ms.lens -eq 0 -and $r.ms.assemble -eq 0)
    $expiredLease='02000000070100000000200000003d4017c3e843895a92b70aa74d1b7ebc9c982ccf2ec4968cc0cd55f12af4660c20000000d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a20000000d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4100000003611170d2d1e130e0d1a413515110e0d010f00000051230f220d5114101a0d1b10152251010f00000051230f220d5114101a0d1b10152251000000008403000000000000dc05000000000000200000000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f200000000000000000000000004000000099ea6c15c5bca2a0a0bc6369b7b2da0056eb3c14892093f3dc694c4977c1603f818fc6520ab8060ebc2731c29e575e3904725b5b46ff93bd5f506932551e280b'
    $r=Ask $service @{op='run';source=$network;lease=$expiredLease}
    Assert 'a lease on an unbound instance is refused' ($r.status -eq 'refused' -and $r.stage -eq 'admission' -and $r.reason -match 'not bound to a requester') ($r|ConvertTo-Json -Compress)
    $r=Ask $service @{op='check';source=$network;lease=$expiredLease}
    Assert 'a lease on check is refused at the request' ($r.status -eq 'refused' -and $r.stage -eq 'request' -and $r.reason -match 'only valid for run') ($r|ConvertTo-Json -Compress)
    $r=Ask $service @{op='run';source=$pure;lease=$expiredLease}
    Assert 'a lease on an ambient-only run is refused' ($r.status -eq 'refused' -and $r.reason -match 'needs none') ($r|ConvertTo-Json -Compress)
    $bad=Source "  hidden : Integer -> Integer`n  hidden (x) = net-status`n  opening : [Console] Nothing = act`n    print-line-uni (show (hidden 0))`n  end"
    $r=Ask $service @{op='run';source=$bad}
    Assert 'undeclared network use is CDX2031' ($r.status -eq 'diagnostics' -and (($r.diagnostics -join ' ') -match 'CDX2031')) ($r|ConvertTo-Json -Compress)
    $memory=Source "  touch : Integer -> Integer`n  touch (x) = let w = poke-byte 20536 0 42 in peek-byte 20536 0`n  opening : [Console] Nothing = act`n    print-line-uni (show (touch 0))`n  end"
    $r=Ask $service @{op='run';source=$memory}
    Assert 'raw memory stays in linear memory' ($r.status -eq 'ok' -and $r.stdout -ceq "42`n") ($r|ConvertTo-Json -Compress)
    $r=Ask $service @{op='lens';plug='wasm';source=$pure}
    Assert 'WASM lens is live' ($r.status -eq 'ok' -and $r.wat.Contains('(module'))
    foreach($raw in @('{"op":"describe","op":"run"}','{"op":"describe","unknown":1}','{"op":"describe","lease":"x"}','{"op":"describe","limits":{"ms":0}}','{"op":"describe","limits":{"ms":10001}}','{"op":"describe","limits":{"ms":2000,"ms":1}}','{"op":"describe"} trailing','{"op":"describe","limits":{"stdout_bytes":-1}}')){
        $r=AskRaw $service $raw;Assert "request refusal $raw" ($r.status -eq 'refused') ($r|ConvertTo-Json -Compress)
    }
    $r=Ask $service @{op='run';source=($pure+[char]0)}
    Assert 'source NUL refused' ($r.status -eq 'refused' -and $r.ms.compile -eq 0)
    $r=Ask $service @{op='unknown';id='refusal-id'}
    Assert 'request refusal preserves a valid id' ($r.status -eq 'refused' -and $r.id -eq 'refusal-id')
    $r=Ask $service @{op='challenge'}
    Assert 'a challenge on an instance without request binding is refused' ($r.status -eq 'refused' -and $r.reason -match 'request-bound') ($r|ConvertTo-Json -Compress)
    $r=Ask $service @{op='run';source=$pure;requester=('ab'*32)}
    Assert 'request authentication fields without request binding are refused' ($r.status -eq 'refused' -and $r.stage -eq 'request' -and $r.reason -match 'only valid on a request-bound instance') ($r|ConvertTo-Json -Compress)
    $r=Ask $service @{op='describe';nonce=('ab'*32)}
    Assert 'a signed describe is refused' ($r.status -eq 'refused' -and $r.reason -ceq 'describe is not signed') ($r|ConvertTo-Json -Compress)
    $r=Ask $service @{op='policy';question='dead-rules'}
    Assert 'a policy question on an unbound instance is refused' ($r.status -eq 'refused' -and $r.stage -eq 'policy' -and $r.reason -match 'not bound to a grantor set') ($r|ConvertTo-Json -Compress)
    foreach($case in @(
        @(@{op='policy';question='prove'},'question must be'),
        @(@{op='policy';question='dead-rules';source=$pure},'policy takes no program'),
        @(@{op='policy';question='can-allow'},'label is required'),
        @(@{op='policy';question='dead-rules';label='Network.Read'},'label is required'),
        @(@{op='policy';question='widens'},'policy is required'),
        @(@{op='check';source=$pure;question='dead-rules'},'only valid for the policy op')
    )){
        $r=Ask $service $case[0]
        Assert "policy request refusal: $($case[1])" ($r.status -eq 'refused' -and $r.stage -eq 'request' -and $r.reason -match $case[1]) ($r|ConvertTo-Json -Compress)
    }
    $r=AskRaw $service '{"\u006fp":"describe","op":"run"}'
    Assert 'escaped duplicate property refused' ($r.status -eq 'refused')
    $r=Ask $service @{op='run';source=('x'*65537)}
    Assert 'source ceiling' ($r.status -eq 'refused' -and $r.ms.compile -eq 0)
    $r=AskRaw $service (' '*262145)
    Assert 'oversized frame refused and drained' ($r.status -eq 'refused')
    $loop=Source "  spin : Integer -> Integer`n  spin (x) = spin x`n  opening : [Console] Nothing = act`n    print-line-uni `"entered`"`n    print-line-uni (show (spin 1))`n  end"
    $r=Ask $service @{op='run';source=$loop;limits=@{ms=1000}}
    Assert 'nonterminating program times out after reaching its body' ($r.status -eq 'timeout' -and $r.stage -eq 'run' -and $r.stdout -ceq "entered`n") ($r|ConvertTo-Json -Compress)
    $r=Ask $service @{op='run';source=$pure}
    Assert 'completing control after timeout' ($r.status -eq 'ok' -and $r.stdout -ceq "42`n")
    $flood=Source "  flood : Integer -> [Console] Nothing`n  flood (x) = act`n    print-line-uni `"01234567890123456789`"`n    flood x`n  end`n  opening : [Console] Nothing = flood 0"
    $r=Ask $service @{op='run';source=$flood;limits=@{stdout_bytes=128;ms=2000}}
    Assert 'output flood hits capture ceiling' ($r.status -eq 'trapped' -and $r.reason -match 'output_bytes') ($r|ConvertTo-Json -Compress)
    $alloc=Source '  opening : Integer = alloc-bytes 134217728'
    $r=Ask $service @{op='run';source=$alloc}
    Assert 'program memory ceiling' ($r.status -eq 'trapped' -and $r.reason -match 'grow.*memory|memory.*grow') ($r|ConvertTo-Json -Compress)
    $partial=Source "  opening : [Console] Nothing = act`n    print-line-uni `"before`"`n    let p = alloc-bytes 134217728`n    in print-line-uni (show p)`n  end"
    $r=Ask $service @{op='run';source=$partial}
    Assert 'trap preserves captured output and row' ($r.status -eq 'trapped' -and $r.stdout -ceq "before`n" -and $r.row -contains 'Console' -and $r.output_sha256.Length -eq 64) ($r|ConvertTo-Json -Compress)
    $warm=@()
    for($i=0;$i -lt 12;$i++){
        $r=Ask $service @{op='run';source=$pure;id="repeat-$i"}
        Assert "repeat $i" ($r.status -eq 'ok' -and $r.id -eq "repeat-$i" -and $r.stdout -ceq "42`n")
        $warm+=[long]$r.ms.total
    }
    $service.process.StandardInput.WriteLine((@{op='run';source=$pure;id='queued-a'}|ConvertTo-Json -Compress))
    $service.process.StandardInput.WriteLine((@{op='run';source=$pure.Replace('42','17');id='queued-b'}|ConvertTo-Json -Compress))
    $qa=$service.process.StandardOutput.ReadLine()|ConvertFrom-Json
    $qb=$service.process.StandardOutput.ReadLine()|ConvertFrom-Json
    Assert 'pipelined requests stay separate' ($qa.id -eq 'queued-a' -and $qb.id -eq 'queued-b' -and $qa.stdout -ceq "42`n" -and $qb.stdout -ceq "17`n")
    $err=Finish $service 'service';$service=$null
    $heaps=@([regex]::Matches($err,'heap_bytes=(\d+)')|ForEach-Object{[long]$_.Groups[1].Value})
    $repeatHeaps=@($heaps|Select-Object -Last 14|Select-Object -First 12)
    $spread=($repeatHeaps|Measure-Object -Maximum).Maximum-($repeatHeaps|Measure-Object -Minimum).Minimum
    Assert 'request heap usage plateaus' ($repeatHeaps.Count -eq 12 -and $spread -lt 4096) "spread=$spread"
    $starts=@([regex]::Matches($err,'heap_start=(\d+)')|ForEach-Object{$_.Groups[1].Value})
    Assert 'heap checkpoint is restored after every request' (@($starts|Sort-Object -Unique).Count -eq 1) ($starts -join ',')
    $rss=@([regex]::Matches($err,'conduit_rss_bytes=(\d+)')|ForEach-Object{[long]$_.Groups[1].Value})
    Write-Host "conduit RSS bytes initial=$($rss[0]) final=$($rss[-1]); request heap spread=$spread"
    Write-Host "warm latency ms min=$(( $warm|Measure-Object -Minimum).Minimum) max=$(( $warm|Measure-Object -Maximum).Maximum)"
    $previous='0'*64;$records=0;$allHashes=$true;$inputHashes=@();$responseHashes=@{};$recordedNeeds=$false
    foreach($line in [IO.File]::ReadLines($journal)){
        $j=[Text.Json.JsonDocument]::Parse($line)
        try {
            $record=$j.RootElement.GetProperty('record').GetRawText()
            $hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($utf8.GetBytes($record))).ToLowerInvariant()
            $fact=$j.RootElement.GetProperty('fact').GetString()
            if($hash -cne $fact -or $j.RootElement.GetProperty('record').GetProperty('previous').GetString() -cne $previous){$allHashes=$false}
            $previous=$fact;$records++
            $inputHashes+=$j.RootElement.GetProperty('record').GetProperty('input_sha256').GetString()
            $responseHashes[$fact]=$j.RootElement.GetProperty('record').GetProperty('response_sha256').GetString()
            $recordObject=$record|ConvertFrom-Json
            if($recordObject.needs -contains 'Network.Read' -and $recordObject.row -contains 'Network.Read'){$recordedNeeds=$true}
        } finally {$j.Dispose()}
    }
    Assert 'journal chain hashes recompute' ($allHashes -and $records -gt 30)
    Assert 'input bytes affect journal identity' (@($inputHashes|Sort-Object -Unique).Count -ge 3)
    Assert 'journal retains the refused capability and row' $recordedNeeds
    $responseCount=0;$responseOk=$true
    foreach($line in [IO.File]::ReadLines((Join-Path $work 'responses.jsonl'))){
        $cut=$line.LastIndexOf(',"fact":')
        if($cut -lt 0){continue}
        $v=$line|ConvertFrom-Json
        if(-not $responseHashes.ContainsKey($v.fact)){continue}
        $original=$line.Substring(0,$cut)+'}'
        $hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($utf8.GetBytes($original))).ToLowerInvariant()
        if($hash -cne $responseHashes[$v.fact]){$responseOk=$false}
        $responseCount++
    }
    Assert 'response hashes verify from received wire bytes' ($responseOk -and $responseCount -gt 30)
    $corePid=[regex]::Match($err,'conduit PID (\d+)').Groups[1].Value
    Assert 'shutdown removes conduit' (-not (Get-Process -Id ([int]$corePid) -ErrorAction SilentlyContinue))
    $stall=[AccpHost.Child]::Run((Get-Command pwsh).Source,@('-NoProfile','-Command','Start-Sleep -Seconds 30'),[byte[]]@(),150,1024,2GB)
    Assert 'assembler process boundary interrupts a stalled child' ($stall.Limit -eq 'ms' -and $stall.Ms -lt 2000)
    $fill=[AccpHost.Child]::Run((Get-Command pwsh).Source,@('-NoProfile','-Command','[Console]::Write("x" * 100000)'),[byte[]]@(),3000,1024,2GB)
    Assert 'intermediate capture limit stops a producer' ($fill.Limit -eq 'output_bytes' -and $fill.Output.Length -le 1024)

    $compiled=[AccpHost.Child]::Run((Get-Command wasmtime).Source,@('run','-W','timeout=5s,max-wasm-stack=16777216,max-memory-size=536870912',(Join-Path $bundle 'codex-compiler.wasm')),$utf8.GetBytes("IR-UNI decks=12`n"+$network+[char]0),10000,16MB,2GB)
    $irMatch=[regex]::Match($utf8.GetString($compiled.Output),'(?ms)^IR-BEGIN\r?\n(.*?)^IR-END\r?$')
    if($compiled.ExitCode -ne 0 -or -not $irMatch.Success){throw 'Network bypass control did not compile'}
    $lens=[AccpHost.Child]::Run((Get-Command wasmtime).Source,@('run','-W','timeout=5s,max-wasm-stack=16777216,max-memory-size=536870912',(Join-Path $bundle 'wasm-stdio.wasm')),$utf8.GetBytes($irMatch.Groups[1].Value+[char]0),10000,16MB,2GB)
    if($lens.ExitCode -ne 0){throw 'Network bypass control lens failed'}
    $uncheckedWat=Join-Path $work 'unchecked.wat';$uncheckedWasm=Join-Path $work 'unchecked.wasm'
    [IO.File]::WriteAllBytes($uncheckedWat,$lens.Output)
    & wat2wasm --enable-tail-call $uncheckedWat -o $uncheckedWasm
    if($LASTEXITCODE -ne 0){throw 'Network bypass control assembly failed'}
    $unchecked=[AccpHost.Child]::Run((Get-Command wasmtime).Source,@('run','-W','timeout=1s,max-memory-size=67108864',$uncheckedWasm),[byte[]]@(),3000,65536,2GB)
    Assert 'removing admission reaches a network trap, not success' ($unchecked.ExitCode -ne 0 -and $utf8.GetString($unchecked.Error) -match 'unreachable') ($utf8.GetString($unchecked.Error))

    $fullJournal=Join-Path $work 'full.jsonl'
    $service=Launch (Get-Command pwsh).Source @('-NoProfile','-File',(Join-Path $PSScriptRoot 'serve.ps1'),'-Journal',$fullJournal,'-JournalBytes','1048576')
    $first=Ask $service @{op='describe'}
    $full=Ask $service @{op='run';source=$pure}
    Assert 'full journal refuses execution' ($first.status -eq 'ok' -and $full.status -eq 'error' -and $full.stage -eq 'audit' -and $full.reason -match 'journal_bytes') ($full|ConvertTo-Json -Compress)
    $service.process.StandardInput.Close();$service.process.WaitForExit()
    Assert 'audit failure stops serving' ($service.process.ExitCode -ne 0 -and @([IO.File]::ReadLines($fullJournal)).Count -eq 1)
    [IO.File]::WriteAllText((Join-Path $work 'full.stderr'),$service.errors.GetAwaiter().GetResult(),$utf8)
    $service.process.Dispose();$service=$null

    $service=Launch (Get-Command pwsh).Source @('-NoProfile','-File',(Join-Path $PSScriptRoot 'serve.ps1'),'-Journal',$work)
    $line=$service.process.StandardOutput.ReadLineAsync()
    if(-not $line.Wait(10000)){throw 'Unwritable journal startup hung'}
    $service.process.WaitForExit()
    $denied=$line.Result|ConvertFrom-Json
    Assert 'unwritable journal prevents startup' ($denied.status -eq 'error' -and $service.process.ExitCode -ne 0)
    $service.process.Dispose();$service=$null

    $tamper=Join-Path $work 'tamper';New-Item -ItemType Directory -Path $tamper|Out-Null
    foreach($name in @('manifest.json','Accp.cdx','Accp.exe','codex-compiler.wasm','wasm-stdio.wasm')){Copy-Item -LiteralPath (Join-Path $bundle $name) -Destination (Join-Path $tamper $name)}
    $badModule=Join-Path $tamper 'codex-compiler.wasm'
    $bytes=[IO.File]::ReadAllBytes($badModule);$bytes[0]=1;[IO.File]::WriteAllBytes($badModule,$bytes)
    $service=Launch (Get-Command pwsh).Source @('-NoProfile','-File',(Join-Path $PSScriptRoot 'serve.ps1'),'-Bundle',$tamper,'-Journal',(Join-Path $work 'tamper.jsonl'))
    if(-not $service.process.WaitForExit(10000)){throw 'Tampered startup hung'}
    $tamperError=$service.errors.GetAwaiter().GetResult()
    Assert 'artifact tampering fails digest validation' ($service.process.ExitCode -ne 0 -and $tamperError -match 'digest mismatch') $tamperError
    $service.process.Dispose();$service=$null

    $requesterKey='3d4017c3e843895a92b70aa74d1b7ebc9c982ccf2ec4968cc0cd55f12af4660c';$grantorA='d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a';$grantorB='c3'*32;$identityKey='d4'*32
    $boundJournal=Join-Path $work 'bound.jsonl'
    $service=Launch (Get-Command pwsh).Source @('-NoProfile','-File',(Join-Path $PSScriptRoot 'serve.ps1'),'-Journal',$boundJournal,'-Requester',$requesterKey,'-Grantors',"$grantorB,$grantorA",'-Identity',$identityKey,'-MaxLeaseSeconds','900')
    $d=Ask $service @{op='describe'}
    $grantorHash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::ASCII.GetBytes((@($grantorA,$grantorB)|Sort-Object -CaseSensitive) -join ','))).ToLowerInvariant()
    Assert 'describe reports the instance binding' ($d.binding.transport -ceq 'stdio' -and $d.binding.requester -ceq $requesterKey -and $d.binding.identity -ceq $identityKey -and $d.binding.grantors_sha256 -ceq $grantorHash -and $d.binding.grantor_count -eq 2 -and $d.binding.max_lease_seconds -eq 900) ($d.binding|ConvertTo-Json -Compress)
    $r=Ask $service @{op='run';source=$network;lease=$expiredLease}
    Assert 'a signed lease outside its window reaches the window check' ($r.status -eq 'refused' -and $r.stage -eq 'admission' -and $r.reason -ceq 'lease is not current' -and $r.lease.lease_id -ceq '0701' -and $r.lease.grantor -ceq $grantorA -and $r.lease.holder -ceq $requesterKey -and $r.needs -contains 'Network.Read') ($r|ConvertTo-Json -Compress)
    $r=Ask $service @{op='run';source=$network;lease='zz'}
    Assert 'a lease that is not hex is refused' ($r.status -eq 'refused' -and $r.reason -ceq 'lease is not lowercase hex') ($r|ConvertTo-Json -Compress)
    $r=Ask $service @{op='run';source=$network;lease='00'}
    Assert 'a lease that is not a grant encoding is refused' ($r.status -eq 'refused' -and $r.reason -ceq 'lease is not a canonical grant encoding') ($r|ConvertTo-Json -Compress)
    $boundErr=Finish $service 'bound';$service=$null
    $boundFacts=@([IO.File]::ReadLines($boundJournal)|ForEach-Object {($_|ConvertFrom-Json).record})
    $boundFact=$boundFacts[0]
    Assert 'the fact records the bound requester' ($boundFact.binding.requester -ceq $requesterKey -and $boundFact.binding.grantors_sha256 -ceq $grantorHash)
    Assert 'the fact records the presented lease' ($boundFacts[1].grant.lease.lease_id -ceq '0701' -and $boundFacts[1].grant.lease.grantor -ceq $grantorA -and $boundFacts[1].reason -ceq 'lease is not current') ($boundFacts[1]|ConvertTo-Json -Compress -Depth 6)
    $leases=(& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'mint-test-lease.ps1') -Work $work -Kernel $Kernel | Select-Object -Last 1) | ConvertFrom-Json
    if($LASTEXITCODE -ne 0 -or -not $leases.allow){throw 'Lease mint failed'}
    $allowPolicy=$leases.allow_policy;$denyPolicy=$leases.deny_policy;$allowHash=$leases.allow_sha256;$bookFile=$leases.name_book;$minted=@{allow=$leases.allow;deny=$leases.deny;flow=$leases.flow;quota=$leases.quota};$quotaPolicy=$leases.quota_policy
    foreach($held in @(@('allow',$allowPolicy),@('deny',$denyPolicy))){
        $heldJournal=Join-Path $work ($held[0]+'-policy.jsonl')
        $service=Launch (Get-Command pwsh).Source @('-NoProfile','-File',(Join-Path $PSScriptRoot 'serve.ps1'),'-Journal',$heldJournal,'-Requester',$requesterKey,'-Grantors',"$grantorB,$grantorA",'-Identity',$identityKey,'-MaxLeaseSeconds','900','-Policy',$held[1],'-NameBook',$bookFile)
        $r=Ask $service @{op='run';source=$network;lease=$minted[$held[0]]}
        if($held[0] -eq 'allow'){
            Assert 'a current lease under a policy that allows passes admission' ($r.stage -ne 'admission' -and $r.lease.lease_id -ceq '0702' -and $r.lease.policy_sha256 -ceq $allowHash) ($r|ConvertTo-Json -Compress)
            $r=Ask $service @{op='policy';question='can-allow';label='Network.Read'}
            Assert 'the held policy can allow Network.Read, witnessed for the agent its book names' ($r.status -eq 'ok' -and $r.holds -eq $true -and $r.witness.capability -ceq 'Network.Read' -and $r.witness.agent_id -ceq $requesterKey -and $r.policy_sha256 -ceq $allowHash) ($r|ConvertTo-Json -Compress)
            $r=Ask $service @{op='policy';question='can-allow';label='Network.Write'}
            Assert 'the held policy can never allow Network.Write' ($r.status -eq 'ok' -and $r.holds -eq $false -and $null -eq $r.witness -and $r.evaluations -gt 0) ($r|ConvertTo-Json -Compress)
            $r=Ask $service @{op='policy';question='dead-rules'}
            Assert 'the allowing policy has no dead rule' ($r.status -eq 'ok' -and @($r.dead).Count -eq 0 -and $r.evaluations -gt 0) ($r|ConvertTo-Json -Compress)
            $wider=([IO.File]::ReadAllText($held[1])).TrimEnd()+"`n`"Jake's agent`" is permitted to use Network.Write."
            $r=Ask $service @{op='policy';question='widens';policy=$wider}
            Assert 'a proposed policy adding Network.Write widens the held one' ($r.status -eq 'ok' -and $r.holds -eq $true -and $r.witness.capability -ceq 'Network.Write') ($r|ConvertTo-Json -Compress)
            $r=Ask $service @{op='policy';question='widens';policy=[IO.File]::ReadAllText($denyPolicy)}
            Assert 'a proposed policy that denies does not widen the held one' ($r.status -eq 'ok' -and $r.holds -eq $false) ($r|ConvertTo-Json -Compress)
        } else {
            Assert 'a current lease under a policy that denies is refused with the policy reason' ($r.status -eq 'refused' -and $r.stage -eq 'admission' -and $r.reason -ceq 'the held policy does not allow Network.Read now: denied by policy') ($r|ConvertTo-Json -Compress)
            $r=Ask $service @{op='policy';question='dead-rules'}
            Assert 'the denying policy reports its shadowed grant as dead' ($r.status -eq 'ok' -and @($r.dead).Count -eq 1) ($r|ConvertTo-Json -Compress)
        }
        $heldErr=Finish $service ($held[0]+'-policy');$service=$null
        $heldFact=(@([IO.File]::ReadLines($heldJournal))[0]|ConvertFrom-Json).record
        Assert "the $($held[0]) fact records the lease and the held policy" ($heldFact.grant.lease.lease_id -ceq '0702' -and $heldFact.binding.policy_sha256.Length -eq 64)
    }
    $flowJournal=Join-Path $work 'flow-policy.jsonl'
    $service=Launch (Get-Command pwsh).Source @('-NoProfile','-File',(Join-Path $PSScriptRoot 'serve.ps1'),'-Journal',$flowJournal,'-Requester',$requesterKey,'-Grantors',"$grantorB,$grantorA",'-Identity',$identityKey,'-MaxLeaseSeconds','900','-Policy',$allowPolicy,'-NameBook',$bookFile)
    $r=Ask $service @{op='run';source=$network;lease=$minted.flow}
    Assert 'a lease forbidding a flow the row holds both ends of is refused at stage flow' ($r.status -eq 'refused' -and $r.stage -eq 'flow' -and @($r.flow).Count -eq 1 -and $r.flow[0].source -ceq 'Network.Read' -and $r.flow[0].sink -ceq 'Console' -and $r.lease.lease_id -ceq '0702') ($r|ConvertTo-Json -Compress -Depth 6)
    Assert 'the flow refusal carries both analyses, each finding the path, for a program printing what it read' ($r.analysis.verdict -ceq 'path' -and @($r.analysis.reach).Count -eq 1 -and $r.analysis.reach[0].sink -ceq 'Console.Write' -and $r.analysis_second -ceq 'path: 0') ($r|ConvertTo-Json -Compress -Depth 6)
    $r=Ask $service @{op='run';source=(Source "  opening : [Console, Network.Read] Nothing = act`n    value <- net-status`n    print-line-uni `"constant`"`n  end");lease=$minted.flow}
    Assert 'a program holding both ends with no path in both analyses passes the flow stage, carrying both verdicts' ($r.stage -ne 'flow' -and $r.status -ne 'refused' -and $r.analysis.verdict -ceq 'no path' -and $r.analysis_second -ceq 'no path' -and $r.lease.lease_id -ceq '0702') ($r|ConvertTo-Json -Compress -Depth 6)
    $r=Ask $service @{op='run';source=(Source "  pass : Integer -> Integer`n  pass (n) = if n < 0 then pass (n + 1) else n`n  opening : [Console, Network.Read] Nothing = act`n    value <- net-status`n    let a = pass value`n    in print-line-uni (show (pass 3))`n  end");lease=$minted.flow}
    Assert 'a program the two analyses disagree on (first path, second no path) stays refused at stage flow naming both' ($r.status -eq 'refused' -and $r.stage -eq 'flow' -and $r.analysis.verdict -ceq 'path' -and $r.analysis_second -ceq 'no path') ($r|ConvertTo-Json -Compress -Depth 6)
    $flowErr=Finish $service 'flow-policy';$service=$null
    $flowFact=(@([IO.File]::ReadLines($flowJournal))[0]|ConvertFrom-Json).record
    Assert 'the flow fact records the stage and the forbidden pair' ($flowFact.stage -ceq 'flow' -and @($flowFact.flow).Count -eq 1 -and $flowFact.flow[0].sink -ceq 'Console' -and $flowFact.analysis.verdict -ceq 'path') ($flowFact|ConvertTo-Json -Compress -Depth 6)
    $rawJournal=Join-Path $work 'flow-raw.jsonl'
    $service=Launch (Get-Command pwsh).Source @('-NoProfile','-File',(Join-Path $PSScriptRoot 'serve.ps1'),'-Journal',$rawJournal,'-Requester',$requesterKey,'-Grantors',"$grantorB,$grantorA",'-Identity',$identityKey,'-MaxLeaseSeconds','900','-Policy',$allowPolicy,'-NameBook',$bookFile)
    $r=Ask $service @{op='run';source=(Source "  opening : [Network.Read] Integer = act`n    value <- net-status`n    peek-byte (alloc-bytes 8) value`n  end");lease=$minted.flow}
    Assert 'a program naming raw memory under a flow rule is refused at stage flow' ($r.status -eq 'refused' -and $r.stage -eq 'flow' -and @($r.needs) -contains 'no raw memory under a flow rule' -and @($r.raw).Count -eq 1 -and $r.raw[0] -ceq 'peek-byte' -and $r.lease.lease_id -ceq '0702') ($r|ConvertTo-Json -Compress -Depth 6)
    $r=Ask $service @{op='run';source=(Source "  opening : [Network.Read] Integer = act`n    value <- net-status`n    value`n  end");lease=$minted.flow}
    Assert 'the same program without raw memory passes the flow stage' ($r.stage -ne 'flow' -and $r.lease.lease_id -ceq '0702') ($r|ConvertTo-Json -Compress -Depth 6)
    $rawErr=Finish $service 'flow-raw';$service=$null
    $rawFact=(@([IO.File]::ReadLines($rawJournal))[0]|ConvertFrom-Json).record
    Assert 'the raw-memory fact records the stage and the named builtin' ($rawFact.stage -ceq 'flow' -and @($rawFact.raw).Count -eq 1 -and $rawFact.raw[0] -ceq 'peek-byte') ($rawFact|ConvertTo-Json -Compress -Depth 6)
    $spin=Source "  spin : Integer -> Integer`n  spin (n) = if n == 0 then 0 else spin (n + 1)`n  opening : [Console, Network.Read] Nothing = act`n    print-line-uni (show (spin 1))`n  end"
    $quotaArgs=@('-Requester',$requesterKey,'-Grantors',"$grantorB,$grantorA",'-Identity',$identityKey,'-MaxLeaseSeconds','900','-Policy',$quotaPolicy,'-NameBook',$bookFile)
    $today=[DateTime]::Now.ToString('yyyy-MM-dd')
    $ledgerLine={param($ms)(([ordered]@{grantor=$grantorA;holder=$requesterKey;rule='grant:Network.Read';period_key=$today;ms=$ms})|ConvertTo-Json -Compress)+"`n"}
    $service=Launch (Get-Command pwsh).Source (@('-NoProfile','-File',(Join-Path $PSScriptRoot 'serve.ps1'),'-Journal',(Join-Path $work 'quota-noledger.jsonl'))+$quotaArgs)
    $r=Ask $service @{op='run';source=$network;lease=$minted.quota}
    Assert 'a quota without a usage ledger is refused' ($r.status -eq 'refused' -and $r.reason -ceq 'the held policy carries a quota and this instance keeps no usage ledger') ($r|ConvertTo-Json -Compress)
    $null=Finish $service 'quota-noledger';$service=$null
    $spentLedger=Join-Path $work 'spent.ledger';[IO.File]::WriteAllText($spentLedger,(& $ledgerLine 3600000),$utf8)
    $service=Launch (Get-Command pwsh).Source (@('-NoProfile','-File',(Join-Path $PSScriptRoot 'serve.ps1'),'-Journal',(Join-Path $work 'quota-spent.jsonl'),'-UsageLedger',$spentLedger)+$quotaArgs)
    $r=Ask $service @{op='run';source=$network;lease=$minted.quota}
    Assert 'a spent quota is refused' ($r.status -eq 'refused' -and $r.reason -ceq 'quota of 60 minutes per day is spent') ($r|ConvertTo-Json -Compress)
    $null=Finish $service 'quota-spent';$service=$null
    $nearLedger=Join-Path $work 'near.ledger';[IO.File]::WriteAllText($nearLedger,(& $ledgerLine 3599000),$utf8)
    $service=Launch (Get-Command pwsh).Source (@('-NoProfile','-File',(Join-Path $PSScriptRoot 'serve.ps1'),'-Journal',(Join-Path $work 'quota-near.jsonl'),'-UsageLedger',$nearLedger)+$quotaArgs)
    $r=Ask $service @{op='run';source=$spin;lease=$minted.quota;limits=@{ms=10000}}
    $charges=@([IO.File]::ReadAllLines($nearLedger)|Where-Object{$_}|ForEach-Object{$_|ConvertFrom-Json})
    Assert 'a quota one second short admits, caps execution at the remainder and charges it' ($r.stage -eq 'run' -and $r.quota.spent_ms_before -eq 3599000 -and $charges.Count -eq 2 -and $charges[1].ms -ge 900 -and $charges[1].ms -le 3000 -and $r.ms.run -le 3000) ("charged $($charges[-1].ms); "+($r|ConvertTo-Json -Compress))
    $null=Finish $service 'quota-near';$service=$null
    $service=Launch (Get-Command pwsh).Source (@('-NoProfile','-File',(Join-Path $PSScriptRoot 'serve.ps1'),'-Journal',(Join-Path $work 'quota-restart.jsonl'),'-UsageLedger',$nearLedger)+$quotaArgs)
    $r=Ask $service @{op='run';source=$network;lease=$minted.quota}
    Assert 'a restarted instance keeps the count' ($r.status -eq 'refused' -and $r.reason -ceq 'quota of 60 minutes per day is spent') ($r|ConvertTo-Json -Compress)
    $null=Finish $service 'quota-restart';$service=$null
    foreach($case in @(
        @('a policy without its name book refuses to start',@('-Requester',$requesterKey,'-Grantors',$grantorA,'-Identity',$identityKey,'-Policy',$allowPolicy),'needs both'),
        @('a requester inside the grantor set refuses to start',@('-Requester',$requesterKey,'-Grantors',"$grantorA,$requesterKey",'-Identity',$identityKey),'self-granted'),
        @('an identity equal to the requester refuses to start',@('-Requester',$requesterKey,'-Grantors',$grantorA,'-Identity',$requesterKey),'must differ'),
        @('a malformed key refuses to start',@('-Requester',$requesterKey.ToUpperInvariant(),'-Grantors',$grantorA,'-Identity',$identityKey),'64 lowercase hex'),
        @('a request-bound instance naming a requester refuses to start',@('-RequestBinding','-Requester',$requesterKey,'-Grantors',$grantorA,'-Identity',$identityKey),'takes no -Requester')
    )){
        $service=Launch (Get-Command pwsh).Source (@('-NoProfile','-File',(Join-Path $PSScriptRoot 'serve.ps1'),'-Journal',(Join-Path $work ([Guid]::NewGuid().ToString('N')+'.jsonl')))+$case[1])
        if(-not $service.process.WaitForExit(10000)){throw "Binding refusal hung: $($case[0])"}
        $why=$service.errors.GetAwaiter().GetResult()
        Assert $case[0] ($service.process.ExitCode -ne 0 -and $why -match $case[2]) $why
        $service.process.Dispose();$service=$null
    }

    $leases=(& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'mint-test-lease.ps1') -Work $work -Kernel $Kernel | Select-Object -Last 1) | ConvertFrom-Json
    if($LASTEXITCODE -ne 0 -or -not $leases.allow){throw 'Lease mint failed'}
    $minted.allow=$leases.allow;$allowPolicy=$leases.allow_policy;$bookFile=$leases.name_book
    $unit=Launch $probe @()
    $fresh={param($Request,[long]$Skew=0)$Request.requester=$requesterKey;$Request.nonce=[Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)).ToLowerInvariant();$Request.issued_at=[DateTimeOffset]::UtcNow.ToUnixTimeSeconds()+$Skew;$Request}
    $signed={param($Request)$text=$Request|ConvertTo-Json -Depth 10 -Compress;$sig=Ask $unit @{op='sign';text=$text};$text.Substring(0,$text.Length-1)+',"signature":"'+$sig+'"}'}
    $requestArgs=@('-RequestBinding','-Grantors',"$grantorB,$grantorA",'-Identity',$identityKey,'-MaxLeaseSeconds','900','-Policy',$allowPolicy,'-NameBook',$bookFile)
    $requestJournal=Join-Path $work 'request-bound.jsonl'
    $service=Launch (Get-Command pwsh).Source (@('-NoProfile','-File',(Join-Path $PSScriptRoot 'serve.ps1'),'-Journal',$requestJournal)+$requestArgs)
    $d=Ask $service @{op='describe'}
    Assert 'describe reports request binding' ($d.binding.transport -ceq 'request' -and $d.binding.requester -ceq '' -and $d.binding.identity -ceq $identityKey) ($d.binding|ConvertTo-Json -Compress)
    $r=Ask $service @{op='run';source=$pure}
    Assert 'an unsigned run on a request-bound instance is refused' ($r.status -eq 'refused' -and $r.stage -eq 'authentication' -and $r.reason -match 'requester must be') ($r|ConvertTo-Json -Compress)
    $line=& $signed (& $fresh @{op='run';source=$pure})
    $r=AskRaw $service $line
    Assert 'a signed ambient run is admitted as its requester' ($r.status -eq 'ok' -and $r.stdout -ceq "42`n" -and $r.requester -ceq $requesterKey) ($r|ConvertTo-Json -Compress)
    $r=AskRaw $service $line
    Assert 'a replayed signed request is refused' ($r.status -eq 'refused' -and $r.stage -eq 'authentication' -and $r.reason -ceq 'nonce was seen inside the window') ($r|ConvertTo-Json -Compress)
    foreach($skew in @(-120,120)){
        $r=AskRaw $service (& $signed (& $fresh @{op='run';source=$pure} $skew))
        Assert "a request issued $skew s from the clock is refused" ($r.status -eq 'refused' -and $r.reason -ceq "issued_at is more than 60 seconds from this conduit's clock") ($r|ConvertTo-Json -Compress)
    }
    $r=AskRaw $service ((& $signed (& $fresh @{op='run';source=$pure})).Replace('= 42','= 43'))
    Assert 'a request changed after signing is refused' ($r.status -eq 'refused' -and $r.reason -ceq 'request signature does not verify') ($r|ConvertTo-Json -Compress)
    $r=AskRaw $service (& $signed (& $fresh @{op='run';source=$network;lease=$minted.allow}))
    Assert 'a leased run on a nonce this instance never issued is refused' ($r.status -eq 'refused' -and $r.stage -eq 'admission' -and $r.reason -match 'must be a challenge this instance issued') ($r|ConvertTo-Json -Compress)
    $r=Ask $service @{op='policy';question='dead-rules'}
    Assert 'an unsigned policy question on a request-bound instance is refused' ($r.status -eq 'refused' -and $r.stage -eq 'authentication') ($r|ConvertTo-Json -Compress)
    $r=AskRaw $service (& $signed (& $fresh @{op='policy';question='can-allow';label='Network.Read'}))
    Assert 'a signed policy question on a request-bound instance is answered' ($r.status -eq 'ok' -and $r.holds -eq $true -and $r.requester -ceq $requesterKey) ($r|ConvertTo-Json -Compress)
    $c=Ask $service @{op='challenge'}
    Assert 'a challenge issues a nonce' ($c.status -eq 'ok' -and $c.nonce -cmatch '^[0-9a-f]{64}$' -and $c.expires_in -eq 60) ($c|ConvertTo-Json -Compress)
    $challenged=& $fresh @{op='run';source=$network;lease=$minted.allow};$challenged.nonce=$c.nonce
    $line=& $signed $challenged
    $r=AskRaw $service $line
    Assert 'a leased run on an issued challenge passes admission as its requester' ($r.stage -ne 'admission' -and $r.stage -ne 'authentication' -and $r.lease.lease_id -ceq '0702' -and $r.lease.holder -ceq $requesterKey -and $r.requester -ceq $requesterKey) ($r|ConvertTo-Json -Compress)
    $r=AskRaw $service $line
    Assert 'a replayed challenged run is refused' ($r.status -eq 'refused' -and $r.reason -ceq 'nonce was seen inside the window') ($r|ConvertTo-Json -Compress)
    $c=Ask $service @{op='challenge'}
    $null=Finish $service 'request-bound';$service=$null
    $requestFacts=@([IO.File]::ReadLines($requestJournal)|ForEach-Object {($_|ConvertFrom-Json).record})
    Assert 'the signed run fact records the requester' ($requestFacts[2].grant.requester -ceq $requesterKey -and $requestFacts[2].binding.transport -ceq 'request') ($requestFacts[2]|ConvertTo-Json -Compress -Depth 6)
    $service=Launch (Get-Command pwsh).Source (@('-NoProfile','-File',(Join-Path $PSScriptRoot 'serve.ps1'),'-Journal',(Join-Path $work 'request-restart.jsonl'))+$requestArgs)
    $restarted=& $fresh @{op='run';source=$network;lease=$minted.allow};$restarted.nonce=$c.nonce
    $r=AskRaw $service (& $signed $restarted)
    Assert 'a challenge issued before a restart is refused after it' ($r.status -eq 'refused' -and $r.stage -eq 'admission' -and $r.reason -match 'must be a challenge this instance issued') ($r|ConvertTo-Json -Compress)
    $null=Finish $service 'request-restart';$service=$null
    $null=Finish $unit 'signer';$unit=$null

    $service=Launch (Get-Command pwsh).Source @('-NoProfile','-File',(Join-Path $PSScriptRoot 'serve.ps1'),'-Journal',(Join-Path $work 'cancel.jsonl'))
    $d=Ask $service @{op='describe'}
    $service.process.StandardInput.WriteLine((@{op='run';source=$loop;limits=@{ms=10000}}|ConvertTo-Json -Compress))
    $owned=@();$wait=[Diagnostics.Stopwatch]::StartNew()
    do {
        $owned=@(Get-CimInstance Win32_Process -Filter "ParentProcessId = $($service.process.Id)")
        $executing=@($owned|Where-Object {$_.Name -eq 'wasmtime.exe' -and $_.CommandLine -match 'program.wasm'})
        if($executing.Count){break}
        Start-Sleep -Milliseconds 50
    } while($wait.ElapsedMilliseconds -lt 7000)
    Assert 'cancellation control reached execution' ($executing.Count -eq 1)
    $service.process.Kill();$service.process.WaitForExit()
    $cancelError=$service.errors.GetAwaiter().GetResult()
    Start-Sleep -Milliseconds 300
    $remaining=@($owned|Where-Object{Get-Process -Id $_.ProcessId -ErrorAction SilentlyContinue})
    Assert 'parent death removes conduit and execution child' ($remaining.Count -eq 0) (($remaining|Select-Object ProcessId,Name|ConvertTo-Json -Compress))
    $orphan=[regex]::Match($cancelError,'session ([^\r\n]+)').Groups[1].Value
    if($orphan){
        $fullPath=[IO.Path]::GetFullPath($orphan);$tempRoot=[IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        if(-not $fullPath.StartsWith($tempRoot,[StringComparison]::OrdinalIgnoreCase) -or (Split-Path $fullPath -Leaf) -notmatch '^accp-[a-f0-9]{32}$'){throw 'Refused cancellation cleanup path'}
        Remove-Item -LiteralPath $fullPath -Recurse -Force
    }
    $service.process.Dispose();$service=$null
} finally {
    foreach($s in @($service,$unit)){if($s){try{$s.process.Kill($true)}catch{};$s.process.Dispose()}}
}
Write-Host "ACCP focused proof: $pass pass, $fail fail. Artifacts: $work"
if($fail -gt 0){exit 1}
