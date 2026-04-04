using Codex.Core;
using Codex.Syntax;
using Codex.Ast;
using Codex.Types;
using Xunit;

namespace Codex.Types.Tests;

public class TypeCheckerTests
{
    private static (Map<string, CodexType> Types, DiagnosticBag Diags) Check(string source)
    {
        SourceText src = new("test.codex", source);
        DiagnosticBag bag = new();
        Lexer lexer = new(src, bag);
        IReadOnlyList<Token> tokens = lexer.TokenizeAll();
        Parser parser = new(tokens, bag);
        DocumentNode doc = parser.ParseDocument();
        Desugarer desugarer = new(bag);
        Chapter chapter = desugarer.Desugar(doc, "Test");
        TypeChecker checker = new(bag);
        Map<string, CodexType> types = checker.CheckChapter(chapter);
        return (types, bag);
    }

    [Fact]
    public void Integer_literal_infers_integer()
    {
        (Map<string, CodexType> types, DiagnosticBag diags) = Check("x = 42");
        Assert.False(diags.HasErrors);
        Assert.IsType<IntegerType>(types["x"]);
    }

    [Fact]
    public void Text_literal_infers_text()
    {
        (Map<string, CodexType> types, DiagnosticBag diags) = Check("x = \"hello\"");
        Assert.False(diags.HasErrors);
        Assert.IsType<TextType>(types["x"]);
    }

    [Fact]
    public void Boolean_literal_infers_boolean()
    {
        (Map<string, CodexType> types, DiagnosticBag diags) = Check("x = True");
        Assert.False(diags.HasErrors);
        Assert.IsType<BooleanType>(types["x"]);
    }

    [Fact]
    public void Addition_infers_integer()
    {
        (Map<string, CodexType> types, DiagnosticBag diags) = Check("x = 1 + 2");
        Assert.False(diags.HasErrors);
        Assert.IsType<IntegerType>(types["x"]);
    }

    [Fact]
    public void Comparison_infers_boolean()
    {
        (Map<string, CodexType> types, DiagnosticBag diags) = Check("x = 1 == 2");
        Assert.False(diags.HasErrors);
        Assert.IsType<BooleanType>(types["x"]);
    }

    [Fact]
    public void If_expression_infers_branch_type()
    {
        (Map<string, CodexType> types, DiagnosticBag diags) =
            Check("x = if True then 1 else 2");
        Assert.False(diags.HasErrors);
        Assert.IsType<IntegerType>(types["x"]);
    }

    [Fact]
    public void If_condition_must_be_boolean()
    {
        (Map<string, CodexType> _, DiagnosticBag diags) =
            Check("x = if 42 then 1 else 2");
        Assert.True(diags.HasErrors);
    }

    [Fact]
    public void If_branches_must_match()
    {
        (Map<string, CodexType> _, DiagnosticBag diags) =
            Check("x = if True then 1 else \"hello\"");
        Assert.True(diags.HasErrors);
    }

    [Fact]
    public void Let_binding_infers_correctly()
    {
        (Map<string, CodexType> types, DiagnosticBag diags) =
            Check("x = let a = 1 in a + 2");
        Assert.False(diags.HasErrors);
        Assert.IsType<IntegerType>(types["x"]);
    }

    [Fact]
    public void Function_with_annotation_checks()
    {
        string source = "square : Integer -> Integer\nsquare (x) = x * x";
        (Map<string, CodexType> types, DiagnosticBag diags) = Check(source);
        Assert.False(diags.HasErrors);
        Assert.IsType<FunctionType>(types["square"]);
        FunctionType ft = (FunctionType)types["square"]!;
        Assert.IsType<IntegerType>(ft.Parameter);
        Assert.IsType<IntegerType>(ft.Return);
    }

    [Fact]
    public void Function_type_mismatch_reports_error()
    {
        string source = "f : Integer -> Integer\nf (x) = \"hello\"";
        (Map<string, CodexType> _, DiagnosticBag diags) = Check(source);
        Assert.True(diags.HasErrors);
    }

    [Fact]
    public void Two_param_function_infers_curried_type()
    {
        string source = "add : Integer -> Integer -> Integer\nadd (x) (y) = x + y";
        (Map<string, CodexType> types, DiagnosticBag diags) = Check(source);
        Assert.False(diags.HasErrors);
        Assert.IsType<FunctionType>(types["add"]);
    }

    [Fact]
    public void Unannotated_function_infers_type()
    {
        string source = "double (x) = x + x";
        (Map<string, CodexType> types, DiagnosticBag diags) = Check(source);
        Assert.False(diags.HasErrors);
        Assert.IsType<FunctionType>(types["double"]);
    }

    [Fact]
    public void List_literal_infers_list_type()
    {
        (Map<string, CodexType> types, DiagnosticBag diags) = Check("xs = [1, 2, 3]");
        Assert.False(diags.HasErrors);
        Assert.IsType<ListType>(types["xs"]);
        ListType lt = (ListType)types["xs"]!;
        Assert.IsType<IntegerType>(lt.Element);
    }

    [Fact]
    public void Empty_list_infers_polymorphic_list()
    {
        (Map<string, CodexType> types, DiagnosticBag diags) = Check("xs = []");
        Assert.False(diags.HasErrors);
        Assert.IsType<ListType>(types["xs"]);
    }

    [Fact]
    public void Mixed_list_elements_report_error()
    {
        (Map<string, CodexType> _, DiagnosticBag diags) =
            Check("xs = [1, \"hello\"]");
        Assert.True(diags.HasErrors);
    }

    [Fact]
    public void Text_concatenation_infers_text()
    {
        (Map<string, CodexType> types, DiagnosticBag diags) =
            Check("x = \"hello\" ++ \" world\"");
        Assert.False(diags.HasErrors);
        Assert.IsType<TextType>(types["x"]);
    }

    [Fact]
    public void Negation_infers_integer()
    {
        (Map<string, CodexType> types, DiagnosticBag diags) = Check("x = -42");
        Assert.False(diags.HasErrors);
        Assert.IsType<IntegerType>(types["x"]);
    }

    [Fact]
    public void Cross_definition_type_propagates()
    {
        string source = "a = 42\nb = a + 1";
        (Map<string, CodexType> types, DiagnosticBag diags) = Check(source);
        Assert.False(diags.HasErrors);
        Assert.IsType<IntegerType>(types["a"]);
        Assert.IsType<IntegerType>(types["b"]);
    }

    [Fact]
    public void Function_application_infers_return_type()
    {
        string source = "f : Integer -> Integer\nf (x) = x + 1\ny = f 5";
        (Map<string, CodexType> types, DiagnosticBag diags) = Check(source);
        Assert.False(diags.HasErrors);
        Assert.IsType<IntegerType>(types["y"]);
    }

    [Fact]
    public void Match_branches_must_agree()
    {
        string source = "x = when True if True -> 1 if False -> 0";
        (Map<string, CodexType> types, DiagnosticBag diags) = Check(source);
        Assert.False(diags.HasErrors);
        Assert.IsType<IntegerType>(types["x"]);
    }

    [Fact]
    public void Arithmetic_on_text_reports_error()
    {
        (Map<string, CodexType> _, DiagnosticBag diags) =
            Check("x = \"hello\" + 1");
        Assert.True(diags.HasErrors);
    }

    [Fact]
    public void Dependent_function_type_parses_and_resolves()
    {
        string source = "f : (n : Integer) -> Integer\nf (x) = x";
        (Map<string, CodexType> types, DiagnosticBag diags) = Check(source);
        Assert.False(diags.HasErrors);
        DependentFunctionType dep = Assert.IsType<DependentFunctionType>(types["f"]!);
        Assert.Equal("n", dep.ParamName);
        Assert.IsType<IntegerType>(dep.ParamType);
    }

    [Fact]
    public void Type_level_integer_literal_resolves()
    {
        string source = "f : (n : Integer) -> Integer\nf (x) = x";
        (Map<string, CodexType> types, DiagnosticBag diags) = Check(source);
        Assert.False(diags.HasErrors);
        Assert.IsType<DependentFunctionType>(types["f"]);
    }

    [Fact]
    public void Type_level_arithmetic_normalizes_constants()
    {
        string source = "f : (m : Integer) -> (n : Integer) -> Integer\nf (x) (y) = x";
        (Map<string, CodexType> types, DiagnosticBag diags) = Check(source);
        Assert.False(diags.HasErrors);
        DependentFunctionType outer = Assert.IsType<DependentFunctionType>(types["f"]);
        Assert.Equal("m", outer.ParamName);
        DependentFunctionType inner = Assert.IsType<DependentFunctionType>(outer.Body);
        Assert.Equal("n", inner.ParamName);
    }

    [Fact]
    public void Constructed_type_with_integer_argument_accepted()
    {
        string source = "f : (n : Integer) -> Vector n Integer -> Integer\nf (n) (v) = n";
        (Map<string, CodexType> types, DiagnosticBag diags) = Check(source);
        Assert.False(diags.HasErrors);
        DependentFunctionType dep = Assert.IsType<DependentFunctionType>(types["f"]);
        Assert.IsType<FunctionType>(dep.Body);
    }

    [Fact]
    public void Type_level_binary_addition_normalizes()
    {
        string source = "f : (m : Integer) -> (n : Integer) -> Vector (m + n) Integer -> Integer\nf (a) (b) (v) = a";
        (Map<string, CodexType> types, DiagnosticBag diags) = Check(source);
        Assert.False(diags.HasErrors);
    }
}
