using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codex.Core;

namespace Codex.Repository;

/// <summary>
/// Result of a network sync operation.
/// </summary>
public sealed record NetworkSyncResult(int Sent, int Received, IReadOnlyList<string> Errors);

/// <summary>
/// A known peer repository for fact exchange.
/// </summary>
public sealed record Peer(string Url, string Name);

partial class FactStore
{
    List<Peer> m_peers = [];

    /// <summary>
    /// Add a peer for network sync.
    /// </summary>
    public void AddPeer(string url, string name)
    {
        if (!m_peers.Any(p => p.Url == url))
            m_peers.Add(new Peer(url.TrimEnd('/'), name));
    }

    /// <summary>
    /// Remove a peer by URL.
    /// </summary>
    public void RemovePeer(string url)
    {
        m_peers.RemoveAll(p => p.Url == url);
    }

    /// <summary>
    /// List known peers.
    /// </summary>
    public IReadOnlyList<Peer> ListPeers() => m_peers;

    /// <summary>
    /// Fetch a single fact from a peer by hash.
    /// Returns null if the peer doesn't have it.
    /// </summary>
    public async Task<Fact?> FetchFactFromPeer(HttpClient client, string peerUrl, ContentHash hash)
    {
        try
        {
            string url = $"{peerUrl}/fact/{hash.ToHex()}";
            HttpResponseMessage response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                return null;

            string json = await response.Content.ReadAsStringAsync();
            NetworkFactDto? dto = JsonSerializer.Deserialize<NetworkFactDto>(json, s_jsonOptions);
            if (dto is null)
                return null;

            return new Fact(
                ContentHash.FromHex(dto.Hash),
                Enum.Parse<FactKind>(dto.Kind),
                dto.Content,
                dto.Author,
                dto.Timestamp,
                dto.Justification,
                [.. dto.References.Select(ContentHash.FromHex)]);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Sync with a remote peer over HTTP.
    /// Fetches facts the local store is missing, sends facts the peer is missing.
    /// </summary>
    public async Task<NetworkSyncResult> SyncWithPeer(HttpClient client, string peerUrl)
    {
        int sent = 0;
        int received = 0;
        List<string> errors = [];
        string baseUrl = peerUrl.TrimEnd('/');

        try
        {
            // Get remote hash list
            HttpResponseMessage listResponse = await client.GetAsync($"{baseUrl}/facts");
            if (!listResponse.IsSuccessStatusCode)
            {
                errors.Add($"Failed to list facts from {baseUrl}: {listResponse.StatusCode}");
                return new NetworkSyncResult(0, 0, errors);
            }

            List<string>? remoteHashes = await listResponse.Content
                .ReadFromJsonAsync<List<string>>(s_jsonOptions);
            if (remoteHashes is null)
            {
                errors.Add("Failed to parse remote hash list");
                return new NetworkSyncResult(0, 0, errors);
            }

            Set<string> localHashes = CollectAllHashes();
            Set<string> remoteSet = Set<string>.s_empty;
            foreach (string h in remoteHashes)
                remoteSet = remoteSet.Add(h);

            // Fetch facts we don't have
            foreach (string hex in remoteHashes)
            {
                if (localHashes.Contains(hex))
                    continue;

                Fact? fact = await FetchFactFromPeer(client, baseUrl, ContentHash.FromHex(hex));
                if (fact is not null)
                {
                    Store(fact);
                    received++;
                }
            }

            // Send facts the peer doesn't have
            foreach (string hex in localHashes)
            {
                if (remoteSet.Contains(hex))
                    continue;

                Fact? fact = Load(ContentHash.FromHex(hex));
                if (fact is null) continue;

                try
                {
                    NetworkFactDto dto = FactToNetworkDto(fact);
                    HttpResponseMessage putResponse = await client.PostAsJsonAsync(
                        $"{baseUrl}/fact", dto, s_jsonOptions);
                    if (putResponse.IsSuccessStatusCode)
                        sent++;
                }
                catch (Exception ex)
                {
                    errors.Add($"Failed to send {hex[..8]}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            errors.Add($"Sync error: {ex.Message}");
        }

        return new NetworkSyncResult(sent, received, errors);
    }

    /// <summary>
    /// Start a minimal HTTP server for fact exchange.
    /// Returns the listener so it can be stopped.
    /// </summary>
    public HttpListener StartFactServer(string prefix)
    {
        HttpListener listener = new();
        listener.Prefixes.Add(prefix);
        listener.Start();
        _ = RunFactServer(listener);
        return listener;
    }

    async Task RunFactServer(HttpListener listener)
    {
        while (listener.IsListening)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await listener.GetContextAsync();
            }
            catch (HttpListenerException)
            {
                break; // listener stopped
            }

            try
            {
                await HandleFactRequest(ctx);
            }
            catch
            {
                ctx.Response.StatusCode = 500;
                ctx.Response.Close();
            }
        }
    }

    async Task HandleFactRequest(HttpListenerContext ctx)
    {
        string path = ctx.Request.Url?.AbsolutePath ?? "/";
        string method = ctx.Request.HttpMethod;

        if (method == "GET" && path == "/facts")
        {
            // Return list of all fact hashes
            Set<string> hashes = CollectAllHashes();
            List<string> list = [];
            foreach (string h in hashes)
                list.Add(h);

            ctx.Response.ContentType = "application/json";
            await JsonSerializer.SerializeAsync(ctx.Response.OutputStream, list, s_jsonOptions);
            ctx.Response.StatusCode = 200;
        }
        else if (method == "GET" && path.StartsWith("/fact/", StringComparison.Ordinal))
        {
            string hex = path["/fact/".Length..];
            Fact? fact = Load(ContentHash.FromHex(hex));
            if (fact is null)
            {
                ctx.Response.StatusCode = 404;
            }
            else
            {
                NetworkFactDto dto = FactToNetworkDto(fact);
                ctx.Response.ContentType = "application/json";
                await JsonSerializer.SerializeAsync(ctx.Response.OutputStream, dto, s_jsonOptions);
                ctx.Response.StatusCode = 200;
            }
        }
        else if (method == "POST" && path == "/fact")
        {
            NetworkFactDto? dto = await JsonSerializer.DeserializeAsync<NetworkFactDto>(
                ctx.Request.InputStream, s_jsonOptions);
            if (dto is not null)
            {
                Fact fact = new(
                    ContentHash.FromHex(dto.Hash),
                    Enum.Parse<FactKind>(dto.Kind),
                    dto.Content,
                    dto.Author,
                    dto.Timestamp,
                    dto.Justification,
                    [.. dto.References.Select(ContentHash.FromHex)]);
                Store(fact);
                ctx.Response.StatusCode = 201;
            }
            else
            {
                ctx.Response.StatusCode = 400;
            }
        }
        else
        {
            ctx.Response.StatusCode = 404;
        }

        ctx.Response.Close();
    }

    static NetworkFactDto FactToNetworkDto(Fact fact) => new()
    {
        Hash = fact.Hash.ToHex(),
        Kind = fact.Kind.ToString(),
        Content = fact.Content,
        Author = fact.Author,
        Timestamp = fact.Timestamp,
        Justification = fact.Justification,
        References = fact.References.Select(r => r.ToHex()).ToList()
    };

    sealed class NetworkFactDto
    {
        public string Hash { get; set; } = "";
        public string Kind { get; set; } = "";
        public string Content { get; set; } = "";
        public string Author { get; set; } = "";
        public DateTime Timestamp { get; set; }
        public string Justification { get; set; } = "";
        public List<string> References { get; set; } = [];
    }
}
