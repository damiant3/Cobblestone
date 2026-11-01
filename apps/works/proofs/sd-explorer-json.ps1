[CmdletBinding()]
param([string]$Kernel='seed/Codex.cdx',[string]$OutDir='')
$ErrorActionPreference='Stop'
$repo=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
Set-Location $repo
if(-not $OutDir){$OutDir=Join-Path $repo ('build-output/sd-explorer-json-'+[guid]::NewGuid().ToString('N'))}
$r=[IO.Path]::GetFullPath($OutDir)
if(Test-Path $r){throw 'Choose a fresh output directory'}
New-Item -ItemType Directory $r|Out-Null
Copy-Item $PSCommandPath "$r/runner.ps1"
Copy-Item (Resolve-Path $Kernel).Path "$r/kernel.cdx"
Copy-Item "$PSScriptRoot/sd-explorer-json.expected" "$r/expected.txt"
& pwsh -NoProfile -File build/bundle-app.ps1 -Src "$PSScriptRoot/sd-explorer-json.codex.txt" -Out "$r/bundled.codex" -InputsOut "$r/inputs.txt"
if($LASTEXITCODE -ne 0){throw 'Bundle failed'}
$source=[IO.File]::ReadAllText("$r/bundled.codex").Replace("`r",'')
$anchor="  opening : [Console] Integer`n  opening = act"
if($source.IndexOf($anchor) -lt 0 -or $source.IndexOf($anchor) -ne $source.LastIndexOf($anchor)){throw 'Production entry anchor not unique'}
$source=$source.Replace($anchor,"  sd-explorer-console-entry : [Console] Integer`n  sd-explorer-console-entry = act")
[IO.File]::WriteAllText("$r/probe.codex",$source,[Text.UTF8Encoding]::new($false))
function Admit {
  $m=Get-CimInstance Win32_OperatingSystem
  "freeKB=$($m.FreePhysicalMemory); commitKB=$($m.FreeVirtualMemory); guests=1"
  if($m.FreePhysicalMemory -le 1572864 -or $m.FreeVirtualMemory -le 3145728){throw 'Memory admission refused'}
}
try {
  Admit
  & pwsh -NoProfile -File build/compile.ps1 -Src "$r/probe.codex" -Out "$r/probe.cdx" -Log "$r/compile.log" -Kernel "$r/kernel.cdx"
  if($LASTEXITCODE -ne 0){throw 'Compile failed'}
  Admit
  & pwsh -NoProfile -File build/test-run.ps1 -Kernel "$r/probe.cdx" -OutFile "$r/actual.txt"
  if($LASTEXITCODE -ne 0){throw 'Execution failed'}
  $actual=[IO.File]::ReadAllText("$r/actual.txt").Replace("`r",'').Trim()
  $expected=[IO.File]::ReadAllText("$r/expected.txt").Replace("`r",'').Trim()
  if($actual -cne $expected){throw 'Handler output differs from the exact oracle'}
  $lines=$actual -split "`n"
  if($lines.Count -ne 8 -or $lines[7] -cne 'sd-json-end'){throw 'Wrong response count or completion marker'}
  $docs=[Collections.Generic.List[object]]::new()
  try {
    for($i=0;$i -lt 7;$i++){
      $prefix=if($i -eq 0 -or $i -eq 4){'400 application/json '}else{'200 application/json '}
      if(-not $lines[$i].StartsWith($prefix)){throw 'Wrong response status/type'}
      $docs.Add([Text.Json.JsonDocument]::Parse([string]$lines[$i].Substring($prefix.Length)))
    }
    $want='"\'+"`n"+[char]0+'café À Ω 中'
    $cfg=$docs[2].RootElement
    $values=@($docs[0].RootElement.GetProperty('error').GetString(),$cfg.GetProperty('models')[0].GetProperty('title').GetString(),$cfg.GetProperty('models')[0].GetProperty('name').GetString(),$cfg.GetProperty('samplers')[0].GetString(),$cfg.GetProperty('loras')[0].GetProperty('name').GetString(),$cfg.GetProperty('upscalers')[0].GetString(),$cfg.GetProperty('current_model').GetString(),$docs[3].RootElement.GetProperty('current_model').GetString())
    foreach($value in $values){if($value -cne $want){throw 'Decoded string differs from supplied characters'}}
    if($docs[1].RootElement.GetString() -cne '' -or $docs[6].RootElement.GetString() -cne 'plain/ascii'){throw 'Empty/plain control differs'}
    if($docs[4].RootElement.GetProperty('error').GetString() -cne 'prompt_hash must be 12 lowercase hex digits'){throw 'Hostile prompt_hash not refused'}
    $file='ABx_euler_s8_c2_lora_none_seed424242.png'
    $url=$docs[5].RootElement.GetProperty('cache_url').GetString()
    if($docs[5].RootElement.GetProperty('file').GetString() -cne $file -or $url -cne ('/cache/0123456789ab/'+$file)){throw 'Decoded cache key or URL differs'}
    if($url -cnotmatch '^/cache/([0-9a-f]{12})/([\w.+-]+\.png)$'){throw 'cache_url is not one the host cache route serves'}
  }finally{foreach($doc in $docs){$doc.Dispose()}}
  Get-FileHash "$r/kernel.cdx","$r/bundled.codex","$r/probe.codex","$r/probe.cdx","$r/actual.txt"|Select-Object Path,Hash|ConvertTo-Json|Set-Content "$r/artifacts.json"
  '0'|Set-Content "$r/exit.code"
  'PASS: exact output and independent JSON parse preserve all supplied strings'
}catch{'1'|Set-Content "$r/exit.code";throw}
