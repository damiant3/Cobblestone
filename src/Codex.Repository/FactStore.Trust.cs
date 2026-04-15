using Codex.Core;

namespace Codex.Repository;

public readonly record struct TrustScore(double Weight, string Reason);

partial class FactStore
{
    static readonly Dictionary<TrustDegree, double> s_trustWeights = new()
    {
        [TrustDegree.Reviewed] = 0.25,
        [TrustDegree.Tested] = 0.5,
        [TrustDegree.Verified] = 0.75,
        [TrustDegree.Critical] = 1.0,
    };

    public TrustScore ComputeTrust(ContentHash factHash, string viewer)
    {
        return ComputeTrustWalk(factHash, viewer, [], 0);
    }

    TrustScore ComputeTrustWalk(
        ContentHash factHash,
        string viewer,
        HashSet<ContentHash> visited,
        int depth)
    {
        if (depth > 5)
        {
            return new TrustScore(0.0, "max depth exceeded");
        }

        if (!visited.Add(factHash))
        {
            return new TrustScore(0.0, "cycle detected");
        }

        IReadOnlyList<Fact> vouches = GetTrustFacts(factHash);
        if (vouches.Count == 0)
        {
            return new TrustScore(0.0, "no vouches");
        }

        double maxTrust = 0.0;
        string bestReason = "no vouches";

        foreach (Fact vouch in vouches)
        {
            TrustDegree? degree = ParseTrustDegree(vouch);
            if (degree is null)
            {
                continue;
            }

            double directWeight = s_trustWeights[degree.Value];

            if (vouch.Author == viewer)
            {
                // Direct vouch by the viewer — full weight
                if (directWeight > maxTrust)
                {
                    maxTrust = directWeight;
                    bestReason = $"direct vouch ({degree.Value}) by {viewer}";
                }
            }
            else
            {
                // Transitive: how much does the viewer trust this voucher?
                double voucherTrust = ComputeAuthorTrust(vouch.Author, viewer, visited, depth + 1);
                double transitive = voucherTrust * directWeight;
                if (transitive > maxTrust)
                {
                    maxTrust = transitive;
                    bestReason = $"transitive via {vouch.Author} ({degree.Value}, voucher trust={voucherTrust:F2})";
                }
            }
        }

        return new TrustScore(maxTrust, bestReason);
    }

    double ComputeAuthorTrust(
        string author,
        string viewer,
        HashSet<ContentHash> visited,
        int depth)
    {
        if (author == viewer)
        {
            return 1.0;
        }

        // Find all facts authored by this person that the viewer has vouched for
        IReadOnlyList<Fact> allTrust = GetFactsByKind(FactKind.Trust);
        double maxTrust = 0.0;

        foreach (Fact vouch in allTrust)
        {
            if (vouch.Author != viewer)
            {
                continue;
            }

            // This is a vouch by the viewer — check if it targets something by the author
            string? targetHex = ExtractTarget(vouch);
            if (targetHex is null)
            {
                continue;
            }

            Fact? targetFact = Load(ContentHash.FromHex(targetHex));
            if (targetFact is null || targetFact.Author != author)
            {
                continue;
            }

            TrustDegree? degree = ParseTrustDegree(vouch);
            if (degree is null)
            {
                continue;
            }

            double weight = s_trustWeights[degree.Value];
            if (weight > maxTrust)
            {
                maxTrust = weight;
            }
        }

        return maxTrust;
    }

    static string? ExtractTarget(Fact trust)
    {
        foreach (string line in trust.Content.Split('\n'))
        {
            if (line.StartsWith("target:", StringComparison.Ordinal))
            {
                return line["target:".Length..].Trim();
            }
        }
        return null;
    }

    // --- Trust threshold on views ---

    public ViewConsistencyResult CheckViewConsistencyWithTrust(
        string viewName,
        IViewConsistencyChecker checker,
        double trustThreshold,
        string viewer)
    {
        ValueMap<string, ContentHash> view = GetNamedView(viewName);
        List<ViewDefinition> definitions = [];

        foreach (KeyValuePair<string, ContentHash> kv in view)
        {
            Fact? fact = Load(kv.Value);
            if (fact is null)
            {
                return new ViewConsistencyResult(false,
                    [$"Definition '{kv.Key}' references missing fact {kv.Value.ToHex()}"]);
            }
            if (fact.Kind != FactKind.Definition)
            {
                return new ViewConsistencyResult(false,
                    [$"View entry '{kv.Key}' references a {fact.Kind} fact, expected Definition"]);
            }
            definitions.Add(new ViewDefinition(kv.Key, fact.Content));
        }

        foreach (ImportedFact import in LoadViewImports(viewName))
        {
            Fact? fact = Load(import.Hash);
            if (fact is null)
            {
                return new ViewConsistencyResult(false,
                    [$"Import '{import.LocalName}' references missing fact {import.Hash.ToShortHex()}"]);
            }
            if (fact.Kind != FactKind.Definition)
            {
                return new ViewConsistencyResult(false,
                    [$"Import '{import.LocalName}' references a {fact.Kind} fact, expected Definition"]);
            }

            // Trust check
            if (trustThreshold > 0.0)
            {
                TrustScore score = ComputeTrust(import.Hash, viewer);
                if (score.Weight < trustThreshold)
                {
                    return new ViewConsistencyResult(false,
                        [$"Import '{import.LocalName}' has trust {score.Weight:F2} (threshold: {trustThreshold:F2}). {score.Reason}"]);
                }
            }

            definitions.Add(new ViewDefinition(import.LocalName, fact.Content));
        }

        if (definitions.Count == 0)
        {
            return new ViewConsistencyResult(true, []);
        }

        return checker.Check(definitions);
    }
}
