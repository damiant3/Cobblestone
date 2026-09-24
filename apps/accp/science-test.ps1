[CmdletBinding()]
param([string]$Bundle='')
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
if(-not $Bundle){$Bundle=Join-Path $PSScriptRoot 'build-output'}
$Bundle=(Resolve-Path $Bundle).Path
$work=Join-Path $PSScriptRoot ('build-output/science-test-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work|Out-Null
$journal=Join-Path $work 'journal.jsonl'
$utf8=[Text.UTF8Encoding]::new($false)
$start=[Diagnostics.ProcessStartInfo]::new((Get-Command pwsh).Source)
$start.UseShellExecute=$false;$start.CreateNoWindow=$true
$start.RedirectStandardInput=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
foreach($arg in @('-NoProfile','-File',(Join-Path $PSScriptRoot 'serve.ps1'),'-Bundle',$Bundle,'-Mcp','-Journal',$journal)){$start.ArgumentList.Add($arg)}
$p=[Diagnostics.Process]::Start($start)
$p.StandardInput.AutoFlush=$true
$errors=$p.StandardError.ReadToEndAsync()
$pass=0;$fail=0
function Assert([string]$Name,[bool]$Ok,[string]$Detail=''){
    if($Ok){$script:pass++;Write-Host "PASS $Name"}else{$script:fail++;Write-Host "FAIL $Name $Detail"}
}
function Call($Id,[string]$Method,$Params=@{}){
    $request=@{jsonrpc='2.0';id=$Id;method=$Method;params=$Params}|ConvertTo-Json -Depth 12 -Compress
    [IO.File]::AppendAllText((Join-Path $work 'requests.jsonl'),$request+"`n",$utf8)
    $p.StandardInput.WriteLine($request)
    $read=$p.StandardOutput.ReadLineAsync()
    if(-not $read.Wait(35000)){throw 'Science response timeout'}
    $line=$read.GetAwaiter().GetResult()
    if(-not $line){throw 'Science server exited'}
    [IO.File]::AppendAllText((Join-Path $work 'responses.jsonl'),$line+"`n",$utf8)
    $answer=$line|ConvertFrom-Json
    return $answer
}
function Tool($Id,[string]$Name,$Arguments){Call $Id 'tools/call' @{name=$Name;arguments=$Arguments}}
function Unit([string[]]$Calls){
    @'
Chapter: ScienceFixture
Section: Functions
  cubic : Real -> Real
  cubic (x) = x * x * x
  polynomial : Real -> Real
  polynomial (x) = x * x - 2.0
  decay : Real, Real -> Real
  decay (t) (y) = 0.0 - y
  invalid : Real -> Real
  invalid (x) = bits-to-real 9218868437227405312
  emit : Text, MathResult -> [Console] Nothing
  emit (name) (r) = act
    when r
      is MathValue (x) -> print-line-uni (name & "|value|" & show (real-to-bits x))
      is MathError (why) -> print-line-uni (name & "|error|" & why)
  end
Section: Entry
  opening : [Console] Nothing = act
'@ + "`n" + ($Calls -join "`n") + "`n  end`nPage 1`n"
}
function CheckCases([string]$Library,$Cases){
    $calls=@($Cases|ForEach-Object{'    emit "'+$_.name+'" ('+$_.code+')'})
    $source=Unit $calls
    $r=Tool $Library 'codex_run' @{source=$source;library=$Library;limits=@{ms=10000}}
    Assert "$Library execution" ($r.result.structuredContent.status -eq 'ok') ($r|ConvertTo-Json -Depth 8 -Compress)
    if($r.result.structuredContent.status -ne 'ok'){return}
    $out=$r.result.structuredContent
    $rows=@($out.stdout -split "`n"|Where-Object{$_})
    Assert "$Library output covers every case" ($rows.Count -eq $Cases.Count)
    for($i=0;$i -lt [Math]::Min($rows.Count,$Cases.Count);$i++){
        $parts=$rows[$i] -split '\|',3;$case=$Cases[$i]
        if($case.ContainsKey('error')){Assert $case.name ($parts[0] -ceq $case.name -and $parts[1] -eq 'error' -and $parts[2] -match $case.error) $rows[$i]}
        else{
            $actual=if($parts[1] -eq 'value'){[BitConverter]::Int64BitsToDouble([long]$parts[2])}else{[double]::NaN}
            $tolerance=$case.tolerance
            Assert $case.name ($parts[0] -ceq $case.name -and [double]::IsFinite($actual) -and [Math]::Abs($actual-$case.expected) -le $tolerance) "actual=$actual expected=$($case.expected) tolerance=$tolerance"
        }
    }
    $libraryText=[IO.File]::ReadAllText((Join-Path $Bundle ($Library+'.codex')))
    $hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($utf8.GetBytes($libraryText+"`n"+$source))).ToLowerInvariant()
    Assert "$Library compiled source identity" ($out.library -ceq $Library -and $out.compiled_source_sha256 -ceq $hash)
}
try{
    $null=Call 'init' 'initialize' @{protocolVersion='2025-11-25';clientInfo=@{name='science-proof';version='1'};capabilities=@{}}
    $p.StandardInput.WriteLine('{"jsonrpc":"2.0","method":"notifications/initialized"}')
    $r=Call 'resources' 'resources/list'
    Assert 'science resource discoverable' ($r.result.resources.uri -contains 'codex://accp/science')
    $r=Call 'science' 'resources/read' @{uri='codex://accp/science'}
    $card=$r.result.contents[0].text
    Assert 'science card states units and approximation' ($card -match 'SI convention' -and $card -match 'MathError' -and $card -match 'approximations')
    foreach($example in @(@('Answer','math',4.0),@('Projectile','physics',(400.0/9.81)))){
        $source=[regex]::Match($card,'(?s)Chapter: '+$example[0]+'\n.*?Page 1\n').Value
        if(-not $source){throw 'Science card example missing'}
        $r=Tool ('example-'+$example[0]) 'codex_run' @{source=$source;library=$example[1];limits=@{ms=10000}}
        $out=$r.result.structuredContent
        Assert ('card example '+$example[0]) ($out.status -eq 'ok' -and [Math]::Abs([double]::Parse($out.stdout,[Globalization.CultureInfo]::InvariantCulture)-$example[2]) -lt 1e-12)
    }
    $math=@(
        @{name='sqrt';code='math-sqrt 2.0';expected=[Math]::Sqrt(2);tolerance=1e-14},
        @{name='sqrt-subnormal';code='math-sqrt (bits-to-real 1)';expected=[Math]::Sqrt([BitConverter]::Int64BitsToDouble(1));tolerance=1e-175},
        @{name='sin-negative';code='math-sin (-2.7)';expected=[Math]::Sin(-2.7);tolerance=2e-14},
        @{name='sin-boundary';code='math-sin 100.0';expected=[Math]::Sin(100);tolerance=2e-13},
        @{name='cos-octant';code='math-cos 0.8';expected=[Math]::Cos(0.8);tolerance=2e-14},
        @{name='atan-quadrant';code='math-atan2 (-1.0) (-1.0)';expected=[Math]::Atan2(-1,-1);tolerance=2e-14},
        @{name='negative-power';code='math-pow 2.0 (-10)';expected=[Math]::Pow(2,-10);tolerance=0.0},
        @{name='exp-negative';code='math-exp (-3.0)';expected=[Math]::Exp(-3);tolerance=2e-15},
        @{name='exp-upper';code='math-exp 700.0';expected=[Math]::Exp(700);tolerance=[Math]::Exp(700)*2e-12},
        @{name='log-small';code='math-log 0.000001';expected=[Math]::Log(0.000001);tolerance=2e-13},
        @{name='log-subnormal';code='math-log (bits-to-real 1)';expected=[Math]::Log([BitConverter]::Int64BitsToDouble(1));tolerance=2e-12},
        @{name='derivative';code='math-derivative cubic 2.0 0.00001';expected=12.0;tolerance=1e-8},
        @{name='integral';code='math-integral cubic 0.0 2.0 100';expected=4.0;tolerance=1e-12},
        @{name='integral-reversed';code='math-integral cubic 2.0 0.0 100';expected=-4.0;tolerance=1e-12},
        @{name='root';code='math-root polynomial 0.0 2.0 0.000000000001 100';expected=[Math]::Sqrt(2);tolerance=2e-12},
        @{name='ode-decay';code='math-ode decay 0.0 1.0 1.0 100';expected=[Math]::Exp(-1);tolerance=5e-10},
        @{name='sqrt-domain';code='math-sqrt (-1.0)';error='nonnegative'},
        @{name='trig-domain';code='math-sin 101.0';error='radians'},
        @{name='log-domain';code='math-log 0.0';error='positive'},
        @{name='power-domain';code='math-pow 0.0 (-1)';error='undefined'},
        @{name='nan-domain';code='math-sqrt (bits-to-real 9221120237041090560)';error='finite'},
        @{name='integral-steps';code='math-integral cubic 0.0 1.0 3';error='even steps'},
        @{name='integral-nonfinite';code='math-integral invalid 0.0 1.0 10';error='non-finite'},
        @{name='root-unbracketed';code='math-root polynomial 2.0 3.0 0.001 10';error='sign change'},
        @{name='root-nonconvergent';code='math-root polynomial 0.0 2.0 0.000000000001 1';error='did not converge'},
        @{name='ode-steps';code='math-ode decay 0.0 1.0 1.0 0';error='steps'}
    )
    CheckCases 'math' $math
    $physics=@(
        @{name='position';code='physics-position 1.0 2.0 3.0 4.0';expected=33.0;tolerance=0.0},
        @{name='force';code='physics-force 2.5 (-4.0)';expected=-10.0;tolerance=0.0},
        @{name='energy';code='physics-kinetic-energy 2.0 3.0';expected=9.0;tolerance=0.0},
        @{name='gravity';code='physics-gravity-force 0.000000000066743 2.0 3.0 2.0';expected=6.6743e-11*1.5;tolerance=1e-24},
        @{name='orbit';code='physics-orbital-speed 100.0 4.0';expected=5.0;tolerance=1e-13},
        @{name='projectile';code='physics-projectile-range 20.0 (math-pi / 4.0) 9.81';expected=400.0/9.81;tolerance=1e-12},
        @{name='spring';code='physics-spring-period 2.0 8.0';expected=[Math]::PI;tolerance=1e-13},
        @{name='wave';code='physics-wave-speed 5.0 2.0';expected=10.0;tolerance=0.0},
        @{name='ohm';code='physics-ohm-current 12.0 4.0';expected=3.0;tolerance=0.0},
        @{name='heat';code='physics-heat 2.0 4.0 (-3.0)';expected=-24.0;tolerance=0.0},
        @{name='orbit-domain';code='physics-orbital-speed 100.0 0.0';error='positive'},
        @{name='gravity-nonfinite';code='physics-gravity-force 1.0 1.0 1.0 (bits-to-real 9218868437227405312)';error='finite'},
        @{name='physics-includes-math';code='math-integral cubic 0.0 2.0 20';expected=4.0;tolerance=1e-12}
    )
    CheckCases 'physics' $physics
    $source="Chapter: Plain`nSection: Entry`n  opening : Integer = 6 * 7`nPage 1`n"
    $r=Tool 'unknown-library' 'codex_run' @{source=$source;library='../private'}
    Assert 'unknown library refused' ($r.result.structuredContent.status -eq 'refused')
    $r=Tool 'wrong-type-library' 'codex_run' @{source=$source;library=@('math')}
    Assert 'library array refused' ($r.result.structuredContent.status -eq 'refused')
    $r=Tool 'standalone' 'codex_run' @{source=$source}
    Assert 'standalone still computes' ($r.result.structuredContent.stdout -ceq "42`n" -and $r.result.structuredContent.library -eq 'none')
    $r=Tool 'source-ceiling' 'codex_run' @{source=($source+(' ' * 50000));library='physics'}
    Assert 'combined source ceiling refused' ($r.result.structuredContent.status -eq 'refused' -and $r.result.structuredContent.stage -eq 'library' -and $null -eq $r.result.structuredContent.compiled_source_sha256)
    $source=Unit @('    emit "check" (math-sqrt 2.0)')
    $r=Tool 'library-check' 'codex_check' @{source=$source;library='math';limits=@{ms=10000}}
    Assert 'library check does not run' ($r.result.structuredContent.status -eq 'ok' -and $r.result.structuredContent.ms.run -eq 0)
    $r=Tool 'library-lens' 'codex_lens' @{source=$source;library='math';limits=@{ms=10000}}
    Assert 'library lens produces WAT' ($r.result.structuredContent.status -eq 'ok' -and $r.result.structuredContent.wat.Contains('(module'))
    $denied=$source.Replace('[Console] Nothing','[Console, Network.Read] Nothing')
    $r=Tool 'library-denied' 'codex_run' @{source=$denied;library='math';limits=@{ms=10000}}
    Assert 'library does not widen effects' ($r.result.structuredContent.status -eq 'refused' -and $r.result.structuredContent.needs -contains 'Network.Read')
    $source=Unit @('    emit "scope" (physics-force 2.0 3.0)')
    $r=Tool 'wrong-scope' 'codex_run' @{source=$source;library='math';limits=@{ms=10000}}
    Assert 'math bundle does not expose physics' ($r.result.structuredContent.status -eq 'diagnostics')
    $p.StandardInput.Close()
    if(-not $p.WaitForExit(10000)){throw 'Science shutdown timeout'}
    $err=$errors.GetAwaiter().GetResult()
    [IO.File]::WriteAllText((Join-Path $work 'stderr.log'),$err,$utf8)
    Assert 'science service clean shutdown' ($p.ExitCode -eq 0)
    $starts=@([regex]::Matches($err,'heap_start=(\d+)')|ForEach-Object{$_.Groups[1].Value}|Sort-Object -Unique)
    Assert 'science requests restore heap checkpoint' ($starts.Count -eq 1)
    $previous='0'*64
    foreach($line in [IO.File]::ReadAllLines($journal)){
        $record=$line|ConvertFrom-Json
        $raw=$line.Substring($line.IndexOf(',"record":')+10);$raw=$raw.Substring(0,$raw.Length-1)
        $hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($utf8.GetBytes($raw))).ToLowerInvariant()
        Assert 'journal fact and chain' ($record.fact -ceq $hash -and $record.record.previous -ceq $previous)
        $previous=$record.fact
    }
}finally{
    if(-not $p.HasExited){$p.Kill($true)}
    $p.Dispose()
}
Write-Host "Science proof: $pass pass, $fail fail. Artifacts: $work"
if($fail -gt 0){exit 1}
