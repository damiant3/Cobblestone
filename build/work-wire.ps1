# The host half of BACKLOG 6.2: asking a peer for a work by hash, and turning
# the answer into the %%QUOTED-WORKS%% blob the compiler already reads.
#
# WHY THE HOST AND NOT THE COMPILER. Library Rule 2 fixes the dependency order
# codex.foreword -> codex -> codex.os -> apps, so the compiler citing the net
# stack inverts it. The decision taken 2026-07-16 is that it never will: the
# host fetches, and hands the work over in the blob. That is safe for exactly
# the reason the blob is safe at all -- the compiler hashes the content, checks
# the signature, checks the pinned key and checks the trust floor itself. The
# transport does not have to be trusted, so it does not have to be the compiler.
#
# WHAT THIS FILE MAY NOT DO, AND IT IS THE WHOLE SAFETY ARGUMENT: it may not
# vouch for anything. It emits WORK lines and never a KEY line. Trust comes from
# the `trusting` declarations in the SOURCE, because the works arrive over a
# transport and a transport must not be able to vouch for its own cargo. A
# signer the source did not pin is an unknown signer, whatever the peer says.
#
# It is dot-sourced, and it is dot-sourced by more than one caller ON PURPOSE.
# cdx-serve-test.ps1 spoke this wire first and privately; a second hand-rolled
# copy in compile.ps1 is how a transport ends up with 41 byte-identical
# implementations (BACKLOG 2.3). One wire, one file, two callers.
#
# Requires vm-config.ps1 (the CCE tables) to be dot-sourced first.

Set-StrictMode -Version Latest

# --- CCE ---------------------------------------------------------------------
# THE WIRE IS CCE AND IT DOES NOT TELL YOU. frame-encode-text calls char-code,
# which gives the CCE code point and not the ASCII one, so even a hash's own hex
# digits go out as CCE bytes. Sending ASCII does not error -- the server simply
# never finds the work and answers "not here", which reads exactly like a
# working server with an empty store.
function ConvertTo-CceBytes([string]$Text) {
    $b = New-Object System.Collections.Generic.List[byte]
    foreach ($ch in $Text.ToCharArray()) {
        $u = [int]$ch
        if ($u -ge 256) { throw "work-wire: '$ch' is outside the single-byte CCE range" }
        $b.Add($script:UnicodeToCce[$u])
    }
    return $b.ToArray()
}

function ConvertFrom-CceBytes([byte[]]$Bytes) {
    $sb = New-Object System.Text.StringBuilder
    foreach ($x in $Bytes) {
        $u = if ($x -lt $script:CceToUnicode.Length) { $script:CceToUnicode[$x] } else { 63 }
        [void]$sb.Append([char]$u)
    }
    return $sb.ToString()
}

# --- Frames ------------------------------------------------------------------
function New-Le32([int]$Value) {
    return [byte[]]@(($Value -band 0xFF), (($Value -shr 8) -band 0xFF), (($Value -shr 16) -band 0xFF), (($Value -shr 24) -band 0xFF))
}

# [int] on every byte before shifting is load-bearing. PowerShell's -shl keeps
# the LEFT operand's width, so `$b[1] -shl 8` on a [byte] shifts the bit clean
# off the end and yields 0 -- every length silently comes back as `n -band 0xFF`.
# That read a real 457-byte frame as 201, which looks exactly like a server
# truncating its reply. The server was right; the ruler was short.
function Read-Le32([byte[]]$Bytes, [int]$Offset) {
    return [int]$Bytes[$Offset] + ([int]$Bytes[$Offset+1] -shl 8) + ([int]$Bytes[$Offset+2] -shl 16) + ([int]$Bytes[$Offset+3] -shl 24)
}

# frame-encode tag body    = le32(1 + len body) ++ [tag] ++ body
# encode-work-request-body = frame-encode-bytes(pubkey) ++ frame-encode-text(hash)
# The public key is empty: the reply is answerable by its own content, so the
# server has no use for who is asking and accept-reply ignores the reply's own
# `from` in turn.
function New-WorkRequestFrame([string]$Hash) {
    $hashCce = ConvertTo-CceBytes $Hash
    $body = @()
    $body += New-Le32 0
    $body += New-Le32 $hashCce.Length
    $body += $hashCce
    $frame = @()
    $frame += New-Le32 (1 + $body.Length)
    $frame += [byte]17          # tag-work-request
    $frame += $body
    return [byte[]]$frame
}

# Ask one peer for one work. Returns @{ Hash; Payload } -- an empty Payload is a
# MISS, which is the ordinary case and not an error: a peer that does not hold a
# work simply does not hold it. Returns $null only when the peer never answered.
function Invoke-WorkAsk {
    param(
        [Parameter(Mandatory=$true)] [string]$HostName,
        [Parameter(Mandatory=$true)] [int]$Port,
        [Parameter(Mandatory=$true)] [string]$Hash,
        [int]$TimeoutSec = 60
    )
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        $c = $null
        try {
            $c = [System.Net.Sockets.TcpClient]::new()
            $c.Connect($HostName, $Port)
            $s = $c.GetStream()
            $s.ReadTimeout = 30000
            $req = New-WorkRequestFrame $Hash
            $s.Write($req, 0, $req.Length)
            $s.Flush()

            $hdr = New-Object byte[] 5
            $n = 0
            while ($n -lt 5) { $r = $s.Read($hdr, $n, 5 - $n); if ($r -le 0) { throw 'short header' }; $n += $r }
            $total = Read-Le32 $hdr 0
            $tag = $hdr[4]
            if ($tag -ne 18) { throw "work-wire: expected tag-work-reply (18), got $tag" }
            $rest = New-Object byte[] ($total - 1)
            $n = 0
            while ($n -lt $rest.Length) { $r = $s.Read($rest, $n, $rest.Length - $n); if ($r -le 0) { throw 'short body' }; $n += $r }
            $c.Dispose()

            # decode-work-reply-body: bytes(pubkey) ++ text(hash) ++ text(payload)
            $o = 0
            $pkLen = Read-Le32 $rest $o; $o += 4 + $pkLen
            $hLen = Read-Le32 $rest $o; $o += 4
            $rHash = ConvertFrom-CceBytes $rest[$o..($o + $hLen - 1)]; $o += $hLen
            $pLen = Read-Le32 $rest $o; $o += 4
            $payload = if ($pLen -gt 0) { ConvertFrom-CceBytes $rest[$o..($o + $pLen - 1)] } else { '' }
            return @{ Hash = $rHash; Payload = $payload }
        } catch {
            if ($c) { $c.Dispose() }
            Start-Sleep -Milliseconds 700
        }
    }
    return $null
}

# --- The stored form ---------------------------------------------------------
# SourceDefWire: hash|path|quire|chapter|signer-fp|sig-hex|author|timestamp|len|<content>
# None of the nine header fields can hold a pipe. The content can, and nearly
# always does -- a pipe is how the language writes a sum type -- so it is the
# last field, length-prefixed, and taken verbatim after the ninth pipe.
function ConvertFrom-SourceDefWire([string]$Line) {
    $p9 = -1; $seen = 0
    for ($i = 0; $i -lt $Line.Length; $i++) {
        if ($Line[$i] -eq '|') { $seen++; if ($seen -eq 9) { $p9 = $i; break } }
    }
    if ($p9 -lt 0) { return $null }
    $parts = $Line.Substring(0, $p9) -split '\|'
    if ($parts.Count -lt 9) { return $null }
    $clen = 0
    if (-not [int]::TryParse($parts[8], [ref]$clen)) { return $null }
    if ($p9 + 1 + $clen -gt $Line.Length) { return $null }
    return @{
        Hash      = $parts[0]
        Path      = $parts[1]
        Quire     = $parts[2]
        Chapter   = $parts[3]
        Signer    = $parts[4]
        SigHex    = $parts[5]
        Author    = $parts[6]
        Timestamp = $parts[7]
        Content   = $Line.Substring($p9 + 1, $clen)
    }
}

function Get-CceContentHash([string]$Content) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $digest = $sha.ComputeHash((ConvertTo-CceBytes $Content))
    } finally { $sha.Dispose() }
    return (($digest | ForEach-Object { $_.ToString('x2') }) -join '')
}

# --- The blob ----------------------------------------------------------------
# A WORK line offers a signed work:
#   WORK <hash> <author> <timestamp> <signer-fp> <sig-hex> <n-lines>
# followed by exactly n-lines lines of content. The compiler rebuilds the
# content as each of those lines plus a newline, so the blob can only express a
# content that ENDS in a newline -- anything else comes back one byte longer
# than it went in and the gate refuses it as corrupt. That is the format being
# honest rather than a limitation worth working around: refuse to emit it here,
# where the reason can be said out loud, rather than downstream as a digest
# mismatch on a work that was served perfectly.
function ConvertTo-WorkBlock($Wire) {
    if (-not $Wire.Content.EndsWith("`n")) {
        throw "work-wire: the work $($Wire.Hash) does not end in a newline, and the offered-works blob cannot express it without changing its bytes"
    }
    $lines = $Wire.Content -split "`n"
    # The split of "a\nb\n" is @('a','b',''); the trailing empty is the newline
    # the last line already carries, not a line of its own.
    $lines = $lines[0..($lines.Count - 2)]
    $block = @("WORK $($Wire.Hash) $($Wire.Author) $($Wire.Timestamp) $($Wire.Signer) $($Wire.SigHex) $($lines.Count)")
    $block += $lines
    return $block
}

$script:QuotedWorksMarker = '%%QUOTED-WORKS%%'

function Get-QuotedHashes([string[]]$Lines) {
    $found = @()
    foreach ($l in $Lines) {
        if ($l -match 'quotes\s+"sha256:([0-9a-fA-F]{64})"') { $found += $matches[1].ToLower() }
    }
    return $found
}

# What the source already offers, so a work handed over beside it is not asked
# for again. The marker splits the source; everything after it is works.
function Get-OfferedHashes([string[]]$Lines) {
    $found = @()
    $inBlob = $false
    foreach ($l in $Lines) {
        if ($l -eq $script:QuotedWorksMarker) { $inBlob = $true; continue }
        if (-not $inBlob) { continue }
        if ($l -match '^(WORK|OFFER)\s+([0-9a-fA-F]{64})\s') { $found += $matches[2].ToLower() }
    }
    return $found
}

# Fetch every hash the source quotes and does not already carry, and prepend the
# answers to the blob. Returns the source lines to compile.
function Add-PeerWorks {
    param(
        # AllowEmptyString, because a source has blank lines and a Mandatory
        # [string[]] rejects an array with an empty element out of hand.
        [Parameter(Mandatory=$true)] [AllowEmptyString()] [string[]]$Lines,
        [Parameter(Mandatory=$true)] [string]$Peer,
        [int]$TimeoutSec = 60
    )
    if ($Peer -notmatch '^([^:]+):(\d+)$') { throw "work-wire: -Peer wants <host>:<port>, got '$Peer'" }
    $peerHost = $matches[1]
    $peerPort = [int]$matches[2]

    $offered = Get-OfferedHashes $Lines
    $wanted = @(Get-QuotedHashes $Lines | Where-Object { $offered -notcontains $_ } | Select-Object -Unique)
    if ($wanted.Count -eq 0) { return $Lines }

    $blocks = @()
    foreach ($h in $wanted) {
        $reply = Invoke-WorkAsk -HostName $peerHost -Port $peerPort -Hash $h -TimeoutSec $TimeoutSec
        if ($null -eq $reply) { throw "work-wire: the peer at $Peer never answered when asked for $h" }
        if ($reply.Payload.Length -eq 0) {
            # A miss is not an error. The work may still resolve from an
            # attached store, and if it resolves from nowhere the gate says so.
            [Console]::Error.WriteLine("work-wire: $Peer does not hold $h")
            continue
        }
        # accept-reply, on the host: a peer that answers a question nobody asked,
        # or hands back something other than what it addresses, is caught here by
        # arithmetic rather than by reputation. The gate checks this again -- the
        # digest on a WORK line is only a claim -- but a lying peer should be
        # named at the fetch, not survive as a confusing refusal downstream.
        if ($reply.Hash -ne $h) { throw "work-wire: asked $Peer for $h and it answered $($reply.Hash)" }
        $wire = ConvertFrom-SourceDefWire $reply.Payload
        if ($null -eq $wire) { throw "work-wire: $Peer answered $h with something that is not a stored work" }
        $actual = Get-CceContentHash $wire.Content
        if ($actual -ne $h) { throw "work-wire: $Peer answered $h with content that addresses $actual" }
        $blocks += ConvertTo-WorkBlock $wire
    }
    if ($blocks.Count -eq 0) { return $Lines }

    $at = [array]::IndexOf($Lines, $script:QuotedWorksMarker)
    if ($at -lt 0) {
        return @($Lines) + @($script:QuotedWorksMarker) + $blocks
    }
    $head = if ($at -eq 0) { @() } else { $Lines[0..($at)] }
    $tail = if ($at -eq $Lines.Count - 1) { @() } else { $Lines[($at + 1)..($Lines.Count - 1)] }
    return @($head) + $blocks + @($tail)
}
