using System.Collections.Immutable;
using Codex.Core;
using Codex.Repository;

namespace Codex.Cli;

public static partial class Program
{
    public static int RunPropose(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine(
                "Usage: codex propose <file.codex> [--stakeholder <name>]... [--justification <text>]");
            return 1;
        }

        string filePath = args[0];
        if (!File.Exists(filePath))
        {
            Console.Error.WriteLine($"File not found: {filePath}");
            return 1;
        }

        string repoDir = FindRepositoryRoot(Directory.GetCurrentDirectory());
        if (repoDir == "")
        {
            Console.Error.WriteLine("No Codex repository found. Run 'codex init' first.");
            return 1;
        }

        FactStore? store = FactStore.Open(repoDir);
        if (store is null)
        {
            Console.Error.WriteLine("Failed to open Codex repository.");
            return 1;
        }

        CompilationResult? result = CompileFile(filePath);
        if (result is null)
        {
            Console.Error.WriteLine("Compilation failed. Fix errors before proposing.");
            return 1;
        }

        List<string> stakeholders = [];
        string justification = "Proposed from CLI";
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--stakeholder" && i + 1 < args.Length)
            {
                stakeholders.Add(args[++i]);
            }
            else if (args[i] == "--justification" && i + 1 < args.Length)
            {
                justification = args[++i];
            }
        }

        string source = File.ReadAllText(filePath);
        string chapterName = Path.GetFileNameWithoutExtension(filePath);
        string author = Environment.UserName;

        Fact definitionFact = Fact.CreateDefinition(source, author, justification);
        ContentHash defHash = store.Store(definitionFact);

        ContentHash? existing = store.LookupView(chapterName);

        Fact proposal = Fact.CreateProposal(
            defHash, author, justification,
            [.. stakeholders],
            existing);
        ContentHash proposalHash = store.Store(proposal);

        Console.WriteLine($"✓ Proposed {chapterName}");
        Console.WriteLine($"  proposal: {proposalHash.ToShortHex()}");
        Console.WriteLine($"  definition: {defHash.ToShortHex()}");
        if (stakeholders.Count > 0)
        {
            Console.WriteLine($"  stakeholders: {string.Join(", ", stakeholders)}");
        }
        else
        {
            Console.WriteLine("  stakeholders: none (auto-accept)");
        }

        return 0;
    }

    public static int RunVerdict(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine(
                "Usage: codex verdict <proposal-hash> <accept|reject|amend|abstain> [reasoning]");
            return 1;
        }

        string proposalHex = args[0];
        string decisionStr = args[1];
        string reasoning = args.Length > 2 ? args[2] : "Verdict from CLI";

        if (!Enum.TryParse<VerdictDecision>(decisionStr, ignoreCase: true, out VerdictDecision decision))
        {
            Console.Error.WriteLine(
                $"Unknown decision: {decisionStr}. Use accept, reject, amend, or abstain.");
            return 1;
        }

        string repoDir = FindRepositoryRoot(Directory.GetCurrentDirectory());
        if (repoDir == "")
        {
            Console.Error.WriteLine("No Codex repository found. Run 'codex init' first.");
            return 1;
        }

        FactStore? store = FactStore.Open(repoDir);
        if (store is null)
        {
            Console.Error.WriteLine("Failed to open Codex repository.");
            return 1;
        }

        ContentHash proposalHash = ContentHash.FromHex(proposalHex);
        Fact? proposal = store.Load(proposalHash);
        if (proposal is null || proposal.Kind != FactKind.Proposal)
        {
            Console.Error.WriteLine($"No proposal found with hash {proposalHex}.");
            return 1;
        }

        string author = Environment.UserName;
        Fact verdict = Fact.CreateVerdict(proposalHash, decision, author, reasoning);
        store.Store(verdict);

        Console.WriteLine($"✓ Verdict: {decision} on proposal {proposalHash.ToShortHex()}");
        Console.WriteLine($"  by {author}");
        Console.WriteLine($"  \"{reasoning}\"");

        if (store.CheckConsensus(proposalHash))
        {
            Console.WriteLine();
            Console.WriteLine(
                "  ★ Consensus reached — proposal can be accepted into the view.");
        }

        return 0;
    }

    public static int RunProposals(string[] args)
    {
        string repoDir = FindRepositoryRoot(Directory.GetCurrentDirectory());
        if (repoDir == "")
        {
            Console.Error.WriteLine("No Codex repository found. Run 'codex init' first.");
            return 1;
        }

        FactStore? store = FactStore.Open(repoDir);
        if (store is null)
        {
            Console.Error.WriteLine("Failed to open Codex repository.");
            return 1;
        }

        IReadOnlyList<Fact> proposals = store.GetProposals();
        if (proposals.Count == 0)
        {
            Console.WriteLine("No proposals found.");
            return 0;
        }

        Console.WriteLine($"Proposals ({proposals.Count}):");
        Console.WriteLine();
        foreach (Fact proposal in proposals)
        {
            ImmutableArray<string> stakeholders = FactStore.ParseStakeholders(proposal);
            IReadOnlyList<Fact> verdicts = store.GetVerdicts(proposal.Hash);
            bool consensus = store.CheckConsensus(proposal.Hash);
            string status = consensus
                ? "✓ consensus"
                : $"{verdicts.Count}/{stakeholders.Length} verdicts";

            Console.WriteLine($"  {proposal.Hash.ToShortHex()} by {proposal.Author}");
            Console.WriteLine($"    \"{proposal.Justification}\"");
            Console.WriteLine(
                $"    stakeholders: " +
                $"{(stakeholders.Length > 0 ? string.Join(", ", stakeholders) : "none")}");
            Console.WriteLine($"    status: {status}");
            Console.WriteLine();
        }

        return 0;
    }

    public static int RunVouch(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine(
                "Usage: codex vouch <hash> <reviewed|tested|verified|critical> [reasoning]");
            return 1;
        }

        string targetHex = args[0];
        string degreeStr = args[1];
        string reasoning = args.Length > 2 ? args[2] : "Vouched from CLI";

        if (!Enum.TryParse<TrustDegree>(degreeStr, ignoreCase: true, out TrustDegree degree))
        {
            Console.Error.WriteLine(
                $"Unknown trust degree: {degreeStr}. Use reviewed, tested, verified, or critical.");
            return 1;
        }

        string repoDir = FindRepositoryRoot(Directory.GetCurrentDirectory());
        if (repoDir == "")
        {
            Console.Error.WriteLine("No Codex repository found. Run 'codex init' first.");
            return 1;
        }

        FactStore? store = FactStore.Open(repoDir);
        if (store is null)
        {
            Console.Error.WriteLine("Failed to open Codex repository.");
            return 1;
        }

        ContentHash targetHash = ContentHash.FromHex(targetHex);
        Fact? target = store.Load(targetHash);
        if (target is null)
        {
            Console.Error.WriteLine($"No fact found with hash {targetHex}.");
            return 1;
        }

        string author = Environment.UserName;
        Fact trust = Fact.CreateTrust(targetHash, degree, author, reasoning);
        store.Store(trust);

        Console.WriteLine($"✓ Vouched ({degree}) for {targetHash.ToShortHex()}");
        Console.WriteLine($"  by {author}");
        Console.WriteLine($"  \"{reasoning}\"");

        return 0;
    }

    public static int RunSync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: codex sync <remote-repo-path>");
            return 1;
        }

        string remotePath = args[0];

        string repoDir = FindRepositoryRoot(Directory.GetCurrentDirectory());
        if (repoDir == "")
        {
            Console.Error.WriteLine("No Codex repository found. Run 'codex init' first.");
            return 1;
        }

        FactStore? localStore = FactStore.Open(repoDir);
        if (localStore is null)
        {
            Console.Error.WriteLine("Failed to open local Codex repository.");
            return 1;
        }

        FactStore? remoteStore = FactStore.Open(remotePath);
        if (remoteStore is null)
        {
            Console.Error.WriteLine(
                $"Failed to open remote Codex repository at {remotePath}.");
            return 1;
        }

        SyncResult result = localStore.Sync(remoteStore);

        Console.WriteLine("✓ Sync complete");
        Console.WriteLine($"  sent: {result.Sent} fact(s)");
        Console.WriteLine($"  received: {result.Received} fact(s)");

        return 0;
    }
}
