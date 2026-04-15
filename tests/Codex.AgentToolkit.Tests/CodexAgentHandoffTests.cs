using Xunit;

namespace Codex.AgentToolkit.Tests;

[Collection("AgentToolkit")]
public class CodexAgentHandoffTests : IDisposable
{
    readonly AgentExeRunner m_runner = new();
    readonly string m_handoffPath;
    readonly string? m_origHandoff;

    public CodexAgentHandoffTests()
    {
        m_handoffPath = Path.Combine(m_runner.SolutionRoot, ".handoff");
        m_origHandoff = File.Exists(m_handoffPath) ? File.ReadAllText(m_handoffPath) : null;
        if (File.Exists(m_handoffPath))
        {
            File.Delete(m_handoffPath);
        }
    }

    public void Dispose()
    {
        if (m_origHandoff != null)
        {
            File.WriteAllText(m_handoffPath, m_origHandoff);
        }
        else if (File.Exists(m_handoffPath))
        {
            File.Delete(m_handoffPath);
        }

        m_runner.CleanupTestDir();
    }

    // ─── show ────────────────────────────────────────────────────

    [Fact]
    public void Handoff_show_no_active()
    {
        (int exit, string stdout, string stderr) = m_runner.Run("codex-agent.exe", "handoff", "show");
        Assert.Equal(0, exit);
        Assert.Contains("No active handoff", stdout);
        Assert.Empty(stderr);
    }

    [Fact]
    public void Handoff_no_args_shows_status()
    {
        (int exit, string stdout, _) = m_runner.Run("codex-agent.exe", "handoff");
        Assert.Equal(0, exit);
        Assert.Contains("No active handoff", stdout);
    }

    // ─── show with file ──────────────────────────────────────────

    [Fact]
    public void Handoff_show_reads_existing_file()
    {
        File.WriteAllText(m_handoffPath,
            "state=awaiting-review\nbranch=test/branch\nauthor=test-agent\nreviewer=\nsummary=test feature\nupdated=2026-03-21\nnotes=\n");
        (int exit, string stdout, string stderr) = m_runner.Run("codex-agent.exe", "handoff", "show");
        Assert.Equal(0, exit);
        Assert.Contains("awaiting-review", stdout);
        Assert.Contains("test/branch", stdout);
        Assert.Contains("test-agent", stdout);
        Assert.Contains("test feature", stdout);
        Assert.Empty(stderr);
    }

    [Fact]
    public void Handoff_show_displays_next_action()
    {
        File.WriteAllText(m_handoffPath,
            "state=awaiting-review\nbranch=test/branch\nauthor=test-agent\nreviewer=\nsummary=test\nupdated=x\nnotes=\n");
        (int exit, string stdout, _) = m_runner.Run("codex-agent.exe", "handoff", "show");
        Assert.Equal(0, exit);
        Assert.Contains("handoff review", stdout);
    }

    // ─── state machine validation ────────────────────────────────

    [Fact]
    public void Handoff_review_requires_awaiting_review()
    {
        File.WriteAllText(m_handoffPath,
            "state=approved\nbranch=b\nauthor=a\nreviewer=\nsummary=s\nupdated=x\nnotes=\n");
        (int exit, string stdout, _) = m_runner.Run("codex-agent.exe", "handoff", "review");
        Assert.Equal(0, exit);
        Assert.Contains("ERROR", stdout);
        Assert.Contains("need awaiting-review", stdout);
    }

    [Fact]
    public void Handoff_approve_requires_under_review()
    {
        File.WriteAllText(m_handoffPath,
            "state=awaiting-review\nbranch=b\nauthor=a\nreviewer=\nsummary=s\nupdated=x\nnotes=\n");
        (int exit, string stdout, _) = m_runner.Run("codex-agent.exe", "handoff", "approve");
        Assert.Equal(0, exit);
        Assert.Contains("ERROR", stdout);
        Assert.Contains("need under-review", stdout);
    }

    [Fact]
    public void Handoff_merge_requires_approved()
    {
        File.WriteAllText(m_handoffPath,
            "state=under-review\nbranch=b\nauthor=a\nreviewer=r\nsummary=s\nupdated=x\nnotes=\n");
        (int exit, string stdout, _) = m_runner.Run("codex-agent.exe", "handoff", "merge");
        Assert.Equal(0, exit);
        Assert.Contains("ERROR", stdout);
        Assert.Contains("need approved", stdout);
    }

    // ─── review transition ───────────────────────────────────────

    [Fact]
    public void Handoff_review_transitions_to_under_review()
    {
        File.WriteAllText(m_handoffPath,
            "state=awaiting-review\nbranch=test/br\nauthor=a\nreviewer=\nsummary=feat\nupdated=x\nnotes=\n");
        (int exit, string stdout, _) = m_runner.Run("codex-agent.exe", "handoff", "review");
        Assert.Equal(0, exit);
        Assert.Contains("Review started", stdout);
        string content = File.ReadAllText(m_handoffPath);
        Assert.Contains("state=under-review", content);
    }

    // ─── approve transition ──────────────────────────────────────

    [Fact]
    public void Handoff_approve_transitions_to_approved()
    {
        File.WriteAllText(m_handoffPath,
            "state=under-review\nbranch=test/br\nauthor=a\nreviewer=r\nsummary=feat\nupdated=x\nnotes=\n");
        (int exit, string stdout, _) = m_runner.Run("codex-agent.exe", "handoff", "approve");
        Assert.Equal(0, exit);
        Assert.Contains("APPROVED", stdout);
        string content = File.ReadAllText(m_handoffPath);
        Assert.Contains("state=approved", content);
    }

    // ─── request-changes transition ──────────────────────────────

    [Fact]
    public void Handoff_request_changes_transitions()
    {
        File.WriteAllText(m_handoffPath,
            "state=under-review\nbranch=test/br\nauthor=a\nreviewer=r\nsummary=feat\nupdated=x\nnotes=\n");
        (int exit, string stdout, _) = m_runner.Run("codex-agent.exe", "handoff", "request-changes", "needs more tests");
        Assert.Equal(0, exit);
        Assert.Contains("CHANGES REQUESTED", stdout);
        Assert.Contains("needs more tests", stdout);
        string content = File.ReadAllText(m_handoffPath);
        Assert.Contains("state=changes-requested", content);
        Assert.Contains("needs more tests", content);
    }

    // ─── abandon ─────────────────────────────────────────────────

    [Fact]
    public void Handoff_abandon_succeeds()
    {
        File.WriteAllText(m_handoffPath,
            "state=awaiting-review\nbranch=test/br\nauthor=a\nreviewer=\nsummary=feat\nupdated=x\nnotes=\n");
        (int exit, string stdout, _) = m_runner.Run("codex-agent.exe", "handoff", "abandon");
        Assert.Equal(0, exit);
        Assert.Contains("ABANDONED", stdout);
        string content = File.ReadAllText(m_handoffPath);
        Assert.Contains("state=abandoned", content);
    }

    // ─── unknown action ──────────────────────────────────────────

    [Fact]
    public void Handoff_unknown_action()
    {
        (int exit, string stdout, string stderr) = m_runner.Run("codex-agent.exe", "handoff", "bogus");
        Assert.Equal(0, exit);
        Assert.Contains("Unknown handoff action", stdout);
        Assert.Empty(stderr);
    }

    // ─── push usage ──────────────────────────────────────────────

    [Fact]
    public void Handoff_push_no_summary_prints_usage()
    {
        (int exit, string stdout, string stderr) = m_runner.Run("codex-agent.exe", "handoff", "push");
        Assert.Equal(0, exit);
        Assert.Contains("Usage", stdout);
        Assert.Empty(stderr);
    }

    // ─── help includes handoff ───────────────────────────────────

    [Fact]
    public void Help_lists_handoff_commands()
    {
        (int exit, string stdout, _) = m_runner.Run("codex-agent.exe", "help");
        Assert.Equal(0, exit);
        Assert.Contains("handoff push", stdout);
        Assert.Contains("handoff review", stdout);
        Assert.Contains("handoff approve", stdout);
        Assert.Contains("handoff merge", stdout);
        Assert.Contains("handoff abandon", stdout);
        Assert.Contains("Handoff lifecycle", stdout);
    }

    // ─── doctor shows handoff status ─────────────────────────────

    [Fact]
    public void Doctor_shows_handoff_none()
    {
        (int exit, string stdout, _) = m_runner.Run("codex-agent.exe", "doctor");
        Assert.Equal(0, exit);
        Assert.Contains("Handoff: (none active)", stdout);
    }

    [Fact]
    public void Doctor_shows_handoff_active()
    {
        File.WriteAllText(m_handoffPath,
            "state=awaiting-review\nbranch=test/br\nauthor=a\nreviewer=\nsummary=feat\nupdated=x\nnotes=\n");
        (int exit, string stdout, _) = m_runner.Run("codex-agent.exe", "doctor");
        Assert.Equal(0, exit);
        Assert.Contains("Handoff: awaiting-review on test/br", stdout);
    }
}
