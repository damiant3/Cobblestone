using Codex.Cli;
using Codex.Core;
using Codex.Semantics;
using Xunit;

namespace Codex.Semantics.Tests;

public class FileChapterLoaderCycleTests
{
    [Fact]
    public void Mutually_citing_chapters_trip_CDX9001_with_ordered_chain()
    {
        // Build a temp codex: root/Q1/A.codex cites Q2 chapter B,
        // root/Q2/B.codex cites Q1 chapter A. The top-level Load("Q1","A")
        // spawns a transitive loader that loads B, which in turn tries to
        // load A — the in-flight guard must trip before the recursion blows
        // the stack.
        string root = Path.Combine(Path.GetTempPath(), "cdx-cycle-" + Guid.NewGuid());
        string q1 = Path.Combine(root, "Q1");
        string q2 = Path.Combine(root, "Q2");
        Directory.CreateDirectory(q1);
        Directory.CreateDirectory(q2);
        try
        {
            File.WriteAllText(Path.Combine(q1, "A.codex"),
                "Chapter: A\n\n  cites Q2 chapter B\n\n x : Integer\n x = 42\n\nPage 1\n");
            File.WriteAllText(Path.Combine(q2, "B.codex"),
                "Chapter: B\n\n  cites Q1 chapter A\n\n y : Integer\n y = 42\n\nPage 1\n");

            DiagnosticBag bag = new();
            FileChapterLoader loader = new(root, bag);
            ResolvedChapter? result = loader.Load("Q1", "A");

            Assert.Null(result);
            Diagnostic? fuelDiag = bag.ToImmutable()
                .FirstOrDefault(d => d.Code == CdxCodes.ResourceExhausted);
            Assert.NotNull(fuelDiag);
            Assert.Contains("circular import", fuelDiag.Message);
            // Chain must list keys in insertion order, not HashSet iteration
            // order. For A → B → A the printed chain contains "Q1::A" before
            // "Q2::B" (A was inserted first), and the final "Q1::A" at the
            // end as the re-entry target.
            string msg = fuelDiag.Message;
            int firstA = msg.IndexOf("Q1::A", StringComparison.Ordinal);
            int firstB = msg.IndexOf("Q2::B", StringComparison.Ordinal);
            Assert.True(firstA >= 0 && firstB >= 0);
            Assert.True(firstA < firstB, "insertion-order chain should list A before B");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Non_cyclic_cite_loads_normally()
    {
        // root/Q1/A.codex cites Q2 chapter B (no back-cite). Load succeeds.
        string root = Path.Combine(Path.GetTempPath(), "cdx-nocycle-" + Guid.NewGuid());
        string q1 = Path.Combine(root, "Q1");
        string q2 = Path.Combine(root, "Q2");
        Directory.CreateDirectory(q1);
        Directory.CreateDirectory(q2);
        try
        {
            File.WriteAllText(Path.Combine(q1, "A.codex"),
                "Chapter: A\n\n  cites Q2 chapter B\n\n x : Integer\n x = 42\n\nPage 1\n");
            File.WriteAllText(Path.Combine(q2, "B.codex"),
                "Chapter: B\n\n y : Integer\n y = 42\n\nPage 1\n");

            DiagnosticBag bag = new();
            FileChapterLoader loader = new(root, bag);
            ResolvedChapter? result = loader.Load("Q1", "A");

            Assert.NotNull(result);
            Assert.DoesNotContain(bag.ToImmutable(), d => d.Code == CdxCodes.ResourceExhausted);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
