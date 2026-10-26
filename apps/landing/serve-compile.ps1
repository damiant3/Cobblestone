[CmdletBinding()]
param(
    [string]$Kernel = 'seed\Codex.cdx',
    [string]$SiteDir = 'apps\landing\web\compile',
    [string]$Index = 'prism.html',
    [int]$HostPort = 0,
    [ValidateSet('ne2k', 'e1000')]
    [string]$Card = 'ne2k',
    [int]$MemMB = 3072,
    [int]$Fetches = 30,
    [switch]$Test,
    # The byte check's control: flips one page byte on the disk, so GET / must fail.
    [switch]$SabotageByte
)

# Serves apps/landing/web/compile from our own kernel: CompileServer.codex in
# codex-vm reads a CDXSITE1 disk this script writes and answers HTTP on guest
# port 8080, forwarded to 127.0.0.1:<HostPort>. Local only.
#
#   pwsh apps/landing/serve-compile.ps1            # serve until Ctrl+C
#   pwsh apps/landing/serve-compile.ps1 -Test      # grade, stop, exit 0 or 1

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Set-Location $repo
. (Join-Path $repo 'build\vm-config.ps1')
if ($HostPort -eq 0) { $HostPort = 18080 + (Get-Random -Min 0 -Max 400) }
$out = Join-Path $repo 'build-output\compile-server'
New-Item -ItemType Directory -Force $out | Out-Null

$types = @{ '.html' = 'text/html; charset=utf-8'; '.js' = 'text/javascript'; '.css' = 'text/css'; '.wasm' = 'application/wasm'
            '.json' = 'application/json'; '.txt' = 'text/plain; charset=utf-8'; '.png' = 'image/png'; '.jpg' = 'image/jpeg'
            '.ico' = 'image/x-icon'; '.svg' = 'image/svg+xml' }

function Write-SiteDisk([string]$dir, [string]$index, [string]$path, [switch]$BadMagic) {
    $root = (Resolve-Path $dir).Path
    $files = @(Get-ChildItem $root -Recurse -File | Sort-Object FullName)
    $entries = [System.Collections.Generic.List[object]]::new()
    foreach ($f in $files) {
        $rel = '/' + $f.FullName.Substring($root.Length + 1).Replace('\', '/')
        $type = $types[$f.Extension.ToLowerInvariant()]
        if (-not $type) { throw "no content type for $rel" }
        $entries.Add(@{ Path = $rel; Type = $type; File = $f.FullName })
    }
    if (-not ($entries | Where-Object { $_.Path -eq '/index.html' })) {
        $target = $entries | Where-Object { $_.Path -eq "/$index" }
        if (-not $target) { throw "index $index not in $dir" }
        $entries.Add(@{ Path = '/index.html'; Type = $target.Type; File = $target.File })
    }
    if ($entries.Count -gt 64) { throw "more than 64 files" }
    $hsec = [int][Math]::Ceiling((16 + 128 * $entries.Count) / 512)
    $head = New-Object byte[] ($hsec * 512)
    $magic = if ($BadMagic) { 'CDXSITE0' } else { 'CDXSITE1' }
    [Text.Encoding]::ASCII.GetBytes($magic).CopyTo($head, 0)
    [BitConverter]::GetBytes([uint32]$entries.Count).CopyTo($head, 8)
    [BitConverter]::GetBytes([uint32]$hsec).CopyTo($head, 12)
    $placed = @{}
    $next = $hsec
    $order = [System.Collections.Generic.List[string]]::new()
    for ($i = 0; $i -lt $entries.Count; $i++) {
        $e = $entries[$i]
        if ($e.Path.Length -gt 63 -or $e.Type.Length -gt 31) { throw "field too long: $($e.Path)" }
        if (-not $placed.ContainsKey($e.File)) {
            $len = (Get-Item $e.File).Length
            $placed[$e.File] = @{ Start = $next; Len = $len }
            $order.Add($e.File)
            $next += [int][Math]::Ceiling($len / 512)
        }
        $o = 16 + 128 * $i
        [Text.Encoding]::ASCII.GetBytes($e.Path).CopyTo($head, $o)
        [Text.Encoding]::ASCII.GetBytes($e.Type).CopyTo($head, $o + 64)
        [BitConverter]::GetBytes([uint32]$placed[$e.File].Start).CopyTo($head, $o + 96)
        [BitConverter]::GetBytes([uint32]$placed[$e.File].Len).CopyTo($head, $o + 100)
    }
    $fs = [IO.File]::Create($path)
    try {
        $fs.Write($head, 0, $head.Length)
        foreach ($file in $order) {
            $bytes = [IO.File]::ReadAllBytes($file)
            $fs.Write($bytes, 0, $bytes.Length)
            $pad = (512 - ($bytes.Length % 512)) % 512
            if ($pad) { $fs.Write((New-Object byte[] $pad), 0, $pad) }
        }
    } finally { $fs.Dispose() }
    return $entries.Count
}

function Start-Server([string]$disk, [string]$log) {
    if ((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory -lt 1572864) { throw 'RAM admission failed: under 1.5 GiB free' }
    if (Test-Path $log) { Remove-Item $log -Force }
    $vmArgs = @('-kernel', "`"$cdx`"", '-disk', "`"$disk`"", '-output', "`"$log`"", '-portfwd', "${HostPort}:8080", '-mem', "$MemMB", '-headless')
    if ($Card -eq 'e1000') { $vmArgs += '-e1000-nat' }
    Start-Process -FilePath $script:CodexVmBin -ArgumentList $vmArgs -PassThru -WindowStyle Hidden -RedirectStandardError "$log.err"
}

function Wait-Line([string]$log, [string]$pattern, $proc, [int]$seconds) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        if ((Test-Path $log) -and (Select-String -Path $log -Pattern $pattern -SimpleMatch -Quiet)) { return $true }
        if ($proc.HasExited) { return $false }
        Start-Sleep -Milliseconds 250
    }
    return $false
}

$cdx = Join-Path $out 'compile-server.cdx'
& pwsh -NoProfile -File (Join-Path $repo 'build\compile.ps1') -Src 'apps\landing\CompileServer.codex' -Out $cdx -Log (Join-Path $out 'compile-server.log') -Kernel $Kernel | Out-Null
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $cdx)) { Write-Host 'FAIL: CompileServer.codex did not compile'; exit 1 }
$disk = Join-Path $out 'site.img'
$count = Write-SiteDisk $SiteDir $Index $disk
Write-Host "[serve-compile] $count entries, disk $((Get-Item $disk).Length) bytes"
if ($SabotageByte) {
    $fs = [IO.File]::Open($disk, 'Open', 'ReadWrite')
    try { $fs.Position = 512 * 1 + 1000000; $b = $fs.ReadByte(); $fs.Position -= 1; $fs.WriteByte($b -bxor 1) } finally { $fs.Dispose() }
}
$log = Join-Path $out 'server.log'
$proc = Start-Server $disk $log

if (-not $Test) {
    try {
        if (-not (Wait-Line $log ' bytes; listening on 8080' $proc 120)) { Get-Content $log -ErrorAction SilentlyContinue | Write-Host; Write-Host 'FAIL: the server did not start'; exit 1 }
        Get-Content $log | Select-String '\[compile-server\]' | ForEach-Object { Write-Host $_.Line }
        Write-Host "[serve-compile] OPEN http://127.0.0.1:$HostPort/   (Ctrl+C to stop)"
        while (-not $proc.HasExited) { Start-Sleep -Seconds 1 }
    } finally { if (-not $proc.HasExited) { Stop-VmGraceful -ProcessId $proc.Id } }
    exit 0
}

$fail = 0
function Check($what, $ok, $detail = '') {
    if ($ok) { Write-Host "  PASS  $what $detail" } else { Write-Host "  FAIL  $what $detail"; $script:fail++ }
}
function Get-Http([string]$path) {
    $client = [System.Net.Http.HttpClient]::new()
    $client.Timeout = [TimeSpan]::FromSeconds(600)
    try {
        for ($try = 1; ; $try++) {
            try { $r = $client.GetAsync("http://127.0.0.1:$HostPort$path").GetAwaiter().GetResult(); break }
            catch [System.Net.Http.HttpRequestException] { if ($try -ge 10 -or $proc.HasExited) { throw }; Start-Sleep -Milliseconds 500 }
        }
        return @{ Status = [int]$r.StatusCode; Type = "$($r.Content.Headers.ContentType)"; Body = $r.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult() }
    } finally { $client.Dispose() }
}
function Get-EmbeddedWasm([byte[]]$page) {
    $text = [Text.Encoding]::Latin1.GetString($page)
    $key = '"codex-compiler.wasm": "'
    $i = $text.IndexOf($key)
    if ($i -lt 0) { return $null }
    $j = $text.IndexOf('"', $i + $key.Length)
    return [Convert]::FromBase64String($text.Substring($i + $key.Length, $j - $i - $key.Length))
}
$sha = [System.Security.Cryptography.SHA256]
try {
    Check 'server loaded its site and listens' (Wait-Line $log ' bytes; listening on 8080' $proc 120)
    $want = [IO.File]::ReadAllBytes((Join-Path $SiteDir $Index))
    $proc.Refresh()
    $before = $proc.PeakWorkingSet64
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $root = Get-Http '/'
    $sw.Stop()
    Check 'GET / answers 200' ($root.Status -eq 200) "($($root.Status))"
    Check 'GET / is the page, byte for byte' (($root.Body.Length -eq $want.Length) -and ([Linq.Enumerable]::SequenceEqual($root.Body, $want))) "($($root.Body.Length) of $($want.Length) bytes, $([Math]::Round($sw.Elapsed.TotalSeconds, 1)) s)"
    Check 'GET / is text/html' ($root.Type -eq $types['.html']) "($($root.Type))"
    $named = Get-Http "/$Index"
    Check "GET /$Index is the same page" (($named.Status -eq 200) -and ([Linq.Enumerable]::SequenceEqual($named.Body, $want))) "($($named.Status), $($named.Body.Length) bytes)"
    $wasm = Get-EmbeddedWasm $root.Body
    $ref = Get-EmbeddedWasm $want
    Check 'the served page carries the compiler wasm' ($null -ne $wasm -and $wasm.Length -gt 8 -and $wasm[0] -eq 0 -and $wasm[1] -eq 0x61 -and $wasm[2] -eq 0x73 -and $wasm[3] -eq 0x6D -and $wasm[4] -eq 1) "($(if ($wasm) { $wasm.Length } else { 0 }) bytes)"
    if ($wasm -and $ref) { Check 'the served compiler wasm is the source page''s' ([Convert]::ToHexString($sha::HashData($wasm)) -eq [Convert]::ToHexString($sha::HashData($ref))) "(sha256 $([Convert]::ToHexString($sha::HashData($wasm)).Substring(0, 16)))" }
    $miss = Get-Http '/no-such-file.html'
    Check 'a missing path answers 404' ($miss.Status -eq 404) "($($miss.Status))"
    $peaks = [System.Collections.Generic.List[long]]::new()
    $intact = $true
    for ($k = 1; $k -le $Fetches; $k++) {
        $again = Get-Http '/'
        if (-not [Linq.Enumerable]::SequenceEqual($again.Body, $want)) { $intact = $false }
        $proc.Refresh()
        $peaks.Add($proc.PeakWorkingSet64)
    }
    Check "$Fetches more full fetches are each the page" $intact
    Write-Host ("  peak working set MiB after each fetch: " + (($peaks | ForEach-Object { [Math]::Round($_ / 1MB, 1) }) -join ' '))
    $grown = $peaks[$peaks.Count - 1] - $before
    Check 'a request is bounded: fetches grow the guest under 64 MiB' ($grown -lt 64MB) "(+$([Math]::Round($grown / 1MB, 1)) MiB from $([Math]::Round($before / 1MB, 1)) MiB)"
    $climb = $peaks[$peaks.Count - 1] - $peaks[0]
    Check "the guest holds flat over $Fetches fetches (under 1 MiB)" ($climb -lt 1MB) "(+$([Math]::Round($climb / 1MB, 2)) MiB from fetch 1 to $Fetches)"
} finally {
    if (-not $proc.HasExited) { Stop-VmGraceful -ProcessId $proc.Id }
}

$bad = Join-Path $out 'site-bad.img'
$null = Write-SiteDisk $SiteDir $Index $bad -BadMagic
$badLog = Join-Path $out 'server-bad.log'
$badProc = Start-Server $bad $badLog
try {
    Check 'a disk with a bad magic is refused' (Wait-Line $badLog 'site refused: not a CDXSITE1 disk' $badProc 120)
} finally {
    if (-not $badProc.HasExited) { Stop-VmGraceful -ProcessId $badProc.Id }
}

if ($fail) { Write-Host "serve-compile: $fail FAILED"; exit 1 }
Write-Host 'serve-compile: PASS'
exit 0
