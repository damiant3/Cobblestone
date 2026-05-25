using System.Text.RegularExpressions;
using Xunit;

namespace Codex.AgentToolkit.Tests;

public class McpToolNameTests
{
    static readonly Regex s_makeToolPattern = new(
        @"MakeTool\(\s*""([^""]+)""",
        RegexOptions.Compiled);

    static readonly Regex s_validToolName = new(
        @"^[a-zA-Z0-9_-]{1,64}$",
        RegexOptions.Compiled);

    [Fact]
    public void All_MCP_tool_names_match_spec_pattern()
    {
        string repoRoot = FindRepoRoot();
        string mcpProgram = Path.Combine(repoRoot, "tools", "Codex.Mcp", "Program.cs");

        Assert.True(File.Exists(mcpProgram), $"MCP server source not found at {mcpProgram}");

        string source = File.ReadAllText(mcpProgram);
        MatchCollection matches = s_makeToolPattern.Matches(source);

        Assert.True(matches.Count > 0, "No MakeTool calls found in Program.cs — test may be stale");

        List<string> invalid = new();
        foreach (Match match in matches)
        {
            string toolName = match.Groups[1].Value;
            if (!s_validToolName.IsMatch(toolName))
                invalid.Add(toolName);
        }

        Assert.True(invalid.Count == 0,
            $"MCP tool names must match ^[a-zA-Z0-9_-]{{1,64}}$ per spec. Invalid: {string.Join(", ", invalid)}");
    }

    [Fact]
    public void MCP_tool_dispatch_names_match_list_names()
    {
        string repoRoot = FindRepoRoot();
        string programFile = Path.Combine(repoRoot, "tools", "Codex.Mcp", "Program.cs");
        string dispatcherFile = Path.Combine(repoRoot, "tools", "Codex.Mcp", "ToolDispatcher.cs");

        Assert.True(File.Exists(programFile));
        Assert.True(File.Exists(dispatcherFile));

        string programSource = File.ReadAllText(programFile);
        string dispatcherSource = File.ReadAllText(dispatcherFile);

        HashSet<string> listedNames = new();
        foreach (Match match in s_makeToolPattern.Matches(programSource))
            listedNames.Add(match.Groups[1].Value);

        Regex dispatchPattern = new(@"""(codex-[^""]+)""\s*=>");
        HashSet<string> dispatchedNames = new();
        foreach (Match match in dispatchPattern.Matches(dispatcherSource))
            dispatchedNames.Add(match.Groups[1].Value);

        Assert.True(listedNames.Count > 0, "No tool names found in Program.cs");
        Assert.True(dispatchedNames.Count > 0, "No dispatch names found in ToolDispatcher.cs");

        HashSet<string> missingInDispatch = new(listedNames);
        missingInDispatch.ExceptWith(dispatchedNames);

        HashSet<string> missingInList = new(dispatchedNames);
        missingInList.ExceptWith(listedNames);

        Assert.True(missingInDispatch.Count == 0,
            $"Tools listed but not dispatched: {string.Join(", ", missingInDispatch)}");
        Assert.True(missingInList.Count == 0,
            $"Tools dispatched but not listed: {string.Join(", ", missingInList)}");
    }

    static string FindRepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "Codex.sln")))
                return dir;
            dir = Path.GetDirectoryName(dir)!;
        }
        throw new InvalidOperationException("Could not find repo root (Codex.sln)");
    }
}
