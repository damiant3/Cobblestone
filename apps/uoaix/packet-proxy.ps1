[CmdletBinding()]
param([ValidateRange(1024,65535)][int]$ListenPort=2593,[Parameter(Mandatory)][ValidateRange(1024,65535)][int]$ServerPort,
    [string]$ServerHost='127.0.0.1',[string]$Log='')
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
Add-Type -TypeDefinition @'
using System; using System.IO; using System.Net; using System.Net.Sockets; using System.Text; using System.Threading; using System.Collections.Generic;
public static class UoaixPacketProxy {
    static readonly object Gate = new object(); static StreamWriter Out; static Dictionary<long,int> Codes = new Dictionary<long,int>(); static int Next;
    static void Line(int id, string dir, string text) { lock (Gate) { Out.WriteLine(DateTime.UtcNow.ToString("HH:mm:ss.fff") + " c" + id + " " + dir + " " + text); Out.Flush(); } }
    public static void Run(int listen, string host, int port, string log, int[] book) {
        for (int i = 0; i <= 256; i++) Codes[((long)book[i * 2] << 32) | (uint)book[i * 2 + 1]] = i;
        Out = new StreamWriter(log, true, new UTF8Encoding(false));
        var listener = new TcpListener(IPAddress.Any, listen); listener.Start();
        Line(0, "--", "LISTEN " + listen + " -> " + host + ":" + port);
        while (true) { var client = listener.AcceptTcpClient(); int id = Interlocked.Increment(ref Next); var t = new Thread(() => Serve(id, client, host, port)); t.IsBackground = true; t.Start(); }
    }
    class Conn { public bool Game; public bool Seeded; public bool First; public int SeedAt; public long Pos; public byte Op; public byte[] Seed = new byte[4]; public ulong Low, High; }
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
                        if (!c.Seeded) { c.Seed[c.SeedAt++] = buf[i]; if (c.SeedAt == 4) { Seed(c); sb.Append("[seed " + BitConverter.ToString(c.Seed).Replace("-", "") + "] "); } continue; }
                        byte p = (byte)(buf[i] ^ (byte)(c.Low & 255)); ulong next = (((c.Low >> 1) | (c.High << 31)) ^ 0x026950C6UL) & 0xFFFFFFFFUL;
                        c.High = (((c.High >> 1) | (c.Low << 31)) ^ 0x389DE58CUL) & 0xFFFFFFFFUL; c.Low = next;
                        if (!c.First) { c.First = true; c.Op = p; if (p == 0x91) c.Game = true; }
                        long at = c.Pos++; bool secret = (c.Op == 0x80 && at >= 31 && at <= 60) || (c.Op == 0x91 && at >= 35 && at <= 64);
                        if (secret) { sb.Append("**"); continue; }
                        sb.Append(p.ToString("X2"));
                    }
                    to.Write(buf, 0, n); Line(id, "C>S", sb.ToString());
                } else {
                    to.Write(buf, 0, n);
                    if (!c.Game) { Line(id, "S>C", "raw " + BitConverter.ToString(buf, 0, n).Replace("-", "")); continue; }
                    for (int i = 0; i < n; i++) for (int b = 7; b >= 0; b--) {
                        value = (value << 1) | (uint)((buf[i] >> b) & 1); bits++; int sym;
                        if (Codes.TryGetValue(((long)bits << 32) | (uint)value, out sym)) {
                            if (sym == 256) { if (plain.Count > 0) Line(id, "S>C", plain[0].ToString("X2") + " " + BitConverter.ToString(plain.ToArray()).Replace("-", "")); plain.Clear(); bits = 0; value = 0; break; }
                            plain.Add((byte)sym); bits = 0; value = 0;
                        } else if (bits > 11) { Line(id, "S>C", "DESYNC huffman"); bits = 0; value = 0; }
                    }
                }
            }
        } catch (Exception e) { Line(id, "--", (up ? "C>S" : "S>C") + " END " + e.Message); }
        try { to.Close(); } catch {}
    }
    static void Seed(Conn c) {
        ulong seed = ((ulong)c.Seed[0] << 24) | ((ulong)c.Seed[1] << 16) | ((ulong)c.Seed[2] << 8) | c.Seed[3]; ulong m = 0xFFFFFFFFUL;
        c.Low = ((((seed ^ m) ^ 0x1357UL) << 16) | ((seed ^ 0xFFFFAAAAUL) & 65535UL)) & m;
        c.High = ((seed ^ 0x43210000UL) >> 16) | (((seed ^ m) ^ 0xABCDFFFFUL) & 4294901760UL); c.Seeded = true;
    }
}
'@
Write-Output "PROXY listening on $ListenPort -> ${ServerHost}:$ServerPort log=$Log"
[UoaixPacketProxy]::Run($ListenPort,$ServerHost,$ServerPort,$Log,$pairs)
