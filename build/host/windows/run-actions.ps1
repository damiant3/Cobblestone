[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Repo,
    [Parameter(Mandatory)][string]$Executor,
    [Parameter(Mandatory)][string]$Source,
    [Parameter(Mandatory)][string]$Kernel,
    [Parameter(Mandatory)][string]$OutDir,
    [string]$Expected = '',
    [string]$Plan = 'compile-test',
    [ValidateRange(1,600000)][int]$RunMs = 60000,
    [string]$CancelFile = ''
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$Repo = (Resolve-Path -LiteralPath $Repo).Path
$Executor = (Resolve-Path -LiteralPath $Executor).Path
$Kernel = [IO.Path]::GetFullPath($Kernel)
$OutDir = [IO.Path]::GetFullPath($OutDir)
if((Test-Path -LiteralPath $OutDir) -and @(Get-ChildItem -LiteralPath $OutDir -Force).Count){throw 'Output directory must be empty'}
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
. (Join-Path $Repo 'build/quire-map.ps1')
Add-Type -Path (Join-Path $Repo 'apps/accp/HostIo.cs')
$utf8 = [Text.UTF8Encoding]::new($false, $true)
$bindings = @{
    source=[IO.Path]::GetFullPath($Source); compiler=$Kernel
    unit=(Join-Path $OutDir 'unit.input'); program=(Join-Path $OutDir 'program.cdx')
    repeat=(Join-Path $OutDir 'repeat.cdx'); output=(Join-Path $OutDir 'output.raw')
    expected=if($Expected){[IO.Path]::GetFullPath($Expected)}else{''}
    record=(Join-Path $OutDir 'record.json')
}
$rawCompile = Join-Path $OutDir 'compile.raw'
$events = [Collections.Generic.List[object]]::new()
$journal = Join-Path $OutDir 'actions.jsonl'
$regions = @()
$core = $null
$coreJob = $null
$done = $null
$script:lastResponse = @{}
$script:activeChild = $null

function Path-Of([string]$Id) {
    if(-not $bindings.ContainsKey($Id) -or -not $bindings[$Id]) { throw "Unbound artifact: $Id" }
    $bindings[$Id]
}
function Write-Journal($Value) {
    [IO.File]::AppendAllText($journal, ($Value | ConvertTo-Json -Compress -Depth 8) + "`n", $utf8)
}
function Read-Bounded([string]$Path, [int]$Limit=65536) {
    if((Get-Item -LiteralPath $Path).Length -gt $Limit){throw "Text limit exceeded: $Path"}
    [IO.File]::ReadAllText($Path,$utf8).Replace("`r",'')
}
function Write-Record($Result) {
    $hashes=[ordered]@{}
    foreach($id in @('source','compiler','unit','program','repeat','output','expected')) {
        $path=$bindings[$id]
        if($path -and (Test-Path -LiteralPath $path -PathType Leaf)){$hashes[$id]=(Get-FileHash -LiteralPath $path).Hash}
    }
    [IO.File]::WriteAllText($bindings.record, (@{plan=$Plan;result=$Result;events=@($events.ToArray());artifacts=$hashes;bindings=$bindings;executor=@{path=$Executor;sha256=(Get-FileHash $Executor).Hash};evidence=@{journal=$journal;diagnostics=(Join-Path $OutDir 'compile.log');executor_stderr=(Join-Path $OutDir 'executor.stderr')};last_response=$script:lastResponse} | ConvertTo-Json -Depth 10),$utf8)
}
function Invoke-Guest($Request, [string]$Image, [string]$InputFile, [string]$OutputFile) {
    if($Request.guests -ne 1 -or $Request.memory_mb -lt 1 -or $Request.memory_mb -gt 2048 -or $Request.timeout_ms -lt 1 -or $Request.timeout_ms -gt 600000){throw 'Invalid resource declaration'}
    $free=(Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1MB
    Write-Journal @{event='admission';free_gib=$free;guests=1;memory_mb=$Request.memory_mb;timeout_ms=$Request.timeout_ms}
    if($free -le 1.5){return @{status='skipped';reason='RAM admission refused'}}
    if($CancelFile -and (Test-Path -LiteralPath $CancelFile)){return @{status='skipped';reason='cancelled'}}
    $argsList=@('-kernel',$Image,'-output',$OutputFile,'-mem',[string]$Request.memory_mb,'-headless')
    if($InputFile){$argsList+=@('-input',$InputFile)}
    $job=[AccpHost.Job]::new(4GB)
    $spawn=$null
    $watch=[Diagnostics.Stopwatch]::StartNew()
    $limit=''
    try {
        $spawn=[AccpHost.Spawn]::Start((Join-Path $Repo 'tools/codex-vm.exe'),$argsList,$job)
        $script:activeChild=$spawn.Process.Id
        Write-Journal @{event='guest-start';pid=$script:activeChild;image=$Image;output=$OutputFile}
        $spawn.Input.Dispose()
        $stdout=[IO.File]::Create($OutputFile+'.host-out')
        $stderr=[IO.File]::Create($OutputFile+'.host-err')
        try {
            $outTask=$spawn.Output.CopyToAsync($stdout)
            $errTask=$spawn.Error.CopyToAsync($stderr)
            while(-not $spawn.Process.WaitForExit(50)) {
                if($watch.ElapsedMilliseconds -ge $Request.timeout_ms){$limit='timeout';break}
                if($CancelFile -and (Test-Path -LiteralPath $CancelFile)){$limit='skipped';break}
                if(($stdout.Length+$stderr.Length) -gt 1MB -or ((Test-Path -LiteralPath $OutputFile) -and (Get-Item -LiteralPath $OutputFile).Length -gt 64MB)){$limit='crash';break}
            }
            if($limit){$job.Terminate();$spawn.Process.WaitForExit()}
            [void]$outTask.GetAwaiter().GetResult()
            [void]$errTask.GetAwaiter().GetResult()
        } finally {$stdout.Dispose();$stderr.Dispose()}
        $errText=Read-Bounded ($OutputFile+'.host-err') 1MB
        Write-Journal @{event='guest-end';pid=$script:activeChild;exit=$spawn.Process.ExitCode;limit=$limit;ms=$watch.ElapsedMilliseconds;peak_bytes=$job.PeakMemory()}
        if($limit){return @{status=$limit}}
        if(((Get-Item -LiteralPath ($OutputFile+'.host-out')).Length+(Get-Item -LiteralPath ($OutputFile+'.host-err')).Length) -gt 1MB -or ((Test-Path -LiteralPath $OutputFile) -and (Get-Item -LiteralPath $OutputFile).Length -gt 64MB)){return @{status='crash';reason='output limit'}}
        if($errText -match 'DROPPED' -or $spawn.Process.ExitCode -ne 1){return @{status='crash';exit=[string]$spawn.Process.ExitCode}}
        if(-not (Test-Path -LiteralPath $OutputFile -PathType Leaf)){return @{status='crash'}}
        return @{status='ok';exit=[string]$spawn.Process.ExitCode}
    } finally {
        $job.Dispose()
        if($spawn){$spawn.Dispose()}
        $script:activeChild=$null
    }
}
function Invoke-Action($r) {
    switch($r.op) {
        'preflight' {
            foreach($id in @('source','compiler')){if(-not (Test-Path -LiteralPath (Path-Of $id) -PathType Leaf)){return @{status='failed';reason="missing $id"}}}
            if($r.plan -eq 'compile-test' -and (-not $bindings.expected -or -not (Test-Path -LiteralPath $bindings.expected -PathType Leaf))){return @{status='failed';reason='missing expected output'}}
            $inputs=@($bindings.source,$bindings.compiler,$bindings.expected)
            foreach($id in @('unit','program','repeat','output','record')){if($inputs -contains $bindings[$id]){return @{status='failed';reason='input aliases output'}}}
            return @{status='ok'}
        }
        'resolve-unit' {
            $sourcePath=Path-Of $r.source
            if(-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)){return @{status='failed';reason='missing source'}}
            $lines=[IO.File]::ReadAllLines($sourcePath,$utf8)
            $seen=@{}
            foreach($line in $lines){if($line -match '^Chapter:\s*(\w+)--(.+?)\s*$'){$seen["$($matches[1])::$($matches[2])"]=$true}}
            try {$ordered=Resolve-CiteOrder -RootLines $lines -Repo $Repo -SeedSeen $seen}
            catch {[IO.File]::WriteAllText((Join-Path $OutDir 'compile.log'),'error 3010: '+$_.Exception.Message,$utf8);return @{status='failed';reason='cite resolution'}}
            $script:regions=Get-DiagRegions -Ordered $ordered -SrcPath $sourcePath
            $writer=[IO.StreamWriter]::new((Path-Of $r.output),$false,$utf8)
            try {
                $writer.NewLine="`n"
                $writer.WriteLine('CDX map')
                foreach($line in (Format-CiteChapters -Ordered $ordered)){$writer.WriteLine($line)}
                foreach($line in $lines){$writer.WriteLine($line)}
                $writer.Write([char]4)
            } finally {$writer.Dispose()}
            return @{status='ok'}
        }
        'compile' {
            [IO.File]::WriteAllText((Join-Path $OutDir 'compile.log'),'',$utf8)
            return Invoke-Guest $r (Path-Of $r.compiler) (Path-Of $r.unit) $rawCompile
        }
        'read-compile-line' {
            $stream=[IO.File]::OpenRead($rawCompile)
            try {
                if($r.offset -lt 0 -or $r.offset -ge $stream.Length){return @{status='crash'}}
                $stream.Position=$r.offset
                $bytes=[Collections.Generic.List[byte]]::new()
                while($stream.Position -lt $stream.Length){$b=$stream.ReadByte();if($b -eq 10){break};$bytes.Add([byte]$b);if($bytes.Count -gt 16384){return @{status='crash'}}}
                $line=$utf8.GetString($bytes.ToArray()).TrimEnd("`r")
                [IO.File]::AppendAllText((Join-Path $OutDir 'compile.log'),(Convert-DiagLine -Line $line -Regions $script:regions)+"`n",$utf8)
                return @{status='ok';text=$line;next=$stream.Position}
            } finally {$stream.Dispose()}
        }
        'copy-artifact' {
            $input=[IO.File]::OpenRead($rawCompile)
            try {
                if($r.size -le 0 -or $r.offset -lt 0 -or $r.offset+$r.size -gt $input.Length){return @{status='crash'}}
                $input.Position=$r.offset
                $output=[IO.File]::Create((Path-Of $r.output))
                try {
                    $buffer=[byte[]]::new(65536)
                    $remaining=[long]$r.size
                    while($remaining -gt 0){$n=$input.Read($buffer,0,[int][Math]::Min($remaining,$buffer.Length));if($n -eq 0){throw 'Short artifact'};$output.Write($buffer,0,$n);$remaining-=$n}
                } finally {$output.Dispose()}
            } finally {$input.Dispose()}
            return @{status='ok'}
        }
        'run-artifact' {return Invoke-Guest $r (Path-Of $r.program) '' (Path-Of $r.output)}
        'read-output' {return @{status='ok';actual=(Read-Bounded (Path-Of $r.output)).TrimStart([char]1);expected=(Read-Bounded (Path-Of $r.expected))}}
        'write-normalized' {[IO.File]::WriteAllText((Join-Path $OutDir 'output.txt'),[string]$r.text,$utf8);return @{status='ok'}}
        'compare-bytes' {
            $left=[IO.File]::OpenRead((Path-Of $r.left));$right=[IO.File]::OpenRead((Path-Of $r.right))
            try {
                $equal=$left.Length -eq $right.Length
                $a=[byte[]]::new(65536);$b=[byte[]]::new(65536)
                while($equal -and $left.Position -lt $left.Length){$n=$left.Read($a,0,$a.Length);$right.ReadExactly($b,0,$n);for($i=0;$i -lt $n;$i++){if($a[$i] -ne $b[$i]){$equal=$false;break}}}
                return @{status='ok';equal=$equal}
            } finally {$left.Dispose();$right.Dispose()}
        }
        'write-record' {Write-Record @{verdict='running'};return @{status='ok'}}
        'event' {$events.Add(@{stage=$r.stage;verdict=$r.verdict;step_status=$r.step_status;workflow=$r.workflow;scratch_bytes=$r.scratch_bytes;retained_scratch_bytes=$r.retained_scratch_bytes;adapter_response=$script:lastResponse});return @{status='ok'}}
        default {return @{status='skipped';reason='unsupported operation'}}
    }
}
try {
    [IO.File]::WriteAllText($journal,'',$utf8)
    $coreJob=[AccpHost.Job]::new(4GB)
    $core=[AccpHost.Spawn]::Start($Executor,@(),$coreJob)
    $writer=[IO.StreamWriter]::new($core.Input,$utf8);$writer.AutoFlush=$true
    $frames=[AccpHost.Frames]::new($core.Output)
    $coreError=[IO.File]::Create((Join-Path $OutDir 'executor.stderr'))
    $errorTask=$core.Error.CopyToAsync($coreError)
    $writer.WriteLine((@{plan=$Plan;run_ms=$RunMs}|ConvertTo-Json -Compress))
    for($requests=0;$requests -lt 8192;$requests++) {
        $read=$frames.ReadAsync(262144)
        if(-not $read.Wait(10000)){throw 'Executor response timeout'}
        $frame=$read.GetAwaiter().GetResult()
        if($frame.Eof -or $frame.TooLarge -or $frame.InvalidUtf8){throw 'Invalid executor frame'}
        $request=$frame.Text | ConvertFrom-Json
        Write-Journal @{request=$request}
        if($request.op -eq 'done'){$done=$request;break}
        try {$response=Invoke-Action $request}
        catch {$response=@{status='crash';reason=$_.Exception.Message}}
        Write-Journal @{response=$response}
        $script:lastResponse=$response
        $writer.WriteLine([AccpHost.Wire]::Ascii(($response | ConvertTo-Json -Compress -Depth 5)))
    }
    if(-not $done){throw 'Executor request limit exceeded'}
    $writer.Dispose()
    if(-not $core.Process.WaitForExit(5000)){throw 'Executor did not exit after its final result'}
    [void]$errorTask.GetAwaiter().GetResult()
    if($core.Process.ExitCode -ne 0){throw "Executor exited $($core.Process.ExitCode) after its final result"}
    Write-Record $done
    $done | ConvertTo-Json -Compress
    if($done.verdict -ne 'pass'){exit 1}
} catch {
    [Console]::Error.WriteLine($_.ToString())
    [Console]::Error.WriteLine($_.ScriptStackTrace)
    Write-Record @{verdict='crash';reason=$_.Exception.Message}
    throw
} finally {
    if($coreJob){$coreJob.Dispose()}
    if($core){try{$core.Dispose()}catch{[Console]::Error.WriteLine("Executor cleanup: $($_.Exception.Message)")}}
    if($null -ne (Get-Variable coreError -ErrorAction SilentlyContinue)){$coreError.Dispose()}
}
