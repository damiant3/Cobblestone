using Codex.Core;
using Codex.Syntax;
using Codex.Ast;
using Codex.Semantics;
using Xunit;

namespace Codex.Semantics.Tests;

public class ModuleScopeTests
{
    static Module ParseModule(string source, string moduleName)
    {
        SourceText src = new($"{moduleName}.codex", source);
        DiagnosticBag bag = new();
        Lexer lexer = new(src, bag);
        IReadOnlyList<Token> tokens = lexer.TokenizeAll();
        Parser parser = new(tokens, bag);
        DocumentNode doc = parser.ParseDocument();
        Desugarer desugarer = new(bag);
        return desugarer.Desugar(doc, moduleName);
    }

    [Fact]
    public void No_collisions_names_unchanged()
    {
        Module modA = ParseModule("foo (x) = x", "ModA");
        Module modB = ParseModule("bar (x) = x", "ModB");

        DiagnosticBag diags = new();
        ModuleScoper scoper = new(diags);
        Module combined = scoper.Scope([modA, modB], "Combined");

        Assert.Equal(2, combined.Definitions.Count);
        Assert.Contains(combined.Definitions, d => d.Name.Value == "foo");
        Assert.Contains(combined.Definitions, d => d.Name.Value == "bar");
    }

    [Fact]
    public void Colliding_names_are_mangled()
    {
        Module modA = ParseModule("emit-expr (x) = x", "ModA");
        Module modB = ParseModule("emit-expr (x) = x", "ModB");

        DiagnosticBag diags = new();
        ModuleScoper scoper = new(diags);
        Module combined = scoper.Scope([modA, modB], "Combined");

        Assert.Equal(2, combined.Definitions.Count);
        Assert.Contains(combined.Definitions, d => d.Name.Value == "mod-a_emit-expr");
        Assert.Contains(combined.Definitions, d => d.Name.Value == "mod-b_emit-expr");
    }

    [Fact]
    public void Internal_calls_mangled_consistently()
    {
        // ModA defines emit-expr and calls it from helper
        Module modA = ParseModule(
            "emit-expr (x) = x\nhelper (x) = emit-expr x",
            "ModA");
        Module modB = ParseModule("emit-expr (x) = x", "ModB");

        DiagnosticBag diags = new();
        ModuleScoper scoper = new(diags);
        Module combined = scoper.Scope([modA, modB], "Combined");

        // helper's body should call mod-a_emit-expr
        Definition helper = combined.Definitions.First(d => d.Name.Value == "helper");
        // The body is an apply: (emit-expr x) -> after rename, (mod-a_emit-expr x)
        ApplyExpr app = Assert.IsType<ApplyExpr>(helper.Body);
        NameExpr callee = Assert.IsType<NameExpr>(app.Function);
        Assert.Equal("mod-a_emit-expr", callee.Name.Value);
    }

    [Fact]
    public void Selective_import_maps_name()
    {
        // ModA and ModC both define emit-expr (collision).
        // ModB imports emit-expr from ModA selectively.
        Module modA = ParseModule("emit-expr (x) = x", "ModA");
        Module modB = ParseModule(
            "import ModA (emit-expr)\nhelper (x) = emit-expr x",
            "ModB");
        Module modC = ParseModule("emit-expr (x) = x", "ModC");

        DiagnosticBag diags = new();
        ModuleScoper scoper = new(diags);
        Module combined = scoper.Scope([modA, modB, modC], "Combined");

        // helper's body should reference mod-a_emit-expr via the import alias
        Definition helper = combined.Definitions.First(d => d.Name.Value == "helper");
        ApplyExpr app = Assert.IsType<ApplyExpr>(helper.Body);
        NameExpr callee = Assert.IsType<NameExpr>(app.Function);
        Assert.Equal("mod-a_emit-expr", callee.Name.Value);
    }

    [Fact]
    public void Non_colliding_names_from_other_module_unchanged()
    {
        // ModA has unique-fn, ModB calls it — should NOT be mangled
        Module modA = ParseModule("unique-fn (x) = x", "ModA");
        Module modB = ParseModule("caller (x) = unique-fn x", "ModB");

        DiagnosticBag diags = new();
        ModuleScoper scoper = new(diags);
        Module combined = scoper.Scope([modA, modB], "Combined");

        Definition caller = combined.Definitions.First(d => d.Name.Value == "caller");
        ApplyExpr app = Assert.IsType<ApplyExpr>(caller.Body);
        NameExpr callee = Assert.IsType<NameExpr>(app.Function);
        Assert.Equal("unique-fn", callee.Name.Value);
    }

    [Fact]
    public void Slug_generation()
    {
        // Test the slug generation by using PascalCase module names
        Module modA = ParseModule("emit (x) = x", "CSharpEmitter");
        Module modB = ParseModule("emit (x) = x", "CodexEmitter");

        DiagnosticBag diags = new();
        ModuleScoper scoper = new(diags);
        Module combined = scoper.Scope([modA, modB], "Combined");

        Assert.Contains(combined.Definitions, d => d.Name.Value == "csharp-emitter_emit");
        Assert.Contains(combined.Definitions, d => d.Name.Value == "codex-emitter_emit");
    }

    [Fact]
    public void Three_modules_only_colliding_pair_mangled()
    {
        Module modA = ParseModule("emit (x) = x\nfoo (x) = x", "ModA");
        Module modB = ParseModule("emit (x) = x\nbar (x) = x", "ModB");
        Module modC = ParseModule("baz (x) = x", "ModC");

        DiagnosticBag diags = new();
        ModuleScoper scoper = new(diags);
        Module combined = scoper.Scope([modA, modB, modC], "Combined");

        // emit is mangled in both A and B
        Assert.Contains(combined.Definitions, d => d.Name.Value == "mod-a_emit");
        Assert.Contains(combined.Definitions, d => d.Name.Value == "mod-b_emit");
        // foo, bar, baz are unchanged
        Assert.Contains(combined.Definitions, d => d.Name.Value == "foo");
        Assert.Contains(combined.Definitions, d => d.Name.Value == "bar");
        Assert.Contains(combined.Definitions, d => d.Name.Value == "baz");
    }
}
