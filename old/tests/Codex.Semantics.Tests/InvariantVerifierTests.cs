using Codex.Core;
using Codex.Syntax;
using Codex.Ast;
using Codex.Semantics;
using Xunit;

namespace Codex.Semantics.Tests;

public class InvariantVerifierTests
{
    static ResolvedChapter Resolve(string source)
    {
        SourceText src = new("test.codex", source);
        DiagnosticBag bag = new();
        Lexer lexer = new(src, bag);
        IReadOnlyList<Token> tokens = lexer.TokenizeAll();
        Parser parser = new(tokens, bag);
        DocumentNode doc = parser.ParseDocument();
        Desugarer desugarer = new(bag);
        Chapter chapter = desugarer.Desugar(doc, "Test");
        NameResolver resolver = new(bag);
        return resolver.Resolve(chapter);
    }

    [Fact]
    public void AfterResolution_passes_for_well_formed_program()
    {
        ResolvedChapter resolved = Resolve("f (x) = x + 1\ng = f 41");
        InvariantVerifier.AfterResolution(resolved);
    }

    [Fact]
    public void AfterResolution_passes_with_let_and_nested_bindings()
    {
        string source = """
            add (a) (b) = a + b
            f = let y = 10 in let z = 5 in add y z
            """;
        ResolvedChapter resolved = Resolve(source);
        InvariantVerifier.AfterResolution(resolved);
    }

    [Fact]
    public void AfterResolution_throws_when_body_references_unbound_name()
    {
        // Synthetic ResolvedChapter: 'opening' body references 'gone' which is
        // not a parameter, let-binding, top-level, ctor, builtin, or type.
        // This models a compiler bug where the resolver produced output that
        // contains an unresolvable name.
        SourceSpan span = new(new SourcePosition(0, 1, 1), new SourcePosition(0, 1, 1), "synthetic.codex");
        Definition mainDef = new(
            Name: new Name("opening"),
            Parameters: [],
            DeclaredType: null,
            Body: new NameExpr(new Name("gone"), span),
            Span: span);
        Chapter chapter = new(
            QualifiedName.Simple("Synthetic"),
            Definitions: [mainDef],
            TypeDefinitions: [],
            Claims: [],
            Proofs: [],
            Span: span);
        ResolvedChapter resolved = new(
            chapter,
            TopLevelNames: Set<string>.Of("opening"),
            TypeNames: Set<string>.s_empty,
            ConstructorNames: Set<string>.s_empty);

        InvariantViolationException ex = Assert.Throws<InvariantViolationException>(
            () => InvariantVerifier.AfterResolution(resolved));
        Assert.Equal("name-resolution", ex.Phase);
        Assert.Contains("gone", ex.Detail);
        Assert.Contains("opening", ex.Detail);
    }
}
