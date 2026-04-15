using Codex.Core;

namespace Codex.Syntax;

public sealed partial class ProseParser
{
    static string? TryMatchRecordTemplate(string line)
    {
        string lower = line.TrimEnd();
        if (!lower.EndsWith(':'))
        {
            return null;
        }

        lower = lower[..^1].Trim();

        if (!lower.EndsWith("containing", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        lower = lower[..^"containing".Length].Trim();

        if (!lower.EndsWith("record", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        lower = lower[..^"record".Length].Trim();

        if (!lower.EndsWith("a", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        lower = lower[..^1].Trim();

        int isIdx = lower.LastIndexOf(" is", StringComparison.OrdinalIgnoreCase);
        if (isIdx < 0)
        {
            isIdx = lower.LastIndexOf(" Is", StringComparison.OrdinalIgnoreCase);
        }

        if (isIdx < 0)
        {
            return null;
        }

        string nameSegment = lower[..isIdx].Trim();

        if (nameSegment.StartsWith("An ", StringComparison.OrdinalIgnoreCase))
        {
            nameSegment = nameSegment[3..].Trim();
        }
        else if (nameSegment.StartsWith("A ", StringComparison.OrdinalIgnoreCase))
        {
            nameSegment = nameSegment[2..].Trim();
        }

        return nameSegment.Length > 0 ? nameSegment : null;
    }

    static string? TryMatchVariantTemplate(string line)
    {
        string lower = line.TrimEnd();
        if (!lower.EndsWith(':'))
        {
            return null;
        }

        lower = lower[..^1].Trim();

        if (!lower.EndsWith("either", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        lower = lower[..^"either".Length].Trim();

        int isIdx = lower.LastIndexOf(" is", StringComparison.OrdinalIgnoreCase);
        if (isIdx < 0)
        {
            return null;
        }

        string nameSegment = lower[..isIdx].Trim();

        return nameSegment.Length > 0 ? nameSegment : null;
    }

    static FunctionTemplateInfo? TryMatchFunctionTemplate(string text, SourceSpan span)
    {
        // Find the "To ..." line — may be followed by "gives" and "failing if" lines
        string[] lines = text.Split('\n');
        int toLineIdx = -1;
        for (int li = lines.Length - 1; li >= 0; li--)
        {
            string t = lines[li].TrimStart();
            if (t.StartsWith("To ", StringComparison.OrdinalIgnoreCase) && t.Contains('('))
            {
                toLineIdx = li;
                break;
            }
        }
        if (toLineIdx < 0)
        {
            return null;
        }

        // Join the To line and any continuation lines (gives, failing if) into one block
        string combined = string.Join(" ", lines[toLineIdx..].Select(l => l.Trim()));
        // Strip trailing colon or period
        if (combined.EndsWith(':') || combined.EndsWith('.'))
        {
            combined = combined[..^1].Trim();
        }

        if (!combined.StartsWith("To ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string body = combined[3..].Trim();
        if (body.Length == 0)
        {
            return null;
        }

        // Extract fail clauses: "failing if ..." or "or fails with "..." if ..."
        List<FailClause> failClauses = [];
        while (true)
        {
            int failIdx = body.IndexOf("failing if ", StringComparison.OrdinalIgnoreCase);
            int orFailIdx = body.IndexOf("or fails with ", StringComparison.OrdinalIgnoreCase);

            if (failIdx >= 0 && (orFailIdx < 0 || failIdx < orFailIdx))
            {
                string condition = body[(failIdx + "failing if ".Length)..].Trim();
                // Strip trailing comma for chained clauses
                if (condition.EndsWith(','))
                {
                    condition = condition[..^1].Trim();
                }

                failClauses.Add(new FailClause(null, condition));
                body = body[..failIdx].Trim();
                if (body.EndsWith(','))
                {
                    body = body[..^1].Trim();
                }
            }
            else if (orFailIdx >= 0)
            {
                string afterOrFails = body[(orFailIdx + "or fails with ".Length)..].Trim();
                string? reason = null;
                string condition;
                // Extract quoted reason: "reason" if condition
                if (afterOrFails.StartsWith('"'))
                {
                    int closeQuote = afterOrFails.IndexOf('"', 1);
                    if (closeQuote > 0)
                    {
                        reason = afterOrFails[1..closeQuote];
                        string rest = afterOrFails[(closeQuote + 1)..].Trim();
                        if (rest.StartsWith("if ", StringComparison.OrdinalIgnoreCase))
                        {
                            rest = rest[3..].Trim();
                        }

                        condition = rest;
                    }
                    else
                    {
                        condition = afterOrFails;
                    }
                }
                else
                {
                    condition = afterOrFails;
                }
                if (condition.EndsWith(','))
                {
                    condition = condition[..^1].Trim();
                }

                failClauses.Add(new FailClause(reason, condition));
                body = body[..orFailIdx].Trim();
                if (body.EndsWith(','))
                {
                    body = body[..^1].Trim();
                }
            }
            else
            {
                break;
            }
        }

        // Extract return type from "gives [a|the|the updated] Type" suffix
        string? returnType = null;
        int givesIdx = body.LastIndexOf(" gives ", StringComparison.OrdinalIgnoreCase);
        if (givesIdx >= 0)
        {
            string givesText = body[(givesIdx + " gives ".Length)..].Trim();
            // Strip leading articles: "a", "an", "the", "the updated"
            if (givesText.StartsWith("the updated ", StringComparison.OrdinalIgnoreCase))
            {
                givesText = givesText["the updated ".Length..].Trim();
            }
            else if (givesText.StartsWith("an ", StringComparison.OrdinalIgnoreCase))
            {
                givesText = givesText[3..].Trim();
            }
            else if (givesText.StartsWith("a ", StringComparison.OrdinalIgnoreCase))
            {
                givesText = givesText[2..].Trim();
            }
            else if (givesText.StartsWith("the ", StringComparison.OrdinalIgnoreCase))
            {
                givesText = givesText[4..].Trim();
            }

            returnType = givesText;
            body = body[..givesIdx].Trim();
        }

        // Extract function name (words before first paren) and parameters
        List<(string Name, string Type)> parameters = [];
        List<string> nameWords = [];
        bool seenParam = false;
        int i = 0;
        while (i < body.Length)
        {
            if (body[i] == '(')
            {
                seenParam = true;
                int close = body.IndexOf(')', i);
                if (close < 0)
                {
                    break;
                }

                string paramText = body[(i + 1)..close].Trim();
                int colonIdx = paramText.IndexOf(':');
                if (colonIdx >= 0)
                {
                    string pName = paramText[..colonIdx].Trim();
                    string pType = paramText[(colonIdx + 1)..].Trim();
                    if (pName.Length > 0 && pType.Length > 0)
                    {
                        parameters.Add((ToFieldName(pName), pType));
                    }
                }
                i = close + 1;
            }
            else if (char.IsWhiteSpace(body[i]))
            {
                i++;
            }
            else
            {
                int wordEnd = i;
                while (wordEnd < body.Length && body[wordEnd] != '(' && !char.IsWhiteSpace(body[wordEnd]))
                {
                    wordEnd++;
                }

                if (!seenParam)
                {
                    nameWords.Add(body[i..wordEnd].ToLowerInvariant());
                }

                i = wordEnd;
            }
        }

        if (nameWords.Count == 0)
        {
            return null;
        }

        string funcName = string.Join("-", nameWords);
        return new FunctionTemplateInfo(funcName, parameters, returnType, span)
        {
            FailClauses = failClauses
        };
    }

    static bool IsFunctionTemplateMatch(string line)
    {
        string trimmed = line.TrimEnd();
        return trimmed.EndsWith(':')
            && trimmed.TrimStart().StartsWith("To ", StringComparison.OrdinalIgnoreCase);
    }

    static bool IsTemplateMatch(string line)
    {
        return TryMatchRecordTemplate(line) is not null
            || TryMatchVariantTemplate(line) is not null;
    }

    void ParseProseOrTemplate(List<DocumentMember> members)
    {
        int startOffset = LineOffset(m_lineIndex);
        List<string> proseLines = [];
        string? templateLine = null;
        int templateLineIdx = -1;

        while (m_lineIndex < m_lines.Length)
        {
            string line = m_lines[m_lineIndex];
            string trimmed = line.Trim();

            if (trimmed.Length == 0)
            {
                break;
            }

            if (trimmed.StartsWith("Chapter:", StringComparison.Ordinal)
                || trimmed.StartsWith("Section:", StringComparison.Ordinal))
            {
                break;
            }

            int indent = MeasureIndent(line);
            if (IsNotationIndent(indent))
            {
                break;
            }

            if (trimmed.StartsWith('-')
                && templateLine is not null)
            {
                break;
            }

            if (IsTemplateMatch(trimmed))
            {
                templateLine = trimmed;
                templateLineIdx = m_lineIndex;
                proseLines.Add(trimmed);
                m_lineIndex++;
                break;
            }

            proseLines.Add(trimmed);
            m_lineIndex++;
        }

        int endOffset = m_lineIndex < m_lines.Length
            ? LineOffset(m_lineIndex) : m_source.Content.Length;
        string text = string.Join("\n", proseLines);
        SourceSpan proseSpan = MakeSpan(startOffset, endOffset);

        // Detect transition markers
        ProseTransitionKind transition = ProseTransitionKind.None;
        if (text.TrimEnd().EndsWith("We say:", StringComparison.OrdinalIgnoreCase))
        {
            transition = ProseTransitionKind.WeSay;
        }
        else if (text.TrimEnd().EndsWith("This is written:", StringComparison.OrdinalIgnoreCase))
        {
            transition = ProseTransitionKind.ThisIsWritten;
        }

        // Check for function template — may span multiple lines (To..., gives..., failing if...)
        FunctionTemplateInfo? funcTemplate = null;
        if (proseLines.Any(l => l.TrimStart().StartsWith("To ", StringComparison.OrdinalIgnoreCase) && l.Contains('(')))
        {
            funcTemplate = TryMatchFunctionTemplate(text, proseSpan);
        }

        // Check for Claim: or Proof: template
        ProseClaimInfo? claimTemplate = null;
        ProseProofInfo? proofTemplate = null;
        string trimmedText = text.Trim();
        if (trimmedText.StartsWith("Claim:", StringComparison.OrdinalIgnoreCase))
        {
            claimTemplate = new ProseClaimInfo(trimmedText["Claim:".Length..].Trim().TrimEnd('.'));
        }
        else if (trimmedText.StartsWith("Proof:", StringComparison.OrdinalIgnoreCase))
        {
            proofTemplate = new ProseProofInfo(trimmedText["Proof:".Length..].Trim().TrimEnd('.'));
        }

        // Check for procedure steps (First, / Then, / Finally,)
        ProseProcedure? procedure = TryParseProcedure(text);

        // Check for quantified statements (for every, there exists, no)
        List<ProseQuantifiedStatement> quantified = ExtractQuantifiedStatements(text);

        // Extract inline references from prose text
        List<InlineCodeRef> codeRefs = ExtractCodeRefs(text);
        List<InlineTypeRef> typeRefs = ExtractTypeRefs(text);

        members.Add(new ProseBlockNode(text, proseSpan)
        {
            Transition = transition,
            FunctionTemplate = funcTemplate,
            ClaimTemplate = claimTemplate,
            ProofTemplate = proofTemplate,
            Procedure = procedure,
            QuantifiedStatements = quantified,
            CodeRefs = codeRefs,
            TypeRefs = typeRefs
        });

        if (templateLine is null)
        {
            return;
        }

        SourceSpan templateSpan = MakeLineSpan(templateLineIdx);

        string? recordName = TryMatchRecordTemplate(templateLine);
        if (recordName is not null)
        {
            string typeName = ToPascalCase(recordName);
            TypeDefinitionNode? typeDef =
                TryParseRecordFromBullets(typeName, templateSpan);
            if (typeDef is not null)
            {
                members.Add(new NotationBlockNode(
                    [], [typeDef], typeDef.Span));
            }

            return;
        }

        string? variantName = TryMatchVariantTemplate(templateLine);
        if (variantName is not null)
        {
            string typeName = ToPascalCase(variantName);
            TypeDefinitionNode? typeDef =
                TryParseVariantFromBullets(typeName, templateSpan);
            if (typeDef is not null)
            {
                members.Add(new NotationBlockNode(
                    [], [typeDef], typeDef.Span));
            }
        }
    }

    TypeDefinitionNode? TryParseRecordFromBullets(
        string typeName, SourceSpan headerSpan)
    {
        SkipBlankLines();
        int startOffset = m_lineIndex < m_lines.Length
            ? LineOffset(m_lineIndex) : headerSpan.End.Offset;

        List<RecordTypeFieldNode> fields = [];
        while (m_lineIndex < m_lines.Length)
        {
            string line = m_lines[m_lineIndex];
            string trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                break;
            }

            if (!trimmed.StartsWith('-'))
            {
                break;
            }

            SourceSpan lineSpan = MakeLineSpan(m_lineIndex);
            string bullet = trimmed[1..].Trim();
            RecordTypeFieldNode? field = ParseRecordBullet(bullet, lineSpan);
            if (field is not null)
            {
                fields.Add(field);
            }

            m_lineIndex++;
        }

        if (fields.Count == 0)
        {
            return null;
        }

        List<ProseConstraint> constraints = ParseConstraintLines();

        int endOffset = m_lineIndex < m_lines.Length
            ? LineOffset(m_lineIndex) : m_source.Content.Length;
        SourceSpan bodySpan = MakeSpan(startOffset, endOffset);
        RecordTypeBody body = new(fields, bodySpan) { Constraints = constraints };

        SourceSpan fullSpan = headerSpan.Through(bodySpan);
        Token nameToken = MakeSyntheticToken(
            TokenKind.TypeIdentifier, typeName, headerSpan);
        return new TypeDefinitionNode(nameToken, [], body, fullSpan);
    }

    TypeDefinitionNode? TryParseVariantFromBullets(
        string typeName, SourceSpan headerSpan)
    {
        SkipBlankLines();
        int startOffset = m_lineIndex < m_lines.Length
            ? LineOffset(m_lineIndex) : headerSpan.End.Offset;

        List<VariantConstructorNode> ctors = [];
        while (m_lineIndex < m_lines.Length)
        {
            string line = m_lines[m_lineIndex];
            string trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                break;
            }

            if (!trimmed.StartsWith('-'))
            {
                break;
            }

            SourceSpan lineSpan = MakeLineSpan(m_lineIndex);
            string bullet = trimmed[1..].Trim();
            VariantConstructorNode? ctor =
                ParseVariantBullet(bullet, lineSpan);
            if (ctor is not null)
            {
                ctors.Add(ctor);
            }

            m_lineIndex++;
        }

        if (ctors.Count == 0)
        {
            return null;
        }

        List<ProseConstraint> constraints = ParseConstraintLines();

        int endOffset = m_lineIndex < m_lines.Length
            ? LineOffset(m_lineIndex) : m_source.Content.Length;
        SourceSpan bodySpan = MakeSpan(startOffset, endOffset);
        VariantTypeBody body = new(ctors, bodySpan) { Constraints = constraints };

        SourceSpan fullSpan = headerSpan.Through(bodySpan);
        Token nameToken = MakeSyntheticToken(
            TokenKind.TypeIdentifier, typeName, headerSpan);
        return new TypeDefinitionNode(nameToken, [], body, fullSpan);
    }

    RecordTypeFieldNode? ParseRecordBullet(string bullet, SourceSpan span)
    {
        int colonIdx = bullet.IndexOf(':');
        if (colonIdx < 0)
        {
            return null;
        }

        string fieldName = ToFieldName(bullet[..colonIdx].Trim());
        string fieldType = bullet[(colonIdx + 1)..].Trim();
        if (fieldName.Length == 0 || fieldType.Length == 0)
        {
            return null;
        }

        Token nameToken = MakeSyntheticToken(
            TokenKind.Identifier, fieldName, span);
        TypeNode typeNode = ParseTypeFromText(fieldType, span);
        return new RecordTypeFieldNode(nameToken, typeNode, span);
    }

    VariantConstructorNode? ParseVariantBullet(string bullet, SourceSpan span)
    {
        int parenIdx = bullet.IndexOf('(');
        string ctorName;
        List<VariantFieldNode> fields = [];

        if (parenIdx >= 0)
        {
            ctorName = ToPascalCase(bullet[..parenIdx].Trim());
            int closeIdx = bullet.LastIndexOf(')');
            if (closeIdx > parenIdx)
            {
                string fieldList = bullet[(parenIdx + 1)..closeIdx];
                foreach (string part in fieldList.Split(','))
                {
                    string trimPart = part.Trim();
                    if (trimPart.Length == 0)
                    {
                        continue;
                    }

                    int colonIdx = trimPart.IndexOf(':');
                    if (colonIdx >= 0)
                    {
                        string fName = ToFieldName(trimPart[..colonIdx].Trim());
                        string fType = trimPart[(colonIdx + 1)..].Trim();
                        Token fToken = MakeSyntheticToken(
                            TokenKind.Identifier, fName, span);
                        TypeNode typeNode = ParseTypeFromText(fType, span);
                        fields.Add(new VariantFieldNode(fToken, typeNode, span));
                    }
                    else
                    {
                        TypeNode typeNode = ParseTypeFromText(trimPart, span);
                        fields.Add(new VariantFieldNode(null, typeNode, span));
                    }
                }
            }
        }
        else
        {
            ctorName = ToPascalCase(bullet.Trim());
        }

        if (ctorName.Length == 0)
        {
            return null;
        }

        Token ctorToken = MakeSyntheticToken(
            TokenKind.TypeIdentifier, ctorName, span);
        return new VariantConstructorNode(ctorToken, fields, span);
    }

    TypeNode ParseTypeFromText(string typeText, SourceSpan span)
    {
        SourceText src = new(m_source.FileName, typeText);
        DiagnosticBag diag = new();
        Lexer lexer = new(src, diag);
        IReadOnlyList<Token> tokens = lexer.TokenizeAll();
        Parser parser = new(tokens, diag);
        TypeNode? result = parser.TryParseType();
        if (result is not null)
        {
            return result;
        }

        Token synth = MakeSyntheticToken(
            TokenKind.TypeIdentifier, typeText.Trim(), span);
        return new NamedTypeNode(synth);
    }

    static Token MakeSyntheticToken(
        TokenKind kind, string text, SourceSpan span)
    {
        return new Token(kind, text, span);
    }

    static ProseProcedure? TryParseProcedure(string text)
    {
        string[] lines = text.Split('\n');
        List<ProcedureStep> steps = [];

        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            string? marker = null;
            string body;
            if (line.StartsWith("First,", StringComparison.OrdinalIgnoreCase))
            {
                marker = "first";
                body = line["First,".Length..].Trim();
            }
            else if (line.StartsWith("Then,", StringComparison.OrdinalIgnoreCase))
            {
                marker = "then";
                body = line["Then,".Length..].Trim();
            }
            else if (line.StartsWith("Finally,", StringComparison.OrdinalIgnoreCase))
            {
                marker = "finally";
                body = line["Finally,".Length..].Trim();
            }
            else
            {
                continue;
            }

            body = body.TrimEnd('.');

            ProcedureStep? step = ParseSingleStep(marker, body);
            if (step is not null)
            {
                steps.Add(step);
            }
        }

        return steps.Count > 0 ? new ProseProcedure(steps) : null;
    }

    static ProcedureStep? ParseSingleStep(string marker, string body)
    {
        if (body.StartsWith("let ", StringComparison.OrdinalIgnoreCase))
        {
            string rest = body[4..].Trim();
            int beIdx = rest.IndexOf(" be ", StringComparison.OrdinalIgnoreCase);
            if (beIdx >= 0)
            {
                string binding = rest[..beIdx].Trim();
                string value = rest[(beIdx + 4)..].Trim();
                return new ProcedureStep(ProcedureStepKind.Let, marker, body)
                {
                    Binding = binding,
                    Value = value
                };
            }
        }

        if (body.StartsWith("set ", StringComparison.OrdinalIgnoreCase))
        {
            string rest = body[4..].Trim();
            int toIdx = rest.IndexOf(" to ", StringComparison.OrdinalIgnoreCase);
            if (toIdx >= 0)
            {
                string binding = rest[..toIdx].Trim();
                string value = rest[(toIdx + 4)..].Trim();
                return new ProcedureStep(ProcedureStepKind.Set, marker, body)
                {
                    Binding = binding,
                    Value = value
                };
            }
        }

        if (body.StartsWith("return ", StringComparison.OrdinalIgnoreCase))
        {
            string value = body[7..].Trim();
            return new ProcedureStep(ProcedureStepKind.Return, marker, body)
            {
                Value = value
            };
        }

        if (body.StartsWith("fail with ", StringComparison.OrdinalIgnoreCase))
        {
            string reason = body[10..].Trim().Trim('"');
            return new ProcedureStep(ProcedureStepKind.FailWith, marker, body)
            {
                Value = reason
            };
        }

        if (body.StartsWith("if ", StringComparison.OrdinalIgnoreCase))
        {
            string rest = body[3..].Trim();
            int otherwiseIdx = rest.LastIndexOf(" otherwise ", StringComparison.OrdinalIgnoreCase);
            if (otherwiseIdx >= 0)
            {
                string condAndThen = rest[..otherwiseIdx].Trim();
                string otherwise = rest[(otherwiseIdx + 11)..].Trim();
                // Split condition from then-branch at the comma
                int commaIdx = condAndThen.IndexOf(',');
                if (commaIdx >= 0)
                {
                    string condition = condAndThen[..commaIdx].Trim();
                    string thenBranch = condAndThen[(commaIdx + 1)..].Trim();
                    return new ProcedureStep(ProcedureStepKind.If, marker, body)
                    {
                        Condition = condition,
                        Value = thenBranch,
                        Otherwise = otherwise
                    };
                }
            }
        }

        return null;
    }

    static List<ProseQuantifiedStatement> ExtractQuantifiedStatements(string text)
    {
        List<ProseQuantifiedStatement> statements = [];
        foreach (string rawLine in text.Split('\n'))
        {
            string line = rawLine.Trim().TrimEnd('.');
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("for every ", StringComparison.OrdinalIgnoreCase))
            {
                // "for every X in Y, CLAIM"
                string rest = line["for every ".Length..];
                int inIdx = rest.IndexOf(" in ", StringComparison.OrdinalIgnoreCase);
                if (inIdx >= 0)
                {
                    string bound = rest[..inIdx].Trim();
                    string afterIn = rest[(inIdx + 4)..].Trim();
                    int commaIdx = afterIn.IndexOf(',');
                    if (commaIdx >= 0)
                    {
                        string collection = afterIn[..commaIdx].Trim();
                        string claim = afterIn[(commaIdx + 1)..].Trim();
                        statements.Add(new ProseQuantifiedStatement(
                            QuantifierKind.ForEvery, bound, collection, null, claim));
                    }
                }
            }
            else if (line.StartsWith("there exists ", StringComparison.OrdinalIgnoreCase))
            {
                // "there exists [qualifier] X in Y such that CLAIM"
                string rest = line["there exists ".Length..];
                string? qualifier = null;
                if (rest.StartsWith("exactly one ", StringComparison.OrdinalIgnoreCase))
                {
                    qualifier = "exactly one";
                    rest = rest["exactly one ".Length..];
                }
                else if (rest.StartsWith("at least one ", StringComparison.OrdinalIgnoreCase))
                {
                    qualifier = "at least one";
                    rest = rest["at least one ".Length..];
                }
                int inIdx = rest.IndexOf(" in ", StringComparison.OrdinalIgnoreCase);
                if (inIdx >= 0)
                {
                    string bound = rest[..inIdx].Trim();
                    string afterIn = rest[(inIdx + 4)..].Trim();
                    int suchIdx = afterIn.IndexOf(" such that ", StringComparison.OrdinalIgnoreCase);
                    if (suchIdx >= 0)
                    {
                        string collection = afterIn[..suchIdx].Trim();
                        string claim = afterIn[(suchIdx + 11)..].Trim();
                        statements.Add(new ProseQuantifiedStatement(
                            QuantifierKind.ThereExists, bound, collection, qualifier, claim));
                    }
                }
            }
            else if (line.StartsWith("no ", StringComparison.OrdinalIgnoreCase))
            {
                // "no X in Y satisfies/has/equals CLAIM"
                string rest = line[3..];
                int inIdx = rest.IndexOf(" in ", StringComparison.OrdinalIgnoreCase);
                if (inIdx >= 0)
                {
                    string bound = rest[..inIdx].Trim();
                    string afterIn = rest[(inIdx + 4)..].Trim();
                    string? claim = null;
                    foreach (string verb in new[] { " satisfies ", " has ", " equals " })
                    {
                        int verbIdx = afterIn.IndexOf(verb, StringComparison.OrdinalIgnoreCase);
                        if (verbIdx >= 0)
                        {
                            string collection = afterIn[..verbIdx].Trim();
                            claim = afterIn[(verbIdx + verb.Length)..].Trim();
                            statements.Add(new ProseQuantifiedStatement(
                                QuantifierKind.No, bound, collection, null, claim));
                            break;
                        }
                    }
                }
            }
        }
        return statements;
    }

    List<ProseConstraint> ParseConstraintLines()
    {
        List<ProseConstraint> constraints = [];
        SkipBlankLines();
        while (m_lineIndex < m_lines.Length)
        {
            string line = m_lines[m_lineIndex];
            string trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                break;
            }

            string? keyword = null;
            string? text = null;
            if (trimmed.StartsWith("such that ", StringComparison.OrdinalIgnoreCase))
            {
                keyword = "such that";
                text = trimmed["such that ".Length..].TrimEnd('.');
            }
            else if (trimmed.StartsWith("where ", StringComparison.OrdinalIgnoreCase))
            {
                keyword = "where";
                text = trimmed["where ".Length..].TrimEnd('.');
            }
            else if (trimmed.StartsWith("provided that ", StringComparison.OrdinalIgnoreCase))
            {
                keyword = "provided that";
                text = trimmed["provided that ".Length..].TrimEnd('.');
            }

            if (keyword is null)
            {
                break;
            }

            constraints.Add(new ProseConstraint(keyword, text!));
            m_lineIndex++;
        }
        return constraints;
    }

    static string ToPascalCase(string text)
    {
        if (text.Length == 0)
        {
            return text;
        }

        string[] words = text.Split(new char[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(words.Select(w =>
            char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));
    }

    static string ToFieldName(string text)
    {
        string[] words = text.Split(new char[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return text;
        }

        string first = words[0].ToLowerInvariant();
        string rest = string.Concat(words.Skip(1).Select(w =>
            char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));
        return first + rest;
    }

    static List<InlineCodeRef> ExtractCodeRefs(string text)
    {
        List<InlineCodeRef> refs = [];
        int i = 0;
        while (i < text.Length)
        {
            if (text[i] == '`')
            {
                int end = text.IndexOf('`', i + 1);
                if (end > i + 1)
                {
                    string code = text[(i + 1)..end];
                    if (code.Length > 0 && !code.Contains('`'))
                    {
                        refs.Add(new InlineCodeRef(code, i, end + 1));
                    }

                    i = end + 1;
                }
                else
                {
                    i++;
                }
            }
            else
            {
                i++;
            }
        }
        return refs;
    }

    static List<InlineTypeRef> ExtractTypeRefs(string text)
    {
        List<InlineTypeRef> refs = [];
        int i = 0;
        while (i < text.Length)
        {
            // Look for standalone PascalCase words (not inside backticks)
            if (char.IsUpper(text[i]) && (i == 0 || !char.IsLetterOrDigit(text[i - 1])))
            {
                int end = i + 1;
                while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '-'))
                {
                    end++;
                }

                string word = text[i..end];
                // Must be PascalCase (starts upper, has at least one lower)
                if (word.Length >= 2 && word.Any(char.IsLower)
                    && !IsCommonEnglishWord(word))
                {
                    refs.Add(new InlineTypeRef(word, i, end));
                }
                i = end;
            }
            else
            {
                i++;
            }
        }
        return refs;
    }

    static bool IsCommonEnglishWord(string word)
    {
        return word is "The" or "This" or "That" or "These" or "Those"
            or "An" or "And" or "Are" or "But" or "Can" or "Did"
            or "For" or "From" or "Has" or "Have" or "How" or "Its"
            or "Let" or "May" or "Not" or "Our" or "She" or "Was"
            or "We" or "When" or "Who" or "Will" or "With" or "You"
            or "Also" or "Each" or "Every" or "Given" or "Here"
            or "Into" or "Just" or "More" or "Must" or "Note"
            or "Only" or "Over" or "Some" or "Such" or "Than"
            or "Then" or "They" or "Very" or "What" or "Your"
            or "After" or "Before" or "Below" or "First" or "Never"
            or "Section" or "Chapter" or "Claim" or "Proof" or "True" or "False";
    }
}
