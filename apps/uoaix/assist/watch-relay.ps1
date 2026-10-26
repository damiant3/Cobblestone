# The relay watch for a Monitor (CurrentPlan blu row): pwsh -File watch-relay.ps1 -RelayPid <pid> -Log <newest assist-2650-*.log>.
# One line per circuit, Keep, Tools, house or weave note, timing report, alarm, takeover, break, a carry-limit crossing,
# or the relay gone (then it exits). Reads the log shared; writes nothing. -Replay reads the log from its start and ends
# at its end, for checking the watch against a log already written.
param([int]$RelayPid, [string]$Log, [switch]$Replay)
$pat = '(?i)ASSIST (Circuit|Lumber: |Houses: |House: |Work: \d+ done|Work: .*(bolt|did not|lifting|came back)|Mill, spin and weave)|Tools: |Timing REPORT|Keep: (?!asking)|Circuit ended|took over|alarm|BROKEN|HALT|RELAY SLOW (S>C|C>S) |DESYNC|exception|forcibly'
$fs = [IO.File]::Open($Log, 'Open', 'Read', 'ReadWrite'); if (-not $Replay) { $fs.Seek(0, 'End') | Out-Null }; $r = [IO.StreamReader]::new($fs)
$lastWeight = $null
# An item lifted (0x07, the assist's or the client's) and not yet dropped (0x08) or refused (0x27) is held, and the server
# counts it in the weight: a bunny hop's 420-log pile read 962 of 418 stones (2026-10-23 23:54:52) between its lift and its
# drop, and until the server's next status (0x11) after the drop: a sample in the drop's own millisecond read 2456
# (23:30:21.934). A crossing is said only from a sample with nothing held and the weight settled.
$held = $false; $settling = $false
while ($true) {
    $line = $r.ReadLine()
    if ($null -ne $line) {
        if ($line -match ' C>S 07') { $held = $true; continue }
        if ($line -match ' C>S 08' -or $line -match ' S>C 27') { $held = $false; $settling = $true; continue }
        if ($line -match ' S>C 11') { if (-not $held) { $settling = $false }; continue }
        if ($line -match ' -- STATE player .* str (\d+) .* weight (\d+) ') {
            if ($held -or $settling) { continue }
            $limit = [int]$Matches[1] * 4 + 30; $w = [int]$Matches[2]; $over = $w -gt $limit
            if ($null -ne $lastWeight -and $over -ne $lastWeight) { [Console]::Out.WriteLine($(if ($over) { "ENCUMBERED: $w of $limit stones" } else { "UNENCUMBERED: $w of $limit stones" })); [Console]::Out.Flush() }
            $lastWeight = $over
            continue
        }
        if ($line -match $pat -and $line -notmatch 'has no metal after|has no wood; back|has no wood; kept empty|logs put down') { [Console]::Out.WriteLine($line.Substring(0, [Math]::Min(240, $line.Length))); [Console]::Out.Flush() }
        continue
    }
    if ($Replay) { exit 0 }
    if (-not (Get-Process -Id $RelayPid -ErrorAction SilentlyContinue)) { [Console]::Out.WriteLine("RELAY PID $RelayPid GONE"); [Console]::Out.Flush(); exit 1 }
    Start-Sleep -Milliseconds 500
}
