# plug-ports.ps1 -- the one place a plug's TCP port is written down.
#
# Every plug used to dial 9100 and every runner used to listen on 9100: the
# same constant duplicated 90 times, in 45 guest sources and 45 host runners.
# Two agents running plugs on this box therefore serialised, and the loser's
# listener bind threw part-way through a run (measured twice, 2026-08-06, once
# between a kill-rate control arm and its mutations).
#
# The ports are MONOTONIC and the table is EXPLICIT rather than computed. A
# computed alphabetical index would renumber every later plug the day someone
# adds one; an explicit table means a new plug takes the next free number and
# nothing else moves. Append, never insert.
#
# 9100 is deliberately NOT in this table: Accounts.codex, WebServer.codex and
# their server.ps1 dial it and they predate the plugs, so the block starts at
# 9101 and a plug can never collide with them.
#
# THE GUEST SIDE MUST AGREE. Each plug's <Name>Plug.codex dials its port in
# net-session-new, and the NAT maps that straight to the host port, so the two
# numbers are one fact written in two places. build/check-plug-ports.ps1 is the
# runner that holds them together; it fails the moment they drift.

$script:PlugPorts = @{
    'ada' = 9101
    'angular' = 9102
    'babbage' = 9103
    'clojure' = 9104
    'cobol' = 9105
    'compose' = 9106
    'csharp' = 9107
    'd' = 9108
    'electron' = 9109
    'elf' = 9110
    'elixir' = 9111
    'flutter' = 9112
    'fortran' = 9113
    'go' = 9114
    'groovy' = 9115
    'gtk' = 9116
    'haskell' = 9117
    'img' = 9118
    'java' = 9119
    'javascript' = 9120
    'julia' = 9121
    'kotlin' = 9122
    'lua' = 9123
    'nim' = 9124
    'objc' = 9125
    'ocaml' = 9126
    'pascal' = 9127
    'pe' = 9128
    'perl' = 9129
    'php' = 9130
    'python' = 9131
    'qt' = 9132
    'react' = 9133
    'recheck' = 9134
    'ruby' = 9135
    'rust' = 9136
    'scala' = 9137
    'scheme' = 9138
    'svelte' = 9139
    'swift' = 9140
    'swiftui' = 9141
    'typescript' = 9142
    'vue' = 9143
    'wpf' = 9144
    'zig' = 9145
}

function Get-PlugPort {
    param([Parameter(Mandatory=$true)][string]$Plug)
    $key = $Plug.ToLower()
    if (-not $script:PlugPorts.ContainsKey($key)) {
        throw "plug-ports: no port assigned for plug '$Plug'. Add it to build/plug-ports.ps1 with the next free number, and set the same number in its <Name>Plug.codex."
    }
    return $script:PlugPorts[$key]
}

function Assert-PlugPortFree {
    param([Parameter(Mandatory=$true)][int]$Port, [string]$Plug = '')
    $inUse = @(Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue)
    if ($inUse.Count -gt 0) {
        throw "plug port $Port (plug '$Plug') is already listening, PID $($inUse[0].OwningProcess). Another run of THIS plug is alive. Refusing to start rather than binding blind."
    }
}
