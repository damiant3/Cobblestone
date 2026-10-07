[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Kernel,
    [Parameter(Mandatory=$true)][string]$OutDir,
    [ValidateRange(0,4294967295)][long]$Seed=104729,
    [ValidateRange(1,1000)][int]$Step=50,
    [ValidateRange(1,100000)][int]$Iterations=20000
)
$ErrorActionPreference='Stop'
$sourceKernel=(Resolve-Path -LiteralPath $Kernel).Path
$repoRoot=(Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$proof=if([IO.Path]::IsPathRooted($OutDir)){[IO.Path]::GetFullPath($OutDir)}else{[IO.Path]::GetFullPath((Join-Path (Get-Location).Path $OutDir))}
if(Test-Path -LiteralPath $proof){throw 'Use a new evidence directory'}
New-Item -ItemType Directory -Path $proof|Out-Null
Set-Location $repoRoot
[Environment]::CurrentDirectory=$repoRoot
Copy-Item -LiteralPath $sourceKernel -Destination "$proof/depot.cdx"
. ./build/quire-map.ps1
$vm=$null
function Admit {
    $free=(Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory/1MB
    Add-Content "$proof/train-status.txt" "RAM $free GiB"
    if($free -le 1.5){throw 'RAM admission refused'}
}
try {
    $inputPaths=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($rootSource in @('apps/uoaix/TownNetworkHillTrain.codex','apps/uoaix/TownNetworkHillKernels.codex')) {
        $absolute=Join-Path $repoRoot $rootSource
        [void]$inputPaths.Add($absolute)
        foreach($entry in (Resolve-CiteOrder -RootLines ([IO.File]::ReadAllLines($absolute)) -Repo $repoRoot)) {
            [void]$inputPaths.Add($entry.Path)
        }
    }
    $sourceHashes=[ordered]@{}
    foreach($path in ($inputPaths|Sort-Object)) {
        $sourceHashes[[IO.Path]::GetRelativePath($repoRoot,$path)]=(Get-FileHash -LiteralPath $path).Hash
    }
    $sourceHashes|ConvertTo-Json|Set-Content "$proof/source-hashes.json"
    $generated=Join-Path $proof ("ptx-"+[guid]::NewGuid().ToString('N')+".codex")
    Admit
    & pwsh -NoProfile -File codex/plugs/ptx/run.ps1 -Src apps/uoaix/TownNetworkHillKernels.codex -Out $generated -Compiler "$proof/depot.cdx" -Chapter TownNetworkHillPtx -Name tnh-training-ptx *> "$proof/ptx-generation.log"
    if($LASTEXITCODE -ne 0){throw 'PTX generation failed'}
    if([IO.File]::ReadAllText($generated).Replace("`r",'') -cne [IO.File]::ReadAllText((Join-Path $repoRoot 'apps/uoaix/TownNetworkHillPtx.codex')).Replace("`r",'')){throw 'Checked-in PTX differs from regeneration'}
    Admit
    & pwsh -NoProfile -File build/compile.ps1 -Src apps/uoaix/TownNetworkHillTrain.codex -Out "$proof/train.cdx" -Log "$proof/train-compile.log" -Kernel "$proof/depot.cdx" *> "$proof/train-compile.out"
    if($LASTEXITCODE -ne 0){throw "trainer compile exit $LASTEXITCODE"}
    . ./build/vm-config.ps1
    Admit
    [IO.File]::WriteAllBytes("$proof/config.input",[Text.Encoding]::ASCII.GetBytes("$Seed $Step $Iterations`n"))
    $vmArguments=@('-kernel',('"{0}"' -f "$proof/train.cdx"),'-output',('"{0}"' -f "$proof/train.raw"),'-mem','3072','-headless','-input',('"{0}"' -f "$proof/config.input"))
    $watch=[Diagnostics.Stopwatch]::StartNew()
    $vm=Start-Process -FilePath $script:CodexVmBin -ArgumentList $vmArguments -PassThru -WindowStyle Hidden -RedirectStandardError "$proof/train.stderr"
    Add-Content "$proof/train-status.txt" "GPU trainer PID $($vm.Id)"
    if(-not $vm.WaitForExit(600000)){throw 'Training timeout'}
    $elapsed=$watch.Elapsed.TotalSeconds
    $stderr=[IO.File]::ReadAllText("$proof/train.stderr")
    if($vm.ExitCode -ne 1 -or $stderr -notmatch '(?m)^FINAL: debug_exit_code=0 process_exit=1\r?$'){throw "Abnormal trainer exit $($vm.ExitCode)"}
    if($stderr -match 'DROPPED|CUDA:.*failed|!EXC='){throw 'GPU or capture failure'}
    foreach($kernelName in @('candidate','hidden','output','score','reduce')) {
        if($stderr -notmatch "GPU CENSUS: kernel_tnh_$kernelName\s+launches\s+$($Iterations+1)\s"){throw "Wrong CUDA training launch count: $kernelName"}
    }
    if($stderr -notmatch '(?m)^GPU BUFFERS: live 0  bytes 0\r?$'){throw 'GPU buffers remain live'}
    $lines=[IO.File]::ReadAllLines("$proof/train.raw")
    if($lines -cnotcontains 'METHOD coordinate-hill-v1' -or $lines -cnotcontains "SEED $Seed STEP $Step ITERATIONS $Iterations"){throw 'Wrong hill configuration'}
    if($lines -cnotcontains 'RETAINED-GROWTH 0'){throw 'Hill loop retained guest heap'}
    $previousScore=[long]::MinValue
    $previousAccepted=0
    $lastIteration=-1
    $firstScore=$null
    foreach($line in $lines) {
        if($line -match '^ITERATION (\d+) SCORE (-?\d+) ACCEPTED (\d+)$') {
            $iteration=[long]$Matches[1]; $score=[long]$Matches[2]; $accepted=[long]$Matches[3]
            if($lastIteration -eq -1){$firstScore=$score}
            if($iteration -le $lastIteration -or $score -lt $previousScore -or $accepted -lt $previousAccepted -or $accepted -gt $iteration){throw 'Hill progress is not monotonic'}
            $previousScore=$score; $previousAccepted=$accepted; $lastIteration=$iteration
        }
    }
    $result=@($lines|Where-Object {$_ -match '^RESULT INITIAL (-?\d+) FINAL (-?\d+) ACCEPTED (\d+)$'})
    if($result.Count -ne 1){throw 'Missing final hill result'}
    $null=$result[0] -match '^RESULT INITIAL (-?\d+) FINAL (-?\d+) ACCEPTED (\d+)$'
    $initialScore=[long]$Matches[1]; $finalScore=[long]$Matches[2]; $acceptedCount=[long]$Matches[3]
    if($lastIteration -ne [Math]::Floor($Iterations/1000)*1000 -or $firstScore -ne $initialScore){throw 'Hill progress boundaries differ'}
    if($finalScore -lt $previousScore -or $acceptedCount -lt $previousAccepted){throw 'Final hill result regressed'}
    if($finalScore -le $initialScore -or $acceptedCount -le 0 -or $acceptedCount -gt $Iterations){throw 'No retained hill improvement'}
    if($stderr -notmatch "GPU CENSUS: kernel_tnh_copy\s+launches\s+$acceptedCount\s"){throw 'Accepted copy count differs'}

    if($lines -match '^FAIL' -or $lines -cnotcontains 'PASS GPU hill climbing and CPU export'){throw 'Training did not pass'}
    if($lines -cnotcontains 'MODEL-BEGIN 766' -or $lines -cnotcontains 'MODEL-END'){throw 'Model framing missing'}
    $weights=[Collections.Generic.List[long]]::new()
    foreach($line in $lines) {
        if($line -match '^W (\d+) (-?\d+)$') {
            if([int]$Matches[1] -ne $weights.Count){throw 'Weight index missing or duplicated'}
            $value=[long]$Matches[2]
            if($value -lt -4000 -or $value -gt 4000){throw 'Exported weight outside CPU bound'}
            $weights.Add($value)
        }
    }
    if($weights.Count -ne 766){throw 'Wrong exported parameter count'}
    $weights.ToArray()|ConvertTo-Json|Set-Content "$proof/trained-weights.json"
    $model=[Collections.Generic.List[string]]::new()
    $model.Add('Chapter: TownNetworkWeights')
    $model.Add('  cites Uoaix chapter TownNetwork')
    $model.Add('  cites Foreword chapter Result')
    $model.Add('')
    $model.Add('  tn-trained-model : Integer -> Result TnModel Text')
    $model.Add('  tn-trained-model (unused) =')
    $parts=@(@{name='hw';start=0;end=512},@{name='hb';start=512;end=528},@{name='ow';start=528;end=752},@{name='ob';start=752;end=766})
    foreach($part in $parts) {
        $prefix=if($part.start -eq 0){'    let '}else{'    in let '}
        $model.Add($prefix+$part.name+' = [')
        for($i=$part.start;$i -lt $part.end;$i+=12) {
            $last=[Math]::Min($i+11,$part.end-1)
            $suffix=if($last -lt $part.end-1){','}else{''}
            $model.Add('      '+(($weights.GetRange($i,$last-$i+1)) -join ', ')+$suffix)
        }
        $model.Add('    ]')
    }
    $model.Add('    in tn-model hw hb ow ob')
    [IO.File]::WriteAllText("$proof/TownNetworkWeights.codex",($model -join "`r`n")+"`r`n",[Text.UTF8Encoding]::new($false))
    foreach($relative in $sourceHashes.Keys) {
        if((Get-FileHash -LiteralPath (Join-Path $repoRoot $relative)).Hash -ne $sourceHashes[$relative]){throw "Source moved during training: $relative"}
    }
    $receipt=[ordered]@{
        kernelHash=(Get-FileHash "$proof/depot.cdx").Hash
        trainerHash=(Get-FileHash "$proof/train.cdx").Hash
        outputHash=(Get-FileHash "$proof/train.raw").Hash
        cudaEvidenceHash=(Get-FileHash "$proof/train.stderr").Hash
        weightsHash=(Get-FileHash "$proof/trained-weights.json").Hash
        modelSourceHash=(Get-FileHash "$proof/TownNetworkWeights.codex").Hash
        sourceManifestHash=(Get-FileHash "$proof/source-hashes.json").Hash
        ptxPlugHash=(Get-FileHash 'codex/plugs/ptx/build-output/ptx-plug.cdx').Hash
        vmHash=(Get-FileHash $script:CodexVmBin).Hash
        hostElapsedSeconds=$elapsed
        method='coordinate-hill-v1'
        scoreFunction='tnh-outcome-v1'
        seed=$Seed
        step=$Step
        iterations=$Iterations
        initialScore=$initialScore
        finalScore=$finalScore
        accepted=$acceptedCount
        parameters=$weights.Count
        dataset=($lines|Where-Object {$_ -match '^CORPUS '})
        scores=@($lines|Where-Object {$_ -match '^ITERATION |^RESULT '})
    }
    $receipt|ConvertTo-Json -Depth 5|Set-Content "$proof/train-receipt.json"
    Add-Content "$proof/train-status.txt" "PASS CUDA hill climbing and integer export; seconds $elapsed"
    Set-Content "$proof/train.exit" 0
    Write-Host "PASS: trained model and receipt in $proof"
} catch {
    Add-Content "$proof/train-status.txt" "FAIL $_"
    Set-Content "$proof/train.exit" 1
    exit 1
} finally {
    if($vm -and -not $vm.HasExited){Stop-Process -Id $vm.Id -Force}
}
