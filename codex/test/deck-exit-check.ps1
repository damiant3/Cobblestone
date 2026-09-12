param(
    [Parameter(Mandatory)][string]$Kernel,
    [Parameter(Mandatory)][string]$WorkDir,
    [switch]$ExpectUnfixed
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot/../..").Path
$Kernel = (Resolve-Path $Kernel).Path
$WorkDir = [IO.Path]::GetFullPath($WorkDir)
if (Test-Path $WorkDir) { throw 'WorkDir must be new.' }
New-Item -ItemType Directory $WorkDir | Out-Null
$boot = [IO.File]::ReadAllText("$repo/codex/compiler/Emit/X86_64Boot.codex")
$cell = [regex]::Match($boot, '(?m)^  deck-bound-counter-addr : Integer = (\d+)\r?$')
if (-not $cell.Success) { throw 'Counter address declaration not found.' }
$counter = [long]$cell.Groups[1].Value
$watch = '0x' + $counter.ToString('x')
@{ kernel=$Kernel; sha256=(Get-FileHash $Kernel).Hash; counterAddress=$counter; expectUnfixed=[bool]$ExpectUnfixed } |
    ConvertTo-Json | Set-Content "$WorkDir/provenance.json"
function Admit {
    $free = (Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1MB
    Write-Host "RAM $free GiB, one guest"
    if ($free -le 1.5) { throw 'RAM admission refused.' }
}
$invalid = @'
Chapter: DeckExitInvalid
  cites Foreword chapter Console
Section: Entry
  opening : [Console] Nothing = act
    print-line-uni "BEFORE"
    let lo = poke-32 COUNTER 0 WORD
    in let hi = poke-32 COUNTER 4 WORD
    in let leave = __deck-exit
    in print-line-uni "AFTER"
  end
'@
$balanced = @'
Chapter: DeckExitBalanced
  cites Foreword chapter Console
Section: Entry
  opening : [Console] Nothing = act
    let before = __heap-save
    in let mark = before + 4096
    in let set = __deck-set mark
    in let enter = __deck-enter
    in let count1 = peek-qword COUNTER 0
    in let on = __heap-save
    in let payload = alloc-bytes 16
    in let filled = __heap-save
    in let leave = __deck-exit
    in let count0 = peek-qword COUNTER 0
    in let after = __heap-save
    in let published = __deck-pos
    in act
      print-line-uni ("counts " & show count1 & " " & show count0)
      print-line-uni (if on == mark & payload == mark & filled == mark + 16 & after == before then "heap restored" else "heap wrong")
      print-line-uni (if published == filled then "deck published" else "deck wrong")
    end
  end
'@
$nested = @'
Chapter: DeckExitNested
  cites Foreword chapter Console
Section: Entry
  opening : [Console] Nothing = act
    let before = __heap-save
    in let mark = before + 4096
    in let set = __deck-set mark
    in let enter1 = __deck-enter
    in let count1 = peek-qword COUNTER 0
    in let outer-payload = alloc-bytes 16
    in let enter2 = __deck-enter
    in let count2 = peek-qword COUNTER 0
    in let inner-payload = alloc-bytes 24
    in let filled = __heap-save
    in let leave1 = __deck-exit
    in let count3 = peek-qword COUNTER 0
    in let inside = __heap-save
    in let leave2 = __deck-exit
    in let count0 = peek-qword COUNTER 0
    in let after = __heap-save
    in let published = __deck-pos
    in act
      print-line-uni ("counts " & show count1 & " " & show count2 & " " & show count3 & " " & show count0)
      print-line-uni (if outer-payload == mark & inner-payload == mark + 16 & filled == mark + 40 & inside == filled & after == before then "heap nested/restored" else "heap wrong")
      print-line-uni (if published == filled then "deck published" else "deck wrong")
    end
  end
'@
$results = @()
foreach ($name in @('zero', 'negative', 'balanced', 'nested')) {
    $source = switch ($name) {
        'zero' { $invalid.Replace('WORD', '0') }
        'negative' { $invalid.Replace('WORD', '(0 - 1)') }
        'balanced' { $balanced }
        'nested' { $nested }
    }
    $source = $source.Replace('COUNTER', $counter.ToString())
    [IO.File]::WriteAllText("$WorkDir/$name.codex", $source)
    Admit
    & pwsh -NoProfile -File "$repo/build/compile.ps1" -Src "$WorkDir/$name.codex" -Out "$WorkDir/$name.cdx" -Log "$WorkDir/$name.compile.log" -Kernel $Kernel *> "$WorkDir/$name.compile.console"
    if ($LASTEXITCODE -ne 0) { throw "Compile failed: $name" }
    Admit
    $run = Start-Process "$repo/tools/codex-vm.exe" -ArgumentList @('-kernel', "$WorkDir/$name.cdx", '-output', "$WorkDir/$name.raw", '-mem', '3072', '-headless', '-hwwatch', $watch, '-hwwatch-len', '8', '-hwwatch-log') -WindowStyle Hidden -PassThru -RedirectStandardError "$WorkDir/$name.vm.log"
    if (-not $run.WaitForExit(30000)) { $run.Kill(); $run.WaitForExit(); throw "Guest timed out: $name" }
    $raw = [IO.File]::ReadAllText("$WorkDir/$name.raw").Replace("`r", '') -replace '^\x01', ''
    $log = [IO.File]::ReadAllText("$WorkDir/$name.vm.log")
    if ($run.ExitCode -notin @(0, 1) -or $log -notmatch '(?m)^FINAL: debug_exit_code=-?\d+ process_exit=(-?\d+)\r?$' -or [int]$matches[1] -ne $run.ExitCode) { throw "Abnormal host termination: $name" }
    if ($log -match 'DROPPED|500 hits, stopping') { throw "Capture/watch failure: $name" }
    $writes = @([regex]::Matches($log, 'HWWATCH #\d+:.*?now=0x([0-9a-fA-F]+)') | ForEach-Object { $_.Groups[1].Value.ToLowerInvariant().PadLeft(16, '0') })
    if (-not $writes.Count) { throw "Watchpoint recorded no counter writes: $name" }
    $last = $writes[-1]
    $fault = $raw -match '!EXC=06\b'
    if ($name -in @('zero', 'negative')) {
        if ($raw -notmatch '(?m)^BEFORE$') { throw "Invalid-state probe not reached: $name" }
        if ($ExpectUnfixed) {
            $expected = if ($name -eq 'zero') { 'ffffffffffffffff' } else { 'fffffffffffffffe' }
            if ($fault -or $raw -notmatch '(?m)^AFTER$' -or $last -ne $expected) { throw "Unfixed control not reproduced: $name" }
        } else {
            $expected = if ($name -eq 'zero') { '0000000000000000' } else { 'ffffffffffffffff' }
            $allowed = if ($name -eq 'zero') { @('0000000000000000') } else { @('0000000000000000', '00000000ffffffff', 'ffffffffffffffff') }
            if (-not $fault -or $raw -match '(?m)^AFTER$' -or $last -ne $expected -or @($writes | Where-Object { $_ -notin $allowed }).Count) { throw "Invalid state was not refused before mutation: $name" }
            if ($name -eq 'negative' -and [Array]::IndexOf($writes, $expected) -ne $writes.Count - 1) { throw 'Counter changed after negative-state setup.' }
        }
    } else {
        $expectedCounts = if ($name -eq 'balanced') { 'counts 1 0' } else { 'counts 1 2 1 0' }
        $expectedHeap = if ($name -eq 'balanced') { 'heap restored' } else { 'heap nested/restored' }
        if ($raw -match '!EXC' -or $raw -notmatch [regex]::Escape($expectedCounts) -or $raw -notmatch [regex]::Escape($expectedHeap) -or $raw -notmatch '(?m)^deck published$' -or $last -ne '0000000000000000' -or '0000000000000001' -notin $writes) { throw "Valid transition failed: $name" }
        if ($name -eq 'nested' -and '0000000000000002' -notin $writes) { throw 'Nested watch control did not observe depth2.' }
    }
    $results += [pscustomobject]@{ case=$name; fault06=$fault; finalCounterHex=$last; counterWrites=$writes; pass=$true }
    Write-Host "$name PASS fault06=$fault final-counter=0x$last"
}
$results | ConvertTo-Json -Depth 5 | Set-Content "$WorkDir/results.json"
Set-Content "$WorkDir/result.txt" 'PASS'
Write-Host 'Deck-exit state controls PASS'
