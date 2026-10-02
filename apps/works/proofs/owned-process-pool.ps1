[CmdletBinding()]
param(
  [string]$Kernel='seed/Codex.cdx',
  [Parameter(Mandatory)][string]$LegacyStub,
  [string]$OutDir=''
)
$ErrorActionPreference='Stop'
$repo=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
Set-Location $repo
if(-not $OutDir){$OutDir=Join-Path $repo ('build-output/owned-pool-'+[guid]::NewGuid().ToString('N'))}
$r=[IO.Path]::GetFullPath($OutDir)
if(Test-Path $r){throw 'Choose a fresh output directory'}
New-Item -ItemType Directory $r|Out-Null
Copy-Item $PSCommandPath "$r/runner.ps1"
Copy-Item (Resolve-Path $Kernel).Path "$r/kernel.cdx"
Copy-Item (Resolve-Path $LegacyStub).Path "$r/legacy.ps1"
Copy-Item build/cdx-to-pe.ps1 "$r/owned.ps1"
& pwsh -NoProfile -File build/bundle-app.ps1 -Src apps/works/proofs/owned-process-pool.codex -Out "$r/template.codex" -InputsOut "$r/inputs.txt"
if($LASTEXITCODE -ne 0){throw 'Probe snapshot failed'}
$template=[IO.File]::ReadAllText("$r/template.codex")
$kernelHash=(Get-FileHash "$r/kernel.cdx").Hash
$results=[Collections.Generic.List[object]]::new()
$placements=@{}
function Admit {
  $m=Get-CimInstance Win32_OperatingSystem
  "freeKB=$($m.FreePhysicalMemory); commitKB=$($m.FreeVirtualMemory); guests=1"
  if($m.FreePhysicalMemory -le 1572864 -or $m.FreeVirtualMemory -le 3145728){throw 'Memory admission refused'}
}
function Read-Live([string]$path){
  if(-not(Test-Path $path)){return ''}
  $f=[IO.File]::Open($path,'Open','Read','ReadWrite');$reader=[IO.StreamReader]::new($f)
  try{$reader.ReadToEnd()}finally{$reader.Dispose();$f.Dispose()}
}
function Compile-Probe([int]$tamper){
  $path="$r/probe-$tamper"
  if(Test-Path "$path.cdx"){return "$path.cdx"}
  $source=$template.Replace('opp-tamper : Integer = 0',"opp-tamper : Integer = $tamper")
  if($tamper -ne 0 -and $source -ceq $template){throw 'Tamper anchor missing'}
  [IO.File]::WriteAllText("$path.codex",$source,[Text.UTF8Encoding]::new($false))
  Admit | Out-Host
  & pwsh -NoProfile -File build/compile.ps1 -Src "$path.codex" -Out "$path.cdx" -Log "$path.compile.log" -Kernel "$r/kernel.cdx" | Out-Host
  if($LASTEXITCODE -ne 0){Get-Content "$path.compile.log"|Out-Host;throw 'Probe compilation failed'}
  return "$path.cdx"
}
function Match-LoaderFootprint([string]$before,[string]$after){
  $old=[IO.File]::ReadAllBytes($before);$new=[IO.File]::ReadAllBytes($after)
  $oldAt=[BitConverter]::ToInt32($old,60)+80
  $newAt=[BitConverter]::ToInt32($new,60)+80
  $oldSize=[BitConverter]::ToUInt32($old,$oldAt)
  $newSize=[BitConverter]::ToUInt32($new,$newAt)
  if($newSize -lt $oldSize -or $new.Length -lt $old.Length){throw 'Owned PE footprint unexpectedly smaller'}
  $record=@{originalHash=(Get-FileHash $before).Hash;originalSize=$oldSize;matchedSize=$newSize;originalLength=$old.Length;matchedLength=$new.Length}
  [Array]::Copy([BitConverter]::GetBytes($newSize),0,$old,$oldAt,4)
  $padded=[byte[]]::new($new.Length)
  [Array]::Copy($old,0,$padded,0,$old.Length)
  [IO.File]::WriteAllBytes($before,$padded)
  return $record
}
try {
  $arms=@(
    @{name='original-auto';legacy=$true;pages=4096;mem=1536;heap=0;tamper=0;refuse=''},
    @{name='owned-auto';legacy=$false;pages=4096;mem=1536;heap=0;tamper=0;refuse='';pair='original-auto'},
    @{name='owned-inside';legacy=$false;pages=4096;mem=3072;heap=1107296256;tamper=0;refuse=''},
    @{name='original-desktop';legacy=$true;pages=131072;mem=1536;heap=0;tamper=0;refuse=''},
    @{name='owned-desktop';legacy=$false;pages=131072;mem=1536;heap=0;tamper=0;refuse='';pair='original-desktop'},
    @{name='relocation-control';legacy=$false;pages=4096;mem=3072;heap=1610612736;tamper=0;refuse='';pair='original-auto'},
    @{name='fx-canary-control';legacy=$false;pages=131072;mem=1536;heap=0;tamper=10;refuse=''},
    @{name='span-refused';legacy=$false;pages=4096;mem=3072;heap=0;tamper=1;refuse='OUT OF MEMORY'},
    @{name='overlap-refused';legacy=$false;pages=4096;mem=3072;heap=1107296256;tamper=2;refuse='OUT OF MEMORY'},
    @{name='alignment-refused';legacy=$false;pages=4096;mem=3072;heap=0;tamper=3;refuse='OUT OF MEMORY'},
    @{name='root-range-refused';legacy=$false;pages=4096;mem=3072;heap=0;tamper=4;refuse='OUT OF MEMORY'},
    @{name='pool-refused';legacy=$false;pages=131072;mem=768;heap=0;tamper=0;refuse='Q'}
  )
  foreach($arm in $arms){
    $cdx=Compile-Probe $arm.tamper
    $dir="$r/$($arm.name)";New-Item -ItemType Directory $dir|Out-Null
    $script=if($arm.legacy){"$r/legacy.ps1"}else{"$r/owned.ps1"}
    $extra=@(if(-not $arm.legacy){'-OwnedProcessPool'})
    & pwsh -NoProfile -File $script -CdxInput $cdx -Out "$dir/probe.efi" -HeapPages $arm.pages -HeapAt $arm.heap -ExitBootServices @extra
    if($LASTEXITCODE -ne 0){throw 'PE conversion failed'}
    $footprint=$null
    if($arm.legacy){
      & pwsh -NoProfile -File "$r/owned.ps1" -CdxInput $cdx -Out "$dir/layout.efi" -HeapPages $arm.pages -HeapAt $arm.heap -ExitBootServices -OwnedProcessPool
      if($LASTEXITCODE -ne 0){throw 'Paired PE layout failed'}
      $footprint=Match-LoaderFootprint "$dir/probe.efi" "$dir/layout.efi"
      $footprint|ConvertTo-Json|Set-Content "$dir/loader-footprint.json"
    }
    & pwsh -NoProfile -File build/build-img.ps1 -PeInput "$dir/probe.efi" -Out "$dir/probe.img"
    if($LASTEXITCODE -ne 0){throw 'Image build failed'}
    Copy-Item 'D:/Program Files/qemu/share/edk2-x86_64-code.fd' "$dir/code.fd"
    Copy-Item 'D:/Program Files/qemu/share/edk2-i386-vars.fd' "$dir/vars.fd"
    (Get-Item "$dir/vars.fd").IsReadOnly=$false
    $imageHash=(Get-FileHash "$dir/probe.img").Hash
    $firmwareHash=(Get-FileHash "$dir/code.fd").Hash
    $listener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0);$listener.Start();$port=$listener.LocalEndpoint.Port;$listener.Stop()
    $qa=@('-accel','tcg','-machine','pc','-m',"$($arm.mem)",'-smp','1','-drive',"if=pflash,format=raw,unit=0,readonly=on,file=$dir/code.fd",'-drive',"if=pflash,format=raw,unit=1,file=$dir/vars.fd",'-drive',"format=raw,file=$dir/probe.img",'-serial',"file:$dir/serial.log",'-monitor',"tcp:127.0.0.1:$port,server,nowait",'-display','none','-no-reboot')
    Admit
    $guest=Start-Process 'D:/Program Files/qemu/qemu-system-x86_64.exe' -WindowStyle Hidden -PassThru -ArgumentList ($qa|ForEach-Object{'"'+$_+'"'}) -RedirectStandardError "$dir/guest.err"
    $guest.Id|Set-Content "$dir/guest.pid"
    "Owned $($arm.name) PID=$($guest.Id); log=$dir/serial.log"
    try {
      $watch=[Diagnostics.Stopwatch]::StartNew()
      do {
        $body=Read-Live "$dir/serial.log"
        if($body.Contains('owned-pool-end') -or ($arm.refuse -eq 'Q' -and $body -match 'Q$') -or ($arm.refuse -eq 'OUT OF MEMORY' -and $body.Contains($arm.refuse))){break}
        if($guest.HasExited -or $body.Contains('!EXC')){throw "Guest failed: $body"}
        Start-Sleep -Milliseconds 200
      }while($watch.Elapsed.TotalSeconds -lt 45)
      $body
      if($arm.refuse){
        $expected=if($arm.refuse -eq 'Q'){$body -match 'Q$'}else{$body.Contains($arm.refuse)}
        if(-not $expected -or $body.Contains('setup=')){throw 'Expected pre-entry refusal absent'}
        if($arm.refuse -eq 'Q'){
          $client=[Net.Sockets.TcpClient]::new('127.0.0.1',$port)
          try{$stream=$client.GetStream();$bytes=[Text.Encoding]::ASCII.GetBytes("screendump $dir/refused.ppm`n");$stream.Write($bytes,0,$bytes.Length);$stream.Flush();Start-Sleep -Milliseconds 700}finally{$client.Dispose()}
          & pwsh -NoProfile -File build/boot/ppm2png.ps1 -Ppm "$dir/refused.ppm" -Out "$dir/refused.png"
          if($LASTEXITCODE -ne 0){throw 'Refusal capture failed'}
        }
      }else{
        $at=$body.IndexOf('setup=')
        if($at -lt 0){throw 'No setup verdict'}
        $got=$body.Substring($at).Replace("`r",'').Trim()
        $verdict=if($arm.legacy){'False'}else{'True'}
        $fx=if($arm.tamper -eq 10){'False'}else{'True'}
        $want="setup=$verdict`npool-layout=True`nordinary=$verdict`npriority=$verdict`ncustom=$verdict`nreused=$verdict`npreempted=$verdict`nsentinel=True`nlegacy-fx-untouched=$fx`nowned-pool-end"
        if($got -cne $want){throw "Unexpected verdict: $($arm.name)"}
        $values=@{}
        foreach($key in @('root-lo','root-hi','pool','entry-heap')){
          $m=[regex]::Match($body,"(?m)$key=(\d+)")
          if(-not $m.Success){throw "Missing address: $key"}
          $values[$key]=[long]$m.Groups[1].Value
        }
        if($values['root-lo'] -ne $values['entry-heap']){throw 'Root measurements are not comparable'}
        if($arm.heap -ne 0 -and $values['root-lo'] -ne $arm.heap){throw 'Fixed placement was not exercised'}
        if($arm.name -in @('owned-desktop','fx-canary-control') -and -not $body.Contains('fx-covered=True')){throw 'Legacy FX locations were not covered'}
        if($arm.pair){
          $prior=$placements[$arm.pair]
          $same=$values['entry-heap'] -eq $prior['entry-heap'] -and $values['root-hi'] -eq $prior['root-hi']
          if($same -eq ($arm.name -eq 'relocation-control')){throw 'Root preservation comparison failed'}
          "root-preserved=$same"
        }
        $placements[$arm.name]=$values
      }
      if((Get-FileHash "$r/kernel.cdx").Hash -ne $kernelHash){throw 'Compiler changed during proof'}
      $results.Add([pscustomobject]@{arm=$arm.name;grade='PASS';kernel=$kernelHash;source=(Get-FileHash ([IO.Path]::ChangeExtension($cdx,'.codex'))).Hash;cdx=(Get-FileHash $cdx).Hash;stub=(Get-FileHash $script).Hash;image=$imageHash;firmware=$firmwareHash;parameters=$arm;addresses=$placements[$arm.name];loaderFootprint=$footprint})
      $results.ToArray()|ConvertTo-Json -Depth 6|Set-Content "$r/results.json"
    }finally{if(-not $guest.HasExited){Stop-Process -Id $guest.Id};$guest.WaitForExit();$guest.Dispose()}
  }
  '0'|Set-Content "$r/exit.code"
}catch{'1'|Set-Content "$r/exit.code";throw}
