using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Codex.Core;

namespace Codex.Repository;

public enum FactKind
{
    Definition,
    Supersession,
    Deprecation,
    Proposal,
    Verdict,
    Trust,
    Proof
}

public enum VerdictDecision
{
    Accept,
    Reject,
    Amend,
    Abstain
}

public enum TrustDegree
{
    Reviewed,
    Tested,
    Verified,
    Critical
}

public sealed record Fact(
    ContentHash Hash,
    FactKind Kind,
    string Content,
    string Author,
    DateTime Timestamp,
    string Justification,
    ImmutableArray<ContentHash> References)
{
    public static Fact CreateDefinition(string source, string author, string justification)
    {
        ContentHash hash = ContentHash.Of(source);
        return new Fact(hash, FactKind.Definition, source, author, DateTime.UtcNow,
            justification, []);
    }

    public static Fact CreateSupersession(
        ContentHash newDefinition, ContentHash supersedes, string author, string justification)
    {
        string content = $"supersedes:{supersedes.ToHex()}\nnew:{newDefinition.ToHex()}";
        ContentHash hash = ContentHash.Of(content);
        return new Fact(hash, FactKind.Supersession, content, author, DateTime.UtcNow,
            justification, [newDefinition, supersedes]);
    }

    public static Fact CreateProposal(
        ContentHash definition,
        string author,
        string justification,
        ImmutableArray<string> stakeholders,
        ContentHash? supersedes = null)
    {
        string supersededPart = supersedes is not null ? $"\nsupersedes:{supersedes.Value.ToHex()}" : "";
        string stakeholderList = string.Join(",", stakeholders);
        string content = $"definition:{definition.ToHex()}{supersededPart}\nstakeholders:{stakeholderList}";
        ContentHash hash = ContentHash.Of(content + $"\nauthor:{author}\ntime:{DateTime.UtcNow:O}");
        ImmutableArray<ContentHash> refs = supersedes is not null
            ? [definition, supersedes.Value]
            : [definition];
        return new Fact(hash, FactKind.Proposal, content, author, DateTime.UtcNow,
            justification, refs);
    }

    public static Fact CreateVerdict(
        ContentHash proposal,
        VerdictDecision decision,
        string author,
        string reasoning,
        ContentHash? amendment = null)
    {
        string amendPart = amendment is not null ? $"\namendment:{amendment.Value.ToHex()}" : "";
        string content = $"proposal:{proposal.ToHex()}\ndecision:{decision}{amendPart}";
        ContentHash hash = ContentHash.Of(content + $"\nauthor:{author}\ntime:{DateTime.UtcNow:O}");
        ImmutableArray<ContentHash> refs = amendment is not null
            ? [proposal, amendment.Value]
            : [proposal];
        return new Fact(hash, FactKind.Verdict, content, author, DateTime.UtcNow,
            reasoning, refs);
    }

    public static Fact CreateTrust(
        ContentHash target,
        TrustDegree degree,
        string author,
        string reasoning)
    {
        string content = $"target:{target.ToHex()}\ndegree:{degree}";
        ContentHash hash = ContentHash.Of(content + $"\nauthor:{author}\ntime:{DateTime.UtcNow:O}");
        return new Fact(hash, FactKind.Trust, content, author, DateTime.UtcNow,
            reasoning, [target]);
    }

    public static Fact CreateProof(
        string claimName,
        string proofSource,
        string author,
        string justification,
        ContentHash? definitionHash = null)
    {
        string content = $"claim:{claimName}\n{proofSource}";
        ContentHash hash = ContentHash.Of(content);
        ImmutableArray<ContentHash> refs = definitionHash is not null
            ? [definitionHash.Value]
            : [];
        return new Fact(hash, FactKind.Proof, content, author, DateTime.UtcNow,
            justification, refs);
    }
}

public sealed partial class FactStore(string rootPath)
{
    readonly string m_rootPath = rootPath;
    readonly string m_factsPath = Path.Combine(rootPath, ".codex", "facts");
    readonly string m_viewPath = Path.Combine(rootPath, ".codex", "view.json");
    Map<ContentHash, Fact> m_cache = Map<ContentHash, Fact>.s_empty;

    public static FactStore Init(string rootPath)
    {
        string codexDir = Path.Combine(rootPath, ".codex");
        string factsDir = Path.Combine(codexDir, "facts");

        Directory.CreateDirectory(codexDir);
        Directory.CreateDirectory(factsDir);

        string viewPath = Path.Combine(codexDir, "view.json");
        if (!File.Exists(viewPath))
        {
            File.WriteAllText(viewPath, "{}");
        }

        return new(rootPath);
    }

    public static FactStore? Open(string rootPath)
    {
        string codexDir = Path.Combine(rootPath, ".codex");
        if (!Directory.Exists(codexDir))
        {
            return null;
        }

        return new(rootPath);
    }

    public ContentHash Store(Fact fact)
    {
        string hex = fact.Hash.ToHex();
        string factDir = Path.Combine(m_factsPath, hex[..2]);
        Directory.CreateDirectory(factDir);

        string factFile = Path.Combine(factDir, hex + ".json");
        if (!File.Exists(factFile))
        {
            FactDto dto = new()
            {
                Hash = hex,
                Kind = fact.Kind.ToString(),
                Content = fact.Content,
                Author = fact.Author,
                Timestamp = fact.Timestamp,
                Justification = fact.Justification,
                References = [.. fact.References.Select(r => r.ToHex())]
            };
            string json = JsonSerializer.Serialize(dto, s_jsonOptions);
            File.WriteAllText(factFile, json);
        }

        m_cache = m_cache.Set(fact.Hash, fact);
        return fact.Hash;
    }

    public Fact? Load(ContentHash hash)
    {
        Fact? cached = m_cache[hash];
        if (cached is not null)
        {
            return cached;
        }

        string hex = hash.ToHex();
        string factFile = Path.Combine(m_factsPath, hex[..2], hex + ".json");
        if (!File.Exists(factFile))
        {
            return null;
        }

        string json = File.ReadAllText(factFile);
        FactDto? dto = JsonSerializer.Deserialize<FactDto>(json, s_jsonOptions);
        if (dto is null)
        {
            return null;
        }

        Fact fact = new(
            ContentHash.FromHex(dto.Hash),
            Enum.Parse<FactKind>(dto.Kind),
            dto.Content,
            dto.Author,
            dto.Timestamp,
            dto.Justification,
            [.. dto.References.Select(ContentHash.FromHex)]);

        m_cache = m_cache.Set(hash, fact);
        return fact;
    }

    public void UpdateView(string name, ContentHash hash)
    {
        Map<string, string> view = LoadViewMap();
        view = view.Set(name, hash.ToHex());
        SaveViewMap(view);
    }

    public ContentHash? LookupView(string name)
    {
        Map<string, string> view = LoadViewMap();
        string? hex = view[name];
        return hex is not null ? ContentHash.FromHex(hex) : null;
    }

    public ValueMap<string, ContentHash> GetView()
    {
        Map<string, string> raw = LoadViewMap();
        ValueMap<string, ContentHash> result = ValueMap<string, ContentHash>.s_empty;
        foreach (KeyValuePair<string, string> kv in raw)
        {
            result = result.Set(kv.Key, ContentHash.FromHex(kv.Value));
        }
        return result;
    }

    public IReadOnlyList<Fact> GetHistory(string name)
    {
        List<Fact> history = [];
        ContentHash? current = LookupView(name);
        Set<string> visited = Set<string>.s_empty;

        while (current is not null)
        {
            string hex = current.Value.ToHex();
            if (visited.Contains(hex))
            {
                break;
            }

            visited = visited.Add(hex);

            Fact? fact = Load(current.Value);
            if (fact is null)
            {
                break;
            }

            history.Add(fact);

            Fact? supersession = FindSupersessionOf(current.Value);
            if (supersession is not null)
            {
                ContentHash oldHash = supersession.References.Length > 1
                    ? supersession.References[1]
                    : default;
                if (oldHash.Bytes.Length > 0)
                {
                    current = oldHash;
                    continue;
                }
            }
            break;
        }

        return history;
    }

    Fact? FindSupersessionOf(ContentHash newHash)
    {
        if (!Directory.Exists(m_factsPath))
        {
            return null;
        }

        foreach (string subDir in Directory.GetDirectories(m_factsPath))
        {
            foreach (string file in Directory.GetFiles(subDir, "*.json"))
            {
                string json = File.ReadAllText(file);
                FactDto? dto = JsonSerializer.Deserialize<FactDto>(json, s_jsonOptions);
                if (dto?.Kind == "Supersession" && dto.References.Contains(newHash.ToHex()))
                {
                    return DtoToFact(dto);
                }
            }
        }
        return null;
    }

    public bool IsInitialized => Directory.Exists(Path.Combine(m_rootPath, ".codex"));

    public IReadOnlyList<Fact> GetFactsByKind(FactKind kind)
    {
        List<Fact> results = [];
        if (!Directory.Exists(m_factsPath))
        {
            return results;
        }

        foreach (string subDir in Directory.GetDirectories(m_factsPath))
        {
            foreach (string file in Directory.GetFiles(subDir, "*.json"))
            {
                string json = File.ReadAllText(file);
                FactDto? dto = JsonSerializer.Deserialize<FactDto>(json, s_jsonOptions);
                if (dto is null || dto.Kind != kind.ToString())
                {
                    continue;
                }

                Fact fact = DtoToFact(dto);
                results.Add(fact);
            }
        }
        return results;
    }

    public IReadOnlyList<Fact> GetProposals()
    {
        return GetFactsByKind(FactKind.Proposal);
    }

    public IReadOnlyList<Fact> GetVerdicts(ContentHash proposalHash)
    {
        List<Fact> results = [];
        IReadOnlyList<Fact> allVerdicts = GetFactsByKind(FactKind.Verdict);
        string proposalHex = proposalHash.ToHex();
        foreach (Fact verdict in allVerdicts)
        {
            if (verdict.Content.Contains($"proposal:{proposalHex}"))
            {
                results.Add(verdict);
            }
        }
        return results;
    }

    public static ImmutableArray<string> ParseStakeholders(Fact proposal)
    {
        foreach (string line in proposal.Content.Split('\n'))
        {
            if (line.StartsWith("stakeholders:", StringComparison.Ordinal))
            {
                string value = line["stakeholders:".Length..];
                if (string.IsNullOrWhiteSpace(value))
                {
                    return [];
                }

                return [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
            }
        }
        return [];
    }

    public static ContentHash? ParseDefinitionHash(Fact proposal)
    {
        foreach (string line in proposal.Content.Split('\n'))
        {
            if (line.StartsWith("definition:", StringComparison.Ordinal))
            {
                string hex = line["definition:".Length..].Trim();
                return ContentHash.FromHex(hex);
            }
        }
        return null;
    }

    public static VerdictDecision? ParseVerdictDecision(Fact verdict)
    {
        foreach (string line in verdict.Content.Split('\n'))
        {
            if (line.StartsWith("decision:", StringComparison.Ordinal))
            {
                string value = line["decision:".Length..].Trim();
                if (Enum.TryParse<VerdictDecision>(value, out VerdictDecision decision))
                {
                    return decision;
                }
            }
        }
        return null;
    }

    public bool CheckConsensus(ContentHash proposalHash)
    {
        Fact? proposal = Load(proposalHash);
        if (proposal is null || proposal.Kind != FactKind.Proposal)
        {
            return false;
        }

        ImmutableArray<string> stakeholders = ParseStakeholders(proposal);
        if (stakeholders.Length == 0)
        {
            return true;
        }

        IReadOnlyList<Fact> verdicts = GetVerdicts(proposalHash);
        Set<string> accepted = Set<string>.s_empty;

        foreach (Fact verdict in verdicts)
        {
            VerdictDecision? decision = ParseVerdictDecision(verdict);
            if (decision is VerdictDecision.Reject)
            {
                return false;
            }

            if (decision is VerdictDecision.Accept or VerdictDecision.Abstain)
            {
                accepted = accepted.Add(verdict.Author);
            }
        }

        foreach (string stakeholder in stakeholders)
        {
            if (!accepted.Contains(stakeholder))
            {
                return false;
            }
        }
        return true;
    }

    public bool AcceptProposal(ContentHash proposalHash, string viewName)
    {
        if (!CheckConsensus(proposalHash))
        {
            return false;
        }

        Fact? proposal = Load(proposalHash);
        if (proposal is null)
        {
            return false;
        }

        ContentHash? definitionHash = ParseDefinitionHash(proposal);
        if (definitionHash is null)
        {
            return false;
        }

        UpdateView(viewName, definitionHash.Value);
        return true;
    }

    public IReadOnlyList<Fact> GetTrustFacts(ContentHash targetHash)
    {
        List<Fact> results = [];
        IReadOnlyList<Fact> allTrust = GetFactsByKind(FactKind.Trust);
        string targetHex = targetHash.ToHex();
        foreach (Fact trust in allTrust)
        {
            if (trust.Content.Contains($"target:{targetHex}"))
            {
                results.Add(trust);
            }
        }
        return results;
    }

    public static TrustDegree? ParseTrustDegree(Fact trust)
    {
        foreach (string line in trust.Content.Split('\n'))
        {
            if (line.StartsWith("degree:", StringComparison.Ordinal))
            {
                string value = line["degree:".Length..].Trim();
                if (Enum.TryParse<TrustDegree>(value, out TrustDegree degree))
                {
                    return degree;
                }
            }
        }
        return null;
    }

    public SyncResult Sync(FactStore other)
    {
        int sent = 0;
        int received = 0;

        Set<string> localHashes = CollectAllHashes();
        Set<string> remoteHashes = other.CollectAllHashes();

        foreach (string hex in remoteHashes)
        {
            if (!localHashes.Contains(hex))
            {
                ContentHash hash = ContentHash.FromHex(hex);
                Fact? fact = other.Load(hash);
                if (fact is not null)
                {
                    Store(fact);
                    received++;
                }
            }
        }

        foreach (string hex in localHashes)
        {
            if (!remoteHashes.Contains(hex))
            {
                ContentHash hash = ContentHash.FromHex(hex);
                Fact? fact = Load(hash);
                if (fact is not null)
                {
                    other.Store(fact);
                    sent++;
                }
            }
        }

        return new SyncResult(sent, received);
    }

    Set<string> CollectAllHashes()
    {
        Set<string> hashes = Set<string>.s_empty;
        if (!Directory.Exists(m_factsPath))
        {
            return hashes;
        }

        foreach (string subDir in Directory.GetDirectories(m_factsPath))
        {
            foreach (string file in Directory.GetFiles(subDir, "*.json"))
            {
                string fileName = Path.GetFileNameWithoutExtension(file);
                hashes = hashes.Add(fileName);
            }
        }
        return hashes;
    }

    Fact DtoToFact(FactDto dto)
    {
        return new Fact(
            ContentHash.FromHex(dto.Hash),
            Enum.Parse<FactKind>(dto.Kind),
            dto.Content,
            dto.Author,
            dto.Timestamp,
            dto.Justification,
            [.. dto.References.Select(ContentHash.FromHex)]);
    }

    Map<string, string> LoadViewMap()
    {
        if (!File.Exists(m_viewPath))
        {
            return Map<string, string>.s_empty;
        }

        string json = File.ReadAllText(m_viewPath);
        Dictionary<string, string>? raw =
            JsonSerializer.Deserialize<Dictionary<string, string>>(json, s_jsonOptions);
        if (raw is null)
        {
            return Map<string, string>.s_empty;
        }

        Map<string, string> result = Map<string, string>.s_empty;
        foreach (KeyValuePair<string, string> kv in raw)
        {
            result = result.Set(kv.Key, kv.Value);
        }

        return result;
    }

    void SaveViewMap(Map<string, string> view)
    {
        Dictionary<string, string> raw = [];
        foreach (KeyValuePair<string, string> kv in view)
        {
            raw[kv.Key] = kv.Value;
        }

        string json = JsonSerializer.Serialize(raw, s_jsonOptions);
        File.WriteAllText(m_viewPath, json);
    }

    static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    sealed class FactDto
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

public readonly record struct SyncResult(int Sent, int Received);
