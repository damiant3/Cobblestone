using System;
using System.IO;
using Xunit;

namespace Codex.AgentToolkit.Tests;

[Collection("AgentToolkit")]
public class CodexAgentSessionTests : IDisposable
{
    readonly AgentExeRunner m_runner = new();
    readonly string m_logPath;
    readonly bool m_hadLog;
    readonly string? m_logBackup;

    public CodexAgentSessionTests()
    {
        m_logPath = Path.Combine(m_runner.SolutionRoot, ".codex-agent", "session.log");
        m_hadLog = File.Exists(m_logPath);
        m_logBackup = m_hadLog ? File.ReadAllText(m_logPath) : null;
    }

    public void Dispose()
    {
        if (m_hadLog)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(m_logPath)!);
            File.WriteAllText(m_logPath, m_logBackup ?? string.Empty);
        }
        else if (File.Exists(m_logPath))
        {
            File.Delete(m_logPath);
        }

        m_runner.CleanupTestDir();
    }

    // ─── doctor ──────────────────────────────────────────────────

    [Fact]
    public void Doctor_succeeds_and_shows_known_conditions()
    {
        (int exit, string stdout, string stderr) = m_runner.Run("codex-agent.exe", "doctor");
        Assert.Equal(0, exit);
        Assert.Contains("Doctor", stdout);
        Assert.Contains("CS5001", stdout);
        Assert.Contains("Advice", stdout);
        Assert.Empty(stderr);
    }

    [Fact]
    public void Doctor_detects_cs5001_trap()
    {
        (int exit, string stdout, _) = m_runner.Run("codex-agent.exe", "doctor");
        Assert.Equal(0, exit);
        Assert.Contains("CS5001 trap: ACTIVE", stdout);
    }

    [Fact]
    public void Doctor_shows_condition_count()
    {
        (int exit, string stdout, _) = m_runner.Run("codex-agent.exe", "doctor");
        Assert.Equal(0, exit);
        Assert.Contains("known)", stdout);
    }

    // ─── log ─────────────────────────────────────────────────────

    [Fact]
    public void Log_no_message_prints_usage()
    {
        (int exit, string stdout, string stderr) = m_runner.Run("codex-agent.exe", "log");
        Assert.Equal(0, exit);
        Assert.Contains("Usage", stdout);
        Assert.Empty(stderr);
    }

    [Fact]
    public void Log_writes_and_confirms()
    {
        (int exit, string stdout, string stderr) = m_runner.Run("codex-agent.exe", "log", "test decision recorded");
        Assert.Equal(0, exit);
        Assert.Contains("Logged: test decision recorded", stdout);
        Assert.Empty(stderr);
    }

    [Fact]
    public void Log_then_recall_round_trip()
    {
        m_runner.Run("codex-agent.exe", "log", "first entry");
        m_runner.Run("codex-agent.exe", "log", "second entry");
        (int exit, string stdout, string stderr) = m_runner.Run("codex-agent.exe", "recall");
        Assert.Equal(0, exit);
        Assert.Contains("first entry", stdout);
        Assert.Contains("second entry", stdout);
        Assert.Empty(stderr);
    }

    // ─── recall ──────────────────────────────────────────────────

    [Fact]
    public void Recall_empty_log_shows_message()
    {
        try
        {
            if (File.Exists(m_logPath))
                File.Delete(m_logPath);

            (int exit, string stdout, string stderr) = m_runner.Run("codex-agent.exe", "recall");
            Assert.Equal(0, exit);
            Assert.True(
                stdout.Contains("no session log") || stdout.Contains("empty"),
                $"Expected empty-log message. Got: {stdout}");
            Assert.Empty(stderr);
        }
        finally
        {
            if (m_hadLog)
                File.WriteAllText(m_logPath, m_logBackup!);
        }
    }

    [Fact]
    public void Recall_with_count_limits_output()
    {
        m_runner.Run("codex-agent.exe", "log", "entry A");
        m_runner.Run("codex-agent.exe", "log", "entry B");
        m_runner.Run("codex-agent.exe", "log", "entry C");
        (int exit, string stdout, _) = m_runner.Run("codex-agent.exe", "recall", "1");
        Assert.Equal(0, exit);
        Assert.Contains("entry C", stdout);
    }

    // ─── help includes new commands ──────────────────────────────

    [Fact]
    public void Help_lists_doctor_command()
    {
        (int exit, string stdout, _) = m_runner.Run("codex-agent.exe", "help");
        Assert.Equal(0, exit);
        Assert.Contains("doctor", stdout);
    }

    [Fact]
    public void Help_lists_log_command()
    {
        (int exit, string stdout, _) = m_runner.Run("codex-agent.exe", "help");
        Assert.Equal(0, exit);
        Assert.Contains("log <message>", stdout);
    }

    [Fact]
    public void Help_lists_recall_command()
    {
        (int exit, string stdout, _) = m_runner.Run("codex-agent.exe", "help");
        Assert.Equal(0, exit);
        Assert.Contains("recall", stdout);
    }

}
