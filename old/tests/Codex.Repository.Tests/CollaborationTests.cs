using System.Collections.Immutable;
using Codex.Core;
using Codex.Repository;
using Xunit;

namespace Codex.Repository.Tests;

public class FactTests
{
    [Fact]
    public void CreateDefinition_produces_deterministic_hash()
    {
        Fact f1 = Fact.CreateDefinition("square x = x * x", "alice", "initial");
        Fact f2 = Fact.CreateDefinition("square x = x * x", "bob", "copy");
        Assert.Equal(f1.Hash, f2.Hash);
    }

    [Fact]
    public void CreateProposal_stores_stakeholders_in_content()
    {
        ContentHash defHash = ContentHash.Of("some definition");
        Fact proposal = Fact.CreateProposal(
            defHash, "alice", "add sort",
            ["bob", "carol"]);

        ImmutableArray<string> stakeholders = FactStore.ParseStakeholders(proposal);
        Assert.Equal(2, stakeholders.Length);
        Assert.Contains("bob", stakeholders);
        Assert.Contains("carol", stakeholders);
    }

    [Fact]
    public void CreateProposal_references_definition()
    {
        ContentHash defHash = ContentHash.Of("def");
        Fact proposal = Fact.CreateProposal(defHash, "alice", "new fn", []);
        Assert.Contains(defHash, proposal.References);
    }

    [Fact]
    public void CreateProposal_with_supersedes_references_both()
    {
        ContentHash defHash = ContentHash.Of("new-def");
        ContentHash oldHash = ContentHash.Of("old-def");
        Fact proposal = Fact.CreateProposal(defHash, "alice", "update", [], oldHash);
        Assert.Contains(defHash, proposal.References);
        Assert.Contains(oldHash, proposal.References);
    }

    [Fact]
    public void CreateVerdict_parses_decision()
    {
        ContentHash proposalHash = ContentHash.Of("proposal");
        Fact verdict = Fact.CreateVerdict(
            proposalHash, VerdictDecision.Accept, "bob", "looks good");

        VerdictDecision? decision = FactStore.ParseVerdictDecision(verdict);
        Assert.Equal(VerdictDecision.Accept, decision);
    }

    [Fact]
    public void CreateTrust_parses_degree()
    {
        ContentHash target = ContentHash.Of("target-fact");
        Fact trust = Fact.CreateTrust(target, TrustDegree.Verified, "carol", "checked proofs");

        TrustDegree? degree = FactStore.ParseTrustDegree(trust);
        Assert.Equal(TrustDegree.Verified, degree);
    }

    [Fact]
    public void ParseDefinitionHash_extracts_hash()
    {
        ContentHash defHash = ContentHash.Of("definition-content");
        Fact proposal = Fact.CreateProposal(defHash, "alice", "justification", []);

        ContentHash? parsed = FactStore.ParseDefinitionHash(proposal);
        Assert.NotNull(parsed);
        Assert.Equal(defHash, parsed.Value);
    }
}

public class FactStoreTests : IDisposable
{
    readonly string m_tempDir;
    readonly FactStore m_store;

    public FactStoreTests()
    {
        m_tempDir = Path.Combine(Path.GetTempPath(), "codex_test_" + Guid.NewGuid().ToString("N")[..8]);
        m_store = FactStore.Init(m_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(m_tempDir, true); } catch { }
    }

    [Fact]
    public void Init_creates_codex_directory()
    {
        Assert.True(m_store.IsInitialized);
    }

    [Fact]
    public void Store_and_load_roundtrip()
    {
        Fact fact = Fact.CreateDefinition("hello = 42", "alice", "initial");
        ContentHash hash = m_store.Store(fact);

        Fact? loaded = m_store.Load(hash);
        Assert.NotNull(loaded);
        Assert.Equal(fact.Hash, loaded.Hash);
        Assert.Equal(fact.Content, loaded.Content);
        Assert.Equal(fact.Kind, loaded.Kind);
    }

    [Fact]
    public void View_update_and_lookup()
    {
        Fact fact = Fact.CreateDefinition("x = 1", "alice", "init");
        ContentHash hash = m_store.Store(fact);
        m_store.UpdateView("my-module", hash);

        ContentHash? looked = m_store.LookupView("my-module");
        Assert.NotNull(looked);
        Assert.Equal(hash, looked.Value);
    }

    [Fact]
    public void GetProposals_returns_proposal_facts()
    {
        ContentHash defHash = ContentHash.Of("def-source");
        Fact def = Fact.CreateDefinition("def-source", "alice", "init");
        m_store.Store(def);

        Fact proposal = Fact.CreateProposal(defHash, "alice", "add feature", ["bob"]);
        m_store.Store(proposal);

        IReadOnlyList<Fact> proposals = m_store.GetProposals();
        Assert.Single(proposals);
        Assert.Equal(FactKind.Proposal, proposals[0].Kind);
    }

    [Fact]
    public void GetVerdicts_returns_verdicts_for_proposal()
    {
        ContentHash defHash = ContentHash.Of("def");
        Fact proposal = Fact.CreateProposal(defHash, "alice", "change", ["bob"]);
        ContentHash proposalHash = m_store.Store(proposal);

        Fact verdict = Fact.CreateVerdict(proposalHash, VerdictDecision.Accept, "bob", "lgtm");
        m_store.Store(verdict);

        IReadOnlyList<Fact> verdicts = m_store.GetVerdicts(proposalHash);
        Assert.Single(verdicts);
        Assert.Equal(FactKind.Verdict, verdicts[0].Kind);
    }

    [Fact]
    public void CheckConsensus_true_when_all_stakeholders_accept()
    {
        ContentHash defHash = ContentHash.Of("def-content");
        Fact proposal = Fact.CreateProposal(defHash, "alice", "update", ["bob", "carol"]);
        ContentHash proposalHash = m_store.Store(proposal);

        Assert.False(m_store.CheckConsensus(proposalHash));

        Fact v1 = Fact.CreateVerdict(proposalHash, VerdictDecision.Accept, "bob", "ok");
        m_store.Store(v1);
        Assert.False(m_store.CheckConsensus(proposalHash));

        Fact v2 = Fact.CreateVerdict(proposalHash, VerdictDecision.Accept, "carol", "ok");
        m_store.Store(v2);
        Assert.True(m_store.CheckConsensus(proposalHash));
    }

    [Fact]
    public void CheckConsensus_false_when_any_stakeholder_rejects()
    {
        ContentHash defHash = ContentHash.Of("def-content");
        Fact proposal = Fact.CreateProposal(defHash, "alice", "update", ["bob", "carol"]);
        ContentHash proposalHash = m_store.Store(proposal);

        Fact v1 = Fact.CreateVerdict(proposalHash, VerdictDecision.Accept, "bob", "ok");
        m_store.Store(v1);

        Fact v2 = Fact.CreateVerdict(proposalHash, VerdictDecision.Reject, "carol", "no");
        m_store.Store(v2);

        Assert.False(m_store.CheckConsensus(proposalHash));
    }

    [Fact]
    public void CheckConsensus_abstain_counts_as_agreement()
    {
        ContentHash defHash = ContentHash.Of("def-content");
        Fact proposal = Fact.CreateProposal(defHash, "alice", "update", ["bob"]);
        ContentHash proposalHash = m_store.Store(proposal);

        Fact v = Fact.CreateVerdict(proposalHash, VerdictDecision.Abstain, "bob", "no opinion");
        m_store.Store(v);

        Assert.True(m_store.CheckConsensus(proposalHash));
    }

    [Fact]
    public void CheckConsensus_no_stakeholders_is_auto_accept()
    {
        ContentHash defHash = ContentHash.Of("def-content");
        Fact proposal = Fact.CreateProposal(defHash, "alice", "trivial change", []);
        ContentHash proposalHash = m_store.Store(proposal);

        Assert.True(m_store.CheckConsensus(proposalHash));
    }

    [Fact]
    public void AcceptProposal_updates_view_on_consensus()
    {
        ContentHash defHash = ContentHash.Of("new-def");
        Fact def = Fact.CreateDefinition("new-def", "alice", "init");
        m_store.Store(def);

        Fact proposal = Fact.CreateProposal(def.Hash, "alice", "add", ["bob"]);
        ContentHash proposalHash = m_store.Store(proposal);

        Fact verdict = Fact.CreateVerdict(proposalHash, VerdictDecision.Accept, "bob", "ok");
        m_store.Store(verdict);

        bool accepted = m_store.AcceptProposal(proposalHash, "my-module");
        Assert.True(accepted);

        ContentHash? viewHash = m_store.LookupView("my-module");
        Assert.NotNull(viewHash);
        Assert.Equal(def.Hash, viewHash.Value);
    }

    [Fact]
    public void AcceptProposal_fails_without_consensus()
    {
        ContentHash defHash = ContentHash.Of("new-def");
        Fact proposal = Fact.CreateProposal(defHash, "alice", "add", ["bob"]);
        ContentHash proposalHash = m_store.Store(proposal);

        bool accepted = m_store.AcceptProposal(proposalHash, "my-module");
        Assert.False(accepted);
    }

    [Fact]
    public void GetTrustFacts_returns_trust_for_target()
    {
        Fact def = Fact.CreateDefinition("trusted-code", "alice", "init");
        ContentHash defHash = m_store.Store(def);

        Fact trust = Fact.CreateTrust(defHash, TrustDegree.Reviewed, "bob", "read it");
        m_store.Store(trust);

        IReadOnlyList<Fact> trusts = m_store.GetTrustFacts(defHash);
        Assert.Single(trusts);

        TrustDegree? degree = FactStore.ParseTrustDegree(trusts[0]);
        Assert.Equal(TrustDegree.Reviewed, degree);
    }
}

public class SyncTests : IDisposable
{
    readonly string m_localDir;
    readonly string m_remoteDir;
    readonly FactStore m_localStore;
    readonly FactStore m_remoteStore;

    public SyncTests()
    {
        m_localDir = Path.Combine(
            Path.GetTempPath(), "codex_sync_local_" + Guid.NewGuid().ToString("N")[..8]);
        m_remoteDir = Path.Combine(
            Path.GetTempPath(), "codex_sync_remote_" + Guid.NewGuid().ToString("N")[..8]);
        m_localStore = FactStore.Init(m_localDir);
        m_remoteStore = FactStore.Init(m_remoteDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(m_localDir, true); } catch { }
        try { Directory.Delete(m_remoteDir, true); } catch { }
    }

    [Fact]
    public void Sync_transfers_facts_bidirectionally()
    {
        Fact localFact = Fact.CreateDefinition("local-code", "alice", "local work");
        m_localStore.Store(localFact);

        Fact remoteFact = Fact.CreateDefinition("remote-code", "bob", "remote work");
        m_remoteStore.Store(remoteFact);

        SyncResult result = m_localStore.Sync(m_remoteStore);

        Assert.Equal(1, result.Sent);
        Assert.Equal(1, result.Received);

        Assert.NotNull(m_localStore.Load(remoteFact.Hash));
        Assert.NotNull(m_remoteStore.Load(localFact.Hash));
    }

    [Fact]
    public void Sync_is_idempotent()
    {
        Fact fact = Fact.CreateDefinition("shared-code", "alice", "init");
        m_localStore.Store(fact);

        SyncResult first = m_localStore.Sync(m_remoteStore);
        Assert.Equal(1, first.Sent);
        Assert.Equal(0, first.Received);

        SyncResult second = m_localStore.Sync(m_remoteStore);
        Assert.Equal(0, second.Sent);
        Assert.Equal(0, second.Received);
    }

    [Fact]
    public void Sync_empty_stores_transfers_nothing()
    {
        SyncResult result = m_localStore.Sync(m_remoteStore);
        Assert.Equal(0, result.Sent);
        Assert.Equal(0, result.Received);
    }

    [Fact]
    public void Sync_transfers_proposals_and_verdicts()
    {
        ContentHash defHash = ContentHash.Of("proposed-def");
        Fact proposal = Fact.CreateProposal(defHash, "alice", "new feature", ["bob"]);
        ContentHash proposalHash = m_localStore.Store(proposal);

        Fact verdict = Fact.CreateVerdict(proposalHash, VerdictDecision.Accept, "bob", "ok");
        m_remoteStore.Store(verdict);

        SyncResult result = m_localStore.Sync(m_remoteStore);

        Assert.Equal(1, result.Sent);
        Assert.Equal(1, result.Received);

        Assert.NotNull(m_localStore.Load(verdict.Hash));
        Assert.NotNull(m_remoteStore.Load(proposal.Hash));
    }
}
