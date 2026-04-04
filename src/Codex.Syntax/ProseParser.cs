using Codex.Core;

namespace Codex.Syntax;

public sealed partial class ProseParser(SourceText source, DiagnosticBag diagnostics)
{
    readonly SourceText m_source = source;
    readonly DiagnosticBag m_diagnostics = diagnostics;
    readonly string[] m_lines = source.Content.Split('\n');
    int m_lineIndex;

    public static bool IsProseDocument(string content)
    {
        foreach (string line in content.Split('\n'))
        {
            string trimmed = line.TrimStart();
            if (trimmed.Length == 0)
                continue;
            return trimmed.StartsWith("Chapter:", StringComparison.Ordinal);
        }
        return false;
    }

    public DocumentNode ParseDocument()
    {
        SourceSpan docSpan = MakeSpan(0, m_source.Content.Length);
        List<ChapterNode> chapters = [];

        SkipBlankLines();

        while (m_lineIndex < m_lines.Length)
        {
            string trimmed = m_lines[m_lineIndex].TrimStart();
            if (trimmed.StartsWith("Chapter:", StringComparison.Ordinal))
            {
                chapters.Add(ParseChapter());
            }
            else
            {
                SkipBlankLines();
                if (m_lineIndex < m_lines.Length)
                {
                    m_diagnostics.Warning("CDX1100",
                        "Expected a Chapter: header in a prose-mode document",
                        MakeLineSpan(m_lineIndex));
                    m_lineIndex++;
                }
            }
        }

        ValidateProseNotationConsistency(chapters);

        List<DefinitionNode> allDefs = [];
        List<TypeDefinitionNode> allTypeDefs = [];
        List<ClaimNode> allClaims = [];
        List<ProofNode> allProofs = [];
        List<CitesNode> allCitations = [];
        List<ExportNode> allExports = [];
        List<EffectDefinitionNode> allEffectDefs = [];
        foreach (ChapterNode chapter in chapters)
        {
            CollectDefinitions(chapter.Members, allDefs, allTypeDefs, allClaims, allProofs,
                allCitations, allExports, allEffectDefs);
        }

        return new DocumentNode(allDefs, allTypeDefs, allClaims,
            allProofs, chapters, docSpan)
        {
            Citations = allCitations,
            Exports = allExports,
            EffectDefinitions = allEffectDefs
        };
    }

    ChapterNode ParseChapter()
    {
        int startOffset = LineOffset(m_lineIndex);
        string headerLine = m_lines[m_lineIndex].TrimStart();
        string title = headerLine["Chapter:".Length..].Trim();
        m_lineIndex++;

        List<DocumentMember> members = [];
        SkipBlankLines();

        while (m_lineIndex < m_lines.Length)
        {
            string trimmed = CurrentTrimmed();
            if (trimmed.Length == 0)
            {
                SkipBlankLines();
                continue;
            }

            if (trimmed.StartsWith("Chapter:", StringComparison.Ordinal))
                break;

            if (trimmed.StartsWith("Section:", StringComparison.Ordinal))
            {
                members.Add(ParseSection());
                continue;
            }

            int indent = MeasureIndent(m_lines[m_lineIndex]);

            if (indent >= 2 && LooksLikeNotation(trimmed))
            {
                members.Add(ParseNotationBlock());
                continue;
            }

            ParseProseOrTemplate(members);
        }

        int endOffset = m_lineIndex < m_lines.Length
            ? LineOffset(m_lineIndex)
            : m_source.Content.Length;
        SourceSpan span = MakeSpan(startOffset, endOffset);
        return new ChapterNode(title, members, span);
    }

    SectionNode ParseSection()
    {
        int startOffset = LineOffset(m_lineIndex);
        string headerLine = m_lines[m_lineIndex].TrimStart();
        string title = headerLine["Section:".Length..].Trim();
        m_lineIndex++;

        List<DocumentMember> members = [];
        SkipBlankLines();

        while (m_lineIndex < m_lines.Length)
        {
            string trimmed = CurrentTrimmed();
            if (trimmed.Length == 0)
            {
                SkipBlankLines();
                continue;
            }

            if (trimmed.StartsWith("Chapter:", StringComparison.Ordinal) ||
                trimmed.StartsWith("Section:", StringComparison.Ordinal))
                break;

            int indent = MeasureIndent(m_lines[m_lineIndex]);

            if (indent >= 2 && LooksLikeNotation(trimmed))
            {
                members.Add(ParseNotationBlock());
                continue;
            }

            ParseProseOrTemplate(members);
        }

        int endOffset = m_lineIndex < m_lines.Length
            ? LineOffset(m_lineIndex)
            : m_source.Content.Length;
        SourceSpan span = MakeSpan(startOffset, endOffset);
        return new SectionNode(title, members, span);
    }

    ProseBlockNode ParseProseBlock()
    {
        int startOffset = LineOffset(m_lineIndex);
        List<string> proseLines = [];

        while (m_lineIndex < m_lines.Length)
        {
            string line = m_lines[m_lineIndex];
            string trimmed = line.Trim();

            if (trimmed.Length == 0)
                break;

            if (trimmed.StartsWith("Chapter:", StringComparison.Ordinal) ||
                trimmed.StartsWith("Section:", StringComparison.Ordinal))
                break;

            int indent = MeasureIndent(line);

            if (indent >= 2 && LooksLikeNotation(trimmed))
                break;

            proseLines.Add(trimmed);
            m_lineIndex++;
        }

        int endOffset = m_lineIndex < m_lines.Length
            ? LineOffset(m_lineIndex)
            : m_source.Content.Length;
        string text = string.Join("\n", proseLines);
        SourceSpan span = MakeSpan(startOffset, endOffset);
        return new ProseBlockNode(text, span);
    }

    NotationBlockNode ParseNotationBlock()
    {
        int startOffset = LineOffset(m_lineIndex);

        List<string> notationLines = [];
        int baseIndent = MeasureIndent(m_lines[m_lineIndex]);

        while (m_lineIndex < m_lines.Length)
        {
            string line = m_lines[m_lineIndex];
            string trimmed = line.Trim();

            if (trimmed.Length == 0)
            {
                int peekIdx = m_lineIndex + 1;
                while (peekIdx < m_lines.Length && m_lines[peekIdx].Trim().Length == 0)
                    peekIdx++;

                if (peekIdx < m_lines.Length && MeasureIndent(m_lines[peekIdx]) >= baseIndent)
                {
                    notationLines.Add("");
                    m_lineIndex++;
                    continue;
                }
                break;
            }

            int indent = MeasureIndent(line);
            if (indent < baseIndent)
                break;

            if (trimmed.StartsWith("Chapter:", StringComparison.Ordinal) ||
                trimmed.StartsWith("Section:", StringComparison.Ordinal))
                break;

            string dedented = indent >= baseIndent ? line[baseIndent..].TrimEnd('\r') : trimmed;
            notationLines.Add(dedented);
            m_lineIndex++;
        }

        string notationSource = string.Join("\n", notationLines);
        SourceText notationText = new(m_source.FileName, notationSource);
        DiagnosticBag notationDiag = new();
        Lexer lexer = new(notationText, notationDiag);
        IReadOnlyList<Token> tokens = lexer.TokenizeAll();
        Parser parser = new(tokens, notationDiag);
        DocumentNode notationDoc = parser.ParseDocument();

        foreach (Diagnostic diag in notationDiag.ToImmutable())
        {
            m_diagnostics.Add(diag);
        }

        int endOffset = m_lineIndex < m_lines.Length
            ? LineOffset(m_lineIndex)
            : m_source.Content.Length;
        SourceSpan span = MakeSpan(startOffset, endOffset);
        return new NotationBlockNode(notationDoc.Definitions, notationDoc.TypeDefinitions, span)
        {
            Claims = notationDoc.Claims,
            Proofs = notationDoc.Proofs,
            Citations = notationDoc.Citations,
            Exports = notationDoc.Exports,
            EffectDefinitions = notationDoc.EffectDefinitions
        };
    }

    static bool LooksLikeNotation(string trimmed)
    {
        if (trimmed.Length == 0) return false;
        if (trimmed[0] == '|') return true;
        // Function template lines are prose, not notation
        if (trimmed.StartsWith("To ", StringComparison.OrdinalIgnoreCase) && trimmed.EndsWith(':'))
            return false;
        if (char.IsLetter(trimmed[0]))
        {
            if (trimmed.Contains(" : ")) return true;
            if (trimmed.Contains(" = ")) return true;
            if (trimmed.EndsWith(" =") || trimmed.EndsWith('=')) return true;
            if (trimmed.Contains('(')) return true;
        }

        return false;
    }

    static void CollectDefinitions(IReadOnlyList<DocumentMember> members,
        List<DefinitionNode> defs, List<TypeDefinitionNode> typeDefs,
        List<ClaimNode> claims, List<ProofNode> proofs,
        List<CitesNode> citations, List<ExportNode> exports,
        List<EffectDefinitionNode> effectDefs)
    {
        foreach (DocumentMember member in members)
        {
            if (member is NotationBlockNode notation)
            {
                defs.AddRange(notation.Definitions);
                typeDefs.AddRange(notation.TypeDefinitions);
                claims.AddRange(notation.Claims);
                proofs.AddRange(notation.Proofs);
                citations.AddRange(notation.Citations);
                exports.AddRange(notation.Exports);
                effectDefs.AddRange(notation.EffectDefinitions);
            }
            else if (member is SectionNode section)
            {
                CollectDefinitions(section.Members, defs, typeDefs, claims, proofs,
                    citations, exports, effectDefs);
            }
        }
    }

    string CurrentTrimmed()
    {
        return m_lineIndex < m_lines.Length ? m_lines[m_lineIndex].Trim() : "";
    }

    void SkipBlankLines()
    {
        while (m_lineIndex < m_lines.Length && m_lines[m_lineIndex].Trim().Length == 0)
        {
            m_lineIndex++;
        }
    }

    static int MeasureIndent(string line)
    {
        int count = 0;
        foreach (char c in line)
        {
            if (c == ' ') count++;
            else break;
        }
        return count;
    }

    int LineOffset(int lineIdx)
    {
        int offset = 0;
        for (int i = 0; i < lineIdx && i < m_lines.Length; i++)
        {
            offset += m_lines[i].Length + 1; // +1 for the \n
        }
        return offset;
    }

    SourceSpan MakeSpan(int startOffset, int endOffset)
    {
        SourcePosition start = OffsetToPosition(startOffset);
        SourcePosition end = OffsetToPosition(endOffset);
        return new SourceSpan(start, end, m_source.FileName);
    }

    SourcePosition OffsetToPosition(int offset)
    {
        int line = 1;
        int col = 1;
        for (int i = 0; i < offset && i < m_source.Content.Length; i++)
        {
            if (m_source.Content[i] == '\n')
            {
                line++;
                col = 1;
            }
            else
            {
                col++;
            }
        }
        return new SourcePosition(offset, line, col);
    }

    SourceSpan MakeLineSpan(int lineIdx)
    {
        int offset = LineOffset(lineIdx);
        int length = lineIdx < m_lines.Length ? m_lines[lineIdx].Length : 0;
        return MakeSpan(offset, offset + length);
    }
}
