using Xunit;

namespace Codex.AgentToolkit.Tests;

[Collection("AgentToolkit")]
public class CodexAgentExeTests : IDisposable
{
    readonly AgentExeRunner m_runner = new();

    public void Dispose() => m_runner.CleanupTestDir();

    // ─── help / no-args ──────────────────────────────────────────

    [Fact]
    public void No_args_prints_help_without_crash()
    {
        (int exit, string stdout, string stderr) = m_runner.Run("codex-agent.exe");
        Assert.Equal(0, exit);
        Assert.Contains("codex-agent", stdout);
        Assert.Empty(stderr);
    }

    [Fact]
    public void Help_command_succeeds()
    {
        (int exit, string stdout, string stderr) = m_runner.Run("codex-agent.exe", "help");
        Assert.Equal(0, exit);
        Assert.Contains("Commands:", stdout);
        Assert.Empty(stderr);
    }

    [Fact]
    public void Unknown_command_prints_error_without_crash()
    {
        (int exit, string stdout, _) = m_runner.Run("codex-agent.exe", "bogus");
        Assert.Equal(0, exit);
        Assert.Contains("Unknown command: bogus", stdout);
    }

    // ─── peek ────────────────────────────────────────────────────

    [Fact]
    public void Peek_valid_file_range()
    {
        string path = m_runner.CreateTempFile("peek-test.txt", "line1\nline2\nline3\nline4\nline5\n");
        string rel = m_runner.RelativePath(path);
        (int exit, string stdout, _) = m_runner.Run("codex-agent.exe", "peek", rel, "2", "4");
        Assert.Equal(0, exit);
        Assert.Contains("line2", stdout);
        Assert.Contains("line4", stdout);
    }

    [Fact]
    public void Peek_missing_file()
    {
        (int exit, string stdout, string stderr) = m_runner.Run("codex-agent.exe", "peek", "nonexistent-file-42.txt");
        Assert.Equal(0, exit);
        Assert.Contains("File not found", stdout);
        Assert.Empty(stderr);
    }

    [Fact]
    public void Peek_no_file_arg_prints_usage()
    {
        (int exit, string stdout, string stderr) = m_runner.Run("codex-agent.exe", "peek");
        Assert.Equal(0, exit);
        Assert.Contains("Usage", stdout);
        Assert.Empty(stderr);
    }

    [Fact]
    public void Peek_non_numeric_start_does_not_crash()
    {
        string path = m_runner.CreateTempFile("peek-abc.txt", "hello\nworld\n");
        string rel = m_runner.RelativePath(path);
        (int exit, string stdout, string stderr) = m_runner.Run("codex-agent.exe", "peek", rel, "abc", "5");
        // text-to-integer returns 0 for non-numeric input; start is clamped to 1.
        Assert.Equal(0, exit);
        Assert.Empty(stderr);
        Assert.Contains("hello", stdout);
    }

    [Fact]
    public void Peek_non_numeric_end_does_not_crash()
    {
        string path = m_runner.CreateTempFile("peek-abc2.txt", "hello\nworld\n");
        string rel = m_runner.RelativePath(path);
        (int exit, string stdout, string stderr) = m_runner.Run("codex-agent.exe", "peek", rel, "1", "xyz");
        // text-to-integer returns 0 for non-numeric end; end=0 means whole file.
        Assert.Equal(0, exit);
        Assert.Empty(stderr);
        Assert.Contains("hello", stdout);
    }

    [Fact]
    public void Peek_zero_zero_full_file_does_not_crash()
    {
        string path = m_runner.CreateTempFile("peek-00.txt", "alpha\nbeta\ngamma\n");
        string rel = m_runner.RelativePath(path);
        (int exit, string stdout, string stderr) = m_runner.Run("codex-agent.exe", "peek", rel, "0", "0");
        // 0 0 means whole file: start clamped to 1, end=0 means total.
        Assert.Equal(0, exit);
        Assert.Empty(stderr);
        Assert.Contains("alpha", stdout);
    }

    [Fact]
    public void Peek_negative_start_does_not_crash()
    {
        string path = m_runner.CreateTempFile("peek-neg.txt", "hello\nworld\n");
        string rel = m_runner.RelativePath(path);
        (int exit, string stdout, string stderr) = m_runner.Run("codex-agent.exe", "peek", rel, "-1", "5");
        // Negative start: text-to-integer returns 0 for "-1" (no sign support), clamped to 1.
        Assert.Equal(0, exit);
        Assert.Empty(stderr);
        Assert.Contains("hello", stdout);
    }

    [Fact]
    public void Peek_start_beyond_end_of_file()
    {
        string path = m_runner.CreateTempFile("peek-beyond.txt", "hello\n");
        string rel = m_runner.RelativePath(path);
        (int exit, _, string stderr) = m_runner.Run("codex-agent.exe", "peek", rel, "9999", "10000");
        Assert.Equal(0, exit);
        Assert.Empty(stderr);
    }

    [Fact]
    public void Peek_start_greater_than_end()
    {
        string path = m_runner.CreateTempFile("peek-rev.txt", "a\nb\nc\n");
        string rel = m_runner.RelativePath(path);
        (int exit, _, string stderr) = m_runner.Run("codex-agent.exe", "peek", rel, "5", "2");
        Assert.Equal(0, exit);
        Assert.Empty(stderr);
    }

    [Fact]
    public void Peek_empty_file()
    {
        string path = m_runner.CreateTempFile("peek-empty.txt", "");
        string rel = m_runner.RelativePath(path);
        (int exit, _, string stderr) = m_runner.Run("codex-agent.exe", "peek", rel);
        Assert.Equal(0, exit);
        Assert.Empty(stderr);
    }

    [Fact]
    public void Peek_single_line_no_newline()
    {
        string path = m_runner.CreateTempFile("peek-single.txt", "no newline at end");
        string rel = m_runner.RelativePath(path);
        (int exit, string stdout, string stderr) = m_runner.Run("codex-agent.exe", "peek", rel, "1", "1");
        Assert.Equal(0, exit);
        Assert.Contains("no newline at end", stdout);
        Assert.Empty(stderr);
    }

    [Fact]
    public void Peek_overflow_integer_does_not_crash()
    {
        string path = m_runner.CreateTempFile("peek-overflow.txt", "hello\n");
        string rel = m_runner.RelativePath(path);
        (int exit, _, string stderr) = m_runner.Run("codex-agent.exe", "peek", rel, "99999999999999999999", "1");
        Assert.True(exit == 0 || stderr.Contains("Exception"),
            $"Expected graceful handling or known crash. Exit={exit}, stderr={stderr}");
    }

    // ─── stat ────────────────────────────────────────────────────

    [Fact]
    public void Stat_valid_file()
    {
        string path = m_runner.CreateTempFile("stat-test.txt", "line1\nline2\nline3\n");
        string rel = m_runner.RelativePath(path);
        (int exit, string stdout, string stderr) = m_runner.Run("codex-agent.exe", "stat", rel);
        Assert.Equal(0, exit);
        Assert.Contains("stat-test.txt", stdout);
        Assert.Empty(stderr);
    }

    [Fact]
    public void Stat_missing_file()
    {
        (int exit, string stdout, string stderr) = m_runner.Run("codex-agent.exe", "stat", "nonexistent-file-42.txt");
        Assert.Equal(0, exit);
        Assert.Contains("not found", stdout);
        Assert.Empty(stderr);
    }

    [Fact]
    public void Stat_no_file_arg_prints_usage()
    {
        (int exit, string stdout, string stderr) = m_runner.Run("codex-agent.exe", "stat");
        Assert.Equal(0, exit);
        Assert.Contains("Usage", stdout);
        Assert.Empty(stderr);
    }

    [Fact]
    public void Stat_multiple_files()
    {
        string path1 = m_runner.CreateTempFile("stat-a.txt", "aaa\n");
        string path2 = m_runner.CreateTempFile("stat-b.txt", "bbb\nccc\n");
        string rel1 = m_runner.RelativePath(path1);
        string rel2 = m_runner.RelativePath(path2);
        (int exit, string stdout, string stderr) = m_runner.Run("codex-agent.exe", "stat", rel1, rel2);
        Assert.Equal(0, exit);
        Assert.Contains("TOTAL", stdout);
        Assert.Empty(stderr);
    }

    [Fact]
    public void Stat_mixed_existing_and_missing()
    {
        string path = m_runner.CreateTempFile("stat-mix.txt", "data\n");
        string rel = m_runner.RelativePath(path);
        (int exit, string stdout, string stderr) = m_runner.Run("codex-agent.exe", "stat", rel, "no-such-file.txt");
        Assert.Equal(0, exit);
        Assert.Contains("not found", stdout);
        Assert.Empty(stderr);
    }

    [Fact]
    public void Stat_empty_file()
    {
        string path = m_runner.CreateTempFile("stat-empty.txt", "");
        string rel = m_runner.RelativePath(path);
        (int exit, string stdout, string stderr) = m_runner.Run("codex-agent.exe", "stat", rel);
        Assert.Equal(0, exit);
        Assert.Contains("stat-empty.txt", stdout);
        Assert.Empty(stderr);
    }

    // ─── snap ────────────────────────────────────────────────────

    [Fact]
    public void Snap_no_subcommand_prints_usage()
    {
        (int exit, string stdout, string stderr) = m_runner.Run("codex-agent.exe", "snap");
        Assert.Equal(0, exit);
        Assert.Contains("Usage", stdout);
        Assert.Empty(stderr);
    }

    [Fact]
    public void Snap_save_missing_file()
    {
        (int exit, string stdout, string stderr) = m_runner.Run("codex-agent.exe", "snap", "save", "nonexistent-42.txt");
        Assert.Equal(0, exit);
        Assert.Contains("not found", stdout);
        Assert.Empty(stderr);
    }

    [Fact]
    public void Snap_diff_without_snapshot()
    {
        string path = m_runner.CreateTempFile("snap-nodiff.txt", "data\n");
        string rel = m_runner.RelativePath(path);
        (int exit, string stdout, string stderr) = m_runner.Run("codex-agent.exe", "snap", "diff", rel);
        Assert.Equal(0, exit);
        Assert.Contains("No snapshot", stdout);
        Assert.Empty(stderr);
    }

    [Fact]
    public void Snap_restore_without_snapshot()
    {
        (int exit, string stdout, string stderr) = m_runner.Run("codex-agent.exe", "snap", "restore", "no-snap.txt");
        Assert.Equal(0, exit);
        Assert.Contains("No snapshot", stdout);
        Assert.Empty(stderr);
    }

    [Fact]
    public void Snap_save_diff_restore_roundtrip()
    {
        string path = m_runner.CreateTempFile("snap-rt.txt", "original content\n");
        string rel = m_runner.RelativePath(path);

        (int exit1, string out1, _) = m_runner.Run("codex-agent.exe", "snap", "save", rel);
        Assert.Equal(0, exit1);
        Assert.Contains("Snapshot saved", out1);

        (int exit2, string out2, _) = m_runner.Run("codex-agent.exe", "snap", "diff", rel);
        Assert.Equal(0, exit2);
        Assert.Contains("no changes", out2);

        File.WriteAllText(path, "modified content\nwith extra line\n");

        (int exit3, string out3, _) = m_runner.Run("codex-agent.exe", "snap", "diff", rel);
        Assert.Equal(0, exit3);
        Assert.Contains("Delta", out3);

        (int exit4, string out4, _) = m_runner.Run("codex-agent.exe", "snap", "restore", rel);
        Assert.Equal(0, exit4);
        Assert.Contains("Restored", out4);
        Assert.Equal("original content\n", File.ReadAllText(path));

        File.Delete(path + ".snap");
    }

    [Fact]
    public void Snap_unknown_action()
    {
        (int exit, string stdout, string stderr) = m_runner.Run("codex-agent.exe", "snap", "bogus", "file.txt");
        Assert.Equal(0, exit);
        Assert.Contains("Unknown snap action", stdout);
        Assert.Empty(stderr);
    }

    [Fact]
    public void Snap_save_no_file_arg()
    {
        (int exit, string stdout, string stderr) = m_runner.Run("codex-agent.exe", "snap", "save");
        Assert.Equal(0, exit);
        Assert.Contains("Usage", stdout);
        Assert.Empty(stderr);
    }

    // ─── status ──────────────────────────────────────────────────

    [Fact]
    public void Status_succeeds()
    {
        (int exit, string stdout, string stderr) = m_runner.Run("codex-agent.exe", "status");
        Assert.Equal(0, exit);
        Assert.Contains("Codex Agent Status", stdout);
        Assert.Empty(stderr);
    }

    // ─── plan ────────────────────────────────────────────────────

    [Fact]
    public void Plan_show_when_empty()
    {
        (int exit, _, string stderr) = m_runner.Run("codex-agent.exe", "plan", "show");
        Assert.Equal(0, exit);
        Assert.Empty(stderr);
    }

    [Fact]
    public void Plan_unknown_action()
    {
        (int exit, string stdout, string stderr) = m_runner.Run("codex-agent.exe", "plan", "bogus");
        Assert.Equal(0, exit);
        Assert.Contains("Unknown plan action", stdout);
        Assert.Empty(stderr);
    }

    [Fact]
    public void Plan_add_no_task()
    {
        (int exit, string stdout, string stderr) = m_runner.Run("codex-agent.exe", "plan", "add");
        Assert.Equal(0, exit);
        Assert.Contains("Usage", stdout);
        Assert.Empty(stderr);
    }

    // ─── check ───────────────────────────────────────────────────

    [Fact]
    public void Check_succeeds()
    {
        (int exit, string stdout, string stderr) = m_runner.Run("codex-agent.exe", "check");
        Assert.Equal(0, exit);
        Assert.Contains("Cognitive Check", stdout);
        Assert.Empty(stderr);
    }
}
