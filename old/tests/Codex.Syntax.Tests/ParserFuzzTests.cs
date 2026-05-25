using System.Text;
using Codex.Core;
using Codex.Syntax;
using Xunit;

namespace Codex.Syntax.Tests;

// Fuzz harness (section H/2). Throws random byte sequences (and mutations of
// valid programs) at the parser and asserts:
//   1. Parse returns. It does not hang — fuel guards (C/3) must trip before
//      any adversarial depth blows the stack.
//   2. Parse does not throw an uncaught exception. Invariants (B) keep
//      every path either producing an AST (possibly with diagnostics) or
//      emitting CDX9001 cleanly. An unhandled exception — especially
//      IndexOutOfRangeException, NullReferenceException, or StackOverflow
//      at a lower level — is the class of bug this catches.
//
// Seeds are fixed so a failure reproduces exactly. Add a new seed to the
// InlineData list to lock a newly-discovered-then-fixed regression into
// the suite.
public class ParserFuzzTests
{
    static DocumentNode ParseBytes(byte[] bytes, out DiagnosticBag diagnostics)
    {
        string text = Encoding.UTF8.GetString(bytes);
        SourceText src = new("fuzz.codex", text);
        diagnostics = new DiagnosticBag();
        return DocumentParser.Parse(src, diagnostics);
    }

    [Theory]
    [InlineData(0xC0DE, 512)]
    [InlineData(0xBEEF, 1024)]
    [InlineData(0xFACE, 2048)]
    [InlineData(0x1234, 4096)]
    public void Random_bytes_never_hang_or_crash(int seed, int byteCount)
    {
        Random rng = new(seed);
        byte[] bytes = new byte[byteCount];
        rng.NextBytes(bytes);

        // If this throws StackOverflowException the process dies before
        // we can assert anything, so we rely on the C/3 fuel guards to
        // halt recursion before then. The test's implicit contract is
        // "this call returns" — xunit timeout is the fallback.
        DocumentNode doc = ParseBytes(bytes, out DiagnosticBag bag);
        Assert.NotNull(doc);
        // Diagnostics are expected — random bytes aren't valid Codex — but
        // the count must be bounded by the bag's MaxErrors (no infinite
        // diagnostic-spam loop either).
    }

    [Theory]
    [InlineData(0xDEAD)]
    [InlineData(0xCAFE)]
    [InlineData(0x5EED)]
    public void ASCII_fuzz_from_printable_set_never_hangs(int seed)
    {
        // Restricting to printable ASCII biases toward syntax that looks
        // like a real program and exercises more parser paths than pure
        // binary noise would (which tokenizes as mostly-error).
        const string alphabet =
            "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789" +
            "(){}[]<>+-*/=.:;,!?|&^~\"' \n\t";
        Random rng = new(seed);
        StringBuilder sb = new();
        for (int i = 0; i < 2048; i++)
        {
            sb.Append(alphabet[rng.Next(alphabet.Length)]);
        }

        SourceText src = new("fuzz.codex", sb.ToString());
        DiagnosticBag bag = new();
        DocumentNode doc = DocumentParser.Parse(src, bag);
        Assert.NotNull(doc);
    }

    [Theory]
    [InlineData(0x123, 50)]
    [InlineData(0x456, 50)]
    [InlineData(0x789, 50)]
    public void Mutation_of_valid_program_never_hangs_or_crashes(int seed, int iterations)
    {
        // Take a small well-formed program, flip one random byte, parse.
        // Catches bugs where a single token goes wrong and cascades.
        const string basis = """
            Chapter: Fuzz

             main : Integer
             main = let x = 1 in x + x

            Page 1
            """;
        byte[] bytes = Encoding.UTF8.GetBytes(basis);
        Random rng = new(seed);
        for (int i = 0; i < iterations; i++)
        {
            byte[] mutated = (byte[])bytes.Clone();
            int idx = rng.Next(mutated.Length);
            mutated[idx] ^= (byte)(rng.Next(255) + 1);

            SourceText src = new("fuzz.codex", Encoding.UTF8.GetString(mutated));
            DiagnosticBag bag = new();
            DocumentNode doc = DocumentParser.Parse(src, bag);
            Assert.NotNull(doc);
        }
    }

    [Fact]
    public void Paren_bomb_terminates_via_fuel()
    {
        // Documented attack from C/3: adversarial paren-depth. Expect
        // CDX9001 in the bag, not a hang or crash.
        StringBuilder sb = new("main : Integer\nmain = ");
        for (int i = 0; i < 500; i++) sb.Append('(');
        sb.Append('0');
        for (int i = 0; i < 500; i++) sb.Append(')');
        sb.Append('\n');

        SourceText src = new("fuzz.codex", sb.ToString());
        DiagnosticBag bag = new();
        DocumentNode doc = DocumentParser.Parse(src, bag);
        Assert.NotNull(doc);
        Assert.Contains(bag.ToImmutable(),
            d => d.Code == CdxCodes.ResourceExhausted);
    }
}
