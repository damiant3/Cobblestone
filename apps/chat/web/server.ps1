# server.ps1 -- CodexChat web server
# Serves static files, chat API, and WebSocket for real-time messaging.
[CmdletBinding()]
param([int]$Port = 8280)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$WebDir = $PSScriptRoot
$Repo = (Resolve-Path (Join-Path $WebDir '..\..\..\..')).Path -replace '\\$',''

# ── State ────────────────────────────────────────────────────────
$script:Accounts   = @{}   # name -> {name, token, pubkey, contacts[], convIds[]}
$script:Sessions   = @{}   # token -> name
$script:Convs      = @{}   # convId -> {id, type, members[], messages[], name, created}
$script:NextConvId = 1
$script:NextMsgId  = 1

# ── State Persistence ───────────────────────────────────────────
$StateFile = Join-Path $WebDir 'data\chat-state.json'
function Save-State {
    $state = @{
        accounts  = $script:Accounts
        sessions  = $script:Sessions
        convs     = $script:Convs
        nextConvId = $script:NextConvId
        nextMsgId  = $script:NextMsgId
    }
    $json = $state | ConvertTo-Json -Depth 10 -Compress
    [System.IO.File]::WriteAllText($StateFile, $json)
}
function Load-State {
    if (-not (Test-Path $StateFile)) { return }
    try {
        $s = Get-Content -Raw $StateFile | ConvertFrom-Json
        $script:Accounts   = @{}; foreach ($p in $s.accounts.PSObject.Properties) { $script:Accounts[$p.Name] = $p.Value }
        $script:Sessions   = @{}; foreach ($p in $s.sessions.PSObject.Properties) { $script:Sessions[$p.Name] = $p.Value }
        $script:Convs      = @{}; foreach ($p in $s.convs.PSObject.Properties) { $script:Convs[$p.Name] = $p.Value }
        $script:NextConvId = [int]$s.nextConvId
        $script:NextMsgId  = [int]$s.nextMsgId
        Write-Host "  Loaded $($script:Accounts.Count) accounts, $($script:Convs.Count) conversations"
    } catch { Write-Host "  Warning: could not load state: $_" -ForegroundColor Yellow }
}
Load-State

# ── Auth Helpers ─────────────────────────────────────────────────
function Get-User($token) {
    $name = $script:Sessions[$token]
    if (-not $name) { return $null }
    return $script:Accounts[$name]
}

# ── API Router ───────────────────────────────────────────────────
function Handle-Api($path, $query, $method) {
    $q = @{}
    if ($query) { foreach ($pair in $query.Split('&')) { $kv = $pair.Split('=',2); if ($kv.Count -eq 2) { $q[$kv[0]] = [System.Web.HttpUtility]::UrlDecode($kv[1]) } } }

    if ($path -eq '/api/chat/register') {
        $name = $q['name']
        if (-not $name -or $name.Length -lt 1) { return '{"error":"name required"}' }
        if ($script:Accounts[$name]) {
            $tok = $script:Accounts[$name].token
            $script:Sessions[$tok] = $name
            Save-State
            return "{`"token`":`"$tok`",`"name`":`"$name`"}"
        }
        $tok = [System.Guid]::NewGuid().ToString('N').Substring(0,16)
        $script:Accounts[$name] = @{ name=$name; token=$tok; pubkey=''; contacts=@(); convIds=@() }
        $script:Sessions[$tok] = $name
        Save-State
        return "{`"token`":`"$tok`",`"name`":`"$name`"}"
    }

    if ($path -eq '/api/chat/me') {
        $user = Get-User $q['t']
        if (-not $user) { return '{"error":"auth"}' }
        return "{`"name`":`"$($user.name)`"}"
    }

    if ($path -eq '/api/chat/conversations') {
        $user = Get-User $q['t']
        if (-not $user) { return '{"error":"auth"}' }
        $ids = @($user.convIds)
        $result = @{ count = "$($ids.Count)" }
        for ($i = 0; $i -lt $ids.Count; $i++) {
            $conv = $script:Convs[$ids[$i]]
            if (-not $conv) { continue }
            $other = ($conv.members | Where-Object { $_ -ne $user.name } | Select-Object -First 1)
            if (-not $other) { $other = $conv.name }
            $msgs = @($conv.messages)
            $last = if ($msgs.Count -gt 0) { $msgs[-1] } else { $null }
            $result["name-$i"] = if ($conv.type -eq 'group') { $conv.name } else { "$other" }
            $result["preview-$i"] = if ($last) { "$($last.body)".Substring(0, [Math]::Min(40, "$($last.body)".Length)) } else { 'No messages yet' }
            $result["time-$i"] = if ($last) { $last.time } else { '' }
            $result["id-$i"]  = $ids[$i]
        }
        return ($result | ConvertTo-Json -Compress)
    }

    if ($path -eq '/api/chat/messages') {
        $user = Get-User $q['t']
        if (-not $user) { return '{"error":"auth"}' }
        $convId = $q['conv']
        $conv = $script:Convs[$convId]
        if (-not $conv) { return '{"count":"0"}' }
        $msgs = @($conv.messages)
        $result = @{ count = "$($msgs.Count)" }
        for ($i = 0; $i -lt $msgs.Count; $i++) {
            $m = $msgs[$i]
            $result["body-$i"]   = $m.body
            $result["sender-$i"] = $m.sender
            $result["time-$i"]   = $m.time
            $result["mine-$i"]   = if ($m.sender -eq $user.name) { '1' } else { '0' }
        }
        return ($result | ConvertTo-Json -Compress)
    }

    if ($path -eq '/api/chat/send') {
        $user = Get-User $q['t']
        if (-not $user) { return '{"error":"auth"}' }
        $convId = $q['to']
        $body = $q['body']
        if (-not $body) { return '{"error":"empty"}' }
        $conv = $script:Convs[$convId]
        if (-not $conv) { return '{"error":"no conv"}' }
        $msg = @{ id=$script:NextMsgId; sender=$user.name; body=$body; time=(Get-Date -Format 'HH:mm') }
        $script:NextMsgId++
        $conv.messages = @($conv.messages) + @($msg)
        Save-State
        return '{"ok":true}'
    }

    if ($path -eq '/api/chat/start') {
        $user = Get-User $q['t']
        if (-not $user) { return '{"error":"auth"}' }
        $withName = $q['with']
        if (-not $withName) { return '{"error":"with required"}' }
        $other = $script:Accounts[$withName]
        if (-not $other) {
            $tok2 = [System.Guid]::NewGuid().ToString('N').Substring(0,16)
            $script:Accounts[$withName] = @{ name=$withName; token=$tok2; pubkey=''; contacts=@(); convIds=@() }
            $script:Sessions[$tok2] = $withName
            $other = $script:Accounts[$withName]
        }
        foreach ($cid in @($user.convIds)) {
            $c = $script:Convs[$cid]
            if ($c -and $c.type -eq 'dm' -and ($c.members -contains $withName)) {
                return "{`"convId`":`"$cid`"}"
            }
        }
        $convId = "c$($script:NextConvId)"
        $script:NextConvId++
        $script:Convs[$convId] = @{ id=$convId; type='dm'; members=@($user.name, $withName); messages=@(); name=''; created=(Get-Date -Format 'o') }
        $user.convIds = @($user.convIds) + @($convId)
        $other.convIds = @($other.convIds) + @($convId)
        Save-State
        return "{`"convId`":`"$convId`"}"
    }

    if ($path -eq '/api/chat/contacts') {
        $user = Get-User $q['t']
        if (-not $user) { return '{"error":"auth"}' }
        $contacts = @($user.contacts)
        $result = @{ count = "$($contacts.Count)" }
        for ($i = 0; $i -lt $contacts.Count; $i++) {
            $result["name-$i"] = $contacts[$i]
        }
        return ($result | ConvertTo-Json -Compress)
    }

    if ($path -eq '/api/chat/group') {
        $user = Get-User $q['t']
        if (-not $user) { return '{"error":"auth"}' }
        $gname = $q['name']
        $members = $q['members']
        if (-not $gname) { return '{"error":"name required"}' }
        $memberList = @($user.name)
        if ($members) { $memberList += $members.Split(',') | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne $user.name -and $_ -ne '' } }
        $convId = "c$($script:NextConvId)"
        $script:NextConvId++
        $script:Convs[$convId] = @{ id=$convId; type='group'; members=$memberList; messages=@(); name=$gname; created=(Get-Date -Format 'o') }
        foreach ($m in $memberList) {
            $acc = $script:Accounts[$m]
            if ($acc) { $acc.convIds = @($acc.convIds) + @($convId) }
        }
        Save-State
        return "{`"convId`":`"$convId`"}"
    }

    return '{"error":"not found"}'
}

# ── HTTP Server ──────────────────────────────────────────────────
Add-Type -AssemblyName System.Web
$listener = [System.Net.HttpListener]::new()
$listener.Prefixes.Add("http://localhost:$Port/")
$listener.Start()
Write-Host ""
Write-Host "  CodexChat at http://localhost:$Port/"
Write-Host ""

$mimeTypes = @{
    '.html'='text/html;charset=utf-8'; '.css'='text/css'; '.js'='application/javascript'
    '.json'='application/json'; '.png'='image/png'; '.ico'='image/x-icon'; '.svg'='image/svg+xml'
}

while ($true) {
    $ctx = $listener.GetContext()
    $req = $ctx.Request
    $resp = $ctx.Response
    $url = $req.Url.AbsolutePath
    $query = $req.Url.Query.TrimStart('?')

    try {
        if ($url.StartsWith('/api/')) {
            $json = Handle-Api $url $query $req.HttpMethod
            $buf = [System.Text.Encoding]::UTF8.GetBytes($json)
            $resp.ContentType = 'application/json'
            $resp.ContentLength64 = $buf.Length
            $resp.OutputStream.Write($buf, 0, $buf.Length)
        } else {
            if ($url -eq '/') { $url = '/chat.html' }
            $file = Join-Path $WebDir ($url.TrimStart('/').Replace('/', '\'))
            if (Test-Path -PathType Leaf $file) {
                $ext = [System.IO.Path]::GetExtension($file)
                $resp.ContentType = if ($mimeTypes[$ext]) { $mimeTypes[$ext] } else { 'application/octet-stream' }
                $bytes = [System.IO.File]::ReadAllBytes($file)
                $resp.ContentLength64 = $bytes.Length
                $resp.OutputStream.Write($bytes, 0, $bytes.Length)
            } else {
                $resp.StatusCode = 404
                $buf = [System.Text.Encoding]::UTF8.GetBytes('404')
                $resp.OutputStream.Write($buf, 0, $buf.Length)
            }
        }
    } catch {
        Write-Host "  ERR: $_" -ForegroundColor Red
        try { $resp.StatusCode = 500 } catch {}
    } finally {
        $resp.Close()
    }
}
