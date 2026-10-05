[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ClientRoot,
    [ValidateRange(1024,65535)][int]$Port = 2595,
    [ValidateRange(1000,60000)][int]$RequestTimeoutMs = 5000,
    [string]$DecorationFile = ''
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $ClientRoot).Path
$names = @('MAP0.MUL','STAIDX0.MUL','STATICS0.MUL','TILEDATA.MUL','MULTI.IDX','MULTI.MUL')
$files = [Collections.Generic.List[IO.FileStream]]::new()
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $Port)
$buffer = [byte[]]::new(65536)
$header = [byte[]]::new(4096)
$decoration = $null
try {
    if ($DecorationFile) {
        $decorationPath = (Resolve-Path -LiteralPath $DecorationFile).Path
        if ((Get-Item -LiteralPath $decorationPath).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Refusing decoration reparse point' }
        $decoration = [IO.File]::Open($decorationPath,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
        if ($decoration.Length -lt 64 -or $decoration.Length -gt 11534400) { throw 'Decoration file outside DWD1 budget' }
    }
    foreach ($name in $names) {
        $path = Join-Path $root $name
        if ((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Refusing reparse point: $name" }
        $files.Add([IO.File]::Open($path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read))
    }
    if ($files[0].Length -ne 77070336 -or $files[1].Length -ne 4718592 -or $files[3].Length -ne 1036288 -or $files[4].Length -ne 49152) {
        throw 'Expected legacy 6144x4096 map, 32-bit tiledata flags and 4096 multi indices'
    }
    $listener.Start()
    Write-Output "UOAIX MUL range service 127.0.0.1:$Port; read-only; client files remain in $root"
    while ($true) {
        $client = $listener.AcceptTcpClient()
        try {
            $client.ReceiveTimeout = $RequestTimeoutMs
            $client.SendTimeout = $RequestTimeoutMs
            $stream = $client.GetStream()
            $used = 0
            $complete = $false
            while ($used -lt $header.Length) {
                $value = $stream.ReadByte()
                if ($value -lt 0) { break }
                $header[$used++] = $value
                if ($used -ge 4 -and $header[$used-4] -eq 13 -and $header[$used-3] -eq 10 -and $header[$used-2] -eq 13 -and $header[$used-1] -eq 10) { $complete = $true; break }
            }
            $status = '400 Bad Request'
            $body = [Text.Encoding]::ASCII.GetBytes('invalid request')
            $length = $body.Length
            if ($complete) {
                $line = ([Text.Encoding]::ASCII.GetString($header,0,$used) -split "`r`n",2)[0]
                if ($line -match '^GET /decoration/([0-9]{1,10})/([0-9]{1,5}) HTTP/1\.[01]$' -and $null -ne $decoration) {
                    $offset = [long]$Matches[1]
                    $count = [int]$Matches[2]
                    if ($count -gt 65536 -or $offset -gt $decoration.Length -or $count -gt $decoration.Length-$offset) {
                        $status = '416 Range Not Satisfiable'
                    } else {
                        [void]$decoration.Seek($offset,[IO.SeekOrigin]::Begin)
                        $read = 0
                        while ($read -lt $count) {
                            $n = $decoration.Read($buffer,$read,$count-$read)
                            if ($n -eq 0) { throw 'Short decoration read' }
                            $read += $n
                        }
                        $body = $buffer
                        $length = $count
                        $status = '200 OK'
                    }
                } elseif ($line -match '^GET /size/([0-5]) HTTP/1\.[01]$') {
                    $body = [BitConverter]::GetBytes([long]$files[[int]$Matches[1]].Length)
                    $length = 8
                    $status = '200 OK'
                } elseif ($line -match '^GET /mul/([0-5])/([0-9]{1,10})/([0-9]{1,5}) HTTP/1\.[01]$') {
                    $file = $files[[int]$Matches[1]]
                    $offset = [long]$Matches[2]
                    $count = [int]$Matches[3]
                    if ($count -gt 65536 -or $offset -gt $file.Length -or $count -gt $file.Length-$offset) {
                        $status = '416 Range Not Satisfiable'
                        $body = [Text.Encoding]::ASCII.GetBytes('range outside file or exceeds 65536 bytes')
                        $length = $body.Length
                    } else {
                        [void]$file.Seek($offset,[IO.SeekOrigin]::Begin)
                        $read = 0
                        while ($read -lt $count) {
                            $n = $file.Read($buffer,$read,$count-$read)
                            if ($n -eq 0) { throw 'Short MUL read' }
                            $read += $n
                        }
                        $body = $buffer
                        $length = $count
                        $status = '200 OK'
                    }
                }
            }
            $reply = [Text.Encoding]::ASCII.GetBytes("HTTP/1.0 $status`r`nContent-Type: application/octet-stream`r`nContent-Length: $length`r`nConnection: close`r`n`r`n")
            $stream.Write($reply,0,$reply.Length)
            $stream.Write($body,0,$length)
        } catch {
            [Console]::Error.WriteLine("MUL request refused: $($_.Exception.Message)")
        } finally {
            $client.Dispose()
        }
    }
} finally {
    $listener.Stop()
    foreach ($file in $files) { $file.Dispose() }
    if ($null -ne $decoration) { $decoration.Dispose() }
}
