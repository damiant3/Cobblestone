[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Repo,
    [Parameter(Mandatory)][string]$Kernel,
    [Parameter(Mandatory)][string]$Executor,
    [Parameter(Mandatory)][string]$OutDir,
    [switch]$CancellationOnly
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$Repo=(Resolve-Path -LiteralPath $Repo).Path
$Kernel=(Resolve-Path -LiteralPath $Kernel).Path
$Executor=(Resolve-Path -LiteralPath $Executor).Path
$OutDir=[IO.Path]::GetFullPath($OutDir)
if(Test-Path -LiteralPath $OutDir){throw 'Proof directory must be new'}
New-Item -ItemType Directory -Path $OutDir | Out-Null
$utf8=[Text.UTF8Encoding]::new($false)
$adapter=Join-Path $Repo 'build/host/windows/run-actions.ps1'
$results=[Collections.Generic.List[object]]::new()
function Check([bool]$Condition,[string]$Name) {
    if(-not $Condition){throw "FAIL: $Name"}
    $results.Add(@{check=$Name;pass=$true})
    Write-Host "PASS: $Name"
}
function Run-Case([string]$Name,[string]$Source,[string]$Expected,[string]$Want,[string]$Plan='compile-test',[int]$Ms=60000,[string]$Compiler=$Kernel,[string]$Cancel='') {
    $destination=Join-Path $OutDir $Name
    & pwsh -NoProfile -File $adapter -Repo $Repo -Executor $Executor -Source $Source -Kernel $Compiler -Expected $Expected -OutDir $destination -Plan $Plan -RunMs $Ms -CancelFile $Cancel *> ($destination+'.host.log')
    $exitCode=$LASTEXITCODE
    $record=Get-Content -LiteralPath (Join-Path $destination 'record.json') -Raw -Encoding utf8 | ConvertFrom-Json
    Check ($record.result.verdict -ceq $Want -and (($exitCode -eq 0) -eq ($Want -eq 'pass'))) "$Name verdict=$Want and process exit"
    if($Want -eq 'pass'){Check ($record.result.workflow -ceq 'completed') "$Name workflow completed"}
    foreach($event in $record.events){Check ($event.retained_scratch_bytes -eq 0 -and $event.scratch_bytes -ge 0 -and $event.scratch_bytes -lt 64MB) "$Name $($event.stage) scratch reclaimed within bound"}
    return $record
}
$arith=Join-Path $Repo 'codex/test/arithmetic.codex'
$arithExpected=Join-Path $Repo 'codex/test/arithmetic.expected'
$scope=Join-Path $Repo 'codex/test/act-let-scope.codex'
$scopeExpected=Join-Path $Repo 'codex/test/act-let-scope.expected'
if(-not $CancellationOnly) {
foreach($item in @(@('arithmetic',$arith,$arithExpected),@('act-let-scope',$scope,$scopeExpected))) {
    $record=Run-Case $item[0] $item[1] $item[2] 'pass'
    Check (($record.events.stage -join ',') -ceq 'resolve,compile,run,grade,record') "$($item[0]) executes every action"
    $free=(Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1MB
    if($free -le 1.5){throw 'Baseline compile RAM admission refused'}
    $baseline=Join-Path $OutDir ($item[0]+'.baseline.cdx')
    & pwsh -NoProfile -File (Join-Path $Repo 'build/compile.ps1') -Src $item[1] -Out $baseline -Log ($baseline+'.log') -Kernel $Kernel -MemMB 2048 *> ($baseline+'.host.log')
    Check ($LASTEXITCODE -eq 0) "$($item[0]) legacy compile"
    Check ((Get-FileHash -LiteralPath $baseline).Hash -ceq $record.artifacts.program) "$($item[0]) artifact bytes match legacy compiler path"
    $free=(Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1MB
    if($free -le 1.5){throw 'Baseline run RAM admission refused'}
    & pwsh -NoProfile -File (Join-Path $Repo 'build/test-run.ps1') -Kernel $baseline -OutFile ($baseline+'.output') *> ($baseline+'.run.log')
    Check ($LASTEXITCODE -eq 0) "$($item[0]) legacy execution"
    Check ((Get-FileHash ($baseline+'.output')).Hash -ceq (Get-FileHash (Join-Path $OutDir ($item[0]+'/output.txt'))).Hash) "$($item[0]) output bytes match legacy run path"
}
$record=Run-Case 'reproduce' $arith '' 'pass' 'compile-reproduce'
Check ($record.artifacts.program -ceq $record.artifacts.repeat) 'reproduce artifact bytes'
$record=Run-Case 'missing-input' (Join-Path $OutDir 'absent.codex') $arithExpected 'fail'
Check ($record.events.Count -eq 0) 'missing input refused before actions'
$invalid=Join-Path $OutDir 'invalid.codex'
[IO.File]::WriteAllText($invalid,"Chapter: InvalidActionControl`n  cites Foreword chapter Console`nSection: Entry`n  opening : [Console] Nothing = print-line-uni missing-value`nPage 1`n",$utf8)
$record=Run-Case 'compiler-error' $invalid $arithExpected 'fail'
Check (@($record.events | Where-Object stage -eq run).Count -eq 0) 'compiler error prevents execution'
Check ((Get-Content (Join-Path $OutDir 'compiler-error/compile.log') -Raw) -match 'CDX3002') 'compiler diagnostic retained'
$record=Run-Case 'wrong-output' $arith $scopeExpected 'fail'
Check (@($record.events | Where-Object { $_.stage -eq 'grade' -and $_.verdict -eq 'fail' }).Count -eq 1) 'incorrect output fails grading'
$record=Run-Case 'timeout' $arith $arithExpected 'timeout' 'compile-test' 1
Check (@($record.events | Where-Object { $_.stage -eq 'run' -and $_.step_status -eq 'timed out' }).Count -eq 1) 'timeout remains distinct'
$record=Run-Case 'unsupported' $arith $arithExpected 'skip' 'unsupported-operation'
Check ($record.events.Count -eq 0) 'unsupported operation refused before actions'
$badKernel=Join-Path $OutDir 'invalid-kernel.cdx'
[IO.File]::WriteAllBytes($badKernel,[byte[]]::new(224))
$record=Run-Case 'crash' $arith $arithExpected 'crash' 'compile-test' 60000 $badKernel
Check (@($record.events | Where-Object { $_.stage -eq 'compile' -and $_.verdict -eq 'crash' }).Count -eq 1) 'VM refusal remains crash'
$cancel=Join-Path $OutDir 'cancel'
[IO.File]::WriteAllText($cancel,'cancel',$utf8)
$record=Run-Case 'cancelled' $arith $arithExpected 'skip' 'compile-test' 60000 $Kernel $cancel
Check (@($record.events | Where-Object { $_.stage -eq 'compile' -and $_.verdict -eq 'skip' }).Count -eq 1) 'cancellation admission remains skip'
}
$spin=Join-Path $OutDir 'spin.codex'
[IO.File]::WriteAllText($spin,"Chapter: ActionCancellation`n  cites Foreword chapter Console`nSection: Entry`n  spin : Integer -> Integer`n  spin (n) = spin (n + 1)`n  opening : [Console] Nothing = print-line-uni (show (spin 0))`nPage 1`n",$utf8)
$activeCancel=Join-Path $OutDir 'active.cancel'
$activeDir=Join-Path $OutDir 'active-cancel'
$cancelArgs=@('-NoProfile','-File',$adapter,'-Repo',$Repo,'-Executor',$Executor,'-Source',$spin,'-Kernel',$Kernel,'-Expected',$arithExpected,'-OutDir',$activeDir,'-CancelFile',$activeCancel) | ForEach-Object {'"'+$_+'"'}
$cancelProcess=Start-Process pwsh -WindowStyle Hidden -PassThru -ArgumentList $cancelArgs -RedirectStandardOutput ($activeDir+'.stdout') -RedirectStandardError ($activeDir+'.stderr')
try {
    $watch=[Diagnostics.Stopwatch]::StartNew()
    $runningGuest=$null
    while(-not $cancelProcess.HasExited -and $watch.ElapsedMilliseconds -lt 60000) {
        $activeJournal=Join-Path $activeDir 'actions.jsonl'
        if(Test-Path -LiteralPath $activeJournal){
            $readStream=[IO.FileStream]::new($activeJournal,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite)
            $reader=[IO.StreamReader]::new($readStream)
            try{$journalText=$reader.ReadToEnd()}finally{$reader.Dispose()}
            foreach($line in $journalText.Split("`n")) {
                if(-not $line.Trim()){continue}
                try{$event=$line|ConvertFrom-Json}catch{continue}
                if($null -eq $event){continue}
                if($event.PSObject.Properties['event'] -and $event.event -eq 'guest-start' -and $event.image -eq (Join-Path $activeDir 'program.cdx')){$runningGuest=$event.pid}
            }
        }
        if($runningGuest){break}
        Start-Sleep -Milliseconds 50
    }
    Check ($null -ne $runningGuest) 'cancellation control reaches running test guest'
    [IO.File]::WriteAllText($activeCancel,'cancel',$utf8)
    Check ($cancelProcess.WaitForExit(10000)) 'in-flight cancellation terminates promptly'
    $record=Get-Content (Join-Path $activeDir 'record.json') -Raw -Encoding utf8|ConvertFrom-Json
    Check ($record.result.verdict -ceq 'skip') 'in-flight cancellation remains skip'
    Check ($null -eq (Get-Process -Id $runningGuest -ErrorAction SilentlyContinue)) 'cancelled guest is gone'
} finally {if(-not $cancelProcess.HasExited){$cancelProcess.Kill($true);$cancelProcess.WaitForExit()};$cancelProcess.Dispose()}
for($i=0;$i -lt 3;$i++){[void](Run-Case "following-control-$i" $arith $arithExpected 'pass')}
$results.ToArray() | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $OutDir 'checks.json') -Encoding utf8
Write-Host "build-actions: $($results.Count) checks passed"
