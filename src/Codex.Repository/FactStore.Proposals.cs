using System.Collections.Immutable;
using Codex.Core;

namespace Codex.Repository;

/// <summary>
/// A structured proposal: a named set of additions and removals to a view.
/// </summary>
public sealed record ViewProposal(
    string Name,
    IReadOnlyList<ProposalAddition> Additions,
    IReadOnlyList<string> Removals);

public sealed record ProposalAddition(string DefinitionName, ContentHash DefinitionHash);

/// <summary>
/// Result of previewing a proposal against a view.
/// </summary>
public sealed record ProposalPreview(
    ValueMap<string, ContentHash> ResultingView,
    IReadOnlyList<string> AddedNames,
    IReadOnlyList<string> RemovedNames,
    IReadOnlyList<string> ModifiedNames);

partial class FactStore
{
    /// <summary>
    /// Create a proposal fact that describes a view diff (multiple adds/removes).
    /// </summary>
    public Fact CreateViewProposal(
        string proposalName,
        IReadOnlyList<ProposalAddition> additions,
        IReadOnlyList<string> removals,
        string author,
        string justification,
        ImmutableArray<string> stakeholders)
    {
        List<string> lines = [$"proposal-name:{proposalName}"];
        List<ContentHash> refs = [];

        foreach (ProposalAddition add in additions)
        {
            lines.Add($"add:{add.DefinitionName}:{add.DefinitionHash.ToHex()}");
            refs.Add(add.DefinitionHash);
        }

        foreach (string removal in removals)
        {
            lines.Add($"remove:{removal}");
        }

        lines.Add($"stakeholders:{string.Join(",", stakeholders)}");

        string content = string.Join("\n", lines);
        ContentHash hash = ContentHash.Of(content + $"\nauthor:{author}\ntime:{DateTime.UtcNow:O}");
        return new Fact(hash, FactKind.Proposal, content, author, DateTime.UtcNow,
            justification, refs.ToImmutableArray());
    }

    /// <summary>
    /// Parse a view proposal fact into structured form.
    /// </summary>
    public static ViewProposal? ParseViewProposal(Fact proposal)
    {
        if (proposal.Kind != FactKind.Proposal)
            return null;

        string name = "";
        List<ProposalAddition> additions = [];
        List<string> removals = [];

        foreach (string line in proposal.Content.Split('\n'))
        {
            if (line.StartsWith("proposal-name:", StringComparison.Ordinal))
            {
                name = line["proposal-name:".Length..].Trim();
            }
            else if (line.StartsWith("add:", StringComparison.Ordinal))
            {
                string rest = line["add:".Length..];
                int colonIdx = rest.IndexOf(':');
                if (colonIdx > 0)
                {
                    string defName = rest[..colonIdx];
                    string hashHex = rest[(colonIdx + 1)..];
                    additions.Add(new ProposalAddition(defName, ContentHash.FromHex(hashHex)));
                }
            }
            else if (line.StartsWith("remove:", StringComparison.Ordinal))
            {
                removals.Add(line["remove:".Length..].Trim());
            }
        }

        if (name.Length == 0)
            return null;

        return new ViewProposal(name, additions, removals);
    }

    /// <summary>
    /// Preview what a view would look like after applying a proposal.
    /// Does not modify the view.
    /// </summary>
    public ProposalPreview PreviewProposal(string viewName, ViewProposal proposal)
    {
        RequireViewExists(viewName);
        ValueMap<string, ContentHash> current = GetNamedView(viewName);

        List<string> added = [];
        List<string> removed = [];
        List<string> modified = [];

        ValueMap<string, ContentHash> result = current;

        foreach (string removal in proposal.Removals)
        {
            if (result[removal] is not null)
            {
                result = result.Remove(removal);
                removed.Add(removal);
            }
        }

        foreach (ProposalAddition addition in proposal.Additions)
        {
            ContentHash? existing = result[addition.DefinitionName];
            if (existing is not null)
            {
                modified.Add(addition.DefinitionName);
            }
            else
            {
                added.Add(addition.DefinitionName);
            }
            result = result.Set(addition.DefinitionName, addition.DefinitionHash);
        }

        return new ProposalPreview(result, added, removed, modified);
    }

    /// <summary>
    /// Check whether applying a proposal to a view would produce a consistent result.
    /// </summary>
    public ViewConsistencyResult CheckProposalConsistency(
        string viewName,
        ViewProposal proposal,
        IViewConsistencyChecker checker)
    {
        ProposalPreview preview = PreviewProposal(viewName, proposal);
        List<ViewDefinition> definitions = [];

        foreach (KeyValuePair<string, ContentHash> kv in preview.ResultingView)
        {
            Fact? fact = Load(kv.Value);
            if (fact is null)
            {
                return new ViewConsistencyResult(false,
                    [$"Definition '{kv.Key}' references missing fact {kv.Value.ToShortHex()}"]);
            }
            if (fact.Kind != FactKind.Definition)
            {
                return new ViewConsistencyResult(false,
                    [$"Entry '{kv.Key}' references a {fact.Kind} fact, expected Definition"]);
            }
            definitions.Add(new ViewDefinition(kv.Key, fact.Content));
        }

        if (definitions.Count == 0)
            return new ViewConsistencyResult(true, []);

        return checker.Check(definitions);
    }

    /// <summary>
    /// Apply a proposal to a view. Requires consensus and consistency.
    /// Returns true if applied successfully.
    /// </summary>
    public bool ApplyViewProposal(
        ContentHash proposalFactHash,
        string viewName,
        IViewConsistencyChecker checker)
    {
        Fact? proposalFact = Load(proposalFactHash);
        if (proposalFact is null)
            return false;

        if (!CheckConsensus(proposalFactHash))
            return false;

        ViewProposal? proposal = ParseViewProposal(proposalFact);
        if (proposal is null)
            return false;

        ViewConsistencyResult consistency = CheckProposalConsistency(viewName, proposal, checker);
        if (!consistency.IsConsistent)
            return false;

        // Apply changes
        RequireViewExists(viewName);
        string viewFile = ResolveViewFile(viewName);
        Map<string, string> map = LoadViewMapFrom(viewFile);

        foreach (string removal in proposal.Removals)
            map = map.Remove(removal);

        foreach (ProposalAddition addition in proposal.Additions)
            map = map.Set(addition.DefinitionName, addition.DefinitionHash.ToHex());

        SaveViewMapTo(viewFile, map);
        return true;
    }
}
