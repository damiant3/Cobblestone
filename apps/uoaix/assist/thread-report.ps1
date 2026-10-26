# The thread vendors' restock report Damian asks for during a session: for each tailor, the last confirmed thread
# buy-out, the last visit (dry or not), the minute its stock stops growing (81 minutes after the buy-out, the earliest
# reset in the trade history), and when it is due again. A vendor restocks on a fixed period of its own, not a fixed
# time after a buy-out (2026-10-21: 37 dry visits 20 to 48 minutes after a buy-out); every period of 25 to 120 minutes
# and phase that today's buys and dry visits allow is kept, and Median is the middle of their next restocks, Ready the
# share already past. Reads trades-<server>.csv and attempts-<server>.txt under %LOCALAPPDATA%\uoaix-assist; writes
# nothing. The $dups rows are buys the server left unanswered (recorded before main 47877) and are skipped.

$day0=[datetime]'2026-10-21 00:00'; $now=[DateTime]::Now
$dups = @('Brian|2026-10-21 19:51:33','Brian|2026-10-21 21:00:34','Kimmie|2026-10-21 20:46:45')
$buys = Import-Csv "$env:LOCALAPPDATA\uoaix-assist\trades-login.uo1998.com-5000.csv" | Where-Object { $_.kind -eq 'buy' -and $_.item -match 'thread' -and [datetime]$_.time -ge $day0 -and ($dups -notcontains ($_.vendor + '|' + $_.time)) } | ForEach-Object { [pscustomobject]@{S=$_.'vendor serial';V=$_.vendor;At=[datetime]$_.time;Dry=$false;N=[int]$_.amount} }
$att = Get-Content "$env:LOCALAPPDATA\uoaix-assist\attempts-login.uo1998.com-5000.txt" | ForEach-Object { $f=$_ -split "`t"; if ($f.Count -ge 4 -and $f[3] -match 'none') { $a=[DateTime]::new([int64]$f[2]); if ($a -ge $day0) { [pscustomobject]@{S=$f[0];At=$a;Dry=$true;N=0} } } }
$names=@{}; $buys | ForEach-Object { $names[$_.S]=$_.V }
$city=@{Slade='Britain';Michelle='Britain';Tracy='Britain';Jannelle="Nujel'm";Simon='Trinsic';Brian='Moonglow';Nana='Magincia';Ella='Jhelom';Kimmie='Jhelom';Morena='Vesper';Stanley='Ocllo';Lynn='Skara Brae'}
$out=@()
foreach ($s in $names.Keys) { if (-not $city.ContainsKey($names[$s])) { continue }
  $l = @($buys | Where-Object S -eq $s) + @($att | Where-Object S -eq $s) | Sort-Object At
  $e2=@(); foreach ($x in $l) { if (-not $x.Dry -and $e2.Count -gt 0 -and -not $e2[-1].Dry -and ($x.At-$e2[-1].At).TotalMinutes -lt 2) { continue }; $e2 += $x }
  $t0=$e2[0].At; $mins = $e2 | ForEach-Object { [pscustomobject]@{T=($_.At-$t0).TotalMinutes;Dry=$_.Dry} }
  $tnow = ($now-$t0).TotalMinutes; $lastBuyE = $e2 | Where-Object { -not $_.Dry } | Select-Object -Last 1; $lastBuy = ($lastBuyE.At-$t0).TotalMinutes; $lastAny = ($mins | Select-Object -Last 1).T; $lastE=$e2[-1]
  $nexts=@()
  for ($T=25.0; $T -le 120; $T+=0.5) { for ($ph=0.0; $ph -lt $T; $ph+=0.5) {
    $bad=0; $lb=$null
    foreach ($x in $mins) { if ($null -ne $lb) { $n=[math]::Floor(($x.T-$ph)/$T)-[math]::Floor(($lb-$ph)/$T); if (($x.Dry -and $n -gt 0) -or (-not $x.Dry -and $n -eq 0)) { $bad++; break } }; if (-not $x.Dry) { $lb=$x.T } }
    if ($bad -eq 0) { $k=[math]::Floor(($lastBuy-$ph)/$T)+1; $tick=$ph+$k*$T; if ($tick -le $lastAny) { $tick += $T * [math]::Ceiling(($lastAny-$tick)/$T + 0.0001) }; $nexts += $tick }
  } }
  $row=[ordered]@{V=$names[$s];City=$city[$names[$s]];LastBuy="$($lastBuyE.N)@"+$lastBuyE.At.ToString('HH:mm');LastSeen=$lastE.At.ToString('HH:mm')+$(if($lastE.Dry){' dry'}else{''});BuyAgo=[math]::Round(($now-$lastBuyE.At).TotalMinutes);SafeBy=$lastBuyE.At.AddMinutes(81).ToString('HH:mm')}
  if ($nexts.Count -gt 0) { $sorted=$nexts|Sort-Object; $row.Median=$t0.AddMinutes($sorted[[math]::Floor(0.5*$sorted.Count)]).ToString('HH:mm'); $row.Ready=[math]::Round(100*(@($nexts|Where-Object{$_ -le $tnow}).Count)/$nexts.Count) } else { $row.Median='no fit'; $row.Ready=-1 }
  $out += [pscustomobject]$row
}
$out | Sort-Object @{e={$_.Ready};Descending=$true},@{e={$_.BuyAgo};Descending=$true} | Format-Table -AutoSize | Out-String -Width 140
$now.ToString('HH:mm')
