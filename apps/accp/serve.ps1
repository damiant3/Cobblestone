[CmdletBinding()]
param([string]$Bundle = '', [string]$Journal = '', [ValidateRange(1048576,33554432)][int]$JournalBytes=33554432, [switch]$Mcp, [string]$ProtocolLog='')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'The first ACCP adapter requires Windows Job Objects.' }
Add-Type -Path (Join-Path $PSScriptRoot 'HostIo.cs')
if (-not $Bundle) { $Bundle = Join-Path $PSScriptRoot 'build-output' }
$Bundle = (Resolve-Path $Bundle).Path
$utf8 = [Text.UTF8Encoding]::new($false, $true)
$manifest = Get-Content -LiteralPath (Join-Path $Bundle 'manifest.json') -Raw | ConvertFrom-Json
$entry=if($Mcp){'AccpMcp'}else{'Accp'}
foreach ($name in @("$entry.cdx","$entry.exe",'codex-compiler.wasm','wasm-stdio.wasm','runtime.json','math.codex','physics.codex','experiment.codex')) {
    $actual = (Get-FileHash -LiteralPath (Join-Path $Bundle $name)).Hash.ToLowerInvariant()
    if ($actual -cne $manifest.files.$name) { throw "Artifact digest mismatch: $name" }
}
$libraries = @{}
foreach ($name in @('math','physics','experiment')) {
    $bytes = [IO.File]::ReadAllBytes((Join-Path $Bundle ($name+'.codex')))
    if ($bytes.Length -gt 65536) { throw 'Library exceeds source_bytes' }
    $digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
    if ($digest -cne $manifest.files.($name+'.codex')) { throw "Library digest mismatch: $name" }
    $libraries[$name] = $utf8.GetString($bytes)
}
$wasmtime = (Get-Command wasmtime -ErrorAction Stop).Source
$assembler = (Get-Command wat2wasm -ErrorAction Stop).Source
$engineVersion = (& $wasmtime --version | Out-String).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Wasmtime version query failed' }
$assemblerVersion = (& $assembler --version | Out-String).Trim()
if ($LASTEXITCODE -ne 0) { throw 'wat2wasm version query failed' }
$meta = [ordered]@{
    kernel=$manifest.files.'codex-compiler.wasm'; lens_module=$manifest.files.'wasm-stdio.wasm'
    conduit=$manifest.files."$entry.cdx"; runner='hosted-wasmtime-cli'; engine=$engineVersion
    wire=if($Mcp){'mcp-jsonrpc'}else{'accp-1'}
    assembler=$assemblerVersion; assembler_sha256=(Get-FileHash $assembler).Hash.ToLowerInvariant()
    engine_sha256=(Get-FileHash $wasmtime).Hash.ToLowerInvariant()
    adapter_sha256=(Get-FileHash $PSCommandPath).Hash.ToLowerInvariant()
    host_io_sha256=(Get-FileHash (Join-Path $PSScriptRoot 'HostIo.cs')).Hash.ToLowerInvariant()
    libraries=@{math=$manifest.files.'math.codex';physics=$manifest.files.'physics.codex';experiment=$manifest.files.'experiment.codex'}
}
$metaJson = $meta | ConvertTo-Json -Compress
$sessionName = 'accp-' + [Guid]::NewGuid().ToString('N')
$sessionRoot = Join-Path ([IO.Path]::GetTempPath()) $sessionName
New-Item -ItemType Directory -Path $sessionRoot | Out-Null
if (-not $Journal) { $Journal = Join-Path $Bundle ($sessionName + '.jsonl') }
$journalStream = $null
$core = $null
$coreJob = $null
$coreSpawn = $null
$previous = '0' * 64
$cached = @{}
$requestClock = [Diagnostics.Stopwatch]::new()
$budgetMs = 10000
$stdoutCap = 65536
$phaseTimes = [ordered]@{compile=0; lens=0; assemble=0; run=0; total=0}
$peakChild = 0L
$lastResponseSent=$false
$readyToServe=$false

function Hash-Bytes([byte[]]$Bytes) { [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($Bytes)).ToLowerInvariant() }
function Hash-Text([string]$Text) { Hash-Bytes $utf8.GetBytes($Text) }
function Trace-Protocol([string]$Direction,[string]$Message) {
    if(-not $ProtocolLog){return}
    $line=(@{direction=$Direction;message=$Message}|ConvertTo-Json -Compress)+"`n"
    $bytes=$utf8.GetBytes($line)
    $stream=[IO.File]::Open($ProtocolLog,[IO.FileMode]::Append,[IO.FileAccess]::Write,[IO.FileShare]::Read)
    try{if($stream.Length+$bytes.Length -gt 32MB){throw 'protocol_log_bytes exceeded'};$stream.Write($bytes,0,$bytes.Length)}finally{$stream.Dispose()}
}
function Ascii-Wire([string]$Text) {
    [AccpHost.Wire]::Ascii($Text)
}
function Send-Core([string]$Text, [int]$Ms=1000) {
    $task=$script:coreWriter.WriteLineAsync((Ascii-Wire $Text))
    if (-not $task.Wait([Math]::Max(1,$Ms))) { $script:coreJob.Terminate(); throw 'conduit input timeout' }
    [void]$task.GetAwaiter().GetResult()
}
function Read-Core([int]$Ms=1000) {
    $task=$script:coreFrames.ReadAsync(100MB)
    if (-not $task.Wait([Math]::Max(1,$Ms))) { $script:coreJob.Terminate(); throw 'conduit response timeout' }
    $frame=$task.GetAwaiter().GetResult()
    if ($frame.Eof -or $frame.TooLarge -or $frame.InvalidUtf8) { throw 'conduit response missing, invalid or oversized' }
    return $frame.Text
}
function Invoke-Tool([string]$File, [string[]]$Arguments, [byte[]]$InputBytes, [int]$Ms, [int]$Cap) {
    if ($Ms -le 0) { return @{status='timeout';reason='ms exceeded';text='';bytes='';sha256='';ms=0} }
    $result=[AccpHost.Child]::Run($File,$Arguments,$InputBytes,$Ms,$Cap,2GB)
    $script:peakChild=[Math]::Max($script:peakChild,[long]$result.PeakMemory)
    $status=if ($result.Limit -eq 'ms') {'timeout'} elseif ($result.Limit) {'trapped'} elseif ($result.ExitCode -ne 0) {'trapped'} else {'ok'}
    $why=if ($result.Limit) {$result.Limit + ' exceeded'} elseif ($result.ExitCode -ne 0) { 'exit '+$result.ExitCode+': '+[Text.Encoding]::UTF8.GetString($result.Error) } else {''}
    if ($why.Length -gt 2048) { $why=$why.Substring(0,2048) }
    @{status=$status;reason=$why;text=[Text.Encoding]::UTF8.GetString($result.Output);bytes=[Convert]::ToBase64String($result.Output);sha256=(Hash-Bytes $result.Output);ms=$result.Ms;output=$result.Output}
}
function Remaining { [int][Math]::Max(0,$script:budgetMs-$script:requestClock.ElapsedMilliseconds) }
function Start-Core {
    $boot=[Diagnostics.Stopwatch]::StartNew()
    if ($script:coreJob) { $script:coreJob.Dispose() }
    if ($script:coreSpawn) { $script:coreSpawn.Dispose() }
    $script:coreJob=[AccpHost.Job]::new(4GB)
    $script:coreSpawn=[AccpHost.Spawn]::Start((Join-Path $Bundle "$entry.exe"),@(),$script:coreJob)
    $script:core=$script:coreSpawn.Process
    $script:coreWriter=[IO.StreamWriter]::new($script:coreSpawn.Input,$utf8)
    $script:coreWriter.AutoFlush=$true
    $script:coreFrames=[AccpHost.Frames]::new($script:coreSpawn.Output)
    $script:coreErrorDrain=$script:coreSpawn.Error.CopyToAsync([IO.Stream]::Null)
    Send-Core $metaJson ([int][Math]::Max(1,30000-$boot.ElapsedMilliseconds))
    $hello=Read-Core ([int][Math]::Max(1,30000-$boot.ElapsedMilliseconds)) | ConvertFrom-Json
    if ($hello.bridge -ne 'inspect-modules') { throw 'conduit startup contract mismatch' }
    $modules=@{compiler=[Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $Bundle 'codex-compiler.wasm')));lens=[Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $Bundle 'wasm-stdio.wasm')))}
    Send-Core ($modules | ConvertTo-Json -Compress) ([int][Math]::Max(1,30000-$boot.ElapsedMilliseconds))
    $ready=Read-Core ([int][Math]::Max(1,30000-$boot.ElapsedMilliseconds)) | ConvertFrom-Json
    if ($ready.bridge -ne 'ready' -or -not $ready.ok) { throw 'conduit refused the module imports' }
}
function Append-Fact([string]$Record, [string]$Response, [bool]$Defer=$false) {
    $bytes=$utf8.GetBytes($Record)
    $hash=Hash-Bytes $bytes
    $line=$utf8.GetBytes('{"fact":"'+$hash+'","record":'+$Record+"}`n")
    if ($line.Length -gt 1MB -or $journalStream.Length+$line.Length -gt $JournalBytes) { throw 'journal_bytes exceeded' }
    $journalStream.Write($line,0,$line.Length); $journalStream.Flush($true)
    $script:previous=$hash
    $final=$Response.Substring(0,$Response.Length-1)+',"fact":"'+$hash+'"}'
    if($Defer){return @{status='ok';response=$final;response_base64=[Convert]::ToBase64String($utf8.GetBytes($final))}}
    [Console]::Out.WriteLine($final); $script:lastResponseSent=$true
    return @{status='ok'}
}

try {
    $journalStream=[IO.File]::Open($Journal,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
    $startup=[Diagnostics.Stopwatch]::StartNew()
    Start-Core
    foreach($name in @('codex-compiler.wasm','wasm-stdio.wasm')) {
        $target=Join-Path $sessionRoot ($name+'.cwasm')
        $r=Invoke-Tool $wasmtime @('compile','-W','epoch-interruption=y,max-wasm-stack=16777216','-o',$target,(Join-Path $Bundle $name)) @() ([int][Math]::Max(1,30000-$startup.ElapsedMilliseconds)) 65536
        if ($r.status -ne 'ok' -or -not (Test-Path $target)) { throw "Native image startup failed: $($r.reason)" }
        $cached[$name]=@{path=$target;hash=(Get-FileHash $target).Hash}
    }
    $readyToServe=$true
    [Console]::Error.WriteLine("ACCP ready; conduit PID $($core.Id); journal $Journal; session $sessionRoot")
    $inputFrames=[AccpHost.Frames]::new([Console]::OpenStandardInput())
    while ($true) {
        $frame=$inputFrames.Read(262144,$true)
        if ($frame.Eof) { break }
        $lastResponseSent=$false
        if ($journalStream.Length -gt $JournalBytes-1MB) { throw 'journal_bytes: execution refused' }
        $requestClock.Restart(); $budgetMs=10000; $stdoutCap=65536
        $phaseTimes=[ordered]@{compile=0;lens=0;assemble=0;run=0;total=0}
        $sourceHash=Hash-Text ''; $inputHash=Hash-Text ''; $compiledSourceHash=$null
        $requestHash=$frame.Sha256
        $line=if($frame.TooLarge){'FRAME_TOO_LARGE'}elseif($frame.InvalidUtf8 -or [AccpHost.Wire]::InvalidControls($frame.Text)){'INVALID_FRAME_ENCODING'}else{$frame.Text}
        Trace-Protocol 'in' $line
        $header=@{previous=$previous;request_sha256=$requestHash} | ConvertTo-Json -Compress
        $finished=$false
        try {
            Send-Core $header 10000
            Send-Core $line 10000
            $finished=$false
            while (-not $finished) {
                $wire=Read-Core ([Math]::Max(1000,(Remaining)))
                $doc=[Text.Json.JsonDocument]::Parse($wire)
                try {
                    $cmd=$wire | ConvertFrom-Json
                    $reply=@{status='ok'}
                    switch ([string]$cmd.bridge) {
                        'hash-inputs' {
                            $sourceHash=Hash-Text ([string]$cmd.source); $inputHash=Hash-Text ([string]$cmd.input)
                            $compiledSourceHash=if($null -ne $cmd.compiled_source){Hash-Text ([string]$cmd.compiled_source)}else{$null}
                            $reply=@{source_sha256=$sourceHash;input_sha256=$inputHash;compiled_source_sha256=$compiledSourceHash}
                        }
                        'library' {
                            if (-not $libraries.ContainsKey([string]$cmd.name)) { throw 'Unknown library artifact' }
                            $reply=@{status='ok';source=$libraries[[string]$cmd.name]}
                        }
                        'compile' {
                            $budgetMs=[Math]::Min(10000,[int]$cmd.limits.ms); $stdoutCap=[Math]::Min(65536,[int]$cmd.limits.stdout_bytes)
                            $cache=$cached['codex-compiler.wasm']
                            if ((Get-FileHash $cache.path).Hash -ne $cache.hash) { throw 'compiler native image changed' }
                            $data=$utf8.GetBytes("IR-UNI decks=12`n"+[string]$cmd.source+[char]0)
                            $reply=Invoke-Tool $wasmtime @('run','--allow-precompiled','-W','epoch-interruption=y,timeout=10000ms,max-wasm-stack=16777216,max-memory-size=536870912,trap-on-grow-failure=y',$cache.path) $data (Remaining) 16MB
                            $phaseTimes.compile=$reply.ms
                            $reply=@{status=$reply.status;reason=$reply.reason;text=$reply.text}
                        }
                        'lens' {
                            $cache=$cached['wasm-stdio.wasm']
                            if ((Get-FileHash $cache.path).Hash -ne $cache.hash) { throw 'lens native image changed' }
                            $reply=Invoke-Tool $wasmtime @('run','--allow-precompiled','-W','epoch-interruption=y,timeout=10000ms,max-wasm-stack=16777216,max-memory-size=536870912,trap-on-grow-failure=y',$cache.path) $utf8.GetBytes([string]$cmd.ir+[char]0) (Remaining) 16MB
                            $phaseTimes.lens=$reply.ms
                            if ($reply.status -eq 'ok') { [IO.File]::WriteAllBytes((Join-Path $sessionRoot 'program.wat'),$reply.output) }
                            $reply=@{status=$reply.status;reason=$reply.reason;text=$reply.text}
                        }
                        'assemble' {
                            $target=Join-Path $sessionRoot 'program.wasm'
                            if(Test-Path $target){Remove-Item -LiteralPath $target -Force}
                            $reply=Invoke-Tool $assembler @('--enable-tail-call',(Join-Path $sessionRoot 'program.wat'),'-o',$target) @() (Remaining) 65536
                            $phaseTimes.assemble=$reply.ms
                            if ($reply.status -eq 'ok') {
                                if ((Get-Item $target).Length -gt 16MB) { $reply=@{status='trapped';reason='intermediate_bytes exceeded'} }
                                else { $reply=@{status='ok';wasm=[Convert]::ToBase64String([IO.File]::ReadAllBytes($target))} }
                            } else { $reply=@{status=$reply.status;reason=$reply.reason} }
                        }
                        'execute' {
                            $reply=Invoke-Tool $wasmtime @('run','-W','epoch-interruption=y,timeout=10000ms,max-wasm-stack=16777216,max-memory-size=67108864,trap-on-grow-failure=y',(Join-Path $sessionRoot 'program.wasm')) $utf8.GetBytes([string]$cmd.input) (Remaining) $stdoutCap
                            $phaseTimes.run=$reply.ms
                            $reply=@{status=$reply.status;reason=$reply.reason;text=$reply.text;bytes=$reply.bytes;sha256=$reply.sha256}
                        }
                        'clock' { $phaseTimes.total=$requestClock.ElapsedMilliseconds; $reply=@{ms=$phaseTimes} }
                        'digest-response' { $reply=@{sha256=(Hash-Text $doc.RootElement.GetProperty('response').GetRawText())} }
                        'record' {
                            $defer=($cmd.PSObject.Properties.Name -contains 'defer_response') -and [bool]$cmd.defer_response
                            if($defer -ne [bool]$Mcp){throw 'record transport mismatch'}
                            $reply=Append-Fact $doc.RootElement.GetProperty('record').GetRawText() $doc.RootElement.GetProperty('response').GetRawText() $defer
                            $core.Refresh()
                            [Console]::Error.WriteLine("ACCP request: heap_start=$($cmd.heap_start) heap_bytes=$($cmd.heap_bytes) conduit_rss_bytes=$($core.WorkingSet64) child_peak_bytes=$peakChild total_ms=$($requestClock.ElapsedMilliseconds)")
                            $finished=-not $defer
                        }
                        'mcp-response' {
                            if(-not $Mcp){throw 'MCP response on L2 transport'}
                            $responseElement=$doc.RootElement.GetProperty('response')
                            if($responseElement.ValueKind -ne [Text.Json.JsonValueKind]::Null){
                                Trace-Protocol 'out' $responseElement.GetRawText()
                                [Console]::Out.WriteLine($responseElement.GetRawText())
                                $script:lastResponseSent=$true
                            }
                            $finished=$true
                        }
                        default { throw "Unknown conduit bridge operation: $($cmd.bridge)" }
                    }
                    Send-Core ($reply | ConvertTo-Json -Depth 12 -Compress) 1000
                } finally { $doc.Dispose() }
            }
        } catch {
            $reason=$_.Exception.Message
            $coreJob.Terminate()
            if ($finished) { throw }
            if ($reason -match 'journal|disk|write') {
                throw
            }
            $status=if($reason -match 'timeout'){'timeout'}else{'error'}
            $response=@{status=$status;stage='adapter';reason=$reason;kernel=$meta.kernel;runner=$meta.runner} | ConvertTo-Json -Compress
            $record=[ordered]@{version=1;previous=$previous;request_sha256=$requestHash;source_sha256=$sourceHash;input_sha256=$inputHash;compiled_source_sha256=$compiledSourceHash;response_sha256=(Hash-Text $response);status=$status;kernel=$meta.kernel;conduit=$meta.conduit;adapter_failure=$true} | ConvertTo-Json -Compress
            $failureReceipt=Append-Fact $record $response ([bool]$Mcp)
            if($Mcp){throw}
            Start-Core
        }
    }
} catch {
    $why=$_.Exception.Message
    if(-not $lastResponseSent -and -not $Mcp){
        $stage=if(-not $readyToServe){'startup'}elseif($why -match 'journal|disk|write'){'audit'}else{'adapter'}
        [Console]::Out.WriteLine((@{status='error';stage=$stage;reason=$why}|ConvertTo-Json -Compress))
    }
    [Console]::Error.WriteLine($why)
    exit 1
} finally {
    if($coreJob){$coreJob.Dispose()}
    if($coreSpawn){$coreSpawn.Dispose()}
    if($journalStream){$journalStream.Dispose()}
    $full=[IO.Path]::GetFullPath($sessionRoot)
    $parent=[IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if ($full.StartsWith($parent,[StringComparison]::OrdinalIgnoreCase) -and (Split-Path $full -Leaf) -ceq $sessionName) {
        Remove-Item -LiteralPath $full -Recurse -Force
    }
}
