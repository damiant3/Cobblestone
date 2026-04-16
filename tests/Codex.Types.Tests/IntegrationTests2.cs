using Codex.Core;
using Codex.Syntax;
using Codex.Ast;
using Codex.Semantics;
using Xunit;

namespace Codex.Types.Tests;

public partial class IntegrationTests
{
    // --- Effectful programs ---

    [Fact]
    public void Effectful_function_type_checks()
    {
        string source =
            "main : [Console] Nothing\n" +
            "main = act\n" +
            "  print-line \"hello\"\n";
        Map<string, CodexType>? types = Helpers.TypeCheck(source);
        Assert.NotNull(types);
        Assert.True(types!.ContainsKey("main"));
        Assert.Contains("Console", types["main"]!.ToString());
    }

    [Fact]
    public void Effectful_function_compiles_to_csharp()
    {
        string source =
            "main : [Console] Nothing\n" +
            "main = act\n" +
            "  print-line \"hello\"\n";
        string? cs = Helpers.CompileToCS(source, "eftest");
        Assert.NotNull(cs);
        Assert.Contains("Console.WriteLine", cs!);
        Assert.DoesNotContain("Console.WriteLine(Codex_eftest.main())", cs);
    }

    [Fact]
    public void Do_bind_type_checks()
    {
        string source =
            "main : [Console] Nothing\n" +
            "main = act\n" +
            "  name <- read-line\n" +
            "  print-line name\n";
        Map<string, CodexType>? types = Helpers.TypeCheck(source);
        Assert.NotNull(types);
        Assert.True(types!.ContainsKey("main"));
    }

    [Fact]
    public void Do_bind_compiles_to_csharp()
    {
        string source =
            "main : [Console] Nothing\n" +
            "main = act\n" +
            "  name <- read-line\n" +
            "  print-line (\"Hello, \" ++ name)\n";
        string? cs = Helpers.CompileToCS(source, "dobind");
        Assert.NotNull(cs);
        Assert.Contains("Console.ReadLine()", cs!);
        Assert.Contains("Console.WriteLine", cs);
    }

    [Fact]
    public void Effectful_helper_function_compiles()
    {
        string source =
            "greet : Text -> [Console] Nothing\n" +
            "greet (name) = print-line (\"Hello, \" ++ name)\n\n" +
            "main : [Console] Nothing\n" +
            "main = act\n" +
            "  greet \"World\"\n";
        string? cs = Helpers.CompileToCS(source, "efhelper");
        Assert.NotNull(cs);
        Assert.Contains("greet", cs!);
        Assert.Contains("Console.WriteLine", cs);
    }

    [Fact]
    public void Pure_function_calling_effectful_produces_error()
    {
        string source =
            "bad : Nothing\n" +
            "bad = print-line \"oops\"\n";
        DiagnosticBag diag = Helpers.TypeCheckWithDiagnostics(source);
        Assert.Contains(diag.ToImmutable(), d => d.Code == CdxCodes.EffectNotDeclared);
    }

    [Fact]
    public void Effectful_function_calling_effectful_is_allowed()
    {
        string source =
            "say-hello : [Console] Nothing\n" +
            "say-hello = print-line \"hello\"\n";
        DiagnosticBag diag = Helpers.TypeCheckWithDiagnostics(source);
        Assert.DoesNotContain(diag.ToImmutable(), d => d.Code == CdxCodes.EffectNotDeclared);
    }

    [Fact]
    public void Multiple_do_statements_compile()
    {
        string source =
            "main : [Console] Nothing\n" +
            "main = act\n" +
            "  print-line \"one\"\n" +
            "  print-line \"two\"\n" +
            "  print-line \"three\"\n";
        string? cs = Helpers.CompileToCS(source, "multi");
        Assert.NotNull(cs);
        int count = 0;
        int idx = 0;
        while ((idx = cs!.IndexOf("Console.WriteLine", idx)) >= 0)
        {
            count++;
            idx++;
        }
        Assert.Equal(3, count);
    }

    // --- M2 gap fixes ---

    [Fact]
    public void Nested_ctor_pattern_compiles()
    {
        string source =
            "Maybe (a) =\n" +
            "  | Just (a)\n" +
            "  | None\n\n" +
            "unwrap-nested : Maybe (Maybe Integer) -> Integer\n" +
            "unwrap-nested (m) =\n" +
            "  when m\n" +
            "    is Just (Just (n)) -> n\n" +
            "    is otherwise -> 0\n";
        string? cs = Helpers.CompileToCS(source, "nested");
        Assert.NotNull(cs);
        Assert.Contains("Just", cs!);
    }

    [Fact]
    public void Type_param_arity_too_many_args_produces_error()
    {
        string source =
            "Maybe (a) =\n" +
            "  | Just (a)\n" +
            "  | None\n\n" +
            "bad : Maybe (Integer) (Text) -> Integer\n" +
            "bad (x) = 0\n";
        DiagnosticBag diag = Helpers.TypeCheckWithDiagnostics(source);
        Assert.Contains(diag.ToImmutable(), d => d.Code == CdxCodes.TypeArgArityMismatch);
    }

    [Fact]
    public void Type_param_arity_correct_no_error()
    {
        string source =
            "Maybe (a) =\n" +
            "  | Just (a)\n" +
            "  | None\n\n" +
            "wrap : Integer -> Maybe Integer\n" +
            "wrap (x) = Just x\n";
        DiagnosticBag diag = Helpers.TypeCheckWithDiagnostics(source);
        Assert.DoesNotContain(diag.ToImmutable(), d => d.Code == CdxCodes.TypeArgArityMismatch);
    }

    [Fact]
    public void Show_as_first_class_value_compiles()
    {
        string source =
            "apply-to : (Integer -> Text) -> Integer -> Text\n" +
            "apply-to (f) (x) = f x\n\n" +
            "main : Text\n" +
            "main = apply-to show 42\n";
        string? cs = Helpers.CompileToCS(source, "showval");
        Assert.NotNull(cs);
        Assert.Contains("Convert.ToString", cs!);
    }

    // --- Linear types (Milestone 6) ---

    [Fact]
    public void Linear_type_parses()
    {
        string source =
            "consume : linear FileHandle -> [FileSystem] Nothing\n" +
            "consume (h) = close-file h\n";
        Map<string, CodexType>? types = Helpers.TypeCheck(source);
        Assert.NotNull(types);
        Assert.True(types!.ContainsKey("consume"));
        CodexType consumeType = types["consume"]!;
        Assert.IsType<FunctionType>(consumeType);
        FunctionType ft = (FunctionType)consumeType;
        Assert.IsType<LinearType>(ft.Parameter);
    }

    [Fact]
    public void Linear_variable_used_once_is_ok()
    {
        string source =
            "consume : linear FileHandle -> [FileSystem] Nothing\n" +
            "consume (h) = close-file h\n";
        DiagnosticBag diag = Helpers.CheckWithLinearity(source);
        Assert.DoesNotContain(diag.ToImmutable(), d =>
            d.Code == CdxCodes.LinearUnused || d.Code == CdxCodes.LinearUsedTwice);
    }

    [Fact]
    public void Linear_variable_used_twice_produces_error()
    {
        string source =
            "use-twice : linear FileHandle -> [FileSystem] Nothing\n" +
            "use-twice (h) = act\n" +
            "  close-file h\n" +
            "  close-file h\n";
        DiagnosticBag diag = Helpers.CheckWithLinearity(source);
        Assert.Contains(diag.ToImmutable(), d => d.Code == CdxCodes.LinearUsedTwice);
    }

    [Fact]
    public void Linear_variable_unused_produces_error()
    {
        string source =
            "leak : linear FileHandle -> Integer\n" +
            "leak (h) = 42\n";
        DiagnosticBag diag = Helpers.CheckWithLinearity(source);
        Assert.Contains(diag.ToImmutable(), d => d.Code == CdxCodes.LinearUnused);
    }

    [Fact]
    public void Linear_file_handle_round_trip_type_checks()
    {
        string source =
            "open-and-close : Text -> [FileSystem] Nothing\n" +
            "open-and-close (path) = act\n" +
            "  handle <- open-file path\n" +
            "  close-file handle\n";
        Map<string, CodexType>? types = Helpers.TypeCheck(source);
        Assert.NotNull(types);
    }

    [Fact]
    public void Linear_file_handle_compiles_to_csharp()
    {
        string source =
            "consume : linear FileHandle -> [FileSystem] Nothing\n" +
            "consume (h) = close-file h\n";
        string? cs = Helpers.CompileToCS(source, "linfile");
        Assert.NotNull(cs);
        Assert.Contains(".Dispose()", cs!);
    }

    [Fact]
    public void Open_file_compiles_to_csharp()
    {
        string source =
            "open-and-close : Text -> [FileSystem] Nothing\n" +
            "open-and-close (path) = act\n" +
            "  h <- open-file path\n" +
            "  close-file h\n";
        string? cs = Helpers.CompileToCS(source, "openclose");
        Assert.NotNull(cs);
        Assert.Contains("File.OpenRead", cs!);
        Assert.Contains(".Dispose()", cs!);
    }

    [Fact]
    public void Linear_type_in_format_type()
    {
        string source =
            "consume : linear FileHandle -> [FileSystem] Nothing\n" +
            "consume (h) = close-file h\n";
        DiagnosticBag diag = Helpers.CheckWithLinearity(source);
        Assert.DoesNotContain(diag.ToImmutable(), d => d.Severity == DiagnosticSeverity.Error);
    }

    // --- Dependent types (Milestone 8) ---

    [Fact]
    public void Dependent_function_type_checks()
    {
        string source =
            "identity : (n : Integer) -> Integer\n" +
            "identity (x) = x\n";
        Map<string, CodexType>? types = Helpers.TypeCheck(source);
        Assert.NotNull(types);
        Assert.IsType<DependentFunctionType>(types!["identity"]);
    }

    [Fact]
    public void Dependent_type_application_substitutes_value()
    {
        string source =
            "f : (n : Integer) -> Integer\n" +
            "f (x) = x\n\n" +
            "main : Integer\n" +
            "main = f 42\n";
        Map<string, CodexType>? types = Helpers.TypeCheck(source);
        Assert.NotNull(types);
        Assert.IsType<IntegerType>(types!["main"]);
    }

    [Fact]
    public void Dependent_type_with_vector_type_checks()
    {
        string source =
            "length : (n : Integer) -> Vector n Integer -> Integer\n" +
            "length (n) (v) = n\n";
        Map<string, CodexType>? types = Helpers.TypeCheck(source);
        Assert.NotNull(types);
        Assert.IsType<DependentFunctionType>(types!["length"]);
    }

    [Fact]
    public void Nested_dependent_types_type_check()
    {
        string source =
            "add-lengths : (m : Integer) -> (n : Integer) -> Integer\n" +
            "add-lengths (a) (b) = a + b\n";
        Map<string, CodexType>? types = Helpers.TypeCheck(source);
        Assert.NotNull(types);
        DependentFunctionType outer = Assert.IsType<DependentFunctionType>(types!["add-lengths"]);
        Assert.IsType<DependentFunctionType>(outer.Body);
    }

    [Fact]
    public void Dependent_type_compiles_to_csharp()
    {
        string source =
            "f : (n : Integer) -> Integer\n" +
            "f (x) = x\n\n" +
            "main : Integer\n" +
            "main = f 5\n";
        string? cs = Helpers.CompileToCS(source, "deptest");
        Assert.NotNull(cs);
        Assert.Contains("f", cs!);
    }

    [Fact]
    public void Type_level_arithmetic_verified_at_compile_time()
    {
        string source =
            "f : (m : Integer) -> (n : Integer) -> Vector (m + n) Integer -> Integer\n" +
            "f (a) (b) (v) = a + b\n\n" +
            "main : Integer\n" +
            "main = f 3 2 [1, 2, 3, 4, 5]\n";
        Map<string, CodexType>? types = Helpers.TypeCheck(source);
        Assert.NotNull(types);
    }

    [Fact]
    public void Proof_obligation_discharged_for_literal_evidence()
    {
        string source =
            "safe-index : (i : Integer) -> (n : Integer) -> {proof : i < n} -> Integer\n" +
            "safe-index (i) (n) = i\n\n" +
            "main : Integer\n" +
            "main = safe-index 3 5\n";
        DiagnosticBag diag = Helpers.TypeCheckWithDiagnostics(source);
        Assert.DoesNotContain(diag.ToImmutable(), d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Proof_obligation_fails_for_invalid_literal_evidence()
    {
        string source =
            "safe-index : (i : Integer) -> (n : Integer) -> {proof : i < n} -> Integer\n" +
            "safe-index (i) (n) = i\n\n" +
            "main : Integer\n" +
            "main = safe-index 5 3\n";
        DiagnosticBag diag = Helpers.TypeCheckWithDiagnostics(source);
        Assert.Contains(diag.ToImmutable(), d => d.Code == CdxCodes.LinearUnused);
    }

}
