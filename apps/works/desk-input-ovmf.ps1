[CmdletBinding()]
param([Parameter(Mandatory)][string]$Cdx,[Parameter(Mandatory)][string]$OutDir)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
Set-Location $repo
$run = [IO.Path]::GetFullPath($OutDir)
if (-not (Test-Path $run -PathType Container)) { throw 'Output directory must exist' }
$tagHash = 0
foreach ($ch in $repo.ToLowerInvariant().ToCharArray()) { $tagHash = ($tagHash * 31 + [int]$ch) % 200 }
$port = 55000 + $tagHash * 2
$serialPort = $port + 1
foreach ($p in @($port,$serialPort)) {
    if (Get-NetTCPConnection -LocalPort $p -State Listen -ErrorAction SilentlyContinue) { throw "Proof port $p already held" }
}
& pwsh -NoProfile -File (Join-Path $repo 'build/cdx-to-pe.ps1') -CdxInput $Cdx -Out "$run/proof.efi" -HeapPages 32768 -ExitBootServices
if ($LASTEXITCODE -ne 0) { throw 'PE conversion failed' }
& pwsh -NoProfile -File (Join-Path $repo 'build/build-img.ps1') -PeInput "$run/proof.efi" -Out "$run/boot.img"
if ($LASTEXITCODE -ne 0) { throw 'Image build failed' }
(Get-FileHash "$run/boot.img" -Algorithm SHA256).Hash | Set-Content "$run/image.sha256"
Copy-Item -LiteralPath 'D:\Program Files\qemu\share\edk2-x86_64-code.fd' -Destination "$run/code.fd"
Copy-Item -LiteralPath 'D:\Program Files\qemu\share\edk2-i386-vars.fd' -Destination "$run/vars.fd"
Set-ItemProperty -LiteralPath "$run/vars.fd" -Name IsReadOnly -Value $false
$freeKB = (Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory
Write-Output "free physical KB: $freeKB; guests: 1"
if ($freeKB -le 1572864) { throw 'RAM admission refused' }
$qargs = @('-accel','tcg','-m','2048','-machine','q35,i8042=off','-drive',"if=pflash,format=raw,unit=0,readonly=on,file=$run/code.fd",'-drive',"if=pflash,format=raw,unit=1,file=$run/vars.fd",'-drive',"format=raw,file=$run/boot.img",'-device','qemu-xhci,id=xhci','-device','usb-kbd,bus=xhci.0','-device','usb-mouse,bus=xhci.0','-serial',"tcp:127.0.0.1:$serialPort,server,nowait",'-monitor',"tcp:127.0.0.1:$port,server,nowait",'-display','none','-vga','std','-no-reboot')
$err = [IO.Path]::GetTempFileName()
$err | Set-Content "$run/stderr-path.txt"
$quotedArgs = $qargs | ForEach-Object { '"' + $_ + '"' }
$guest = Start-Process -FilePath 'D:\Program Files\qemu\qemu-system-x86_64.exe' -ArgumentList $quotedArgs -WindowStyle Hidden -PassThru -RedirectStandardError $err
$serialClient = $null
$monitor = $null
try {
    Set-Content "$run/guest.pid" $guest.Id
    Write-Output "QEMU PID: $($guest.Id); trace: $run/trace.log"
    Start-Sleep -Milliseconds 500
    $serialClient = [Net.Sockets.TcpClient]::new('127.0.0.1',$serialPort)
    $serialStream = $serialClient.GetStream()
    $serialText = [Text.StringBuilder]::new()
    function Read-Serial {
        $buffer = [byte[]]::new(8192)
        while ($serialStream.DataAvailable) {
            $n = $serialStream.Read($buffer,0,$buffer.Length)
            if ($n -le 0) { break }
            [void]$serialText.Append([Text.Encoding]::ASCII.GetString($buffer,0,$n))
            if ($serialText.Length -gt 1048576) { throw 'Serial capture exceeds proof bound' }
        }
        [IO.File]::WriteAllText("$run/trace.log",$serialText.ToString())
        return $serialText.ToString()
    }
    function Wait-Marker([string]$marker,[int]$seconds) {
        $watch = [Diagnostics.Stopwatch]::StartNew()
        do {
            Start-Sleep -Milliseconds 5
            $body = Read-Serial
            if ($guest.HasExited) { throw 'QEMU exited before proof completed' }
        } until ($body.Contains($marker) -or $watch.Elapsed.TotalSeconds -gt $seconds)
        if (-not $body.Contains($marker)) { throw "Missing marker: $marker" }
    }
    Wait-Marker 'waiting for F1 anchor' 45
    $monitor = [Net.Sockets.TcpClient]::new('127.0.0.1',$port)
    $stream = $monitor.GetStream()
    function Read-Monitor {
        $reply = [Text.StringBuilder]::new()
        $buffer = [byte[]]::new(8192)
        $watch = [Diagnostics.Stopwatch]::StartNew()
        do {
            while ($stream.DataAvailable) {
                $n = $stream.Read($buffer,0,$buffer.Length)
                if ($n -le 0) { throw 'QEMU monitor disconnected' }
                [void]$reply.Append([Text.Encoding]::ASCII.GetString($buffer,0,$n))
                if ($reply.Length -gt 65536) { throw 'Monitor reply exceeds proof bound' }
            }
            if ($reply.ToString().EndsWith('(qemu) ')) { return $reply.ToString() }
            Start-Sleep -Milliseconds 1
        } while ($watch.ElapsedMilliseconds -lt 2000 -and -not $guest.HasExited)
        throw 'QEMU monitor reply timed out'
    }
    $banner = Read-Monitor
    function Send-Monitor([string]$line) {
        $bytes = [Text.Encoding]::ASCII.GetBytes($line + "`n")
        $stream.Write($bytes,0,$bytes.Length)
        $stream.Flush()
        return Read-Monitor
    }
    function Read-Words([long]$address,[int]$count) {
        $reply = Send-Monitor ('xp /{0}gx 0x{1:x}' -f $count,$address)
        $clean = [regex]::Replace($reply,'\x1b\[[0-?]*[ -/]*[@-~]','').Replace("`r",'')
        $values = @([regex]::Matches($clean,'(?mi)^(?:0x)?[0-9a-f]+:\s*((?:0x[0-9a-f]+[ \t]*)+)') | ForEach-Object {
            [regex]::Matches($_.Groups[1].Value,'(?i)0x([0-9a-f]+)') | ForEach-Object { [Convert]::ToInt64($_.Groups[1].Value,16) }
        })
        if ($values.Count -ne $count) { throw "Unreadable guest state: $reply" }
        return $values
    }
    $layout = [regex]::Match($serialText.ToString(),'input-proof queue=(\d+) events=(\d+) pointer=(\d+) capacity=(\d+) event-bytes=(\d+)')
    if (-not $layout.Success) { throw 'Missing input-proof state layout' }
    $queue = [long]$layout.Groups[1].Value
    $events = [long]$layout.Groups[2].Value
    $pointer = [long]$layout.Groups[3].Value
    $capacity = [long]$layout.Groups[4].Value
    $eventBytes = [long]$layout.Groups[5].Value
    $script:missingInput = $false
    function Wait-Input([string]$name,[scriptblock]$ready) {
        $watch = [Diagnostics.Stopwatch]::StartNew()
        do {
            if (& $ready) { "collected: $name"; return }
            Start-Sleep -Milliseconds 1
        } while ($watch.ElapsedMilliseconds -lt 150)
        $script:missingInput = $true
        "not collected within driver wait: $name"
    }
    function Wait-Pointer([long]$x,[long]$y,[long]$buttons) {
        Wait-Input "pointer $x,$y buttons $buttons" {
            $state = @(Read-Words $pointer 3)
            $state[0] -eq $x -and $state[1] -eq $y -and $state[2] -eq $buttons
        }
    }
    $reply = Send-Monitor 'sendkey f1 10'
    Wait-Marker 'input-proof anchored' 10
    $reply = Send-Monitor 'mouse_move 10 5'
    Wait-Pointer 10 5 0
    $reply = Send-Monitor 'mouse_move 2 3'
    Wait-Pointer 12 8 0
    $reply = Send-Monitor 'mouse_button 1'
    Wait-Pointer 12 8 1
    $reply = Send-Monitor 'sendkey a 30'
    Wait-Input 'key release' {
        $state = @(Read-Words $queue 2)
        if ($state[0] -lt 0 -or $state[0] -ge $capacity -or $state[1] -lt 0 -or $state[1] -gt $capacity) { throw 'Invalid guest queue state' }
        $tail = ($state[0] + $state[1] + $capacity - 1) % $capacity
        $event = @(Read-Words ($events + $tail * $eventBytes) 6)
        $event[0] -eq 3 -and $event[5] -eq 158
    }
    $reply = Send-Monitor 'mouse_move 8 6'
    Wait-Pointer 20 14 1
    $reply = Send-Monitor 'mouse_button 0'
    Wait-Pointer 20 14 0
    Wait-Marker 'trace-end' 45
    if ($script:missingInput -and $serialText.ToString().Contains('input-proof ready mode=0 ')) { throw 'Positive arm missed an input acknowledgement' }
} catch {
    $_ | Out-String | Set-Content "$run/failure.log"
    throw
} finally {
    if (-not $guest.HasExited) { Stop-Process -Id $guest.Id -Force }
    if (-not $guest.WaitForExit(5000)) { throw "QEMU PID $($guest.Id) did not exit after termination" }
    $guest.WaitForExit()
    $guest.Dispose()
    if ($serialClient) { $serialClient.Dispose() }
    if ($monitor) { $monitor.Dispose() }
    Move-Item -LiteralPath $err -Destination "$run/qemu.err"
}
