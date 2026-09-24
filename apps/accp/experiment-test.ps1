[CmdletBinding()]
param([string]$Bundle='')
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
if(-not $Bundle){$Bundle=Join-Path $PSScriptRoot 'build-output'}
$work=Join-Path $PSScriptRoot ('build-output/experiment-test-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work|Out-Null
$utf8=[Text.UTF8Encoding]::new($false)
$start=[Diagnostics.ProcessStartInfo]::new((Get-Command pwsh).Source)
$start.UseShellExecute=$false;$start.CreateNoWindow=$true
$start.RedirectStandardInput=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
foreach($arg in @('-NoProfile','-File',(Join-Path $PSScriptRoot 'serve.ps1'),'-Bundle',$Bundle,'-Mcp','-Journal',(Join-Path $work 'journal.jsonl'))){$start.ArgumentList.Add($arg)}
$p=[Diagnostics.Process]::Start($start);$p.StandardInput.AutoFlush=$true
$errors=$p.StandardError.ReadToEndAsync();$pass=0;$fail=0;$sequence=0
function Assert([string]$Name,[bool]$Ok,[string]$Detail=''){
    if($Ok){$script:pass++;Write-Host "PASS $Name"}else{$script:fail++;Write-Host "FAIL $Name $Detail"}
}
function Call([string]$Method,$Params=@{}){
    $script:sequence++
    $request=@{jsonrpc='2.0';id=$sequence;method=$Method;params=$Params}|ConvertTo-Json -Depth 15 -Compress
    [IO.File]::AppendAllText((Join-Path $work 'requests.jsonl'),$request+"`n",$utf8)
    $p.StandardInput.WriteLine($request)
    $read=$p.StandardOutput.ReadLineAsync()
    if(-not $read.Wait(35000)){throw 'Experiment response timeout'}
    $line=$read.GetAwaiter().GetResult();if(-not $line){throw 'Experiment server closed'}
    [IO.File]::AppendAllText((Join-Path $work 'responses.jsonl'),$line+"`n",$utf8)
    $line|ConvertFrom-Json
}
function Run([string]$Source,[string]$Output='json'){
    $r=Call 'tools/call' @{name='codex_run';arguments=@{source=$Source;library='experiment';output=$Output;limits=@{ms=10000}}}
    if($r.result.structuredContent.status -ne 'ok'){throw ($r.result.structuredContent|ConvertTo-Json -Depth 10 -Compress)}
    return $r.result.structuredContent
}
function Unit([string]$Expression,[string]$Definitions=''){
    "Chapter: Fixture`n  cites Accp chapter AccpExperiment`nSection: Definitions`n$Definitions`nSection: Entry`n  opening : [Console] Nothing = act`n    print-line-uni (experiment-json ($Expression))`n  end`nPage 1`n"
}
$osc=@'
  oscillator : Real, List Real -> List Real
  oscillator (t) (y) = [list-at y 1, 0.0 - list-at y 0]
'@
$ode='experiment-ode oscillator 0.0 [1.0,0.0] 6.0 120 2 ["position","velocity"] ["m","m/s"] [0.00001,0.00001] ["unit angular frequency"]'
try{
    $null=Call 'initialize' @{protocolVersion='2025-11-25';clientInfo=@{name='experiment-proof';version='1'};capabilities=@{}}
    $p.StandardInput.WriteLine('{"jsonrpc":"2.0","method":"notifications/initialized"}')
    $r=Call 'resources/list';Assert 'experiment resource discoverable' ($r.result.resources.uri -contains 'codex://accp/experiments')
    $r=Call 'resources/read' @{uri='codex://accp/experiments'};$card=$r.result.contents[0].text
    foreach($name in @('UnitsExample','Oscillator')){
        $source=[regex]::Match($card,'(?s)Chapter: '+$name+'\n.*?Page 1\n').Value
        if(-not $source){throw "Missing example $name"}
        $out=Run $source
        Assert "copied example $name" ($out.result.status -eq 'ok') ($out|ConvertTo-Json -Depth 8 -Compress)
        if($name -eq 'Oscillator'){
            $t=$out.result.settings.t1;$omega=[Math]::Sqrt(4.0-0.2*0.2)
            $x=[Math]::Exp(-0.2*$t)*([Math]::Cos($omega*$t)+0.2/$omega*[Math]::Sin($omega*$t))
            $v=-4.0/$omega*[Math]::Exp(-0.2*$t)*[Math]::Sin($omega*$t)
            Assert 'damped oscillator matches analytic solution' ([Math]::Abs($out.result.value[0]-$x) -lt 1e-6 -and [Math]::Abs($out.result.value[1]-$v) -lt 1e-6)
        }
    }
    $r=(Run (Unit 'experiment-quantity (quantity-add (quantity 3.0 "ft") (quantity 1.0 "m")) "m" ["conversion"]')).result
    Assert 'mixed length conversion' ($r.status -eq 'ok' -and [Math]::Abs($r.value[0]-1.9144) -lt 1e-14)
    Assert 'structured metadata' ($r.schema -eq 'accp-experiment-1' -and $r.metadata_authority -eq 'program-reported' -and $r.units[0] -eq 'm' -and $r.convergence -eq 'not_checked' -and $null -eq $r.tolerance)
    foreach($bits in @('1','9218868437227405311','-4503599627370497','-9223372036854775808')){
        $r=(Run (Unit ('experiment-scalar (MathValue (bits-to-real '+$bits+')) "1" "binary64 boundary" []'))).result
        Assert "lossless binary64 $bits" ($r.status -eq 'ok' -and $r.binary64.value[0] -ceq $bits)
        Assert "finite decimal preview $bits" ([double]::IsFinite([double]$r.value[0]))
        if($bits -eq '1'){Assert 'subnormal decimal preview remains nonzero' ($r.value[0] -gt 0)}
    }
    $r=(Run (Unit 'experiment-quantity (quantity-mul (quantity 5.0 "kg") (quantity 2.0 "m/s^2")) "N" []')).result
    Assert 'derived force dimensions' ($r.status -eq 'ok' -and $r.value[0] -eq 10.0)
    $r=(Run (Unit 'experiment-quantity (quantity 60.0 "mph") "m/s" []')).result
    Assert 'speed conversion' ([Math]::Abs($r.value[0]-26.8224) -lt 1e-12)
    foreach($expr in @('experiment-quantity (quantity-add (quantity 1.0 "m") (quantity 1.0 "s")) "m" []','experiment-quantity (quantity 1.0 "m") "kg" []','experiment-quantity (quantity 20.0 "C") "K" []','experiment-quantity (quantity-div (quantity 1.0 "m") (quantity 0.0 "s")) "m/s" []')){
        $r=(Run (Unit $expr)).result;Assert 'unit/domain refusal' ($r.status -eq 'error' -and $null -eq $r.value -and $r.samples.Count -eq 0)
    }
    $out=Run (Unit $ode $osc);$r=$out.result
    Assert 'coupled oscillator refinement' ($r.status -eq 'ok' -and $r.convergence -eq 'refinement_passed' -and $r.refinement_difference[0] -gt 0 -and $r.refinement_difference[0] -le $r.tolerance[0])
    Assert 'trajectory grid and settings' ($r.coordinates.Count -eq 61 -and $r.coordinates[0] -eq 0 -and $r.coordinates[-1] -eq 6 -and $r.settings.fine_steps -eq 240 -and $r.settings.published -eq 'fine')
    $maxError=0.0
    for($i=0;$i -lt $r.coordinates.Count;$i++){$t=$r.coordinates[$i];$maxError=[Math]::Max($maxError,[Math]::Abs($r.samples[$i][0]-[Math]::Cos($t)));$maxError=[Math]::Max($maxError,[Math]::Abs($r.samples[$i][1]+[Math]::Sin($t)))}
    Assert 'oscillator trajectory matches analytic solution' ($maxError -lt 3e-7) "max absolute error $maxError"
    [IO.File]::WriteAllText((Join-Path $work 'oscillator.json'),($r|ConvertTo-Json -Depth 10),$utf8)
    $r=(Run (Unit ($ode.Replace('6.0 120 2','-1.0 120 2')) $osc)).result
    Assert 'backward integration' ($r.coordinates[-1] -eq -1 -and [Math]::Abs($r.value[0]-[Math]::Cos(1)) -lt 1e-8 -and [Math]::Abs($r.value[1]-[Math]::Sin(1)) -lt 1e-8)
    $r=(Run (Unit ($ode.Replace('6.0 120 2','6.0 1 1')) $osc)).result
    Assert 'nonconvergence retains explicitly flagged trajectory' ($r.status -eq 'ok' -and $r.convergence -eq 'not_converged' -and $r.samples.Count -eq 2)
    $bad=@'
  oscillator : Real, List Real -> List Real
  oscillator (t) (y) = [0.0]
'@
    $r=(Run (Unit ($ode.Replace('6.0 120 2','0.0 120 2')) $bad)).result
    Assert 'zero interval does not evaluate callback' ($r.status -eq 'ok' -and $r.convergence -eq 'not_applicable' -and $r.samples.Count -eq 1 -and $r.value[0] -eq 1)
    $r=(Run (Unit $ode $bad)).result;Assert 'wrong derivative shape fails' ($r.status -eq 'error' -and $r.reason -match 'vector length')
    $bad=$bad.Replace('[0.0]','[bits-to-real 9218868437227405312,0.0]')
    $r=(Run (Unit $ode $bad)).result;Assert 'nonfinite derivative fails' ($r.status -eq 'error' -and $r.reason -match 'non-finite')
    foreach($expr in @($ode.Replace('120 2','1025 5'),$ode.Replace('120 2','120 7'),$ode.Replace('[0.00001,0.00001]','[0.0,0.00001]'),$ode.Replace('120 2','1024 1'))){
        $r=(Run (Unit $expr $osc)).result;Assert 'ODE bound/tolerance refusal' ($r.status -eq 'error' -and $null -eq $r.value)
    }
    $mutator=@'
  oscillator : Real, List Real -> List Real
  oscillator (t) (y) = let changed = list-set-at y 0 999.0 in [0.0,0.0]
'@
    $r=(Run (Unit $ode $mutator)).result;Assert 'callback mutation cannot change saved state' ($r.status -eq 'ok' -and $r.value[0] -eq 1 -and $r.samples[0][0] -eq 1 -and $r.settings.initial[0] -eq 1)
    $maxDef=@'
  steady : Real, List Real -> List Real
  steady (t) (y) = [0.0,0.0,0.0,0.0,0.0,0.0,0.0,0.0]
'@
    $maxExpr='experiment-ode steady 0.0 [1.0,2.0,3.0,4.0,5.0,6.0,7.0,8.0] 1.0 1024 16 ["a","b","c","d","e","f","g","h"] ["1","1","1","1","1","1","1","1"] [0.001,0.001,0.001,0.001,0.001,0.001,0.001,0.001] []'
    $r=(Run (Unit $maxExpr $maxDef)).result
    Assert 'maximum step and component boundary completes' ($r.status -eq 'ok' -and $r.convergence -eq 'refinement_passed' -and $r.samples.Count -eq 65 -and $r.value[7] -eq 8)
    $sweepDef=@'
  range : Real -> MathResult
  range (degrees) = physics-projectile-range 20.0 (degrees * math-pi / 180.0) 9.81
'@
    $sweep='experiment-sweep range [60.0,15.0,45.0,15.0] "deg" "m" ["same height, no drag"]'
    $r=(Run (Unit $sweep $sweepDef)).result
    Assert 'sweep preserves order and duplicates' ($r.status -eq 'ok' -and $r.coordinates[0] -eq 60 -and $r.coordinates[1] -eq 15 -and $r.coordinates[3] -eq 15 -and $r.samples[1][0] -eq $r.samples[3][0])
    Assert 'sweep matches projectile model' ([Math]::Abs($r.samples[2][0]-400.0/9.81) -lt 1e-12 -and $r.convergence -eq 'not_checked')
    $r=(Run (Unit ($sweep.Replace('[60.0,15.0,45.0,15.0]','[15.0,-1.0,45.0]')) $sweepDef)).result
    Assert 'failed sweep does not publish partial values' ($r.status -eq 'error' -and $null -eq $r.value -and $r.samples.Count -eq 0)
    foreach($plot in @(@('oscillator',$ode,$osc),@('sweep',$sweep,$sweepDef))){
        $source="Chapter: PlotFixture`n  cites Accp chapter AccpExperiment`nSection: Model`n$($plot[2])`nSection: Entry`n  opening : [Console] Nothing = act`n    when experiment-svg ($($plot[1])) 0`n      is PlotSvg (svg) -> print-line-uni svg`n      is PlotError (why) -> print-line-uni why`n  end`nPage 1`n"
        $out=Run $source 'text';$svg=[xml]$out.stdout
        Assert "$($plot[0]) SVG valid and passive" ($svg.DocumentElement.LocalName -eq 'svg' -and $svg.SelectNodes('//*[local-name()="script" or local-name()="foreignObject"] | //@href').Count -eq 0)
        $points=$svg.SelectSingleNode('//*[local-name()="polyline"]').GetAttribute('points') -split ' '
        $inside=$true
        foreach($point in $points){$xy=$point -split ',';$x=[double]::Parse($xy[0],[Globalization.CultureInfo]::InvariantCulture);$y=[double]::Parse($xy[1],[Globalization.CultureInfo]::InvariantCulture);if($x -lt 89.999 -or $x -gt 760.001 -or $y -lt 69.999 -or $y -gt 390.001){$inside=$false}}
        Assert "$($plot[0]) SVG points inside axes" $inside
        [IO.File]::WriteAllText((Join-Path $work ($plot[0]+'.svg')),$out.stdout,$utf8)
    }
    $constantDef="  constant : Real -> MathResult`n  constant (x) = MathValue (-2.0)"
    $constant='experiment-sweep constant [0.0] "s" "m" []'
    $source="Chapter: ConstantPlot`n  cites Accp chapter AccpExperiment`nSection: Model`n$constantDef`nSection: Entry`n  opening : [Console] Nothing = act`n    when experiment-svg ($constant) 0`n      is PlotSvg (svg) -> print-line-uni svg`n      is PlotError (why) -> print-line-uni why`n  end`nPage 1`n"
    $out=Run $source 'text';$svg=[xml]$out.stdout
    Assert 'constant single-point plot is visible' ($svg.SelectNodes('//*[local-name()="circle"]').Count -eq 1)
    $hostile=$ode.Replace('["position","velocity"]','["<script>&test</script>","velocity"]')
    $source="Chapter: EscapedPlot`n  cites Accp chapter AccpExperiment`nSection: Model`n$osc`nSection: Entry`n  opening : [Console] Nothing = act`n    when experiment-svg ($hostile) 0`n      is PlotSvg (svg) -> print-line-uni svg`n      is PlotError (why) -> print-line-uni why`n  end`nPage 1`n"
    $out=Run $source 'text';$svg=[xml]$out.stdout
    Assert 'SVG label markup is text' ($svg.SelectNodes('//*[local-name()="script"]').Count -eq 0 -and $svg.DocumentElement.InnerText.Contains('<script>&test</script>'))
    $source=@'
Chapter: PrettyJson
Section: Entry
  opening : [Console] Nothing = act
    print-line-uni "{"
    print-line-uni "  \"message\": \"a b\\n c\","
    print-line-uni "  \"values\": [1, 2]"
    print-line-uni "}"
  end
Page 1
'@
    $r=Run $source
    Assert 'multiline JSON preserves strings and original stdout' ($r.result.message -ceq "a b`n c" -and $r.result.values[1] -eq 2 -and $r.stdout.Contains("{`n"))
    $source="Chapter: BadJson`nSection: Entry`n  opening : Integer = 42`nPage 1`n"
    $r=Call 'tools/call' @{name='codex_run';arguments=@{source=$source;output='json'}}
    Assert 'JSON output refuses a non-object with retained stdout' ($r.result.isError -and $r.result.structuredContent.stage -eq 'result' -and $r.result.structuredContent.stdout -eq "42`n")
    $p.StandardInput.Close();if(-not $p.WaitForExit(10000)){throw 'Shutdown timeout'}
    $err=$errors.GetAwaiter().GetResult();[IO.File]::WriteAllText((Join-Path $work 'stderr.log'),$err,$utf8)
    Assert 'clean shutdown' ($p.ExitCode -eq 0)
    $starts=@([regex]::Matches($err,'heap_start=(\d+)')|ForEach-Object{$_.Groups[1].Value}|Sort-Object -Unique)
    Assert 'experiment request heap checkpoint restored' ($starts.Count -eq 1)
}finally{
    if(-not $p.HasExited){$p.Kill($true)}
    if($errors.Wait(5000)){[IO.File]::WriteAllText((Join-Path $work 'stderr.log'),$errors.GetAwaiter().GetResult(),$utf8)}
    $p.Dispose()
}
Write-Host "Experiment proof: $pass pass, $fail fail. Artifacts: $work"
if($fail -gt 0){exit 1}
