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
