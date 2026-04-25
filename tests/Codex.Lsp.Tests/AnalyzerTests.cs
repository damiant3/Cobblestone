using Codex.Ast;
using Codex.Types;
using Xunit;

namespace Codex.Lsp.Tests;

public class AnalyzerTests
{
    [Fact]
    public void Simple_definition_has_no_errors()
    {
        AnalysisResult result = Analyzer.Analyze("test.codex", "x = 42");
        Assert.Empty(result.Diagnostics.Where(d => d.IsError));
        Assert.True(result.Types.ContainsKey("x"));
        Assert.IsType<IntegerType>(result.Types["x"]);
    }

    [Fact]
    public void Function_definition_infers_type()
    {
        string source = "square : Integer -> Integer\nsquare (x) = x * x";
        AnalysisResult result = Analyzer.Analyze("test.codex", source);
        Assert.Empty(result.Diagnostics.Where(d => d.IsError));
        Assert.IsType<FunctionType>(result.Types["square"]);
    }

    [Fact]
    public void Undefined_name_produces_error()
    {
        AnalysisResult result = Analyzer.Analyze("test.codex", "x = unknownThing");
        Assert.Contains(result.Diagnostics, d => d.IsError);
    }

    [Fact]
    public void Type_mismatch_produces_error()
    {
        string source = "f : Integer -> Integer\nf (x) = \"hello\"";
        AnalysisResult result = Analyzer.Analyze("test.codex", source);
        Assert.Contains(result.Diagnostics, d => d.IsError);
    }

    [Fact]
    public void Definitions_are_populated()
    {
        string source = "a = 1\nb = 2\nc = 3";
        AnalysisResult result = Analyzer.Analyze("test.codex", source);
        Assert.Equal(3, result.Definitions.Count);
        Assert.Equal("a", result.Definitions[0].Name.Value);
        Assert.Equal("b", result.Definitions[1].Name.Value);
        Assert.Equal("c", result.Definitions[2].Name.Value);
    }

    [Fact]
    public void Tokens_are_populated_for_notation_mode()
    {
        AnalysisResult result = Analyzer.Analyze("test.codex", "x = 42");
        Assert.NotEmpty(result.Tokens);
    }

    [Fact]
    public void Prose_document_analyzes_without_crash()
    {
        string source = "Chapter: Test\n\n  Some prose.\n\n    greet : Text -> Text\n    greet (name) = name\n";
        AnalysisResult result = Analyzer.Analyze("test.codex", source);
        Assert.NotNull(result);
    }

    [Fact]
    public void Multiple_definitions_all_typed()
    {
        string source = "a = 1\nb = a + 2";
        AnalysisResult result = Analyzer.Analyze("test.codex", source);
        Assert.Empty(result.Diagnostics.Where(d => d.IsError));
        Assert.IsType<IntegerType>(result.Types["a"]);
        Assert.IsType<IntegerType>(result.Types["b"]);
    }
}

public class LspHelpersTests
{
    [Fact]
    public void GetWordAt_middle_of_identifier()
    {
        string text = "square (x) = x * x";
        string? word = LspHelpers.GetWordAt(text, 0, 3);
        Assert.Equal("square", word);
    }

    [Fact]
    public void GetWordAt_start_of_identifier()
    {
        string text = "square (x) = x * x";
        string? word = LspHelpers.GetWordAt(text, 0, 0);
        Assert.Equal("square", word);
    }

    [Fact]
    public void GetWordAt_on_space_returns_null()
    {
        string text = "square (x) = x * x";
        string? word = LspHelpers.GetWordAt(text, 0, 6);
        Assert.Null(word);
    }

    [Fact]
    public void GetWordAt_hyphenated_name()
    {
        string text = "print-line \"hello\"";
        string? word = LspHelpers.GetWordAt(text, 0, 5);
        Assert.Equal("print-line", word);
    }

    [Fact]
    public void GetWordAt_second_line()
    {
        string text = "a = 1\nb = 2";
        string? word = LspHelpers.GetWordAt(text, 1, 0);
        Assert.Equal("b", word);
    }

    [Fact]
    public void GetWordAt_out_of_bounds_returns_null()
    {
        string text = "x = 1";
        Assert.Null(LspHelpers.GetWordAt(text, 5, 0));
        Assert.Null(LspHelpers.GetWordAt(text, 0, 100));
    }
}

public class DefinitionLookupTests
{
    [Fact]
    public void Definition_finds_function_by_name()
    {
        string source = "square : Integer -> Integer\nsquare (x) = x * x";
        AnalysisResult result = Analyzer.Analyze("test.codex", source);
        Assert.Single(result.Definitions);
        Assert.Equal("square", result.Definitions[0].Name.Value);
    }

    [Fact]
    public void Definition_finds_variant_type()
    {
        string source = "Shape = Circle (Integer) | Rectangle (Integer) (Integer)\n\nopening = 1";
        AnalysisResult result = Analyzer.Analyze("test.codex", source);
        Assert.NotEmpty(result.TypeDefinitions);
        Assert.Equal("Shape", result.TypeDefinitions[0].Name.Value);
    }

    [Fact]
    public void Definition_finds_variant_constructors()
    {
        string source = "Shape = Circle (Integer) | Rectangle (Integer) (Integer)\nopening = 1";
        AnalysisResult result = Analyzer.Analyze("test.codex", source);
        VariantTypeDef variant = Assert.IsType<VariantTypeDef>(result.TypeDefinitions[0]);
        Assert.Equal(2, variant.Constructors.Count);
        Assert.Equal("Circle", variant.Constructors[0].Name.Value);
        Assert.Equal("Rectangle", variant.Constructors[1].Name.Value);
    }

    [Fact]
    public void Definition_finds_record_type()
    {
        string source = "Person = record { name : Text, age : Integer }\nopening = 1";
        AnalysisResult result = Analyzer.Analyze("test.codex", source);
        Assert.Single(result.TypeDefinitions);
        Assert.Equal("Person", result.TypeDefinitions[0].Name.Value);
    }
}
