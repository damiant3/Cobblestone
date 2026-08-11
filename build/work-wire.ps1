# The host half of peer quotation: asking a peer for a work by hash, and turning
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
# implementations. One wire, one file, two callers.
#
# Requires vm-config.ps1 (the CCE tables) to be dot-sourced first.

Set-StrictMode -Version Latest

# --- CCE ---------------------------------------------------------------------
# THE WIRE IS CCE AND IT DOES NOT TELL YOU. frame-encode-text calls char-code,
# which gives the CCE code point and not the ASCII one, so even a hash's own hex
# digits go out as CCE bytes. Sending ASCII does not error -- the server simply
# never finds the work and answers "not here", which reads exactly like a
# working server with an empty store.
#
# ConvertTo-CceBytes / ConvertFrom-CceBytes come from vm-config.ps1 and are
# tier-aware. This file used to carry its own single-byte pair, and both halves
# were wrong in the quiet direction. The encoder folded 112 of the 128 code points
# in 128..255 onto CCE 68 -- which is '?' -- and every one of those 112 has a real
# tier 1 CCE code point, so a single accented character in a work name silently
# changed the bytes that got hashed. The decoder answered '?' above tier 0 in the
# same way, which reads as a corrupt reply rather than an unread one. Both now
# refuse (encode throws, decode counts) instead of guessing.

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
function New-HashAskFrame([string]$Hash, [byte]$Tag) {
    $hashCce = ConvertTo-CceBytes $Hash
    $body = @()
    $body += New-Le32 0
    $body += New-Le32 $hashCce.Length
    $body += $hashCce
    $frame = @()
    $frame += New-Le32 (1 + $body.Length)
    $frame += $Tag
    $frame += $body
    return [byte[]]$frame
}

# tag-work-request (17) and tag-locate-request (19) have byte-identical bodies,
# and their replies (18 / 20) do too -- bytes(pubkey) ++ text(hash) ++ text(rest).
# Only the meaning of the third field differs: a work reply's is CONTENT and is
# checked against the hash, a locate reply's is a list of ADDRESSES and is
# checked by nothing. One codec, two verbs.
function New-WorkRequestFrame([string]$Hash) {
    return New-HashAskFrame $Hash ([byte]17)
}

# Ask one peer for one work. Returns @{ Hash; Payload } -- an empty Payload is a
# MISS, which is the ordinary case and not an error: a peer that does not hold a
# work simply does not hold it. Returns $null only when the peer never answered.
function Invoke-HashAsk {
    param(
        [Parameter(Mandatory=$true)] [string]$HostName,
        [Parameter(Mandatory=$true)] [int]$Port,
        [Parameter(Mandatory=$true)] [string]$Hash,
        [Parameter(Mandatory=$true)] [byte]$Tag,
        [Parameter(Mandatory=$true)] [byte]$ExpectTag,
        [int]$TimeoutSec = 60,
        # EVERY RETRY IS A CONNECTION THE SERVER MUST WADE THROUGH. codex-vm's
        # port forward accepts and allocates a slot per attempt whether or not
        # a guest is listening, and a single-threaded server then picks up the
        # abandoned ones in turn, finds them silent, and burns its accept cycle
        # on each. Unbounded retrying does not out-wait that -- it causes it.
        [int]$MaxAttempts = 0,
        # THE READ TIMEOUT MUST COVER THE WORK THE ANSWER COSTS, and for a
        # locate that is not the same as for a fetch. A peer answers a
        # work-request out of an index it built at boot; a registry answers a
        # locate by probing every peer it knows, which is a guest-to-guest
        # connect and receive EACH. At 30 s the host gave up first, and because
        # every retry opens a new connection and triggers a fresh probe, the
        # registry spent forever answering sockets nobody was holding any more.
        # Measured: the registry completed the whole exchange and printed
        # `answer-locate: reply sent` while the host had already moved on.
        [int]$ReadTimeoutMs = 30000
    )
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    $attempt = 0
    while ((Get-Date) -lt $deadline) {
        if ($MaxAttempts -gt 0 -and $attempt -ge $MaxAttempts) { return $null }
        $attempt++
        $c = $null
        try {
            $c = [System.Net.Sockets.TcpClient]::new()
            $c.Connect($HostName, $Port)
            $s = $c.GetStream()
            $s.ReadTimeout = $ReadTimeoutMs
            $req = New-HashAskFrame $Hash $Tag
            $s.Write($req, 0, $req.Length)
            $s.Flush()

            $hdr = New-Object byte[] 5
            $n = 0
            while ($n -lt 5) { $r = $s.Read($hdr, $n, 5 - $n); if ($r -le 0) { throw 'short header' }; $n += $r }
            $total = Read-Le32 $hdr 0
            $tag = $hdr[4]
            if ($tag -ne $ExpectTag) { throw "work-wire: expected tag $ExpectTag, got $tag" }
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
            # DO NOT HAMMER. codex-vm's port forward accepts a host connection
            # and allocates a NAT slot even when no guest is listening yet, and
            # an abandoned slot stays live. Retrying every 700 ms through a
            # ~20 s guest boot opened ~70 connections and exhausted the 64-slot
            # table, after which every frame was dropped -- which reads as the
            # server being broken when it is the client that broke it.
            Start-Sleep -Milliseconds 3000
        }
    }
    return $null
}

function Invoke-WorkAsk {
    param(
        [Parameter(Mandatory=$true)] [string]$HostName,
        [Parameter(Mandatory=$true)] [int]$Port,
        [Parameter(Mandatory=$true)] [string]$Hash,
        [int]$TimeoutSec = 60
    )
    return Invoke-HashAsk -HostName $HostName -Port $Port -Hash $Hash `
        -Tag ([byte]17) -ExpectTag ([byte]18) -TimeoutSec $TimeoutSec
}

# Ask a registry who holds a digest. Returns an array of 'host:port' -- EMPTY is
# the ordinary answer for "nobody I know of", not an error, exactly as an empty
# work payload is. Returns $null only when the registry never answered.
#
# What comes back is a rumour and is treated as one. Nothing here checks that a
# named peer exists, is honest, or holds anything; the addresses are only used,
# by fetching from them and hashing what comes back. A registry that names a
# hostile peer costs a round trip -- Add-PeerWorks still refuses any content
# that does not address the hash it asked for.
function Invoke-WorkLocate {
    param(
        [Parameter(Mandatory=$true)] [string]$HostName,
        [Parameter(Mandatory=$true)] [int]$Port,
        [Parameter(Mandatory=$true)] [string]$Hash,
        [int]$TimeoutSec = 120
    )
    # THE READ TIMEOUT MUST BE WELL UNDER THE OUTER DEADLINE, or the retry loop
    # cannot retry. codex-vm's port forward accepts the host connection long
    # before the guest is behind it, so an early ask does not fail fast -- it
    # blocks on a socket nobody is reading. With the read timeout longer than
    # $TimeoutSec, that first block outlives the whole deadline and the loop
    # exits having made exactly one attempt, reporting a healthy server as
    # never having come up. It was 240000 against a 180 s deadline, which is
    # why this passed against a single fast-booting VM and failed in the full
    # harness where the registry boots behind three compiles.
    $reply = Invoke-HashAsk -HostName $HostName -Port $Port -Hash $Hash `
        -Tag ([byte]19) -ExpectTag ([byte]20) -TimeoutSec $TimeoutSec `
        -ReadTimeoutMs 25000 -MaxAttempts 4
    if ($null -eq $reply) { return $null }
    if ($reply.Hash -ne $Hash) {
        throw "work-wire: asked the registry at ${HostName}:$Port to locate $Hash and it answered for $($reply.Hash)"
    }
    # AN OBJECT, NOT A BARE ARRAY, and the reason is worth the extra field.
    # PowerShell unrolls arrays on return, so `return @()` for "nobody holds it"
    # arrives at the caller as $null -- indistinguishable from "the server never
    # answered", which is how a working registry got reported as dead and sent
    # this whole investigation after a transport bug that was never there. The
    # `,@()` idiom that supposedly fixes it does not behave consistently either:
    # a toy reproduction counted 0 and this same call counted 1. So the empty
    # case is not encoded in the shape of the return value at all. `$null` means
    # no answer; anything else is an answer, and `.Peers` is what it said.
    $peers = @()
    if ($reply.Payload.Length -gt 0) {
        $peers = @($reply.Payload -split '\s+' | Where-Object { $_ -ne '' })
    }
    return [pscustomobject]@{ Peers = $peers; Count = $peers.Count }
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

function Split-HostPort([string]$Value, [string]$What) {
    if ($Value -notmatch '^([^:]+):(\d+)$') {
        throw "work-wire: -$What wants <host>:<port>, got '$Value'"
    }
    return @{ HostName = $matches[1]; Port = [int]$matches[2] }
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
        [string]$Peer,
        [string]$Registry,
        [int]$TimeoutSec = 60
    )
    if (-not $Peer -and -not $Registry) {
        throw 'work-wire: Add-PeerWorks wants -Peer <host:port> or -Registry <host:port>'
    }
    if ($Peer -and $Registry) {
        throw 'work-wire: -Peer names a holder and -Registry finds one; pass one or the other'
    }
    if ($Peer) { [void](Split-HostPort $Peer 'Peer') }
    $reg = if ($Registry) { Split-HostPort $Registry 'Registry' } else { $null }

    $offered = Get-OfferedHashes $Lines
    $wanted = @(Get-QuotedHashes $Lines | Where-Object { $offered -notcontains $_ } | Select-Object -Unique)
    if ($wanted.Count -eq 0) { return $Lines }

    $blocks = @()
    foreach ($h in $wanted) {
        # WHERE THE CANDIDATES COME FROM IS THE ONLY THING -Registry CHANGES.
        # Everything below this is identical either way, and deliberately so: a
        # located peer gets exactly the same scrutiny as a named one, because
        # being named by a registry is not evidence of anything.
        $candidates = @()
        if ($Peer) {
            $candidates = @($Peer)
        } else {
            $found = Invoke-WorkLocate -HostName $reg.HostName -Port $reg.Port -Hash $h -TimeoutSec $TimeoutSec
            if ($null -eq $found) {
                throw "work-wire: the registry at $Registry never answered when asked to locate $h"
            }
            if ($found.Count -eq 0) {
                [Console]::Error.WriteLine("work-wire: the registry at $Registry knows nobody holding $h")
                continue
            }
            $candidates = $found.Peers
        }

        $block = $null
        foreach ($cand in $candidates) {
            $cp = Split-HostPort $cand 'peer'
            $reply = Invoke-WorkAsk -HostName $cp.HostName -Port $cp.Port -Hash $h -TimeoutSec $TimeoutSec
            if ($null -eq $reply) {
                # A named peer that never answers is an error, because it is the
                # only route offered. A LOCATED one is a bad rumour: try the next
                # address the registry named before giving up on the digest.
                if ($Peer) { throw "work-wire: the peer at $Peer never answered when asked for $h" }
                [Console]::Error.WriteLine("work-wire: $cand was named for $h and never answered")
                continue
            }
            if ($reply.Payload.Length -eq 0) {
                # A miss is not an error. The work may still resolve from an
                # attached store, and if it resolves from nowhere the gate says so.
                [Console]::Error.WriteLine("work-wire: $cand does not hold $h")
                continue
            }
            # accept-reply, on the host: a peer that answers a question nobody asked,
            # or hands back something other than what it addresses, is caught here by
            # arithmetic rather than by reputation. The gate checks this again -- the
            # digest on a WORK line is only a claim -- but a lying peer should be
            # named at the fetch, not survive as a confusing refusal downstream.
            if ($reply.Hash -ne $h) { throw "work-wire: asked $cand for $h and it answered $($reply.Hash)" }
            $wire = ConvertFrom-SourceDefWire $reply.Payload
            if ($null -eq $wire) { throw "work-wire: $cand answered $h with something that is not a stored work" }
            $actual = Get-CceContentHash $wire.Content
            if ($actual -ne $h) { throw "work-wire: $cand answered $h with content that addresses $actual" }
            $block = ConvertTo-WorkBlock $wire
            break
        }
        if ($null -ne $block) { $blocks += $block }
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
