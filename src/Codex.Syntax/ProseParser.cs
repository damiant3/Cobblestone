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
            {
                continue;
            }

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
                    m_diagnostics.Warning(CdxCodes.ProseMissingChapter,
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
        List<EffectDefinitionNode> allEffectDefs = [];
        foreach (ChapterNode chapter in chapters)
        {
            CollectDefinitions(chapter.Members, allDefs, allTypeDefs, allClaims, allProofs,
                allCitations, allEffectDefs, null);
        }

        // Scan for page marker (last non-blank line starting with "Page")
        PageMarker? pageMarker = null;
        for (int i = m_lines.Length - 1; i >= 0; i--)
        {
            string ln = m_lines[i].Trim();
            if (ln.Length == 0)
            {
                continue;
            }

            if (ln.StartsWith("Page ", StringComparison.Ordinal)
                && ln.Length > 5 && char.IsDigit(ln[5]))
            {
                pageMarker = ParsePageMarkerFromLine(ln, i);
            }
            break;
        }

        return new DocumentNode(allDefs, allTypeDefs, allClaims,
            allProofs, chapters, docSpan)
        {
            Citations = allCitations,
            EffectDefinitions = allEffectDefs,
            Page = pageMarker
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
            {
                break;
            }

            if (trimmed.StartsWith("Page ", StringComparison.Ordinal)
                && trimmed.Length > 5 && char.IsDigit(trimmed[5]))
            {
                // Page marker — skip it in the chapter body, capture in ParseDocument
                m_lineIndex++;
                continue;
            }

            if (trimmed.StartsWith("Section:", StringComparison.Ordinal))
            {
                members.Add(ParseSection());
                continue;
            }

            int indent = MeasureIndent(m_lines[m_lineIndex]);

            if (IsNotationIndent(indent))
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
            {
                break;
            }

            int indent = MeasureIndent(m_lines[m_lineIndex]);

            if (IsNotationIndent(indent))
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
            {
                break;
            }

            if (trimmed.StartsWith("Chapter:", StringComparison.Ordinal) ||
                trimmed.StartsWith("Section:", StringComparison.Ordinal))
            {
                break;
            }

            int indent = MeasureIndent(line);

            if (IsNotationIndent(indent))
            {
                break;
            }

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
                {
                    peekIdx++;
                }

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
            {
                break;
            }

            if (trimmed.StartsWith("Chapter:", StringComparison.Ordinal) ||
                trimmed.StartsWith("Section:", StringComparison.Ordinal))
            {
                break;
            }

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
            EffectDefinitions = notationDoc.EffectDefinitions
        };
    }

    static bool IsNotationIndent(int indent) => indent >= 2;

    static void CollectDefinitions(IReadOnlyList<DocumentMember> members,
        List<DefinitionNode> defs, List<TypeDefinitionNode> typeDefs,
        List<ClaimNode> claims, List<ProofNode> proofs,
        List<CitesNode> citations, List<EffectDefinitionNode> effectDefs,
        string? currentSection)
    {
        foreach (DocumentMember member in members)
        {
            if (member is NotationBlockNode notation)
            {
                foreach (DefinitionNode def in notation.Definitions)
                {
                    defs.Add(def with { Section = currentSection });
                }

                foreach (TypeDefinitionNode td in notation.TypeDefinitions)
                {
                    typeDefs.Add(td with { Section = currentSection });
                }

                claims.AddRange(notation.Claims);
                proofs.AddRange(notation.Proofs);
                citations.AddRange(notation.Citations);
                effectDefs.AddRange(notation.EffectDefinitions);
            }
            else if (member is SectionNode section)
            {
                CollectDefinitions(section.Members, defs, typeDefs, claims, proofs,
                    citations, effectDefs, section.Title);
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

    static PageMarker? ParsePageMarkerFromLine(string line, int lineIndex)
    {
        // "Page 1" or "Page 1 of 3"
        string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || parts[0] != "Page")
        {
            return null;
        }

        if (!int.TryParse(parts[1], out int pageNum))
        {
            return null;
        }

        int? total = null;
        if (parts.Length >= 4 && parts[2] == "of" && int.TryParse(parts[3], out int t))
        {
            total = t;
        }

        SourceSpan span = SourceSpan.Single(0, lineIndex + 1, 1, "<page>");
        return new PageMarker(pageNum, total, span);
    }

    static int MeasureIndent(string line)
    {
        int count = 0;
        foreach (char c in line)
        {
            if (c == ' ')
            {
                count++;
            }
            else
            {
                break;
            }
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
