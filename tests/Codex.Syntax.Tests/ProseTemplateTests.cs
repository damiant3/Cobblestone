using Codex.Core;
using Codex.Syntax;
using Xunit;

namespace Codex.Syntax.Tests;

public class ProseTemplateTests
{
    static (DocumentNode Doc, DiagnosticBag Diags) ParseProse(string source)
    {
        SourceText src = new("test.codex", source);
        DiagnosticBag bag = new();
        ProseParser parser = new(src, bag);
        return (parser.ParseDocument(), bag);
    }

    [Fact]
    public void Record_template_produces_type_definition()
    {
        string source =
            "Chapter: Banking\n" +
            "\n" +
            "An Account is a record containing:\n" +
            "- owner : Text\n" +
            "- balance : Integer\n";

        (DocumentNode doc, DiagnosticBag diags) = ParseProse(source);
        Assert.False(diags.HasErrors);
        Assert.Single(doc.TypeDefinitions);
        Assert.Equal("Account", doc.TypeDefinitions[0].Name.Text);

        RecordTypeBody body = Assert.IsType<RecordTypeBody>(doc.TypeDefinitions[0].Body);
        Assert.Equal(2, body.Fields.Count);
        Assert.Equal("owner", body.Fields[0].Name.Text);
        Assert.Equal("balance", body.Fields[1].Name.Text);
    }

    [Fact]
    public void Variant_template_produces_type_definition()
    {
        string source =
            "Chapter: Shapes\n" +
            "\n" +
            "Shape is either:\n" +
            "- Circle (radius : Number)\n" +
            "- Rectangle (width : Number, height : Number)\n" +
            "- Point\n";

        (DocumentNode doc, DiagnosticBag diags) = ParseProse(source);
        Assert.False(diags.HasErrors);
        Assert.Single(doc.TypeDefinitions);
        Assert.Equal("Shape", doc.TypeDefinitions[0].Name.Text);

        VariantTypeBody body = Assert.IsType<VariantTypeBody>(doc.TypeDefinitions[0].Body);
        Assert.Equal(3, body.Constructors.Count);
        Assert.Equal("Circle", body.Constructors[0].Name.Text);
        Assert.Single(body.Constructors[0].Fields);
        Assert.Equal("Rectangle", body.Constructors[1].Name.Text);
        Assert.Equal(2, body.Constructors[1].Fields.Count);
        Assert.Equal("Point", body.Constructors[2].Name.Text);
        Assert.Empty(body.Constructors[2].Fields);
    }

    [Fact]
    public void Record_template_with_article_a()
    {
        string source =
            "Chapter: Data\n" +
            "\n" +
            "A Point is a record containing:\n" +
            "- x : Integer\n" +
            "- y : Integer\n";

        (DocumentNode doc, _) = ParseProse(source);
        Assert.Single(doc.TypeDefinitions);
        Assert.Equal("Point", doc.TypeDefinitions[0].Name.Text);
    }

    [Fact]
    public void Template_with_notation_below()
    {
        string source =
            "Chapter: Banking\n" +
            "\n" +
            "An Account is a record containing:\n" +
            "- owner : Text\n" +
            "- balance : Integer\n" +
            "\n" +
            "    deposit : Account -> Integer -> Account\n" +
            "    deposit (account) (amount) = account\n";

        (DocumentNode doc, DiagnosticBag diags) = ParseProse(source);
        Assert.False(diags.HasErrors);
        Assert.Single(doc.TypeDefinitions);
        Assert.Single(doc.Definitions);
        Assert.Equal("deposit", doc.Definitions[0].Name.Text);
    }

    [Fact]
    public void No_template_match_produces_plain_prose()
    {
        string source =
            "Chapter: Intro\n" +
            "\n" +
            "This is just regular prose.\n" +
            "Nothing special here.\n";

        (DocumentNode doc, _) = ParseProse(source);
        Assert.Empty(doc.TypeDefinitions);
        Assert.Empty(doc.Definitions);
    }

    [Fact]
    public void Template_without_bullets_is_ignored()
    {
        string source =
            "Chapter: Data\n" +
            "\n" +
            "Account is a record containing:\n" +
            "\n" +
            "  Some more prose.\n";

        (DocumentNode doc, _) = ParseProse(source);
        Assert.Empty(doc.TypeDefinitions);
    }

    [Fact]
    public void Record_template_desugars_to_ast()
    {
        string source =
            "Chapter: Data\n" +
            "\n" +
            "An Account is a record containing:\n" +
            "- owner : Text\n" +
            "- balance : Integer\n";

        (DocumentNode doc, _) = ParseProse(source);
        DiagnosticBag diagnostics = new();
        Codex.Ast.Desugarer desugarer = new(diagnostics);
        Codex.Ast.Chapter module = desugarer.Desugar(doc, "data");

        Assert.False(diagnostics.HasErrors,
            string.Join("; ", diagnostics.ToImmutable()));
        Assert.Single(module.TypeDefinitions);
        Codex.Ast.RecordTypeDef rec =
            Assert.IsType<Codex.Ast.RecordTypeDef>(module.TypeDefinitions[0]);
        Assert.Equal("Account", rec.Name.Value);
        Assert.Equal(2, rec.Fields.Count);
    }

    // --- Phase 1: Function templates ---

    [Fact]
    public void Function_template_basic()
    {
        string source =
            "Chapter: Greeting\n" +
            "\n" +
            "To greet (name : Text):\n" +
            "\n" +
            "    greet : Text -> Text\n" +
            "    greet (name) = \"Hello, \" ++ name\n";

        (DocumentNode doc, DiagnosticBag diags) = ParseProse(source);
        Assert.False(diags.HasErrors);

        ChapterNode chapter = Assert.Single(doc.Chapters);
        ProseBlockNode prose = chapter.Members.OfType<ProseBlockNode>().First();
        Assert.NotNull(prose.FunctionTemplate);
        Assert.Equal("greet", prose.FunctionTemplate.FunctionName);
        Assert.Single(prose.FunctionTemplate.Parameters);
        Assert.Equal("name", prose.FunctionTemplate.Parameters[0].Name);
        Assert.Equal("Text", prose.FunctionTemplate.Parameters[0].Type);
    }

    [Fact]
    public void Function_template_multiple_params()
    {
        string source =
            "Chapter: Banking\n" +
            "\n" +
            "To deposit (amount : Integer) into (account : Account):\n";

        (DocumentNode doc, _) = ParseProse(source);
        ChapterNode chapter = Assert.Single(doc.Chapters);
        ProseBlockNode prose = chapter.Members.OfType<ProseBlockNode>().First();
        Assert.NotNull(prose.FunctionTemplate);
        Assert.Equal("deposit", prose.FunctionTemplate.FunctionName);
        Assert.Equal(2, prose.FunctionTemplate.Parameters.Count);
        Assert.Equal("amount", prose.FunctionTemplate.Parameters[0].Name);
        Assert.Equal("Integer", prose.FunctionTemplate.Parameters[0].Type);
        Assert.Equal("account", prose.FunctionTemplate.Parameters[1].Name);
        Assert.Equal("Account", prose.FunctionTemplate.Parameters[1].Type);
    }

    [Fact]
    public void Function_template_gives_clause()
    {
        string source =
            "Chapter: Math\n" +
            "\n" +
            "To compute (x : Integer) gives Integer:\n";

        (DocumentNode doc, _) = ParseProse(source);
        ChapterNode chapter = Assert.Single(doc.Chapters);
        ProseBlockNode prose = chapter.Members.OfType<ProseBlockNode>().First();
        Assert.NotNull(prose.FunctionTemplate);
        Assert.Equal("compute", prose.FunctionTemplate.FunctionName);
        Assert.Equal("Integer", prose.FunctionTemplate.ReturnType);
    }

    [Fact]
    public void Function_template_no_colon_is_plain_prose()
    {
        string source =
            "Chapter: Intro\n" +
            "\n" +
            "To understand this module, read the docs.\n";

        (DocumentNode doc, _) = ParseProse(source);
        ChapterNode chapter = Assert.Single(doc.Chapters);
        ProseBlockNode prose = chapter.Members.OfType<ProseBlockNode>().First();
        Assert.Null(prose.FunctionTemplate);
    }

    [Fact]
    public void Function_template_multi_word_name()
    {
        string source =
            "Chapter: Banking\n" +
            "\n" +
            "To compute monthly interest (rate : Number) on (account : Account):\n";

        (DocumentNode doc, _) = ParseProse(source);
        ChapterNode chapter = Assert.Single(doc.Chapters);
        ProseBlockNode prose = chapter.Members.OfType<ProseBlockNode>().First();
        Assert.NotNull(prose.FunctionTemplate);
        Assert.Equal("compute-monthly-interest", prose.FunctionTemplate.FunctionName);
        Assert.Equal(2, prose.FunctionTemplate.Parameters.Count);
        Assert.Equal("rate", prose.FunctionTemplate.Parameters[0].Name);
        Assert.Equal("account", prose.FunctionTemplate.Parameters[1].Name);
    }

    // --- Phase 2: Claims and proofs ---

    [Fact]
    public void Claims_collected_from_notation_block()
    {
        string source =
            "Chapter: Proofs\n" +
            "\n" +
            "    claim add-comm (x) (y) : Integer === Integer\n";

        (DocumentNode doc, DiagnosticBag diags) = ParseProse(source);
        Assert.False(diags.HasErrors);
        Assert.Single(doc.Claims);
        Assert.Equal("add-comm", doc.Claims[0].Name.Text);
    }

    // --- Phase 3: Transition markers ---

    [Fact]
    public void We_say_sets_transition_kind()
    {
        string source =
            "Chapter: Greeting\n" +
            "\n" +
            "This module provides greetings. We say:\n" +
            "\n" +
            "    greet : Text -> Text\n" +
            "    greet (name) = \"Hello, \" ++ name\n";

        (DocumentNode doc, _) = ParseProse(source);
        ChapterNode chapter = Assert.Single(doc.Chapters);
        ProseBlockNode prose = chapter.Members.OfType<ProseBlockNode>().First();
        Assert.Equal(ProseTransitionKind.WeSay, prose.Transition);
    }

    [Fact]
    public void This_is_written_sets_transition_kind()
    {
        string source =
            "Chapter: Math\n" +
            "\n" +
            "Addition is straightforward. This is written:\n" +
            "\n" +
            "    add : Integer -> Integer -> Integer\n" +
            "    add (x) (y) = x + y\n";

        (DocumentNode doc, _) = ParseProse(source);
        ChapterNode chapter = Assert.Single(doc.Chapters);
        ProseBlockNode prose = chapter.Members.OfType<ProseBlockNode>().First();
        Assert.Equal(ProseTransitionKind.ThisIsWritten, prose.Transition);
    }

    [Fact]
    public void Plain_prose_has_no_transition()
    {
        string source =
            "Chapter: Intro\n" +
            "\n" +
            "This is just regular prose.\n";

        (DocumentNode doc, _) = ParseProse(source);
        ChapterNode chapter = Assert.Single(doc.Chapters);
        ProseBlockNode prose = chapter.Members.OfType<ProseBlockNode>().First();
        Assert.Equal(ProseTransitionKind.None, prose.Transition);
    }

    // --- Phase 5: Prose-notation consistency checking ---

    [Fact]
    public void Matching_function_template_no_warning()
    {
        string source =
            "Chapter: Greeting\n" +
            "\n" +
            "To greet (name : Text):\n" +
            "\n" +
            "    greet : Text -> Text\n" +
            "    greet (name) = \"Hello, \" ++ name\n";

        (DocumentNode doc, DiagnosticBag diags) = ParseProse(source);
        Assert.False(diags.HasErrors);
        Assert.Empty(diags.ToImmutable().Where(d => d.Code == "CDX1101" || d.Code == "CDX1102"));
    }

    [Fact]
    public void Mismatched_function_name_warns()
    {
        string source =
            "Chapter: Banking\n" +
            "\n" +
            "To deposit (amount : Integer):\n" +
            "\n" +
            "    withdraw : Integer -> Integer\n" +
            "    withdraw (amount) = amount\n";

        (DocumentNode doc, DiagnosticBag diags) = ParseProse(source);
        Assert.Contains(diags.ToImmutable(), d => d.Code == "CDX1101");
    }

    [Fact]
    public void Mismatched_parameter_name_warns()
    {
        string source =
            "Chapter: Banking\n" +
            "\n" +
            "To deposit (amount : Integer):\n" +
            "\n" +
            "    deposit : Integer -> Integer\n" +
            "    deposit (qty) = qty\n";

        (DocumentNode doc, DiagnosticBag diags) = ParseProse(source);
        Assert.Contains(diags.ToImmutable(), d => d.Code == "CDX1102");
    }

    [Fact]
    public void No_warning_when_prose_has_no_template()
    {
        string source =
            "Chapter: Greeting\n" +
            "\n" +
            "This module provides greetings.\n" +
            "\n" +
            "    greet : Text -> Text\n" +
            "    greet (name) = \"Hello, \" ++ name\n";

        (DocumentNode doc, DiagnosticBag diags) = ParseProse(source);
        Assert.Empty(diags.ToImmutable().Where(d => d.Code == "CDX1101" || d.Code == "CDX1102"));
    }

    // --- Phase 4: Inline code references ---

    [Fact]
    public void Backtick_code_ref_extracted()
    {
        string source =
            "Chapter: Greeting\n" +
            "\n" +
            "The `greet` function says hello.\n";

        (DocumentNode doc, _) = ParseProse(source);
        ChapterNode chapter = Assert.Single(doc.Chapters);
        ProseBlockNode prose = chapter.Members.OfType<ProseBlockNode>().First();
        Assert.Single(prose.CodeRefs);
        Assert.Equal("greet", prose.CodeRefs[0].Code);
    }

    [Fact]
    public void Multiple_code_refs_extracted()
    {
        string source =
            "Chapter: Banking\n" +
            "\n" +
            "The `deposit` and `withdraw` functions modify the account.\n";

        (DocumentNode doc, _) = ParseProse(source);
        ChapterNode chapter = Assert.Single(doc.Chapters);
        ProseBlockNode prose = chapter.Members.OfType<ProseBlockNode>().First();
        Assert.Equal(2, prose.CodeRefs.Count);
        Assert.Equal("deposit", prose.CodeRefs[0].Code);
        Assert.Equal("withdraw", prose.CodeRefs[1].Code);
    }

    [Fact]
    public void Type_ref_extracted_from_prose()
    {
        string source =
            "Chapter: Banking\n" +
            "\n" +
            "An Account holds a balance.\n";

        (DocumentNode doc, _) = ParseProse(source);
        ChapterNode chapter = Assert.Single(doc.Chapters);
        ProseBlockNode prose = chapter.Members.OfType<ProseBlockNode>().First();
        Assert.Contains(prose.TypeRefs, r => r.TypeName == "Account");
    }

    [Fact]
    public void No_refs_in_plain_prose()
    {
        string source =
            "Chapter: Intro\n" +
            "\n" +
            "This is just regular prose with no references.\n";

        (DocumentNode doc, _) = ParseProse(source);
        ChapterNode chapter = Assert.Single(doc.Chapters);
        ProseBlockNode prose = chapter.Members.OfType<ProseBlockNode>().First();
        Assert.Empty(prose.CodeRefs);
    }

    // --- Constraint templates ---

    [Fact]
    public void Record_such_that_constraint()
    {
        string source =
            "Chapter: Banking\n" +
            "\n" +
            "An Account is a record containing:\n" +
            "- owner : Text\n" +
            "- balance : Integer\n" +
            "such that the balance is at least zero.\n";

        (DocumentNode doc, _) = ParseProse(source);
        Assert.Single(doc.TypeDefinitions);
        RecordTypeBody body = Assert.IsType<RecordTypeBody>(doc.TypeDefinitions[0].Body);
        Assert.Single(body.Constraints);
        Assert.Equal("such that", body.Constraints[0].Keyword);
        Assert.Equal("the balance is at least zero", body.Constraints[0].Text);
    }

    [Fact]
    public void Record_where_constraint()
    {
        string source =
            "Chapter: Math\n" +
            "\n" +
            "A Positive is a record containing:\n" +
            "- value : Integer\n" +
            "where value is greater than zero.\n";

        (DocumentNode doc, _) = ParseProse(source);
        RecordTypeBody body = Assert.IsType<RecordTypeBody>(doc.TypeDefinitions[0].Body);
        Assert.Single(body.Constraints);
        Assert.Equal("where", body.Constraints[0].Keyword);
    }

    [Fact]
    public void Variant_provided_that_constraint()
    {
        string source =
            "Chapter: Data\n" +
            "\n" +
            "Status is either:\n" +
            "- Active\n" +
            "- Suspended\n" +
            "provided that no Status is both Active and Suspended.\n";

        (DocumentNode doc, _) = ParseProse(source);
        VariantTypeBody body = Assert.IsType<VariantTypeBody>(doc.TypeDefinitions[0].Body);
        Assert.Single(body.Constraints);
        Assert.Equal("provided that", body.Constraints[0].Keyword);
    }

    [Fact]
    public void No_constraint_when_absent()
    {
        string source =
            "Chapter: Data\n" +
            "\n" +
            "A Point is a record containing:\n" +
            "- x : Integer\n" +
            "- y : Integer\n";

        (DocumentNode doc, _) = ParseProse(source);
        RecordTypeBody body = Assert.IsType<RecordTypeBody>(doc.TypeDefinitions[0].Body);
        Assert.Empty(body.Constraints);
    }

    // --- Claim and proof templates ---

    [Fact]
    public void Claim_template_recognized()
    {
        string source =
            "Chapter: Proofs\n" +
            "\n" +
            "Claim: reversing a list twice gives the original list.\n" +
            "\n" +
            "    claim reverse-twice (xs) : Integer === Integer\n";

        (DocumentNode doc, _) = ParseProse(source);
        ChapterNode chapter = Assert.Single(doc.Chapters);
        ProseBlockNode prose = chapter.Members.OfType<ProseBlockNode>().First();
        Assert.NotNull(prose.ClaimTemplate);
        Assert.Equal("reversing a list twice gives the original list", prose.ClaimTemplate.Value.Description);
    }

    [Fact]
    public void Proof_template_recognized()
    {
        string source =
            "Chapter: Proofs\n" +
            "\n" +
            "Proof: by induction on the list.\n";

        (DocumentNode doc, _) = ParseProse(source);
        ChapterNode chapter = Assert.Single(doc.Chapters);
        ProseBlockNode prose = chapter.Members.OfType<ProseBlockNode>().First();
        Assert.NotNull(prose.ProofTemplate);
        Assert.Equal("by induction on the list", prose.ProofTemplate.Value.Strategy);
    }

    [Fact]
    public void Claim_without_notation_warns()
    {
        string source =
            "Chapter: Proofs\n" +
            "\n" +
            "Claim: something is true.\n" +
            "\n" +
            "Just some prose here.\n";

        (DocumentNode doc, DiagnosticBag diags) = ParseProse(source);
        Assert.Contains(diags.ToImmutable(), d => d.Code == "CDX1105");
    }

    // --- Fail clauses on function templates ---

    [Fact]
    public void Function_template_failing_if()
    {
        string source =
            "Chapter: Banking\n" +
            "\n" +
            "To deposit (amount : Integer) into (account : Account),\n" +
            "failing if amount is less than zero:\n";

        (DocumentNode doc, _) = ParseProse(source);
        ChapterNode chapter = Assert.Single(doc.Chapters);
        ProseBlockNode prose = chapter.Members.OfType<ProseBlockNode>().First();
        Assert.NotNull(prose.FunctionTemplate);
        Assert.Equal("deposit", prose.FunctionTemplate.FunctionName);
        Assert.Single(prose.FunctionTemplate.FailClauses);
        Assert.Null(prose.FunctionTemplate.FailClauses[0].Reason);
        Assert.Equal("amount is less than zero", prose.FunctionTemplate.FailClauses[0].Condition);
    }

    [Fact]
    public void Function_template_or_fails_with()
    {
        string source =
            "Chapter: Banking\n" +
            "\n" +
            "To withdraw (amount : Integer) from (account : Account),\n" +
            "or fails with \"insufficient funds\" if the balance is less than amount:\n";

        (DocumentNode doc, _) = ParseProse(source);
        ChapterNode chapter = Assert.Single(doc.Chapters);
        ProseBlockNode prose = chapter.Members.OfType<ProseBlockNode>().First();
        Assert.NotNull(prose.FunctionTemplate);
        Assert.Equal("withdraw", prose.FunctionTemplate.FunctionName);
        Assert.Single(prose.FunctionTemplate.FailClauses);
        Assert.Equal("insufficient funds", prose.FunctionTemplate.FailClauses[0].Reason);
        Assert.Contains("balance is less than amount", prose.FunctionTemplate.FailClauses[0].Condition);
    }

    [Fact]
    public void Function_template_gives_and_fails()
    {
        string source =
            "Chapter: Banking\n" +
            "\n" +
            "To transfer (amount : Integer) gives TransferResult,\n" +
            "failing if amount is zero:\n";

        (DocumentNode doc, _) = ParseProse(source);
        ChapterNode chapter = Assert.Single(doc.Chapters);
        ProseBlockNode prose = chapter.Members.OfType<ProseBlockNode>().First();
        Assert.NotNull(prose.FunctionTemplate);
        Assert.Equal("transfer", prose.FunctionTemplate.FunctionName);
        Assert.Equal("TransferResult", prose.FunctionTemplate.ReturnType);
        Assert.Single(prose.FunctionTemplate.FailClauses);
        Assert.Equal("amount is zero", prose.FunctionTemplate.FailClauses[0].Condition);
    }

    [Fact]
    public void Function_template_gives_the_updated()
    {
        string source =
            "Chapter: Banking\n" +
            "\n" +
            "To deposit (amount : Integer) gives the updated Account:\n";

        (DocumentNode doc, _) = ParseProse(source);
        ChapterNode chapter = Assert.Single(doc.Chapters);
        ProseBlockNode prose = chapter.Members.OfType<ProseBlockNode>().First();
        Assert.NotNull(prose.FunctionTemplate);
        Assert.Equal("Account", prose.FunctionTemplate.ReturnType);
    }

    // --- Procedure steps (CPL Form 5) ---

    [Fact]
    public void Procedure_let_step()
    {
        string source =
            "Chapter: Banking\n" +
            "\n" +
            "First, let new-balance be the balance plus the amount.\n";

        (DocumentNode doc, _) = ParseProse(source);
        ChapterNode chapter = Assert.Single(doc.Chapters);
        ProseBlockNode prose = chapter.Members.OfType<ProseBlockNode>().First();
        Assert.NotNull(prose.Procedure);
        Assert.Single(prose.Procedure.Steps);
        ProcedureStep step = prose.Procedure.Steps[0];
        Assert.Equal(ProcedureStepKind.Let, step.Kind);
        Assert.Equal("first", step.Marker);
        Assert.Equal("new-balance", step.Binding);
        Assert.Equal("the balance plus the amount", step.Value);
    }

    [Fact]
    public void Procedure_multi_step()
    {
        string source =
            "Chapter: Banking\n" +
            "\n" +
            "First, let updated be the balance plus amount.\n" +
            "Then, return a new Account with balance set to updated.\n";

        (DocumentNode doc, _) = ParseProse(source);
        ChapterNode chapter = Assert.Single(doc.Chapters);
        ProseBlockNode prose = chapter.Members.OfType<ProseBlockNode>().First();
        Assert.NotNull(prose.Procedure);
        Assert.Equal(2, prose.Procedure.Steps.Count);
        Assert.Equal(ProcedureStepKind.Let, prose.Procedure.Steps[0].Kind);
        Assert.Equal(ProcedureStepKind.Return, prose.Procedure.Steps[1].Kind);
        Assert.Equal("a new Account with balance set to updated", prose.Procedure.Steps[1].Value);
    }

    [Fact]
    public void Procedure_if_otherwise()
    {
        string source =
            "Chapter: Banking\n" +
            "\n" +
            "First, if the amount is negative, fail with \"invalid amount\", otherwise let valid be the amount.\n";

        (DocumentNode doc, _) = ParseProse(source);
        ChapterNode chapter = Assert.Single(doc.Chapters);
        ProseBlockNode prose = chapter.Members.OfType<ProseBlockNode>().First();
        Assert.NotNull(prose.Procedure);
        ProcedureStep step = prose.Procedure.Steps[0];
        Assert.Equal(ProcedureStepKind.If, step.Kind);
        Assert.Equal("the amount is negative", step.Condition);
        Assert.Contains("invalid amount", step.Value!);
        Assert.Contains("let valid be the amount", step.Otherwise!);
    }

    [Fact]
    public void Procedure_fail_with()
    {
        string source =
            "Chapter: Error\n" +
            "\n" +
            "Finally, fail with \"not enough funds\".\n";

        (DocumentNode doc, _) = ParseProse(source);
        ChapterNode chapter = Assert.Single(doc.Chapters);
        ProseBlockNode prose = chapter.Members.OfType<ProseBlockNode>().First();
        Assert.NotNull(prose.Procedure);
        ProcedureStep step = prose.Procedure.Steps[0];
        Assert.Equal(ProcedureStepKind.FailWith, step.Kind);
        Assert.Equal("finally", step.Marker);
        Assert.Equal("not enough funds", step.Value);
    }

    [Fact]
    public void No_procedure_in_plain_prose()
    {
        string source =
            "Chapter: Intro\n" +
            "\n" +
            "This is just regular prose.\n";

        (DocumentNode doc, _) = ParseProse(source);
        ChapterNode chapter = Assert.Single(doc.Chapters);
        ProseBlockNode prose = chapter.Members.OfType<ProseBlockNode>().First();
        Assert.Null(prose.Procedure);
    }

    // --- Quantified statements (CPL Form 6) ---

    [Fact]
    public void For_every_statement()
    {
        string source =
            "Chapter: Rules\n" +
            "\n" +
            "For every transaction in the history, the amount is positive.\n";

        (DocumentNode doc, _) = ParseProse(source);
        ChapterNode chapter = Assert.Single(doc.Chapters);
        ProseBlockNode prose = chapter.Members.OfType<ProseBlockNode>().First();
        Assert.Single(prose.QuantifiedStatements);
        ProseQuantifiedStatement qs = prose.QuantifiedStatements[0];
        Assert.Equal(QuantifierKind.ForEvery, qs.Quantifier);
        Assert.Equal("transaction", qs.BoundVariable);
        Assert.Equal("the history", qs.Collection);
        Assert.Equal("the amount is positive", qs.Claim);
    }

    [Fact]
    public void There_exists_exactly_one()
    {
        string source =
            "Chapter: Rules\n" +
            "\n" +
            "There exists exactly one owner in the accounts such that the owner is active.\n";

        (DocumentNode doc, _) = ParseProse(source);
        ChapterNode chapter = Assert.Single(doc.Chapters);
        ProseBlockNode prose = chapter.Members.OfType<ProseBlockNode>().First();
        Assert.Single(prose.QuantifiedStatements);
        ProseQuantifiedStatement qs = prose.QuantifiedStatements[0];
        Assert.Equal(QuantifierKind.ThereExists, qs.Quantifier);
        Assert.Equal("exactly one", qs.Qualifier);
        Assert.Equal("owner", qs.BoundVariable);
        Assert.Equal("the accounts", qs.Collection);
        Assert.Equal("the owner is active", qs.Claim);
    }

    [Fact]
    public void No_statement()
    {
        string source =
            "Chapter: Rules\n" +
            "\n" +
            "No account in the ledger has a negative balance.\n";

        (DocumentNode doc, _) = ParseProse(source);
        ChapterNode chapter = Assert.Single(doc.Chapters);
        ProseBlockNode prose = chapter.Members.OfType<ProseBlockNode>().First();
        Assert.Single(prose.QuantifiedStatements);
        ProseQuantifiedStatement qs = prose.QuantifiedStatements[0];
        Assert.Equal(QuantifierKind.No, qs.Quantifier);
        Assert.Equal("account", qs.BoundVariable);
        Assert.Equal("the ledger", qs.Collection);
        Assert.Equal("a negative balance", qs.Claim);
    }

    [Fact]
    public void No_quantified_in_plain_prose()
    {
        string source =
            "Chapter: Intro\n" +
            "\n" +
            "Nothing quantified here.\n";

        (DocumentNode doc, _) = ParseProse(source);
        ChapterNode chapter = Assert.Single(doc.Chapters);
        ProseBlockNode prose = chapter.Members.OfType<ProseBlockNode>().First();
        Assert.Empty(prose.QuantifiedStatements);
    }
}
