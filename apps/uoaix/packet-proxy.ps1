[CmdletBinding()]
param([ValidateRange(1024,65535)][int]$ListenPort=2593,[Parameter(Mandatory)][ValidateRange(1024,65535)][int]$ServerPort,
    [string]$ServerHost='127.0.0.1',[string]$Log='',[switch]$Any)
# The listener is on 127.0.0.1 unless -Any is given; -Any puts it on every interface, for clients on other machines.
# Forwards a UO client to the shard and logs both directions: client bytes decrypted with the
# connection's keystream (seeded by its first 4 bytes), game-connection server bytes Huffman-decoded.
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if($ListenPort -eq $ServerPort){throw 'ListenPort and ServerPort must differ'}
if(Get-NetTCPConnection -State Listen -LocalPort $ListenPort -ErrorAction SilentlyContinue){throw "Port $ListenPort is owned; nothing was stopped"}
if(-not $Log){$Log=Join-Path $repo ('build-output/uoaix/proxy-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'.log')}
$Log=[IO.Path]::GetFullPath($Log)
[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Log))
$book=[regex]::Match([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'GameHuffman.codex')),'(?s)gh-codebook : List Integer = \[(.*?)\]').Groups[1].Value
$pairs=[int[]]@([regex]::Matches($book,'#([0-9A-Fa-f]+)')|ForEach-Object{[Convert]::ToInt32($_.Groups[1].Value,16)})
if($pairs.Count -ne 514){throw "Huffman codebook has $($pairs.Count) values, not 514"}
$table=[regex]::Match([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'GameOpcode.codex')),'(?s)go-lengths : List Integer = \[(.*?)\]').Groups[1].Value
$lengths=[int[]]@([regex]::Matches($table,'\d+')|ForEach-Object{[int]$_.Value})
if($lengths.Count -ne 180){throw "go-lengths has $($lengths.Count) entries, not 180 (opcodes 00 to B3)"}
$source=@'
using System; using System.IO; using System.Net; using System.Net.Sockets; using System.Text; using System.Threading; using System.Collections.Generic;
using Uoaix.Assist;
public static class UoaixPacketProxy {
    static readonly object Gate = new object(); static StreamWriter Out; static ValueMap<long,int> Codes = ValueMap<long,int>.Empty; static int Next; static int[] Lengths;
    static void Line(int id, string dir, string text) { lock (Gate) { Out.WriteLine(DateTime.UtcNow.ToString("HH:mm:ss.fff") + " c" + id + " " + dir + " " + text); Out.Flush(); } }
    public static void Run(int listen, string host, int port, string log, int[] book, int[] lengths, bool any) {
        Lengths = lengths;
        for (int i = 0; i <= 256; i++) Codes = Codes.Set(((long)book[i * 2] << 32) | (uint)book[i * 2 + 1], i);
        Out = new StreamWriter(log, true, new UTF8Encoding(false));
        var listener = new TcpListener(any ? IPAddress.Any : IPAddress.Loopback, listen); listener.Start();
        Line(0, "--", "LISTEN " + (any ? "0.0.0.0:" : "127.0.0.1:") + listen + " -> " + host + ":" + port);
        while (true) { var client = listener.AcceptTcpClient(); int id = Interlocked.Increment(ref Next); var t = new Thread(() => Serve(id, client, host, port)); t.IsBackground = true; t.Start(); }
    }
    // Each client packet is tracked by the client's length table, so a password field is masked wherever its packet falls;
    // once a packet cannot be framed every later byte is masked.
    class Conn { public bool Game; public bool First; public int SeedAt; public byte Op; public int At, Len, Hi; public bool Lost; public byte[] Seed = new byte[4]; public UoKeystream Key; }
    // The 30-byte password fields: 0x80 and 0x91 the account's, 0x5D the character's, 0x83 (delete a character) the account's.
    static bool Secret(byte op, int at) { int from = op == 0x91 || op == 0x5D ? 35 : op == 0x80 ? 31 : op == 0x83 ? 1 : -1; return from >= 0 && at >= from && at < from + 30; }
    static bool Masks(Conn c, byte p) {
        if (c.Lost) return true;
        if (c.At == 0) { c.Op = p; c.Len = p < Lengths.Length ? Lengths[p] : -1; if (c.Len < 0) { c.Lost = true; return true; } }
        else if (c.Len == 0 && c.At == 1) c.Hi = p;
        else if (c.Len == 0 && c.At == 2) { c.Len = (c.Hi << 8) | p; if (c.Len < 3) { c.Lost = true; return true; } }
        bool secret = Secret(c.Op, c.At);
        c.At++; if (c.Len > 0 && c.At >= c.Len) c.At = 0;
        return secret;
    }
    static void Serve(int id, TcpClient client, string host, int port) {
        TcpClient server = null;
        try {
            client.NoDelay = true; server = new TcpClient(host, port); server.NoDelay = true;
            Line(id, "--", "OPEN " + client.Client.RemoteEndPoint);
            var c = new Conn(); var cs = client.GetStream(); var ss = server.GetStream();
            var up = new Thread(() => Pump(id, cs, ss, c, true)); up.IsBackground = true; up.Start();
            Pump(id, ss, cs, c, false); up.Join(2000);
        } catch (Exception e) { Line(id, "--", "ERROR " + e.Message); }
        finally { try { client.Close(); } catch {} try { if (server != null) server.Close(); } catch {} Line(id, "--", "CLOSE"); }
    }
    static void Pump(int id, NetworkStream from, NetworkStream to, Conn c, bool up) {
        var buf = new byte[8192]; var plain = new List<byte>(); int bits = 0; long value = 0;
        try {
            while (true) {
                int n = from.Read(buf, 0, buf.Length); if (n <= 0) break;
                if (up) {
                    var sb = new StringBuilder();
                    for (int i = 0; i < n; i++) {
                        if (c.Key == null) { c.Seed[c.SeedAt++] = buf[i]; if (c.SeedAt == 4) { c.Key = new UoKeystream(((uint)c.Seed[0] << 24) | ((uint)c.Seed[1] << 16) | ((uint)c.Seed[2] << 8) | c.Seed[3]); sb.Append("[seed " + BitConverter.ToString(c.Seed).Replace("-", "") + "] "); } continue; }
                        byte p = c.Key.Apply(buf[i]);
                        if (!c.First) { c.First = true; if (p == 0x91) c.Game = true; }
                        if (Masks(c, p)) { sb.Append("**"); continue; }
                        sb.Append(p.ToString("X2"));
                    }
                    to.Write(buf, 0, n); Line(id, "C>S", sb.ToString());
                } else {
                    to.Write(buf, 0, n);
                    if (!c.Game) { Line(id, "S>C", "raw " + BitConverter.ToString(buf, 0, n).Replace("-", "")); continue; }
                    for (int i = 0; i < n; i++) for (int b = 7; b >= 0; b--) {
                        value = (value << 1) | (uint)((buf[i] >> b) & 1); bits++; int sym;
                        if (Codes.TryGet(((long)bits << 32) | (uint)value, out sym)) {
                            if (sym == 256) { if (plain.Count > 0) Line(id, "S>C", plain[0].ToString("X2") + " " + BitConverter.ToString(plain.ToArray()).Replace("-", "")); plain.Clear(); bits = 0; value = 0; break; }
                            plain.Add((byte)sym); bits = 0; value = 0;
                        } else if (bits > 11) { Line(id, "S>C", "DESYNC huffman"); bits = 0; value = 0; }
                    }
                }
            }
        } catch (Exception e) { Line(id, "--", (up ? "C>S" : "S>C") + " END " + e.Message); }
        try { to.Close(); } catch {}
    }
}
'@
# UoWire.cs is a file of the Assist's (its usings and namespace), so it is compiled as a file beside this source, not
# appended to it, with Collections.cs, whose ValueMap it uses; the source goes to a file of this run's own (the PID alone
# is reused, L-SHARED).
$sourceFile=Join-Path ([IO.Path]::GetTempPath()) "uoaix-packet-proxy-$PID-$(Get-Date -Format 'yyyyMMddHHmmssfff').cs"
[IO.File]::WriteAllText($sourceFile,$source)
try{Add-Type -Path @($sourceFile,(Join-Path $PSScriptRoot 'assist/UoWire.cs'),(Join-Path $PSScriptRoot 'assist/Collections.cs'))}finally{Remove-Item -LiteralPath $sourceFile -ErrorAction SilentlyContinue}
Write-Output "PROXY listening on $ListenPort -> ${ServerHost}:$ServerPort log=$Log"
[UoaixPacketProxy]::Run($ListenPort,$ServerHost,$ServerPort,$Log,$pairs,$lengths,[bool]$Any)
