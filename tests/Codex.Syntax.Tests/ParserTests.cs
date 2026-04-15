using Codex.Core;
using Codex.Syntax;
using Xunit;

namespace Codex.Syntax.Tests;

public class ParserTests
{
    private static DocumentNode Parse(string source)
    {
        SourceText src = new("test.codex", source);
        DiagnosticBag bag = new();
        Lexer lexer = new(src, bag);
        IReadOnlyList<Token> tokens = lexer.TokenizeAll();
        Parser parser = new(tokens, bag);
        return parser.ParseDocument();
    }

    private static (DocumentNode Doc, DiagnosticBag Diags) ParseWithDiags(string source)
    {
        SourceText src = new("test.codex", source);
        DiagnosticBag bag = new();
        Lexer lexer = new(src, bag);
        IReadOnlyList<Token> tokens = lexer.TokenizeAll();
        Parser parser = new(tokens, bag);
        return (parser.ParseDocument(), bag);
    }

    [Fact]
    public void Parse_simple_definition()
    {
        DocumentNode doc = Parse("x = 42");
        Assert.Single(doc.Definitions);
        Assert.Equal("x", doc.Definitions[0].Name.Text);
        Assert.IsType<LiteralExpressionNode>(doc.Definitions[0].Body);
    }

    [Fact]
    public void Parse_definition_with_type_annotation()
    {
        string source = "square : Integer -> Integer\nsquare (x) = x * x";
        DocumentNode doc = Parse(source);
        Assert.Single(doc.Definitions);
        Assert.NotNull(doc.Definitions[0].TypeAnnotation);
        Assert.Equal("square", doc.Definitions[0].Name.Text);
    }

    [Fact]
    public void Parse_when_arm_with_is()
    {
        DocumentNode doc = Parse("f (k) = when k\n is True -> 1\n is False -> 0\n");
        ExpressionNode body = doc.Definitions[0].Body;
        Assert.IsType<MatchExpressionNode>(body);
        MatchExpressionNode match = (MatchExpressionNode)body;
        Assert.Equal(2, match.Branches.Count);
    }

    [Fact]
    public void Parse_otherwise_wildcard()
    {
        DocumentNode doc = Parse("f (k) = when k\n is True -> 1\n is otherwise -> 0\n");
        MatchExpressionNode match = (MatchExpressionNode)doc.Definitions[0].Body;
        Assert.IsType<WildcardPatternNode>(match.Branches[1].Pattern);
    }

    [Fact]
    public void Parse_when_with_if_emits_diagnostic()
    {
        var (_, diags) = ParseWithDiags("f (k) = when k\n if True -> 1\n");
        Assert.Contains(diags.ToImmutable(), d => d.Message.Contains("Use 'is'"));
    }

    [Fact]
    public void Parse_underscore_pattern_emits_diagnostic()
    {
        var (_, diags) = ParseWithDiags("f (k) = when k\n is True -> 1\n is _ -> 0\n");
        Assert.Contains(diags.ToImmutable(), d => d.Message.Contains("Use 'otherwise'"));
    }

    [Fact]
    public void Parse_inline_const_integer()
    {
        DocumentNode doc = Parse("c : Integer = 1000");
        Assert.Single(doc.Definitions);
        DefinitionNode def = doc.Definitions[0];
        Assert.Equal("c", def.Name.Text);
        Assert.NotNull(def.TypeAnnotation);
        Assert.Empty(def.Parameters);
        Assert.IsType<LiteralExpressionNode>(def.Body);
    }

    [Fact]
    public void Parse_inline_const_text()
    {
        DocumentNode doc = Parse("greeting : Text = \"hi\"");
        Assert.Single(doc.Definitions);
        DefinitionNode def = doc.Definitions[0];
        Assert.Equal("greeting", def.Name.Text);
        Assert.NotNull(def.TypeAnnotation);
        Assert.Empty(def.Parameters);
        Assert.IsType<LiteralExpressionNode>(def.Body);
    }

    [Fact]
    public void Parse_inline_and_twoline_mixed()
    {
        string source = "a : Integer = 1\nb : Integer\nb = 2\n";
        DocumentNode doc = Parse(source);
        Assert.Equal(2, doc.Definitions.Count);
        Assert.Equal("a", doc.Definitions[0].Name.Text);
        Assert.NotNull(doc.Definitions[0].TypeAnnotation);
        Assert.Equal("b", doc.Definitions[1].Name.Text);
        Assert.NotNull(doc.Definitions[1].TypeAnnotation);
    }

    [Fact]
    public void Parse_definition_with_parameters()
    {
        DocumentNode doc = Parse("add (x) (y) = x + y");
        Assert.Single(doc.Definitions);
        Assert.Equal(2, doc.Definitions[0].Parameters.Count);
    }

    [Fact]
    public void Parse_if_expression()
    {
        DocumentNode doc = Parse("x = if True then 1 else 0");
        Assert.IsType<IfExpressionNode>(doc.Definitions[0].Body);
    }

    [Fact]
    public void Parse_let_expression()
    {
        DocumentNode doc = Parse("x = let a = 1 in a + 2");
        Assert.IsType<LetExpressionNode>(doc.Definitions[0].Body);
    }

    [Fact]
    public void Parse_binary_expression()
    {
        DocumentNode doc = Parse("x = 1 + 2 * 3");
        ExpressionNode body = doc.Definitions[0].Body;
        Assert.IsType<BinaryExpressionNode>(body);
        BinaryExpressionNode add = (BinaryExpressionNode)body;
        Assert.Equal(TokenKind.Plus, add.Operator.Kind);
        Assert.IsType<BinaryExpressionNode>(add.Right);
    }

    [Fact]
    public void Parse_list_literal()
    {
        DocumentNode doc = Parse("xs = [1, 2, 3]");
        Assert.IsType<ListExpressionNode>(doc.Definitions[0].Body);
        ListExpressionNode list = (ListExpressionNode)doc.Definitions[0].Body;
        Assert.Equal(3, list.Elements.Count);
    }

    [Fact]
    public void Parse_function_application()
    {
        DocumentNode doc = Parse("x = f 1 2");
        ExpressionNode body = doc.Definitions[0].Body;
        Assert.IsType<ApplicationExpressionNode>(body);
    }

    [Fact]
    public void Parse_record_construction()
    {
        DocumentNode doc = Parse("x = Point { x = 1, y = 2 }");
        Assert.IsType<RecordExpressionNode>(doc.Definitions[0].Body);
    }

    [Fact]
    public void Parse_match_expression()
    {
        string source = "x = when y is True -> 1 is False -> 0";
        (DocumentNode doc, DiagnosticBag diags) = ParseWithDiags(source);
        ExpressionNode body = doc.Definitions[0].Body;
        Assert.IsType<MatchExpressionNode>(body);
        MatchExpressionNode match = (MatchExpressionNode)body;
        Assert.Equal(2, match.Branches.Count);
    }

    [Fact]
    public void Parse_multiple_definitions()
    {
        string source = "a = 1\nb = 2\nc = 3";
        DocumentNode doc = Parse(source);
        Assert.Equal(3, doc.Definitions.Count);
    }

    [Fact]
    public void Parse_function_type()
    {
        string source = "f : Integer -> Integer -> Integer\nf (x) (y) = x + y";
        DocumentNode doc = Parse(source);
        Assert.NotNull(doc.Definitions[0].TypeAnnotation);
        Assert.IsType<FunctionTypeNode>(doc.Definitions[0].TypeAnnotation!.Type);
    }

    [Fact]
    public void Parse_negative_number()
    {
        DocumentNode doc = Parse("x = -42");
        Assert.IsType<UnaryExpressionNode>(doc.Definitions[0].Body);
    }

    [Fact]
    public void Parse_parenthesized_expression()
    {
        DocumentNode doc = Parse("x = (1 + 2) * 3");
        ExpressionNode body = doc.Definitions[0].Body;
        Assert.IsType<BinaryExpressionNode>(body);
        BinaryExpressionNode mul = (BinaryExpressionNode)body;
        Assert.Equal(TokenKind.Star, mul.Operator.Kind);
    }

    [Fact]
    public void Parse_record_type_definition()
    {
        string source = "Point = record { x : Number, y : Number }";
        DocumentNode doc = Parse(source);
        Assert.Single(doc.TypeDefinitions);
        Assert.Equal("Point", doc.TypeDefinitions[0].Name.Text);
        Assert.IsType<RecordTypeBody>(doc.TypeDefinitions[0].Body);
        RecordTypeBody body = (RecordTypeBody)doc.TypeDefinitions[0].Body;
        Assert.Equal(2, body.Fields.Count);
        Assert.Equal("x", body.Fields[0].Name.Text);
        Assert.Equal("y", body.Fields[1].Name.Text);
    }

    [Fact]
    public void Parse_variant_type_definition()
    {
        string source = "Color =\n  | Red\n  | Green\n  | Blue";
        DocumentNode doc = Parse(source);
        Assert.Single(doc.TypeDefinitions);
        Assert.Equal("Color", doc.TypeDefinitions[0].Name.Text);
        Assert.IsType<VariantTypeBody>(doc.TypeDefinitions[0].Body);
        VariantTypeBody body = (VariantTypeBody)doc.TypeDefinitions[0].Body;
        Assert.Equal(3, body.Constructors.Count);
        Assert.Equal("Red", body.Constructors[0].Name.Text);
        Assert.Equal("Green", body.Constructors[1].Name.Text);
        Assert.Equal("Blue", body.Constructors[2].Name.Text);
    }

    [Fact]
    public void Parse_variant_with_fields()
    {
        string source = "Shape =\n  | Circle (radius : Number)\n  | Rect (width : Number) (height : Number)";
        DocumentNode doc = Parse(source);
        Assert.Single(doc.TypeDefinitions);
        VariantTypeBody body = (VariantTypeBody)doc.TypeDefinitions[0].Body;
        Assert.Equal(2, body.Constructors.Count);
        Assert.Single(body.Constructors[0].Fields);
        Assert.Equal(2, body.Constructors[1].Fields.Count);
    }

    [Fact]
    public void Parse_variant_with_type_parameters()
    {
        string source = "Maybe (a) =\n  | Just (value : a)\n  | None";
        DocumentNode doc = Parse(source);
        Assert.Single(doc.TypeDefinitions);
        Assert.Single(doc.TypeDefinitions[0].TypeParameters);
        Assert.Equal("a", doc.TypeDefinitions[0].TypeParameters[0].Text);
    }

    [Fact]
    public void Parse_type_def_and_value_def_together()
    {
        string source =
            "Color =\n  | Red\n  | Green\n  | Blue\n\n" +
            "favorite : Color\nfavorite = Blue\n";
        DocumentNode doc = Parse(source);
        Assert.Single(doc.TypeDefinitions);
        Assert.Single(doc.Definitions);
        Assert.Equal("favorite", doc.Definitions[0].Name.Text);
    }

    [Fact]
    public void Parse_effectful_type_annotation()
    {
        DocumentNode doc = Parse("main : [Console] Nothing\nmain = 42");
        Assert.NotNull(doc.Definitions[0].TypeAnnotation);
        Assert.IsType<EffectfulTypeNode>(doc.Definitions[0].TypeAnnotation!.Type);
        EffectfulTypeNode eft = (EffectfulTypeNode)doc.Definitions[0].TypeAnnotation!.Type;
        Assert.Single(eft.Effects);
        Assert.IsType<NamedTypeNode>(eft.Effects[0]);
        Assert.IsType<NamedTypeNode>(eft.Return);
    }

    [Fact]
    public void Parse_effectful_type_with_multiple_effects()
    {
        DocumentNode doc = Parse("main : [Console, FileSystem] Nothing\nmain = 42");
        Assert.NotNull(doc.Definitions[0].TypeAnnotation);
        Assert.IsType<EffectfulTypeNode>(doc.Definitions[0].TypeAnnotation!.Type);
        EffectfulTypeNode eft = (EffectfulTypeNode)doc.Definitions[0].TypeAnnotation!.Type;
        Assert.Equal(2, eft.Effects.Count);
    }

    [Fact]
    public void Parse_do_expression()
    {
        string source = "main : [Console] Nothing\nmain = do\n  print-line \"hello\"\n";
        DocumentNode doc = Parse(source);
        Assert.IsType<DoExpressionNode>(doc.Definitions[0].Body);
        DoExpressionNode doExpr = (DoExpressionNode)doc.Definitions[0].Body;
        Assert.Single(doExpr.Statements);
        Assert.IsType<DoExprStatementNode>(doExpr.Statements[0]);
    }

    [Fact]
    public void Parse_do_bind_statement()
    {
        string source = "main : [Console] Nothing\nmain = do\n  x <- read-line\n  print-line x\n";
        DocumentNode doc = Parse(source);
        Assert.IsType<DoExpressionNode>(doc.Definitions[0].Body);
        DoExpressionNode doExpr = (DoExpressionNode)doc.Definitions[0].Body;
        Assert.Equal(2, doExpr.Statements.Count);
        Assert.IsType<DoBindStatementNode>(doExpr.Statements[0]);
        DoBindStatementNode bind = (DoBindStatementNode)doExpr.Statements[0];
        Assert.Equal("x", bind.Name.Text);
    }

    [Fact]
    public void Parse_function_with_effectful_return()
    {
        string source = "greet : Text -> [Console] Nothing\ngreet (name) = print-line name\n";
        DocumentNode doc = Parse(source);
        Assert.NotNull(doc.Definitions[0].TypeAnnotation);
        TypeNode typeNode = doc.Definitions[0].TypeAnnotation!.Type;
        Assert.IsType<FunctionTypeNode>(typeNode);
        FunctionTypeNode fn = (FunctionTypeNode)typeNode;
        Assert.IsType<NamedTypeNode>(fn.Parameter);
        Assert.IsType<EffectfulTypeNode>(fn.Return);
    }

    [Fact]
    public void Parse_linear_type_annotation()
    {
        DocumentNode doc = Parse("consume : linear FileHandle -> Nothing\nconsume (h) = h");
        Assert.NotNull(doc.Definitions[0].TypeAnnotation);
        TypeNode typeNode = doc.Definitions[0].TypeAnnotation!.Type;
        Assert.IsType<FunctionTypeNode>(typeNode);
        FunctionTypeNode fn = (FunctionTypeNode)typeNode;
        Assert.IsType<LinearTypeNode>(fn.Parameter);
        LinearTypeNode lin = (LinearTypeNode)fn.Parameter;
        Assert.IsType<NamedTypeNode>(lin.Inner);
    }

    [Fact]
    public void Parse_linear_type_with_effect()
    {
        DocumentNode doc = Parse("f : linear FileHandle -> [FileSystem] Nothing\nf (h) = close-file h");
        Assert.NotNull(doc.Definitions[0].TypeAnnotation);
        TypeNode typeNode = doc.Definitions[0].TypeAnnotation!.Type;
        Assert.IsType<FunctionTypeNode>(typeNode);
        FunctionTypeNode fn = (FunctionTypeNode)typeNode;
        Assert.IsType<LinearTypeNode>(fn.Parameter);
        Assert.IsType<EffectfulTypeNode>(fn.Return);
    }

    [Fact]
    public void Parse_dependent_function_type()
    {
        DocumentNode doc = Parse("f : (n : Integer) -> Integer\nf (x) = x");
        Assert.NotNull(doc.Definitions[0].TypeAnnotation);
        TypeNode typeNode = doc.Definitions[0].TypeAnnotation!.Type;
        Assert.IsType<DependentTypeNode>(typeNode);
        DependentTypeNode dep = (DependentTypeNode)typeNode;
        Assert.Equal("n", dep.ParamName.Text);
        Assert.IsType<NamedTypeNode>(dep.ParamType);
        Assert.IsType<NamedTypeNode>(dep.Body);
    }

    [Fact]
    public void Parse_type_with_integer_literal_argument()
    {
        DocumentNode doc = Parse("f : Vector 5 Integer -> Integer\nf (v) = 0");
        Assert.NotNull(doc.Definitions[0].TypeAnnotation);
        TypeNode typeNode = doc.Definitions[0].TypeAnnotation!.Type;
        Assert.IsType<FunctionTypeNode>(typeNode);
        FunctionTypeNode fn = (FunctionTypeNode)typeNode;
        Assert.IsType<ApplicationTypeNode>(fn.Parameter);
        ApplicationTypeNode app = (ApplicationTypeNode)fn.Parameter;
        Assert.Equal("Vector", ((NamedTypeNode)app.Constructor).Name.Text);
        Assert.Equal(2, app.Arguments.Count);
        Assert.IsType<IntegerTypeNode>(app.Arguments[0]);
    }

    [Fact]
    public void Parse_type_level_binary_in_parens()
    {
        DocumentNode doc = Parse("f : Vector (m + n) Integer -> Integer\nf (v) = 0");
        Assert.NotNull(doc.Definitions[0].TypeAnnotation);
        TypeNode typeNode = doc.Definitions[0].TypeAnnotation!.Type;
        Assert.IsType<FunctionTypeNode>(typeNode);
        FunctionTypeNode fn = (FunctionTypeNode)typeNode;
        Assert.IsType<ApplicationTypeNode>(fn.Parameter);
        ApplicationTypeNode app = (ApplicationTypeNode)fn.Parameter;
        Assert.IsType<BinaryTypeNode>(app.Arguments[0]);
    }

    [Fact]
    public void Parse_nested_dependent_types()
    {
        DocumentNode doc = Parse("f : (m : Integer) -> (n : Integer) -> Integer\nf (a) (b) = a");
        Assert.NotNull(doc.Definitions[0].TypeAnnotation);
        TypeNode typeNode = doc.Definitions[0].TypeAnnotation!.Type;
        Assert.IsType<DependentTypeNode>(typeNode);
        DependentTypeNode outer = (DependentTypeNode)typeNode;
        Assert.Equal("m", outer.ParamName.Text);
        Assert.IsType<DependentTypeNode>(outer.Body);
        DependentTypeNode inner = (DependentTypeNode)outer.Body;
        Assert.Equal("n", inner.ParamName.Text);
    }

    // --- Lambda expressions ---

    [Fact]
    public void Parse_lambda_single_param()
    {
        DocumentNode doc = Parse("f = \\x -> x + 1");
        Assert.Single(doc.Definitions);
        ExpressionNode body = doc.Definitions[0].Body;
        Assert.IsType<LambdaExpressionNode>(body);
        LambdaExpressionNode lam = (LambdaExpressionNode)body;
        Assert.Single(lam.Parameters);
        Assert.Equal("x", lam.Parameters[0].Text);
    }

    [Fact]
    public void Parse_lambda_multiple_params()
    {
        DocumentNode doc = Parse("f = \\x y -> x + y");
        LambdaExpressionNode lam = Assert.IsType<LambdaExpressionNode>(doc.Definitions[0].Body);
        Assert.Equal(2, lam.Parameters.Count);
        Assert.Equal("x", lam.Parameters[0].Text);
        Assert.Equal("y", lam.Parameters[1].Text);
    }

    [Fact]
    public void Parse_lambda_in_application()
    {
        DocumentNode doc = Parse("f = map (\\x -> x + 1) xs");
        Assert.Single(doc.Definitions);
    }

    // --- Claims and Proofs (Milestone 10) ---

    [Fact]
    public void Parse_simple_claim()
    {
        DocumentNode doc = Parse("claim zero-eq : 0 === 0\nproof zero-eq = Refl");
        Assert.Single(doc.Claims);
        Assert.Equal("zero-eq", doc.Claims[0].Name.Text);
        Assert.IsType<IntegerTypeNode>(doc.Claims[0].Left);
        Assert.IsType<IntegerTypeNode>(doc.Claims[0].Right);
    }

    [Fact]
    public void Parse_claim_with_parameters()
    {
        DocumentNode doc = Parse("claim eq (x) (y) : x === y\nproof eq (x) (y) = assume");
        Assert.Single(doc.Claims);
        Assert.Equal(2, doc.Claims[0].Parameters.Count);
        Assert.Equal("x", doc.Claims[0].Parameters[0].Text);
        Assert.Equal("y", doc.Claims[0].Parameters[1].Text);
    }

    [Fact]
    public void Parse_proof_with_refl()
    {
        DocumentNode doc = Parse("claim a : 1 === 1\nproof a = Refl");
        Assert.Single(doc.Proofs);
        Assert.Equal("a", doc.Proofs[0].Name.Text);
        Assert.IsType<ReflNode>(doc.Proofs[0].Body);
    }

    [Fact]
    public void Parse_proof_with_assume()
    {
        DocumentNode doc = Parse("claim a : 1 === 2\nproof a = assume");
        Assert.Single(doc.Proofs);
        Assert.IsType<AssumeNode>(doc.Proofs[0].Body);
    }

    [Fact]
    public void Parse_proof_with_sym()
    {
        DocumentNode doc = Parse("claim a : 1 === 1\nproof a = Refl\nclaim b : 1 === 1\nproof b = sym a");
        Assert.Equal(2, doc.Proofs.Count);
        Assert.IsType<SymNode>(doc.Proofs[1].Body);
    }

    [Fact]
    public void Parse_proof_with_induction()
    {
        string source = "claim id (xs) : xs === xs\n" +
                         "proof id (xs) =\n" +
                         "  induction xs\n" +
                         "    is Nil -> Refl\n" +
                         "    is Cons (h) (t) -> Refl\n";
        DocumentNode doc = Parse(source);
        Assert.Single(doc.Proofs);
        InductionNode ind = Assert.IsType<InductionNode>(doc.Proofs[0].Body);
        Assert.Equal("xs", ind.Variable.Text);
        Assert.Equal(2, ind.Cases.Count);
    }

    [Fact]
    public void Parse_proof_with_cong()
    {
        string source = "claim a : 1 === 1\nproof a = Refl\n" +
                         "claim b : List 1 === List 1\nproof b = cong List a";
        DocumentNode doc = Parse(source);
        CongNode cong = Assert.IsType<CongNode>(doc.Proofs[1].Body);
        Assert.Equal("List", cong.FunctionName.Text);
    }

    [Fact]
    public void Parse_proof_with_trans()
    {
        string source = "claim a : 1 === 1\nproof a = Refl\n" +
                         "claim b : 1 === 1\nproof b = trans a a";
        DocumentNode doc = Parse(source);
        Assert.IsType<TransNode>(doc.Proofs[1].Body);
    }

    // --- Error recovery tests ---

    [Fact]
    public void Recovery_continues_after_bad_definition()
    {
        string source = "!! bad\na = 1\nb = 2";
        (DocumentNode doc, DiagnosticBag diags) = ParseWithDiags(source);
        Assert.True(diags.HasErrors);
        Assert.True(doc.Definitions.Count >= 1);
    }

    [Fact]
    public void Recovery_if_missing_then()
    {
        string source = "x = if True 1 else 0";
        (DocumentNode doc, DiagnosticBag diags) = ParseWithDiags(source);
        Assert.True(diags.HasErrors);
        Assert.Contains(diags.ToImmutable(), d => d.Code == CdxCodes.ExpectedThenKeyword);
    }

    [Fact]
    public void Recovery_if_missing_else()
    {
        string source = "x = if True then 1";
        (DocumentNode doc, DiagnosticBag diags) = ParseWithDiags(source);
        Assert.True(diags.HasErrors);
        Assert.Contains(diags.ToImmutable(), d => d.Code == CdxCodes.ExpectedElseKeyword);
    }

    [Fact]
    public void Recovery_let_missing_in()
    {
        string source = "x = let a = 1 a + 2";
        (DocumentNode doc, DiagnosticBag diags) = ParseWithDiags(source);
        Assert.True(diags.HasErrors);
        Assert.Contains(diags.ToImmutable(), d => d.Code == CdxCodes.ExpectedInKeyword);
    }

    [Fact]
    public void Recovery_match_missing_arrow()
    {
        string source = "x = when y is True 1 is False 0";
        (DocumentNode doc, DiagnosticBag diags) = ParseWithDiags(source);
        Assert.True(diags.HasErrors);
        Assert.Contains(diags.ToImmutable(), d => d.Code == CdxCodes.ExpectedArrowAfterPattern);
    }

    [Fact]
    public void Recovery_second_definition_parsed_after_error()
    {
        string source = "a = @#$\nb = 42";
        (DocumentNode doc, DiagnosticBag diags) = ParseWithDiags(source);
        Assert.True(diags.HasErrors);
        bool found42 = doc.Definitions.Any(d => d.Name.Text == "b");
        Assert.True(found42);
    }

    [Fact]
    public void Recovery_multiple_errors_reported()
    {
        string source = "a = if True 1 else 0\nb = if False 2 else 3";
        (DocumentNode doc, DiagnosticBag diags) = ParseWithDiags(source);
        Assert.True(diags.HasErrors);
        int errorCount = diags.ToImmutable().Count(d => d.Code == CdxCodes.ExpectedThenKeyword);
        Assert.True(errorCount >= 2);
    }

    // --- Enhanced error recovery tests ---

    [Fact]
    public void Recovery_type_def_bad_body_produces_error_type_body()
    {
        string source = "Color = !! bad\na = 1";
        (DocumentNode doc, DiagnosticBag diags) = ParseWithDiags(source);
        Assert.True(diags.HasErrors);
        Assert.Single(doc.TypeDefinitions);
        Assert.Equal("Color", doc.TypeDefinitions[0].Name.Text);
        Assert.IsType<ErrorTypeBody>(doc.TypeDefinitions[0].Body);
        Assert.True(doc.Definitions.Count >= 1);
    }

    [Fact]
    public void Recovery_type_def_preserves_name_for_subsequent_defs()
    {
        string source = "Shape = oops\nb = 42\nc = 99";
        (DocumentNode doc, DiagnosticBag diags) = ParseWithDiags(source);
        Assert.True(diags.HasErrors);
        Assert.Single(doc.TypeDefinitions);
        Assert.Equal("Shape", doc.TypeDefinitions[0].Name.Text);
        Assert.True(doc.Definitions.Count >= 1);
        Assert.Contains(doc.Definitions, d => d.Name.Text == "b" || d.Name.Text == "c");
    }

    [Fact]
    public void Recovery_definition_missing_equals_produces_partial_def()
    {
        string source = "f (x) oops\ng = 42";
        (DocumentNode doc, DiagnosticBag diags) = ParseWithDiags(source);
        Assert.True(diags.HasErrors);
        Assert.Contains(doc.Definitions, d => d.Name.Text == "g");
    }

    [Fact]
    public void Recovery_three_defs_middle_has_error()
    {
        string source = "a = 1\nb = @#$\nc = 3";
        (DocumentNode doc, DiagnosticBag diags) = ParseWithDiags(source);
        Assert.True(diags.HasErrors);
        Assert.Contains(doc.Definitions, d => d.Name.Text == "a");
        Assert.Contains(doc.Definitions, d => d.Name.Text == "c");
    }

    [Fact]
    public void Recovery_variant_bad_constructor_continues()
    {
        string source = "Shape =\n  | Circle (Integer)\n  | 123\n  | Square (Integer)";
        (DocumentNode doc, DiagnosticBag diags) = ParseWithDiags(source);
        Assert.True(diags.HasErrors);
        Assert.Single(doc.TypeDefinitions);
        VariantTypeBody body = Assert.IsType<VariantTypeBody>(doc.TypeDefinitions[0].Body);
        Assert.True(body.Constructors.Count >= 1);
        Assert.Contains(body.Constructors, c => c.Name.Text == "Circle");
    }

    [Fact]
    public void Recovery_record_bad_field_continues()
    {
        string source = "Point = record { x : Integer, !! bad, y : Integer }";
        (DocumentNode doc, DiagnosticBag diags) = ParseWithDiags(source);
        Assert.True(diags.HasErrors);
        Assert.Single(doc.TypeDefinitions);
        RecordTypeBody body = Assert.IsType<RecordTypeBody>(doc.TypeDefinitions[0].Body);
        Assert.True(body.Fields.Count >= 1);
    }

    [Fact]
    public void Recovery_do_expression_continues_after_bad_statement()
    {
        string source = "f = do\n  @#$ bad\n  print-line \"hello\"";
        (DocumentNode doc, DiagnosticBag diags) = ParseWithDiags(source);
        Assert.True(diags.HasErrors);
        Assert.Single(doc.Definitions);
    }

    [Fact]
    public void Recovery_interleaved_types_and_defs_with_errors()
    {
        string source =
            "Color =\n  | Red\n  | Green\n\n" +
            "f = @#$\n\n" +
            "Point = record { x : Integer, y : Integer }\n\n" +
            "g = 42";
        (DocumentNode doc, DiagnosticBag diags) = ParseWithDiags(source);
        Assert.True(diags.HasErrors);
        Assert.Contains(doc.TypeDefinitions, td => td.Name.Text == "Color");
        Assert.Contains(doc.TypeDefinitions, td => td.Name.Text == "Point");
        Assert.Contains(doc.Definitions, d => d.Name.Text == "g");
    }

    [Fact]
    public void Recovery_error_count_limited_no_infinite_loop()
    {
        DocumentNode doc = Parse("!! bad\n!! bad\n!! bad\na = 1");
        (DocumentNode _, DiagnosticBag diags) = ParseWithDiags("!! bad\n!! bad\n!! bad\na = 1");
        Assert.True(diags.HasErrors);
        Assert.True(diags.ToImmutable().Length < 100);
        Assert.Contains(doc.Definitions, d => d.Name.Text == "a");
    }

    [Fact]
    public void Recovery_claim_after_bad_definition()
    {
        string source = "f = @#$\nclaim eq : 1 == 1";
        (DocumentNode doc, DiagnosticBag diags) = ParseWithDiags(source);
        Assert.True(diags.HasErrors);
        Assert.True(doc.Claims.Count >= 1);
    }

    [Fact]
    public void Recovery_import_after_bad_definition()
    {
        string source = "cites Math\nf = @#$\ng = 42";
        (DocumentNode doc, DiagnosticBag diags) = ParseWithDiags(source);
        Assert.True(diags.HasErrors);
        Assert.Single(doc.Citations);
        Assert.Contains(doc.Definitions, d => d.Name.Text == "g");
    }
}
