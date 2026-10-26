param([string]$Since = '00:00:00', [string]$Until = '99:99:99')
# One circuit run from the newest relay log: phases by time, sewing pace, refusals, idle gaps, thread vendors.
$log = (Get-ChildItem D:\Projects\Cobblestone-root\build-output\uoaix\assist -Filter 'assist-2650-*.log' | Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName
$fs = [IO.File]::Open($log, 'Open', 'Read', 'ReadWrite'); $sr = New-Object IO.StreamReader($fs); $all = $sr.ReadToEnd() -split "`n"; $fs.Dispose()
$lines = $all | Where-Object { $_.Length -gt 12 -and $_.Substring(0, 12) -ge $Since -and $_.Substring(0, 12) -le $Until }
function T([string]$l) { [TimeSpan]::Parse($l.Substring(0, 12)) }
function Msg([string]$l) { if ($l -cmatch 'S>C (1C[0-9A-F]+)\s*$') { $b = [Convert]::FromHexString($Matches[1]); if ($b.Length -gt 44 -and $b[3] -eq 0xFF) { return [Text.Encoding]::ASCII.GetString($b, 44, $b.Length - 44).TrimEnd([char]0) } }; $null }
$first = T $lines[0]; $last = T $lines[-1]; "window $($lines[0].Substring(0,12)) to $($lines[-1].Substring(0,12)) ($([int]($last - $first).TotalSeconds) s)"
# Sewing: each kit use (0x06 injected) to its result message.
$made = 0; $failed = 0; $refused = 0; $sewSecs = 0.0; $useAt = $null; $roundGaps = @()
$prevUse = $null
foreach ($l in $lines) {
  if ($l -cmatch 'INJ C>S 06') { $t = T $l; if ($prevUse) { $g = ($t - $prevUse).TotalMilliseconds; if ($g -lt 5000) { $roundGaps += $g } }; $prevUse = $t; $useAt = $t }
  $m = Msg $l; if (-not $m) { continue }
  if ($m -like 'You create*') { $made++ } elseif ($m -like 'Tailoring failed*') { $failed++ } elseif ($m -like 'You must wait*') { $refused++ }
}
$avg = if ($roundGaps.Count) { [int](($roundGaps | Measure-Object -Average).Average) } else { 0 }
"sewing: $made made, $failed failed, $refused 'You must wait'; kit use to kit use median $(if ($roundGaps.Count) { ($roundGaps | Sort-Object)[[int]($roundGaps.Count/2)] } else { 0 }) ms, mean $avg ms over $($roundGaps.Count) rounds"
# Phases from the assist's own lines.
$recalls = @($lines | Where-Object { $_ -cmatch 'ASSIST Circuit: recalled to' }).Count
$walks = @($lines | Where-Object { $_ -cmatch 'ASSIST Walk: planning .* took (\d+) ms' })
$planMs = 0; foreach ($w in $walks) { if ($w -cmatch 'took (\d+) ms') { $planMs += [int]$Matches[1] } }
"travel: $recalls recalls, $($walks.Count) slow plans totalling $planMs ms"
# Idle gaps: no client or injected packet for more than 3 s.
$prev = $null; $gaps = @()
foreach ($l in $lines) { if ($l -cmatch ' (INJ )?C>S ') { $t = T $l; if ($prev -and ($t - $prev).TotalSeconds -gt 3) { $gaps += [pscustomobject]@{ At = $prev.ToString('hh\:mm\:ss'); S = [math]::Round(($t - $prev).TotalSeconds, 1) } }; $prev = $t } }
"idle gaps over 3 s: $($gaps.Count), $([math]::Round(($gaps | Measure-Object S -Sum).Sum, 0)) s in all; longest: " + (($gaps | Sort-Object S -Descending | Select-Object -First 5 | ForEach-Object { "$($_.At) $($_.S)s" }) -join ', ')
# Thread vendors: what each sold and what it had left.
# A buy line names its item ("bought 10 spool of thread for 30 gold"); an older line names none and was thread.
$buys = $lines | Where-Object { $_ -cmatch 'ASSIST Circuit: bought (\d+) ((?:\S+ )*?)for (\d+) gold from (\S+); (\d+) left' }
foreach ($b in $buys) { if ($b -cmatch 'bought (\d+) ((?:\S+ )*?)for (\d+) gold from (\S+); (\d+) left') { $g = @($Matches[1], $Matches[2], $Matches[3], $Matches[4], $Matches[5]); if ($g[1] -eq '' -or $g[1] -match 'thread') { "  buy $($b.Substring(0,8)) $($g[3]): $($g[0]) for $($g[2]) gp, $($g[4]) left" } } }
$sold = $lines | Where-Object { $_ -cmatch 'ASSIST (Circuit|Watch): sold (\d+)' }; foreach ($s in $sold) { if ($s -cmatch 'sold (\d+)( more fancy shirt)? (to (\S+) )?for (\d+) gold( to (\S+))?') { "  sell $($s.Substring(0,8)) $($Matches[1]) for $($Matches[5])" } }
$lines | Where-Object { $_ -cmatch 'ASSIST Circuit: (Circuit ended|.*away|.*no vendor answered|.*not reached|.*under the lowest)' } | ForEach-Object { '  note ' + $_.Substring(0,8) + ' ' + ($_ -replace '^.*ASSIST Circuit: ', '').Substring(0, [Math]::Min(110, ($_ -replace '^.*ASSIST Circuit: ', '').Length)) }
